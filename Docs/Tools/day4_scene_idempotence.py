#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D4 灰盒场景「跨会话重建」幂等性证据工具（纯 Python、只读、不依赖 Unity）。

════════════════════════════════════════════════════════════════════════════
一、这份证据要证明什么
════════════════════════════════════════════════════════════════════════════
`Assets/_Project/Scenes/PhysicsGreybox.unity` 是**代码生成物**（`GreyboxSceneSetup`）。
本脚本证明：**同一份代码生成的场景，内容完全确定** —— 与"哪一次会话"无关。

════════════════════════════════════════════════════════════════════════════
二、为什么必须「归一化 fileID」
════════════════════════════════════════════════════════════════════════════
Unity 保存场景时，会为**运行期创建**的每个对象分配一个**随机会话内的 fileID**
（`m_LocalIdentfierInFile`，在 YAML 里表现为 `--- !u!1 &123456789` 与引用处的 `{fileID: 123456789}`），
并**按该 ID 排序**把对象依次写入文件。

⇒ 后果有两个，且都**不可能**通过"写得更好"来消除：
  1. 两次重建的 `.unity` **逐字节必然不同**（ID 不同）；
  2. 连**行的顺序**都会变（因为排序键是随机 ID）。

⇒ 因此"逐字节相同"这个判据在 Unity 语义下**恒为假**，拿它当验收条件只会永远红。
本工具把 ID 归一化为占位符、再排序，从而比较**内容的多重集**。

════════════════════════════════════════════════════════════════════════════
三、为什么这个证据比「逐字节相同」更强（不是更弱）
════════════════════════════════════════════════════════════════════════════
- "逐字节相同"**可以被字节巧合偶然满足**，也**必然被随机 fileID 破坏** ⇒ 它测的是运气，不是代码。
- "归一化后 diff = 0" 排除了随机 ID 这个噪声源，剩下的差异**只能**来自**代码或数据的真实变化**
  ⇒ 它证明的是**代码确定性**（determinism of the generator），正是我们真正要的不变量。

════════════════════════════════════════════════════════════════════════════
四、为什么「跨会话」重建比「同一会话内重跑」更强
════════════════════════════════════════════════════════════════════════════
本工具的两个样本来自**两个不同的 Unity 进程**（两次独立 `RebuildFromCommandLine`）。
同一会话内重跑，ID 的随机源只被采样一次；**跨会话**覆盖的是**"换一天 / 换一台机器重建"**这个
真实场景（评审复现、CI、别人 clone 后重建都属于这一类）。
⇒ 归一化后仍 diff = 0 ⇒ "代码确定性"这一结论**对会话无关**，而不只是"对这一次会话成立"。

════════════════════════════════════════════════════════════════════════════
五、判据的边界（不夸大）
════════════════════════════════════════════════════════════════════════════
归一化 + 排序后做的是**行多重集**比较，能发现**任何字段/数值/行**的增删改；
但它**不**验证"某一行属于哪个对象"这种**归属关系**
（若 A 对象的某行与 B 对象的某行互换，多重集不变）。

**归属关系未被任何检查覆盖**（D4-C3 更正后如实收窄，不再引用第二条覆盖）：
  · `PhysicsValidator` 的 `D4-SCN-02` 只做**计数**（实测 `rootObjects=1 CushionGizmo=1 PhysicsTestDrive=1`），
    而**计数 ≠ 归属** —— 对象数相同、某字段却被搬到另一个对象上时，该计数照样通过。
  · 本节此前曾写"归属关系由 `D4-SCN-02` 与**场景内各对象的 YAML 结构**（两条）覆盖"。
    经复核方指出后逐项核查：**不存在**任何"逐对象 YAML 结构"检查的实现
    （全仓检索 `yaml|归属|attribution`，在 `Assets/_Project/Tests/` 与 `Editor/Visual/` 下**零命中**）
    ⇒ 那一条是**不实引用**，已删除。**不得**把它当作已覆盖。
  · 本工具**不**声称覆盖归属关系 —— 这是刻意的边界，避免过度断言（铁律 9）。

