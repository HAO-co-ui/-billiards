"""生成台球项目的 Input Actions 资产。

设计取舍：
- 只替换 Unity URP 模板中与本项目无关的 "Player" 映射，保留 "UI" 映射原样。
  原因：InputSystemUIInputModule（UGUI 的输入模块）依赖 UI 映射，删掉会导致
  后续 HUD 无法交互。保留可直接复用。
- 保留模板的 controlSchemes 定义（Keyboard&Mouse / Gamepad / Touch / Joystick / XR）。
  它们只是元数据，保留零风险；本项目的绑定只挂 ;Keyboard&Mouse。
- 输出写回原路径（保留 .meta 与 GUID），随后由 Editor 工具用 AssetDatabase.MoveAsset
  迁移到 Assets/_Project/Settings/Input/，迁移后 GUID 不变，项目级输入引用不会失效。

用法：
  python generate_input_actions.py
"""
import json
import os
import sys
import uuid

ROOT = r"E:\Unity_Deve\Unity_Project\Billiards game"
TEMPLATE = os.path.join(ROOT, "Assets", "InputSystem_Actions.inputactions")

GAMEPLAY_GROUP = ";Keyboard&Mouse"

# (动作名, 类型, expectedControlType, interaction, [(路径, processors)])
GAMEPLAY_ACTIONS = [
    (
        "PointerPosition", "Value", "Vector2", "",
        [("<Pointer>/position", "")],
    ),
    (
        "AimLock", "Button", "Button", "",
        [("<Mouse>/leftButton", "")],
    ),
    (
        "AimNudge", "Value", "Axis", "",
        [
            ("<Keyboard>/rightArrow", ""),
            ("<Keyboard>/leftArrow", "invert"),
            ("<Keyboard>/d", ""),
            ("<Keyboard>/a", "invert"),
        ],
    ),
    (
        "PowerAdjust", "Value", "Axis", "",
        [
            ("<Keyboard>/upArrow", ""),
            ("<Keyboard>/downArrow", "invert"),
            ("<Mouse>/scroll/y", ""),
        ],
    ),
    (
        "Shoot", "Button", "Button", "",
        [("<Keyboard>/space", "")],
    ),
    (
        "CancelAim", "Button", "Button", "",
        [("<Mouse>/rightButton", ""), ("<Keyboard>/escape", "")],
    ),
    (
        "ToggleAimGuide", "Button", "Button", "",
        [("<Keyboard>/tab", "")],
    ),
    (
        "RestartRack", "Button", "Button", "",
        [("<Keyboard>/r", "")],
    ),
]


def build_gameplay_map():
    actions = []
    bindings = []
    for name, action_type, expected, interaction, paths in GAMEPLAY_ACTIONS:
        actions.append({
            "name": name,
            "type": action_type,
            "id": str(uuid.uuid4()),
            "expectedControlType": expected,
            "processors": "",
            "interactions": interaction,
            "initialStateCheck": action_type == "Value",
        })
        for path, processors in paths:
            bindings.append({
                "name": "",
                "id": str(uuid.uuid4()),
                "path": path,
                "interactions": "",
                "processors": processors,
                "groups": GAMEPLAY_GROUP,
                "action": name,
                "isComposite": False,
                "isPartOfComposite": False,
            })

    return {
        "name": "Gameplay",
        "id": str(uuid.uuid4()),
        "actions": actions,
        "bindings": bindings,
    }


def main():
    with open(TEMPLATE, "r", encoding="utf-8") as fh:
        template = json.load(fh)

    ui_map = next((m for m in template["maps"] if m["name"] == "UI"), None)
    if ui_map is None:
        print("ERROR: 模板中未找到 UI 映射，无法安全替换。", file=sys.stderr)
        return 1

    asset = {
        "name": "Billiards",
        "maps": [build_gameplay_map(), ui_map],
        "controlSchemes": template["controlSchemes"],
    }

    with open(TEMPLATE, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(asset, fh, indent=4, ensure_ascii=False)
        fh.write("\n")

    # 回读校验，确保写出的内容可解析且结构完整。
    with open(TEMPLATE, "r", encoding="utf-8") as fh:
        check = json.load(fh)

    print("asset name :", check["name"])
    print("maps       :", [m["name"] for m in check["maps"]])
    print("schemes    :", [s["name"] for s in check["controlSchemes"]])
    for m in check["maps"]:
        print("  map {0}: {1} actions / {2} bindings".format(
            m["name"], len(m["actions"]), len(m["bindings"])))
    gp = next(m for m in check["maps"] if m["name"] == "Gameplay")
    print("gameplay actions:", [a["name"] for a in gp["actions"]])
    return 0


if __name__ == "__main__":
    sys.exit(main())
