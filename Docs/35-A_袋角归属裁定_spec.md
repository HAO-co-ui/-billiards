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

## 4c. 第二次实施记录（"弹回"动作四种候选的实测）—— ✅ **已裁定 A 并实施完成**

### 结论先行

**§3.1 第 2 档的"推回喉平面内侧 1 mm + 法向反射"在本工程的袋口几何下不可实现。** 实测它只能在
① "球静默飞出台面"、② "接触风暴"、③ "球塞进袋角环内反复弹撞" 三者之间三选一；
**只有改变该带的归属（口径）才能同时消除三种失效** ⇒ 该带的口径属**玩法裁定**，须用户拍板（见 §4d）。

### 关键几何事实（本次实测，非推断）

1. **袋喉平面之后没有任何碰撞几何** —— 捕获契约完全由 `CheckPocketProximity` 兑现。
   ⇒ 历史"越喉即离台"的**唯一**作用是保证"球不得静默离台"；它不是冗余，删了会飞台（实测）。
2. **`captureRadius = openingRadius − BallRadius − 2 mm`**（6 袋逐袋成立）。
   真实值：`openingRadius ≈ 0.061317`、`captureRadius ≈ 0.030742`（`Assets/_Project/Data/TableConfig.asset`）。
   ⇒ 擦袋角带 `(captureRadius, openingRadius]` **恰好等于"球体与袋角侧壁重叠"**；
   而"球心落在喉口宽度内"意味着**球心已经在洞口上方**（现实里必然落下）。
3. 袋角侧壁是**一串首尾相接的短段**（单段长 8–14 mm；全表 82 段 = 6 直库 + 76 侧壁）⇒
   高速球的单子步位移（8 m/s ⇒ 22.2 mm）**比单段更长** ⇒ 存在**隧穿**（这是 `T31b`/`T35` 那类
   "越喉但横向偏移在外"的场景得以出现的机制）。

### 四种候选动作的实测（`T34` 散球样例：4 球 × 240 步；上界 `TotalContactCount ≤ 64`、`bb ≤ 64`）

| # | 第 2 档动作 | 实测 | 判定 |
| --- | --- | --- | --- |
| ① | **推回 1 mm + 法向反射**（§3.1 原文） | 球被塞进**袋角 V 形槽**：停摆点与两段侧壁的**公共端点恰好相切**（`dist == r`）；总接触 **199**（单步 **+90**、**+94**） | 🔴 接触风暴（超上界 3.1 倍） |
| ② | **不冻结、不动作**（"交给库边"） | 球穿喉后**沿对角飞出**，实测 `x = −7.077 m`；`T34-2a` 红 | 🔴 静默飞台 |
| ③ | **推回到"不与任何段重叠"的最近位置 + 反射**（本次新增的有界扫描救援） | 仍为风暴：球被放进袋角环的**凹侧空腔**，总接触 **199** | 🔴 接触风暴（同量级） |
| ④ | **球心越过喉平面且在喉口宽度内 ⇒ 入袋**；`ρ > openingRadius`（只能靠隧穿达到）⇒ 送回台内 | `TotalContactCount = **14**`（基线 74）、`bb = 6`、`CapturedBallCount = 2`（入对袋 P3/P0）、无逃逸、**`T34` 全绿** | ✅ 两种失效同时消失 |

### 机理（用一次性计数探针定位，探针已删、`git status` 已复原）

- 风暴**不是**深重叠回路（实测 `deep = 0`），而是 **TOI 同刻接触组**：球停在袋角环内时
  `SolveToi` 反复给出 `t ≈ 0` 的"进入"根 ⇒ 每个子步解算 ~**15 轮**（每轮 `_segBall` ≤ 2 条）直到能量耗尽。
- ⇒ **"把球放进袋角环空腔"这一类动作天然会风暴**；只有"球根本不进入该腔"才能避免。
- 风暴**不受** `MaxToiIterations`（128）约束：实测 `ToiIterationGuardTrips == 0` 而单子步仅 ~15 轮
  ⇒ **不得**把"守卫未触发"当作"没发生风暴"的证据。
