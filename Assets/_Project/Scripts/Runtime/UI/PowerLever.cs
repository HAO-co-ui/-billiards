using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **左侧竖向蓄力杆（移动端专用）**：手指按住杆上任意位置上下拖 ⇒ 力度 0..1，松手出杆。
    ///
    /// 为什么需要它：手机上"按住白球往后拖"虽然能用，但**手指会挡住白球与瞄准线**，
    /// 精细控制反而更难；一根独立的竖向杆把"瞄准"与"力度"彻底分开（瞄准在台面，力度在左侧）。
    ///
    /// 边界：
    /// · 只在**触摸设备**创建（PC 保留右键蓄力，不新增视觉噪音）；
    /// · 力度阈值、出杆路径**全部复用** <see cref="Billiards.Physics.PointerPowerController"/> 的既有逻辑
    ///   （`launchThreshold` / 球杆前推动画 / 直接出杆兜底），本组件**不自己调 `Launch()`**；
    /// · 拖动期间置 `AnyChargingWithPointer` ⇒ 瞄准组件让位，避免同一根手指既改力度又改方向。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PowerLever : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>力度条（竖向填充，锚点在下边缘）。</summary>
        public RectTransform fill;

        /// <summary>真实驱动器（由 HUD 注入）。</summary>
        public Billiards.Physics.PointerPowerController power;

        private RectTransform _track;

        private void Awake()
        {
            _track = (RectTransform)transform;
        }

        /// <summary>把屏幕点换算成 0..1 的力度（**以轨道下边缘为 0、上边缘为 1**）。</summary>
        private float PowerFromScreen(Vector2 screenPoint)
        {
            if (_track == null)
            {
                return 0f;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _track, screenPoint, null, out Vector2 local))
            {
                return 0f;
            }

            Rect r = _track.rect;
            float t = Mathf.InverseLerp(r.yMin, r.yMax, local.y);
            return Mathf.Clamp01(t);
        }

        private void Apply(float value)
        {
            if (fill != null)
            {
                fill.anchorMin = new Vector2(0f, 0f);
                fill.anchorMax = new Vector2(1f, Mathf.Clamp01(value));
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (power == null)
            {
                return;
            }

            power.BeginLeverCharge();
            float v = PowerFromScreen(eventData.position);
            power.SetLeverPower(v);
            Apply(v);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (power == null)
            {
                return;
            }

            float v = PowerFromScreen(eventData.position);
            power.SetLeverPower(v);
            Apply(v);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (power == null)
            {
                return;
            }

            float v = PowerFromScreen(eventData.position);
            power.EndLeverCharge(v);
            Apply(0f);
        }
    }
}
