using System;
using System.Collections.Generic;
using UnityEngine;

namespace Billiards.Reference
{
    // ─────────────────────────────────────────────────────────────────────
    // 参考数据的 JSON 传输对象（DTO）与两份源文件的逐点交叉比对。
    //
    // 为什么放在 Runtime 程序集：这些是**纯数据契约 + 纯函数**，无任何 Editor 依赖，
    // 放在 Runtime 后可被 Editor 导入器、Editor 校验器、EditMode 测试三方共用，
    // 避免逻辑分叉（同一份比对规则只写一次）。
    //
    // 用 JsonUtility 反序列化，因此字段名必须与 JSON 键**逐字相同**；
    // 未声明的字段会被自动忽略（只声明需要的子集）。
    // 源文件（入库于 Docs/Reference/）：
    //   A = table-metadata-reference.json
    //   B = gameplay-markers-reference.json
    // ─────────────────────────────────────────────────────────────────────

    [Serializable]
    public class Vec2Dto
    {
        public float x;
        public float y;
    }

    [Serializable]
    public class BoundsDto
    {
        public float[] min;
        public float[] max;
    }

    [Serializable]
    public class SegmentDto
    {
        public Vec2Dto a;
        public Vec2Dto b;
        public string rail;
    }

    [Serializable]
    public class PocketDto
    {
        public int index;
        public string kind;
        public Vec2Dto runtimeCenter;
        public float runtimeInscribedRadius;
        public Vec2Dto[] runtimeMouth;
        public Vec2Dto[] runtimeThroat;
        public Vec2Dto runtimeThroatCenter;
        public Vec2Dto runtimeThroatNormal;
        public float runtimeMouthWidth;
        public float runtimeThroatWidth;
        public float openingRadius;
        public float captureRadius;
        public float recommendedCaptureRadius;
        public string captureNote;
    }

    [Serializable]
    public class CollisionDto
    {
        public float halfLength;
        public float halfWidth;
        public float clothHeight;
        public float noseHeight;
        public float ballRadius;
        public SegmentDto[] segments;
        public PocketDto[] pockets;
    }

    [Serializable]
    public class TableMetaDto
    {
        public string source;
        public string source_sha256;
        public string runtime_node;
        public int vertices;
        public int triangles;
        public float uniform_scale;
        public bool nonuniform_scale_applied;
        public float cloth_y;
        public float inner_length;
        public float inner_width;
        public float half_length;
        public float half_width;
        public float nose_height;
        public float lowest_y;
        public BoundsDto bounds_runtime;
        public CollisionDto collision;
    }

    [Serializable]
    public class Physics2dSegDto
    {
        public Vec2Dto a;
        public Vec2Dto b;
        public string rail;
    }

    [Serializable]
    public class SegmentBDto
    {
        public string id;
        public string rail;
        public Physics2dSegDto physics2d;
    }

    [Serializable]
    public class Physics2dPocketDto
    {
        public int index;
        public string kind;
        public float x;
        public float y;
        public float openingRadius;
        public float captureRadius;
        public Vec2Dto[] mouth;
        public Vec2Dto[] throat;
    }

    [Serializable]
    public class PocketBDto
    {
        public string id;
        public int index;
        public string kind;
        public Physics2dPocketDto physics2d;
    }

    [Serializable]
    public class MeasuredTableDto
    {
        public float halfLength;
        public float halfWidth;
        public float clothHeight;
        public float ballRadius;
        public float[] canonical_inner_dimensions_m;
        public float canonical_nose_height_m;
    }

    [Serializable]
    public class OffTableDto
    {
        public float halfX;
        public float halfY;
    }

    [Serializable]
    public class EngineReferenceDto
    {
        public Vec2Dto foot_spot_physics2d;
        public OffTableDto off_table_envelope_physics2d;
    }

    [Serializable]
    public class ValidationDto
    {
        public int pocket_count;
        public int cushion_segment_count;
        public int practice_ball_count;
        public int match_ball_count;
    }

    [Serializable]
    public class GameplayMarkersDto
    {
        public MeasuredTableDto measured_table;
        public PocketBDto[] pockets;
        public SegmentBDto[] cushion_segments;
        public EngineReferenceDto existing_engine_reference_parameters;
        public ValidationDto validation;
    }

