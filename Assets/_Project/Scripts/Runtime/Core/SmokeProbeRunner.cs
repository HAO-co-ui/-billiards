using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// <see cref="SmokeProbe"/> 的运行期执行体：数帧 → 采样 → 写 JSON → 退出码。
    ///
    /// ── 采样口径（写死，事后不得放宽）────────────────────────────────────
    /// · **丢弃前 <see cref="WarmupFrames"/> 帧**（只丢**一次**，在多轮之前）：首帧要编着色器、要建可视物、
    ///   要预热物理 —— 把它们算进去等于量"启动成本"而不是"稳态帧率"；
    /// · 之后跑 **<see cref="SmokeProbe.Rounds"/> 轮**，每轮连续采 <c>frames</c> 帧的
    ///   `Time.unscaledDeltaTime`（不受 timeScale 影响）；
    /// · **判定取"最好那一轮"**（均值最小）—— 单轮可能撞上一次外部抢占；同时把各轮均值的
    ///   **min / median / max 一起写进 JSON**（只看"最好那轮"会藏住离散度）；
    /// · 统计与判定走 <see cref="SmokeProbe.ComputeStats"/> / <see cref="SmokeProbe.MeetsTarget"/>
    ///   （纯函数）；**静置与拖动两条负载同判**；
    /// · 渲染器/材质计数**采样期逐帧取峰值**（旧实现只在结束时取一次快照 ⇒ 抓不到峰值）；
    /// · **机器分类**（<see cref="SmokeProbe.IsPremiumMachine"/>）：忙机 ⇒ 以
    ///   <see cref="SmokeProbe.ExitCodeEnvUnclean"/> **独立退出码**结束，**不出具 PASS/FAIL**。
    /// </summary>
    [AddComponentMenu("")]   // 不出现在 Add Component 菜单里：它只由命令行开关创建
    public sealed class SmokeProbeRunner : MonoBehaviour
    {
        /// <summary>被丢弃的预热帧数（首帧着色器编译/建物成本不计入稳态）。**只丢一次**（在多轮之前）。</summary>
        public const int WarmupFrames = 60;

        /// <summary>单轮统计（多轮取最好那轮作判定）。</summary>
        private struct RoundStats
        {
            public float mean;
            public float p95;
            public float max;
            public float min;
            public int dropped;
        }

        private readonly List<RoundStats> _rounds = new List<RoundStats>(SmokeProbe.Rounds);
        private readonly List<float> _frameMs = new List<float>(1024);
        private readonly HashSet<Material> _materialScratch = new HashSet<Material>();

        private int _frames;
        private string _outputPath;
        private int _warmupLeft;

        /// <summary>是否在采样期间连续改变瞄准角（拖动瞄准负载）。见 <see cref="SmokeProbe.AimSweepSwitch"/>。</summary>
        private bool _aimSweep;

        /// <summary>已采样的帧计数（**跨轮单调**，= 冒烟角函数的输入）。</summary>
        private int _sampled;

        /// <summary>本轮最小帧时（机器分类用；每轮重置）。</summary>
        private float _roundMin;

        /// <summary>采样期**峰值**渲染器数 / 材质数（逐帧取 max）。</summary>
        private int _maxRenderers;
        private int _maxMaterials;

        /// <summary>场景里可被脚本化驱动瞄准的目标（拖动负载用）；无 ⇒ <c>null</c>（不驱动）。</summary>
        private ISmokeAimSweepTarget _aimTarget;

        /// <summary>由 <see cref="SmokeProbe.Bootstrap"/> 调用；也可被判据/探针直接调用。</summary>
        public void Begin(int frames, string outputPath, bool aimSweep = false)
        {
            _frames = frames;
            _outputPath = string.IsNullOrEmpty(outputPath)
                ? Path.Combine(Application.persistentDataPath, SmokeProbe.DefaultOutputFileName)
                : outputPath;
            _frameMs.Clear();
            _rounds.Clear();
            _warmupLeft = WarmupFrames;
            _sampled = 0;
            _roundMin = 0f;
            _maxRenderers = 0;
            _maxMaterials = 0;
            _aimSweep = aimSweep;
            _aimTarget = null;

            if (!_aimSweep)
            {
                return;
            }

            // 一次查找即可（场景在 AfterSceneLoad 时已加载）。
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ISmokeAimSweepTarget target)
                {
                    _aimTarget = target;
                    break;
                }
            }

            if (_aimTarget == null)
            {
                Debug.LogWarning("[SMOKE] 已请求拖动瞄准负载，但场景里没有 ISmokeAimSweepTarget ⇒ 本项退化为静置。");
            }
        }

        private void Update()
        {
            if (_frames <= 0)
            {
                return;
            }

            if (_warmupLeft > 0)
            {
                _warmupLeft--;
                return;
            }

            // **拖动瞄准负载**：采样帧内每帧推进瞄准角（逼出"预测重算"这条最坏路径）。
            // 注意放在**采样帧**（预热之后）：静置 60 帧预热不计入统计。
            if (_aimSweep && _aimTarget != null)
            {
                _aimTarget.ApplySmokeAimDegrees(SmokeProbe.AimSweepDegrees(_sampled));
            }

            float ms = Time.unscaledDeltaTime * 1000f;
            _frameMs.Add(ms);
            if (_roundMin <= 0f || ms < _roundMin)
            {
                _roundMin = ms;
            }

            SampleScenePeaks();

            _sampled++;
            if (_frameMs.Count < _frames)
            {
                return;
            }

            // 本轮结束 ⇒ 记统计、开下一轮（**不清 `_sampled`**：拖动扫掠跨轮继续）。
            SmokeProbe.ComputeStats(_frameMs, out float mean, out float p95, out float max, out int dropped);
            _rounds.Add(new RoundStats { mean = mean, p95 = p95, max = max, min = _roundMin, dropped = dropped });
            _frameMs.Clear();
            _roundMin = 0f;

            if (_rounds.Count < SmokeProbe.Rounds)
            {
                return;
            }

            Finish();
        }

        /// <summary>
        /// **采样期逐帧取峰值**渲染器 / 材质数。
        /// 旧实现只在 <c>Finish</c> 里取**一次末帧快照** ⇒ 抓不到峰值（而"渲染器膨胀"正是最该抓的那类回归：
        /// 拖动路径曾从 67 涨到 260）。材质集用**复用**的 <see cref="HashSet{T}"/> ⇒ 不留每帧垃圾。
        /// </summary>
        private void SampleScenePeaks()
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            if (renderers.Length > _maxRenderers)
            {
                _maxRenderers = renderers.Length;
            }

            _materialScratch.Clear();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material m = renderers[i].sharedMaterial;
                if (m != null)
                {
                    _materialScratch.Add(m);
                }
            }

            if (_materialScratch.Count > _maxMaterials)
            {
                _maxMaterials = _materialScratch.Count;
            }
        }

        private void Finish()
        {
            // ── 取**最好那一轮**（均值最小）作判定（`best-of-3` 兜底）──
            int best = 0;
            for (int i = 1; i < _rounds.Count; i++)
            {
                if (_rounds[i].mean < _rounds[best].mean)
                {
                    best = i;
                }
            }

            RoundStats r = _rounds[best];

            bool frameGateOk = SmokeProbe.MeetsTarget(r.mean, r.dropped, _frames);
            bool sceneBudgetOk = SmokeProbe.MeetsSceneBudget(_maxRenderers, _maxMaterials);
            bool premium = SmokeProbe.IsPremiumMachine(r.mean, r.min);
            float loadRatio = r.min > 0f ? r.mean / r.min : 0f;

            // 各轮均值的 min / median / max（离散度；只报"最好那轮"会藏住它）
            var means = new List<float>(_rounds.Count);
            for (int i = 0; i < _rounds.Count; i++)
            {
                means.Add(_rounds[i].mean);
            }

            means.Sort();
            float rMin = means[0];
            float rMax = means[means.Count - 1];
            float rMedian = means.Count % 2 == 1
                ? means[means.Count / 2]
                : 0.5f * (means[means.Count / 2 - 1] + means[means.Count / 2]);

            var result = new SmokeResult
            {
                unityVersion = Application.unityVersion,
                width = Screen.width,
                height = Screen.height,
                fullScreen = Screen.fullScreen,
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                frames = _frames,
                meanMs = r.mean,
                minMs = r.min,
                p95Ms = r.p95,
                maxMs = r.max,
                fpsMean = r.mean > 0f ? 1000f / r.mean : 0f,
                droppedOverBudget = r.dropped,
                loadRatio = loadRatio,
                machineClass = premium ? "premium" : "loaded",
                rounds = _rounds.Count,
                roundMeanMsMin = rMin,
                roundMeanMsMedian = rMedian,
                roundMeanMsMax = rMax,
                rendererCount = _maxRenderers,
                distinctMaterialCount = _maxMaterials,
                physicsSteps = SmokeProbe.PhysicsStepsReported,
                predictionComputeCount = SmokeProbe.PredictComputeReported,
                totalPredictSteps = SmokeProbe.PredictStepsReported,
                aimSweep = _aimSweep,
                // 启动门（开始界面）的实际走向 —— **"冒烟确实抑制了首页"的可验证证据**。
                // 冒烟态下若读到 `unreported` 或 `shown`，就说明这次采样量的不是纯玩法负载。
                homeStartup = SmokeProbe.HomeStartupReport,
                gate = _aimSweep ? "drag" : "static",
                frameGateOk = frameGateOk,
                sceneBudgetOk = sceneBudgetOk,
                // **环境不洁 ⇒ 不出具判定**（`passed` 不成立，退出码另给 3）
                passed = premium && frameGateOk && sceneBudgetOk,
            };

            string json = SmokeProbe.ToJson(result);
            try
            {
                string dir = Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_outputPath, json);
                // `homeStartup` 必须打在这一行里：只写 JSON 不进日志时，肉眼看日志的人**看不出**
                // 这次到底测的是玩法还是开始界面（"注释不算证明"同样适用于日志）。
                if (result.homeStartup != SmokeProbe.HomeStartupSuppressed)
                {
                    Debug.LogWarning($"[SMOKE] ⚠ 首页启动显示未被抑制（homeStartup={result.homeStartup}）"
                                     + " ⇒ 本次采样量的可能不是纯玩法负载。");
                }

                Debug.Log($"[SMOKE] 写入 {_outputPath}：{result.frames}×{result.rounds} 帧 "
                          + $"mean={result.meanMs.ToString("F3", CultureInfo.InvariantCulture)}ms "
                          + $"fps={result.fpsMean.ToString("F2", CultureInfo.InvariantCulture)} "
                          + $"dropped={result.droppedOverBudget} loadRatio={result.loadRatio.ToString("F3", CultureInfo.InvariantCulture)} "
                          + $"machine={result.machineClass} renderers={result.rendererCount} "
                          + $"home={result.homeStartup} "
                          + $"predSteps={result.totalPredictSteps} passed={result.passed}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SMOKE] 写盘失败：{e.Message}");
                Application.Quit(2);
                return;
            }

            // **退出码**：环境不洁 ⇒ 独立码 3（**不混入 0/1** ⇒ 不会被读成"产物变慢"）；
            // 否则 0 = 达标 / 1 = 未达标。闸门明细已在 JSON 的 `frameGateOk`/`sceneBudgetOk` 里。
            if (!premium)
            {
                Application.Quit(SmokeProbe.ExitCodeEnvUnclean);
                return;
            }

            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
