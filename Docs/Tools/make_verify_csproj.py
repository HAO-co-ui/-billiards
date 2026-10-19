"""为离线编译核验生成三个验证用 csproj。

为什么需要它：Unity 生成的 `.csproj` 是**显式源文件列表**（不是通配符），
本轮**新增**的 `.cs` 不在列表里 ⇒ 直接编译只会报成片的 `CS0246 找不到类型`，
既证明不了代码有问题、也证明不了没问题。
本脚本生成副本（**不动 Unity 的原文件**），改三处：AssemblyName / OutputPath / 追加新文件。

用法：<managed venv python> Docs/Tools/make_verify_csproj.py
"""

from __future__ import annotations

import io
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# 被引用的兄弟程序集 → (改写成什么, 程序集名)
#
# 为什么**不能**保留指向**原工程**的 `ProjectReference`：
# `Billiards.Editor.csproj` / `Billiards.Tests.EditMode.csproj` 通过 `ProjectReference`
# 引用 `Billiards.Runtime.csproj`，而 MSBuild 会去编译那个**原文件** ——
# 原文件里没有本轮新增的 `.cs` ⇒ 编译必然以 `CS0246` 失败，
# 而且失败信息会把责任推到**原工程**上（看起来像是"我的代码错了"）。
#
# ⚠ **P8 修的一个真缺口（2026-10-17；此前被误登记为"假信号"）**：
# 旧版把这三条改成**直接引 DLL**（`HintPath` 指 `Temp\bin\Verify\*.dll`）。
# 那条路只有在"你**先手工按顺序**编过 Runtime、再编 Editor、最后编 Tests"时才成立；
# **单编 Tests** 时那两个 DLL 还不存在 ⇒ MSBuild **不报缺引用**、只是拿不到类型
# ⇒ 满屏 `CS0234 命名空间 Billiards 中不存在 EditorTools`（16 条），**看起来像代码错**。
# ⇒ 现在改成指向**验证版工程**的 `ProjectReference`（`_verify_*.csproj`）：
#   MSBuild 会自动按依赖顺序把它们编出来 ⇒ **单命令 `dotnet build _verify_Billiards.Tests.EditMode.csproj`
#   就能得到 0 error**，与"先编谁"无关。
#   注意：指向的是 `_verify_*`（含本轮新增文件），**不是**原工程 ⇒ 仍保留"不编原工程"的好处。
# `Billiards.Diagnostics` 本轮未改动 ⇒ 继续直接引 `Library/ScriptAssemblies` 的产物。
REFERENCE_MAP = {
    "Billiards.Runtime.csproj": ("_verify_Billiards.Runtime.csproj", "Billiards.Runtime-verify"),
    "Billiards.Editor.csproj": ("_verify_Billiards.Editor.csproj", "Billiards.Editor-verify"),
    "Billiards.Diagnostics.csproj": (None, "Billiards.Diagnostics"),
}

# `None` 那一档（未改动的程序集）仍按 **HintPath 直接引 DLL**
DLL_FALLBACK = {
    "Billiards.Diagnostics.csproj": (r"Library\ScriptAssemblies\Billiards.Diagnostics.dll",
                                     "Billiards.Diagnostics"),
}

