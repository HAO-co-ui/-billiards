# 35-A Day 12 VFX 重建 —— 实施规格

> 目标：按排期「Day 12 — VFX 重建（7 h）」重建 4 类程序化特效（击杆 / 碰球 / 撞库 / 入袋），
> 补上**入袋反馈强化**（下沉 + 缩放 + 高亮——"进球必须有回报"），全部池化、可开关、有性能上限。
> 判据编号：**`T92`（动作曲线全式）/ `T93`（接触推导）/ `T94`（限频门）**（`T91` 已占）。

---

## 1. 范围（照抄排期，不得放宽）

| # | 任务 | 工时 |
| --- | --- | --- |
| 1 | 按 `05_VFX/Code_Reference/pool-vfx.js` 重建 **4 类**：击杆、碰球、撞库、入袋 | 3.5 h |
| 2 | 入袋反馈强化：下沉 + 缩放 + 短暂高亮（**进球必须"有回报"**） | 2 h |
| 3 | 特效开关 + 池化（复用，不 Instantiate 抖动） | 1 h |
| 4 | 性能上限：特效不遮挡球桌、粒子数封顶 | 0.5 h |

**说明**（素材包原话）：包内**没有**序列帧或 Unity Prefab，只有程序化几何特效源码——**按意图重建，不追求逐帧复刻**。
**验收**：**进球瞬间有明确视觉回报；关掉特效不影响玩法。**

## 2. 参考实现事实（`pool-vfx.js`，已通读 221 行）

- **48 槽对象池** + **3 种共享几何**（ring / arc / spark）；槽满**丢弃**并计数（`dropped`）。
- 环贴台面（`y = 0.009`）平放；透明度按 `(1−p)^1.7` **渐隐**；半径按 `radius × (1 + p × expansion)` **扩张**。
- 颜色：`chalk #f8f5e7`（击杆/碰球）/ `brass #f3d999`（入袋、胜利）/ `rail #ccdacb`（撞库）/ `foul #b96873`（犯规）。
- **6 类参数表（本单取前 4）**：

| 类 | 形状 | radius | duration | expansion | sparks | 颜色 | 限频 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 击杆 strike（弧） | arc | 0.012 × (0.7 + 0.6 × power) | 0.12 | 0.4 | 3 | chalk | —（跟随出杆） |
| 碰球 hit（环） | ring | 0.011 | 0.11 | 0.5 | 2 | chalk | ≥ 0.045 s |
| 撞库 rail（弧） | arc | 0.014 | 0.15 | 0.2 | 0 | rail | ≥ 0.07 s |
| 入袋 pocket（环） | ring | 0.023（黑八 0.029） | 0.29（黑八 0.45） | 0.5 | 3 | brass | —（跟随落袋） |
| 犯规 foul（环） | ring | r+0.008 | 0.38 | 0.2 | 0 | foul | — |
| 结算 result（环） | ring | 0.04 | 0.55 | 0.35 | 0 | brass/foul | — |

- **触发与位置**（参考实现的"状态推导"口径——**本单沿用**）：
  - `hit`：遍历球对，找 `|dist − 2r| ≤ 0.007` 且 `speed = |v|a + |v|b > 0.1` 的**质量最高**对
    （`quality = speed / (0.002 + |dist − 2r|)`）⇒ 特效在两球中点，朝向 = 连心线。
  - `rail`：找 `|distToRail − r| ≤ 0.01` 且 `speed > 0.1` 的最高质量球 ⇒ 特效在最近库段垂足。
  - `pocket`：袋口（`mouth` 中点向袋内偏 0.013）。
  - `strike`：白球**后侧** 0.85 r 处（`cue − aimDir × 0.85 r`），朝向 = 出射反方向。
- **可访问性**：`reducedMotion` ⇒ 无扩张、透明度 ×0.55、无火花。**独立 VFX 开关**（关闭即 `reset()`）。

