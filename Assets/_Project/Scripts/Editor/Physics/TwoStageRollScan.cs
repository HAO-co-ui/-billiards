using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Billiards.Physics;
using Billiards.Table;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **台呢滚阻两段模型的参数扫描器**（Editor CLI，**只出表 + 曲线，不含任何断言**）。
    ///
    /// ── 为什么它是"显式参数"的 ─────────────────────────────────────────────
    /// 网格与对照行**全部由代码里的显式字面量给出，完全不读 `PhysicsTuning.asset`** ⇒
    /// **跑扫描不需要先定出厂值**（出厂值由用户拍板后另行落资产）。
    /// 对照行 = **历史恒定档**（两段同值 `0.9`，即本次改成两段之前的出厂口径）⇒ 便于"改前 vs 改后"直接比。
    ///
    /// ── 口径（与既有实测一致，使新旧数字可直接比）──────────────────────────
    /// · 单球、**台面中心**（物理坐标 `(0, 0)`）、朝 **`+x` 长库方向**；
    /// · **路径 = 逐帧弦长累加**（不是首末直线距离 —— 球会撞库）；
    /// · 步长 = 内核固定步 `1/120 s`；上限 <see cref="StepCap"/> 步（超出即标记未静止）；
    /// · "撞库次数" = 内核诊断量 `TotalContactCount`（单球、无球-球 ⇒ 即段接触次数）。
    ///
    /// ── 产物（默认 `Docs/Tuning/`；可用 `-outputDir` 改到仓库外）────────────
    /// · `two-stage-roll-scan.csv`（网格 A + 网格 B + 历史恒定档对照行，逐格三指标）
    /// · `two-stage-roll-summary.txt`（网格 A/B 满力表 + **新出厂值 vs 现状 力度档对照** + 力度档次表）
    /// · `two-stage-roll-curve.svg`（速度-时间曲线：若干两段档 vs 历史恒定档）
    /// </summary>
    public static class TwoStageRollScan
    {
        private const string DefaultDir = "Docs/Tuning/";
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>单杆步数上限（= 30 s @120 Hz）。超出即视为"未静止"，如实落表。</summary>
        private const int StepCap = 3600;

        private const float FixedStepsPerSecond = 120f;

        /// <summary>扫描网格 A（**显式字面量**，不读资产）：spec §8 指定的 12 格。</summary>
        private static readonly float[] SlideGridA = { 2.0f };

        private static readonly float[] RollGridA = { 0.10f, 0.15f, 0.25f, 0.40f };
        private static readonly float[] TransitionGridA = { 1.0f, 2.0f, 3.0f };

        /// <summary>
        /// 扫描网格 B（补充，**显式字面量**）：`a_slide ≤ 历史恒定口径 0.9` ⇒ **比旧口径滚得更远**。
        /// 理由：网格 A 固定 `a_slide = 2.0`（是旧口径 `0.9` 的 2 倍以上）⇒ 高速段扣得更狠，
        /// 满力路径反而**短于**旧口径（实测 3.76–4.98 m vs 旧口径 6.20 m）。
        /// 用户第一诉求是"滚得太短" ⇒ 必须同时给出"变长"那一侧的可选值。
        /// ★ **用户拍板档 ④ = `0.50 / 0.25 / 2.0` 就在本网格内**（见 `FactoryNow`）。
        /// </summary>
        private static readonly float[] SlideGridB = { 0.5f, 0.7f, 0.9f };

        private static readonly float[] RollGridB = { 0.05f, 0.10f, 0.15f, 0.25f };
        private static readonly float[] TransitionGridB = { 1.0f, 2.0f, 3.0f };

        /// <summary>力度档（0 / 25 / 50 / 75 / 100 %）。</summary>
        private static readonly float[] PowerGrid = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        /// <summary>**新出厂口径（用户拍板档 ④）** —— 只用于摘要里的"改前 vs 改后"对照，**不读资产**。</summary>
        private static readonly float[] FactoryNow = { 0.50f, 0.25f, 2.0f };

        /// <summary>
        /// **历史恒定档对照**：`a_slide == a_roll == 0.9`（= 本次改成两段之前的出厂口径）。
        /// ⚠ 旧单值滚阻字段已删，"恒定档"由"两段取同值"表达（退化档下 `v_transition` 不生效，取 1.0）。
        /// </summary>
        private static readonly float[] ConstantPrior = { 0.9f, 0.9f, 1.0f };

        /// <summary>CSV/摘要里的 case 标签（单点常量，避免各处字面量分叉）。</summary>
        private const string CaseGridA = "two-stage";

        private const string CaseGridB = "two-stage-B";
        private const string CaseConstant = "constant(prior-0.9)";

        public static void RunFromCommandLine()
        {
            int exit = Run();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exit);
            }
        }

        [MenuItem("Billiards/Physics/Run Two-Stage Roll Scan", false, 81)]
        public static void RunInteractive()
        {
            int exit = Run();
            Debug.Log("两段滚阻扫描退出码：" + exit);
        }

        private static int Run()
        {
            var table = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            if (table == null)
            {
                Debug.LogError("两段滚阻扫描：缺少 " + TablePath);
                return 2;
            }

            string dir = CommandLineArgs.Get("-outputDir");
            if (!string.IsNullOrEmpty(dir) && !Path.IsPathRooted(dir))
            {
                dir = ProjectPaths.ToFullPath(dir.TrimEnd('/') + "/");
            }

            if (string.IsNullOrEmpty(dir))
            {
                dir = ProjectPaths.ToFullPath(DefaultDir);
            }

            Directory.CreateDirectory(dir);

            // ── 力度 → 初速（与交互层同式；**显式字面量**，不读资产）──────────
            const float minLaunch = 0.5f;
            const float maxLaunch = 8f;

            // ── 网格 A（12 格）+ 网格 B（变长方向，36 格）+ 历史恒定档对照行 ────
            var rows = new List<Row>();
            AddGrid(rows, CaseGridA, SlideGridA, RollGridA, TransitionGridA);
            AddGrid(rows, CaseGridB, SlideGridB, RollGridB, TransitionGridB);

            // 对照行：历史恒定档（两段同值 0.9 = 本次改成两段之前的出厂口径）
            rows.Add(new Row
            {
                Case = CaseConstant,
                Slide = ConstantPrior[0],
                Roll = ConstantPrior[1],
                Transition = ConstantPrior[2],
                Shots = new Shot[PowerGrid.Length],
            });

            foreach (Row row in rows)
            {
                PhysicsParams p = Params(row.Slide, row.Roll, row.Transition);
                for (int k = 0; k < PowerGrid.Length; k++)
                {
                    float v0 = minLaunch + PowerGrid[k] * (maxLaunch - minLaunch);
                    row.Shots[k] = Shoot(table, p, v0);
                }
            }

            string csvPath = Path.Combine(dir, "two-stage-roll-scan.csv");
            string txtPath = Path.Combine(dir, "two-stage-roll-summary.txt");
            string svgPath = Path.Combine(dir, "two-stage-roll-curve.svg");

            WriteCsv(csvPath, rows);
            WriteSummary(txtPath, rows, minLaunch, maxLaunch);
            WriteCurve(svgPath, table, rows, minLaunch, maxLaunch);

            Debug.Log("两段滚阻扫描产物 → " + dir);
            Debug.Log("TWO_STAGE_ROLL_SCAN=" + dir + " rows=" + rows.Count + " cap=" + StepCap);
            return 0;
        }

        // ── 单杆测量 ──────────────────────────────────────────────────────

        private static void AddGrid(List<Row> rows, string kase, float[] slides, float[] rolls, float[] transitions)
        {
            foreach (float aSlide in slides)
            {
                foreach (float aRoll in rolls)
                {
                    foreach (float vTransition in transitions)
                    {
                        rows.Add(new Row
                        {
                            Case = kase,
                            Slide = aSlide,
                            Roll = aRoll,
                            Transition = vTransition,
                            Shots = new Shot[PowerGrid.Length],
                        });
                    }
                }
            }
        }

        /// <summary>一杆的结果（"撞库次数" = `TotalContactCount`）。</summary>
        private struct Shot
        {
            public float RestS;
            public float TravelM;
            public int CushionHits;
            public bool Rested;
        }

        private struct Row
        {
            public string Case;
            public float Slide;
            public float Roll;
            public float Transition;
            public Shot[] Shots;   // 长度 = PowerGrid.Length（构造时分配；Run 内按格赋值）
        }

        private static PhysicsParams Params(float aSlide, float aRoll, float vTransition)
        {
            return new PhysicsParams
            {
                cushionRestitution = 0.8f,
                cushionTangentialFriction = 0.9f,
                ballRestitution = 0.95f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = aSlide,
                rollingDecelerationRoll = aRoll,
                rollingTransitionSpeed = vTransition,
            };
        }

        /// <summary>
        /// 单球、台面中心、朝 `+x` 打一杆；返回到"宏观静止"的耗时 / 逐帧弦长累加路径 / 段接触数。
        /// **用内核本身跑**，不另写一套物理。
        /// </summary>
        private static Shot Shoot(TableConfig table, PhysicsParams p, float v0)
        {
            var sim = new PoolSimulator(table, p);
            sim.LoadBalls(new[]
            {
                new BallState { position = Vector2.zero, velocity = new Vector2(v0, 0f) },
            });
            sim.ResetDiagnostics();

            int steps = 0;
            float travel = 0f;
            Vector2 prev = sim.Balls[0].position;
            while (steps < StepCap && !sim.TableAtRest)
            {
                sim.Step();
                steps++;
                Vector2 cur = sim.Balls[0].position;
                travel += (cur - prev).magnitude;
                prev = cur;
            }

            return new Shot
            {
                RestS = steps / FixedStepsPerSecond,
                TravelM = travel,
                CushionHits = sim.TotalContactCount,
                Rested = sim.TableAtRest,
            };
        }

        // ── 落盘 ──────────────────────────────────────────────────────────

        private static void WriteCsv(string path, List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.Append("case,aSlide,aRoll,vTransition,power,launchSpeed,stepsToRest,restS,travelM,cushionHits,rested\n");
            for (int r = 0; r < rows.Count; r++)
            {
                Row row = rows[r];
                for (int k = 0; k < PowerGrid.Length; k++)
                {
                    float v0 = 0.5f + PowerGrid[k] * 4f;
                    Shot s = row.Shots[k];
                    sb.Append(row.Case).Append(',')
                      .Append(F(row.Slide, "F2")).Append(',')
                      .Append(F(row.Roll, "F2")).Append(',')
                      .Append(F(row.Transition, "F2")).Append(',')
                      .Append((PowerGrid[k] * 100f).ToString("F0", CultureInfo.InvariantCulture)).Append('%').Append(',')
                      .Append(F(v0, "F2")).Append(',')
                      .Append(Mathf.RoundToInt(s.RestS * FixedStepsPerSecond).ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(F(s.RestS, "F4")).Append(',')
                      .Append(F(s.TravelM, "F4")).Append(',')
                      .Append(s.CushionHits.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(s.Rested ? "1" : "0").Append('\n');
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, List<Row> rows, float minLaunch, float maxLaunch)
        {
            var sb = new StringBuilder();
            sb.Append("台呢滚阻**两段模型**参数扫描（**不含任何断言**；可复现、可复算）\n");
            sb.Append("口径：单球 · 台面中心 (0,0) · 朝 +x 长库 · 路径 = 逐帧弦长累加 · 步长 1/120 s · 上限 ")
              .Append(StepCap).Append(" 步（").Append(StepCap / (int)FixedStepsPerSecond).Append(" s）\n");
            sb.Append("网格 A（spec 指定 12 格）：a_slide = 2.0 固定 × a_roll ∈ {0.10, 0.15, 0.25, 0.40} × v_transition ∈ {1.0, 2.0, 3.0}\n");
            sb.Append("网格 B（补充 36 格）：a_slide ∈ {0.5, 0.7, 0.9} × a_roll ∈ {0.05, 0.10, 0.15, 0.25} × v_transition ∈ {1.0, 2.0, 3.0}\n");
            sb.Append("  ⚠ 网格 A 的 a_slide = 2.0 是**历史出厂 0.9 的 2 倍以上** ⇒ 高速段扣得更狠、满力路径**短于**旧口径；\n");
            sb.Append("     网格 B 是**变长那一侧**（a_slide ≤ 0.9）—— 用户第一诉求是\"滚得太短\" ⇒ 两侧都要看。\n");
            sb.Append("★ **新出厂口径 = `a_slide = ").Append(F(FactoryNow[0], "F2")).Append("` / `a_roll = ")
              .Append(F(FactoryNow[1], "F2")).Append("` / `v_transition = ").Append(F(FactoryNow[2], "F2"))
              .Append("`**（= 网格 B 的一行，见下面的对照表）。\n");
            sb.Append("★ **历史恒定档对照 = `").Append(F(ConstantPrior[0], "F2")).Append('/').Append(F(ConstantPrior[1], "F2"))
              .Append('/').Append(F(ConstantPrior[2], "F2")).Append("`**（两段同值 ⇒ 退化 ⇒ 与历史单一恒定减速度逐位等价）。\n");
            sb.Append("力度 → 初速：v0 = ").Append(F(minLaunch, "F2")).Append(" + 力度·(")
              .Append(F(maxLaunch - minLaunch, "F2")).Append(") ⇒ 0/25/50/75/100% = ")
              .Append(F(minLaunch, "F2")).Append('/').Append(F(minLaunch + 0.25f * (maxLaunch - minLaunch), "F2"))
              .Append('/').Append(F(minLaunch + 0.5f * (maxLaunch - minLaunch), "F2"))
              .Append('/').Append(F(minLaunch + 0.75f * (maxLaunch - minLaunch), "F2"))
              .Append('/').Append(F(maxLaunch, "F2")).Append(" m/s\n");
            sb.Append("⚠ \"撞库次数\" = 内核诊断量 TotalContactCount（单球、无球-球 ⇒ 即段接触次数）。\n\n");

            // 表 A / A2：满力一杆
            AppendFullPowerTable(sb, rows, CaseGridA, "═══ 表 A：**网格 A 满力一杆（100%，v0 = 4.5 m/s）** ═══", true);
            AppendFullPowerTable(sb, rows, CaseGridB, "═══ 表 A2：**网格 B 满力一杆（100%，v0 = 4.5 m/s）** —— **变长方向** ═══", false);

            // ★ 新出厂值 vs 现状（向用户解释"尾巴有多长"用）
            AppendFactoryCompare(sb, rows);

            var main = new List<Row>();
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Case == CaseGridA || rows[r].Case == CaseConstant)
                {
                    main.Add(rows[r]);
                }
            }

            // 表 B / C / D：网格 A + 历史恒定档 的各力度档
            sb.Append("\n═══ 表 B：**网格 A + 历史恒定档 · 各力度档 · 静止耗时 (s)** ═══\n");
            sb.Append("参数（a_slide/a_roll/v_t) | 0% | 25% | 50% | 75% | 100%\n");
            AppendPowerMatrix(sb, main, 0);

            sb.Append("\n═══ 表 C：**网格 A + 历史恒定档 · 各力度档 · 滚动路径 (m)** ═══\n");
            sb.Append("参数（a_slide/a_roll/v_t) | 0% | 25% | 50% | 75% | 100%\n");
            AppendPowerMatrix(sb, main, 1);

            sb.Append("\n═══ 表 D：**网格 A + 历史恒定档 · 各力度档 · 撞库次数** ═══\n");
            sb.Append("参数（a_slide/a_roll/v_t) | 0% | 25% | 50% | 75% | 100%\n");
            AppendPowerMatrix(sb, main, 2);

            sb.Append("\n★ = 上限 ").Append(StepCap).Append(" 步内**未静止**（该格数字为上限值，不代表真值）。\n");
            sb.Append("说明：**选值由主理人 + 用户拍板**；扫描**不含任何断言**，仅作调参依据。\n");
            sb.Append("      网格 B 的力度档明细见 CSV（`two-stage-roll-scan.csv`，`case=").Append(CaseGridB).Append("`）。\n");
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// **新出厂值 vs 现状**：逐力度档并列两行（**本工具不读资产**，两行都是显式字面量）。
        /// 用途：向用户解释"改了以后每一档各滚多远、停多久"。
        /// </summary>
        private static void AppendFactoryCompare(StringBuilder sb, List<Row> rows)
        {
            int nowIdx = IndexOfRow(rows, FactoryNow);
            int priorIdx = IndexOfRow(rows, ConstantPrior);
            sb.Append("\n★★★ 新出厂值 vs 现状（逐力度档；**本表两行都是显式字面量，不读资产**）★★★\n");
            if (nowIdx < 0 || priorIdx < 0)
            {
                sb.Append("（内部错误：新出厂行或对照行未生成）\n");
                return;
            }

            Row nowRow = rows[nowIdx];
            Row priorRow = rows[priorIdx];
            sb.Append("口径：单球 · 台面中心 · 朝 +x · 路径 = 逐帧弦长累加 · 上限 ").Append(StepCap).Append(" 步\n\n");
            sb.Append("力度 | 初速(m/s) | ").Append(F(priorRow.Slide, "F2")).Append('/').Append(F(priorRow.Roll, "F2"))
              .Append("/vt=").Append(F(priorRow.Transition, "F2")).Append("（现状）耗时/路径/撞库 | ")
              .Append(F(nowRow.Slide, "F2")).Append('/').Append(F(nowRow.Roll, "F2")).Append("/vt=")
              .Append(F(nowRow.Transition, "F2")).Append("（新出厂）耗时/路径/撞库 | 路径变化\n");
            for (int k = 0; k < PowerGrid.Length; k++)
            {
                Shot o = priorRow.Shots[k];
                Shot n = nowRow.Shots[k];
                float delta = o.TravelM != 0f ? (n.TravelM / o.TravelM - 1f) * 100f : 0f;
                sb.Append((PowerGrid[k] * 100f).ToString("F0", CultureInfo.InvariantCulture)).Append("% | ")
                  .Append(F(0.5f + PowerGrid[k] * 4f, "F2")).Append(" | ")
                  .Append(F(o.RestS, "F4")).Append(" s / ").Append(F(o.TravelM, "F4")).Append(" m / ")
                  .Append(o.CushionHits.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(F(n.RestS, "F4")).Append(" s / ").Append(F(n.TravelM, "F4")).Append(" m / ")
                  .Append(n.CushionHits.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                  .Append((delta >= 0f ? "+" : "")).Append(delta.ToString("F1", CultureInfo.InvariantCulture)).Append("%\n");
            }

            sb.Append('\n');
        }

        /// <summary>按 (a_slide, a_roll, v_transition) 找行下标；找不到返回 −1。</summary>
        private static int IndexOfRow(List<Row> rows, float[] p)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Slide == p[0] && rows[r].Roll == p[1] && rows[r].Transition == p[2])
                {
                    return r;
                }
            }

            return -1;
        }

        /// <summary>满力一杆表（按 <paramref name="fullPower"/> 决定是否附恒定档对照行）。</summary>
        private static void AppendFullPowerTable(StringBuilder sb, List<Row> rows, string kase, string title, bool fullPower)
        {
            sb.Append(title).Append('\n');
            sb.Append("a_slide | a_roll | v_transition | 静止耗时(s) | 滚动路径(m) | 撞库次数\n");
            for (int r = 0; r < rows.Count; r++)
            {
                Row row = rows[r];
                if (fullPower)
                {
                    if (row.Case != kase && row.Case != CaseConstant)
                    {
                        continue;
                    }
                }
                else if (row.Case != kase)
                {
                    continue;
                }

                Shot s = row.Shots[PowerGrid.Length - 1];
                sb.Append(F(row.Slide, "F2")).Append(" | ")
                  .Append(F(row.Roll, "F2")).Append(" | ")
                  .Append(F(row.Transition, "F2")).Append(" | ")
                  .Append(F(s.RestS, "F4")).Append(Star(s)).Append(" | ")
                  .Append(F(s.TravelM, "F4")).Append(" | ")
                  .Append(s.CushionHits.ToString(CultureInfo.InvariantCulture));
                if (row.Case == CaseConstant)
                {
                    sb.Append("  ← 历史恒定档（改动前的出厂口径）");
                }

                sb.Append('\n');
            }

            sb.Append('\n');
        }

        /// <summary><paramref name="metric"/>：0 = 耗时(s)，1 = 路径(m)，2 = 撞库次数。</summary>
        private static void AppendPowerMatrix(StringBuilder sb, List<Row> rows, int metric)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                Row row = rows[r];
                sb.Append(F(row.Slide, "F2")).Append('/').Append(F(row.Roll, "F2")).Append('/').Append(F(row.Transition, "F2"));
                if (row.Case != CaseGridA)
                {
                    sb.Append(" ← ").Append(row.Case);
                }

                sb.Append(" |");
                for (int k = 0; k < PowerGrid.Length; k++)
                {
                    Shot s = row.Shots[k];
                    string cell = metric == 0 ? F(s.RestS, "F4")
                        : metric == 1 ? F(s.TravelM, "F4")
                        : s.CushionHits.ToString(CultureInfo.InvariantCulture);
                    sb.Append(' ').Append(cell).Append(metric == 0 && !s.Rested ? "*" : string.Empty).Append(" |");
                }

                sb.Append('\n');
            }
        }

        private static string Star(Shot s) => s.Rested ? string.Empty : "*";

        // ── 曲线 ─────────────────────────────────────────────────────────

        /// <summary>速度-时间曲线（满力一杆）：若干两段档 vs 恒定档。**纯演示，无断言**。</summary>
        private static void WriteCurve(string path, TableConfig table, List<Row> rows, float minLaunch, float maxLaunch)
        {
            // 曲线选点：恒定档 + 网格 A 两档 + 网格 B 两档（按参数查找，不用下标，避免网格增删时错位）
            float[][] wanted =
            {
                new[] { 0.9f, 0.9f, 1.0f },    // 历史恒定档（改动前的出厂口径）
                new[] { 2.0f, 0.10f, 1.0f },   // 网格 A
                new[] { 2.0f, 0.40f, 3.0f },   // 网格 A
                new[] { 0.5f, 0.05f, 1.0f },   // 网格 B（变长）
                new[] { 0.9f, 0.15f, 2.0f },   // 网格 B
            };

            var picks = new List<Row>();
            foreach (float[] want in wanted)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    if (rows[r].Slide == want[0] && rows[r].Roll == want[1] && rows[r].Transition == want[2])
                    {
                        picks.Add(rows[r]);
                        break;
                    }
                }
            }

            const int w = 900;
            const int h = 420;
            const float left = 60f;
            const float top = 30f;
            const float plotW = w - 90f;
            const float plotH = h - 80f;
            float tMax = 20f;                       // 横轴上限（秒；网格 B 的"变长"档会拉到 ~12 s）
            float vMax = maxLaunch;

            string[] colors = { "#d62728", "#1f77b4", "#9467bd", "#2ca02c", "#ff7f0e" };

            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(w).Append("\" height=\"").Append(h).Append("\">\n");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fafafa\"/>\n");
            sb.Append("<text x=\"10\" y=\"20\" font-size=\"14\">满力一杆速度-时间曲线：两段滚阻 vs 恒定档（单球 · 台面中心 · 朝 +x · v0 = ")
              .Append(F(vMax, "F2")).Append(" m/s）</text>\n");

            // 坐标轴
            sb.Append("<line x1=\"").Append(left).Append("\" y1=\"").Append(top + plotH)
              .Append("\" x2=\"").Append(left + plotW).Append("\" y2=\"").Append(top + plotH).Append("\" stroke=\"#444\"/>\n");
            sb.Append("<line x1=\"").Append(left).Append("\" y1=\"").Append(top)
              .Append("\" x2=\"").Append(left).Append("\" y2=\"").Append(top + plotH).Append("\" stroke=\"#444\"/>\n");
            sb.Append("<text x=\"").Append(left - 52f).Append("\" y=\"").Append(top + 12f)
              .Append("\" font-size=\"11\">v (m/s)</text>\n");
            sb.Append("<text x=\"").Append(left + plotW - 40f).Append("\" y=\"").Append(top + plotH + 30f)
              .Append("\" font-size=\"11\">t (s)</text>\n");

            for (int k = 0; k < picks.Count; k++)
            {
                Row row = picks[k];
                PhysicsParams p = Params(row.Slide, row.Roll, row.Transition);
                var sim = new PoolSimulator(table, p);
                sim.LoadBalls(new[]
                {
                    new BallState { position = Vector2.zero, velocity = new Vector2(vMax, 0f) },
                });

                var pts = new StringBuilder();
                int steps = 0;
                while (steps < StepCap && !sim.TableAtRest)
                {
                    float t = steps / FixedStepsPerSecond;
                    float v = sim.Balls[0].velocity.magnitude;
                    AppendPoint(pts, left, top, plotW, plotH, t, v, tMax, vMax);
                    sim.Step();
                    steps++;
                }

                AppendPoint(pts, left, top, plotW, plotH, steps / FixedStepsPerSecond,
                    sim.Balls[0].velocity.magnitude, tMax, vMax);

                string color = colors[k % colors.Length];
                sb.Append("<polyline fill=\"none\" stroke=\"").Append(color)
                  .Append("\" stroke-width=\"1.8\" points=\"").Append(pts.ToString().TrimEnd()).Append("\"/>\n");
                sb.Append("<rect x=\"").Append(left + 8f + k * 170f).Append("\" y=\"").Append(h - 18f)
                  .Append("\" width=\"10\" height=\"10\" fill=\"").Append(color).Append("\"/>\n");
                sb.Append("<text x=\"").Append(left + 22f + k * 170f).Append("\" y=\"").Append(h - 9f)
                  .Append("\" font-size=\"12\" fill=\"").Append(color).Append("\">a=")
                  .Append(F(row.Slide, "F2")).Append('/').Append(F(row.Roll, "F2")).Append("/vt=")
                  .Append(F(row.Transition, "F2"))
                  .Append(row.Case == CaseConstant ? "(历史恒定档)" : string.Empty)
                  .Append("</text>\n");
            }

            sb.Append("</svg>\n");
            File.WriteAllText(path, sb.ToString());
        }

        private static void AppendPoint(StringBuilder pts, float left, float top, float plotW, float plotH,
            float t, float v, float tMax, float vMax)
        {
            float x = left + Mathf.Clamp01(t / tMax) * plotW;
            float y = top + plotH - Mathf.Clamp01(v / vMax) * plotH;
            pts.Append(F(x, "F1")).Append(',').Append(F(y, "F1")).Append(' ');
        }

        private static string F(float x, string fmt) => x.ToString(fmt, CultureInfo.InvariantCulture);
    }
}
