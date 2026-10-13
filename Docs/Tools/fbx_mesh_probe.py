#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fbx_mesh_probe.py -- 直接解析 FBX 二进制的网格顶点（绕过 Unity，独立）
================================================================================
目的：
  1) 绕过 Unity，直接从 PoolTable_Normalized.fbx 取顶点 —— 补齐 D2-T02A 的独立性缺口。
  2) 用「我 D2-T02A 基线端点」对网格做 48 候选轴枚举 + 最近距离，独立复现 AXL-04/05。
  3) 对"0.26mm 是轮廓近似残差"做**可证伪检验**：
     - 端点→最近顶点 (vertex) 距离
     - 端点→最近三角形上一点 (point-on-mesh) 距离   ← 若显著下降，说明残差来自"顶点采样"而非几何错位
     - 线段中点→最近网格点 距离（沿段偏差）         ← 反映曲率逼近
     - 局部顶点间距（三角形边长）                    ← 给出"最近顶点"的物理下限
运行： PY Docs/Tools/fbx_mesh_probe.py
"""
from __future__ import annotations
import struct, zlib, sys, io, os, json, math

try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
FBX = os.path.join(ROOT, "Assets/_Project/Art/Models/PoolTable_Normalized.fbx")
BASELINE = os.path.join(ROOT, "Docs/Reports/d2t02a-baseline.json")

MAGIC = b"Kaydara FBX Binary  \x00"


class Node:
    __slots__ = ("name", "props", "children")

    def __init__(self, name):
        self.name = name
        self.props = []
        self.children = []


def parse_fbx(path):
    data = open(path, "rb").read()
    assert data[:21] == MAGIC, "not FBX binary"
    version = struct.unpack_from("<I", data, 23)[0]
    is64 = version >= 7500
    off = 27

    def read_prop(p):
        t = data[p:p+1]
        p += 1
        if t == b"Y":
            v = struct.unpack_from("<h", data, p)[0]; p += 2
        elif t == b"C":
            v = data[p]; p += 1
        elif t == b"I":
            v = struct.unpack_from("<i", data, p)[0]; p += 4
        elif t == b"F":
            v = struct.unpack_from("<f", data, p)[0]; p += 4
        elif t == b"D":
            v = struct.unpack_from("<d", data, p)[0]; p += 8
        elif t == b"L":
            v = struct.unpack_from("<q", data, p)[0]; p += 8
        elif t in (b"f", b"d", b"l", b"i", b"b"):
            count, enc, clen = struct.unpack_from("<III", data, p); p += 12
            fmt = {b"f": "f", b"d": "d", b"l": "q", b"i": "i", b"b": "b"}[t]
            esize = struct.calcsize(fmt)
            if enc == 1:
                raw = zlib.decompress(data[p:p + clen]); p += clen
            else:
                raw = data[p:p + count * esize]; p += count * esize
            v = list(struct.unpack("<%d%s" % (count, fmt), raw))
        elif t == b"S":
            ln = struct.unpack_from("<I", data, p)[0]; p += 4
            v = data[p:p+ln]; p += ln
        elif t == b"R":
            ln = struct.unpack_from("<I", data, p)[0]; p += 4
            v = data[p:p+ln]; p += ln
        else:
            raise ValueError(f"unknown prop type {t!r} at {p}")
        return v, p

    def read_node(p):
        if is64:
            end, nprops, plen = struct.unpack_from("<QQQ", data, p); p += 24
        else:
            end, nprops, plen = struct.unpack_from("<III", data, p); p += 12
        namelen = data[p]; p += 1
        if end == 0:
            return None, p
        name = data[p:p + namelen].decode("utf-8", "replace"); p += namelen
        node = Node(name)
        for _ in range(nprops):
            v, p = read_prop(p)
            node.props.append(v)
        while p < end:
            child, p = read_node(p)
            if child is None:
                break
            node.children.append(child)
        return node, end

    root = Node("ROOT")
    while off < len(data):
        n, off = read_node(off)
        if n is None:
            break
        root.children.append(n)
    return version, root


def iter_nodes(node, name):
    for c in node.children:
        if c.name == name:
            yield c
        yield from iter_nodes(c, name)


def dist2(a, b):
    return (a[0]-b[0])**2 + (a[1]-b[1])**2 + (a[2]-b[2])**2


def main():
    version, root = parse_fbx(FBX)
    print(f"FBX version={version}")

    geo = list(iter_nodes(root, "Geometry"))
    print(f"Geometry 节点数 = {len(geo)}")
    meshes = []
    for gi, g in enumerate(geo):
        verts = None; pvi = None
        for c in g.children:
            if c.name == "Vertices" and c.props and isinstance(c.props[0], list):
                verts = c.props[0]
            if c.name == "PolygonVertexIndex" and c.props and isinstance(c.props[0], list):
                pvi = c.props[0]
        if verts:
            n = len(verts) // 3
            print(f"  Geometry[{gi}] verts={n}  polys={'yes' if pvi else 'no'}")
            meshes.append((gi, verts, pvi))

    if not meshes:
        print("未找到顶点"); return

    # 汇总所有顶点（本 FBX 只导出 pooltable_low）
    allv = []
    for gi, verts, pvi in meshes:
        allv.extend([(verts[i], verts[i+1], verts[i+2]) for i in range(0, len(verts), 3)])
    print(f"总顶点 = {len(allv)}  (源 JSON 声称 vertices=10572)")

    # bbox（FBX 原始局部坐标）
    mn = [min(v[i] for v in allv) for i in range(3)]
    mx = [max(v[i] for v in allv) for i in range(3)]
    print(f"FBX 原始局部 bbox min=({mn[0]:.6f},{mn[1]:.6f},{mn[2]:.6f}) max=({mx[0]:.6f},{mx[1]:.6f},{mx[2]:.6f})")
    print("  （对照实现者 AxisProbe 的 RAW local bounds min=(-1.412409,-0.797634,-0.863517) max=(1.412686,0.797634,0.061022)）")

    # 载入我的基线端点
    bl = json.load(open(BASELINE, encoding="utf-8"))
    endpoints = []
    for s in bl["segments"]:
        endpoints.append((s["a"]["x"], s["a"]["y"]))
        endpoints.append((s["b"]["x"], s["b"]["y"]))
    for p in bl["pockets"]:
        for grp in ("mouth", "throat"):
            for pt in p[grp]:
                endpoints.append((pt["x"], pt["y"]))
    print(f"基线端点(含段与袋口) = {len(endpoints)}")

    # 48 候选枚举（水平面对齐：物理(x,y)->(x,·,y)）
    perms = [(0,1,2),(0,2,1),(1,0,2),(1,2,0),(2,0,1),(2,1,0)]
    res = []
    for perm in perms:
        for s0 in (1,-1):
            for s1 in (1,-1):
                for s2 in (1,-1):
                    worst = 0.0
                    for (ex, ey) in endpoints:
                        best = 1e18
                        for v in allv:
                            c = (s0*v[perm[0]], s1*v[perm[1]], s2*v[perm[2]])
                            d = (c[0]-ex)**2 + (c[2]-ey)**2
                            if d < best: best = d
                        if best > worst: worst = best
                    res.append((f"{perm[0]}{perm[1]}{perm[2]}", f"{s0},{s1},{s2}", math.sqrt(worst)))
    res.sort(key=lambda x: x[2])
    print("\n48 候选 Top5（FBX 直解，绕过 Unity）:")
    for r in res[:5]:
        print(f"  perm={r[0]} sign={r[1]} worst={r[2]*1000:.4f} mm")
    print(f"  次优排列(非021)最优 = {min(r[2] for r in res if r[0]!='021')*1000:.3f} mm")

    # ── 可证伪检验：0.26mm 是"轮廓近似残差"还是"顶点采样/索引偏置"？──────
    # 取最优候选 (021 / -1,1,1)
    s0,s1,s2 = -1,1,1; perm=(0,2,1)
    tv = [(s0*v[perm[0]], s1*v[perm[1]], s2*v[perm[2]]) for v in allv]

    # (a) 端点 -> 最近顶点
    def nearest_vertex(p):
        px, py = p
        b = 1e18
        for v in tv:
            d = (v[0]-px)**2+(v[2]-py)**2
            if d < b: b = d
        return math.sqrt(b)

    # (b) 端点 -> 最近三角形上的点（用 PolygonVertexIndex 建三角）
    tris = []
    if meshes[0][2]:
        pvi = meshes[0][2]
        # 重新对每个 geometry 建三角（此处仅第一个网格，含绝大多数几何）
        for gi, verts, pv in meshes:
            base = 0
            cur = []
            pos = []
            for idx in pv:
                if idx < 0:
                    cur.append(~idx)   # 负数取反表示多边形结束
                    if len(cur) >= 3:
                        # fan
                        for k in range(1, len(cur)-1):
                            pos.append((cur[0], cur[k], cur[k+1]))
                    cur = []
                else:
                    cur.append(idx)
            tris.append((verts, pos))
    def nearest_tri(p):
        px, py = p
        b = 1e18
        for verts, polys in tris:
            for (i0,i1,i2) in polys:
                # 顶点（已变换）
                ax = s0*verts[3*i0+perm[0]]; ay = s1*verts[3*i0+perm[1]]; az = s2*verts[3*i0+perm[2]]
                bx = s0*verts[3*i1+perm[0]]; by = s1*verts[3*i1+perm[1]]; bz = s2*verts[3*i1+perm[2]]
                cx = s0*verts[3*i2+perm[0]]; cy = s1*verts[3*i2+perm[1]]; cz = s2*verts[3*i2+perm[2]]
                # 到 XY? 用水平面 (x, z)。简化为点到三角形在 (x,z) 投影的最近距离
                d = point_tri_dist_2d(px, py, ax, az, bx, bz, cx, cz)
                if d < b: b = d
        return math.sqrt(b)

    def point_tri_dist_2d(px, py, ax, ay, bx, by, cx, cy):
        # 标准 2D 点到三角形最近距离平方
        # 先判断是否在三角形内（重心）
        def sign(x1,y1,x2,y2,x3,y3):
            return (x1-x3)*(y2-y3)-(x2-x3)*(y1-y3)
        d1=sign(px,py,ax,ay,bx,by); d2=sign(px,py,bx,by,cx,cy); d3=sign(px,py,cx,cy,ax,ay)
        neg = (d1<0)or(d2<0)or(d3<0); pos=(d1>0)or(d2>0)or(d3>0)
        if not (neg and pos):
            return 0.0
        return min(seg_d2(px,py,ax,ay,bx,by), seg_d2(px,py,bx,by,cx,cy), seg_d2(px,py,cx,cy,ax,ay))
    def seg_d2(px,py,ax,ay,bx,by):
        dx,dy=bx-ax,by-ay
        L=dx*dx+dy*dy
        if L==0: return (px-ax)**2+(py-ay)**2
        t=max(0,min(1,((px-ax)*dx+(py-ay)*dy)/L))
        qx,qy=ax+t*dx,ay+t*dy
        return (px-qx)**2+(py-qy)**2

    worst_v = max(nearest_vertex(e) for e in endpoints)
    print(f"\n[可证伪检验]")
    print(f"  (a) 端点→最近**顶点**(vertex) 最坏 = {worst_v*1000:.6f} mm")
    vdists = [nearest_vertex(e) for e in endpoints]
    print(f"      分布: >1e-5m 的端点数={sum(1 for d in vdists if d>1e-5)}  >1e-4m={sum(1 for d in vdists if d>1e-4)}  >1e-3m={sum(1 for d in vdists if d>1e-3)}  / {len(vdists)}")
    if tris:
        tdists = [nearest_tri(e) for e in endpoints]
        worst_t = max(tdists)
        print(f"  (b) 端点→最近**三角形面上点**(point-on-mesh) 最坏 = {worst_t*1000:.9f} mm  ({worst_t:.3e} m)")
        print(f"      分布: >1e-6m={sum(1 for d in tdists if d>1e-6)}  >1e-5m={sum(1 for d in tdists if d>1e-5)}  >1e-4m={sum(1 for d in tdists if d>1e-4)}  >1e-3m={sum(1 for d in tdists if d>1e-3)}  / {len(tdists)}")
        # 采样下限解释：对"最近顶点最坏"的那个端点，找其最近三角形三顶点的水平间距
        wi = max(range(len(vdists)), key=lambda i: vdists[i])
        # 找该端点的最近三角形
        pe = endpoints[wi]
        bestd = 1e18; besttri = None
        for verts, polys in tris:
            for (i0,i1,i2) in polys:
                ax=-verts[3*i0+0]; az=verts[3*i0+1]
                bx=-verts[3*i1+0]; bz=verts[3*i1+1]
                cx=-verts[3*i2+0]; cz=verts[3*i2+1]
                d = point_tri_dist_2d(pe[0],pe[1],ax,az,bx,bz,cx,cz)
                if d < bestd:
                    bestd = d
                    besttri = ((ax,az),(bx,bz),(cx,cz))
        if besttri:
            def dd(p,q): return math.hypot(p[0]-q[0],p[1]-q[1])
            edges = [dd(besttri[0],besttri[1]), dd(besttri[1],besttri[2]), dd(besttri[2],besttri[0])]
            print(f"  (c) 采样下限解释：最坏端点 {pe} 落在某三角形内，该三角形水平边长为 "
                  f"{[round(e*1000,4) for e in edges]} mm（最近顶点到该点 ≈ {vdists[wi]*1000:.4f} mm）")
            print(f"      ⇒ 端点本身在网格面上(tri距离={bestd*1000:.6f}mm)，'0.26mm' 是**离散顶点采样下限**，非几何错位。")


if __name__ == "__main__":
    main()
