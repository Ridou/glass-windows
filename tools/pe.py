import struct, sys

def pe_info(path):
    b = open(path, 'rb').read()
    pe = struct.unpack_from('<I', b, 0x3C)[0]
    assert b[pe:pe+4] == b'PE\0\0'
    machine, nsec, _, _, _, optsize, chars = struct.unpack_from('<HHIIIHH', b, pe + 4)
    opt = pe + 24
    magic = struct.unpack_from('<H', b, opt)[0]
    subsystem = struct.unpack_from('<H', b, opt + 68)[0]
    dllchars = struct.unpack_from('<H', b, opt + 70)[0]
    ndirs_off = opt + (108 if magic == 0x20b else 92)
    ndirs = struct.unpack_from('<I', b, ndirs_off)[0]
    dirs = [struct.unpack_from('<II', b, ndirs_off + 4 + 8 * i) for i in range(ndirs)]
    secs = []
    so = opt + optsize
    for i in range(nsec):
        name, vsize, va, rawsize, rawptr = struct.unpack_from('<8sIIII', b, so + 40 * i)
        secs.append((name.rstrip(b'\0').decode(), va, vsize, rawptr, rawsize))
    def off(rva):
        for n, va, vs, rp, rs in secs:
            if va <= rva < va + max(vs, rs): return rva - va + rp
        raise ValueError(hex(rva))
    print(path)
    print('  machine=%#x (x64=0x8664) magic=%#x subsystem=%d (2=GUI, 3=console) dllchars=%#x' % (machine, magic, subsystem, dllchars))
    print('  sections:', [s[0] for s in secs])
    rva, size = dirs[2]
    if not rva:
        print('  NO RESOURCE DIRECTORY'); return b, None
    base = off(rva)
    names = {3: 'RT_ICON', 14: 'RT_GROUP_ICON', 16: 'RT_VERSION', 24: 'RT_MANIFEST'}
    found = {}
    def walk(o, depth, path):
        _, _, _, _, nnamed, nid = struct.unpack_from('<IIHHHH', b, base + o)
        for i in range(nnamed + nid):
            ident, target = struct.unpack_from('<II', b, base + o + 16 + 8 * i)
            key = ident if not ident & 0x80000000 else 'name'
            if target & 0x80000000:
                walk(target & 0x7fffffff, depth + 1, path + [key])
            else:
                data_rva, dsize, _, _ = struct.unpack_from('<IIII', b, base + target)
                found.setdefault(path[0] if path else key, []).append((path + [key], data_rva, dsize))
    walk(0, 0, [])
    for t, items in found.items():
        print('  %-14s x%d  sizes=%s' % (names.get(t, t), len(items), [s for _, _, s in items][:12]))
    return b, (found, off)

b, res = pe_info(sys.argv[1])
if res:
    found, off = res
    if 24 in found:
        _, rva, size = found[24][0]
        print('  manifest:', b[off(rva):off(rva)+size].decode('utf-8', 'replace')[:300].replace('\n', ' ')[:300], '...')
    if 16 in found:
        _, rva, size = found[16][0]
        blob = b[off(rva):off(rva)+size]
        txt = blob.decode('utf-16-le', 'replace')
        import re
        print('  version strings:', re.findall(r'[\x20-\x7e]{3,}', txt)[:30])
