mergeInto(LibraryManager.library, {
  // **Unity → 浏览器**的唯一桥：把一局练习成绩交给页面里的 `window.PoolCloud`。
  //
  // 为什么必须走 jslib：WebGL 里 C# 拿不到 DOM/SDK，只能用 `DllImport("__Internal")` 导出成
  // JS 侧可调的函数。参数用 4 个 int（避免字符串编码问题），由 JS 侧组装对象。
  //
  // ⚠ 页面没接云服务时（本地/未发布）`window.PoolCloud` 不存在 ⇒ 静默返回，不影响单机玩法。
  PoolCloud_SubmitScore: function (shots, potted, fouls, cleared) {
    try {
      if (typeof window !== 'undefined' && window.PoolCloud && window.PoolCloud.submitScore) {
        window.PoolCloud.submitScore(shots | 0, potted | 0, fouls | 0, cleared | 0);
      }
    } catch (e) {
      // 云服务不可用绝不打断游戏；错误留在控制台供排查。
      if (typeof console !== 'undefined') console.warn('[PoolCloud] submit failed:', e);
    }
  },
});
