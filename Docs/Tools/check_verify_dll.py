"""核验"新类型真的进了产物 DLL"（防离线编译的假绿）。

为什么单独一步：编译成功 ≠ 新文件被编译。Unity 生成的 `.csproj` 是显式列表，
本轮的验证 csproj 靠追加通配符纳入新文件 —— 若通配符写错，MSBuild 会**静默跳过**，
于是"0 error"只说明旧文件没问题。这里用 DLL 里的**字段名/方法名**做证据
（不用类型名：.NET 的 `#Strings` 堆会按后缀复用字符串，类型名可能查不到）。

用法：<managed venv python> Docs/Tools/check_verify_dll.py
"""

from __future__ import annotations

import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# (DLL 相对路径, 该程序集里必须出现的符号)
CHECKS = [
    (r"Temp\bin\Verify\Billiards.Runtime-verify.dll", [
        "PanelScoreboard",          # ClubUiTokens 的字段
        "BrandBallRingSize",        # ClubUiTokens 的常量
        "TryParseHex",              # ClubUiTokens 的方法
        "PanelForRadius",           # ClubUiSprites 的方法
        "ValidateSlots",            # ClubUiSprites / ClubBallChips 的方法
        "TopBandSampleUv",          # ClubBallChips 的字段（判据采样点）
        "IsStriped",                # ClubBallChips 的方法
        "_ballInHandLatch",         # PracticeHudView 的私有字段
        "OnToggleVfx",              # PracticeHudView 的开关回调
        "ApplyOutcomeStyle",        # PracticeResultView 的结局配色
        "ResolveVisualState",       # PracticeHudState 的状态求解
        "UnreachableStates",        # PracticeHudState 的不可达登记
        "PlayAgainButtonLocal",     # PracticeResultLayout 的卡内矩形
        "StatsTopLineY",            # PracticeResultLayout 的统计区上边线
        # 本轮（35-B 起步 + 瞄准性能）
        "ShouldRecompute",          # AimAssistController 的节流纯函数
        "minRecomputeIntervalSeconds",   # 节流间隔字段
        "ProbeRestFrames",          # ShotPredictionWorkspace 的缓存键字段
        "AimSweepDegreesPerFrame",  # SmokeProbe 的拖动步长常量
        "AimSweepDegrees",          # SmokeProbe 的角函数
        "ApplySmokeAimDegrees",     # PhysicsTestDrive 的拖动注入入口
        "showDebugOverlay",         # PhysicsTestDrive 的调试叠加层开关
        # 批次 C：球-段扫描的保守包围盒预筛
        "SegmentScreenMargin",      # 预筛余量 ε（具名常量）
        "_segBoxMinX",              # 逐段预筛盒字段（私有字段名在元数据里逐字出现）
        # ⚠ `SegmentScreenSkips` 是**自动属性** ⇒ 元数据里是 `<…>k__BackingField` 与 `get_/set_` 形态，
        #    **不以裸名出现**；本检查是"**精确 run 匹配**"（非子串）⇒ 列它会恒假红。它的"非空转"证据
        #    改由**行为**提供（T119 单步 µs 对照 + 预筛计数在规格里登记）。
    ]),
    (r"Temp\bin\Verify\Billiards.Editor-verify.dll", [
        "UiSpriteImportPostprocessor",
        "BallChipsDirectory",
        "PanelBorder",
        "LoadUiSpritesOrThrow",
        "LoadBallChipsOrThrow",
        # 本轮：Main 入口生成
        "AttachMainGameplayLayer",
        "MainHasGameplayLayer",
        "BuildGameplayHosts",
        "MainGameplayRootName",
        "RebuildMainScene",
        "BuildScenePath",
    ]),
    (r"Temp\bin\Verify\Billiards.Tests.EditMode-verify.dll", [
        "T124_DesignTokens_AndSprites_SingleSource",
        "T125_StatusContract_MapsDesignStates",
        "T126_FoulAndResultFeedback_MatchDesign",
        "T122_BallVisuals_MatchKernelCount_AndUsePerNumberMaterial",
        "T123_CueModel_MatchesTipOriginConvention",
        "CheckBallChipPixels",
        "IsNeutralLight",
        # 本轮
        "T127_MainScene_IsPlayableEntry_NotShell",
        "T128_MainGameplayLayer_ApplyIsIdempotent",
        "T129_DebugOverlay_DefaultOff_HotkeyGate",
        "T130_AimRecomputeThrottle_TrailingEdgeGuarantee",
        "T131_SmokeAimSweep_PureLogic",
    ]),
]


def scan(path: str) -> set[str]:
    data = open(path, "rb").read()
    return set(m.decode("utf-8", "ignore") for m in re.findall(rb"[\x20-\x7e]{3,60}", data))


def main() -> None:
    failed = False
    for relative, symbols in CHECKS:
        path = os.path.join(ROOT, relative)
        if not os.path.exists(path):
            print(f"MISS 产物不存在：{relative}")
            failed = True
            continue

        found = scan(path)
        missing = [s for s in symbols if s not in found]
        if missing:
            print(f"FAIL {relative}：缺 {len(missing)} 个符号 -> {missing}")
            failed = True
        else:
            print(f"OK   {relative}：{len(symbols)} 个符号全在产物里")

    raise SystemExit(1 if failed else 0)


if __name__ == "__main__":
    main()
