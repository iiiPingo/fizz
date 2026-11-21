# Writes a Win32 .res with the app manifest (RT_MANIFEST 1), the icon (RT_ICON + RT_GROUP_ICON) and a version
# block (RT_VERSION 1), because Mono's mcs ignores -win32manifest and -win32res replaces the version mcs would add.
# Usage: make_res.py app.manifest fizz.ico out.res [version]
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

# ---- VS_VERSIONINFO, so Explorer, Task Manager and Minibar can see Fizz's version
def pad4(b): return b + b'\0' * ((4 - len(b) % 4) % 4)
def node(key, value=b'', vtype=1, children=b'', value_len=None):
    vl = value_len if value_len is not None else len(value) // (2 if vtype == 1 else 1)
    b = pad4(struct.pack('<HHH', 0, vl, vtype) + (key + '\0').encode('utf-16-le'))
    if value: b = pad4(b + value)
    b += children
    return struct.pack('<H', len(b)) + b[2:]
def sval(key, text): return pad4(node(key, (text + '\0').encode('utf-16-le'), 1))
ver = sys.argv[4] if len(sys.argv) > 4 else '1.1.0.0'
v = [int(x) for x in (ver.split('.') + ['0'] * 4)[:4]]
ffi = struct.pack('<13I', 0xFEEF04BD, 0x00010000, (v[0] << 16) | v[1], (v[2] << 16) | v[3], (v[0] << 16) | v[1], (v[2] << 16) | v[3],
                  0x3F, 0, 0x40004, 1, 0, 0, 0)
strings = b''.join(sval(k, t) for k, t in [('CompanyName', 'iiiPingo'), ('FileDescription', 'Fizz - in-game performance overlay'),
                                             ('FileVersion', ver), ('InternalName', 'Fizz'), ('OriginalFilename', 'Fizz.exe'),
                                             ('ProductName', 'Fizz'), ('ProductVersion', ver)])
table = pad4(node('040904B0', children=strings, value_len=0))
sfi = pad4(node('StringFileInfo', children=table, value_len=0))
var = pad4(node('Translation', struct.pack('<HH', 0x0409, 1200), vtype=0))
vfi = pad4(node('VarFileInfo', children=var, value_len=0))
vi = node('VS_VERSION_INFO', ffi, vtype=0, children=sfi + vfi)
out += entry(16, 1, vi, flags=0x0030)
open(sys.argv[3], 'wb').write(out)
print("wrote", sys.argv[3], len(out), "bytes,", count, "icon images, version", ver)
