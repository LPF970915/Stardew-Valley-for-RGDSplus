"""Read-only native ABI/block-format checks, usable on the host and ARM64 device."""
import ctypes as c
import ctypes.util
import random
import sys


def functions(library, shim):
    prefix = "Java_org_lwjgl_util_lz4_LZ4_" if shim else "LZ4_"
    bound = getattr(library, prefix + ("LZ4_1compressBound" if shim else "compressBound"))
    compress = getattr(library, prefix + ("nLZ4_1compress_1default" if shim else "compress_default"))
    decompress = getattr(library, prefix + ("nLZ4_1decompress_1safe" if shim else "decompress_safe"))
    extra = [c.c_void_p, c.c_void_p] if shim else []
    bound.argtypes = extra + [c.c_int]
    compress.argtypes = decompress.argtypes = extra + [c.c_void_p, c.c_void_p, c.c_int, c.c_int]
    for function in (bound, compress, decompress):
        function.restype = c.c_int
    args = (None, None) if shim else ()
    return (
        lambda size: bound(*args, size),
        lambda src, dst, size, capacity: compress(*args, src, dst, size, capacity),
        lambda src, dst, size, capacity: decompress(*args, src, dst, size, capacity),
    )


def encode(api, data):
    bound, compress, _ = api
    destination = c.create_string_buffer(bound(len(data)))
    size = compress(c.create_string_buffer(data), destination, len(data), len(destination))
    assert size > 0
    return destination.raw[:size]


def decode(api, block, expected):
    destination = c.create_string_buffer(max(1, len(expected)))
    size = api[2](c.create_string_buffer(block), destination, len(block), len(expected))
    assert size == len(expected)
    assert destination.raw[:size] == expected


shim = functions(c.CDLL(sys.argv[1]), True)
reference_path = ctypes.util.find_library("lz4")
reference = functions(c.CDLL(reference_path), False) if reference_path else None
decode(shim, b"\x50hello", b"hello")
assert shim[0](-1) == 0
rng = random.Random(24642)
cases = [b"", b"x", b"hello", bytes(range(256)), b"A" * 4096,
         b"StardewValley farmhand world state " * 8192]
cases += [rng.randbytes(size) for size in (15, 255, 1023, 1024, 1025, 65536, 262144)]
for data in cases:
    compressed = encode(shim, data)
    decode(shim, compressed, data)
    if reference:
        decode(reference, compressed, data)
        decode(shim, encode(reference, data), data)
buffer = c.create_string_buffer(1)
assert shim[1](c.create_string_buffer(b"A" * 1024), buffer, 1024, 1) == 0
assert shim[2](c.create_string_buffer(b"\x50hello"), buffer, 6, 1) < 0
assert shim[2](c.create_string_buffer(b"\x50h"), buffer, 2, 1) < 0
print(f"PASS: three LWJGL exports, {len(cases)} block roundtrips, "
      f"known block, invalid/capacity checks; independent LZ4={bool(reference)}")
