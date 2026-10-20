"""Pack PNG renders (icon-<size>.png) into a Windows .ico. Usage: make-ico.py <png folder> <out.ico>"""
import struct, sys, pathlib

src, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
images = sorted((int(p.stem.split("-")[1]), p.read_bytes()) for p in src.glob("icon-*.png"))
header = struct.pack("<HHH", 0, 1, len(images))
offset = 6 + 16 * len(images)
entries, data = b"", b""
for size, png in images:
    dim = 0 if size >= 256 else size
    entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset + len(data))
    data += png
out.write_bytes(header + entries + data)
print(f"{out} ({len(images)} sizes)")
