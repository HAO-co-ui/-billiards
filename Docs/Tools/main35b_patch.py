"""35-B 负对照：注入 / 逐字节还原（cp 备份 + 精确替换 + sha1 校验）。

用法：
  python Docs/Tools/main35b_patch.py apply     # 备份 + 注入（改 4 处源码）
  python Docs/Tools/main35b_patch.py restore   # 从备份逐字节还原（含场景文件）
  python Docs/Tools/main35b_patch.py verify    # 校验当前源码/场景是否与备份逐字节一致（还原后应一致）

为什么用 python 而不是 `rm`/`sed`：本机命令守卫拒绝带盘符路径的 `rm`，且 `rm … && run` 会**整条静默短路**
（本工程已因此误读过一次结果）。备份与还原一律走本脚本，跑完用 `verify` 逐字节核对。

注入的四处（**生成物类注入必须"改码 → 重跑生成 → 断言"**）：
  · `overlay-default`：`showDebugOverlay` 默认值 false → true ⇒ `T129 ①` 红；
  · `trailing-edge` ：删掉 `ShouldRecompute` 的尾沿分支 ⇒ `T130 ①/②` 红；
  · `sweep-step`    ：`AimSweepDegreesPerFrame` 4.5 → 0 ⇒ `T131 ②` 红；
  · `rack-count`    ：`BuildRackVisuals` 只返回白球 ⇒（**重跑 `rebuild-main` 生成后**）`T127 ③` 红。
"""

from __future__ import annotations

import hashlib
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BACKUP = os.environ.get("INJECT_BACKUP", os.path.join(ROOT, "_inject_backup_35b"))

DRIVE = "Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs"
CONTROLLER = "Assets/_Project/Scripts/Runtime/Physics/AimAssistController.cs"
SMOKE = "Assets/_Project/Scripts/Runtime/Core/SmokeProbe.cs"
GENERATOR = "Assets/_Project/Scripts/Editor/Visual/GreyboxSceneSetup.cs"
MAIN_SCENE = "Assets/_Project/Scenes/Main.unity"

PATCHES = [
    ("overlay-default", DRIVE,
     "public bool showDebugOverlay = false;",
     "public bool showDebugOverlay = true;"),
    ("trailing-edge", CONTROLLER,
     "            if (settledSinceLastFrame)\n            {\n                return true;\n            }\n",
     ""),
    ("sweep-step", SMOKE,
     "public const float AimSweepDegreesPerFrame = 4.5f;",
     "public const float AimSweepDegreesPerFrame = 0f;"),
    ("rack-count", GENERATOR,
     "            return visuals;",
     "            return new[] { cueBall.transform };"),
]

FILES = [DRIVE, CONTROLLER, SMOKE, GENERATOR, MAIN_SCENE]


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
            raise SystemExit(f"注入 [{tag}] 目标不唯一或未命中（count={count}）：{rel}")
        open(path, "wb").write(text.replace(o, n).encode("utf-8"))
        print(f"injected [{tag}] {rel}")
    print("backup dir:", BACKUP)


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
        a = sha1(src)
        b = sha1(full(rel))
        ok = a == b
        bad += 0 if ok else 1
        print(f"{'OK  ' if ok else 'DIFF'} {rel}  backup={a[:12]} now={b[:12]}")
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
        raise SystemExit("用法：main35b_patch.py apply|restore|verify")
