using System.Collections.Generic;
using UnityEngine;

namespace Billiards.Diagnostics
{
    /// <summary>
    /// 网格对齐的**三层度量**（均匀网格加速）：
    ///  · **L1 面点**：端点 → 最近三角形**面内点**（含内部与边）—— 只验"是否落在网格面上"；
    ///  · **L2 边距**：端点 → 最近**网格边**（线段）—— 高灵敏，但**对沿边方向滑动全盲**；
    ///  · **L3 顶点**：端点 → 最近**顶点**，并用最近三角形的**最长边** `L_local` 归一（比值）——
    ///    **唯一无方向性盲区**，但存在**周期性盲带**（δ 增大时点落到另一顶点附近，距离回落）。
    ///
    /// 三层各有明确盲区，**叠加覆盖彼此**；谁也不作"唯一位移判据"。
    ///
    /// 为什么放在 `Billiards.Diagnostics`（Editor-only）：Editor 校验器与 EditMode 测试
    /// **必须共用同一实现**（否则口径分叉），但 **Runtime 内零消费者** ——
    /// 放在 Runtime 会被编进玩家构建（实测 281/1118 行），且其构造会建多个 Dictionary/数组，
    /// 一旦有人在运行时 `new` 它就是一次性大分配。本类无 Editor API 依赖、无副作用，仅做几何计算。
    /// </summary>
    public sealed class MeshAlignmentMetrics
    {
        public struct Measurement
        {
            /// <summary>到最近三角形面内点的水平距离（米）。</summary>
            public float Face;
            /// <summary>到最近网格边的水平距离（米）。</summary>
            public float Edge;
            /// <summary>到最近顶点的水平距离（米）。</summary>
            public float Vertex;
            /// <summary>最近三角形的**最长边**（网格在该处的特征间距，米）。</summary>
            public float LocalLongestEdge;
        }

        private const float Cell = 0.02f;   // 20 mm 网格

        private readonly Vector3[] _v;
        private readonly int[] _tri;
        private readonly int[] _edgeA;
        private readonly int[] _edgeB;

        private readonly Dictionary<long, List<int>> _cellVerts = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, List<int>> _cellEdges = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, List<int>> _cellTris = new Dictionary<long, List<int>>();
        private readonly List<int> _globalTris = new List<int>();

        public MeshAlignmentMetrics(Vector3[] vertices, int[] triangles)
        {
            _v = vertices;
            _tri = triangles;

            // 唯一边（去重）
            var edgeSet = new HashSet<long>();
            var ea = new List<int>();
            var eb = new List<int>();
            for (int t = 0; t + 2 < _tri.Length; t += 3)
            {
                AddEdge(edgeSet, ea, eb, _tri[t], _tri[t + 1]);
                AddEdge(edgeSet, ea, eb, _tri[t + 1], _tri[t + 2]);
                AddEdge(edgeSet, ea, eb, _tri[t + 2], _tri[t]);
            }

            _edgeA = ea.ToArray();
            _edgeB = eb.ToArray();

            for (int i = 0; i < _v.Length; i++)
            {
                Register(_cellVerts, Key(_v[i].x, _v[i].z), i);
            }

            for (int i = 0; i < _edgeA.Length; i++)
            {
                Vector3 a = _v[_edgeA[i]], b = _v[_edgeB[i]];
                foreach (long k in CellsInBox(a.x, a.z, b.x, b.z))
                {
                    Register(_cellEdges, k, i);
                }
            }

            for (int t = 0; t + 2 < _tri.Length; t += 3)
            {
                Vector3 a = _v[_tri[t]], b = _v[_tri[t + 1]], c = _v[_tri[t + 2]];
                float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                int cells = (int)((maxX - minX) / Cell + 1) * (int)((maxZ - minZ) / Cell + 1);
                if (cells > 4000)
                {
                    _globalTris.Add(t);   // 巨型三角形（如台呢面）直接全局登记
                    continue;
                }

                foreach (long k in CellsInBox(minX, minZ, maxX, maxZ))
                {
                    Register(_cellTris, k, t);
                }
            }
        }

        private static void AddEdge(HashSet<long> set, List<int> ea, List<int> eb, int i, int j)
        {
            int lo = i < j ? i : j;
            int hi = i < j ? j : i;
            long key = ((long)lo << 32) | (uint)hi;
            if (set.Add(key))
            {
                ea.Add(lo);
                eb.Add(hi);
            }
        }

        private static void Register(Dictionary<long, List<int>> map, long key, int value)
        {
            if (!map.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                map[key] = list;
            }

            list.Add(value);
        }

        private static long Key(float x, float z)
        {
            long cx = (long)Mathf.Floor(x / Cell);
            long cz = (long)Mathf.Floor(z / Cell);
            return (cx << 32) ^ (cz & 0xffffffffL);
        }

