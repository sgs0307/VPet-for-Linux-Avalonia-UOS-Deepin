#!/usr/bin/env python3
"""生成一个带 dispose_op / blend_op 的 APNG 测试文件（不依赖 PIL）。

画布 100x100，4 帧，刻意覆盖 APNG 里最容易做错的三件事：
  f0 整幅蓝 + dispose=BACKGROUND(1)   -> 显示后把整幅清成透明
  f1 红方块 + blend=SOURCE(0)         -> 直接替换，应只剩红方块
  f2 半透明绿 + blend=OVER(1) + dispose=PREVIOUS(2) -> 叠加，显示后恢复这一块
  f3 黄方块 + blend=SOURCE(0)         -> 绿块应已被 PREVIOUS 撤销
"""
import struct
import sys
import zlib

W = H = 100


def chunk(tag: bytes, data: bytes) -> bytes:
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def raw_pixels(w, h, rgba):
    """filter=0 的 RGBA 扫描行"""
    row = b"\x00" + bytes(rgba) * w
    return row * h


def solid(w, h, color):
    return zlib.compress(raw_pixels(w, h, color), 9)


def main(path):
    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)
    frames = [
        # (w, h, x, y, delay_num, delay_den, dispose, blend, pixel)
        (100, 100, 0, 0, 100, 1000, 1, 0, (0, 0, 255, 255)),
        (40, 40, 20, 20, 200, 1000, 0, 0, (255, 0, 0, 255)),
        (30, 30, 10, 10, 300, 1000, 2, 1, (0, 255, 0, 128)),
        (20, 20, 60, 60, 400, 1000, 0, 0, (255, 255, 0, 255)),
    ]

    out = bytearray(sig)
    out += chunk(b"IHDR", ihdr)
    out += chunk(b"acTL", struct.pack(">II", len(frames), 0))  # 0 = 无限循环

    seq = 0
    for i, (w, h, x, y, dn, dd, disp, blend, color) in enumerate(frames):
        data = solid(w, h, color)
        out += chunk(b"fcTL", struct.pack(">IIIIIHHBB", seq, w, h, x, y, dn, dd, disp, blend))
        seq += 1
        if i == 0:
            out += chunk(b"IDAT", data)
        else:
            out += chunk(b"fdAT", struct.pack(">I", seq) + data)
            seq += 1

    out += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(bytes(out))
    print(f"wrote {path} ({len(out)} bytes, {len(frames)} frames)")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "apng_dispose.apng")
