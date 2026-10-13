using System.IO;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **报告输出路径的唯一实现**（铁律 16）。
    ///
    /// **为什么要有这个类**（D4-FIX-03 E1）：此前 Day2 与 Day3 各写了一份 `ResolveReportPath`，
    /// **签名相同、逻辑几乎相同，但已经分叉** ——
    /// `TableIntegrityValidator` 的版本调用 <c>Directory.CreateDirectory</c>，`Day3Validator` 的**没有**。
    /// 后果：`-outputDir` 指向一个不存在的目录时，Day2 能写、Day3 会失败。
    /// 之前没暴露**只是因为测试目录恰好已存在**（调用顺序的巧合，不是设计）——
    /// 与 `SCN-04`/`SCN-06` 是同一类缺陷（本工程第五次）。
    ///
    /// 本类同时补上 **Day1** —— Day1 此前**完全没有 `-outputDir`**，
    /// 其入口 `ApplyDay1FromCommandLine` 是「Apply + Verify 合一」，
    /// 跑一次校验就会**覆写已入库的 `Docs/Reports/day1-verification.json`**，
    /// 导致复核方无法独立复跑 Day1（只能靠自报）。
    /// </summary>
    public static class ReportOutput
    {
        /// <summary>
        /// 解析报告输出路径。命令行给 `-outputDir &lt;path&gt;`（绝对或相对项目根）则写到那里，
        /// 否则写默认的相对入库路径（如 `Docs/Reports/day1-verification.json`）。
        /// **目录不存在时会创建** —— 这是本单点实现与 Day3 旧版的唯一差别，务必保留。
        /// </summary>
        /// <param name="defaultRelative">未给 `-outputDir` 时的入库路径（相对项目根）。</param>
        /// <param name="fileName">输出文件名（`-outputDir` 生效时用于拼接）。</param>
        public static string ResolveReportPath(string defaultRelative, string fileName)
        {
            string dir = CommandLineArgs.Get("-outputDir");

            if (string.IsNullOrEmpty(dir))
            {
                string defaultFull = ProjectPaths.ToFullPath(defaultRelative);
                // 默认分支也要建目录：旧 WriteToDisk 是无条件 CreateDirectory(ReportsDirectory)，
                // 收敛时若只在 -outputDir 分支建目录，会**丢掉这个既有保障**（被我自查发现）。
                Directory.CreateDirectory(Path.GetDirectoryName(defaultFull));
                return defaultFull;
            }

            string full = Path.IsPathRooted(dir)
                ? Path.Combine(dir, fileName)
                : ProjectPaths.ToFullPath(dir.TrimEnd('/') + "/" + fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(full));
            return full;
        }
    }
}
