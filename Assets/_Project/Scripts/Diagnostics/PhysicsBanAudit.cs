using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Billiards.Diagnostics
{
    /// <summary>
    /// 自研物理铁律的**单点审计实现**（铁律 16：同一不变量的检查必须单点实现）。
    ///
    /// **铁律**：本项目球物理由自研二维定长步进模拟器驱动，**禁止 Unity PhysX**
    /// （任何 `Rigidbody`/`Collider` 都意味着有人误接入了 PhysX，会破坏确定性与手感一致性）。
    ///
    /// **本类是全工程唯一的实现**：`SCN-04`（`BilliardsProjectVerifier`）、`SCN-06`（`MaterialVisualValidator`）
    /// 与 `PhysicsBanGuardTests`（EditMode 测试）**全部调用它**。
    ///
    /// **历史（为什么要有这个类）**：`SCN-04` 与 `SCN-06` 原本各写一份实现，且**已经分叉** ——
    /// `SCN-04` 用固定路径 `MainScenePath`，`SCN-06` 用 `SceneManager.GetActiveScene()`（可能扫错对象）；
    /// 且两者**都只扫场景、不扫 prefab**，而工程有 18 个 prefab，球正是从它们实例化的。
    /// 现收敛为单点实现，并**把 prefab 纳入扫描**。
    ///
    /// **唯一的计数原语是 `CountInHierarchy(GameObject, …)`**：场景（`CountInScene`）与 prefab
    /// 两条路径都只是它的**薄适配层**，故三者不可能再"各留一份、彼此分叉"。
    /// </summary>
    public static class PhysicsBanAudit
    {
        public const string ScenesRoot = "Assets/_Project/Scenes";
        public const string PrefabsRoot = "Assets/_Project/Prefabs";

        /// <summary>工程级扫描结果。</summary>
        public struct Scan
        {
            public int SceneCount;
            public int PrefabCount;
            public int RigidbodyCount;
            public int ColliderCount;
            public List<string> Offenders;

            /// <summary>
            /// 扫描本身是否**真的扫到了东西**。
            /// 用途：防止「`RigidbodyCount == 0` 其实是因为一个文件都没扫到」这类**恒真**断言 ——
            /// 这正是本工程反复要防的「检查测了个方便测的量」。
            /// </summary>
            public bool CoverageIsNonVacuous => SceneCount > 0 && PrefabCount > 0;
        }

        // ── 核心：在**层级**上统计（唯一实现）──────────────────────────────

        /// <summary>
        /// **层级级原语 —— 本工程唯一的计数实现**：统计 <paramref name="root"/> 及其**全部后代**
        /// 中的 <see cref="Rigidbody"/> / <see cref="Collider"/>。
        ///
        /// **场景与 prefab 都只是薄适配层**：它们各自负责"加载 / 迭代根对象"，
        /// 但**计数逻辑（`GetComponentsInChildren`）只有这一份**（铁律 16）——
        /// 本类内不再有第二处计数代码。
        ///
        /// **为什么要有这一层**：让负对照可以**纯内存构造**一个层级来证明"计数能识别注入对象"，
        /// 而不必新建/打开/关闭场景 —— 后者在批处理下会因初始场景未保存而被拒绝
        /// （`NewSceneMode.Additive` 抛 `InvalidOperationException`），
        /// 而改用 `NewSceneMode.Single` 又会在**交互式下丢弃用户当前打开的场景**。
        /// </summary>
        /// <param name="root">层级根；<c>null</c> 视为空层级（不计数）。</param>
        /// <param name="rigidbodies">累加 Rigidbody 数（`ref`，允许调用方已有累计值）。</param>
        /// <param name="colliders">累加 Collider 数。</param>
        /// <param name="offenders">非 <c>null</c> 时逐条记录违规对象，格式为 <c>[类型] 作用域前缀+对象名</c>。</param>
        /// <param name="scope">违规串前缀（场景 `场景名/`；prefab `路径::`）。</param>
        /// <returns>该层级内 PhysX 组件总数（Rigidbody + Collider）。</returns>
        public static int CountInHierarchy(GameObject root, ref int rigidbodies, ref int colliders,
            List<string> offenders, string scope)
        {
            if (root == null)
            {
                return 0;
            }

            Rigidbody[] rigidbodyComponents = root.GetComponentsInChildren<Rigidbody>(true);
            Collider[] colliderComponents = root.GetComponentsInChildren<Collider>(true);

            rigidbodies += rigidbodyComponents.Length;
            colliders += colliderComponents.Length;

            if (offenders != null)
            {
                foreach (Rigidbody rb in rigidbodyComponents)
                {
                    offenders.Add($"[Rigidbody] {scope}{rb.gameObject.name}");
                }

                foreach (Collider col in colliderComponents)
                {
                    offenders.Add($"[Collider] {scope}{col.gameObject.name}");
                }
            }

            return rigidbodyComponents.Length + colliderComponents.Length;
        }

        /// <summary>
        /// 层级级原语的**便捷形式**：返回层级内 PhysX 组件（Rigidbody + Collider）总数。
        /// 供只关心"注入后计数是多少"的调用方（如负对照测试）使用；
        /// 需要分别得到 Rigidbody / Collider 数时，用带 `ref` 的重载。
        /// </summary>
        public static int CountInHierarchy(GameObject root)
        {
            int rigidbodies = 0;
            int colliders = 0;
            return CountInHierarchy(root, ref rigidbodies, ref colliders, null, null);
        }

        // ── 场景适配层 ─────────────────────────────────────────────────────

        /// <summary>
        /// 统计一个 <see cref="Scene"/> 内的 Rigidbody / Collider。
        /// **薄适配层**：仅负责"迭代根对象"，计数委托给
        /// <see cref="CountInHierarchy(GameObject, ref int, ref int, List{string}, string)"/>，保证口径单一。
        /// </summary>
        public static void CountInScene(Scene scene, ref int rigidbodies, ref int colliders, List<string> offenders)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            string scope = scene.name + "/";
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                CountInHierarchy(root, ref rigidbodies, ref colliders, offenders, scope);
            }
        }

        /// <summary>
        /// 按**固定路径**打开场景（Additive，不干扰当前正在编辑的场景）后统计 Rigidbody。
        /// **唯一入口** —— 刻意不提供 `GetActiveScene()` 变体：那正是 `SCN-06` 原先扫错对象的成因。
        /// </summary>
        public static int CountRigidbodiesInScene(string scenePath)
        {
            if (!File.Exists(FullPath(scenePath)))
            {
                return 0;
            }

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            int rigidbodies = 0;
            int colliders = 0;
            CountInScene(scene, ref rigidbodies, ref colliders, null);
            EditorSceneManager.CloseScene(scene, true);
            return rigidbodies;
        }

        // ── 工程级：Scenes + Prefabs 全量扫描 ──────────────────────────────

        /// <summary>
        /// 遍历 <see cref="ScenesRoot"/> 下全部 `.unity` 与 <see cref="PrefabsRoot"/> 下全部 `.prefab`，
        /// 统计 Rigidbody / Collider 并列出违规对象。
        ///
        /// **用文件系统枚举而非 `AssetDatabase.FindAssets("t:Scene")`**：前者口径明确、不依赖资产过滤器语法，
        /// 且"扫到了几个文件"可直接作为**非空转证据**（见 <see cref="Scan.CoverageIsNonVacuous"/>）。
        /// </summary>
        public static Scan ScanAll()
        {
            var scan = new Scan { Offenders = new List<string>() };

            foreach (string scenePath in EnumerateAssets(ScenesRoot, "*.unity"))
            {
                scan.SceneCount++;
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                CountInScene(scene, ref scan.RigidbodyCount, ref scan.ColliderCount, scan.Offenders);
                EditorSceneManager.CloseScene(scene, true);
            }

            foreach (string prefabPath in EnumerateAssets(PrefabsRoot, "*.prefab"))
            {
                scan.PrefabCount++;
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    // **薄适配层**：只负责加载 prefab，计数委托给唯一原语 CountInHierarchy。
                    CountInHierarchy(root, ref scan.RigidbodyCount, ref scan.ColliderCount,
                        scan.Offenders, prefabPath + "::");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return scan;
        }

        // ── helpers ────────────────────────────────────────────────────────

        private static IEnumerable<string> EnumerateAssets(string assetRoot, string pattern)
        {
            string full = FullPath(assetRoot);
            if (!Directory.Exists(full))
            {
                yield break;
            }

            var found = new List<string>(Directory.GetFiles(full, pattern, SearchOption.AllDirectories));
            found.Sort(System.StringComparer.Ordinal);   // 确定性顺序（铁律：不用无序容器迭代序）

            foreach (string f in found)
            {
                yield return assetRoot + "/" + f.Substring(full.Length).TrimStart('/', '\\').Replace('\\', '/');
            }
        }

        private static string FullPath(string assetPath)
        {
            DirectoryInfo root = Directory.GetParent(Application.dataPath);
            string projectRoot = root == null ? Application.dataPath : root.FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
