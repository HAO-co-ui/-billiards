using Billiards.Core;
using NUnit.Framework;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// Day 3 / ADR-0001 §9：**模型朝向常量**与"物理帧 ↔ 世界坐标"不变量的测试。
    ///
    /// 背景：Day 2 曾把 `PhysicsToUnity` 定为 `(px, h, py)`，但它**只与网格局部顶点比对过**，
    /// 从未与"实例化后的世界坐标"比对，因而漏掉了模型根节点自带的 `Rx≈270°`
    /// （= <see cref="PoolGeometry.ModelOrientation"/>），使两者相差绕 Y 的 180°。
    /// 本组测试把该不变量钉死，并**证明它可以被证伪**（翻转朝向常量必然失败）。
    /// </summary>
    public class ModelOrientationTests
    {
        private const float Tol = 1e-6f;

        /// <summary>ADM 的判决量：本桌角袋 x 向不对称，等于"世界包围盒 X 端镜像"的量级。</summary>
        private const float CornerAsymmetry = 2.77e-4f;

        [Test]
        public void ModelOrientation_IsMinus90AboutX()
        {
            // FBX 根节点自带 Rx≈270°（= −90°）：实测场景 rootRot=(270.02,0,0)
            Vector3 up = PoolGeometry.ModelOrientation * new Vector3(0f, 0f, 1f);
            Assert.AreEqual(0f, up.x, Tol);
            Assert.AreEqual(1f, up.y, Tol, "模型局部 +Z（源的上轴）必须落到世界 +Y");
            Assert.AreEqual(0f, up.z, Tol);

            // 纯旋转（det=+1）：不引入镜像
            Vector3 ex = PoolGeometry.ModelOrientation * Vector3.right;
            Vector3 ey = PoolGeometry.ModelOrientation * Vector3.up;
            Vector3 ez = PoolGeometry.ModelOrientation * Vector3.forward;
            float det = Vector3.Dot(Vector3.Cross(ex, ey), ez);
            Assert.AreEqual(1f, det, 1e-5f, "朝向常量必须是纯旋转，否则会翻转三角形绕序。");
        }

        [Test]
        public void PhysicsToUnity_MatchesModelVertexToUnity_AlongWholeChain()
        {
            // 不变量：物理点 (px,py) 在世界里的位置，必须等于"该物理点在网格局部空间中的对应顶点"
            //         经唯一模型侧转换后的世界位置。
            var samples = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1.27f, 0.635f),
                new Vector2(-1.29147879593222f, -0.6560756205992297f),
                new Vector2(1.291202f, 0.656076f),
                new Vector2(0.51f, 0f),
            };

            foreach (Vector2 p in samples)
            {
                const float height = 0.12f;
                Vector3 viaPhysics = PoolGeometry.PhysicsToUnity(p, height);
                Vector3 viaLocal = PoolGeometry.ModelVertexToUnity(PoolGeometry.PhysicsToModelLocal(p, height));
                Assert.AreEqual(viaLocal.x, viaPhysics.x, Tol);
                Assert.AreEqual(viaLocal.y, viaPhysics.y, Tol);
                Assert.AreEqual(viaLocal.z, viaPhysics.z, Tol);
            }
        }

        [Test]
        public void PhysicsToUnity_RoundTripsThroughUnityToPhysics()
        {
            var samples = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1.27f, 0.635f),
                new Vector2(-1.334342835f, -0.699095331f),
                new Vector2(0.51f, 0f),
            };

            foreach (Vector2 p in samples)
            {
                Vector2 back = PoolGeometry.UnityToPhysics(PoolGeometry.PhysicsToUnity(p, 0.03f));
                Assert.AreEqual(p.x, back.x, 1e-6f);
                Assert.AreEqual(p.y, back.y, 1e-6f);
            }
        }

        [Test]
        public void WorldBounds_AreDerived_AndRetainCornerAsymmetry()
        {
            // 世界包围盒由转换链从源包络推导（不硬编码），X 端应呈镜像后的**不对称**符号
            Vector3 min = PoolGeometry.WorldBoundsMin;
            Vector3 max = PoolGeometry.WorldBoundsMax;

            // 世界 X 端 = 源 X 端的镜像：min.x = -OuterMax.x, max.x = -OuterMin.x
            Assert.AreEqual(-PoolGeometry.OuterBoundsMax.x, min.x, Tol);
            Assert.AreEqual(-PoolGeometry.OuterBoundsMin.x, max.x, Tol);

            // 竖直轴保持"桌腿底在下"，Z（短轴）对称
            Assert.AreEqual(PoolGeometry.OuterBoundsMin.y, min.y, Tol);
            Assert.AreEqual(PoolGeometry.OuterBoundsMax.y, max.y, Tol);

            // 判决量：X 端不对称应恰为 2.77e-4 m 量级，且**远大于**任何数值容差（0.1 mm）
            float asym = Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x))
                       - Mathf.Min(Mathf.Abs(min.x), Mathf.Abs(max.x));
            Assert.AreEqual(CornerAsymmetry, asym, 1e-5f,
                "角袋 x 向不对称是「朝向是否被翻转」的判决量");
            Assert.Greater(asym, TableIntegrityValidatorTol, "判决量必须大于容差，否则判据无判别力");
        }

        /// <summary>与校验器 SCN-04 / AXL-02 同源的容差（0.1 mm）。</summary>
        private const float TableIntegrityValidatorTol = 1e-4f;

        // ── 【必须证伪】翻转朝向常量 → 世界一致性检查必然 FAIL ──────────────────

        [Test]
        public void Falsification_FlippingOrientationConstant_MakesWorldBoundsCheckFail()
        {
            // 正确朝向 vs 翻转朝向（+90° 代替 −90°）：后者会在世界空间把几何翻过去，
            // 使与"正确期望包络"的偏差跳到角袋不对称量级（≈2.77e-4 m），**超过 1e-4 m 容差** ⇒ 检查必然 FAIL。
            Vector3 correctMin = PoolGeometry.WorldBoundsMin;
            Vector3 correctMax = PoolGeometry.WorldBoundsMax;

            // "翻转朝向"= 在正确姿态上再多转绕 Y 的 180° —— 这正是 Day 2 漏掉 ModelOrientation 时的实际误差。
            // （若改用 Rx(+90°) 代替 Rx(−90°)，会把桌子翻成头朝下，偏差达 0.80 m；那不是本项目的既有错误形态，故不采用。）
            Quaternion flipped = Quaternion.Euler(0f, 180f, 0f) * PoolGeometry.ModelOrientation;
            Vector3 flippedMin = BoundsCornerWith(flipped, false, false, false);
            Vector3 flippedMax = BoundsCornerWith(flipped, true, true, true);

            float err = Mathf.Max(
                Mathf.Abs(flippedMin.x - correctMin.x), Mathf.Abs(flippedMin.y - correctMin.y), Mathf.Abs(flippedMin.z - correctMin.z),
                Mathf.Abs(flippedMax.x - correctMax.x), Mathf.Abs(flippedMax.y - correctMax.y), Mathf.Abs(flippedMax.z - correctMax.z));

            TestContext.WriteLine($"[证伪] 翻转朝向常量后，世界包围盒与正确期望的最大偏差 = {err:E3} m");
            TestContext.WriteLine($"[证伪] 容差 = {TableIntegrityValidatorTol:E0} m ⇒ 判定 = {(err > TableIntegrityValidatorTol ? "FAIL（如预期）" : "PASS（不可接受）")}");

            Assert.Greater(err, TableIntegrityValidatorTol,
                "翻转朝向常量后检查**必须**失败；若这里通过了，说明该检查没有判别力（形同虚设）。");
            Assert.AreEqual(CornerAsymmetry, err, 1e-5f,
                "失败的偏差量应等于角袋 x 向不对称（判决量），以此证明判据确实抓的是朝向。");
        }

        private static Vector3 BoundsCornerWith(Quaternion orientation, bool maxX, bool maxY, bool maxZ)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int xi = 0; xi < 2; xi++)
            for (int yi = 0; yi < 2; yi++)
            for (int zi = 0; zi < 2; zi++)
            {
                float px = xi == 0 ? PoolGeometry.OuterBoundsMin.x : PoolGeometry.OuterBoundsMax.x;
                float up = yi == 0 ? PoolGeometry.OuterBoundsMin.y : PoolGeometry.OuterBoundsMax.y;
                float py = zi == 0 ? PoolGeometry.OuterBoundsMin.z : PoolGeometry.OuterBoundsMax.z;
                Vector3 w = orientation * PoolGeometry.PhysicsToModelLocal(new Vector2(px, py), up);
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }

            return new Vector3(maxX ? max.x : min.x, maxY ? max.y : min.y, maxZ ? max.z : min.z);
        }
    }
}
