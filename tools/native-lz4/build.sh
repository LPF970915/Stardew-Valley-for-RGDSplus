#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
SOURCE="$ROOT/cache/lz4-1.10.0"
OUTPUT="$ROOT/build/native-lz4"
PAYLOAD="$ROOT/port/stardewvalleymainline"
CC="${CC:-aarch64-linux-gnu-gcc}"
READELF="${READELF:-aarch64-linux-gnu-readelf}"
mkdir -p "$SOURCE" "$OUTPUT" "$PAYLOAD/libs.aarch64" "$PAYLOAD/licenses"

fetch() {
    local file="$1" digest="$2"
    if [ ! -f "$SOURCE/$file" ] || \
        ! printf '%s  %s\n' "$digest" "$SOURCE/$file" | sha256sum -c --status; then
        curl --fail --location --retry 3 \
            "https://raw.githubusercontent.com/lz4/lz4/v1.10.0/lib/$file" \
            --output "$SOURCE/$file.next"
        printf '%s  %s\n' "$digest" "$SOURCE/$file.next" | sha256sum -c
        mv "$SOURCE/$file.next" "$SOURCE/$file"
    fi
}

fetch lz4.c 9396f7de527bc8435de9c7569fb7998e56545a84b4f3c2d808c0235c01774539
fetch lz4.h 26b82efc53d1570f3b54eef02e9c4764c1ad374ff03cac04e2ced5ea4d4c552f
fetch LICENSE 8b58c446121a109ccf32edc094bba3010a3d85e4ee3702950db55e4d3e87736c

FLAGS=(-std=c99 -O2 -fPIC -fvisibility=hidden -Wall -Wextra -Werror
       -shared -Wl,-z,defs,-z,relro,-z,now,-soname,liblwjgl_lz4.so)
"$CC" "${FLAGS[@]}" -I"$SOURCE" \
    "$ROOT/tools/native-lz4/lwjgl_lz4.c" "$SOURCE/lz4.c" \
    -o "$OUTPUT/liblwjgl_lz4.so"
"$READELF" -h "$OUTPUT/liblwjgl_lz4.so"
"$READELF" --dyn-syms --wide "$OUTPUT/liblwjgl_lz4.so"
"$READELF" --version-info "$OUTPUT/liblwjgl_lz4.so"

# A native build exercises the same shim against the host's independent LZ4 library.
gcc "${FLAGS[@]}" -I"$SOURCE" \
    "$ROOT/tools/native-lz4/lwjgl_lz4.c" "$SOURCE/lz4.c" \
    -o "$OUTPUT/liblwjgl_lz4-host.so"
python3 "$ROOT/tools/native-lz4/test-lz4.py" "$OUTPUT/liblwjgl_lz4-host.so"
install -m 644 "$OUTPUT/liblwjgl_lz4.so" "$PAYLOAD/libs.aarch64/liblwjgl_lz4.so"
install -m 644 "$SOURCE/LICENSE" "$PAYLOAD/licenses/LICENSE.LZ4.txt"
sha256sum "$PAYLOAD/libs.aarch64/liblwjgl_lz4.so"
