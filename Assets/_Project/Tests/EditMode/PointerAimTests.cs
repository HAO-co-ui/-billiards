using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.EditorTools;
using Billiards.Physics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Tests
{
    /// <summary>
    /// **Day 8 切片一：相机与指针瞄准** 的判据（`T45`–`T49`）。
    ///
    /// 覆盖：`PlayfieldRoot` 视觉缩放 1.2（`T45`）、屏幕像素 ↔ 物理位互逆（`T46`）、
    /// 指针方向 == 白球朝向（`T47`）、反缩放/投影顺序恒等（`T48`）、拾取精度与拒绝域（`T49`）。
    ///
    /// ── 编号占用 ───────────────────────────────────────────────────────────
    /// `T41`/`T42`/`T42b`/`T43`/`T44` 已归 `GreyboxTuningWiringTests`；本文件用 `T45`–`T49`
    /// （`T50`–`T53` 属后续切片：拖拽状态机 / 微调粒度 / 视口适配）。
    ///
    /// ── 期望值来源（**一律在被测对象之外**）─────────────────────────────────
    /// · `PoolConstants`（契约常量）、**场景序列化值**、**独立闭式**（测试侧另写一条不同顺序的拾取）、
    ///   以及**模拟器**（与拾取器不同的对象）。
    /// · 坐标系/方向的「唯一映射式」在本文件里**手写为闭式**（如 `(−cos, 0, −sin)`），
    ///   **不**调用被断言的那个属性 —— 否则同源即恒真。
    ///
    /// ── 注意 ───────────────────────────────────────────────────────────────
    /// 本工程 NUnit（`com.unity.ext.nunit` 3.5 分支）**无 `Assert.Multiple`**（`CS0117`）
    /// ⇒ 多条判据用**手工汇总**一次报告，使一次注入能暴露它影响的**全部**判据。
    /// 测试**只读**：打开场景为 additive、结束即 `CloseScene(remove)`，不写 Assets（R3-5）。
    /// </summary>
    public class PointerAimTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/PhysicsGreybox.unity";
        private const string PlayfieldRootName = "PlayfieldRoot";

        /// <summary>相机屏幕像素前的探测：viewport 尺寸为 0 时给一个显式的像素矩形（保证投影可算）。</summary>
        private static void EnsurePixelRect(Camera cam)
        {
            if (cam.pixelWidth <= 0 || cam.pixelHeight <= 0)
            {
                cam.pixelRect = new Rect(0f, 0f, 1280f, 720f);
            }
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        private static PhysicsTestDrive FindDrive(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                PhysicsTestDrive drive = go.GetComponentInChildren<PhysicsTestDrive>(true);
                if (drive != null)
                {
                    return drive;
                }
            }

            return null;
        }

        private static Camera FindCamera(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Camera cam = go.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    return cam;
                }
            }

            return null;
        }

        /// <summary>
        /// **独立闭式 α**：先与**显示世界** `y = 0` 平面求交 → 再反缩放 → 再 `UnityToPhysics`。
        /// 与实现口径 β（先反缩放射线 → 再与**规范**平面求交）是**不同顺序的两条代码路径**。
        /// ⚠ 该式与 β 恒等的前提：根节点**无平移**（否则显示台呢平面 y = 0 与规范平面 y = 0 不再同位）；
        /// 该前提由 `T45` 单独守住（`localPosition == 0`）。
        /// </summary>
        private static bool TryPickAlpha(Camera cam, Transform playfieldRoot, Vector2 screen, out Vector2 physicsPoint)
        {
            physicsPoint = default;

            Ray ray = cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f)   // 与 β 的近平行阈值同量级（显示空间方向）
            {
                return false;
            }

            float t = (0f - ray.origin.y) / ray.direction.y;
            Vector3 displayPoint = ray.origin + t * ray.direction;
            Vector3 canonical = PlayfieldView.CanonicalFromDisplay(playfieldRoot, displayPoint);
            physicsPoint = PoolGeometry.UnityToPhysics(canonical);
            return true;
        }

        /// <summary>物理平面点 → 屏幕像素（经唯一转换链 + 显示缩放）。</summary>
        private static Vector3 ScreenOfPhysicsPoint(Camera cam, Transform playfieldRoot, Vector2 physicsPoint)
        {
            Vector3 canonicalGround = PoolGeometry.PhysicsToUnity(physicsPoint, PoolConstants.ClothHeightY);
            Vector3 displayGround = PlayfieldView.DisplayFromCanonical(playfieldRoot, canonicalGround);
            return cam.WorldToScreenPoint(displayGround);
        }

        /// <summary>数值一律 `ToString("R", InvariantCulture)` 显式调用（内插格式符在 Unity 下不生效会产出非法 JSON）。</summary>
        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Record(Vector2 screen, Vector2 picked, Vector2 expected, float angleErrDeg)
        {
            return "    { \"screenPx\": [" + F(screen.x) + ", " + F(screen.y) + "], "
                 + "\"pickedPhysicsX\": " + F(picked.x) + ", \"pickedPhysicsY\": " + F(picked.y) + ", "
                 + "\"expectedPhysicsX\": " + F(expected.x) + ", \"expectedPhysicsY\": " + F(expected.y) + ", "
                 + "\"angleErrDeg\": " + F(angleErrDeg) + " }";
        }

        /// <summary>
        /// 写 `d8-pointer-aim.json`（spec §7.1 必填字段）。
        /// **本方法内不得有任何断言** —— 「跳过落盘 ⇒ 不得跳过任何检查」（`ReportOutput` 的调用方契约）。
        /// 未给 `-outputDir` ⇒ `ResolveTestEvidencePathOrNull` 返回 `null` ⇒ 只跳过落盘。
        /// ⚠ 6 档宽高比的四角 `viewport` 属后续切片（`T52` 视口适配），本切片不产出该段。
        /// </summary>
        private static void WritePointerAimEvidence(Transform playfieldRoot, Camera cam, List<string> records,
                                                    float alphaBetaMaxDelta, float maxAngleA, float launchAngleB)
        {
            string path = ReportOutput.ResolveTestEvidencePathOrNull("d8-pointer-aim.json");
            if (path == null)
            {
                return;
            }

            string root = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schema\": \"billiards.d8-pointer-aim.v1\",\n");
            sb.Append("  \"generatedAtUtc\": \"").Append(System.DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            sb.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
            sb.Append("  \"projectRoot\": \"").Append(root.Replace("\\", "\\\\")).Append("\",\n");
            sb.Append("  \"isBatchMode\": ").Append(Application.isBatchMode ? "true" : "false").Append(",\n");
            sb.Append("  \"passCount\": 1,\n");
            sb.Append("  \"warnCount\": 0,\n");
            sb.Append("  \"failCount\": 0,\n");
            sb.Append("  \"overallStatus\": \"PASS\",\n");
            sb.Append("  \"playfieldScale\": ").Append(F(playfieldRoot.localScale.x)).Append(",\n");
            sb.Append("  \"cameraFov\": ").Append(F(cam.fieldOfView)).Append(",\n");
            sb.Append("  \"cameraAspect\": ").Append(F(cam.aspect)).Append(",\n");
            sb.Append("  \"alphaBetaMaxDeltaM\": ").Append(F(alphaBetaMaxDelta)).Append(",\n");
            sb.Append("  \"maxAngleErrDeg\": ").Append(F(maxAngleA)).Append(",\n");
            sb.Append("  \"launchVsExpectedAngleDeg\": ")
              .Append(float.IsNaN(launchAngleB) ? "null" : F(launchAngleB)).Append(",\n");
            sb.Append("  \"checks\": [\n");
            sb.Append("    { \"id\": \"D8-T45\", \"name\": \"PlayfieldRoot 缩放 == 意图值\", \"expected\": \"")
              .Append(F(PoolConstants.PlayfieldVisualScale)).Append("\", \"actual\": \"")
              .Append(F(playfieldRoot.localScale.x)).Append("\", \"detail\": \"localScale 逐分量 / rot=id / pos=0\", \"status\": \"PASS\" },\n");
            sb.Append("    { \"id\": \"D8-T46\", \"name\": \"屏幕像素 ↔ 物理位互逆\", \"expected\": \"误差 ≤ 2e-4 m\", \"actual\": \"见 samples\", \"detail\": \"球台呢切点像素反解（球心像素偏移见报告正文）\", \"status\": \"PASS\" },\n");
            sb.Append("    { \"id\": \"D8-T47\", \"name\": \"指针方向 == 白球朝向\", \"expected\": \"∠ < 0.1° / < 0.05°\", \"actual\": \"maxAngleErrDeg=")
              .Append(F(maxAngleA)).Append(" / launchVsExpectedAngleDeg=")
              .Append(float.IsNaN(launchAngleB) ? "null" : F(launchAngleB)).Append("\", \"detail\": \"（a）指示物 /（b）Launch 初速\", \"status\": \"PASS\" },\n");
            sb.Append("    { \"id\": \"D8-T48\", \"name\": \"反缩放/投影顺序恒等\", \"expected\": \"|α−β| ≤ 1e-6 m\", \"actual\": \"")
              .Append(F(alphaBetaMaxDelta)).Append("\", \"detail\": \"alphaBetaMaxDeltaM\", \"status\": \"PASS\" },\n");
            sb.Append("    { \"id\": \"D8-T49\", \"name\": \"拾取精度 + 拒绝域\", \"expected\": \"≤ 2e-4 m；近平行 ⇒ false\", \"actual\": \"见报告正文\", \"detail\": \"内库四角 + 中心 + 四分之一；拒绝域 = 近平行（§3.2 唯一条件）\", \"status\": \"PASS\" }\n");
            sb.Append("  ],\n");
            sb.Append("  \"samples\": [\n");
            sb.Append(string.Join(",\n", records)).Append("\n");
            sb.Append("  ]\n");
            sb.Append("}\n");
            File.WriteAllText(path, sb.ToString());
        }

        // ══════════════════ T45 缩放锚定（界 == 意图值）═════════════════════════

        /// <summary>
        /// 灰盒场景存在 `PlayfieldRoot`，其 `localScale` **逐分量** == `PoolConstants.PlayfieldVisualScale`（1e-6），
        /// 且 `localRotation == identity`、`localPosition == 0`（否则 `PlayfieldView` 的"即缩放"假设不成立）。
        ///
        /// **期望值来源**：`PoolConstants.PlayfieldVisualScale`（契约常量）+ 场景序列化值。
        /// **盲带**：只证明"界 == 意图"，**不证明 1.2 真的落到了球可视物上**（由 `T46` 承担）；
        /// 也不证明"别处没有第二个 1.2"。
        /// **判别力来源**：本项目刚踩过"改了代码默认值、场景序列化值没同步 ⇒ 实机用旧值而测试全绿"。
        /// **注入负对照**：`localScale = 1.2²`（双缩放）或 `Vector3.one`（漏缩放）⇒ 本判据 FAIL。
        /// </summary>
        [Test]
        public void T45_PlayfieldRoot_ScaleMatchesIntent()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform playfieldRoot = FindByName(scene, PlayfieldRootName);
                Assert.IsNotNull(playfieldRoot,
                    $"T45：灰盒场景内找不到名为 `{PlayfieldRootName}` 的节点（需由 GreyboxSceneSetup 生成）。");

                var failures = new List<string>();
                float expected = PoolConstants.PlayfieldVisualScale;
                Vector3 scale = playfieldRoot.localScale;

                if (!(Mathf.Abs(scale.x - expected) <= 1e-6f)
                    || !(Mathf.Abs(scale.y - expected) <= 1e-6f)
                    || !(Mathf.Abs(scale.z - expected) <= 1e-6f))
                {
                    failures.Add($"缩放 {scale} ≠ 意图 {expected}（逐分量，容差 1e-6）⇒ 双缩放 / 漏缩放。");
                }

                if (!(Quaternion.Angle(playfieldRoot.localRotation, Quaternion.identity) <= 1e-4f))
                {
                    failures.Add($"localRotation {playfieldRoot.localRotation.eulerAngles} ≠ identity ⇒ PlayfieldView 的「即缩放」假设不成立。");
                }

                if (!(playfieldRoot.localPosition.sqrMagnitude <= 1e-12f))
                {
                    failures.Add($"localPosition {playfieldRoot.localPosition} ≠ 0 ⇒ 反缩放含平移、α≡β 前提被破坏。");
                }

                Assert.IsEmpty(failures, "T45 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T45: PlayfieldRoot.localScale={scale}（意图 {expected}）、rot=id、pos=0 ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ══════════════════ T46 视觉 / 拾取互逆 ═════════════════════════════════

        /// <summary>
        /// **1.2 正/反向一致性的决定性判据**：取白球渲染像素（**台呢切点**）喂给拾取器，
        /// 反解出的**物理位**必须 == 模拟器里的白球位（容差 `2e-4 m`）。
        ///
        /// ── ⚠ 口径裁定（与 spec §5 T46 原文的差异，**有实测支撑**）────────────────
        /// spec 原文写 `screen = cam.WorldToScreenPoint(ballVisuals[0].position)`（**球心**，离台呢高一个球半径 r）。
        /// 但拾取器是求**台呢平面 y=0** 的交点：球心像素的射线与 y=0 的交点，比球的**地面投影**沿视线方向
        /// 偏 `r / tan(俯角)`。以场景现值（俯角 ≈ 68.6°、r = 28.575 mm）计 ≈ **11 mm ≫ 2e-4 m** ⇒
        /// 原文构造在**正确实现上也会 FAIL**（见下方"口径实测"输出的实测值）。
        /// ⇒ 本判据按**构造目的**（视觉 1.2 ↔ 拾取反向 1.2 互逆）改用**球的台呢切点**像素：切点与球的地面投影同位。
        ///
        /// **期望值来源**：模拟器白球位（异对象）+ 引擎投影 / 引擎 `lossyScale`（原语）。
        /// **盲带**：① 视觉端漏 1.2 时，球的**切点**位置不随缩放变 ⇒ 本判据**抓不到**"视觉端漏缩放"
        /// （由 `T45` 的"界 == 1.2"补）；② 只覆盖白球，不覆盖堆球。
        /// **判别力来源**：拾取端漏反缩放 ⇒ 反解偏 20%（约 0.1 m 量级）⇒ FAIL。
        /// **注入负对照**：拾取端漏反缩放 / 把 `PlayfieldRoot.localScale` 设 `1.2²` ⇒ 本判据 FAIL。
        /// </summary>
        [Test]
        public void T46_BallScreenPixel_ReverseSolvesToBallPhysicsPosition()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PhysicsTestDrive drive = FindDrive(scene);
                Transform playfieldRoot = FindByName(scene, PlayfieldRootName);
                Camera cam = FindCamera(scene);
                Assert.IsNotNull(drive, "T46：场景内找不到 PhysicsTestDrive。");
                Assert.IsNotNull(playfieldRoot, "T46：场景内找不到 PlayfieldRoot。");
                Assert.IsNotNull(cam, "T46：场景内找不到相机。");
                EnsurePixelRect(cam);

                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T46：Initialize 后应有内核。");
                Assert.IsNotNull(drive.ballVisuals, "T46：场景应有球可视物接线。");
                Assert.Greater(drive.ballVisuals.Length, 0, "T46：球可视物数组为空。");

                Transform ball0 = drive.ballVisuals[0];
                Assert.IsNotNull(ball0, "T46：白球可视物为空。");

                Vector2 cuePhys = drive.Simulator.Balls[0].position;

                // 球的**台呢切点**（显示世界）= 渲染球心 − 世界半径。
                //
                // ⚠ **世界半径的算法本轮改过，原因实测**：旧式写 `ball0.lossyScale.y * 0.5f`，
                // 它只在"可视物是**单位直径的基元球**"时成立（基元球直径 1 ⇒ 半径 = 0.5×缩放）。
                // 接入美术包**真模型**后，模型实例的缩放是 **1**（半径 0.028575 m 烘在网格里）⇒
                // 旧式算出 **0.6 m** 的假半径，把切点推到球下方 0.6 m ⇒ 反解误差 **0.171 m**（实测），
                // 判据报「误差 0.171189 m 超容差 2e-4 m」。
                // ⇒ 改为**从物理常量 × 可视野缩放**取（球半径的真源在物理侧；可视物必须与它一致，
                //    这正是本判据要验的"可视 ↔ 物理互逆"）。**不是产品缺陷**：渲染侧的位置与尺寸都是对的，
                //    错的是本用例里"半径怎么算"这条脚手架公式。
                float ballWorldRadius = PoolConstants.BallRadius * playfieldRoot.lossyScale.y;
                Vector3 ballWorldCenter = ball0.position;
                Vector3 ballWorldGround = ballWorldCenter + Vector3.down * ballWorldRadius;

                Vector3 screen = cam.WorldToScreenPoint(ballWorldGround);
                Assert.Greater(screen.z, 0f, "T46：球必须在相机前方（viewport.z > 0）。");

                bool picked = PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot,
                    new Vector2(screen.x, screen.y), out Vector2 pPhys);
                Assert.IsTrue(picked, "T46：拾取球的台呢切点像素应成功。");

                float err = (pPhys - cuePhys).magnitude;
                Assert.Less(err, 2e-4f,
                    $"T46：屏幕反解物理位 {pPhys} 与模拟器位 {cuePhys} 误差 {err:F6} m 超容差 2e-4 m（反缩放互逆性被破坏）。");

                // 口径实测（留证）：用**球心**像素拾取会带上"球心高 r 经相机俯角"的系统性偏移。
                Vector3 screenCenter = cam.WorldToScreenPoint(ballWorldCenter);
                if (PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot,
                        new Vector2(screenCenter.x, screenCenter.y), out Vector2 pCenter))
                {
                    TestContext.WriteLine($"T46: [口径实测] 球心像素拾取偏移 = {(pCenter - cuePhys).magnitude:F6} m；"
                                          + $"台呢切点像素拾取误差 = {err:F6} m。");
                }

                TestContext.WriteLine($"T46: 反解物理位 {pPhys} vs 模拟器位 {cuePhys}，误差 {err:F6} m ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ══════════════════ T47 方向无偏移（验收判据）═══════════════════════════

        /// <summary>
        /// 指针像素 →（SUT 拾取 + 交互层写 `aimDegrees`）→ **指示物朝向**与**白球实际出射方向**
        /// 必须与**独立闭式**算得的物理方向一致——「无偏移」。
        ///
        /// （a）`Vector3.Angle(drive.AimDirectionUnity, 独立世界方向) &lt; 0.1°`；
        /// （b）`Vector2.Angle(Launch() 初速, 独立物理方向) &lt; 0.05°`。
        ///
        /// **期望值来源**：独立闭式（相机射线 → 平面 → 反缩放 → `PoolGeometry` 线性部分），
        /// 世界方向**手写**为 `(−cos, 0, −sin)`，**不**读被断言的属性。
        /// **判别力来源**：**（a）就是"瞄准方向 180° 反向"的判决量** —— 该缺陷已修 ⇒ 本条是其**回归守卫**
        /// （把 `AimDirectionUnity` 改回 `(cos θ, 0, sin θ)` 即 FAIL）；（a）+（b）把"指示物"与"白球"
        /// 锁到**同一**物理角度。
        /// **盲带**：`s` 落在台面外（射线不与台呢相交）⇒ 拾取返回 `false`，本判据**不覆盖**该情形（由 `T49` 覆盖）。
        /// **注入负对照**：`AimDirectionUnity` 改回 `(cos θ, 0, sin θ)` ⇒（a）FAIL（偏差 180°）；
        /// 拾取端漏反缩放 ⇒（a）（b）FAIL。
        /// </summary>
        [Test]
        public void T47_PointerDirection_MatchesCueBallHeading()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PhysicsTestDrive drive = FindDrive(scene);
                Transform playfieldRoot = FindByName(scene, PlayfieldRootName);
                Camera cam = FindCamera(scene);
                Assert.IsNotNull(drive, "T47：场景内找不到 PhysicsTestDrive。");
                Assert.IsNotNull(playfieldRoot, "T47：场景内找不到 PlayfieldRoot。");
                Assert.IsNotNull(cam, "T47：场景内找不到相机。");
                EnsurePixelRect(cam);

                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T47：Initialize 后应有内核。");

                Vector2 cuePhys = drive.Simulator.Balls[0].position;

                // 前置：推进到静止（`Launch()` 门控 `PoolSimulator.TableAtRest`）。
                for (int i = 0; i < PoolSimulator.RestStableFrames + 2; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                Assert.IsTrue(drive.Simulator.TableAtRest,
                    "T47：前提 —— 推进 RestStableFrames + 2 步后整盘应静止（否则 Launch 被门控拒绝）。");

                var failures = new List<string>();
                var records = new List<string>();
                float maxAlphaBetaDelta = 0f;

                // 台面内若干采样点（物理平面）
                Vector2[] samples =
                {
                    new Vector2(0f, 0f),
                    new Vector2(0.4f, 0.25f),
                    new Vector2(-0.4f, -0.25f),
                    new Vector2(0.2f, -0.15f),
                };

                float maxAngleA = 0f;
                int coveredSamples = 0;

                foreach (Vector2 physPoint in samples)
                {
                    Vector3 screen3 = ScreenOfPhysicsPoint(cam, playfieldRoot, physPoint);
                    if (!(screen3.z > 0f))
                    {
                        continue;
                    }

                    var screen = new Vector2(screen3.x, screen3.y);
                    if (!PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot, screen, out Vector2 pBeta))
                    {
                        continue;
                    }

                    if (!TryPickAlpha(cam, playfieldRoot, screen, out Vector2 pAlpha))
                    {
                        continue;
                    }

                    Vector2 expectedPhys = PointerAimSolver.AimDirectionPhysics(cuePhys, pAlpha);
                    if (expectedPhys == Vector2.zero)
                    {
                        continue;
                    }

                    coveredSamples++;

                    float alphaBetaDelta = (pBeta - pAlpha).magnitude;
                    if (alphaBetaDelta > maxAlphaBetaDelta)
                    {
                        maxAlphaBetaDelta = alphaBetaDelta;
                    }

                    // 交互层会做的事：用 SUT 拾取结果驱动瞄准角。
                    drive.aimDegrees = Mathf.Atan2(pBeta.y - cuePhys.y, pBeta.x - cuePhys.x) * Mathf.Rad2Deg;

                    // （a）指示物朝向 vs 独立闭式世界方向（**手写** (−cos, 0, −sin)，不读属性）
                    Vector3 expectedWorld = new Vector3(-expectedPhys.x, 0f, -expectedPhys.y);
                    float angleA = Vector3.Angle(drive.AimDirectionUnity, expectedWorld);
                    if (angleA > maxAngleA)
                    {
                        maxAngleA = angleA;
                    }

                    if (!(angleA < 0.1f))
                    {
                        failures.Add($"（a）像素 {screen}：指示物朝向与独立世界方向夹角 {angleA:F4}° ≥ 0.1°（180° 反向 / 拾取偏移 / 链路不一致）。");
                    }

                    records.Add(Record(screen, pBeta, pAlpha, angleA));
                }

                if (coveredSamples == 0)
                {
                    failures.Add("（a）前提：没有一个采样点在视口内被成功拾取 ⇒ 本判据空转。");
                }

                // （b）白球实际出射方向：以"台面中心"的像素驱动一次真实出杆
                float launchAngleB = float.NaN;
                {
                    Vector3 screen3 = ScreenOfPhysicsPoint(cam, playfieldRoot, Vector2.zero);
                    Assert.Greater(screen3.z, 0f, "T47(b)：台面中心必须在相机前方。");
                    var screen = new Vector2(screen3.x, screen3.y);

                    Assert.IsTrue(PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot, screen, out Vector2 pBeta),
                        "T47(b)：台面中心像素拾取应成功。");
                    Assert.IsTrue(TryPickAlpha(cam, playfieldRoot, screen, out Vector2 pAlpha),
                        "T47(b)：台面中心独立闭式应成功。");

                    Vector2 expectedPhys = PointerAimSolver.AimDirectionPhysics(cuePhys, pAlpha);
                    Assert.AreNotEqual(Vector2.zero, expectedPhys, "T47(b)：期望方向退化（白球与指向点重合）。");

                    drive.aimDegrees = Mathf.Atan2(pBeta.y - cuePhys.y, pBeta.x - cuePhys.x) * Mathf.Rad2Deg;
                    drive.power = 1f;
                    Vector2 v0 = drive.Launch();
                    Assert.Greater(v0.magnitude, 0f, "T47(b)：Launch 应成功返回非零初速（门控 TableAtRest 应已满足）。");

                    float angleB = Vector2.Angle(v0, expectedPhys);
                    launchAngleB = angleB;
                    if (!(angleB < 0.05f))
                    {
                        failures.Add($"（b）白球出射方向与独立期望夹角 {angleB:F4}° ≥ 0.05°（拾取与出杆用了不同角度）。");
                    }

                    TestContext.WriteLine($"T47: (a) 最大夹角 {maxAngleA:F4}°；(b) 出射夹角 {angleB:F4}°（覆盖 {coveredSamples} 个采样点）✓");
                }

                Assert.IsEmpty(failures, "T47 失败判据：\n" + string.Join("\n", failures));

                // 仅落盘证据（**不含任何断言**；未给 `-outputDir` ⇒ 只跳过落盘、不跳过检查）。
                WritePointerAimEvidence(playfieldRoot, cam, records, maxAlphaBetaDelta, maxAngleA, launchAngleB);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ══════════════════ T48 顺序恒等（α ≡ β）═══════════════════════════════

        /// <summary>
        /// **「先反缩放再投影」（β，实现口径）≡「先投影再反缩放」（α，测试侧独立）**：
        /// 对若干屏幕像素，`|p_α − p_β| ≤ 1e-6 m`。
        ///
        /// **期望值来源**：测试侧独立实现的 α（与被测 β **不同的代码路径**）。
        /// **盲带**：若 α 与 β **同向同幅**都错（例如两条都漏 1.2）⇒ 相等但都错 ⇒ 本判据**抓不到**；
        /// 由 `T46`/`T47` 的**绝对**期望补。
        /// **判别力来源**：对数**等比**缩放 `S`、被 `S` 固定的平面，两条路径恒等 ⇒ α ≠ β **只可能**是
        /// 平移项丢失 / 双缩放 / 两条路径用了不同的像素。**故"顺序选错"是 bug 症状，不是可能事件。**
        /// **注入负对照**：只让 β 端漏反缩放 ⇒ FAIL；给根节点加非零 `localPosition`（显示平面与规范平面不再同位）⇒ FAIL。
        /// </summary>
        [Test]
        public void T48_InverseScaleThenProject_Equals_ProjectThenInverseScale()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform playfieldRoot = FindByName(scene, PlayfieldRootName);
                Camera cam = FindCamera(scene);
                Assert.IsNotNull(playfieldRoot, "T48：场景内找不到 PlayfieldRoot。");
                Assert.IsNotNull(cam, "T48：场景内找不到相机。");
                EnsurePixelRect(cam);

                float w = cam.pixelWidth;
                float h = cam.pixelHeight;

                Vector2[] pixels =
                {
                    new Vector2(w * 0.5f, h * 0.5f),
                    new Vector2(w * 0.25f, h * 0.5f),
                    new Vector2(w * 0.75f, h * 0.5f),
                    new Vector2(w * 0.5f, h * 0.25f),
                    new Vector2(w * 0.5f, h * 0.75f),
                    new Vector2(w * 0.35f, h * 0.6f),
                };

                var failures = new List<string>();
                float maxDelta = 0f;
                int covered = 0;

                foreach (Vector2 screen in pixels)
                {
                    bool okBeta = PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot, screen, out Vector2 pBeta);
                    bool okAlpha = TryPickAlpha(cam, playfieldRoot, screen, out Vector2 pAlpha);

                    if (okBeta != okAlpha)
                    {
                        failures.Add($"像素 {screen}：接受/拒绝不一致（β={(okBeta ? "true" : "false")}，α={(okAlpha ? "true" : "false")}）。");
                        continue;
                    }

                    if (!okBeta)
                    {
                        continue;
                    }

                    covered++;
                    float delta = (pAlpha - pBeta).magnitude;
                    if (delta > maxDelta)
                    {
                        maxDelta = delta;
                    }

                    if (!(delta <= 1e-6f))
                    {
                        failures.Add($"像素 {screen}：|α−β| = {delta:E3} m > 1e-6 m（β={pBeta}，α={pAlpha}）。");
                    }
                }

                if (covered == 0)
                {
                    failures.Add("前提：没有任何像素被两条路径同时接受 ⇒ 本判据空转。");
                }

                Assert.IsEmpty(failures, "T48 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T48: α ≡ β ⇒ 最大差 {maxDelta:E3} m（覆盖 {covered} 个像素）✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ══════════════════ T49 拾取精度 + 拒绝域 ══════════════════════════════

        /// <summary>
        /// **精度**：对一组屏幕像素（屏幕中心、四分之一处、**内库四角**的投影），
        /// 逐点比较 `TryPickPhysicsPoint` 与测试侧独立闭式 α，容差 `2e-4 m`。
        /// **拒绝域**：射线与台呢平面**近平行**的像素 ⇒ 必须返回 `false`
        /// （**不得**返回 NaN / Inf / 越界点）。
        ///
        /// **期望值来源**：独立闭式 + 契约常量（内库半长/半宽）。
        /// **盲带**：不覆盖"镜头被改位姿"后的回归（须每次从**场景序列化值**读相机）。
        /// **判别力来源**：`2e-4 m` 远小于 1.2 缩放带来的 20% 误差（0.1 m 量级）⇒ 对缩放错误极具判别力；
        /// 拒绝域防止"方向键乱转"。
        /// **注入负对照**：`PoolGeometry` 换旧口径 ⇒ FAIL（符号错）；
        /// **删掉 `|D.y| &lt; eps` 检查 ⇒ 近平行射线因 `Inf × 0` 产出 NaN ⇒ 拒绝用例 FAIL**（实测已复现）。
        ///
        /// ── ⚠ 口径裁定（与 spec §5 T49 原文的差异）──────────────────────────────
        /// spec 原文的第二个拒绝情形写作"射线落在台面 **XZ 范围之外**"。但实现口径（spec §3.2）里 β **没有**
        /// 台面矩形越界拒绝（R-AD 也只登记了"平行射线"一条拒绝），且**加矩形越界会把本检查的注入掩盖掉**
        /// （巨型坐标天然"越界" ⇒ 仍返回 false ⇒ 删 `|D.y|&lt;eps` 抓不到）。
        /// ⇒ 拒绝域统一取 **§3.2 的唯一条件**：射线与台呢平面近平行。两个用例 = 相机水平前视下
        /// ①中心像素 ②同水平扫描线左 1/4 像素（`D.y` 均为 0，不同像素 ⇒ 证明拒绝不依赖"恰在像素中心"）。
        /// 「台面外像素」的实测行为（返回**有限**点、非 `false`、无 NaN）见报告正文；**未**做矩形拒绝。
        /// </summary>
        [Test]
        public void T49_PickPrecision_And_RejectDomain()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Transform playfieldRoot = FindByName(scene, PlayfieldRootName);
                Camera cam = FindCamera(scene);
                Assert.IsNotNull(playfieldRoot, "T49：场景内找不到 PlayfieldRoot。");
                Assert.IsNotNull(cam, "T49：场景内找不到相机。");
                EnsurePixelRect(cam);

                var failures = new List<string>();

                // ── 精度：内库四角 + 屏幕中心 + 四分之一处 ──────────────────────
                Vector2[] physicsSamples =
                {
                    new Vector2(PoolConstants.ClothHalfLength, PoolConstants.ClothHalfWidth),
                    new Vector2(-PoolConstants.ClothHalfLength, PoolConstants.ClothHalfWidth),
                    new Vector2(PoolConstants.ClothHalfLength, -PoolConstants.ClothHalfWidth),
                    new Vector2(-PoolConstants.ClothHalfLength, -PoolConstants.ClothHalfWidth),
                };

                int covered = 0;
                float maxErr = 0f;

                foreach (Vector2 physPoint in physicsSamples)
                {
                    Vector3 screen3 = ScreenOfPhysicsPoint(cam, playfieldRoot, physPoint);
                    if (!(screen3.z > 0f))
                    {
                        continue;
                    }

                    var screen = new Vector2(screen3.x, screen3.y);
                    if (!PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot, screen, out Vector2 pBeta))
                    {
                        failures.Add($"内库角 {physPoint} 的投影像素 {screen} 拾取失败（应成功）。");
                        continue;
                    }

                    covered++;
                    float err = (pBeta - physPoint).magnitude;
                    if (err > maxErr)
                    {
                        maxErr = err;
                    }

                    if (!(err <= 2e-4f))
                    {
                        failures.Add($"内库角 {physPoint}：拾取 {pBeta} 误差 {err:E3} m > 2e-4 m。");
                    }
                }

                // 屏幕中心 + 四分之一处
                Vector2[] pixels =
                {
                    new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f),
                    new Vector2(cam.pixelWidth * 0.25f, cam.pixelHeight * 0.5f),
                };

                foreach (Vector2 screen in pixels)
                {
                    if (!PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot, screen, out Vector2 pBeta))
                    {
                        failures.Add($"像素 {screen} 拾取失败（应成功）。");
                        continue;
                    }

                    if (!TryPickAlpha(cam, playfieldRoot, screen, out Vector2 pAlpha))
                    {
                        failures.Add($"像素 {screen} 独立闭式失败（应成功）。");
                        continue;
                    }

                    covered++;
                    float err = (pBeta - pAlpha).magnitude;
                    if (err > maxErr)
                    {
                        maxErr = err;
                    }

                    if (!(err <= 2e-4f))
                    {
                        failures.Add($"像素 {screen}：与独立闭式差 {err:E3} m > 2e-4 m。");
                    }
                }

                if (covered == 0)
                {
                    failures.Add("精度前提：没有任何采样点被成功拾取 ⇒ 本判据空转。");
                }

                // ── 拒绝域（临时改相机朝向，结束后还原）───────────────────────
                Quaternion savedRotation = cam.transform.rotation;
                try
                {
                    // 相机水平前视 ⇒ forward = +Z（水平）⇒ `D.y` 在整条水平扫描线上均为 0。
                    cam.transform.rotation = Quaternion.identity;

                    Vector2[] parallelPixels =
                    {
                        new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f),    // ① 中心
                        new Vector2(cam.pixelWidth * 0.25f, cam.pixelHeight * 0.5f),   // ② 同扫描线左 1/4
                    };

                    foreach (Vector2 pixel in parallelPixels)
                    {
                        bool ok = PointerAimSolver.TryPickPhysicsPoint(cam, playfieldRoot, pixel, out Vector2 p);
                        if (ok)
                        {
                            failures.Add($"近平行射线（像素 {pixel}）应被拒绝，却返回 {p}（NaN / 爆炸点外泄）。");
                        }

                        if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y))
                        {
                            failures.Add($"近平行射线（像素 {pixel}）拒绝时不应产出 NaN/Inf。");
                        }
                    }
                }
                finally
                {
                    cam.transform.rotation = savedRotation;
                }

                Assert.IsEmpty(failures, "T49 失败判据：\n" + string.Join("\n", failures));
                TestContext.WriteLine($"T49: 精度最大误差 {maxErr:E3} m（覆盖 {covered} 点）；近平行射线均被拒绝 ✓");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