        private static IEnumerable<long> CellsInBox(float x0, float z0, float x1, float z1)
        {
            long cx0 = (long)Mathf.Floor(Mathf.Min(x0, x1) / Cell);
            long cx1 = (long)Mathf.Floor(Mathf.Max(x0, x1) / Cell);
            long cz0 = (long)Mathf.Floor(Mathf.Min(z0, z1) / Cell);
            long cz1 = (long)Mathf.Floor(Mathf.Max(z0, z1) / Cell);
            for (long cx = cx0; cx <= cx1; cx++)
            {
                for (long cz = cz0; cz <= cz1; cz++)
                {
                    yield return (cx << 32) ^ (cz & 0xffffffffL);
                }
            }
        }

        /// <summary>一次查询给出 L1/L2/L3 三个量。</summary>
        public Measurement Measure(Vector3 q)
        {
            var m = new Measurement();
            long cx = (long)Mathf.Floor(q.x / Cell);
            long cz = (long)Mathf.Floor(q.z / Cell);

            float bestFace = float.MaxValue, bestEdge = float.MaxValue, bestVert = float.MaxValue;
            float localLongest = 0f;

            for (int ring = 0; ring <= 64; ring++)
            {
                // 提前退出：本环最近可能的距离已超过当前最优
                float ringMin = (ring - 1) * Cell;
                if (ring > 1 && ringMin > Mathf.Max(bestFace, Mathf.Max(bestEdge, bestVert)))
                {
                    break;
                }

                ScanRing(q, cx, cz, ring, ref bestFace, ref bestEdge, ref bestVert, ref localLongest);
            }

            // 巨型三角形（全局登记）
            for (int i = 0; i < _globalTris.Count; i++)
            {
                int t = _globalTris[i];
                Vector3 a = _v[_tri[t]], b = _v[_tri[t + 1]], c = _v[_tri[t + 2]];
                float d = TriDistSq(q, a, b, c);
                if (d < bestFace)
                {
                    bestFace = d;
                    localLongest = Mathf.Max(Vector3.Distance(a, b), Mathf.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                }
            }

            m.Face = Mathf.Sqrt(bestFace);
            m.Edge = Mathf.Sqrt(bestEdge);
            m.Vertex = Mathf.Sqrt(bestVert);
            m.LocalLongestEdge = localLongest;
            return m;
        }

        private void ScanRing(Vector3 q, long cx, long cz, int ring,
            ref float bestFace, ref float bestEdge, ref float bestVert, ref float localLongest)
        {
            for (long dx = -ring; dx <= ring; dx++)
            {
                for (long dz = -ring; dz <= ring; dz++)
                {
                    if (ring > 0 && Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring)
                    {
                        continue;   // 只扫环（内部已扫过）
                    }

                    long k = ((cx + dx) << 32) ^ ((cz + dz) & 0xffffffffL);

                    if (_cellVerts.TryGetValue(k, out List<int> vs))
                    {
                        foreach (int i in vs)
                        {
                            float ddx = _v[i].x - q.x, ddz = _v[i].z - q.z;
                            float d = ddx * ddx + ddz * ddz;
                            if (d < bestVert)
                            {
                                bestVert = d;
                            }
                        }
                    }

                    if (_cellEdges.TryGetValue(k, out List<int> es))
                    {
                        foreach (int i in es)
                        {
                            float d = SegDistSq(q, _v[_edgeA[i]], _v[_edgeB[i]]);
                            if (d < bestEdge)
                            {
                                bestEdge = d;
                            }
                        }
                    }

                    if (_cellTris.TryGetValue(k, out List<int> ts))
                    {
                        foreach (int t in ts)
                        {
                            Vector3 a = _v[_tri[t]], b = _v[_tri[t + 1]], c = _v[_tri[t + 2]];
                            float d = TriDistSq(q, a, b, c);
                            if (d < bestFace)
                            {
                                bestFace = d;
                                localLongest = Mathf.Max(Vector3.Distance(a, b), Mathf.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                            }
                        }
                    }
                }
            }
        }

        // ── 几何 ────────────────────────────────────────────────────────────

        private static float TriDistSq(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float projArea2 = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            if (Mathf.Abs(projArea2) >= 1e-12f && InsideXZ(p, a, b, c))
            {
                return 0f;
            }

            return Mathf.Min(SegDistSq(p, a, b), Mathf.Min(SegDistSq(p, b, c), SegDistSq(p, c, a)));
        }

        private static bool InsideXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static float Cross(Vector3 p, Vector3 a, Vector3 b)
        {
            return (a.x - p.x) * (b.z - p.z) - (a.z - p.z) * (b.x - p.x);
        }

        private static float SegDistSq(Vector3 q, Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float len2 = dx * dx + dz * dz;
            if (len2 <= 1e-18f)
            {
                float x0 = q.x - a.x, z0 = q.z - a.z;
                return x0 * x0 + z0 * z0;
            }

            float t = Mathf.Clamp01(((q.x - a.x) * dx + (q.z - a.z) * dz) / len2);
            float ex = q.x - (a.x + t * dx), ez = q.z - (a.z + t * dz);
            return ex * ex + ez * ez;
        }
    }
}
