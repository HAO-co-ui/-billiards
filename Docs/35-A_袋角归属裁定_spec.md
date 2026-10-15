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

**用户原话**：「碰网框边缘时感觉不真实，好像缺少些弹力，球碰到碰网框边缘像贴在上面一样」
⇒ 第 2 行就是病根；第 3 行是同一个 bug 的另一种表现（球在库边上也被"粘住"）。

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

### `T108` 恒等式与既有捕获不变量

- `PocketProximityEvents == CapturedBallCount + PocketJawCrossingCount` 在三类场景各成立（正中入袋 / 擦角弹回 / 开口带外）。
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

## 5. 盲带（如实）

- **弹回后的手感**（反弹速度是否"看起来对"）不可判 —— 需用户实机。
- 推回距离 `0.001 m` 是**防抖动**的经验值；判据只锁"回到内侧"，不锁该值的观感。
- `openingRadius` 是**美术包给的口袋开口半径**；用它划"开口带"是**本单的工程解读**，不是美术包的明文规则
  ⇒ 该解读须登记，后续若美术/策划给出更明确的开口判据，替换此值即可（单点）。
