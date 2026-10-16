using System;
using Billiards.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Billiards.EditorTools
{
    /// <summary>
    /// **球桌可视物 + 最小布景的唯一实现点**（`Main` 场景与物理灰盒**共用**，铁律 16）。
    ///
    /// ── 为什么必须收成一处（本类是被用户实机反馈逼出来的）─────────────────────
    /// 用户报"`PhysicsGreybox` 场景全是黑的、看不到球袋在哪"。根因：**灰盒场景从来就没建过球桌模型** ——
    /// 它只建了球、相机、灯；而库边是靠 `CushionGizmo` 的 `Gizmos.DrawLine` 画的，
    /// **`Gizmos` 只在编辑器 Scene 视图可见，Game 视图里什么都不显示**。于是 Game 视图里只有黑底上的几颗球。
    /// 而 `Main` 场景（`MaterialVisualSetup` 生成）**早就有**桌模型 + 袋网材质 + 地面。
    /// ⇒ 正确修法不是"在灰盒里再写一份摆桌代码"，而是把这份能力**提出来共用**：
    /// 否则两个场景的桌子迟早分叉（材质路径 / 子网格约定 / Layer 三项任一漂移就长得不一样）。
    ///
    /// ── 子网格约定（来自 Day 3 的美术导入，**不可猜**）────────────────────────
    /// 桌模型 `PoolTable_Normalized.fbx` 的子网格：**0 = 桌体（不透明）、1 = 袋网（Alpha Clipping）**。
    /// 若模型只有 1 个子网格，则整体用桌体材质。
    /// </summary>
    public static class TableVisualBuilder
    {
        public const string TableObjectName = "PoolTable";
        public const string RoomObjectName = "Room";
        public const string FloorObjectName = "Floor";

        /// <summary>把球桌模型挂到 <paramref name="parent"/> 下（用 `SetParent(parent, false)` ⇒ 父级缩放照旧生效）。</summary>
        public static GameObject AttachTable(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            GameObject table = InstantiateModel(MaterialVisualSetup.TableModelPath, TableObjectName);
            table.transform.SetParent(parent, false);
            SetLayerRecursive(table, BilliardsLayers.Table);
            AssignTableMaterials(table);
            return table;
        }

        /// <summary>
        /// 把最小布景（地面，与**桌腿底**对齐）挂到 <paramref name="parent"/> 下。
        /// 用 `localPosition` 而不是 `position`：父级带缩放时（灰盒的可视根 ×1.2）地面才会跟着桌腿一起抬，
        /// 不会出现"桌子浮在地面上方"的错位。
        /// </summary>
        public static GameObject AttachRoom(Transform parent)
        {
            var room = new GameObject(RoomObjectName);
            if (parent != null)
            {
                room.transform.SetParent(parent, false);   // 传 null ⇒ 落在场景根（`Main` 场景即如此）
            }

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = FloorObjectName;
            Collider floorCollider = floor.GetComponent<Collider>();
            if (floorCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(floorCollider);   // 自研物理：场景不放碰撞体
            }

            floor.transform.SetParent(room.transform, false);
            floor.transform.localPosition = new Vector3(0f, PoolConstants.TableLegBottomY, 0f);
            floor.transform.localScale = new Vector3(3f, 1f, 3f);      // 默认平面 10×10 ⇒ 30×30 m
            SetLayerRecursive(floor, BilliardsLayers.Environment);

            var renderer = floor.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialVisualSetup.FloorMaterialPath);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            // 球房环境（墙 + 13 件美术道具）：走**唯一实现点** `RoomEnvironmentSetup`。
            // 落点仍是本方法创建的 `Room`（不新建地面、不移动 Room）⇒ `SCN-01`/`SCN-07` 语义不变。
            RoomEnvironmentSetup.Attach(room.transform);

            return room;
        }

        /// <summary>给球桌网格按**子网格**分配材质：0 = 桌体（不透明），1 = 袋网（Alpha Clipping）。</summary>
        public static void AssignTableMaterials(GameObject root)
        {
            Material body = AssetDatabase.LoadAssetAtPath<Material>(MaterialVisualSetup.BodyMaterialPath);
            Material nets = AssetDatabase.LoadAssetAtPath<Material>(MaterialVisualSetup.NetsMaterialPath);

            foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                int subMeshes = (mf != null && mf.sharedMesh != null) ? Mathf.Max(1, mf.sharedMesh.subMeshCount) : 1;
                var mats = new Material[subMeshes];
                for (int i = 0; i < subMeshes; i++)
                {
                    mats[i] = (i == 1) ? nets : body;
                }

                mr.sharedMaterials = mats;
            }
        }

        public static GameObject InstantiateModel(string modelPath, string name)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null)
            {
                throw new InvalidOperationException("无法加载模型：" + modelPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = name;
            return instance;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