- 附带发现：球在袋角环内停摆时**速度归零且 `_frozen == false`**（既不在场上活跃、也不会被冻结清理）
  ⇒ 它是"球挂在袋口"在**未冻结**实现下的等价形态。

### 判据影响（选项 ④ 的口径改动清单）

- `T31b`（`ρ = captureRadius + 1e-3 ⇒ 不捕获、计窗口`）：该带改为入袋 ⇒ **口径必须改写**（否则恒红）。
- `T35` / `T39`：合成袋 fixture 的 `ρ = 0.08` 已**超出** `openingRadius`（合成袋 = `captureRadius + r + 2 mm`）
  ⇒ 在旧"越界冻结"口径下它算窗口，在新口径下"超出喉口" ⇒ fixture 需改到带内或按新口径改写。
- `T72b` / `T32b` / `T37`：**行为真变**（球不再在袋口冻结）⇒ 按纪律**重钉基线**（含 16 球终态逐位快照）。
- `T106` / `T107` / `T108`：`T108`（恒等式 + 静止补判）**与选项无关**；
  `T106`（喉口内 ⇒ 入袋）与 `T107`（喉口外 ⇒ 越界冻结）按裁定 A 的语义新写（见 §4d）。

## 4d. 裁定与实施（用户选 **A：喉口内即入袋**）

### 最终语义（`PoolSimulator.CheckPocketProximity`，`s = dot(C − tc, tn) > 0` 为前提）

| 档 | 条件 | 动作 | 计数 | 与历史的关系 |
| --- | --- | --- | --- | --- |
| 1 | `ρ ≤ openingRadius` | **入袋**（冻结离台） | `PocketProximityEvents++` + `CapturedBallCount++` + `_capturedPocket` | **边界由 `captureRadius` 移到 `openingRadius`** |
| 2 | `ρ > openingRadius` | **越界冻结**（离台、`_capturedPocket` 保持 −1） | `PocketProximityEvents++` + `PocketJawCrossingCount++` | 动作与历史**一致**（同样是冻结） |
| 3 | 整盘静止（`TableAtRest` **上升沿**）且 `|C − tc| ≤ openingRadius` 的**未冻结**球 | **补判入袋**（走与第 1 档完全相同的路径） | `CapturedBallCount++` + `AtRestMouthDrops++`（**不**计 `PocketProximityEvents`） | **新增**（§1.1 的第二批反馈） |

**恒等式**：`PocketProximityEvents + AtRestMouthDrops == CapturedBallCount + PocketJawCrossingCount`
（第 3 档不进 `PocketProximityEvents` ⇒ 必须显式出现在左边；`T108` 在四类场景各断言一次）。

### 与规格初稿的三处偏离（**均以实测为依据，逐条登记**）

1. **§1 行 2「弹回」→ 实为「入袋」**。理由：`captureRadius = openingRadius − BallRadius − 2 mm`
   ⇒ 该带 = "球体与袋角侧壁重叠"，而"球心在喉口宽度内"= **球心已在洞口上方** ⇒ 现实里必落。
   把它当"弹回"处理，四种候选动作里三种失效（§4c 表）；"入袋"同时消除风暴与逃逸（T34：199 → **14** 次接触）。
2. **§1 行 3「ρ > openingRadius ⇒ 交给正常库边」→ 实为「越界冻结」**。理由：该区域在袋喉平面**之后**、
   本工程**没有任何碰撞几何**。实测"不动作"⇒ 球**静默飞出台面**（`x = −7.077 m`）；
   "送回台内 + 反射"⇒ 接触风暴（199）。冻结是唯一**可证安全**的动作。
   ⚠ 该档只在**隧穿**时达到 —— `T34`/`T37` 两个大场景实测 `PocketJawCrossingCount == 0`
   （即"擦袋角"在本表几何下基本不可达）⇒ 该档的"归属"是"离台但不入袋"。
