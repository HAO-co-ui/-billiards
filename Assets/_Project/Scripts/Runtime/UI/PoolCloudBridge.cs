using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **云服务桥（只对 WebGL 生效）**：把一局练习成绩交给页面里的 `window.PoolCloud`，
    /// 由它写入云数据库并刷新排行榜。
    ///
    /// 边界（非常重要）：
    /// · **只在 `UNITY_WEBGL && !UNITY_EDITOR` 编译**；桌面 / 编辑器里本类是**空实现** ⇒ 单机玩法完全不受影响。
    /// · **失败绝不打断游戏**：页面没接云服务（本地跑、离线、SDK 没加载）时 `window.PoolCloud` 不存在，
    ///   jslib 侧会静默返回；这里也不做任何等待或重试。
    /// · **不发送任何身份信息**：排行榜是公开数据，玩家名由页面侧统一给（不读本地昵称，避免泄露）。
    /// </summary>
    public static class PoolCloudBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void PoolCloud_SubmitScore(int shots, int potted, int fouls, int cleared);
#endif

        /// <summary>上报一局练习成绩（**幂等语义由页面侧决定**：每次结算只调一次）。</summary>
        public static void SubmitPracticeScore(int shots, int potted, int fouls, bool tableCleared)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                PoolCloud_SubmitScore(shots, potted, fouls, tableCleared ? 1 : 0);
            }
            catch (System.Exception e)
            {
                // 桥不可用时只留日志，不影响结算卡与后续操作。
                Debug.LogWarning("[PoolCloud] 成绩上报失败（不影响本局）：" + e.Message);
            }
#endif
        }
    }
}
