# 35-A 袋角归属裁定（用户选 A）—— 实施规格

> **来源**：用户实机反馈"停在袋口的那颗球，被撞后碰网框边缘**缺少弹力、像贴在上面一样**"，
> 以及"这个角度发球为什么就发射不了了呢"。**用户已裁定选 A**（见 §1）。
> **性质**：**内核规则改动**（`PoolSimulator`），触及袋口族不变量与**既有逐位基线** ⇒ 必须一次做完。

---

## 1. 裁定（用户 2026-10-15 选定 A）

给"擦袋角越界"一个**归属**，而不是**静默冻结**：

| 条件 | 现状 | 裁定后 |
| --- | --- | --- |
| `s > 0` 且 `ρ ≤ captureRadius` | 入袋（冻结离台） | **不变** |
| `s > 0` 且 `captureRadius < ρ ≤ openingRadius` | **冻结**（速度置零、离台、**无反弹**） | **弹回**：推回喉平面内侧 + 法向反射 × 恢复系数，**不冻结、不离台** |
| `s > 0` 且 `ρ > openingRadius` | **冻结**（同上） | **不算袋口事件**：不冻结、不计数，交给正常库边碰撞处理 |
| **`s ≤ 0` 且球心落在袋口开口区内，且整盘静止** | **什么都不发生**（球永久挂在袋口） | **判入袋**（见 §1.1） |

**用户原话**：「碰网框边缘时感觉不真实，好像缺少些弹力，球碰到碰网框边缘像贴在上面一样」
⇒ 第 2 行就是病根；第 3 行是同一个 bug 的另一种表现（球在库边上也被"粘住"）。

### 1.1 第 4 行（**第二批反馈补入 —— 本规格第一版漏了这一支**）

> 用户第二次贴图："**这样挂着视角看着不太合理**" —— 一颗球**可见地停在袋口里**（袋网之上）。

**这一支为什么第一版没盖到**：本规格前三行都以 `s > 0`（球心越过袋喉平面）为前提；
而用户截图中那颗球**球心尚未越过喉平面** ⇒ 它**仍是"在台上的球"**，
捕获契约（美术包 `captureNote`：球心穿过袋喉朝网方向后才捕获）**根本不认为有事发生**
⇒ 内核**合法地什么都不做**，球就**永久挂在袋口**。

**裁定**：整盘静止时，若某球球心落在**袋口开口区**内，判它**入袋**（现实里袋口边缘的球会掉下去）。
- **开口区的形式化**（沿用 §2.1 的逐袋几何）：球心到**袋喉中心** `throatCenter` 的距离
  ≤ `openingRadius`，**且**球心在**袋喉外侧**（`s ≤ 0`，即尚未越喉但已在开口内）。
  用 `openingRadius` 而非 `mouthWidth/2`：前者是美术包给的**开口半径**单值，逐袋已存在，语义直接。
- **触发时机**：只在**整盘静止**（`TableAtRest` 达成的那个子步）判 —— **不得**每子步判，
  否则会与 §3.1 第 2/3 行的弹回逻辑抢判定（正在飞入袋的球会半路被"静止"误伤）。
- 判定后走**与正常入袋完全相同的路径**（`CapturedBallCount++`、`_capturedPocket[index] = p`、
  速度置零、`_frozen = true`）⇒ 表现层**自动**获得下沉动画与袋口特效，不需要任何新表现代码。
- ⚠ **`PocketProximityEvents` 是否 +1**：本支**不是**越喉事件 ⇒ **不加**（保持
  `PocketProximityEvents == CapturedBallCount + PocketJawCrossingCount` 的既有恒等式**不被破坏**）。
  但那样 `CapturedBallCount` 会**大于**恒等式右边 ⇒ **该恒等式必须改为**：
  `PocketProximityEvents + AtRestMouthDrops == CapturedBallCount + PocketJawCrossingCount`，
  并新增一个独立计数 `AtRestMouthDrops`；**恒等式改动本身要写进判据**（`T108` 扩面）。

---

## 2. 现状核实（本单实测，非推断）

`PoolSimulator.CheckPocketProximity`（袋口判定单点实现）现行不变量，注释原文：

> **不变量**：只要 `s > 0`（**不论是否落袋**）⇒ 该球**离台**（速度置零、停止积分）。

```csharp
PocketProximityEvents++;
if (rho <= _pocketCaptureRadius[p]) { CapturedBallCount++; _capturedPocket[index] = p; }
else { PocketJawCrossingCount++; }
...
_balls[index].velocity = Vector2.zero;   // ← 无反弹
_frozen[index] = true;                   // ← 永远离开台面
```

**连带症状（已由用户实机观察确认）**：白球若在此处冻结 ⇒ 预测器跳过离台球 ⇒ **瞄准线一条都画不出来**
（用户："这个角度发球**没有瞄准线**"）；而 `Launch()` 走"白球离台 ⇒ 放回开球点"那支，
开球点正是球堆位置 ⇒ 视觉上"**松手球不动**"。