# (原 csproj, 验证用 AssemblyName, 追加的 Compile Include 列表)
#
# ⚠ 只追加**本轮新增的 .cs**（不是目录通配符）：Unity 生成的 csproj 是**显式列表**且通常是
# **当前的** ⇒ 对已列入的文件再追一条会变成**重复 Compile 项**（MSBuild 报错）。
TARGETS = [
    (
        "Billiards.Runtime.csproj",
        "Billiards.Runtime-verify",
        [r"Assets\_Project\Scripts\Runtime\Physics\ShotPredictionWorkspace.cs",
         # 球杆材质接入配套：瞄准线/特效线共享材质单点（材质预算 30/30 零余量）
         r"Assets\_Project\Scripts\Runtime\Physics\RuntimeLineMaterial.cs",
         # 35-C P6（UI 侧）：结算卡两形态的文案/配色单点 + "回到首页"接缝
         r"Assets\_Project\Scripts\Runtime\UI\MatchResultCopy.cs",
         r"Assets\_Project\Scripts\Runtime\UI\HomeSeams.cs"],
    ),
    (
        "Billiards.Editor.csproj",
        "Billiards.Editor-verify",
        [],
    ),
    (
        "Billiards.Tests.EditMode.csproj",
        "Billiards.Tests.EditMode-verify",
        [r"Assets\_Project\Tests\EditMode\MainSceneTests.cs",
         r"Assets\_Project\Tests\EditMode\DebugOverlayTests.cs",
         r"Assets\_Project\Tests\EditMode\AimPredictionThrottleTests.cs"],
    ),
]

PROJECT_REFERENCE = re.compile(r"\s*<ProjectReference Include=\"([^\"]+)\">.*?</ProjectReference>", re.S)


def rewrite_references(text: str) -> str:
    def replace(match: re.Match[str]) -> str:
        included = match.group(1)
        if included not in REFERENCE_MAP:
            raise SystemExit(f"未登记的 ProjectReference：{included}")
        verify_proj, name = REFERENCE_MAP[included]
        if verify_proj:
            # **指向验证版工程的** ProjectReference ⇒ MSBuild 自动按依赖顺序编，单命令即可 0 error
            return (f'\n    <ProjectReference Include="{verify_proj}">\n'
                    f'      <Name>{name}</Name>\n'
                    f'    </ProjectReference>')
        hint, dll_name = DLL_FALLBACK[included]
        return (f'\n    <Reference Include="{dll_name}">\n'
                f'      <HintPath>{hint}</HintPath>\n'
                f'      <Private>false</Private>\n'
                f'    </Reference>')

    return PROJECT_REFERENCE.sub(replace, text)


def build(source: str, name: str, extra: list[str]) -> str:
    path = os.path.join(ROOT, source)
    text = io.open(path, encoding="utf-8-sig").read()

    text = text.replace(f"<AssemblyName>{source[:-7]}</AssemblyName>", f"<AssemblyName>{name}</AssemblyName>")
    if f"<AssemblyName>{name}</AssemblyName>" not in text:
        raise SystemExit(f"{source}: 未能改写 AssemblyName（原值不是 {source[:-7]}）")

    old_out = "<OutputPath>Temp\\bin\\Debug\\</OutputPath>"
    if old_out not in text:
        raise SystemExit(f"{source}: 未找到 {old_out}")
    text = text.replace(old_out, "<OutputPath>Temp\\bin\\Verify\\</OutputPath>")

    text = rewrite_references(text)

    lines = "".join(f'    <Compile Include="{item}" />\n' for item in extra if item.endswith(".cs"))
    lines_added = sum(1 for item in extra if item.endswith(".cs"))
    # ⚠ **幂等**：Unity 会重生成 csproj；若它**已经**把本轮新文件列进去了，再追加会变成
    # **重复 Compile 项**（MSBuild 报 `CS2002 源文件指定了多次`）⇒ 只追加**尚未列出**的。
    if lines:
        present = [item for item in extra if item.endswith(".cs") and f'Include="{item}"' in text]
        if present:
            print(f"  （已在上游列表，跳过追加：{present}）")
            lines = "".join(f'    <Compile Include="{item}" />\n'
                            for item in extra if item.endswith(".cs") and item not in present)
            lines_added -= len(present)
    index = text.rfind("<Compile Include=")
    end = text.find("\n", text.find("/>", index) + 2) + 1
    if lines:
        text = text[:end] + lines + text[end:]

    out = os.path.join(ROOT, "_verify_" + source)
    io.open(out, "w", encoding="utf-8-sig").write(text)
    print(f"generated _verify_{source}（追加 {lines_added} 条）")
    return out


def main() -> None:
    for source, name, extra in TARGETS:
        build(source, name, extra)


if __name__ == "__main__":
    main()
