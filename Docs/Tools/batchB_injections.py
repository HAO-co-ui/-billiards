"""批次 B 的**注入式反例**（7 条负对照；全部只需 EditMode，不需重生成产物）。

用法：
  python Docs/Tools/batchB_injections.py apply     # 备份 + 注入 7 处
  python Docs/Tools/batchB_injections.py restore   # 从备份逐字节还原
  python Docs/Tools/batchB_injections.py verify    # 校验与备份逐字节一致

注入清单（每条对应 `T131` 的一个子检查）：
  g3-constant    `AimSweepDegreesPerFrame` 4.5 → **0.5625**（缩 8 倍）⇒ ② 红
                 ⚠ **这条是本批 G3 的核心证据**：旧写法（拿常量自身当期望）在同样注入下**仍绿**。
  g4-fraction    `DroppedFractionNumerator` 2 → **3** ⇒ ④ 红
  g5-peaks       去掉 `SampleScenePeaks()` 调用（退回"只在结束时取一次快照"）⇒ ⑤ 红
  g6-report      去掉 `AimAssistController` 里的 `ReportPredictionCost(...)` ⇒ ⑤ 红
  gate-back      把已删除的 `MeetsDragTarget` **加回来** ⇒ ⑤ 红（源码级 + 类型级）
  machine-class  `LoadRatioCeiling` 1.35 → **10** ⇒ ⑦ 红（忙机形态会被误判为 premium）
  g6-accumulate  删 `AimAssistController.cs` 的 `TotalPredictSteps += …`（**保留属性声明**）⇒ ⑤红
                 ⚠ 它验的是"**守卫必须写累计使用形态**"：旧守卫 `Contains("TotalPredictSteps")` 命中的是**声明**
                 （`AimAssistController.cs:136` `public long TotalPredictSteps { get; private set; }`）⇒ 删累计行仍绿；
                 **同型空转**由**复核**注入证实、本批修（改为 `Contains("TotalPredictSteps +=")`）
"""

from __future__ import annotations

import hashlib
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BACKUP = os.environ.get("INJECT_BACKUP", os.path.join(ROOT, "_inject_backup_batchB"))

SMOKE = "Assets/_Project/Scripts/Runtime/Core/SmokeProbe.cs"
RUNNER = "Assets/_Project/Scripts/Runtime/Core/SmokeProbeRunner.cs"
ASSIST = "Assets/_Project/Scripts/Runtime/Physics/AimAssistController.cs"

TOJSON_DOC = "        /// <summary>把一次冒烟采样的结果序列化为 JSON（纯函数，字段顺序固定，数字用不变文化）。</summary>"

PATCHES = [
    ("g3-constant", SMOKE,
     "public const float AimSweepDegreesPerFrame = 4.5f;",
     "public const float AimSweepDegreesPerFrame = 0.5625f;"),
    ("g4-fraction", SMOKE,
     "public const int DroppedFractionNumerator = 2;",
     "public const int DroppedFractionNumerator = 3;"),
    ("g5-peaks", RUNNER,
     "            SampleScenePeaks();\n",
     ""),
    ("g6-report", ASSIST,
     "            Billiards.Core.SmokeProbe.ReportPredictionCost(PredictionComputeCount, TotalPredictSteps);\n",
     ""),
    ("gate-back", SMOKE,
     TOJSON_DOC,
     "        /// <summary>[注入] 已删除的死闸门（负对照用）。</summary>\n"
     "        public static bool MeetsDragTarget(float meanMs, int droppedOverBudget, int frames)\n"
     "        {\n"
     "            return frames > 0 && meanMs <= 50f;\n"
     "        }\n\n" + TOJSON_DOC),
    ("machine-class", SMOKE,
     "public const float LoadRatioCeiling = 1.35f;",
     "public const float LoadRatioCeiling = 10f;"),
    # A2（复核新提，与 A1 同型）：删掉**累计行**、**保留属性声明**（:136）
    # ⇒ 旧守卫 `Contains("TotalPredictSteps")` 会命中声明 ⇒ **恒真**；改调用形态后此注入必须红。
    ("g6-accumulate", ASSIST,
     "            TotalPredictSteps += _prediction.StepsSimulated;\n",
     ""),
]

FILES = [SMOKE, RUNNER, ASSIST]


def full(rel: str) -> str:
    return os.path.join(ROOT, rel)


def sha1(path: str) -> str:
    return hashlib.sha1(open(path, "rb").read()).hexdigest()


def eol_of(text: str) -> str:
    return "\r\n" if "\r\n" in text else "\n"


def apply() -> None:
    os.makedirs(BACKUP, exist_ok=True)
    for rel in FILES:
        shutil.copy2(full(rel), os.path.join(BACKUP, rel.replace("/", "__")))

    for tag, rel, old, new in PATCHES:
        path = full(rel)
        text = open(path, "rb").read().decode("utf-8")
        o = old.replace("\n", eol_of(text))
        n = new.replace("\n", eol_of(text))
        count = text.count(o)
        if count != 1:
            raise SystemExit(f"注入 [{tag}] 未命中/不唯一（count={count}）：{rel}")
        open(path, "wb").write(text.replace(o, n).encode("utf-8"))
        print(f"injected [{tag}] {rel}")


def restore() -> None:
    for rel in FILES:
        src = os.path.join(BACKUP, rel.replace("/", "__"))
        if not os.path.exists(src):
            raise SystemExit("备份缺失，无法还原：" + src)
        shutil.copy2(src, full(rel))
        print(f"restored {rel}  sha1={sha1(full(rel))}")


def verify() -> None:
    bad = 0
    for rel in FILES:
        src = os.path.join(BACKUP, rel.replace("/", "__"))
        a, b = sha1(src), sha1(full(rel))
        ok = a == b
        bad += 0 if ok else 1
        print(f"{'OK  ' if ok else 'DIFF'} {rel}")
    raise SystemExit(1 if bad else 0)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "apply":
        apply()
    elif cmd == "restore":
        restore()
    elif cmd == "verify":
        verify()
    else:
        raise SystemExit("用法：batchB_injections.py apply|restore|verify")