### 2.1 可用的几何量（`TableConfig.Pocket`，逐袋真源）

`mouthA/B`、`throatA/B`、`throatCenter`、`throatNormal`（指向网内）、`mouthWidth`、`throatWidth`、
**`openingRadius`（开口半径）**、`captureRadius`（已扣球半径与 2 mm 余量）。
内核当前**只缓存了** `_pocketThroatCenter` / `_pocketThroatNormal` / `_pocketCaptureRadius`
⇒ 本次**必须新增** `_pocketOpeningRadius` 缓存（在构造袋口数组处一并填）。

---

## 3. 实现设计

### 3.1 `PoolSimulator`（唯一改动点）

1. 新增 `_pocketOpeningRadius`（`float[]`），与另三个袋口缓存同址初始化。
2. `CheckPocketProximity` 的分类改三支：
   - `ρ ≤ captureRadius` ⇒ 入袋 → 计 `CapturedBallCount` + `_capturedPocket`，**冻结**（不变）。
   - `captureRadius < ρ ≤ openingRadius` ⇒ 计 `PocketJawCrossingCount`（语义从"越界冻结"改为"**擦袋角弹回**"，注释要改），
     **不冻结**，执行**弹回**：
     ```
     // 把球心推回喉平面内侧：s 归零再多留 1 mm，避免下一子步立刻又判 s > 0 形成抖动死循环。
     C' = C − tn × (s + 0.001)
     v  = v − 2 × dot(v, tn) × tn        // 法向反射
     v *= cushionRestitution             // 与库边同一恢复系数（不得另立一个数）
     ```
   - `ρ > openingRadius` ⇒ **不计任何袋口事件**（`PocketProximityEvents` 也不加）、不冻结、不反弹，
     让它按正常库边段去解算。
3. `RecordContactEvent(ContactKind.Pocket, ...)` 的**位置与条件**要随之明确：
   仅在前两支记录（真实袋口事件）；第三支不记。
4. ⚠ **`PocketProximityEvents == CapturedBallCount + PocketJawCrossingCount` 这条恒等式必须保持**
   （它是既有判据验证过的结论）—— 第三支不进任何一个计数，故恒等式**仍成立**。
5. ⚠ **不得**用"一变量 = 另两者之和"来实现计数（既有注释明令禁止）。

### 3.2 连带修正（同一根因的其他表现）

- `PhysicsTestDrive.Launch()` 的"白球离台 ⇒ 放回 `footSpot`"分支：`footSpot` 是**球堆位置**
  ⇒ 放回后白球会与球堆重叠。**本单只登记**，修法（改放"开球线附近空位"或改球堆位置）**不在本单范围**。
- 用户的"没瞄准线"由 3.1 自然解决（白球不再冻结）。**不另加隐藏逻辑**。

---

## 4. 判据

### `T106` 擦袋角**弹回**（替换"越界冻结"的语义）

- (a) 构造一球：朝某袋的**喉平面**运动，面内偏移 `ρ` 取 `(captureRadius, openingRadius]` 中值 ⇒ 步进到越界后：
  - `IsBallInPlay(index) == true`（**不冻结**）；
  - `CapturedBallCount == 0` 且 `PocketJawCrossingCount == 1`；
  - **法向分量反向**：`dot(v_after, tn) < 0` 且 `|dot(v_after, tn)| ≈ cushionRestitution × |dot(v_before, tn)|`（容差 1e-4）；
  - 球心回到喉平面**内侧**（`s(C') < 0`）。
- (b) 同一场景在**改动前**会冻结 ⇒ 本判据即为回归守卫。

### `T107` 远离袋口开口的越界**不算袋口事件**

- 取 `ρ > openingRadius`（仍在 `s > 0` 的半平面内、但在开口带外）⇒：
  `PocketProximityEvents` 不增、`IsBallInPlay == true`、速度**未被置零**（由库边解算负责）。

### `T108` 恒等式与既有捕获不变量（**扩面**：加入 §1.1 的 `AtRestMouthDrops`）

- `PocketProximityEvents + AtRestMouthDrops == CapturedBallCount + PocketJawCrossingCount`
  在**四类**场景各成立（正中入袋 / 擦角弹回 / 开口带外 / **静止在开口内的球被补判入袋**）。
- **(新) 静止补判**：一球球心置于某袋 `openingRadius` 内、`s ≤ 0`，推进到整盘静止 ⇒
  `AtRestMouthDrops == 1`、`IsBallInPlay == false`、`CapturedPocketIndex` 指向该袋；
  **且飞行中的球不得被误伤**（同一场景把该球给一个朝袋的速度，静止前不得触发本支）。
- 正中入袋路径**行为逐位不变**（`ρ ≈ 0`）：`CapturedBallCount == 1` 且冻结。

