# 场景语义等价评审器（scenes-as-artifacts 的评审仪器）
#
# 用途：**生成场景**的评审。生成器会重建场景，逐行 diff 充满 fileID/guid 漂移（噪声），
# 但**语义**往往只差目标字段 ⇒ 本工具按 `--- !u!` 把场景解构为文档多重集合、剥掉实例引用
# 与顺序，只比**语义**，并报"语义差异块数"。
# 用法：python Docs/Tools/scene_semantic_diff.py <HEAD|base 路径> <工作区路径>
# 评审口径：**语义差异块数 == 预期被改动的字段数**（例如本单 = 1：`maxLaunchSpeed`）。
# 它是只读仪器（不改任何文件）；与 aimcost_probe 同范式：**源码入库、产物不入库**。
#
"""场景"语义等价"核验：把 HEAD 版与工作区版的 Unity 场景做**规范化解构**后比较。

目的：生成器重建场景会让 fileID 漂移、YAML 行顺序变化 ⇒ 行级 diff 无法评审。
本脚本剥掉一切"实例引用"（fileID / guid / PrefabInstance 覆盖段），把每个 !u! 文档
规范化为 (class, 排序后的 key: value 集合)，再比两份文档**多重集合**的差。
输出 = 语义上有差异的文档清单（理想结果：只有目标字段那一条）。

只读：不写任何文件。
"""
import collections
import os
import re
import subprocess
import sys

ROOT = r"E:\Unity_Deve\Unity_Project\Billiards game"
REV = "HEAD"
SCENES = [
    "Assets/_Project/Scenes/PhysicsGreybox.unity",
    "Assets/_Project/Scenes/Main.unity",
]

DOC_HEAD = re.compile(r"^--- !u!(\d+) &(\d+)")


def read_head(rel):
    out = subprocess.run(["git", "show", f"{REV}:{rel}"], cwd=ROOT, capture_output=True)
    if out.returncode != 0:
        raise SystemExit(f"git show 失败：{rel}\n{out.stderr.decode('utf-8', 'replace')}")
    return out.stdout.decode("utf-8", "replace")


def read_work(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as handle:
        return handle.read()


def decompose(text):
    docs = []
    current = None
    for line in text.splitlines():
        if DOC_HEAD.match(line):
            current = []
            docs.append(current)
            continue
        if current is not None:
            current.append(line)
    return docs


def normalize(lines):
    """剥掉实例引用，保留类声明与标量字段（顺序无关）。"""
    kept = []
    for line in lines:
        if "fileID:" in line or "guid:" in line:
            continue
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        kept.append(stripped)
    return tuple(sorted(kept))


def signature(lines):
    """给文档一个人类可读的标签：优先 m_Name，其次第一条保留行。"""
    names = [ln.strip() for ln in lines if ln.strip().startswith("m_Name:")]
    if names:
        return names[0]
    kept = normalize(lines)
    return kept[0] if kept else "(空文档)"


def compare(rel):
    old_docs = decompose(read_head(rel))
    new_docs = decompose(read_work(rel))
    print(f"\n=== {rel} ===")
    print(f"  HEAD 文档数 = {len(old_docs)}   工作区文档数 = {len(new_docs)}")

    old_counter = collections.Counter(normalize(d) for d in old_docs)
    new_counter = collections.Counter(normalize(d) for d in new_docs)

    print(f"  规范化后不同的文档块数 = {len(old_counter - new_counter) + len(new_counter - old_counter)}")
    if old_counter == new_counter:
        print("  ⇒ 语义完全等价（除被剥掉的实例引用外无任何差异）")
        return True

    sig_old = {}
    for d in old_docs:
        sig_old.setdefault(normalize(d), signature(d))
    sig_new = {}
    for d in new_docs:
        sig_new.setdefault(normalize(d), signature(d))

    shown = 0
    for key in sorted(set(old_counter) | set(new_counter), key=lambda k: str(k)):
        delta = new_counter[key] - old_counter[key]
        if delta == 0:
            continue
        shown += 1
        if shown > 40:
            print("  …（差异块超过 40，已截断）")
            break
        tag = sig_new.get(key) or sig_old.get(key)
        side = "新增/多出" if delta > 0 else "消失/减少"
        print(f"  [{side} ×{abs(delta)}] {tag}")
        print(f"      行: {' | '.join(key)}")
    print(f"  ⇒ 语义差异块总数 = {shown}")
    return False


def main():
    ok = True
    for rel in SCENES:
        ok &= compare(rel)
    print("\n结论：" + ("两份场景都与 HEAD 语义等价" if ok else "存在语义差异（见上）"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
