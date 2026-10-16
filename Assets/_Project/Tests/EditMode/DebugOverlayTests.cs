using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Billiards.EditorTools;
using Billiards.Physics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **调试叠加层默认关闭 + 热键可开**（`T129`）。
    ///
    /// 动机（用户实机）：左上角那串诊断文本（`瞄准 148.7° 力度 100% 初速 4.50 m/s` / 球数 / 接触计数 /
    /// 台面静止状态 / 中文按键提示）在**正式入口**里一直显示，盖住画面。
    /// 用户拍板：**默认关闭 + 热键（F1）可开**；**诊断场景灰盒保持开启**。
    ///
    /// 本判据四组：
    /// ① 组件默认值（`showDebugOverlay == false`、热键为 `F1`）；
    /// ② **场景级**：正式入口 `Main.unity` 的驱动器关闭；诊断场景 `PhysicsGreybox.unity` 的驱动器开启；
    /// ③ **源码级结构守卫**（批处理无渲染，管不到实际绘制）：`OnGUI` 的**首条语句**就是开关判据（关 ⇒ 直接返回）；
    /// ④ 热键读取存在（`Input.GetKeyDown(debugOverlayToggleKey)`）。
    ///
    /// **盲带（如实）**：`OnGUI` 的**实际绘制**不在 EditMode 可判范围内（无渲染、无 `OnGUI` 派发）——
    /// ③/④ 只保证**结构**（判据存在且是首条语句），不证明"真的没画"。按需 PlayMode / 手动验收。
    ///
    /// **注入负对照**：把默认值翻成 `true`（或删掉 `OnGUI` 首行判据）⇒ ①/③ 红。
    /// </summary>
    public class DebugOverlayTests
    {
        private const string DriveSourcePath = "Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs";
        private const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string GreyboxScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";

        [Test]
        public void T129_DebugOverlay_DefaultOff_HotkeyGate()
        {
            var failures = new List<string>();

            // ① 组件默认值
            var go = new GameObject("T129Drive");
            try
            {
                var drive = go.AddComponent<PhysicsTestDrive>();
                if (drive.showDebugOverlay)
                {
                    failures.Add("① 新建 PhysicsTestDrive 的 showDebugOverlay 默认应为 false（正式态不显示诊断文本）。");
                }

                if (drive.debugOverlayToggleKey != KeyCode.F1)
                {
                    failures.Add($"① 切换热键应为 F1，实际 {drive.debugOverlayToggleKey}。");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            // ② 场景级：Main 关 / 灰盒开
            AssertSceneOverlay(failures, MainScenePath, expectOn: false, tag: "② 正式入口 Main.unity");
            AssertSceneOverlay(failures, GreyboxScenePath, expectOn: true, tag: "② 诊断场景 PhysicsGreybox.unity");

            // ③ / ④ 源码级结构守卫
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", DriveSourcePath);
            Assert.IsTrue(File.Exists(full), "T129：找不到驱动器源码：" + DriveSourcePath);
            string code = StripCommentLines(File.ReadAllText(full));

            if (!Regex.IsMatch(code, @"public\s+bool\s+showDebugOverlay\s*=\s*false\s*;"))
            {
                failures.Add("③ 源码里 `showDebugOverlay` 的默认值声明不是 `= false`。");
            }

            if (!Regex.IsMatch(code, @"Input\.GetKeyDown\(\s*debugOverlayToggleKey\s*\)"))
            {
                failures.Add("④ 源码里没有热键读取 `Input.GetKeyDown(debugOverlayToggleKey)`。");
            }

            int onGui = code.IndexOf("private void OnGUI()", System.StringComparison.Ordinal);
            if (onGui < 0)
            {
                failures.Add("③ 源码里找不到 `OnGUI`。");
            }
            else
            {
                int brace = code.IndexOf('{', onGui);
                string head = brace >= 0 ? code.Substring(brace, Mathf.Min(220, code.Length - brace)) : string.Empty;
                if (!head.Contains("showDebugOverlay") || !head.Contains("return"))
                {
                    failures.Add("③ `OnGUI` 的首条语句不是「`showDebugOverlay` 为假 ⇒ 直接返回」的开关判据"
                                 + "（关闭时仍会走绘制路径）。");
                }
            }

            Assert.IsEmpty(failures, "T129 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T129: 默认 false / 热键 F1 / Main 关·灰盒开 / OnGUI 首行判据 ✓");
        }

        private static void AssertSceneOverlay(List<string> failures, string scenePath, bool expectOn, string tag)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                PhysicsTestDrive drive = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    drive = root.GetComponentInChildren<PhysicsTestDrive>(true);
                    if (drive != null)
                    {
                        break;
                    }
                }

                if (drive == null)
                {
                    failures.Add($"{tag} 里找不到 PhysicsTestDrive。");
                    return;
                }

                if (drive.showDebugOverlay != expectOn)
                {
                    failures.Add($"{tag} 的 showDebugOverlay 应为 {expectOn}，实际 {drive.showDebugOverlay}。");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>去掉整行注释（`//` 起始，含 `///`）——与既有源码守卫同口径。</summary>
        private static string StripCommentLines(string source)
        {
            var sb = new System.Text.StringBuilder(source.Length);
            foreach (string line in source.Split('\n'))
            {
                if (line.TrimStart().StartsWith("//", System.StringComparison.Ordinal))
                {
                    continue;
                }

                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }
    }
}
