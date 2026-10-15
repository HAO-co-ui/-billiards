"""注入补丁工具：把某档注入写进指定文件（原地改写，还原由调用方的 cp 备份负责）。

用法：python Docs/Tools/feel_fix_patch.py <v1|v2|v3|v4> <目标文件>
为什么单独成文件：注入片段里含 `<<'PY'` 之类的字面量时，套在 shell heredoc 里会因同名分隔符提前结束。
"""
import io
import sys


def apply(path, old, new):
    s = io.open(path, encoding="utf-8", newline="").read()
    if old not in s:
        o = old.replace("\n", "\r\n")
        n = new.replace("\n", "\r\n")
        if o not in s:
            raise SystemExit("anchor not found: " + old[:70])
        s = s.replace(o, n, 1)
    else:
        s = s.replace(old, new, 1)
    io.open(path, "w", encoding="utf-8", newline="").write(s)
    print("injected", sys.argv[1], "->", path)


MODE, PATH = sys.argv[1], sys.argv[2]

if MODE == "v1":
    # 抬升角退回固定 4°：球在库边附近时杆身从木库边中穿过（用户报的穿模原状）
    apply(PATH,
          """            Vector2 back = -aimPhysics.normalized;
            float d = ExitDistanceToTableEdge(ballPhysics, back);""",
          """            return MinStickElevationDegrees;
            Vector2 back = -aimPhysics.normalized;
            float d = ExitDistanceToTableEdge(ballPhysics, back);""")
elif MODE == "v2":
    # 入袋动画退回"原地垂直下沉"（用户报的"贴着库边慢慢往下滑"原状）
    apply(PATH,
          """            PocketSinkPose(progress, out float yOffset, out _);
            float s = PocketSinkSlide(progress);
            return new Vector3(
                Mathf.Lerp(originLocal.x, holeLocal.x, s),
                originLocal.y + yOffset,
                Mathf.Lerp(originLocal.z, holeLocal.z, s));""",
          """            PocketSinkPose(progress, out float yOffset, out _);
            return originLocal + new Vector3(0f, yOffset, 0f);""")
elif MODE == "v3":
    # 静止补判半径放宽（2 倍）：边界外的静止球也被补判
    apply(PATH,
          """                    if (dx * dx + dy * dy > _pocketOpeningRadius[p] * _pocketOpeningRadius[p])
                    {
                        continue;
                    }

                    RecordContactEvent(ContactKind.Pocket, Length(_balls[i].velocity), c);""",
          """                    if (dx * dx + dy * dy > 4f * _pocketOpeningRadius[p] * _pocketOpeningRadius[p])
                    {
                        continue;
                    }

                    RecordContactEvent(ContactKind.Pocket, Length(_balls[i].velocity), c);""")
elif MODE == "v4":
    # 洞口中心不夹到台面内（直接用喉心）：表现层落点会指到台面之外
    apply(PATH,
          """                _pocketHoleCenter[i] = new Vector2(
                    Mathf.Clamp(p.throatCenter.x, -PoolConstants.ClothHalfLength, PoolConstants.ClothHalfLength),
                    Mathf.Clamp(p.throatCenter.y, -PoolConstants.ClothHalfWidth, PoolConstants.ClothHalfWidth));""",
          """                _pocketHoleCenter[i] = p.throatCenter;""")
else:
    raise SystemExit("unknown mode " + MODE)
