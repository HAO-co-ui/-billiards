using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **左半屏蓄力区（移动端）**：一块透明、可接收射线的整块区域，手指按下即蓄力、上下拖动改力度、松手出杆。
    ///
    /// 为什么用 uGUI 事件而不是读 `Input`：
    /// · 触摸在 WebGL 移动浏览器上**不保证**映射成 `Input.mousePosition` / `GetMouseButton`，
    ///   实测"左手拉不了"就是这条路的失效（`Input.touchSupported` 也返回 false）；
    /// · uGUI 的 `OnPointerDown/Drag/Up` 走的是 **EventSystem 的触摸管线**，只要 Canvas 有 `GraphicRaycaster`
    ///   （本画布有）且图元开了 `raycastTarget`，就一定收得到，**与平台无关**。
    ///
    /// 边界：力度阈值、出杆路径全部复用 <see cref="Billiards.Physics.PointerPowerController"/>；
    /// 本组件只负责"把手指位置翻译成 0..1"。
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class ChargeArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>真实蓄力控制器（由 HUD 注入）。</summary>
        public Billiards.Physics.PointerPowerController power;

        /// <summary>力度填充条（可选；没有就不画）。</summary>
        public RectTransform fill;

        private RectTransform _rect;
        private float _startY;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void OnDisable()
        {
            // 区域被隐藏（换局/结算）时把"正在蓄力"信号清掉，避免瞄准永久让位。
            if (power != null && power.IsCharging)
            {
                power.EndLeverCharge(0f);
            }
        }

        private float PowerFrom(Vector2 screenPoint)
        {
            if (_rect == null)
            {
                return 0f;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPoint, null, out Vector2 local))
            {
                return 0f;
            }

            // **相对按下点**的竖向位移：抬起 / 下拉都按"向上为正"换算，移动屏高 40% 视为满力。
            float range = Mathf.Max(1f, _rect.rect.height * 0.8f);
            return Mathf.Clamp01((local.y - _startY) / range);
        }

        private void Apply(float value)
        {
            if (fill == null)
            {
                return;
            }

            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(1f, Mathf.Clamp01(value));
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (power == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, eventData.position, null,
                    out Vector2 local))
            {
                return;
            }

            _startY = local.y;
            power.BeginLeverCharge();
            Apply(0f);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (power == null)
            {
                return;
            }

            float v = PowerFrom(eventData.position);
            power.SetLeverPower(v);
            Apply(v);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (power == null)
            {
                return;
            }

            float v = PowerFrom(eventData.position);
            power.EndLeverCharge(v);
            Apply(0f);
        }
    }
}