3. **§3.1 的"推回 + 法向反射"机制整体删除**（含其后的"扫描式最近不重叠位置"救援）。
   理由：**推回方向与入射路径反向** ⇒ 推回点必落在入射射线上 ⇒ 该扫描在本几何下**恒等于**推回 1 mm
   ⇒ 无正当理由保留（实测也证实它没有改变结果：仍 199）。
   ⇒ 同时删掉了"袋口情节迟滞"（`_pocketEpisode`）：两档都**立即冻结**球 ⇒ 后续子步的 `_frozen` 早退
   已保证"同一次越喉只计一次"，迟滞成为**不可达逻辑**（铁律 11）。

### 连带口径变更（**必须随本裁定一起理解**）

- **`captureRadius` 不再是判定边界** ⇒ `PoolSimulator` **不再读取**该量（逐袋缓存字段已删）；
  连带 **`PhysicsTuning.pocketCaptureRadiusScale` 成为无效果参数**（保留字段与 `PocketCaptureRadiusScaleValue`
  出口，以不破坏 `T41` 资产接线判据与 `T44` 出厂默认判据；**台账该行需改口径**，35-B 重新定义"净空捕获"时再用）。
- **判据改写（保留 ID，改描述性后缀）**：`T31a_CaptureRadius_JustInside_IsCaptured` →
  `T31a_InsideThroatOpening_FarInside_IsCaptured`；`T31b_CaptureRadius_JustOutside_IsNotCaptured` →
  `T31b_OutsideThroatOpening_IsNotCaptured`（已登记进 `Docs/工程标准.md` §9.3 更名对照表）。
  **`T31a`/`T31c` 的判别力登记**：其 ρ 同时满足新旧两个边界 ⇒ 在裁定 A 下**不再区分 `captureRadius` 与
  `openingRadius`**；边界判别力**不是被删除而是被迁移**：新增 **`T31d`**（`openingRadius` 双侧 ±1 mm）。
- **`T35` / `T39` / `T34-6` / `T72b` 口径同步**：恒等式加 `AtRestMouthDrops`；`T39` 独立枚举的分类边界
  改 `openingRadius`；`T72b` 重钉 `T37` 起点基线（`CapturedBallCount 0 → 2`、`PocketJawCrossingCount 2 → 0`）。
  ⚠ **`T37` 的 16 球终态逐位快照与 `T32b` 接触数基线未变** ⇒ 本裁定对既有物理**逐位无影响**：
  变的是"越喉且在喉口内的球"由"越界冻结"改判"入袋冻结"，**冻结位置相同**。

### 实测（本轮）

| 项 | 结果 |
| --- | --- |
| EditMode 全量 | **198 / 198**（194 + `T31d` + `T106` + `T107` + `T108`；failed=0 skipped=0） |
| 四验收器 | **26 / 33 / 22 / 7**（FAIL=0 WARN=0，四档 exit 0；`_d9r_out/v7/validators/`）※ `day1` 走 **Apply 入口**故为 26（含 `RUN-01`），见台账口径说明 |
| `T34` 接触数 | **14**（上界 64；改动前 74；三种失效候选分别 199 / 199 / 飞台） |
| 注入负对照（四档，均实测红 + 逐字节还原 `sha1 5881626b…`） | ① 边界回退到 `PocketCaptureRadius` ⇒ `T106`+`T31d` 红；② 第 2 档不冻结 ⇒ `T107`+`T31b`+`T31d`+`T35` 红；③ 去掉静止补判 ⇒ `T108` 红；④ `PocketProximityEvents` 由和推导 ⇒ `T39b` 红（白盒守卫有效） |

（注入脚本：`Docs/Tools/pocket_jaw_injections.sh`；产物在仓库外 `_d9r_out/v7/inj_v*.xml`。）

## 5. 盲带（如实）
- 推回距离 `0.001 m` 是**防抖动**的经验值；判据只锁"回到内侧"，不锁该值的观感。
- `openingRadius` 是**美术包给的口袋开口半径**；用它划"开口带"是**本单的工程解读**，不是美术包的明文规则
  ⇒ 该解读须登记，后续若美术/策划给出更明确的开口判据，替换此值即可（单点）。
