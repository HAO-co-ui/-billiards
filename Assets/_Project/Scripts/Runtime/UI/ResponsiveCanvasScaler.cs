using UnityEngine;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **画布自适应**（用户裁定：真自适应，PC / 手机 / 平板 / 竖屏都要能用）：
    /// 按屏幕宽高比**动态切换 `CanvasScaler.matchWidthOrHeight`**。
    ///
    /// 规则与理由：
    /// · **横屏（aspect ≥ 1）** ⇒ `match = 1`（按高度缩放）：设计高度 1080 恒定，宽度多出来的部分给球台
    ///   —— 这正是 HUD 锚点假设的口径（它按高度分带：顶栏 / 记分板 / 底栏）。
    /// · **竖屏（aspect &lt; 1）** ⇒ `match = 0`（按宽度缩放）：竖屏高度远大于宽度，按高度缩放会让
    ///   菜单横向溢出到屏幕外（实测：844×390 横屏时按钮被裁到左屏外）⇒ 改为以宽度为准，保证横向完整。
    ///
    /// ⚠ **只改 match，不改 `referenceResolution`**：参考分辨率是"设计稿像素"，两端共用同一套坐标；
    ///   换参考分辨率会让 `HomeLayout.Px()` 之类的设计稿常量全部失效。
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    [DisallowMultipleComponent]
    public sealed class ResponsiveCanvasScaler : MonoBehaviour
    {
        private CanvasScaler _scaler;
        private float _lastAspect = -1f;

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            Apply();
        }

        private void Update()
        {
            // 只在宽高比变化时写（避免每帧写字段造成脏标记）。
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            if (!Mathf.Approximately(aspect, _lastAspect))
            {
                Apply();
            }
        }

        /// <summary>按当前屏幕宽高比应用（幂等；判据可直接调）。</summary>
        public void Apply()
        {
            if (_scaler == null)
            {
                _scaler = GetComponent<CanvasScaler>();
            }

            if (_scaler == null)
            {
                return;
            }

            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            _lastAspect = aspect;
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = aspect >= 1f ? 1f : 0f;
        }
    }
}
