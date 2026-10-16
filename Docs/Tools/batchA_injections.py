"""批次 A 的**注入式反例**（生成物类判据：改码 → 重生成 → 断言）。

用法：
  python Docs/Tools/batchA_injections.py inject-scale100     # 根缩放 1.2 → 1.0
  python Docs/Tools/batchA_injections.py inject-scale144     # 根缩放 1.2 → 1.44
  python Docs/Tools/batchA_injections.py inject-roomcam       # Room 摘出缩放根 + 相机 fov 52 → 40
  python Docs/Tools/batchA_injections.py restore              # 从备份逐字节还原源码
  python Docs/Tools/batchA_injections.py verify               # 校验源码是否与备份逐字节一致

⚠ 这些注入改的是**生成器源码** ⇒ 断言前必须**重跑生成**（`unity_batch.sh rebuild-main-base`）；
   **还原之后也要重新生成**，否则注入会留在产物（`Main.unity`）里。
"""

from __future__ import annotations

import hashlib
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BACKUP = os.environ.get("INJECT_BACKUP", os.path.join(ROOT, "_inject_backup_batchA"))

SETUP = "Assets/_Project/Scripts/Editor/Visual/MaterialVisualSetup.cs"

SCALE_OLD = "playfield.transform.localScale = Vector3.one * PoolConstants.PlayfieldVisualScale;"
ROOM_OLD = "TableVisualBuilder.AttachRoom(playfield.transform);"
FOV_OLD = "cam.fieldOfView = GreyboxSceneSetup.CameraBaseVerticalFovDeg;"

CAM_B_OLD = ("camGo.transform.position = GreyboxSceneSetup.CameraPosition;\n"
             "            camGo.transform.rotation = Quaternion.LookRotation(\n"
             "                GreyboxSceneSetup.CameraLookTarget - GreyboxSceneSetup.CameraPosition, Vector3.up);")
CAM_A_NEW = ("camGo.transform.position = new Vector3(0f, 3.05f, -0.88f);\n"
             "            camGo.transform.rotation = Quaternion.LookRotation(\n"
             "                Vector3.zero - new Vector3(0f, 3.05f, -0.88f), Vector3.up);")

INJECTIONS = {
    "inject-scale100": [(SETUP, SCALE_OLD, "playfield.transform.localScale = Vector3.one * 1.0f;")],
    "inject-scale144": [(SETUP, SCALE_OLD, "playfield.transform.localScale = Vector3.one * 1.44f;")],
    "inject-roomcam": [
        (SETUP, ROOM_OLD, "TableVisualBuilder.AttachRoom(playfield.transform.parent);"),
        (SETUP, FOV_OLD, "cam.fieldOfView = 40f;"),
    ],
    # 配置 (A)：保留 Main 原来的高机位 (0,3.05,−0.88) + 玩法 fov 52 —— 只改**一边**的取景
    # ⇒ 预期 `CAM-04`（占比变小）与 `CAM-05`（两场景取景不一致）红，`CAM-01`（≠ 共用常量）也红。
    "inject-configA": [(SETUP, CAM_B_OLD, CAM_A_NEW)],
}


def full(rel: str) -> str:
    return os.path.join(ROOT, rel)


def sha1(path: str) -> str:
    return hashlib.sha1(open(path, "rb").read()).hexdigest()


def eol_of(text: str) -> str:
    return "\r\n" if "\r\n" in text else "\n"


def backup() -> None:
    os.makedirs(BACKUP, exist_ok=True)
    dst = os.path.join(BACKUP, SETUP.replace("/", "__"))
    if not os.path.exists(dst):
        shutil.copy2(full(SETUP), dst)
        print("backed up", SETUP)


def inject(name: str) -> None:
    backup()
    for rel, old, new in INJECTIONS[name]:
        path = full(rel)
        text = open(path, "rb").read().decode("utf-8")
        o = old.replace("\n", eol_of(text))
        n = new.replace("\n", eol_of(text))
        count = text.count(o)
        if count != 1:
            raise SystemExit(f"注入 [{name}] 未命中/不唯一（count={count}）：{rel}\n  目标：{o}")
        open(path, "wb").write(text.replace(o, n).encode("utf-8"))
        print(f"injected [{name}] {rel}")


def restore() -> None:
    src = os.path.join(BACKUP, SETUP.replace("/", "__"))
    if not os.path.exists(src):
        raise SystemExit("备份缺失，无法还原：" + src)
    shutil.copy2(src, full(SETUP))
    print("restored", SETUP, "sha1=", sha1(full(SETUP)))


def verify() -> None:
    src = os.path.join(BACKUP, SETUP.replace("/", "__"))
    ok = sha1(src) == sha1(full(SETUP))
    print(("OK  " if ok else "DIFF"), SETUP, sha1(full(SETUP))[:12])
    raise SystemExit(0 if ok else 1)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd in INJECTIONS:
        inject(cmd)
    elif cmd == "restore":
        restore()
    elif cmd == "verify":
        verify()
    else:
        raise SystemExit("用法：batchA_injections.py inject-scale100|inject-scale144|inject-roomcam|restore|verify")
