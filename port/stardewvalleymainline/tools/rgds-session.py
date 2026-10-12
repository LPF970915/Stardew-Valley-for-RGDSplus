"""RGDS session input guard: firmware menu isolation and independent hold-to-exit.

Uses only Linux Python stdlib. Signals are restricted to recorded process births
and to the game carrying this launcher's private session token.
"""
import argparse
import glob
import os
from pathlib import Path
import select
import signal
import struct
import time

EVENT = struct.Struct('@llHHi')
HOLD_SECONDS = 1.5
DEVICES = {'ANBERNIC-rk3568-keys': {310, 312}, 'adc-keys': {158}}
MENU_NAMES = {'dmenu.bin', 'muos2.bin', 'muos3.bin'}


class Hold:
    def __init__(self):
        self.since = {}

    def event(self, source, code, value, now):
        key = (source, code)
        if value == 1:
            self.since.setdefault(key, now)
        elif value == 0:
            self.since.pop(key, None)

    def cancel(self, source):
        self.since = {key: stamp for key, stamp in self.since.items() if key[0] != source}

    def expired(self, now):
        return any(now - stamp >= HOLD_SECONDS for stamp in self.since.values())


def process(pid):
    try:
        fields = Path('/proc/%d/stat' % pid).read_text().rsplit(')', 1)[1].split()
        return None if fields[0] == 'Z' else (fields[19], fields[0])
    except (OSError, IndexError):
        return None


def alive(target):
    state = process(target[0])
    return state is not None and state[0] == target[1]


def game_identity(session, game_dir):
    try:
        pid = int(Path(str(session) + '.game.pid').read_text())
        proc = Path('/proc/%d' % pid)
        argv = (proc / 'cmdline').read_bytes().split(b'\0')
        environment = (proc / 'environ').read_bytes().split(b'\0')
        if argv[0].decode() != str(game_dir / 'dotnet/dotnet'):
            return None
        if argv[1] not in (b'StardewModdingAPI.dll', b'Stardew Valley.dll'):
            return None
        if ('SDV_SESSION_TOKEN=' + str(session)).encode() not in environment:
            return None
        if (proc / 'cwd').resolve() != (game_dir / 'gamedata').resolve():
            return None
        state = process(pid)
        return (pid, state[0]) if state else None
    except (OSError, ValueError, IndexError):
        return None


def terminate(target, grace=2.0):
    if not alive(target):
        return
    try:
        print('[session] TERM game pid=%d' % target[0], flush=True)
        os.kill(target[0], signal.SIGTERM)
        until = time.monotonic() + grace
        while alive(target) and time.monotonic() < until:
            time.sleep(.05)
        if alive(target):
            print('[session] KILL stalled game pid=%d' % target[0], flush=True)
            os.kill(target[0], signal.SIGKILL)
    except ProcessLookupError:
        pass


def pause_menus(paused):
    for path in Path('/proc').iterdir():
        if not path.name.isdigit():
            continue
        try:
            pid = int(path.name)
            if (path / 'comm').read_text().strip() not in MENU_NAMES:
                continue
            exe = str((path / 'exe').resolve())
            if not exe.startswith(('/mnt/vendor/bin/', '/mnt/mmc/')):
                continue
            state = process(pid)
            if state is None or state[1] in ('T', 't') or pid in paused:
                continue
            paused[pid] = state[0]
            os.kill(pid, signal.SIGSTOP)
            print('[session] paused firmware menu pid=%d' % pid, flush=True)
        except (OSError, ValueError):
            continue


def resume_menus(paused):
    for pid, birth in paused.items():
        if alive((pid, birth)):
            try:
                os.kill(pid, signal.SIGCONT)
                print('[session] resumed firmware menu pid=%d' % pid, flush=True)
            except ProcessLookupError:
                pass


def run(session, owner, game_dir):
    import fcntl
    state = process(owner)
    if state is None:
        raise RuntimeError('launcher is not alive')
    owner_target = (owner, state[0])
    devices, paused = {}, {}
    hold = Hold()
    target = None
    next_scan = 0
    requested = False
    stopping = False

    def stop(signum, frame):
        nonlocal stopping
        stopping = True

    for sig in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP):
        signal.signal(sig, stop)

    def close_device(fd):
        hold.cancel(fd)
        os.close(fd)
        devices.pop(fd, None)

    try:
        while not stopping and alive(owner_target):
            now = time.monotonic()
            if target is None:
                target = game_identity(session, game_dir)
            elif not alive(target):
                # The launcher can return to title in a fresh child. Keep the
                # menu and exit-key guard owned by the launcher until it exits.
                target = game_identity(session, game_dir)
            if now >= next_scan:
                next_scan = now + .5
                pause_menus(paused)
                paths = {value[0] for value in devices.values()}
                for namefile in glob.glob('/sys/class/input/event*/device/name'):
                    name = Path(namefile).read_text().strip()
                    path = '/dev/input/' + Path(namefile).parents[1].name
                    if name not in DEVICES or path in paths:
                        continue
                    fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK)
                    blocked = set()
                    bits = bytearray(96)
                    fcntl.ioctl(fd, 0x80604518, bits)
                    blocked.update(code for code in DEVICES[name] if bits[code // 8] & (1 << (code % 8)))
                    devices[fd] = (path, DEVICES[name], blocked)
                    print('[session] exit key device=%s name=%s' % (path, name), flush=True)
                if devices:
                    Path(str(session) + '.ready').touch()
            ready, _, _ = select.select(list(devices), [], [], .05)
            for fd in ready:
                try:
                    data = os.read(fd, EVENT.size * 64)
                except OSError:
                    close_device(fd)
                    continue
                if not data:
                    close_device(fd)
                    continue
                _, codes, blocked = devices[fd]
                for _, _, kind, code, value in EVENT.iter_unpack(data):
                    if kind == 0 and code == 3:
                        hold.cancel(fd)
                        blocked.update(codes)
                    elif kind == 1 and code in codes:
                        if code in blocked:
                            if value == 0:
                                blocked.remove(code)
                            continue
                        hold.event(fd, code, value, time.monotonic())
            if not requested and hold.expired(time.monotonic()):
                requested = True
                Path(str(session) + '.exit-requested').touch()
                print('[session] long press 1.5s: exit requested', flush=True)
            if requested and target:
                terminate(target)
                break
    finally:
        # This also handles an unexpectedly lost launcher without leaving its
        # game alive on top of the restored system menu.
        if target is None:
            target = game_identity(session, game_dir)
        if target and alive(target):
            terminate(target)
        for fd in list(devices):
            close_device(fd)
        resume_menus(paused)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--session', type=Path, required=True)
    parser.add_argument('--owner', type=int, required=True)
    parser.add_argument('--game-dir', type=Path, required=True)
    args = parser.parse_args()
    run(args.session, args.owner, args.game_dir.resolve())


if __name__ == '__main__':
    main()
