#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
png_probe.py -- D2-T02B 独立材质复核（纯 Python PNG 解码，不用实现者脚本）
================================================================================
1) 解码 table_metallic_roughness.png，校验 R=Metallic、A=1−Roughness、G=B=0。
   与源 table_metallic.png / table_roughness.png 逐像素比对。
2) 解码 table_normal.png，独立复算绿通道均值（实现者称 125.7）。
3) 打印各贴图尺寸。

运行： PY Docs/Tools/png_probe.py
"""
import struct, zlib, sys, io, os

try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

TEX = r"E:/Unity_Deve/Unity_Project/Billiards game/Assets/_Project/Art/Textures"


def decode_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not PNG"
    pos = 8
    width = height = bitdepth = colortype = interlace = None
    idat = b""
    palette = None
    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos:pos+4])
        ctype = data[pos+4:pos+8]
        chunk = data[pos+8:pos+8+length]
        pos += 12 + length
        if ctype == b"IHDR":
            width, height, bitdepth, colortype, comp, filt, interlace = struct.unpack(">IIBBBBB", chunk)
        elif ctype == b"IDAT":
            idat += chunk
        elif ctype == b"PLTE":
            palette = chunk
        elif ctype == b"IEND":
            break
    assert interlace == 0, "interlaced PNG not supported"
    assert bitdepth == 8, f"bitdepth {bitdepth} unsupported"
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[colortype]
    raw = zlib.decompress(idat)
    bpp = channels
    stride = width * bpp
    out = bytearray(height * stride)
    prev = bytearray(stride)
    p = 0
    for y in range(height):
        ftype = raw[p]; p += 1
        line = bytearray(raw[p:p+stride]); p += stride
        if ftype == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i-bpp]) & 0xFF
        elif ftype == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ftype == 3:
            for i in range(stride):
                a = line[i-bpp] if i >= bpp else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ftype == 4:
            for i in range(stride):
                a = line[i-bpp] if i >= bpp else 0
                b = prev[i]
                c = prev[i-bpp] if i >= bpp else 0
                pp = a + b - c
                pa, pb, pc = abs(pp-a), abs(pp-b), abs(pp-c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        out[y*stride:(y+1)*stride] = line
        prev = line
    return {"w": width, "h": height, "colortype": colortype, "channels": channels, "px": out, "palette": palette}


def channels_of(img):
    """返回 (R,G,B,A) 四个 bytes，按 colortype 展开。"""
    ct = img["colortype"]; px = img["px"]; n = img["w"] * img["h"]
    if ct == 0:
        g = px
        return g, g, g, bytes([255])*n
    if ct == 2:
        r = px[0::3]; g = px[1::3]; b = px[2::3]
        return r, g, b, bytes([255])*n
    if ct == 4:
        g = px[0::2]; a = px[1::2]
        return g, g, g, a
    if ct == 6:
        return px[0::4], px[1::4], px[2::4], px[3::4]
    raise ValueError(ct)


def main():
    files = ["table_metallic.png", "table_roughness.png", "table_metallic_roughness.png",
             "table_normal.png", "table_net_rgba.png", "table_basecolor.jpg"]
    imgs = {}
    print("=" * 70)
    print("贴图尺寸（独立解码）")
    for f in files:
        p = os.path.join(TEX, f)
        if not os.path.exists(p):
            print(f"  {f}: 缺失"); continue
        if f.endswith(".jpg"):
            print(f"  {f}: JPEG（{os.path.getsize(p)} B，本脚本不解 JPEG）")
            continue
        img = decode_png(p)
        msg = f"  {f}: {img['w']}x{img['h']} colortype={img['colortype']} channels={img['channels']}"
        # 尺寸检查
        exp = 2048 if f in ("table_normal.png", "table_net_rgba.png") else 1024
        msg += "  [尺寸OK]" if img["w"] == exp else f"  [期望{exp}!]"
        print(msg)
        imgs[f] = img

    # 通道打包核对
    m = imgs["table_metallic.png"]; r = imgs["table_roughness.png"]; pk = imgs["table_metallic_roughness.png"]
    mr, mg, mb, ma = channels_of(m)
    rr, rg, rb, ra = channels_of(r)
    pr, pg, pb, pa = channels_of(pk)

    n = min(len(pr), len(mr), len(rr))
    g_nonzero = sum(1 for i in range(n) if pg[i] != 0)
    b_nonzero = sum(1 for i in range(n) if pb[i] != 0)
    # R 应等于 metallic 值；A 应等于 1 - roughness = 255 - roughness
    dr = dpk_a = 0
    max_dr = max_da = 0
    for i in range(n):
        exp_r = mr[i]
        exp_a = 255 - rr[i]
        d1 = abs(pr[i] - exp_r)
        d2 = abs(pa[i] - exp_a)
        if d1 > 1: dr += 1
        if d2 > 1: dpk_a += 1
        max_dr = max(max_dr, d1); max_da = max(max_da, d2)

    print("\n通道打包核对 (packed R=metallic, A=1-roughness, G=B=0):")
    print(f"  G!=0 像素数 = {g_nonzero}   B!=0 像素数 = {b_nonzero}  (期望 0)")
    print(f"  |packed.R - metallic| > 1 的像素数 = {dr} / {n}  (maxΔ={max_dr})")
    print(f"  |packed.A - (255-roughness)| > 1 的像素数 = {dpk_a} / {n}  (maxΔ={max_da})")
    print(f"  metallic 值分布: min={min(mr)} max={max(mr)} 已采样={n}")
    print(f"  roughness 值分布: min={min(rr)} max={max(rr)}")

    # 法线绿通道
    nl = imgs["table_normal.png"]
    nr, ng, nb, na = channels_of(nl)
    mean_g = sum(ng)/len(ng)
    mean_r = sum(nr)/len(nr)
    mean_b = sum(nb)/len(nb)
    print("\n法线贴图通道均值（独立复算）:")
    print(f"  R mean = {mean_r:.2f}   G mean = {mean_g:.2f}   B mean = {mean_b:.2f}  (实现者称绿均值 125.7)")
    print(f"  G 在 [120,136] 的像素占比 = {sum(1 for v in ng if 120<=v<=136)/len(ng)*100:.1f}%  (中性区附近)")


if __name__ == "__main__":
    main()