## 3. 实现设计（Unity 侧；**零内核改动**）

### 3.1 触发源（全部复用既有只读出口）

| 特效 | 触发门（计数增量） | 出处 |
| --- | --- | --- |
| strike | `PhysicsTestDrive.LaunchCount` | Day 11 新增 |
| hit | `PoolSimulator.BallBallContactCount` | 既有 |
| rail | `PoolSimulator.TotalContactCount − BallBallContactCount` | 既有 |
| pocket | `PoolSimulator.PocketProximityEvents` | 既有 |

> **门只决定"本帧要不要找位置"；位置一律由状态推导**（同参考实现）——两类信息互补，
> **不消费接触事件流**（那是音频的 `DrainContactEvents` 单消费者通道，避免争抢；如实登记该架构选择）。

### 3.2 新组件 `PoolVfxView`（`Runtime/Physics/`，表现层）

- **池化**：启动时**一次性**预建槽位（默认 16 槽；每槽 = 环 LineRenderer + 3 条火花 LineRenderer，
  **运行期零 Instantiate**）；槽满**丢弃**并计数（同参考实现）。
- 绘制顶点一律走 **`AimAssistView.MarkerWorldPoint`**（显示层单点——X/Z 双取反 + 球心高度）与
  `CueStickController` 同款世界坐标口径；**不引入第二处坐标转换**。
- 环 = 32 段 LineRenderer loop；弧 = 16 段（120°）；火花 = 2 点短线（面片以线近似——**本单以线代面，登记**）。
- 每帧：读计数增量（存储上次值）→ 触发时**推导位置**（3.3 纯函数）→ 分配槽位（写 age=0、参数）
  → 统一 `Advance(dt)` 推进全部活动槽（写顶点/透明度/宽度）。
- **开关**：`vfxEnabled`（默认 true，设置页预留——单字段单读点；关闭 ⇒ 全部隐藏 + 触发停止）。
- **性能上限（排期 4）**：槽数 16 封顶（= 最多 16×4 = 64 条线的粒子上限）；环/弧/线宽 0.004 规范米；
  顶点高度取球心（不遮挡球号——深度测试开启；线在球心高度以下不遮球面标签——沿用参考"depthTest: true"语义）。

### 3.3 纯函数（判据直接驱动）

```csharp
// ① 动作曲线（T92）
public static void VfxPose(float progress, float radius, float expansion, float opacity,
                           out float scale, out float alpha);
//    scale = radius × (1 + p × expansion)；alpha = opacity × (1 − p)^1.7（p clamps 到 [0,1]）

public static void PocketSinkPose(float progress, out float yOffset, out float scale);
//    yOffset = −0.08 × p；scale = 1 − p（线性；p ∈ [0,1]）

// ② 接触推导（T93）——输入为"纯数组快照"，不依赖 PoolSimulator 实例
public static bool FindHitContact(IReadOnlyList<Vector2> pos, IReadOnlyList<Vector2> vel,
                                  float ballRadius, out Vector2 point, out float angleDeg, out float speed);
//    |dist − 2r| ≤ 0.007 且 speed > 0.1 的最高 quality（quality = speed / (0.002 + |dist − 2r|)）

public static bool FindRailContact(IReadOnlyList<Vector2> pos, IReadOnlyList<Vector2> vel,
                                   IReadOnlyList<Vector2> segA, IReadOnlyList<Vector2> segB,
                                   float ballRadius, out Vector2 point, out float angleDeg, out float speed);
//    |distToRail − r| ≤ 0.01 且 speed > 0.1 的最高 quality

// ③ 限频门（T94）
public sealed class IntervalGate
{
    public IntervalGate(float minIntervalSeconds);
    public bool TryPass(float nowSeconds);   // 首次必过；间隔内拒；超间隔过
    public void Reset();
}
```

### 3.4 入袋反馈强化（排期任务 2；`PhysicsTestDrive.SyncBallVisual` 扩展）

