#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
D4 切片 C 收尾：把实测结论回写进 spec 与工程标准。

三处 spec 改动（§2.3 契约常量 / 常量表 / D-2 设计边界 / 判据 10 注记）
+ 一处工程标准改动（V-6 豁免条款 + V-7 工程经验）。

模式同 d4fix03_converge_tolerances.py：**逐处先断言锚点存在，任一不符即中止且不写盘**。
"""

import sys
import os

ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
SPEC = os.path.join(ROOT, "Docs/35-A_Day4_物理内核I_spec.md")
STD = os.path.join(ROOT, "Docs/工程标准.md")


def replace_once(text, old, new, label):
    """在 text 中把 old 替换为 new；必须恰好命中 1 次，否则报错。"""
    n = text.count(old)
    if n != 1:
        raise AssertionError(f"[{label}] 锚点命中 {n} 次（应为 1）：{old[:80]!r}")
    return text.replace(old, new)


def main():
    # ── spec ──────────────────────────────────────────────────────────
    with open(SPEC, "r", encoding="utf-8") as fh:
        spec = fh.read()

    # A) 契约常量本体：16 → 128 + 分层推导
    spec = replace_once(
        spec,
        "        public const int   MaxToiIterations = 16;",
        "        /// <summary>\n"
        "        /// 单个子步内\"推进到接触 → 解算 → 继续\"的迭代上限。\n"
        "        ///\n"
        "        /// **v2.1 → v2.2 由 16 上调为 128**，依据是切片 C 的分层实测（其余条件逐一叠加）：\n"
        "        /// ```\n"
        "        /// 档 0  原实现（速度用精确零比较、预算 16）      guardTrips = 1810（326/1230 次发射触发）\n"
        "        /// 档 A  + 近零速度即停 StoppedSpeedEpsilon=1e-6   guardTrips = 1196\n"
        "        /// 档 B  + 连续两次零时间推进即停                  guardTrips =  339\n"
        "        /// 档 C  + 预算 64                                 guardTrips =    3\n"
        "        /// 档 D  + 预算 128  ← 交付态                      guardTrips =    0\n"
        "        /// ```\n"
        "        /// **关键**：A/B 两档修掉的是\"解算无法分离球\"引发的空转（(乙)：earliest 恒为 0、remaining 一步不减）；\n"
        "        /// 剩下的 339 经逐迭代记录定性为 (甲) —— `earliest` 为正且递增（5.5e-6 → 1.4e-4）、`remaining` 递减，\n"
        "        /// 即球楔死在凹袋角、以约 24 nm 间隙反复微接触，**每次消耗的是真实时间**。\n"
        "        /// ⇒ 收紧预算会在真实场景上误报，故上调至 128 覆盖该场景。\n"
        "        ///\n"
        "        /// **它仍是活的硬门**：若有场景需要 >128 次迭代，守卫会再次触发 —— 那正是它该做的。\n"
        "        /// 修前/修后对照已证明本改动**不改变任何可观测物理量**\n"
        "        /// （maxPenetration 逐位相同 6.146729e-08 m；82 段命中数逐段相同；32 个未命中段相同）。\n"
        "        /// </summary>\n"
        "        public const int   MaxToiIterations = 128;",
        "A 契约常量",
    )

    # B) 常量落点表
    spec = replace_once(
        spec,
        "| `MaxToiIterations` / `ToiTimeEpsilon` / `ToiRemainingEpsilon` | `PoolSimulator` | 本 spec 定义 | 16 / 1e-9 s / 1e-9 s |",
        "| `MaxToiIterations` / `ToiTimeEpsilon` / `ToiRemainingEpsilon` | `PoolSimulator` | 本 spec 定义 | **128** / 1e-9 s / 1e-9 s（128 的推导见 §2.3） |\n"
        "| `StoppedSpeedEpsilon` | `PoolSimulator` | 本 spec 定义 | **1e-6 m/s**（R-C 修复：速度近零即置零并停止；远高于 5e-9 残差、远低于任何玩法相关速度） |",
        "B 常量表",
    )

    # C) D-2 设计边界（紧接\"不构成正确性风险\"之后）
    spec = replace_once(
        spec,
        "若远低于 5，则 `SubSteps` 可能偏保守（浪费算力，不影响正确性）。**不构成正确性风险。**",
        "若远低于 5，则 `SubSteps` 可能偏保守（浪费算力，不影响正确性）。**不构成正确性风险。**\n"
        "\n"
        "**设计边界（v2.2 追加，来自切片 C 实测）**：判据 B 的不等式**结构自守卫** ——\n"
        "`MaxRollingDeceleration` 调大时 `SubSteps` 会由 `DeriveSubSteps` 同步重推，\n"
        "故\"调大 a_max 就会破坏不等式\"**并不成立**（实测 a_max = 20 时 `½·20·(dt/9)² = 8.573e-6 ≤ 5e-5` 仍成立）。\n"
        "⇒ **真正需要断言的是判据 B 分支的\"活体\"**（`DeriveSubSteps(2) == 2` —— v = 2 时判据 A 只需 1 子步，\n"
        "故该结果只可能由 B 给出）。这条断言由切片 C 的 `D-2` 覆盖；\n"
        "**若删掉判据 B，全套 85 条测试中只有 D-2 能抓到它**（T6 抓不到，因 a_max = 5 时 B 不支配 —— 这正是\"算过、没验过\"）。\n"
        "\n"
        "**允许上界**：`a_max` 不能无限上调。a_max = 20（SubSteps 9）时不等式余量仅 **1.15×**（4.340e-5 ≤ 5e-5）。\n"
        "⇒ **`MaxRollingDeceleration` 的允许上界由该不等式反推，不得凭手感上调。**",
        "C D-2 边界",
    )

    # D) 判据 10 注记
    spec = replace_once(
        spec,
        "| 10 | `ToiIterationGuardTrips` / `EnergyGainViolations` | **全部 == 0**（硬断言，全测试族） |",
        "| 10 | `ToiIterationGuardTrips` / `EnergyGainViolations` | **全部 == 0**（硬断言，全测试族）。\n"
        "**v2.2 注**：本条此前**不可达**（R-C 缺陷使 guardTrips = 1810）。修 R-C 后实测归零 ⇒ **本条按原样保留为硬断言，不加范围限定**。\n"
        "T7 已按本条把守卫升级为硬门（原先只在 §5 作为产出项记录）。\n"
        "**若将来再次出现 guardTrips ≠ 0，先查\"解算是否无法分离球\"，而不是放宽本条。** |",
        "D 判据 10",
    )

    # ── 工程标准 ──────────────────────────────────────────────────────
    with open(STD, "r", encoding="utf-8") as fh:
        std = fh.read()

    std = replace_once(
        std,
        "  一律见 `Docs/测试数基线台账.md`（避免两处各写一份而分叉）。",
        "  一律见 `Docs/测试数基线台账.md`（避免两处各写一份而分叉）。\n"
        "\n"
        "**R3-7** **测试可以产出证据，但必须满足两个条件**（V-6 豁免条款，来自 Day 4 切片 C）。\n"
        "- **允许**：测试把结构化证据写到 `Docs/Reports/**`（先例：`d2t02b-crosscheck.json`、`d4-segment-coverage.json`），\n"
        "  但必须 ① 经 `ReportOutput.ResolveReportPath` 一类**支持 `-outputDir` 的单点**输出，\n"
        "  ② 产物**入库为证据**（不是临时垃圾）。\n"
        "- **判据**：不满足这两条的测试写盘视为违规。\n"
        "- **为什么**：验收器（`DayNValidator`）与测试的分工是\"**谁判定、谁记录**\"——\n"
        "  但有些证据只有测试跑得出（如 1230 次逐段扫描），强行搬进验收器会让验收器承担仿真负担。\n"
        "  ⇒ 用这条豁免把\"允许\"与\"随意写盘\"分开。\n"
        "\n"
        "**R3-8** **JSON / 机器可读输出里的数值，一律显式 `ToString(InvariantCulture)`**（V-7，来自 Day 4 切片 C）。\n"
        "- **现象（实测）**：Unity 里 `StringBuilder.Append($\"...{x:F5}...\")` 的**内插格式说明符不生效** ——\n"
        "  原样输出字面量 `F5`，使 JSON 非法（切片 C 因此拿到过一次 JSON 解析失败）。\n"
        "- **规则**：机器可读输出中的数值不要依赖内插格式说明符；一律\n"
        "  `x.ToString(\"F5\", CultureInfo.InvariantCulture)` 之类**显式调用**。\n"
        "- **另一个理由**：`InvariantCulture` 同时挡掉\"不同区域设置下小数点变逗号\"这一类静默损坏。",
        "E 工程标准 R3-7/R3-8",
    )

    # 全部通过才写盘
    with open(SPEC, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(spec)
    with open(STD, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(std)
    print("✓ 已写 Docs/35-A_Day4_物理内核I_spec.md（4 处）")
    print("✓ 已写 Docs/工程标准.md（1 处，含 R3-7/R3-8 两条）")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as e:
        print("✗ 中止，未写盘：" + str(e))
        sys.exit(1)
