#!/usr/bin/env python3
"""Reference SFF v1/v2 decoder used to generate the C# test fixtures.

This is an independent Python implementation of the format (written from the
Ikemen GO Go sources in `engine/ikemen-go/src/image.go`). The Unity C# reader in
`unity/Assets/IK/Scripts/Core/` must agree with it sprite for sprite: the EditMode
test `SffReaderTests` compares its own decode against the JSON this script writes.

Usage:
    python3 tools/sff_dump.py <file.sff> [-o fixture.json] [--png DIR] [--limit N]
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import struct
import sys
from dataclasses import dataclass, field


# ---------------------------------------------------------------- decoders


def rle_pcx_decode(data: bytes, w: int, h: int, bpl: int) -> bytes:
    """PCX run-length decoding (`Sprite.RlePcxDecode`), `bpl` = bytes per line."""
    out = bytearray(w * h)
    if not data or bpl <= 0:
        return bytes(data[: w * h].ljust(w * h, b"\0"))
    i = j = k = 0
    n_total = len(data)
    while j < len(out):
        n, d = 1, data[i]
        if i < n_total - 1:
            i += 1
        if d >= 0xC0:
            n = d & 0x3F
            d = data[i]
            if i < n_total - 1:
                i += 1
        while n > 0:
            if k < w and j < len(out):
                out[j] = d
                j += 1
            k += 1
            if k == bpl:
                k = 0
                n = 1
            n -= 1
    return bytes(out)


def rle8_decode(data: bytes, w: int, h: int) -> bytes:
    out = bytearray(w * h)
    if not data:
        return bytes(out)
    i = j = 0
    n_total = len(data)
    while j < len(out):
        n, d = 1, data[i]
        if i < n_total - 1:
            i += 1
        if d & 0xC0 == 0x40:
            n = d & 0x3F
            d = data[i]
            if i < n_total - 1:
                i += 1
        while n > 0:
            if j < len(out):
                out[j] = d
                j += 1
            n -= 1
    return bytes(out)


def rle5_decode(data: bytes, w: int, h: int) -> bytes:
    out = bytearray(w * h)
    if not data:
        return bytes(out)
    i = j = 0
    n_total = len(data)
    while j < len(out):
        rl = data[i]
        if i < n_total - 1:
            i += 1
        dl = data[i] & 0x7F
        c = 0
        if data[i] >> 7:
            if i < n_total - 1:
                i += 1
            c = data[i]
        if i < n_total - 1:
            i += 1
        while True:
            if j < len(out):
                out[j] = c
                j += 1
            rl -= 1
            if rl < 0:
                dl -= 1
                if dl < 0:
                    break
                c = data[i] & 0x1F
                rl = data[i] >> 5
                if i < n_total - 1:
                    i += 1
    return bytes(out)


def lz5_decode(data: bytes, w: int, h: int) -> bytes:
    out = bytearray(w * h)
    if not data:
        return bytes(out)
    i = j = n = 0
    n_total = len(data)
    ct, cts, rb, rbc = data[i], 0, 0, 0
    if i < n_total - 1:
        i += 1
    while j < len(out):
        d = data[i]
        if i < n_total - 1:
            i += 1
        if ct & (1 << cts):
            if d & 0x3F == 0:
                d = ((d << 2) | data[i]) + 1
                if i < n_total - 1:
                    i += 1
                n = data[i] + 2
                if i < n_total - 1:
                    i += 1
            else:
                rb |= (d & 0xC0) >> rbc
                rb &= 0xFF
                rbc += 2
                n = d & 0x3F
                if rbc < 8:
                    d = data[i] + 1
                    if i < n_total - 1:
                        i += 1
                else:
                    d = rb + 1
                    rb, rbc = 0, 0
            while True:
                if j < len(out):
                    out[j] = out[j - d]
                    j += 1
                n -= 1
                if n < 0:
                    break
        else:
            if d & 0xE0 == 0:
                n = data[i] + 8
                if i < n_total - 1:
                    i += 1
            else:
                n = d >> 5
                d &= 0x1F
            while n > 0:
                if j < len(out):
                    out[j] = d
                    j += 1
                n -= 1
        cts += 1
        if cts >= 8:
            ct, cts = data[i], 0
            if i < n_total - 1:
                i += 1
    return bytes(out)


# ---------------------------------------------------------------- model


@dataclass
class Sprite:
    group: int
    number: int
    w: int = 0
    h: int = 0
    ox: int = 0
    oy: int = 0
    fmt: int = 0           # v2 format byte; -1 for a v1 PCX sprite
    coldepth: int = 8
    palidx: int = -1
    link: int = 0
    pixels: bytes = b""    # 8-bit indices, or raw RGBA/RGB bytes when raw=True
    raw: bool = False
    pal: list = field(default_factory=list)   # v1 only: 256 packed ABGR values


def _pal_from_rgb_triples(buf: bytes) -> list:
    pal = []
    for i in range(256):
        r, g, b = buf[i * 3], buf[i * 3 + 1], buf[i * 3 + 2]
        a = 0 if i == 0 else 255
        pal.append((a << 24) | (b << 16) | (g << 8) | r)
    return pal


def read_v1(f, nspr: int, first: int, is_char: bool) -> list:
    sprites = []
    shofs = first
    prev = None
    for _ in range(nspr):
        f.seek(shofs)
        hdr = f.read(19)
        nxt, size, ox, oy, group, number, link = struct.unpack("<IIhhHHH", hdr[:18])
        ps = hdr[18]
        s = Sprite(group=group, number=number, ox=ox, oy=oy, fmt=-1, link=link)
        if size == 0:
            if link < len(sprites):
                src = sprites[link]
                s.w, s.h, s.pixels, s.pal = src.w, src.h, src.pixels, src.pal
                s.ox, s.oy = src.ox, src.oy
            sprites.append(s)
            shofs = nxt
            continue

        offset = shofs + 32
        f.seek(offset)
        pcx = f.read(128)
        encoding, bpp = pcx[2], pcx[3]
        if bpp != 8:
            raise ValueError("PCX colour depth %d" % bpp)
        x0, y0, x1, y1 = struct.unpack("<HHHH", pcx[4:12])
        bpl = struct.unpack("<H", pcx[66:68])[0]
        s.w, s.h = x1 - x0 + 1, y1 - y0 + 1
        rle_bpl = bpl if encoding == 1 else 0
        pcx_data_start = offset + 128
        palette_same = ps != 0 and prev is not None

        is_char_first = is_char and (prev is None or (group == 0 and number == 0))
        if is_char_first:
            data_size = nxt - offset if nxt > offset else size
            px = f.read(max(0, data_size - 128))
            pal_off = offset + data_size - 768
            pal_has_marker = False
        else:
            block_end = nxt if nxt > offset else offset + size
            if palette_same:
                pal_off = block_end
            else:
                pal_off = -1
                pos = block_end - 769
                while pos >= pcx_data_start:
                    f.seek(pos)
                    if f.read(1) == b"\x0c":
                        pal_off = pos
                        break
                    pos -= 1
                if pal_off < 0:
                    pal_off = block_end - 769
            f.seek(pcx_data_start)
            px = f.read(max(0, pal_off - pcx_data_start))
            pal_has_marker = True

        if palette_same and prev is not None:
            s.pal = prev.pal
        else:
            f.seek(pal_off + (1 if pal_has_marker else 0))
            s.pal = _pal_from_rgb_triples(f.read(768))

        s.pixels = rle_pcx_decode(px, s.w, s.h, rle_bpl)
        sprites.append(s)
        prev = s
        shofs = nxt
    return sprites


def read_v2(f, nspr: int, first: int, lofs: int, tofs: int, npal: int, pal_first: int,
            ver2: int) -> tuple:
    # palettes
    pals, pal_table = [], {}
    unique = {}
    for i in range(npal):
        f.seek(pal_first + i * 16)
        g, n, numcols, link = struct.unpack("<HHHH", f.read(8))
        ofs, plsize = struct.unpack("<II", f.read(8))
        if (g, n) in unique:
            idx = unique[(g, n)]
            pal = pals[idx]
        elif plsize == 0:
            idx = link
            pal = pals[idx] if idx < len(pals) else []
        else:
            f.seek(lofs + ofs)
            raw_count = plsize // 4
            depth = 1
            while depth < raw_count:
                depth *= 2
            depth = max(16, min(256, depth))
            pal = []
            for c in range(depth):
                if c < raw_count:
                    r, gg, b, a = f.read(4)
                else:
                    r = gg = b = a = 0
                if ver2 == 0:
                    a = 0 if c == 0 else 255
                pal.append((a << 24) | (b << 16) | (gg << 8) | r)
            idx = i
        unique[(g, n)] = idx
        pals.append(pal)
        pal_table[(g, n)] = idx

    sprites = []
    shofs = first
    for _ in range(nspr):
        f.seek(shofs)
        hdr = f.read(28)
        group, number, w, h, ox, oy, link = struct.unpack("<HHHHhhH", hdr[:14])
        fmt, coldepth = hdr[14], hdr[15]
        dofs, dsize = struct.unpack("<II", hdr[16:24])
        palidx, flags = struct.unpack("<HH", hdr[24:28])
        dofs += tofs if flags & 1 else lofs
        s = Sprite(group=group, number=number, w=w, h=h, ox=ox, oy=oy, fmt=fmt,
                   coldepth=coldepth, palidx=palidx, link=link)
        shofs += 28
        if dsize == 0:
            if link < len(sprites):
                src = sprites[link]
                s.w, s.h = src.w, src.h
                s.pixels, s.raw, s.coldepth = src.pixels, src.raw, src.coldepth
                s.palidx = src.palidx
            sprites.append(s)
            continue
        if fmt == 0:
            f.seek(dofs)
            px = f.read(dsize)
            if coldepth in (24, 32):
                s.raw = True
            s.pixels = px
        elif fmt in (2, 3, 4):
            f.seek(dofs + 4)
            px = f.read(max(0, dsize - 4))
            s.pixels = {2: rle8_decode, 3: rle5_decode, 4: lz5_decode}[fmt](px, w, h)
        elif fmt in (10, 11, 12):
            from PIL import Image
            f.seek(dofs + 4)
            img = Image.open(io.BytesIO(f.read(dsize - 4)))
            img.load()
            if fmt == 10 and img.mode == "P":
                s.pixels = img.tobytes()
            else:
                s.raw = True
                s.coldepth = 32
                s.pixels = img.convert("RGBA").tobytes()
        else:
            raise ValueError("unknown sprite format %d" % fmt)
        sprites.append(s)
    return sprites, pals, pal_table


def load(path: str, is_char: bool = True):
    with open(path, "rb") as f:
        head = f.read(64)
        if head[:12] != b"ElecbyteSpr\0":
            raise ValueError("not an SFF file")
        verlo3, verlo2, verlo1, verhi = head[12:16]
        f.seek(0)
        data = io.BytesIO(f.read())
    if verhi == 1:
        nspr, first = struct.unpack("<II", head[20:28])
        sprites = read_v1(data, nspr, first, is_char)
        return dict(version=(verhi, verlo1, verlo2, verlo3), sprites=sprites,
                    pals=[], pal_table={})
    if verhi != 2:
        raise ValueError("unsupported SFF version %d" % verhi)
    first, nspr, pal_first, npal = struct.unpack("<IIII", head[36:52])
    lofs = struct.unpack("<I", head[52:56])[0]
    tofs = struct.unpack("<I", head[60:64])[0]
    sprites, pals, pal_table = read_v2(data, nspr, first, lofs, tofs, npal, pal_first,
                                       verlo2)
    return dict(version=(verhi, verlo1, verlo2, verlo3), sprites=sprites, pals=pals,
                pal_table=pal_table)


def sha1(b: bytes) -> str:
    return hashlib.sha1(b).hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("sff")
    ap.add_argument("-o", "--out")
    ap.add_argument("--png", help="write decoded sprites as PNG into this directory")
    ap.add_argument("--limit", type=int, default=0, help="only the first N sprites")
    ap.add_argument("--stage", action="store_true", help="not a character SFF")
    args = ap.parse_args()

    sff = load(args.sff, is_char=not args.stage)
    sprites = sff["sprites"]
    if args.limit:
        sprites = sprites[: args.limit]

    entries = []
    for i, s in enumerate(sprites):
        entries.append(dict(index=i, group=s.group, number=s.number, w=s.w, h=s.h,
                            x=s.ox, y=s.oy, fmt=s.fmt, coldepth=s.coldepth,
                            palidx=s.palidx, raw=s.raw, bytes=len(s.pixels),
                            sha1=sha1(s.pixels)))
    doc = dict(file=args.sff, version=list(sff["version"]),
               sprites=len(sff["sprites"]), palettes=len(sff["pals"]),
               palette_sha1=[sha1(struct.pack("<%dI" % len(p), *p)) for p in sff["pals"]],
               entries=entries)
    text = json.dumps(doc, indent=1)
    if args.out:
        with open(args.out, "w") as fh:
            fh.write(text)
        print("wrote", args.out, len(entries), "entries")
    else:
        print(text)

    if args.png:
        import os
        from PIL import Image
        os.makedirs(args.png, exist_ok=True)
        for s in sprites:
            if s.w == 0 or s.h == 0 or not s.pixels:
                continue
            name = "%s/%d-%d.png" % (args.png, s.group, s.number)
            if s.raw:
                mode = "RGBA" if s.coldepth == 32 else "RGB"
                need = s.w * s.h * (4 if mode == "RGBA" else 3)
                if len(s.pixels) < need:
                    continue
                Image.frombytes(mode, (s.w, s.h), s.pixels[:need]).save(name)
            else:
                pal = s.pal if s.pal else (sff["pals"][s.palidx] if s.palidx < len(sff["pals"]) else [])
                if not pal:
                    continue
                img = Image.frombytes("P", (s.w, s.h), s.pixels.ljust(s.w * s.h, b"\0"))
                flat = []
                for c in pal:
                    flat += [c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF]
                flat += [0] * (768 - len(flat))
                img.putpalette(flat[:768])
                img = img.convert("RGBA")
                px = img.load()
                for y in range(s.h):      # index 0 is transparent
                    for x in range(s.w):
                        if s.pixels[y * s.w + x] == 0:
                            px[x, y] = (0, 0, 0, 0)
                img.save(name)
    return 0


if __name__ == "__main__":
    sys.exit(main())
