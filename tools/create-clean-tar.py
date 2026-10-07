"""Create a portable deployment archive with explicit POSIX executable modes."""
import pathlib
import sys
import tarfile

source = pathlib.Path(sys.argv[1]).resolve(strict=True)
output = pathlib.Path(sys.argv[2]).resolve()

def normalize(entry):
    path = pathlib.PurePosixPath(entry.name)
    executable = path.suffix == ".sh" or path.name in {"dotnet", "smapi-common"}
    entry.mode = 0o755 if entry.isdir() or executable else 0o644
    entry.uid = entry.gid = 0
    entry.uname = entry.gname = "root"
    return entry

with tarfile.open(output, "x:gz", format=tarfile.PAX_FORMAT) as archive:
    archive.add(source, arcname=".", filter=normalize)

with tarfile.open(output, "r:gz") as archive:
    entries = archive.getmembers()
    for entry in entries:
        path = pathlib.PurePosixPath(entry.name)
        if entry.isfile() and path.suffix == ".sh" and entry.mode & 0o111 == 0:
            raise RuntimeError(f"Launcher not executable: {entry.name}")
        if entry.isfile() and "/StardewValleyMainline/gamedata/" in entry.name:
            raise RuntimeError(f"Game assets in release: {entry.name}")
        if entry.isfile() and "/StardewValleyMainline/savedata/" in entry.name:
            raise RuntimeError(f"Save data in release: {entry.name}")
    print(f"Validated {len(entries)} archive entries, executable launchers, no game assets or saves.")
