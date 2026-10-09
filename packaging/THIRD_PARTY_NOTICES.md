# Third-Party Components

This is an unofficial, game-free RGDSplus adapter maintained by Blood_roc.
Stardew Valley and its game content belong to their respective rights holders.
Purchase the game and import your own legal Windows Steam mainline installation.
The adapter does not supply game assets, authenticate ownership, access Steam
credentials, or bypass game licensing.

## PortMaster Baseline

The baseline is `stardewvalleymainline.zip`, release `2026-07-07_1030`:
https://github.com/PortsMaster/PortMaster-New/releases/download/2026-07-07_1030/stardewvalleymainline.zip

SHA256: `40dabf057800b8f9f4ec25e61426ffd49dfc900522d5302910e3d059319ac682`

Mainline porter: Producdevity; original port and patches: Johnny on Flame.
Retained upstream notices:

- Port launcher: `licenses/LICENSE.Port.txt` (MIT).
- Original patch components: `licenses/LICENSE.Patch.txt` (modified BSD).
- .NET 6.0.32: `licenses/LICENSE.DotNet.txt`,
  `licenses/THIRD-PARTY-NOTICES.DotNet.txt`, and `dotnet/` notices.
- SMAPI 4.5.2: `licenses/LICENSE.SMAPI.txt`.
- MonoGame, Harmony, Mono.Cecil and GL4ES: corresponding `licenses/LICENSE.*.txt`.
- SkiaSharp: `licenses/LICENSE.SkiaSharp.txt`,
  `licenses/THIRD-PARTY-NOTICES.SkiaSharp.txt`.

The original port readme, metadata, cover and screenshot remain attributed
upstream files. They are not newly extracted from the user's Steam resources.
Firmware PortMaster controls/input helpers are prerequisites, not repackaged
system components.

## Added ARM64 LZ4 Shim

Official LZ4 1.10.0:
https://github.com/lz4/lz4/tree/v1.10.0/lib

License: BSD-2-Clause; retained at `licenses/LICENSE.LZ4.txt`.
`libs.aarch64/liblwjgl_lz4.so` wraps three symbols used by the game's existing
LWJGL compression interface; it is not a full LWJGL or Java distribution.
Shim source and pinned source checksums are in the project `tools/native-lz4/`.

## RGDSplus Adapter

The source project contains the dual-screen SMAPI adapter, native touch/session
integration, local import tool, release builder, tests and implementation records.
Third-party components retain their own licenses. This notice neither relicenses
their code nor grants any rights to Stardew Valley game content.
The companion page structure is a reference-based RGDSplus implementation;
no Android APK, extracted Android code, or Android runtime is shipped.
