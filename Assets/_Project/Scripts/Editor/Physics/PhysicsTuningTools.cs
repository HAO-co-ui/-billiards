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

            // 扫描表：球-球恢复系数（台球实测 0.93–0.96，取上下界与中点）
            float[] eBalls = { 1.0f, 0.98f, 0.95f, 0.93f, 0.90f };
            var series = new List<(float eBall, List<Vector2> cuePath)>();

            foreach (float eBall in eBalls)
            {
                var p = new PhysicsParams
                {
                    cushionRestitution = 0.9f,
                    cushionTangentialFriction = 0.9f,
                    ballRestitution = eBall,
                    ballTangentialFriction = 1f,
                    rollingDeceleration = 0f,
                };

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
    }
}
