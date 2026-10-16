using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// **构建产物冒烟模式**（验收工具，不属于游戏玩法）：命令行开关 → 跑满 N 帧 → 写 JSON → 退出。
    ///
    /// ── 为什么需要它 ─────────────────────────────────────────────────────
    /// "构建包可脱离编辑器独立运行 + 1080p 稳定 60 fps"这两条**只能在构建产物里**回答：
    /// 编辑器内计时受调试开销影响，`-batchmode`/`-nographics` 又不渲染。本模式让产物**自己**跑真渲染若干帧、
    /// 把逐帧毫秒与渲染器/材质计数写成 **JSON 证据**，并用**退出码**表达通过与否 ⇒ 验收变成可判定的。
    ///
    /// 用法（对构建出的 exe）：
    /// <code>
    /// Idea35PoolRoom.exe -billiards-smoke 600 -billiards-smoke-out D:/evidence/smoke.json
    /// </code>
    /// · `-billiards-smoke &lt;N&gt;`：跑 N 帧后结束；缺省或非法 ⇒ **不进入冒烟模式**（游戏照常运行）；
    /// · `-billiards-smoke-out &lt;path&gt;`：JSON 落盘路径；缺省 ⇒ `&lt;工程数据目录同级&gt;/smoke.json`；
    /// · 退出码：**0 = 达标**，**1 = 未达标**（帧率或掉帧超阈值），**2 = 写盘失败**。
    ///
    /// ⚠ **判定口径写死在 <see cref="FrameBudgetMs"/> / <see cref="MaxDroppedFraction"/>**，
    /// 事后不得放宽；"稳定"的定义就是这两条（见 `Docs/35-A_Day16_性能与打包_spec.md` §4.2）。
    ///
    /// ── 纯逻辑与运行期分离 ───────────────────────────────────────────────
    /// <see cref="TryParse"/> / <see cref="ComputeStats"/> / <see cref="MeetsTarget"/> 是**纯函数**
    /// （判据 `T121` 直接驱动，不需要真的跑构建产物）；<see cref="Bootstrap"/> 只负责把开关接上运行期。
    /// **全程无随机数**（统计是排序取分位，确定性）。
    /// </summary>
    public static class SmokeProbe
    {
        /// <summary>启用开关（值 = 帧数）。</summary>
        public const string FramesSwitch = "-billiards-smoke";

        /// <summary>JSON 落盘路径开关（可选）。</summary>
        public const string OutputSwitch = "-billiards-smoke-out";

        /// <summary>
        /// **拖动瞄准负载开关**（无值标志，可选）。带上它 ⇒ 采样期间**每帧连续改变瞄准角**
        /// （<see cref="AimSweepDegrees"/>）⇒ 逼出"拖动瞄准线"这一路的最坏帧时。
        ///
        /// 动机：原来的冒烟只测**静置** 600 帧 ⇒ 击球 / 拖动瞄准的峰值**从未被测过**（既有登记盲带）。
        /// 瞄准预测线在"每帧都变"时才触发重算（见 <c>AimAssistController</c> 的脏标记），
        /// 静置帧根本不走那条路径。
        /// </summary>
        public const string AimSweepSwitch = "-billiards-smoke-aim";

        /// <summary>拖动瞄准时每帧推进的瞄准角（度）。4.5°/帧 ⇒ 60 fps 下 270°/s（快速连续拖动）。</summary>
        public const float AimSweepDegreesPerFrame = 4.5f;

        /// <summary>
        /// **连续拖动瞄准角的纯函数**（判据可直接驱动）：第 <paramref name="frame"/> 帧的瞄准角（度，[0,360)）。
        /// 逐帧**单调递增**（模 360）⇒ 保证指纹每帧都变 ⇒ 触发预测重算路径。
        /// </summary>
        public static float AimSweepDegrees(int frame)
        {
            float raw = frame * AimSweepDegreesPerFrame;
            float wrapped = raw % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        /// <summary>60 fps 的单帧预算（ms）。</summary>
        public const float FrameBudgetMs = 1000f / 60f;

        /// <summary>超预算占比上限的**分子**（2 ⇒ 2%）：口径的真源是这对整数。</summary>
        public const int DroppedFractionNumerator = 2;

        /// <summary>超预算占比上限的**分母**（100）。</summary>
        public const int DroppedFractionDenominator = 100;

        /// <summary>
        /// 允许的"超预算帧"占比上限（写死口径）——**由上面那对整数派生**，不是第二份真源；
        /// 仅供报告/JSON 展示用，**判定一律用整数比较**（见 <see cref="MeetsTarget"/> 的实测教训）。
        /// </summary>
        public const float MaxDroppedFraction = (float)DroppedFractionNumerator / DroppedFractionDenominator;

        /// <summary>缺省 JSON 文件名。</summary>
        public const string DefaultOutputFileName = "smoke.json";

        /// <summary>
        /// **解析命令行**（纯函数）。找到 <see cref="FramesSwitch"/> 且其后是一个 **≥ 2** 的整数才进入冒烟模式；
        /// 缺参数、非整数、≤ 1 ⇒ 返回 <c>false</c>（调用方照常运行游戏）。
        /// </summary>
        public static bool TryParse(string[] args, out int frames, out string outputPath)
        {
            return TryParse(args, out frames, out outputPath, out _);
        }

        /// <summary>
        /// **解析命令行**（纯函数，含拖动瞄准开关）。找到 <see cref="FramesSwitch"/> 且其后是一个 **≥ 2** 的整数
        /// 才进入冒烟模式；缺参数、非整数、≤ 1 ⇒ 返回 <c>false</c>（调用方照常运行游戏）。
        /// <paramref name="aimSweep"/> = 命令里出现过 <see cref="AimSweepSwitch"/>（无值标志）。
        /// </summary>
        public static bool TryParse(string[] args, out int frames, out string outputPath, out bool aimSweep)
        {
            frames = 0;
            outputPath = null;
            aimSweep = false;
            if (args == null)
            {
                return false;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == AimSweepSwitch)
                {
                    aimSweep = true;
                }
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != FramesSwitch)
                {
                    continue;
                }

                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out frames) || frames < 2)
                {
                    return false;
                }

                for (int j = 0; j < args.Length - 1; j++)
                {
                    if (args[j] == OutputSwitch)
                    {
                        outputPath = args[j + 1];
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// **帧时统计**（纯函数）：均值 / p95 / 最大 / 超预算帧数。
        /// p95 取"升序后第 `ceil(0.95·n) − 1` 个"（确定性定义，不用插值）。
        /// </summary>
        public static void ComputeStats(IReadOnlyList<float> frameMs, out float mean, out float p95, out float max,
            out int droppedOverBudget)
        {
            mean = 0f;
            p95 = 0f;
            max = 0f;
            droppedOverBudget = 0;
            if (frameMs == null || frameMs.Count == 0)
            {
                return;
            }

            var sorted = new List<float>(frameMs.Count);
            double sum = 0.0;
            for (int i = 0; i < frameMs.Count; i++)
            {
                float v = frameMs[i];
                sorted.Add(v);
                sum += v;
                if (v > max)
                {
                    max = v;
                }

                if (v > FrameBudgetMs)
                {
                    droppedOverBudget++;
                }
            }

            sorted.Sort();
            mean = (float)(sum / frameMs.Count);
            int index = Mathf.Clamp(Mathf.CeilToInt(0.95f * sorted.Count) - 1, 0, sorted.Count - 1);
            p95 = sorted[index];
        }

        /// <summary>**达标判定**（纯函数）：帧时均值 ≤ 单帧预算 **且** 超预算帧占比 ≤ 上限。</summary>
        public static bool MeetsTarget(float meanMs, int droppedOverBudget, int frames)
        {
            if (frames <= 0)
            {
                return false;
            }

            // ⚠ **边界必须用整数比较**（实测教训，不得改回浮点除法）：
            //   写成单表达式 `(float)dropped / frames <= MaxDroppedFraction` 时，Mono 会把除法中间结果
            //   保持**更高精度**（double/扩展）再与已四舍五入的 float 常量比较 ⇒ 在"占比恰等于上限"这一格
            //   会因 1 个 ULP 判为不通过（实测：位模式两边都等于 `0x3CA3D70A`、单独比较为 true，
            //   但快照表达式返回 false；拆成先存 `float` 局部再比较则为 true）。
            //   ⇒ 口径本身是**分数**（2/100），直接按整数比值比较，彻底绕开浮点域。
            return meanMs <= FrameBudgetMs
                   && droppedOverBudget * DroppedFractionDenominator <= DroppedFractionNumerator * frames;
        }

        // ── 拖动瞄准路径的**宽松闸门** ────────────────────────────────────────
        //
        // 为什么单独一条口径（不是"把静置闸门放宽"）：
        //  · **静置闸门**（`MeetsTarget`：mean ≤ 16.67 ms、掉帧 ≤ 2%）**原样保留**，仍只用于静置冒烟；
        //  · **拖动瞄准**是本工程**已知最重的每帧路径** —— 每次重算要跑一次**最多 240 步**的预测
        //    （内核单步 ~0.37 ms ⇒ 单次重算 ~数十毫秒，属内核固有成本）。在这个成本量级上要求
        //    "60 fps 且掉帧 ≤ 2%"**结构上不可达**（一次同步重算就吃掉数帧）⇒ 若沿用静置闸门，
        //    得到的只会是**恒红**，等于没有判据。
        //  · ⇒ 本闸门只拦**量级失控**（回归到"每帧都重算"或"更糟"会被抓住），阈值取实测均值的 ~2 倍余量。
        //    **它不声称 60 fps** —— 拖动路径的帧率目标需先裁定"是否接受较短的拖动期预测"（见规格 §盲带）。

        /// <summary>拖动瞄准的**均值上界**（ms）。实测均值 ~24 ms ⇒ 取 50 ms（~2 倍余量，只拦量级失控）。</summary>
        public const float DragFrameBudgetMs = 50f;

        /// <summary>拖动瞄准的**掉帧占比上限分子**（4 ⇒ 40%）。</summary>
        public const int DragDroppedFractionNumerator = 4;

        /// <summary>拖动瞄准的**掉帧占比上限分母**（10）。</summary>
        public const int DragDroppedFractionDenominator = 10;

        /// <summary>拖动瞄准的掉帧占比上限（派生展示值；判定一律用整数比较）。</summary>
        public const float DragMaxDroppedFraction =
            (float)DragDroppedFractionNumerator / DragDroppedFractionDenominator;

        /// <summary>**拖动瞄准的达标判定**（纯函数，宽松闸门）：均值 ≤ <see cref="DragFrameBudgetMs"/> 且掉帧占比 ≤ 上限。</summary>
        public static bool MeetsDragTarget(float meanMs, int droppedOverBudget, int frames)
        {
            if (frames <= 0)
            {
                return false;
            }

            return meanMs <= DragFrameBudgetMs
                   && droppedOverBudget * DragDroppedFractionDenominator <= DragDroppedFractionNumerator * frames;
        }

        /// <summary>把一次冒烟采样的结果序列化为 JSON（纯函数，字段顺序固定，数字用不变文化）。</summary>
        public static string ToJson(SmokeResult result)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\n");
            sb.Append("  \"unityVersion\": \"").Append(result.unityVersion).Append("\",\n");
            sb.Append("  \"width\": ").Append(result.width).Append(",\n");
            sb.Append("  \"height\": ").Append(result.height).Append(",\n");
            sb.Append("  \"fullScreen\": ").Append(result.fullScreen ? "true" : "false").Append(",\n");
            sb.Append("  \"vSyncCount\": ").Append(result.vSyncCount).Append(",\n");
            sb.Append("  \"targetFrameRate\": ").Append(result.targetFrameRate).Append(",\n");
            sb.Append("  \"frames\": ").Append(result.frames).Append(",\n");
            sb.Append("  \"meanMs\": ").Append(F(result.meanMs)).Append(",\n");
            sb.Append("  \"p95Ms\": ").Append(F(result.p95Ms)).Append(",\n");
            sb.Append("  \"maxMs\": ").Append(F(result.maxMs)).Append(",\n");
            sb.Append("  \"fpsMean\": ").Append(F(result.fpsMean)).Append(",\n");
            sb.Append("  \"droppedOverBudget\": ").Append(result.droppedOverBudget).Append(",\n");
            sb.Append("  \"frameBudgetMs\": ").Append(F(FrameBudgetMs)).Append(",\n");
            sb.Append("  \"rendererCount\": ").Append(result.rendererCount).Append(",\n");
            sb.Append("  \"distinctMaterialCount\": ").Append(result.distinctMaterialCount).Append(",\n");
            sb.Append("  \"physicsSteps\": ").Append(result.physicsSteps).Append(",\n");
            sb.Append("  \"aimSweep\": ").Append(result.aimSweep ? "true" : "false").Append(",\n");
            sb.Append("  \"gate\": \"").Append(result.gate).Append("\",\n");
            sb.Append("  \"passed\": ").Append(result.passed ? "true" : "false").Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// 最近一次**由驱动器上报**的累计物理步数（只作冒烟证据的一个字段，不参与判定）。
        /// 由 <c>PhysicsTestDrive</c> 每帧上报 ⇒ 依赖方向是 **Physics → Core**（下层不反向依赖上层）。
        /// </summary>
        public static long PhysicsStepsReported { get; private set; }

        /// <summary>驱动器上报累计物理步数（幂等覆盖，不累加）。</summary>
        public static void ReportPhysicsSteps(long totalSteps)
        {
            PhysicsStepsReported = totalSteps;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!TryParse(System.Environment.GetCommandLineArgs(), out int frames, out string outputPath,
                    out bool aimSweep))
            {
                return;
            }

            var go = new GameObject("SmokeProbe");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SmokeProbeRunner>().Begin(frames, outputPath, aimSweep);
        }
    }

    /// <summary>
    /// **冒烟模式下"可被脚本化驱动瞄准角"的宿主接口**。
    ///
    /// <para>**为什么是 Core 里的接口**：冒烟执行体（<see cref="SmokeProbeRunner"/>）位于 Core，
    /// 而驱动器（`PhysicsTestDrive`）位于 Physics —— 依赖方向是 **Physics → Core**（不得反向）。
    /// 若 Core 直接引用 `PhysicsTestDrive`，就会形成**反向依赖**（违 ADR / 工程标准 R1）。
    /// 故由 **Core 定义接口、Physics 实现** ⇒ 运行时用 <c>is</c> 匹配即可，无需反射、无类型环。</para>
    /// </summary>
    public interface ISmokeAimSweepTarget
    {
        /// <summary>把瞄准角（度）设为 <paramref name="degrees"/>（供冒烟模式脚本化拖动）。</summary>
        void ApplySmokeAimDegrees(float degrees);
    }

    /// <summary>一次冒烟采样的结果（JSON 字段见 <see cref="SmokeProbe.ToJson"/>）。</summary>
    public struct SmokeResult
    {
        public string unityVersion;
        public int width;
        public int height;
        public bool fullScreen;
        public int vSyncCount;
        public int targetFrameRate;
        public int frames;
        public float meanMs;
        public float p95Ms;
        public float maxMs;
        public float fpsMean;
        public int droppedOverBudget;
        public int rendererCount;
        public int distinctMaterialCount;
        public long physicsSteps;
        public bool aimSweep;

        /// <summary>本次采样适用的判定口径名（`static` / `drag`；见 <see cref="MeetsTarget"/> / <see cref="MeetsDragTarget"/>）。</summary>
        public string gate;
        public bool passed;
    }
}
