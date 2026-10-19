using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **`T162`：`_verify_*.csproj` 的编译清单必须覆盖 `Tests/EditMode/` 下的每一个测试文件**。
    ///
    /// ── 为什么需要这条"元判据"（这是本轮真实踩到的假绿）──────────────────────────
    /// `_verify_Billiards.Tests.EditMode.csproj` 是 `Docs/Tools/make_verify_csproj.py` 生成的**显式清单**：
    /// 它把每个 `.cs` 逐条写成 `<Compile Include="..." />`（当前 43 条），**不会自动收录新文件**。
    /// 实测事故：新增 `MatchUiVisibilityTests.cs` 后直接 `dotnet build _verify_*.csproj` ⇒
    /// **0 error 全绿**，而那个用例**根本没被编译**（清单里 0 命中）。
    /// ⇒ 这是"判据不在场却显示通过"——正是本卡一直在治的病，只不过这次病在**判据工程自己身上**。
    /// ⇒ 所以用一条判据把"清单是否覆盖全部测试文件"钉住：新增测试文件忘了重生成，它会**红**。
    ///
    /// ── 为什么 `csproj` 缺失时**跳过**而不是通过 ──────────────────────────────────
    /// 该文件是**已 gitignore 的生成物**（未入库）。干净检出（CI/新机器）上它本来就不存在，
    /// 此时`跳过`是诚实的；**静默通过才是新的假绿**（"看起来验过了，其实一条没验"）。
    /// 因此缺文件时用 <see cref="Assert.Ignore(string)"/> 并把原因写进日志，
    /// 与既有 3 条跳过（`T149c` / `T151` / `T152b`）同一处理口径。
    ///
    /// ── 判据只断一件事，不越界 ────────────────────────────────────────────────
    /// 只断"**每个测试文件都被清单收录**"；**不**断清单里是否残留已删除的条目
    /// （那是生成器的事，且残留条目通常仍能编译，误报成本更高）。
    /// ⚠ 本判据**不能**替代真正的编译：清单齐了但代码编不过，它照样绿 —— 编译仍必须真跑一次。
    /// </summary>
    public class VerifyProjectCoverageTests
    {
        /// <summary>生成物：EditMode 测试程序集的离线校验工程（仓根，已 gitignore）。</summary>
        private const string VerifyProjectFileName = "_verify_Billiards.Tests.EditMode.csproj";

        /// <summary>测试源码目录（相对仓根）。</summary>
        private const string TestsDirectoryRelative = "Assets/_Project/Tests/EditMode";

        /// <summary>清单项的正则（`<Compile Include="..." />`，属性顺序无关、容忍引号内外空白）。</summary>
        private static readonly Regex CompileIncludePattern =
            new Regex("<Compile\\s+Include\\s*=\\s*\"(?<path>[^\"]+)\"", RegexOptions.Compiled);

        /// <summary>
        /// **`T162`：`_verify_*.csproj` 的 `Compile Include` 清单必须覆盖测试目录下每个 `.cs`。**
        ///
        /// 失败信息给出**缺失文件名**与"跑 `Docs/Tools/make_verify_csproj.py` 重生成"的处置，
        /// 让下一个人不必再猜一遍（本轮就是猜出来的）。
        /// </summary>
        [Test]
        public void T162_VerifyProject_IncludesEveryTestFile()
        {
            string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string projectPath = Path.Combine(root, VerifyProjectFileName);
            string testsPath = Path.Combine(root, TestsDirectoryRelative);

            // ── 生成物缺失 ⇒ 诚实跳过（不许静默通过）──
            if (!File.Exists(projectPath))
            {
                Assert.Ignore($"{VerifyProjectFileName} 不存在（它是 `make_verify_csproj.py` 的生成物、已 gitignore）⇒ "
                              + "本判据跳过。**跳过是诚实的，静默通过才是新的假绿**："
                              + "请先跑 `Docs/Tools/make_verify_csproj.py` 生成再看本用例。");
            }

            if (!Directory.Exists(testsPath))
            {
                Assert.Fail($"T162 找不到测试目录 `{TestsDirectoryRelative}` ⇒ 判据失效（不是通过）。");
            }

            // ── 清单侧：解析出所有 Compile Include 的规范化路径（统一用 '\\'，忽略大小写）──
            string projectText = File.ReadAllText(projectPath, Encoding.UTF8);
            var included = new HashSet<string>();
            foreach (Match match in CompileIncludePattern.Matches(projectText))
            {
                included.Add(Normalize(match.Groups["path"].Value));
            }

            if (included.Count == 0)
            {
                Assert.Fail($"T162 `{VerifyProjectFileName}` 里一条 `Compile Include` 都解析不出来 ⇒ "
                            + "判据无从比对（生成器格式变了？）。不是通过。");
            }

            // ── 源码侧：逐个测试文件比对 ──
            // ⚠ 不用 `Path.GetRelativePath`：本程序集目标是 **.NET Framework 4.7.1**，该方法在 netstandard2.1 才有
            //   ⇒ 手写相对路径（文件都在 `root` 之下，去掉前缀即可），避免"判据自己编不过"。
            string normalizedRoot = Normalize(Path.GetFullPath(root));
            string[] testFiles = Directory.GetFiles(testsPath, "*.cs", SearchOption.AllDirectories);
            var missing = new List<string>();
            foreach (string file in testFiles)
            {
                string full = Normalize(Path.GetFullPath(file));
                if (!full.StartsWith(normalizedRoot + "\\", System.StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add(full);
                    continue;
                }

                // 相对**仓根**的路径（清单里就是这个形态，例：Assets\_Project\Tests\EditMode\T.cs）
                string relative = full.Substring(normalizedRoot.Length + 1);
                if (!included.Contains(relative))
                {
                    missing.Add(relative);
                }
            }

            TestContext.WriteLine(
                $"T162：测试目录 {testFiles.Length} 个 .cs；清单 {included.Count} 条 Compile Include；"
                + $"缺失 {missing.Count} 个。");

            if (missing.Count > 0)
            {
                missing.Sort(System.StringComparer.OrdinalIgnoreCase);
                Assert.Fail(
                    $"T162 `{VerifyProjectFileName}` 的编译清单漏了 {missing.Count} 个测试文件：\n  - "
                    + string.Join("\n  - ", missing)
                    + "\n⇒ 后果：`dotnet build` 会**绿**，但这些用例**压根没被编译**（判据不在场却显示通过）。"
                    + "\n⇒ 处置：跑 `Docs/Tools/make_verify_csproj.py` 重新生成 verify 工程（生成后再跑一次本用例应为绿）。");
            }
        }

        /// <summary>路径规范化：统一分隔符为 `\`（清单里是 Windows 形态），供大小写无关比较。</summary>
        private static string Normalize(string path)
        {
            return (path ?? string.Empty).Replace('/', '\\').Trim();
        }
    }
}
