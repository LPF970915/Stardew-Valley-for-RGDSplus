"""Verify release contents without extracting or executing anything."""
import hashlib
import json
import pathlib
import sys
import tarfile

with tarfile.open(sys.argv[1], "r:gz") as archive:
    files = {m.name.removeprefix("./"): m for m in archive.getmembers() if m.isfile()}
    manifest = archive.extractfile(files["SHA256SUMS.txt"]).read().decode("ascii")
    expected = dict((line.split("  ", 1)[1], line.split("  ", 1)[0]) for line in manifest.splitlines())
    assert set(files) - {"SHA256SUMS.txt"} == set(expected), "Checksum manifest does not cover exactly the shipped files"
    for name, member in files.items():
        path = pathlib.PurePosixPath(name)
        assert not path.is_absolute() and ".." not in path.parts, name
        assert "/StardewValleyMainline/gamedata/" not in name, name
        assert "/StardewValleyMainline/savedata/" not in name, name
        assert path.suffix not in {".log", ".pdb", ".next"} and not path.name.startswith("frame-metrics"), name
        if path.suffix == ".sh" or path.name == "dotnet":
            assert member.mode & 0o111, f"Not executable: {name}"
        if name != "SHA256SUMS.txt":
            digest = hashlib.sha256()
            with archive.extractfile(member) as stream:
                while block := stream.read(1024 * 1024):
                    digest.update(block)
            assert digest.hexdigest() == expected[name], f"Hash mismatch: {name}"
    release = json.load(archive.extractfile(files["RELEASE.json"]))
    dll = "StardewVerticalWorldD2/StardewValleyMainline/Mods/RGDSplus.VerticalWorld/VerticalWorld.dll"
    assert release["modSha256"] == expected[dll]
    print(f"PASS: {release['version']}, {len(expected)} file hashes, runtime permissions, no game assets/saves/logs/debug symbols.")