    /// <summary>
    /// A（table-metadata）与 B（gameplay-markers）的逐点交叉比对。
    ///
    /// 背景：两份文件都声称含 82 段库边与 6 袋，且都自称权威。任务要求**不自行选一份了事**，
    /// 而是逐点比对并给出差异清单与判定依据。本类把比对做成可复算的纯函数，
    /// 由 Validator 调用并把"一致到什么精度"写进报告，也由 EditMode 测试直接断言。
    /// </summary>
    public static class ReferenceCrossCheck
    {
        /// <summary>判定"逐位一致"的容差。两份文件在 float 下应逐位相同，1e-9 已很宽松。</summary>
        public const float BitIdenticalTolerance = 1e-9f;

        public class Result
        {
            public readonly List<string> Mismatches = new List<string>();

            public int SegmentCountA;
            public int SegmentCountB;
            public int PocketCountA;
            public int PocketCountB;

            public float MaxSegmentEndpointDelta;
            public float MaxPocketCenterDelta;
            public float MaxPocketMouthDelta;
            public float MaxPocketThroatDelta;
            public float MaxRadiusDelta;
            public float MaxMeasuredScalarDelta;

            public bool Identical => Mismatches.Count == 0;

            public float OverallMaxDelta => Mathf.Max(
                Mathf.Max(MaxSegmentEndpointDelta, MaxPocketCenterDelta),
                Mathf.Max(MaxPocketMouthDelta, Mathf.Max(MaxPocketThroatDelta, Mathf.Max(MaxRadiusDelta, MaxMeasuredScalarDelta))));

            public string ToSummaryLine()
            {
                return $"segments {SegmentCountA}/{SegmentCountB}  pockets {PocketCountA}/{PocketCountB}  "
                     + $"mismatches {Mismatches.Count}  maxDelta {OverallMaxDelta:E3} m";
            }
        }

