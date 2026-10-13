using System;
using System.Collections.Generic;
using Billiards.Core;
using UnityEngine;

namespace Billiards.Table
{
    /// <summary>
    /// 球桌碰撞几何的**运行时真源**。由 <c>TableDataImporter</c> 从
    /// <c>Docs/Reference/table-metadata-reference.json</c> 生成，不应手工编辑。
    ///
    /// 数据语义（与 JSON 一一对应）：
    ///  · 82 段库边/袋角/弧段：每段为二维线段 {a, b} + rail 归属；
    ///  · 6 个袋口：袋口中心、袋喉(mouth)、袋喉内沿(throat)、袋喉中心/法向、口宽/喉宽、
    ///    开口半径 openingRadius、捕获半径 captureRadius 与捕获规则 captureNote。
    ///
    /// 坐标：全部位于**规范二维物理平面**（见 PoolGeometry）—— x 长轴、y 短轴，单位米。
    /// 竖直方向（台呢高度、库鼻高度）以标量记录，物理模拟是二维的。
    ///
    /// 与 PoolConstants 的关系：PoolConstants 是**单位制与全局硬指标**的唯一真源；
    /// TableConfig 是**逐段/逐袋碰撞几何**的唯一真源。两者的公共标量（内库尺寸、球半径、
    /// 台呢高度、袋口半径）必须一致，由 <see cref="ConsistencyErrors"/> 与 Validator 断言。
    /// </summary>
    [CreateAssetMenu(fileName = "TableConfig", menuName = "Billiards/Table Config", order = 0)]
    public class TableConfig : ScriptableObject
    {
        // ── 溯源（可复现性：谁在什么时间从哪份源生成了这份配置）──────────────

        [Header("Provenance")]
        public string sourceFileName = "table-metadata-reference.json";
        public string sourceSha256 = string.Empty;
        public string runtimeNode = string.Empty;
        public string generatedAtUtc = string.Empty;
        public string generator = "TableDataImporter";
        public int sourceVertexCount;
        public int sourceTriangleCount;
        public float sourceUniformScale;
        public bool sourceNonUniformScaleApplied;

        // ── 全局尺寸（米；与 PoolConstants 必须一致）───────────────────────

        [Header("Dimensions (metres)")]
        public float innerLength;
        public float innerWidth;
        public float halfLength;
        public float halfWidth;
        public float clothHeight;
        public float noseHeight;
        public float ballRadius;
        public float legBottomY;

        [Header("Outer bounds (runtime3d: X long, Y up, Z short)")]
        public Vector3 outerBoundsMin;
        public Vector3 outerBoundsMax;

        // ── 碰撞几何 ───────────────────────────────────────────────────────

        [Header("Collision geometry")]
        public List<CushionSegment> segments = new List<CushionSegment>();
        public List<Pocket> pockets = new List<Pocket>();

        // ── 引擎参考参数（来自 gameplay-markers-reference.json，仅参考，非最终规则）──

        [Header("Engine reference parameters")]
        [Tooltip("脚点（开球点）的物理平面坐标。来自源原型 pool-engine.js 的 foot_spot。")]
        public Vector2 footSpot;
        [Tooltip("球桌离屏包络半尺寸 {halfX, halfY}。来自源原型的 off_table envelope。")]
        public Vector2 offTableHalfExtents;

        public int SegmentCount => segments == null ? 0 : segments.Count;
        public int PocketCount => pockets == null ? 0 : pockets.Count;

        /// <summary>按 index 取袋口；越界返回 null。</summary>
        public Pocket GetPocket(int index)
        {
            if (pockets == null)
            {
                return null;
            }

            foreach (Pocket p in pockets)
            {
                if (p.index == index)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>
        /// 与 PoolConstants（单位制唯一真源）的一致性校验。返回空列表即一致。
        /// 容差取 1e-6 m：数据经 float 存储，源 JSON 的 double 精度在 1e-6 m 量级已足够。
        /// </summary>
        public List<string> ConsistencyErrors(float tolerance = 1e-6f)
        {
            var errors = new List<string>();

            Check(errors, "innerLength", innerLength, PoolConstants.ClothInnerLength, tolerance);
            Check(errors, "innerWidth", innerWidth, PoolConstants.ClothInnerWidth, tolerance);
            Check(errors, "halfLength", halfLength, PoolConstants.ClothHalfLength, tolerance);
            Check(errors, "halfWidth", halfWidth, PoolConstants.ClothHalfWidth, tolerance);
            Check(errors, "clothHeight", clothHeight, PoolConstants.ClothHeightY, tolerance);
            Check(errors, "noseHeight", noseHeight, PoolConstants.CushionNoseHeight, tolerance);
            Check(errors, "ballRadius", ballRadius, PoolConstants.BallRadius, tolerance);
            Check(errors, "legBottomY", legBottomY, PoolConstants.TableLegBottomY, tolerance);

            if (SegmentCount != PoolConstants.CushionSegmentCount)
            {
                errors.Add($"segment count {SegmentCount} != PoolConstants.CushionSegmentCount {PoolConstants.CushionSegmentCount}");
            }

            if (PocketCount != PoolConstants.PocketCount)
            {
                errors.Add($"pocket count {PocketCount} != PoolConstants.PocketCount {PoolConstants.PocketCount}");
            }

            foreach (Pocket p in pockets)
            {
                Check(errors, $"pocket[{p.index}].openingRadius", p.openingRadius, PoolConstants.PocketOpeningRadius, tolerance);
                Check(errors, $"pocket[{p.index}].captureRadius", p.captureRadius, PoolConstants.PocketCaptureRadius, 1e-6f);
            }

            return errors;
        }

        private static void Check(List<string> errors, string name, float actual, float expected, float tolerance)
        {
            if (Mathf.Abs(actual - expected) > tolerance)
            {
                errors.Add($"{name} {actual:R} != expected {expected:R}");
            }
        }
    }

    /// <summary>
    /// 一段库边/袋角/弧段。a、b 为物理平面上的端点；rail 是源数据里的栏板归属，
    /// 用于调试与分组（如 end_negative / long_positive_positive），不参与碰撞数学。
    /// </summary>
    [Serializable]
    public struct CushionSegment
    {
        public Vector2 a;
        public Vector2 b;
        public string rail;
    }

    /// <summary>
    /// 一个球袋。mouth 为袋口（张嘴）两端，throat 为袋喉（收口）两端。
    /// captureNote 是**不可省略**的捕获规则：仅当球心穿过袋喉、朝网方向运动后才判定捕获；
    /// captureRadius 已扣除球半径与 2 mm 余量，单靠半径不足以判定进袋。
    /// </summary>
    [Serializable]
    public class Pocket
    {
        public int index;
        public string kind;            // "corner" | "side"

        public Vector2 center;         // runtimeCenter
        public Vector2 mouthA;         // runtimeMouth[0]
        public Vector2 mouthB;         // runtimeMouth[1]
        public Vector2 throatA;        // runtimeThroat[0]
        public Vector2 throatB;        // runtimeThroat[1]
        public Vector2 throatCenter;   // runtimeThroatCenter
        public Vector2 throatNormal;   // runtimeThroatNormal（单位向量，指向网内）

        public float mouthWidth;
        public float throatWidth;
        public float openingRadius;
        public float captureRadius;
        public string captureNote;

        public bool IsCorner => kind == "corner";
        public bool IsSide => kind == "side";
    }
}
