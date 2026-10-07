"""Minimal .NET assembly metadata reader used for port triage.

Reads PE/CLI metadata without external dependencies and reports the metadata
streams plus every name in the #Strings heap that matches an optional regex.
This is a read-only inspection aid for the vendored ARM64 payload under
port/stardewvalleymainline; it never writes to the payload.

Usage:
    python tools/dev/inspect-assembly.py <assembly.dll> [regex]
"""

import re
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
    version = data[md + 16:md + 16 + ver_len].rstrip(b'\0').decode('latin1')
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
    return version, streams


def iter_strings(data, base, size):
    for token in data[base:base + size].split(b'\0'):
        if not token:
            continue
        try:
            yield token.decode('utf-8')
        except UnicodeDecodeError:
            continue


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[1]
    pattern = re.compile(argv[2]) if len(argv) > 2 else None
    data = open(path, 'rb').read()
    md = read_metadata_location(data)
    version, streams = read_streams(data, md)
    print('file      :', path)
    print('size      :', len(data))
    print('runtime   :', version)
    print('streams   :', ', '.join('%s(%d)' % (k, v[1]) for k, v in sorted(streams.items())))
    if pattern is None:
        return 0
    base, size = streams['#Strings']
    names = sorted({name for name in iter_strings(data, base, size) if pattern.search(name)})
    print('matches   :', len(names))
    for name in names[:400]:
        print('   ', name)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
