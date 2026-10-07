"""Dump the #US (user string) heap of a .NET assembly.

The #US heap holds every string literal in the assembly, which is the fastest
way to learn what a prebuilt tool such as MainlineGameDataPatcher does: argument
names, file names, error messages, and target type/method names.

Usage:
    python tools/dev/dump-user-strings.py <assembly.dll> [min-length]
"""

import struct
import sys


def read_metadata_location(data):
    pe = struct.unpack_from('<I', data, 0x3C)[0]
    if data[pe:pe + 4] != b'PE\0\0':
        raise ValueError('not a PE file')
    nsec = struct.unpack_from('<H', data, pe + 6)[0]
    optsize = struct.unpack_from('<H', data, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from('<H', data, opt)[0]
    ddir = opt + (96 if magic == 0x10B else 112)
    cli_rva, _ = struct.unpack_from('<II', data, ddir + 14 * 8)
    sections = []
    for i in range(nsec):
        s = opt + optsize + i * 40
        vsize, vaddr, rsize, raddr = struct.unpack_from('<IIII', data, s + 8)
        sections.append((vaddr, vsize, raddr, rsize))

    def rva_to_offset(rva):
        for vaddr, vsize, raddr, rsize in sections:
            if vaddr <= rva < vaddr + max(vsize, rsize):
                return raddr + (rva - vaddr)
        return None

    cli = rva_to_offset(cli_rva)
    md_rva, _ = struct.unpack_from('<II', data, cli + 8)
    return rva_to_offset(md_rva)


def read_streams(data, md):
    ver_len = struct.unpack_from('<I', data, md + 12)[0]
    p = md + 16 + ver_len + 2
    count = struct.unpack_from('<H', data, p)[0]
    p += 2
    streams = {}
    for _ in range(count):
        off, size = struct.unpack_from('<II', data, p)
        p += 8
        end = data.index(b'\0', p)
        name = data[p:end].decode('latin1')
        p = (end + 1 + 3) & ~3
        streams[name] = (md + off, size)
    return streams


def read_compressed_uint(data, offset):
    b0 = data[offset]
    if b0 & 0x80 == 0:
        return b0, offset + 1
    if b0 & 0x40 == 0:
        return ((b0 & 0x3F) << 8) | data[offset + 1], offset + 2
    return (((b0 & 0x1F) << 24) | (data[offset + 1] << 16)
            | (data[offset + 2] << 8) | data[offset + 3]), offset + 4


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[1]
    min_len = int(argv[2]) if len(argv) > 2 else 3
    data = open(path, 'rb').read()
    md = read_metadata_location(data)
    streams = read_streams(data, md)
    base, size = streams['#US']
    blob = data[base:base + size]
    offset = 1
    seen = set()
    out = []
    while offset < size:
        length, next_offset = read_compressed_uint(blob, offset)
        if length == 0:
            offset = next_offset
            continue
        raw = blob[next_offset:next_offset + length]
        if len(raw) < length:
            break
        # Final byte is a literal-string flag; the rest is UTF-16LE.
        text = raw[:-1].decode('utf-16-le', 'replace')
        if text not in seen and len(text) >= min_len:
            seen.add(text)
            out.append(text)
        offset = next_offset + length
    print('file    :', path)
    print('literals:', len(out))
    for text in sorted(out):
        print('  ', text)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