**注入负对照（每档必须实测红）**：
① 把"擦角弹回"分支改回冻结 ⇒ `T106 (a)` 红；
② 把第三支也计入 `PocketJawCrossingCount` ⇒ `T107` 红；
③ 反弹时漏乘 `cushionRestitution` ⇒ `T106 (a)` 的速度分量断言红。

### 基线重钉（**必做，不得跳过**）

改动会改变任何"球擦袋角"路径的终态 ⇒ **既有逐位基线必须重钉**，至少核对并更新：
- `T72b`（`T32b total` / `T37 bb` / 16 球终态逐位快照）；
- `T37`（开球散球基线上界）；
- 袋口族 `T27`–`T36` 中凡涉及"越界冻结"语义的断言 —— **逐条判定是"行为真变了"还是"断言口径变了"**，
  两者处置不同：前者重钉数值，后者改口径并写明理由。

---

## 4b. ⚠ **第一次实施记录（已按纪律回滚，不得直接沿用）**

**结论：本规格的四支分类已实现且编译通过（0 错误），但实测出现"接触风暴"，已整文件回滚。**
回滚后 `git diff` 为空、内容与 HEAD 逐字节一致（仅行尾差异）。

### 爆炸半径（**重要，直接可用**）

全量 EditMode 由 `194/194` 变为 **`189/194`（5 条红）** —— **比预期小得多**：

| 失败项 | 原文 | 定性 |
| --- | --- | --- |
| `T72b_BallBallContactDiagnostic_IsSinglePointAndInert` | `T37 CapturedBallCount 基线 0 → 实测 2`；`TotalContactCount 6746 → 6748`；`越喉/擦角 2/2 → 4/2`；三球终态位移 ~1e-3 m | **行为真变了**（应重钉基线） |
| `T35_CaptureClassification_EqualityHolds` | `应恰有 1 次窗口越界，实际 0` | 该场景的越界**改走它支**（口径/行为双变） |
| `T39_IndependentCrossingEnumeration_MatchesCounters` | `独立枚举窗口数须等于 PocketJawCrossingCount：应 1，实际 0` | 同上，与 `T35` 同源 |
| **`T34_BreakSample_DecidableCriteria`** | **`总接触数超上界 64，实际 1135`** | **🔴 病态 —— 见下** |
| `T55_DecelerationAt_IsTheOnlyReadPoint_AndNoCallSiteBypassesIt` | （源码级调用点计数守） | 新增的 `BounceOffPocketJaw` **调用了 `DecelerationAt`** ⇒ 调用点计数变了；需判定该新调用点是否合规 |

### 🔴 关键诊断：`T34` 的 1135 说明**弹回实现制造了接触风暴**

`T37` 的增量只有 `+2` 接触（正常），但 `T34` 的破球样例冲到 **1135**（上界 64，超 17 倍）
⇒ **在特定几何下每子步都在反复触发**。**根因未查明**，下一位实施者**先查这个再谈别的**。

**已排除**：不是 `s > 0` 的推回量不够（推回 `penetration + 1 mm` 后 `s = −1 mm`，同子步不会立刻复判）。
**最可疑（待验）**：① 球同时落在**两个袋**（角袋与中袋）的 `openingRadius` 内 ⇒ 被两袋轮流弹；
② `cushionRestitution` 后的反射速度很小、切向分量把球又带回喉平面内 ⇒ 抖动；
③ 第三支改成 `continue` 后，原本"冻结即止"的球**继续在库边段间反复解算**。

**下一次的正确姿势**：**先把"弹回"实现成"一次性、带去抖"**（例如每球一个"刚弹回"冷却计数/标志位，
或要求反射后的法向速度 ≥ 某阈值才弹、否则按入袋处理），**再**跑 `T34` 单条确认接触数回到 64 以内 ——
**不要**先改基线。

### 其他实测事实

- 四支实现中，**第 1 支（正中入袋）未改动** ⇒ 只要不触发第 2/3 支，逐位基线不变（`T37` 只差 `+2` 即为证）。
- `AtRestMouthDrops` 与恒等式改动**未观察到任何失败** ⇒ 第 4 支的接入点（`UpdateTermination` 的
  `TableAtRest` 上升沿）**未破坏**任何既有判据。
- ⚠ `PoolSimulator.cs` 被 `git checkout --` 回滚后**行尾由 CRLF 变 LF** ⇒ 该文件的 sha1 会变，
  **比对要用 `git diff` 而不是 sha1**（内容一致的判据是 `git diff` 为空）。

## 5. 盲带（如实）
- 推回距离 `0.001 m` 是**防抖动**的经验值；判据只锁"回到内侧"，不锁该值的观感。
- `openingRadius` 是**美术包给的口袋开口半径**；用它划"开口带"是**本单的工程解读**，不是美术包的明文规则
  ⇒ 该解读须登记，后续若美术/策划给出更明确的开口判据，替换此值即可（单点）。