- 每球记录"离台时刻"（惰性数组 `_pocketSinkStart[]`，同 `_lastVisualPositions` 模式）。
- 球离台（`!IsBallInPlay(i)`）：
  - 首次检测 ⇒ 记 `start = Time.time`——嗯，**不读 `Time`**？——⚠ 本类是**表现层驱动器**（`Update` 已读 `Input`/`Time.fixedDeltaTime`），
    **确定性纪律针对内核**；表现层允许读时钟（如实登记）——用 `Time.time`。
  - 动画（0.3 s）：`PocketSinkPose(p)` ⇒ `localPosition.y += yOffset`、`localScale = 原缩放 × scale`；`p ≥ 1` ⇒ `SetActive(false)`。
- 球回到场上（重摆 / `LoadBalls`）⇒ **复位**：`SetActive(true)`、恢复缩放、清"离台时刻"。
- **"短暂高亮"的落地形式**：袋口 **双环**（主环 = 参考实现参数；叠加一个**更亮更短**的内环——
  `opacity 0.9 / duration 0.18 / expansion 0.2`——即"高亮"的可判定实现）。**球体材质级高亮不做**
  （需正式材质/emission——登记为"待美术接入"）。

### 3.5 场景接线（`GreyboxSceneSetup`）

- 新宿主 `PoolVfx`（常量 `PoolVfxObjectName`），接线 `drive` + `playfieldRoot`（显示层根）；重建场景。

## 4. 判据

### `T92` 动作曲线全式
- `VfxPose(0, r, e, o)` ⇒ `scale == r`、`alpha == o`（逐位级，容差 1e-6）；
- `VfxPose(1, …)` ⇒ `alpha == 0`；
- `VfxPose(0.5, r, e, o)` ⇒ `scale == r × (1 + 0.5e)`、`alpha == o × 0.5^1.7`（闭式，1e-6）；
- `PocketSinkPose(0/0.5/1)` ⇒ `(0, 1) / (−0.04, 0.5) / (−0.08, 0)`（线性闭式）。
- **注入负对照**：alpha 改线性 `(1−p)` ⇒ 0.5 段红（0.308 vs 0.5）。

### `T93` 接触推导
- `FindHitContact`：① 两球相距 `2r + 0.001` 且速度大 ⇒ 找到（点在两球中点、角度 = 连心线）；② 相距 1 m ⇒ 未找到；
  ③ 距离贴但两球静止 ⇒ 未找到（speed 门）；④ 三组对中选 quality 最高（构造明显最优对）。
- `FindRailContact`：① 球距库段 `r + 0.002` 且有速度 ⇒ 找到（点在垂足）；② 离库 0.2 m ⇒ 未找到。
- **注入负对照**：`FindHitContact` 去掉 `speed > 0.1` 门 ⇒ 静止用例红。

### `T94` 限频门
- 首过 ⇒ true；`now + 0.02`（< min）⇒ false；`now + min + ε` ⇒ true；`Reset` 后首过再 true。

## 5. 验证姿势与边界（照抄工程标准 §8）

- 全量 EditMode 应 **180 → 183**（+`T92`/`T93`/`T94`）；四验收器 25/33/22/7 复跑；场景重建。
- 注入的临时改动**逐字节还原**（sha1 核对）。
- **盲带（如实）**：
  - 特效**观感**（好不好看/回报感）非判据可测——排期验收由用户实机；
  - `SyncBallVisual` 的下沉**接线**不被 EditMode 覆盖（表现层盲带）；`PocketSinkPose` 纯函数有判据；
  - 批处理 `-nographics` 无渲染——判据只驱动纯逻辑/状态；LineRenderer 顶点无判据（同 AimAssistView 的既有盲带口径）。
  - **架构选择登记**：VFX **不消费** `DrainContactEvents`（音频单消费者通道）——门 + 状态推导；
    若将来两者都要事件流，应改为"单一分发者 fan-out"（超出本单）。
