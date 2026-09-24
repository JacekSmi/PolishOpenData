"""Generates icon.png (128x128 RGBA) for the NuGet packages. Stdlib only."""
import struct
import zlib

SIZE = 128
WHITE = (255, 255, 255, 255)
RED = (220, 20, 60, 255)
DARK = (150, 10, 40, 255)


def pixel(x: int, y: int) -> tuple:
    if x + y >= 2 * SIZE - 36:  # folded corner, bottom right
        return DARK
    return WHITE if y < SIZE // 2 else RED


def chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


rows = b"".join(b"\x00" + b"".join(bytes(pixel(x, y)) for x in range(SIZE)) for y in range(SIZE))
png = (
    b"\x89PNG\r\n\x1a\n"
    + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
    + chunk(b"IDAT", zlib.compress(rows, 9))
    + chunk(b"IEND", b"")
)
with open("icon.png", "wb") as f:
    f.write(png)
print("icon.png written,", len(png), "bytes")
