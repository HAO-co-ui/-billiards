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

# 被引用的兄弟程序集 → (替换后的 HintPath, 替换后的程序集名)
#
# 为什么要把 `ProjectReference` 全换成**直接引用 DLL**：
# `Billiards.Editor.csproj` / `Billiards.Tests.EditMode.csproj` 通过 `ProjectReference`
# 引用 `Billiards.Runtime.csproj`，而 MSBuild 会去编译那个**原文件** ——
# 原文件里没有本轮新增的 `.cs` ⇒ 编译必然以 `CS0246` 失败，
# 而且失败信息会把责任推到**原工程**上（看起来像是"我的代码错了"）。
# 换成引用验证版产物 DLL 之后，引用链与 Unity 的编译顺序一致，错误才指向真实位置。
#
# 注：`Library/ScriptAssemblies/*.dll` 是 Unity 上一次成功编译的产物；
# 本轮**未改动**的程序集（如 `Billiards.Diagnostics`）直接引用它是正确的（内容未变）。
REFERENCE_MAP = {
    "Billiards.Runtime.csproj": (r"Temp\bin\Verify\Billiards.Runtime-verify.dll", "Billiards.Runtime-verify"),
    "Billiards.Editor.csproj": (r"Temp\bin\Verify\Billiards.Editor-verify.dll", "Billiards.Editor-verify"),
    "Billiards.Diagnostics.csproj": (r"Library\ScriptAssemblies\Billiards.Diagnostics.dll", "Billiards.Diagnostics"),
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
        hint, name = REFERENCE_MAP[included]
        return (f'\n    <Reference Include="{name}">\n'
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
