# Writes a Win32 .res with the app manifest (RT_MANIFEST 1) and the icon (RT_ICON + RT_GROUP_ICON),
# because Mono's mcs ignores -win32manifest. Usage: make_res.py app.manifest fizz.ico out.res
import struct, sys

def entry(rtype, rid, data, flags=0x1030, lang=0x0409):
    hdr = struct.pack('<IIHHHHIHHII', len(data), 32, 0xFFFF, rtype, 0xFFFF, rid, 0, flags, lang, 0, 0)
    pad = (4 - len(data) % 4) % 4
    return hdr + data + b'\0' * pad

manifest = open(sys.argv[1], 'rb').read()
ico = open(sys.argv[2], 'rb').read()
_, _, count = struct.unpack_from('<HHH', ico, 0)
out = entry(0, 0, b'', flags=0, lang=0)  # mandatory empty first entry
group = struct.pack('<HHH', 0, 1, count)
for i in range(count):
    w, h, cc, res, planes, bits, size, off = struct.unpack_from('<BBBBHHII', ico, 6 + 16 * i)
    out += entry(3, i + 1, ico[off:off + size], flags=0x1010)
    group += struct.pack('<BBBBHHIH', w, h, cc, res, planes, bits, size, i + 1)
out += entry(14, 1, group)
out += entry(24, 1, manifest, flags=0x0030)
open(sys.argv[3], 'wb').write(out)
print("wrote", sys.argv[3], len(out), "bytes,", count, "icon images")
