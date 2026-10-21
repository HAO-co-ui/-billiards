namespace Billiards.UI
{
    /// <summary>
    /// **平台能力判定（唯一入口）**：到底该按"触摸设备"还是"桌面"来布局。
    ///
    /// 背景（实机踩过）：Unity 在 WebGL 里 `Application.isMobilePlatform` 与 `Input.touchSupported`
    /// 在 iPhone Safari 上**都返回 false**。于是所有"移动端才建"的控件（左半屏蓄力、瞄准精调、
    /// 图标放大）全部不生效，而"PC 才显示"的 POWER 面板反而露出来 —— 用户看到的就是"手机端改完全没了"。
    ///
    /// 判定顺序：
    /// · **WebGL**：问浏览器（`WebPlatform_IsTouch`，看 `ontouchstart` / `maxTouchPoints`）；
    /// · **其它平台**：沿用 Unity 自己的判断。
    ///
    /// 只缓存一次（结果在运行期不会变），避免每帧跨 JS 边界调用。
    /// </summary>
    public static class WebPlatform
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int WebPlatform_IsTouch();
#endif

        private static int _isTouch = -1;

        /// <summary>是否按触摸设备处理界面。</summary>
        public static bool IsTouch
        {
            get
            {
                if (_isTouch < 0)
                {
                    _isTouch = Resolve();
                }

                return _isTouch == 1;
            }
        }

        private static int Resolve()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return WebPlatform_IsTouch() == 1 ? 1 : 0;
            }
            catch (System.Exception)
            {
                // 桥不可用时按桌面处理（宁可少几个移动端控件，也不要抛异常打断启动）。
                return 0;
            }
#else
            return (UnityEngine.Application.isMobilePlatform || UnityEngine.Input.touchSupported) ? 1 : 0;
#endif
        }
    }
}
