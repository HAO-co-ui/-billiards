using System.IO;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **报告输出路径的唯一实现**（铁律 16）。
    ///
    /// **为什么要有这个类**：此前 TableIntegrityValidator 与 MaterialVisualValidator 各写了一份 `ResolveReportPath`，
    /// **签名相同、逻辑几乎相同，但已经分叉** ——
    /// `TableIntegrityValidator` 的版本调用 <c>Directory.CreateDirectory</c>，`MaterialVisualValidator` 的**没有**。
    /// 后果：`-outputDir` 指向一个不存在的目录时，前者能写、后者会失败。
    /// 之前没暴露**只是因为测试目录恰好已存在**（调用顺序的巧合，不是设计）——
    /// 与 `SCN-04`/`SCN-06` 是同一类缺陷（本工程第五次）。
    ///
    /// 本类同时补上**工程配置入口** —— 它此前**完全没有 `-outputDir`**，
    /// 其入口 `ApplyProjectConfigFromCommandLine` 是「Apply + Verify 合一」，
    /// 跑一次校验就会**覆写已入库的 `Docs/Reports/day1-verification.json`**，
    /// 导致复核方无法独立复跑工程配置入口（只能靠自报）。
    /// </summary>
    public static class ReportOutput
    {
        /// <summary>
        /// 解析报告输出路径。命令行给 `-outputDir &lt;path&gt;`（绝对或相对项目根）则写到那里，
        /// 否则写默认的相对入库路径（如 `Docs/Reports/day1-verification.json`）。
        /// **目录不存在时会创建** —— 这是本单点实现与 MaterialVisualValidator 旧版的唯一差别，务必保留。
        /// </summary>
        /// <param name="defaultRelative">未给 `-outputDir` 时的入库路径（相对项目根）。</param>
        /// <param name="fileName">输出文件名（`-outputDir` 生效时用于拼接）。</param>
        public static string ResolveReportPath(string defaultRelative, string fileName)
        {
            string underOutputDir = ResolveUnderOutputDirOrNull(fileName);
            if (underOutputDir != null)
            {
                return underOutputDir;
            }

            string defaultFull = ProjectPaths.ToFullPath(defaultRelative);
            // 默认分支也要建目录：旧 WriteToDisk 是无条件 CreateDirectory(ReportsDirectory)，
            // 收敛时若只在 -outputDir 分支建目录，会**丢掉这个既有保障**（被我自查发现）。
            Directory.CreateDirectory(Path.GetDirectoryName(defaultFull));
            return defaultFull;
        }

        /// <summary>
        /// **测试专用**入口：**只在命令行显式给出 `-outputDir` 时**返回证据输出路径，
        /// **否则返回 <c>null</c>**。**绝不回落到入库路径。**
        ///
        /// <para>
        /// **为什么要单开这个入口**：`-runTests` 常被**不带** `-outputDir` 地跑（例如 G2 验收门）。
        /// 若测试走 <see cref="ResolveReportPath"/>，那些路径就会**回落进仓库** ——
        /// 而仓库里放的是**已入库证据**。后果是三重的：
        /// ① 污染工作区；② 覆写已入库证据；③ **破坏冻结锚点**（`git diff &lt;提交&gt; -- .` 不再为空）。
        /// 这违反 **R3-5「测试不得产生仓库改动」**。
        /// </para>
        /// <para>
        /// **调用方契约（强制）**：拿到 <c>null</c> 时**只能跳过 `File.WriteAllText`**。
        /// **绝不允许跳过任何一条断言** —— 否则"不带 `-outputDir` 跑"就退化成
        /// 一条不再检查的检查（铁律 4 / 铁律 12 的同族陷阱）。
        /// ⇒ 调用点必须写成"写不写"的二分，且该二分必须位于**所有断言之后**。
        /// </para>
        /// <para>
        /// 落地有**两种**合规形态 —— 本注释原先只写第一种，与已落地的第二例不符，已更正：
        /// </para>
        /// <list type="bullet">
        /// <item>**(a) 二分在所有断言之后**（只跳过最后一步落盘）。实例：
        /// <c>PoolSimulatorSegmentCoverageTests.WriteReport</c>。</item>
        /// <item>**(b) 把落盘封进不含任何断言的 `void` 方法**，其内部 `return` 只退出自身，
        /// 调用方的断言在调用它**之后**。实例：<c>MeshAlignmentAuditTests.WriteAudit</c>。
        /// **此形态一旦被塞入断言即静默失效**（跳过落盘 == 跳过检查），故该方法内须常驻一句警示。</item>
        /// </list>
        /// <para>
        /// 两种形态的**共同不变量只有一条：跳过落盘 ⇒ 没有任何断言被跳过**。
        /// 评审按这条不变量判，不要按「二分必须在最后」的字面判。
        /// </para>
        /// <para>
        /// **注意**：<see cref="ResolveReportPath"/> 的**回落行为保持不变** —— 四个验收器（工程配置 / 数据完整性 / 材质视觉 / 物理）的
        /// `-executeMethod` 入口靠它把证据写进 `Docs/Reports/`，那是**设计意图**，不是缺陷。
        /// </para>
        /// </summary>
        /// <param name="fileName">输出文件名（仅拼接用；未给 `-outputDir` 时不产生任何路径）。</param>
        /// <returns>给了 `-outputDir` ⇒ 仓库外（或指定目录）的完整路径；没给 ⇒ <c>null</c>。</returns>
        public static string ResolveTestEvidencePathOrNull(string fileName)
        {
            return ResolveUnderOutputDirOrNull(fileName);
        }

        /// <summary>
        /// `-outputDir` 分支的**唯一实现**（铁律 16）：给了则解析并建目录，没给则 <c>null</c>。
        /// <see cref="ResolveReportPath"/> 与 <see cref="ResolveTestEvidencePathOrNull"/> 共用本方法，
        /// 以保证两者对该分支的行为**不可能分叉**。
        /// </summary>
        private static string ResolveUnderOutputDirOrNull(string fileName)
        {
            string dir = CommandLineArgs.Get("-outputDir");
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            string full = Path.IsPathRooted(dir)
                ? Path.Combine(dir, fileName)
                : ProjectPaths.ToFullPath(dir.TrimEnd('/') + "/" + fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(full));
            return full;
        }
    }
}
