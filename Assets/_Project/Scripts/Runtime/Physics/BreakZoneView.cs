using Billiards.Core;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **开球摆球区提示**（用户要求）：开球前把「白球可摆放的区域」画成一闪一闪的白色半透明地面。
    ///
    /// 边界口径与判定**同源**：区域 = 开球线之后（`x > PoolConstants.KitchenLineX`）到库边内缩一个球位，
    /// 即 `PoolSimulator.IsPlacementLegalInKitchen` 的可行域近似；**判定本身只看那个纯函数**，
    /// 本组件只做"看得见"，不参与合法与否。
    ///
    /// 复用 `RuntimeLineMaterial.Shared`（**不新增材质实例**）：项目材质预算是 30/30 零余量。
    /// 显隐只看 <see cref="PhysicsTestDrive.BreakPlacementActive"/>（**单一真源**）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BreakZoneView : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>驱动源（由 <see cref="PhysicsTestDrive"/> 在构建时注入）。</summary>
        public PhysicsTestDrive drive;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;

        private void LateUpdate()
        {
            if (_renderer == null)
            {
                return;
            }

            // ⚠ **首页/结算/设置/引导打开时一律不显示**（用户："主页面不要有闪白"）：
            //   这些界面把球台当背景展示，开球提示在那里没有意义，只会变成脏斑。
            bool modal = Billiards.UI.HomeView.IsOpen
                         || Billiards.UI.PracticeResultView.IsOpen
                         || Billiards.UI.PracticeHudView.IsSettingsOpen
                         || Billiards.UI.PracticeHudView.IsOnboardingOpen;
            bool show = drive != null && drive.BreakPlacementActive && !modal;
            if (_renderer.enabled != show)
            {
                _renderer.enabled = show;
            }

            if (!show)
            {
                return;
            }

            // 闪白：0.05–0.14 的呼吸，够显眼但不盖住球与线。
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f);
            _block.SetColor(ColorId, new Color(1f, 1f, 1f, Mathf.Lerp(0.05f, 0.14f, wave)));
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// 构建区域网格（**在本地米制平面**：`(x, 0, y)`，与球的可视物同一父节点 ⇒ 缩放随台面）。
        /// </summary>
        public void Build(Transform parent, float kitchenLineX, float halfLength, float halfWidth, float ballRadius)
        {
            if (_renderer != null)
            {
                return;
            }

            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = Vector3.zero;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = Vector3.one;

            // 内缩：开球线留 2 mm（避免与线重叠闪烁），库边留一个球半径。
            float x0 = kitchenLineX + 0.002f;
            float x1 = halfLength - ballRadius;
            float y0 = -halfWidth + ballRadius;
            float y1 = halfWidth - ballRadius;
            float z = 0.001f;   // 抬高 1 mm，避免与台呢 z-fighting

            // ⚠ **必须走唯一转换点 `PoolGeometry.PhysicsToUnity`**：映射是 `(−px, h, −py)`。
            //   之前直接写 `(x, z, y)` ⇒ X/Y 两个符号都反了 ⇒ 区域被画到**对角**（用户实机截图：闪白在脚端）。
            var mesh = new Mesh { name = "BreakZone" };
            mesh.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                PoolGeometry.PhysicsToUnity(new Vector2(x0, y0), z),
                PoolGeometry.PhysicsToUnity(new Vector2(x1, y0), z),
                PoolGeometry.PhysicsToUnity(new Vector2(x1, y1), z),
                PoolGeometry.PhysicsToUnity(new Vector2(x0, y1), z),
            });
            mesh.SetColors(new System.Collections.Generic.List<Color>
            {
                Color.white, Color.white, Color.white, Color.white,
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();

            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = RuntimeLineMaterial.Shared;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.enabled = false;

            _block = new MaterialPropertyBlock();
        }
    }
}