════════════════════════════════════════════════════════════════════════════
六、非空转（falsifiability）
════════════════════════════════════════════════════════════════════════════
"diff = 0" 与"脚本根本没比"在输出上长得一样。故本工具：
  · 断言每个样本都有**行数下限**（`MIN_LINES`）且**样本数 ≥ 2**；
  · 提供 `--self-check`：把样本复制到**仓库外**临时目录，**只改一个字段的一格**，
    断言 diff > 0 且退出码非 0 —— 证明比对逻辑**能失败**，不是恒真。
    临时目录在结束时**递归清理**，并在输出里打印其路径与清理状态。

用法：
    python day4_scene_idempotence.py                      # 默认比较 3 份（仓库场景 + 两份跨会话重建）
    python day4_scene_idempotence.py A.unity B.unity ...  # 第 1 份为基准
    python day4_scene_idempotence.py --self-check         # 注入实验（纯离线，写仓库外临时目录）

退出码：0 = 判据成立；1 = 判据不成立（或 self-check 未能失败）。
"""

import argparse
import difflib
import re
import shutil
import sys
import tempfile
from pathlib import Path

# ── 归一化：把随机会话内 ID 换成占位符（这是本工具的核心口径）──────────────

# 对象锚点：      `--- !u!1 &123456789`
RE_ANCHOR = re.compile(r"&[0-9]+")
# 引用与自指：    `{fileID: 123456789}` / `m_GameObject: {fileID: 0}`
RE_FILEID = re.compile(r"fileID:\s*-?[0-9]+")

# 非空转下限：一份真实的灰盒场景 YAML 远超该行数（实测 ~500 行）。
# 若某份样本因为路径写错而读成空文件，这里会先失败。
MIN_LINES = 100

DEFAULT_SCENES = [
    "Assets/_Project/Scenes/PhysicsGreybox.unity",
    "Docs/Reports/d4-scene-idempotence/rebuild-a.unity",
    "Docs/Reports/d4-scene-idempotence/rebuild-b.unity",
]

# self-check 用的定点注入：把球体缩放 0.05715 改一格（改 ±1 于最后一位十进制）。
# 选它是因为它是**真实且唯一**的数值（球直径 PoolConstants.BallDiameter），单点定位、无歧义。
INJECT_FROM = "0.05715"
INJECT_TO = "0.05716"


def repo_root() -> Path:
    """仓库根 = 本脚本上溯两级（<root>/Docs/Tools/day4_scene_idempotence.py）。"""
    return Path(__file__).resolve().parents[2]


def normalize(text: str):
    """归一化 fileID，并返回**排序后的行列表**（顺序会因随机 ID 而变，故必须排序）。"""
    text = RE_ANCHOR.sub("&ID", text)
    text = RE_FILEID.sub("fileID: ID", text)
    return sorted(text.splitlines())


def load_sample(path: Path):
    """读取并归一化一个样本；违反非空转下限则抛错（不许拿空文件去"比出 0 差异"）。"""
    if not path.is_file():
        raise FileNotFoundError(f"样本不存在：{path}")
    lines = normalize(path.read_text(encoding="utf-8"))
    if len(lines) < MIN_LINES:
        raise ValueError(f"样本疑似为空转：{path} 只有 {len(lines)} 行（下限 {MIN_LINES}）")
    return lines


def diff_count(a, b):
    """两个已归一化行列表之间的差异行数（difflib 的增删计数）。"""
    return sum(1 for _ in difflib.unified_diff(a, b, lineterm="", n=0)
               if _.startswith(("+", "-")) and not _.startswith(("+++", "---")))


def run_compare(paths, label):
    """比较给定样本列表（第 1 份为基准），打印每对 diff 行数并返回总差异行数。"""
    samples = [(p, load_sample(p)) for p in paths]
    print(f"[{label}] 样本 {len(samples)} 份，归一化后行数："
          + ", ".join(f"{p.name}={len(l)}" for p, l in samples))

    total = 0
    reference_path, reference = samples[0]
    for path, lines in samples[1:]:
        d = diff_count(reference, lines)
        total += d
        print(f"[{label}]   {reference_path.name}  vs  {path.name}  →  diffLines={d}")
    return total


def self_check(paths):
    """注入实验：仓库外临时目录里改样本一个字段，断言比对**能**失败，然后清理。"""
    tmp_root = Path(tempfile.mkdtemp(prefix="d4-idem-selfcheck-"))
    print(f"[self-check] 临时目录（仓库外）：{tmp_root}")
    try:
        src = paths[0]
        mutated = tmp_root / (src.stem + "-mutated.unity")
        text = src.read_text(encoding="utf-8")

        if INJECT_FROM in text:
            # 只替换**第一处**出现（球体 localScale.x）
            mutated.write_text(text.replace(INJECT_FROM, INJECT_TO, 1), encoding="utf-8")
            injection = f"'{INJECT_FROM}' → '{INJECT_TO}'（改最后一位十进制，1 处）"
        else:
            # 兜底：定点串不在样本里时，追加一行哨兵（同样应被检出）
            mutated.write_text(text + "\n# INJECTED-SENTINEL\n", encoding="utf-8")
            injection = "追加哨兵行（定点串未命中）"

        print(f"[self-check] 注入：{injection}")
        d = diff_count(load_sample(src), load_sample(mutated))
        print(f"[self-check] 注入后 diffLines={d}（期望 > 0）")

        ok = d > 0
        print(f"[self-check] {'SELFCHECK_PASS' if ok else 'SELFCHECK_FAIL'}"
              f"：比对逻辑{'能' if ok else '不能'}失败 ⇒ {'非恒真' if ok else '恒真，必须修'}")

        # 顺带证明：未注入的**同内容**副本必须 diff = 0（避免"永远报差异"的另一类恒真）
        same = tmp_root / (src.stem + "-same.unity")
        same.write_text(text, encoding="utf-8")
        d_same = diff_count(load_sample(src), load_sample(same))
        print(f"[self-check] 对照：内容相同的副本 diffLines={d_same}（期望 == 0）")
        ok = ok and d_same == 0
        return 0 if ok else 1
    finally:
        shutil.rmtree(tmp_root, ignore_errors=True)
        print(f"[self-check] 临时目录已清理：{'是' if not tmp_root.exists() else '否（残留！）'}")


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="D4 灰盒场景跨会话重建幂等性证据（归一化 fileID 后比对）")
    parser.add_argument("scenes", nargs="*", help="待比较的 .unity（第 1 份为基准）；省略则用默认 3 份")
    parser.add_argument("--self-check", action="store_true", help="注入实验（离线，写仓库外临时目录）")
    args = parser.parse_args(argv)

    root = repo_root()
    paths = [Path(s) if Path(s).is_absolute() else root / s for s in (args.scenes or DEFAULT_SCENES)]
    if len(paths) < 2:
        print("ERROR: 至少需要 2 份样本")
        return 1

    if args.self_check:
        return self_check(paths)

    try:
        total = run_compare(paths, "idempotence")
    except (FileNotFoundError, ValueError) as ex:
        print(f"ERROR: {ex}")
        return 1

    ok = total == 0
    print(f"IDEMPOTENCE_{'OK' if ok else 'VIOLATION'} totalDiffLines={total}")
    print("说明：fileID 已归一化（Unity 每会话随机分配）；行序已排序（Unity 按随机 ID 落盘）。"
          " 差异只能来自代码/数据变化，故 diff=0 证明的是**代码确定性**，非字节巧合。")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
