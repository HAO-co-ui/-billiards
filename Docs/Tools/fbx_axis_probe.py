"""量一个 FBX 的**局部包围盒**与各轴尺寸（离线，绕过 Unity）。

用途：判定"杆尖 = 局部原点、杆身沿 +Z、长 1.4 m"这条约定在**导入到 Unity 之后**是否成立。
实测起因：判据 `T123` 报「杆长 0.0319 m ≠ 1.4 m、横向 1.4 m」⇒ 长轴不在 Z 上。
必须先**量出来**长轴到底是哪一根、指向哪个方向，才能决定实例化时要补哪个旋转
（猜错方向会把杆装反，实机上看到的是"球杆倒着打"）。

复用 `fbx_mesh_probe.py` 的 FBX 二进制解析器（同一套 Node 解析，不另写一份）。

用法：<managed venv python> Docs/Tools/fbx_axis_probe.py [<fbx 路径>]
"""

from __future__ import annotations

import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fbx_mesh_probe import iter_nodes, parse_fbx  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_FBX = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Cue", "PoolCue_TipOrigin.fbx")


def main() -> None:
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_FBX
    _version, root = parse_fbx(path)

    vertices: list[tuple[float, float, float]] = []
    geometries = [node for node in iter_nodes(root, "Geometry")]
    for geometry in geometries:
        for child in geometry.children:
            if child.name != "Vertices":
                continue
            data = child.props[0]
            for i in range(0, len(data), 3):
                vertices.append((data[i], data[i + 1], data[i + 2]))

    # 局部变换（Lcl Translation / Rotation / Scaling）—— FBX 里模型常把朝向写在这里
    models = [node for node in iter_nodes(root, "Model")]
    print(f"文件：{os.path.relpath(path, ROOT)}")
    print(f"Geometry 节点 {len(geometries)} 个；顶点 {len(vertices)}")
    for model in models:
        for child in model.children:
            if child.name in ("Lcl Translation", "Lcl Rotation", "Lcl Scaling", "PreRotation"):
                print(f"  Model.{child.name} = {[round(v, 6) for v in child.props]}")
            if child.name == "UpAxis" or child.name == "UpAxisSign":
                print(f"  {child.name} = {child.props}")

    for node in iter_nodes(root, "GlobalSettings"):
        for child in node.children:
            if child.name in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign"):
                print(f"  GlobalSettings.{child.name} = {child.props}")

    if not vertices:
        print("没有顶点数据。")
        return

    xs = [v[0] for v in vertices]
    ys = [v[1] for v in vertices]
    zs = [v[2] for v in vertices]
    mins = (min(xs), min(ys), min(zs))
    maxs = (max(xs), max(ys), max(zs))
    sizes = tuple(maxs[i] - mins[i] for i in range(3))

    print()
    print(f"原始局部 bbox  min=({mins[0]:.6f}, {mins[1]:.6f}, {mins[2]:.6f})")
    print(f"                max=({maxs[0]:.6f}, {maxs[1]:.6f}, {maxs[2]:.6f})")
    print(f"各轴尺寸        X={sizes[0]:.6f}  Y={sizes[1]:.6f}  Z={sizes[2]:.6f}  (m)")
    long_axis = max(range(3), key=lambda i: sizes[i])
    print(f"长轴 = {'XYZ'[long_axis]}（{sizes[long_axis]:.6f} m）；"
          f"杆尖所在端 = -{'XYZ'[long_axis]} 侧（min={mins[long_axis]:.6f}）")


if __name__ == "__main__":
    main()
