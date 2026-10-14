using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 创建 **`PhysicsTuning.asset`**（物理调参的唯一可调入口）。
    /// 资产默认值 = 现口径 ⇒ 行为不变；`PhysicsTestDrive` 未指定资产时**回落到现有默认** ⇒ 开箱即用。
    /// </summary>
    public static class PhysicsTuningSetup
    {
        public const string AssetPath = "Assets/_Project/Data/PhysicsTuning.asset";

        [MenuItem("Billiards/Data/Create Physics Tuning Asset", false, 60)]
        public static void CreateInteractive()
        {
            string path = CreateIfMissing();
            Debug.Log("PhysicsTuning 资产：" + path);
        }

        /// <summary>命令行入口：缺则建，已存在则保留（幂等；不改动已有数值）。</summary>
        public static void CreateFromCommandLine()
        {
            string path = CreateIfMissing();
            Debug.Log("PHYSICS_TUNING=" + path);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        public static string CreateIfMissing()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(AssetPath);
            if (existing != null)
            {
                return AssetPath;
            }

            var asset = ScriptableObject.CreateInstance<PhysicsTuning>();
            PhysicsTuning.Defaults d = PhysicsTuning.DefaultValues();
            asset.cushionRestitution = d.cushionRestitution;
            asset.cushionTangentialFriction = d.cushionTangentialFriction;
            asset.rollingDeceleration = d.rollingDeceleration;
            asset.ballRestitution = d.ballRestitution;
            asset.ballTangentialFriction = d.ballTangentialFriction;
            asset.pocketCaptureRadiusScale = d.pocketCaptureRadiusScale;
            asset.restSpeedThreshold = PoolSimulator.RestSpeedThreshold;
            asset.restStableFrames = PoolSimulator.RestStableFrames;

            string dir = Path.GetDirectoryName(AssetPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return AssetPath;
        }
    }

    /// <summary>
    /// **参数扫描**：批量跑**本内核**（不另写一套物理），输出 CSV + **轨迹对比图（SVG）**。
    /// 确定性：固定初态（`RackPositions`）+ 固定步数 ⇒ 同参数必得同结果；扫描表随产物落盘。
    /// 产物默认落 `Docs/Tuning/`（**不是** `Docs/Reports/**` —— 那是已入库证据目录）；可用 `-outputDir` 改到仓库外。
    /// </summary>
    public static class PhysicsParamScan
    {
        private const string DefaultDir = "Docs/Tuning/";
        private const int Steps = 240;

        public static void RunFromCommandLine()
        {
            int exit = Run();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exit);
            }
        }

        [MenuItem("Billiards/Physics/Run Parameter Scan", false, 80)]
        public static void RunInteractive()
        {
            int exit = Run();
            Debug.Log("参数扫描退出码：" + exit);
        }

        private static int Run()
        {
            var table = AssetDatabase.LoadAssetAtPath<TableConfig>("Assets/_Project/Data/TableConfig.asset");
            if (table == null)
            {
                Debug.LogError("参数扫描：缺少 TableConfig.asset");
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

            // **扫描基线 = 出厂灰盒配置**（即 `PhysicsTuning.asset` 的值）——
            // 否则"扫描测的不是出厂配置"，结论不能外推（`D7-T02` 阻塞项 ①-4）。
            var tuningAsset = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(PhysicsTuningSetup.AssetPath);
            PhysicsParams baseParams;
            if (tuningAsset != null)
            {
                baseParams = tuningAsset.ToPhysicsParams();
            }
            else
            {
                PhysicsTuning.Defaults d = PhysicsTuning.DefaultValues();
                baseParams = new PhysicsParams
                {
                    cushionRestitution = d.cushionRestitution,
                    cushionTangentialFriction = d.cushionTangentialFriction,
                    ballRestitution = d.ballRestitution,
                    ballTangentialFriction = d.ballTangentialFriction,
                    rollingDeceleration = d.rollingDeceleration,
                };
            }

            // 扫描表：球-球恢复系数（台球实测 0.93–0.96，取上下界与中点）
            float[] eBalls = { 1.0f, 0.98f, 0.95f, 0.93f, 0.90f };
            var series = new List<(float eBall, List<Vector2> cuePath)>();

            foreach (float eBall in eBalls)
            {
                PhysicsParams p = baseParams;   // struct 拷贝
                p.ballRestitution = eBall;

                Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 3);
                var balls = new BallState[rack.Length];
                for (int i = 0; i < rack.Length; i++)
                {
                    balls[i] = new BallState { position = rack[i], velocity = Vector2.zero };
                }

                balls[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

                var sim = new PoolSimulator(table, p);
                sim.LoadBalls(balls);

                var path = new List<Vector2> { balls[0].position };
                for (int s = 0; s < Steps; s++)
                {
                    sim.Step();
                    path.Add(sim.Balls[0].position);
                }

                series.Add((eBall, path));
            }

            WriteCsv(Path.Combine(dir, "param-scan-eball.csv"), series);
            WriteSvg(Path.Combine(dir, "param-scan-eball.svg"), series);
            WriteSummary(Path.Combine(dir, "param-scan-summary.txt"), series);
            WriteRollingDecelerationSweep(dir);

            Debug.Log("参数扫描产物 → " + dir);
            Debug.Log("PARAM_SCAN=" + dir + " series=" + series.Count + " steps=" + Steps);
            return 0;
        }

        private static void WriteCsv(string path, List<(float eBall, List<Vector2> cuePath)> series)
        {
            var sb = new StringBuilder();
            sb.Append("eBall,step,x,y\n");
            foreach ((float eBall, List<Vector2> cuePath) in series)
            {
                for (int i = 0; i < cuePath.Count; i++)
                {
                    sb.Append(eBall.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                      .Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(cuePath[i].x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                      .Append(cuePath[i].y.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSvg(string path, List<(float eBall, List<Vector2> cuePath)> series)
        {
            const int w = 720;
            const int h = 360;
            const float half = 1.30f;   // 视图半宽（米）
            string[] colors = { "#1f77b4", "#2ca02c", "#ff7f0e", "#d62728", "#9467bd" };

            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(w).Append("\" height=\"").Append(h).Append("\">\n");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fafafa\"/>\n");
            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(w).Append("\" height=\"").Append(h)
              .Append("\" fill=\"none\" stroke=\"#999\"/>\n");
            sb.Append("<text x=\"10\" y=\"20\" font-size=\"14\">白球轨迹对比：球-球恢复系数 e_ball（台呢滚阻 0，库边 e=0.9）</text>\n");

            for (int k = 0; k < series.Count; k++)
            {
                (float eBall, List<Vector2> cuePath) = series[k];
                var pts = new StringBuilder();
                foreach (Vector2 p in cuePath)
                {
                    float px = (p.x / half) * (w * 0.5f) + (w * 0.5f);
                    float py = (p.y / half) * (h * 0.5f) + (h * 0.5f);
                    pts.Append(px.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                       .Append(py.ToString("F1", CultureInfo.InvariantCulture)).Append(' ');
                }

                sb.Append("<polyline fill=\"none\" stroke=\"").Append(colors[k % colors.Length])
                  .Append("\" stroke-width=\"1.5\" points=\"").Append(pts.ToString().TrimEnd()).Append("\"/>\n");
                sb.Append("<text x=\"").Append(10 + k * 130).Append("\" y=\"").Append(h - 10)
                  .Append("\" font-size=\"12\" fill=\"").Append(colors[k % colors.Length]).Append("\">e_ball=")
                  .Append(eBall.ToString("F2", CultureInfo.InvariantCulture)).Append("</text>\n");
            }

            sb.Append("</svg>\n");
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, List<(float eBall, List<Vector2> cuePath)> series)
        {
            var sb = new StringBuilder();
            sb.Append("参数扫描汇总（4 球开球，240 步 = 2 s @120 Hz；初态固定 = RackPositions(footSpot,3)）\n");
            sb.Append("e_ball | 白球行进路程(m) | 白球末速(m/s) | 末位(x,y)\n");
            foreach ((float eBall, List<Vector2> cuePath) in series)
            {
                float dist = 0f;
                for (int i = 1; i < cuePath.Count; i++)
                {
                    dist += (cuePath[i] - cuePath[i - 1]).magnitude;
                }

                Vector2 last = cuePath[cuePath.Count - 1];
                sb.Append(eBall.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append(dist.ToString("F3", CultureInfo.InvariantCulture)).Append(" | ")
                  .Append("(非解析)").Append(" | ")
                  .Append(last.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(last.y.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
            }

            sb.Append("\n说明：本扫描**不含任何断言**，仅供调参参考（`乙-系数选定` 的复核手段）。\n");
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// **台呢滚阻扫描**：单球在**空台**上以 `MaxBallSpeed` 自由滚动 ⇒ 记录"到 `TableAtRest` 的步数/时间 + 行程"。
        /// 空台（无段无袋）是为**隔离** `rollingDeceleration` 的作用（不被碰撞耗散干扰）。**无断言**、可复现。
        /// 另附 `DeriveSubSteps` 对子步界的确认。
        /// </summary>
        private static void WriteRollingDecelerationSweep(string dir)
        {
            var empty = ScriptableObject.CreateInstance<TableConfig>();
            empty.segments = new List<CushionSegment>();
            empty.pockets = new List<Pocket>();

            float[] decels = { 0f, 0.05f, 0.10f, 0.15f, 0.20f, 0.30f, 0.50f };
            float[] speeds = { 4f, 5f, 6f, 8f };
            const int cap = 3600;

            PhysicsTuning.Defaults d = PhysicsTuning.DefaultValues();
            var csv = new StringBuilder();
            csv.Append("v0,rollingDeceleration,stepsToRest,timeToRestS,travelM,rested\n");

            foreach (float v0 in speeds)
            {
                foreach (float a in decels)
                {
                    var p = new PhysicsParams
                    {
                        cushionRestitution = d.cushionRestitution,
                        cushionTangentialFriction = d.cushionTangentialFriction,
                        ballRestitution = d.ballRestitution,
                        ballTangentialFriction = d.ballTangentialFriction,
                        rollingDeceleration = a,
                    };

                    var sim = new PoolSimulator(empty, p);
                    sim.LoadBalls(new[]
                    {
                        new BallState { position = Vector2.zero, velocity = new Vector2(v0, 0f) },
                    });
                    sim.ResetDiagnostics();

                    int steps = 0;
                    float travel = 0f;
                    Vector2 prev = sim.Balls[0].position;
                    while (steps < cap && !sim.TableAtRest)
                    {
                        sim.Step();
                        steps++;
                        Vector2 cur = sim.Balls[0].position;
                        travel += (cur - prev).magnitude;
                        prev = cur;
                    }

                    csv.Append(v0.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                       .Append(a.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                       .Append(steps.ToString(CultureInfo.InvariantCulture)).Append(',')
                       .Append((steps / 120f).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                       .Append(travel.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                       .Append(sim.TableAtRest ? "1" : "0").Append('\n');
                }
            }

            File.WriteAllText(Path.Combine(dir, "param-scan-rolling.csv"), csv.ToString());

            int subSteps = PoolSimulator.DeriveSubSteps(PoolConstants.MaxBallSpeed);
            var txt = new StringBuilder();
            txt.Append("**滚阻 × 初速 交叉表**（单球、**空台**；每格 = 到 `TableAtRest`；上限 ")
               .Append(cap).Append(" 步 = ").Append(cap / 120).Append(" s；**不含任何断言**）\n\n");
            txt.Append("—— 时间 (s) 矩阵（行 = 初速 m/s，列 = 滚阻 m/s²）——\n");
            txt.Append("v0\\a |").Append(string.Join("|", System.Array.ConvertAll(decels, x => x.ToString("F2", CultureInfo.InvariantCulture)))).Append('\n');
            foreach (float v0 in speeds)
            {
                txt.Append(v0.ToString("F0", CultureInfo.InvariantCulture).PadLeft(4)).Append(" |");
                foreach (float a in decels)
                {
                    txt.Append(Cell(csv.ToString(), v0, a, 3)).Append('|');
                }

                txt.Append('\n');
            }

            txt.Append("\n—— 白球总行程 (m) 矩阵 ——\n");
            foreach (float v0 in speeds)
            {
                txt.Append(v0.ToString("F0", CultureInfo.InvariantCulture).PadLeft(4)).Append(" |");
                foreach (float a in decels)
                {
                    txt.Append(Cell(csv.ToString(), v0, a, 4)).Append('|');
                }

                txt.Append('\n');
            }

            txt.Append("\n**★ 满力那一杆（v0 = 8 m/s）**：");
            foreach (float a in decels)
            {
                txt.Append(" a=").Append(a.ToString("F2", CultureInfo.InvariantCulture))
                   .Append(" → ").Append(Cell(csv.ToString(), 8f, a, 3)).Append(" s / ")
                   .Append(Cell(csv.ToString(), 8f, a, 4)).Append(" m；");
            }

            txt.Append("\n\n**子步界确认**：`DeriveSubSteps` = ").Append(subSteps)
               .Append("，只依赖常量 `MaxBallSpeed = ").Append(PoolConstants.MaxBallSpeed)
               .Append("`（**与 `PhysicsTestDrive.maxLaunchSpeed` 无关**）⇒ **只降 `maxLaunchSpeed` 不影响子步数**。\n");
            txt.Append("⚠ **但不得把 `PoolConstants.MaxBallSpeed` 降下来** —— 它喂 `DeriveSubSteps`：\n")
               .Append("  v0=4 ⇒ nA = ceil(v·dt/r) = ")
               .Append(Mathf.CeilToInt((4f * (1f / 120f)) / (PoolConstants.BallDiameter * 0.5f)))
               .Append(" ⇒ `SubSteps` 由 3 变 2 ⇒ 会打破 `FixedDeltaTimeAndSubSteps_ArePinnedByIndependentLiterals`（钉字面量 3）。\n");
            txt.Append("说明：**选值由主理人 + 用户拍板**；本扫描不含任何断言。\n");
            File.WriteAllText(Path.Combine(dir, "param-scan-rolling-summary.txt"), txt.ToString());

            Debug.Log("滚阻 × 初速 交叉表 → " + Path.Combine(dir, "param-scan-rolling.csv"));
        }

        /// <summary>从 CSV 文本里取某格的第 <paramref name="column"/> 列（0 基）。**纯读，无断言**。</summary>
        private static string Cell(string csv, float v0, float a, int column)
        {
            foreach (string line in csv.Split('\n'))
            {
                string[] parts = line.Split(',');
                if (parts.Length > column && parts[0] == v0.ToString("F0", CultureInfo.InvariantCulture)
                    && parts[1] == a.ToString("F2", CultureInfo.InvariantCulture))
                {
                    return parts[column];
                }
            }

            return "?";
        }
    }
}
