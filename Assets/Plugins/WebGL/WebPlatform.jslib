mergeInto(LibraryManager.library, {
  // **"这是不是触摸设备"的权威判定**（交给浏览器回答）。
  //
  // 为什么需要它：Unity 在 WebGL 下的 `Application.isMobilePlatform` 与 `Input.touchSupported`
  // **都可能是 false**（实测 iPhone Safari 上两者皆 false）⇒ 所有"移动端专属"的界面分支永远不生效
  // （蓄力区、瞄准精调、图标放大全部消失，右上角 POWER 面板反而出现了）。
  // 浏览器侧的 `ontouchstart` / `maxTouchPoints` 才是可靠信号。
  WebPlatform_IsTouch: function () {
    try {
      return (('ontouchstart' in window) || (navigator.maxTouchPoints > 0)) ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },
});
