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
    /// · **丢弃前 <see cref="WarmupFrames"/> 帧**：首帧要编着色器、要建可视物、要预热物理 ——
    ///   把它们算进去等于量"启动成本"而不是"稳态帧率"；
    /// · 之后连续采 <c>frames</c> 帧的 `Time.unscaledDeltaTime`（不受 timeScale 影响）；
    /// · 统计与判定走 <see cref="SmokeProbe.ComputeStats"/> / <see cref="SmokeProbe.MeetsTarget"/>（纯函数，判据 `T121` 驱动）；
    /// · 渲染器/材质计数在**采样结束时**取一次（`FindObjectsByType`，含未激活对象）。
    /// </summary>
    [AddComponentMenu("")]   // 不出现在 Add Component 菜单里：它只由命令行开关创建
    public sealed class SmokeProbeRunner : MonoBehaviour
    {
        /// <summary>被丢弃的预热帧数（首帧着色器编译/建物成本不计入稳态）。</summary>
        public const int WarmupFrames = 60;

        private readonly List<float> _frameMs = new List<float>(1024);
        private int _frames;
        private string _outputPath;
        private int _warmupLeft;

        /// <summary>由 <see cref="SmokeProbe.Bootstrap"/> 调用；也可被判据/探针直接调用。</summary>
        public void Begin(int frames, string outputPath)
        {
            _frames = frames;
            _outputPath = string.IsNullOrEmpty(outputPath)
                ? Path.Combine(Application.persistentDataPath, SmokeProbe.DefaultOutputFileName)
                : outputPath;
            _frameMs.Clear();
            _warmupLeft = WarmupFrames;
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

            _frameMs.Add(Time.unscaledDeltaTime * 1000f);
            if (_frameMs.Count < _frames)
            {
                return;
            }

            Finish();
        }

        private void Finish()
        {
            SmokeProbe.ComputeStats(_frameMs, out float mean, out float p95, out float max, out int dropped);
            bool passed = SmokeProbe.MeetsTarget(mean, dropped, _frameMs.Count);

            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var materials = new HashSet<Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material m = renderers[i].sharedMaterial;
                if (m != null)
                {
                    materials.Add(m);
                }
            }

            var result = new SmokeResult
            {
                unityVersion = Application.unityVersion,
                width = Screen.width,
                height = Screen.height,
                fullScreen = Screen.fullScreen,
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                frames = _frameMs.Count,
                meanMs = mean,
                p95Ms = p95,
                maxMs = max,
                fpsMean = mean > 0f ? 1000f / mean : 0f,
                droppedOverBudget = dropped,
                rendererCount = renderers.Length,
                distinctMaterialCount = materials.Count,
                physicsSteps = SmokeProbe.PhysicsStepsReported,
                passed = passed,
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
                Debug.Log($"[SMOKE] 写入 {_outputPath}：{result.frames} 帧 mean={result.meanMs.ToString("F3", CultureInfo.InvariantCulture)}ms "
                          + $"fps={result.fpsMean.ToString("F2", CultureInfo.InvariantCulture)} "
                          + $"dropped={dropped} passed={passed}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SMOKE] 写盘失败：{e.Message}");
                Application.Quit(2);
                return;
            }

            Application.Quit(passed ? 0 : 1);
        }
    }
}