        public static Result Compare(TableMetaDto a, GameplayMarkersDto b)
        {
            var r = new Result();

            if (a == null || a.collision == null)
            {
                r.Mismatches.Add("A 缺少 collision 段。");
                return r;
            }

            if (b == null)
            {
                r.Mismatches.Add("B 解析失败。");
                return r;
            }

            SegmentDto[] sa = a.collision.segments ?? new SegmentDto[0];
            SegmentBDto[] sb = b.cushion_segments ?? new SegmentBDto[0];
            r.SegmentCountA = sa.Length;
            r.SegmentCountB = sb.Length;

            if (sa.Length != sb.Length)
            {
                r.Mismatches.Add($"段数不一致：A={sa.Length} vs B={sb.Length}");
            }

            int segN = Mathf.Min(sa.Length, sb.Length);
            for (int i = 0; i < segN; i++)
            {
                Physics2dSegDto bp = sb[i].physics2d;
                if (bp == null)
                {
                    r.Mismatches.Add($"段[{i}]：B 缺少 physics2d。");
                    continue;
                }

                if (!string.Equals(sa[i].rail, sb[i].rail) || !string.Equals(sa[i].rail, bp.rail))
                {
                    r.Mismatches.Add($"段[{i}] rail 不一致：A='{sa[i].rail}' B='{sb[i].rail}' B.physics2d='{bp.rail}'");
                }

                r.MaxSegmentEndpointDelta = Mathf.Max(
                    r.MaxSegmentEndpointDelta,
                    Vector2.Distance(ToV2(sa[i].a), ToV2(bp.a)),
                    Vector2.Distance(ToV2(sa[i].b), ToV2(bp.b)));
            }

            if (r.MaxSegmentEndpointDelta > BitIdenticalTolerance)
            {
                r.Mismatches.Add($"段端点最大偏差 {r.MaxSegmentEndpointDelta:E6} m 超过容差 {BitIdenticalTolerance:E1}");
            }

            PocketDto[] pa = a.collision.pockets ?? new PocketDto[0];
            PocketBDto[] pb = b.pockets ?? new PocketBDto[0];
            r.PocketCountA = pa.Length;
            r.PocketCountB = pb.Length;

            if (pa.Length != pb.Length)
            {
                r.Mismatches.Add($"袋数不一致：A={pa.Length} vs B={pb.Length}");
            }

            int pockets = Mathf.Min(pa.Length, pb.Length);
            for (int i = 0; i < pockets; i++)
            {
                PocketDto A = pa[i];
                PocketBDto bp = pb[i];
                if (bp == null || bp.physics2d == null)
                {
                    r.Mismatches.Add($"袋[{i}]：B 缺少 physics2d。");
                    continue;
                }

                Physics2dPocketDto B = bp.physics2d;

                if (A.index != bp.index || A.index != B.index)
                {
                    r.Mismatches.Add($"袋[{i}] index 不一致：A={A.index} B={bp.index} B.physics2d={B.index}");
                }

                if (!string.Equals(A.kind, bp.kind) || !string.Equals(A.kind, B.kind))
                {
                    r.Mismatches.Add($"袋[{i}] kind 不一致：A='{A.kind}' B='{bp.kind}/{B.kind}'");
                }

                r.MaxPocketCenterDelta = Mathf.Max(r.MaxPocketCenterDelta,
                    Vector2.Distance(ToV2(A.runtimeCenter), new Vector2(B.x, B.y)));

                r.MaxPocketMouthDelta = Mathf.Max(r.MaxPocketMouthDelta,
                    Vector2.Distance(ToV2(A.runtimeMouth, 0), ToV2(B.mouth, 0)),
                    Vector2.Distance(ToV2(A.runtimeMouth, 1), ToV2(B.mouth, 1)));

                r.MaxPocketThroatDelta = Mathf.Max(r.MaxPocketThroatDelta,
                    Vector2.Distance(ToV2(A.runtimeThroat, 0), ToV2(B.throat, 0)),
                    Vector2.Distance(ToV2(A.runtimeThroat, 1), ToV2(B.throat, 1)));

                r.MaxRadiusDelta = Mathf.Max(r.MaxRadiusDelta,
                    Mathf.Abs(A.openingRadius - B.openingRadius),
                    Mathf.Abs(A.captureRadius - B.captureRadius));
            }

            if (r.MaxPocketCenterDelta > BitIdenticalTolerance) r.Mismatches.Add($"袋中心最大偏差 {r.MaxPocketCenterDelta:E6} m");
            if (r.MaxPocketMouthDelta > BitIdenticalTolerance) r.Mismatches.Add($"袋 mouth 最大偏差 {r.MaxPocketMouthDelta:E6} m");
            if (r.MaxPocketThroatDelta > BitIdenticalTolerance) r.Mismatches.Add($"袋 throat 最大偏差 {r.MaxPocketThroatDelta:E6} m");
            if (r.MaxRadiusDelta > BitIdenticalTolerance) r.Mismatches.Add($"袋半径最大偏差 {r.MaxRadiusDelta:E6} m");

            MeasuredTableDto mt = b.measured_table;
            if (mt == null)
            {
                r.Mismatches.Add("B 缺少 measured_table。");
            }
            else
            {
                r.MaxMeasuredScalarDelta = Mathf.Max(
                    Mathf.Abs(a.half_length - mt.halfLength),
                    Mathf.Abs(a.half_width - mt.halfWidth),
                    Mathf.Abs(a.cloth_y - mt.clothHeight),
                    Mathf.Abs(a.collision.ballRadius - mt.ballRadius),
                    Mathf.Abs(a.nose_height - mt.canonical_nose_height_m));

                if (mt.canonical_inner_dimensions_m != null && mt.canonical_inner_dimensions_m.Length >= 2)
                {
                    r.MaxMeasuredScalarDelta = Mathf.Max(
                        r.MaxMeasuredScalarDelta,
                        Mathf.Abs(a.inner_length - mt.canonical_inner_dimensions_m[0]),
                        Mathf.Abs(a.inner_width - mt.canonical_inner_dimensions_m[1]));
                }
                else
                {
                    r.Mismatches.Add("B.measured_table.canonical_inner_dimensions_m 缺失。");
                }

                if (r.MaxMeasuredScalarDelta > BitIdenticalTolerance)
                {
                    r.Mismatches.Add($"全局标量最大偏差 {r.MaxMeasuredScalarDelta:E6} m");
                }
            }

            if (b.validation != null)
            {
                if (b.validation.pocket_count != pb.Length)
                {
                    r.Mismatches.Add($"B.validation.pocket_count={b.validation.pocket_count} != B.pockets 长度 {pb.Length}");
                }

                if (b.validation.cushion_segment_count != sb.Length)
                {
                    r.Mismatches.Add($"B.validation.cushion_segment_count={b.validation.cushion_segment_count} != B.cushion_segments 长度 {sb.Length}");
                }
            }
            else
            {
                r.Mismatches.Add("B 缺少 validation。");
            }

            return r;
        }

        private static Vector2 ToV2(Vec2Dto v)
        {
            return v == null ? new Vector2(float.NaN, float.NaN) : new Vector2(v.x, v.y);
        }

        private static Vector2 ToV2(Vec2Dto[] arr, int i)
        {
            return (arr == null || i >= arr.Length) ? new Vector2(float.NaN, float.NaN) : ToV2(arr[i]);
        }
    }
}
