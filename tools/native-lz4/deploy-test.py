"""Test on ARM64 before atomically installing only the missing native library."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import time

import paramiko

ROOT = Path(__file__).resolve().parents[2]
GAME = "/mnt/sdcard/Ports/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"


def run(ssh, command):
    _, stdout, stderr = ssh.exec_command(command, timeout=60)
    output = stdout.read().decode(errors="replace") + stderr.read().decode(errors="replace")
    if stdout.channel.recv_exit_status():
        raise RuntimeError(command + "\n" + output)
    return output


def upload(ssh, ftp, local, remote):
    ftp.put(str(local), remote)
    digest = hashlib.sha256(local.read_bytes()).hexdigest()
    actual = run(ssh, "sha256sum " + shlex.quote(remote)).split()[0]
    if actual != digest:
        raise RuntimeError("Upload checksum mismatch: " + remote)
    return digest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="[device-host]")
    parser.add_argument("--game", default=GAME)
    args = parser.parse_args()
    game = args.game
    stamp = time.strftime("%Y%m%d-%H%M%S")
    output = ROOT / "build/native-lz4" / ("device-" + stamp)
    output.mkdir(parents=True, exist_ok=False)
    ssh = paramiko.SSHClient()
    ssh.load_system_host_keys()
    ssh.set_missing_host_key_policy(paramiko.RejectPolicy())
    ssh.connect(args.host, username="root", password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=15)
    evidence = {}
    try:
        temp = run(ssh, "mktemp -d /tmp/sdv-lz4-test-XXXXXXXX").strip()
        evidence["temporaryDirectory"] = temp
        protected = [game + "/StardewValleyMainline.sh", game + "/gamedata/Stardew Valley.dll",
                     game + "/Mods/RGDSplus.VerticalWorld/VerticalWorld.dll",
                     "/mnt/sdcard/Ports/Stardew Valley Vertical World D2 Card2.sh"]
        hash_command = "sha256sum " + " ".join(map(shlex.quote, protected))
        evidence["protectedBefore"] = run(ssh, hash_command)
        evidence["platform"] = run(ssh, "uname -m; getconf GNU_LIBC_VERSION")
        with ssh.open_sftp() as ftp:
            library = ROOT / "port/stardewvalleymainline/libs.aarch64/liblwjgl_lz4.so"
            evidence["librarySha256"] = upload(ssh, ftp, library, temp + "/liblwjgl_lz4.so")
            upload(ssh, ftp, ROOT / "tools/native-lz4/test-lz4.py", temp + "/test-lz4.py")
            managed = ROOT / "tools/native-lz4/ManagedSmoke/bin/Release/net6.0"
            for name in ("ManagedSmoke.dll", "ManagedSmoke.deps.json", "ManagedSmoke.runtimeconfig.json"):
                upload(ssh, ftp, managed / name, temp + "/" + name)
            run(ssh, "mkdir " + shlex.quote(temp + "/pc-fixtures"))
            for local in sorted((ROOT / "build/native-lz4/pc-fixtures").glob("*")):
                upload(ssh, ftp, local, temp + "/pc-fixtures/" + local.name)
            evidence["nativeTest"] = run(ssh, "python3 " + shlex.quote(temp + "/test-lz4.py") +
                                         " " + shlex.quote(temp + "/liblwjgl_lz4.so"))
            managed_command = (
                "env LD_LIBRARY_PATH=" + shlex.quote(temp + ":" + game + "/libs.aarch64") +
                " " + shlex.quote(game + "/dotnet/dotnet") +
                " " + shlex.quote(temp + "/ManagedSmoke.dll") +
                " " + shlex.quote(game + "/gamedata"))
            evidence["pcToDevice"] = run(ssh, managed_command + " verify " +
                                        shlex.quote(temp + "/pc-fixtures"))
            evidence["deviceRoundtrip"] = run(ssh, managed_command + " emit " +
                                             shlex.quote(temp + "/device-fixtures"))
            fixtures = output / "device-fixtures"
            fixtures.mkdir()
            for name in ("0.raw", "0.lz4", "1.raw", "1.lz4"):
                ftp.get(temp + "/device-fixtures/" + name, str(fixtures / name))
            evidence["elf"] = run(ssh, "readelf -h " + shlex.quote(temp + "/liblwjgl_lz4.so"))
            evidence["dependencies"] = run(ssh, "ldd " + shlex.quote(temp + "/liblwjgl_lz4.so"))
            destination = game + "/libs.aarch64/liblwjgl_lz4.so"
            license_destination = game + "/licenses/LICENSE.LZ4.txt"
            for local, remote in (
                (library, destination),
                (ROOT / "port/stardewvalleymainline/licenses/LICENSE.LZ4.txt", license_destination),
            ):
                # Preserve a pre-existing file, including on repeat deployments.
                backup = remote + ".before-lz4-" + stamp
                run(ssh, "if [ -e " + shlex.quote(remote) + " ]; then cp -p " +
                    shlex.quote(remote) + " " + shlex.quote(backup) + "; fi")
                upload(ssh, ftp, local, remote + ".next")
                run(ssh, "chmod 644 " + shlex.quote(remote + ".next") + " && mv " +
                    shlex.quote(remote + ".next") + " " + shlex.quote(remote))
            installed_command = managed_command.replace(
                shlex.quote(temp + ":" + game + "/libs.aarch64"), shlex.quote(game + "/libs.aarch64"))
            evidence["installedTest"] = run(ssh, installed_command + " verify " +
                                           shlex.quote(temp + "/pc-fixtures"))
            evidence["installedSha256"] = run(ssh, "sha256sum " + shlex.quote(destination)).split()[0]
            if evidence["installedSha256"] != evidence["librarySha256"]:
                raise RuntimeError("Installed library checksum mismatch.")
        evidence["protectedAfter"] = run(ssh, hash_command)
        if evidence["protectedBefore"] != evidence["protectedAfter"]:
            raise RuntimeError("Protected game/runtime files changed during deployment.")
        evidence["success"] = True
        print(json.dumps(evidence, ensure_ascii=False, indent=2))
        print("Device fixtures for PC reverse verification: " + str(fixtures))
    finally:
        (output / "results.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
        ssh.close()


if __name__ == "__main__":
    main()
