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
    /// · 退出码：**0 = 达标**，**1 = 未达标**（帧时闸门或场景预算超限），**2 = 写盘失败**，
    ///   **3 = 环境不洁**（机器忙 ⇒ **不出具判定**，见 <see cref="IsPremiumMachine"/> —— 独立码，**不混入 0/1**）。
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

        // ── 冒烟态对"启动门"的抑制（开始界面）────────────────────────────────
        //
        // **为什么必须抑制**：两个冒烟都是**无人操作跑 N 帧**的验收工具。开始界面（首页）默认
        // **启动即显示** ⇒ 若不让位，采到的就不是游戏内负载。更硬的一条：
        // `AimAssistController.ShouldShow` 会被 `HomeView.IsOpen` 整体让位掉 ⇒ 预测路径**根本不执行**
        // ⇒ `-billiards-smoke-aim` 测到的是"**没有预测的帧**" —— 那不是"数字偏乐观"，是**测错了对象**。
        //
        // **为什么挂在冒烟入口里、而不是命令行开关**：显式开关一旦有人手跑冒烟时忘了带，
        // 就会**静默**去测菜单（正是最该避免的陷阱）；挂在入口上则**不可能被忘掉**。
        //
        // **为什么 Core 只提供"事实"、不直接去关首页**：依赖方向是 **UI → Core**（不得反向）
        // ⇒ Core 不能引用 `Billiards.UI.HomeView`。故这里只置一个**中性的进程事实**
        // （"本次进程是冒烟模式"），由首页在自己的 `Awake` 里读它并让位。

        /// <summary>
        /// **本次进程是否处于冒烟模式**（由 <see cref="ParseEarly"/> 在**场景加载前**置位）。
        /// 启动门（开始界面）在 `Awake` 里读它 ⇒ 必须在场景加载**之前**就有值。
        /// </summary>
        public static bool IsSmokeMode { get; private set; }

        /// <summary>`homeStartup` 字段：首页**确实显示**了（非冒烟态的常规启动）。</summary>
        public const string HomeStartupShown = "shown";

        /// <summary>`homeStartup` 字段：首页被**冒烟态抑制**了（这是冒烟应有的取值）。</summary>
        public const string HomeStartupSuppressed = "suppressed";

        /// <summary>
        /// `homeStartup` 字段：**没有任何一方上报** —— 即场景里压根没有首页宿主
        /// （接线缺失/旧场景）。**它不是"通过"**：冒烟证据里出现它就意味着"抑制"这件事没人证明。
        /// </summary>
        public const string HomeStartupUnreported = "unreported";

        /// <summary>
        /// 首页启动显示的报告（写进冒烟 JSON 的 `homeStartup`，取值见三个常量）。
        /// **它是"抑制确实发生"的可验证证据** —— 光写注释不算证明。
        /// </summary>
        public static string HomeStartupReport { get; private set; } = HomeStartupUnreported;

        /// <summary>由首页（<c>HomeView.Awake</c>）上报一次启动显示的实际走向。</summary>
        public static void ReportHomeStartup(string state)
        {
            HomeStartupReport = state;
        }

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

        // ── 判定口径：**统一为严格闸门**（拖动路径与静置路径同判）────────────────
        //
        // ── 口径变更（登记，含前后两套数字）───────────────────────────────────
        // **旧**：拖动路径用一条**宽松闸门** `MeetsDragTarget`（均值 ≤ 50 ms、掉帧 ≤ 40%）。
        //   它当时是**必要的**：内核每子步无条件扫 `每球 × 全部 82 段` ⇒ 一次 240 步预测要数十毫秒，
        //   "60 fps 且掉帧 ≤ 2%"在该成本量级下**结构上不可达**。
        //   **实测（旧）**：拖动 mean **24.9 ms（40 fps）/ p95 76.3 / 掉帧 162**。
        // **新**：批次 C 给内核加了**保守包围盒预筛**（等价、逐位不变）⇒ 拖动 **mean 6.96 ms（143.8 fps）
        //   / p95 6.97 / 掉帧 0** ⇒ 已达到"60 fps 且掉帧 ≤ 2%" ⇒ **与静置同用 `MeetsTarget`**。
        // ⇒ 宽松闸门与其两个阈值常量**已删除**（**不留"已作废"的死闸门** —— 留着必被后人误用）。
        // 判据：`T131` 用**独立字面量**钉住 `MeetsTarget` 的两个阈值（见该测试），并断言本类
        // **不再存在** `MeetsDragTarget`（源码级守卫）。

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

        /// <summary>
        /// **达标判定**（纯函数）：帧时均值 ≤ 单帧预算 **且** 超预算帧占比 ≤ 上限。
        /// **静置与拖动两条负载现在同用此闸门**（批次 C 后拖动已达 60 fps；口径变更与前后数字见上文的"口径变更"）。
        /// </summary>
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

        // ── 场景预算（渲染器 / 材质数量）──────────────────────────────────────
        //
        // **为什么加**：拖动路径早期实测 `rendererCount` 从 67 涨到 **260**（瞄准线虚线池按"每个路径取样点一段"
        // 膨胀）—— 那类**泄漏式膨胀**让帧时慢慢变差，而"只报告、不设上限"等于没人管。
        // 观测：静置 27–34、拖动 34 ⇒ 上限 **60**（约 2× 余量）；不同材质 21 ⇒ 上限 **30**。

        /// <summary>采样期**最大渲染器数**上限（观测 27–34 ⇒ 取 60，约 2× 余量）。</summary>
        public const int MaxRendererCount = 60;

        /// <summary>采样期**最大不同材质数**上限（观测 21 ⇒ 取 30）—— 副守卫。</summary>
        public const int MaxDistinctMaterialCount = 30;

        /// <summary>**场景预算判定**（纯函数）：渲染器与材质数量的**峰值**都不得超上限。</summary>
        public static bool MeetsSceneBudget(int maxRendererCount, int maxDistinctMaterialCount)
        {
            return maxRendererCount <= MaxRendererCount
                   && maxDistinctMaterialCount <= MaxDistinctMaterialCount;
        }

        // ── 机器分类：把"产物慢"与"机器忙"分开 ───────────────────────────────
        //
        // **为什么需要**：实测同一份产物在**忙机**上静置 `mean 12.63 ms / 掉帧 59`（FAIL）、空载重跑
        // `8.33 / 12`（PASS）⇒ **机器负载会被读成"产物变慢"**，是纯粹的假红源。
        // **判据（相对量，不需要机器参照）**：`meanMs <= minMs × LoadRatioCeiling`。
        //   · 空载（vSync 生效）：帧时被刷新率夹住 ⇒ 分布极紧 ⇒ `min ≈ mean` ⇒ 比值 ≈ 1 ⇒ **premium**；
        //   · 忙机：少数帧被外部抢占拉长 ⇒ `mean` 抬升而 `min` 不动 ⇒ 比值升高 ⇒ **loaded**。
        //   ⇒ "**各帧一样慢**"（真的产物慢）比值仍 ≈ 1 ⇒ 照常判定 ⇒ 两类可分。
        // ⚠ **盲带（如实）**：若负载表现为**均匀**变慢（整机降频），比值不升 ⇒ 分不出 ⇒ 仍会红。
        //   ⇒ 冒烟**必须在空载下跑**；本判据只是**兜底**，不能替代空载。

        /// <summary>`mean / min` 的上界：超过即判机器忙（判据与盲带见上）。</summary>
        public const float LoadRatioCeiling = 1.35f;

        /// <summary>**机器是否"干净"**（纯函数）：比值不超上限 ⇒ 可判；否则"环境不洁"。</summary>
        public static bool IsPremiumMachine(float meanMs, float minMs)
        {
            if (!(minMs > 0f))
            {
                return false;
            }

            return meanMs <= minMs * LoadRatioCeiling;
        }

        /// <summary>
        /// **"环境不洁"的独立退出码**：机器忙 ⇒ **不出具判定**（既不算 PASS 也不算 FAIL），
        /// 用一个**与 0/1 不同**的码退出 ⇒ 不会被误读成"产物变慢"。
        /// </summary>
        public const int ExitCodeEnvUnclean = 3;

        // ── 多轮采样（`best-of-3` 兜底）──────────────────────────────────────
        //
        // 单轮可能撞上一次外部抢占 ⇒ 多跑几轮、取**最好**那轮作判定，并把 min/median/max **一起**写进 JSON
        // （只看"最好那轮"会藏住离散度；三者并列才看得出稳定性）。

        /// <summary>采样轮数（`best-of-3`）。</summary>
        public const int Rounds = 3;

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
            sb.Append("  \"minMs\": ").Append(F(result.minMs)).Append(",\n");
            sb.Append("  \"p95Ms\": ").Append(F(result.p95Ms)).Append(",\n");
            sb.Append("  \"maxMs\": ").Append(F(result.maxMs)).Append(",\n");
            sb.Append("  \"fpsMean\": ").Append(F(result.fpsMean)).Append(",\n");
            sb.Append("  \"droppedOverBudget\": ").Append(result.droppedOverBudget).Append(",\n");
            sb.Append("  \"frameBudgetMs\": ").Append(F(FrameBudgetMs)).Append(",\n");
            sb.Append("  \"loadRatio\": ").Append(F(result.loadRatio)).Append(",\n");
            sb.Append("  \"loadRatioCeiling\": ").Append(F(LoadRatioCeiling)).Append(",\n");
            sb.Append("  \"machineClass\": \"").Append(result.machineClass).Append("\",\n");
            sb.Append("  \"rounds\": ").Append(result.rounds).Append(",\n");
            sb.Append("  \"roundMeanMsMin\": ").Append(F(result.roundMeanMsMin)).Append(",\n");
            sb.Append("  \"roundMeanMsMedian\": ").Append(F(result.roundMeanMsMedian)).Append(",\n");
            sb.Append("  \"roundMeanMsMax\": ").Append(F(result.roundMeanMsMax)).Append(",\n");
            sb.Append("  \"rendererCount\": ").Append(result.rendererCount).Append(",\n");
            sb.Append("  \"rendererCountLimit\": ").Append(MaxRendererCount).Append(",\n");
            sb.Append("  \"distinctMaterialCount\": ").Append(result.distinctMaterialCount).Append(",\n");
            sb.Append("  \"distinctMaterialCountLimit\": ").Append(MaxDistinctMaterialCount).Append(",\n");
            sb.Append("  \"physicsSteps\": ").Append(result.physicsSteps).Append(",\n");
            sb.Append("  \"predictionComputeCount\": ").Append(result.predictionComputeCount).Append(",\n");
            sb.Append("  \"totalPredictSteps\": ").Append(result.totalPredictSteps).Append(",\n");
            sb.Append("  \"aimSweep\": ").Append(result.aimSweep ? "true" : "false").Append(",\n");
            sb.Append("  \"homeStartup\": \"")
                .Append(string.IsNullOrEmpty(result.homeStartup) ? HomeStartupUnreported : result.homeStartup)
                .Append("\",\n");
            sb.Append("  \"gate\": \"").Append(result.gate).Append("\",\n");
            sb.Append("  \"frameGateOk\": ").Append(result.frameGateOk ? "true" : "false").Append(",\n");
            sb.Append("  \"sceneBudgetOk\": ").Append(result.sceneBudgetOk ? "true" : "false").Append(",\n");
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

        /// <summary>
        /// 最近一次**由驱动器上报**的预测成本 = `(重算次数, 累计预测步数)`（只作冒烟证据字段，不参与判定）。
        ///
        /// **为什么用"步数"而不是 ms**（`G6` 的动机）：毫秒是**机器相关**的 —— 同一份代码在忙机 / 空闲机的
        /// 均值能差 ~2×（实测 8.33 ↔ 12.63 ms）⇒ 用 ms 比较"改动前后谁更贵"会被机器差异污染。
        /// 而"**重算了几次、累计步进了多少步**"是**纯计数** ⇒ **与机器无关** ⇒ 可以直接跨运行比较。
        /// ⇒ 本项与 <see cref="PhysicsStepsReported"/> 同族（**Physics → Core** 方向上报，无反向依赖）。
        /// </summary>
        public static long PredictComputeReported { get; private set; }

        /// <summary>见 <see cref="PredictComputeReported"/>。</summary>
        public static long PredictStepsReported { get; private set; }

        /// <summary>驱动器上报预测成本（幂等覆盖，不累加）。</summary>
        public static void ReportPredictionCost(long computeCount, long totalSteps)
        {
            PredictComputeReported = computeCount;
            PredictStepsReported = totalSteps;
        }

        /// <summary>
        /// **在场景加载前**解析一次命令行、把"冒烟模式"这个事实置上。
        ///
        /// ⚠ **时序是硬约束**：场景对象的 `Awake` 早于 <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/>
        /// ⇒ 若只在 <see cref="Bootstrap"/> 里（AfterSceneLoad）解析，首页的 `Awake` 早就跑完了
        /// （那时它已经显示出来了）—— 抑制会**静默失效**，而这正是"看起来做了、其实没做"的典型死法。
        /// 故这里用 **BeforeSceneLoad**。
        ///
        /// 本方法**只置位、不建对象**（建对象仍在 <see cref="Bootstrap"/> 里做）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ParseEarly()
        {
            if (TryParse(System.Environment.GetCommandLineArgs(), out _, out _, out _))
            {
                IsSmokeMode = true;
            }
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

        /// <summary>判定所用均值 = **最好那一轮**的均值（多轮 `best-of-3` 兜底；见 <see cref="SmokeProbe.Rounds"/>）。</summary>
        public float meanMs;

        /// <summary>最好那一轮的**最小帧时**（`mean/min` 比值用于机器分类；见 <see cref="SmokeProbe.IsPremiumMachine"/>）。</summary>
        public float minMs;

        public float p95Ms;
        public float maxMs;
        public float fpsMean;
        public int droppedOverBudget;

        /// <summary>`mean / min`（机器分类量；判据见 <see cref="SmokeProbe.LoadRatioCeiling"/>）。</summary>
        public float loadRatio;

        /// <summary>`premium`（可判）/ `loaded`（机器忙 ⇒ 环境不洁，不出具判定）。</summary>
        public string machineClass;

        /// <summary>实际采样轮数。</summary>
        public int rounds;

        /// <summary>各轮均值的 min / median / max —— **三者并列**才看得出离散度（只报"最好那轮"会藏住它）。</summary>
        public float roundMeanMsMin;
        public float roundMeanMsMedian;
        public float roundMeanMsMax;

        /// <summary>采样期**逐帧取最大值**的渲染器数（**峰值**；旧实现只在结束时取一次快照 ⇒ 抓不到峰值）。</summary>
        public int rendererCount;

        /// <summary>采样期**逐帧取最大值**的不同材质数（**峰值**）。</summary>
        public int distinctMaterialCount;

        public long physicsSteps;

        /// <summary>驱动器上报的**预测重算次数**（与机器无关的"成本"量之一）。</summary>
        public long predictionComputeCount;

        /// <summary>驱动器上报的**累计预测步数**（**与机器无关**的成本量 —— 比 ms 更可靠，见 `G6` 的动机）。</summary>
        public long totalPredictSteps;

        public bool aimSweep;

        /// <summary>
        /// 启动门（开始界面）的实际走向：<see cref="SmokeProbe.HomeStartupSuppressed"/>（冒烟应有的取值）/
        /// <see cref="SmokeProbe.HomeStartupShown"/>（非冒烟启动）/ <see cref="SmokeProbe.HomeStartupUnreported"/>
        /// （没人上报 = 场景里没有首页宿主）。
        /// **它是"冒烟确实把首页抑制掉了"的可验证证据**（只写注释不算）。
        /// 取值与语义见 `Docs/35-A_美术接入与UI重做_spec.md` §9.3。**不参与判定**（与 `passed` 无关）。
        /// </summary>
        public string homeStartup;

        /// <summary>本次采样适用的**负载名**（`static` / `drag`；两者**同判** `MeetsTarget`）。</summary>
        public string gate;

        /// <summary>帧时闸门结果（`MeetsTarget`）。</summary>
        public bool frameGateOk;

        /// <summary>场景预算结果（`MeetsSceneBudget`：渲染器 / 材质峰值不超上限）。</summary>
        public bool sceneBudgetOk;
        public bool passed;
    }
}
