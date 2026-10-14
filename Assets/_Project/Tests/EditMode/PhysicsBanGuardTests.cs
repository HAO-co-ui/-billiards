using System.Reflection;
using Billiards.Core;
using Billiards.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **自研物理铁律的自动执行守卫**（铁律 18：新增守卫优先进测试套件）。
    ///
    /// 背景：该铁律原先只有 `SCN-04`（工程配置入口）/ `SCN-06`（MaterialVisualValidator）两份守卫，
    /// 而本工程**没有 CI** ⇒ **没人点菜单就不跑**（"有守卫" ≠ "守卫会跑"）。
    /// 更严重的是两份守卫**只扫场景、不扫 prefab**，而工程有 18 个 prefab 且物理内核正是从它们实例化球 ——
    /// 一个 `Rigidbody` 挂在球 prefab 上，过去**所有守卫继续全绿，确定性静默崩坏**。
    ///
    /// 本测试把守卫**拉进 `-runTests`**，使其自动执行；并覆盖 prefab。
    /// </summary>
    public class PhysicsBanGuardTests
    {
        // ── a) 工程内不得有 Rigidbody/Collider（场景 + prefab）───────────────

        [Test]
        public void ScenesAndPrefabs_ContainNoRigidbodyOrCollider()
        {
            PhysicsBanAudit.Scan scan = PhysicsBanAudit.ScanAll();

            if (scan.Offenders.Count > 0)
            {
                TestContext.WriteLine("违规对象：\n" + string.Join("\n", scan.Offenders));
            }

            Assert.AreEqual(0, scan.RigidbodyCount,
                $"检测到 {scan.RigidbodyCount} 个 Rigidbody ⇒ 有人误接入了 PhysX，将破坏确定性与手感一致性。");
            Assert.AreEqual(0, scan.ColliderCount,
                $"检测到 {scan.ColliderCount} 个 Collider ⇒ 同上（自研二维物理不使用任何 Unity 碰撞体）。");
        }

        /// <summary>
        /// **非空转对照**：证明上面的「0」不是因为**一个文件都没扫到**。
        /// 这一条是必须的 —— 一个"扫了 0 个文件所以 0 个 Rigidbody"的实现会让上面那条**恒真**。
        /// </summary>
        [Test]
        public void Scan_CoversScenesAndPrefabs_CoverageIsNonVacuous()
        {
            PhysicsBanAudit.Scan scan = PhysicsBanAudit.ScanAll();

            TestContext.WriteLine($"扫描覆盖：scene={scan.SceneCount}  prefab={scan.PrefabCount}");

            Assert.GreaterOrEqual(scan.SceneCount, 1, "至少要扫到 1 个场景，否则上面的 0 是空转。");
            Assert.GreaterOrEqual(scan.PrefabCount, 18,
                "至少要扫到 18 个 prefab（16 球 + BallPrefab + PoolCue），否则 prefab 覆盖是空转。");
            Assert.IsTrue(scan.CoverageIsNonVacuous, "扫描必须非空转。");
        }

        /// <summary>
        /// **负对照**：在**纯内存层级**中注入 1 个 `Rigidbody` + 1 个 `Collider`，
        /// 断言计数得到**精确期望值** —— 证明计数函数能**识别注入的刚体/碰撞体**，不是恒真。
        ///
        /// **为什么是层级而不是场景**：负对照要证明的是"计数函数能识别注入对象"，
        /// 载体不必是场景。改用纯内存层级后：
        ///  · **批处理与交互式行为一致** —— 不再依赖初始场景是否已保存；
        ///  · **不修改编辑器现场** —— 不新建/打开/关闭任何场景（`NewSceneMode.Single` 会在交互式下
        ///    丢弃用户当前打开的场景，故**刻意不用**）；
        ///  · **仍能失败** —— 去掉注入后计数变 0，断言 1/1/2 会 FAIL（见下方空层级对照）。
        ///
        /// 全程无资产 I/O，`finally` 中 `DestroyImmediate` 清理，测试**不产生任何仓库改动**。
        /// </summary>
        [Test]
        public void Counter_DetectsInjectedRigidbodyAndCollider_OnSyntheticHierarchy()
        {
            var root = new GameObject("InjectedHost");
            try
            {
                root.AddComponent<Rigidbody>();

                var child = new GameObject("InjectedChildCollider");
                child.transform.SetParent(root.transform);
                child.AddComponent<BoxCollider>();

                int rigidbodies = 0;
                int colliders = 0;
                int total = PhysicsBanAudit.CountInHierarchy(
                    root, ref rigidbodies, ref colliders, null, "synthetic/");

                Assert.AreEqual(1, rigidbodies, "注入 1 个 Rigidbody 后计数必须**精确为 1** —— 否则计数恒真、守卫形同虚设。");
                Assert.AreEqual(1, colliders, "注入 1 个 Collider 后计数必须**精确为 1** —— 同上。");
                Assert.AreEqual(2, total, "层级内 PhysX 组件总数必须为 2（1 Rigidbody + 1 Collider）。");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            // 空层级对照：证明计数函数对"没有注入"的层级返回 0 —— 与上面的非零结果一起，
            // 排除了"函数永远返回 0""函数恒返回非零"两类恒真实现。
            var empty = new GameObject("EmptyHost");
            try
            {
                int emptyRigidbodies = 0;
                int emptyColliders = 0;
                int emptyTotal = PhysicsBanAudit.CountInHierarchy(
                    empty, ref emptyRigidbodies, ref emptyColliders, null, "synthetic/");

                Assert.AreEqual(0, emptyRigidbodies, "空层级的 Rigidbody 必须为 0。");
                Assert.AreEqual(0, emptyColliders, "空层级的 Collider 必须为 0。");
                Assert.AreEqual(0, emptyTotal, "空层级的总计数必须为 0 —— 防止计数恒真。");
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }

        // ── b) 运行时程序集不得引用 PhysX 模块 ──────────────────────────────

        /// <summary>
        /// 代码级守卫：`Billiards.Runtime` **不得引用 `UnityEngine.PhysicsModule`**。
        /// 覆盖 `SCN-04`/`SCN-06` 抓不到的那一类 —— 例如有人为实现瞄准射线调用 Unity 的射线检测：
        /// 那**不会新增任何 Rigidbody**（场景检查仍全绿），却把 PhysX 拖进了运行时依赖。
        ///
        /// **写法提醒（本工程特有）**：调用 Unity 物理 API 必须**全限定**（如 `UnityEngine.Physics.Raycast`），
        /// 因为 `Billiards.Physics` 会遮蔽 `UnityEngine.Physics` —— 在 `Billiards.*` 内**裸写未限定的那个命名空间名**，
        /// 名字查找会先命中 `Billiards.Physics`，于是 CS0234（找不到该成员）。
        /// 依据：`Docs/工程标准.md` R1-3a。
        /// </summary>
        [Test]
        public void RuntimeAssembly_DoesNotReferencePhysXModule()
        {
            Assembly runtime = typeof(PoolConstants).Assembly;

            Assert.AreEqual("Billiards.Runtime", runtime.GetName().Name,
                "PoolConstants 必须位于 `Billiards.Runtime` 程序集。");

            foreach (AssemblyName referenced in runtime.GetReferencedAssemblies())
            {
                Assert.AreNotEqual("UnityEngine.PhysicsModule", referenced.Name,
                    "`Billiards.Runtime` 引用了 PhysX 模块 —— 自研物理铁律要求运行时不得依赖 PhysX。");
            }
        }

        // ── c) 分层守卫：诊断代码不得回到 Runtime（守 A1 的整改）─────────────

        /// <summary>
        /// **守 A1**：`MeshAlignmentMetrics` / `TableReference` 等诊断代码**必须待在 Editor-only 程序集**。
        /// 它们 Runtime 内零消费者；回到 Runtime 就会被编进玩家构建
        ///（整改前实测 667/1118 行 = 60% 不是运行时资产）。本测试防其回归。
        /// </summary>
        [Test]
        public void DiagnosticsAssembly_IsEditorOnlyAndSeparateFromRuntime()
        {
            Assembly diag = typeof(MeshAlignmentTolerances).Assembly;
            Assembly runtime = typeof(PoolConstants).Assembly;

            Assert.AreEqual("Billiards.Diagnostics", diag.GetName().Name,
                "诊断代码必须位于 `Billiards.Diagnostics`。");
            Assert.AreNotSame(runtime, diag,
                "诊断代码被编进了 `Billiards.Runtime` —— 会进玩家构建；A1 分层整改被回退了。");

            string asmdefPath = "Assets/_Project/Scripts/Diagnostics/Billiards.Diagnostics.asmdef";
            var asmdef = JsonUtility.FromJson<AsmdefModel>(
                System.IO.File.ReadAllText(asmdefPath));
            Assert.Contains("Editor", asmdef.includePlatforms,
                "`Billiards.Diagnostics` 必须限定 Editor 平台，否则仍会进玩家构建。");
        }

        /// <summary>`JsonUtility` 用的 DTO：字段由反序列化填充，故显式初始化以消 CS0649。</summary>
        [System.Serializable]
        private class AsmdefModel
        {
            public string[] includePlatforms = System.Array.Empty<string>();
        }
    }
}
