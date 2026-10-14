# 35-A Day 9 — 瞄准辅助与击球预测 · 实施 spec

| 项 | 值 |
| --- | --- |
| 版本 | **v1**（定稿级；待独立评审后可修订） |
| 状态 | **待实现**（未写代码） |
| 卡 | 35-A 击球手感验证（P0） |
| 排期依据 | `Docs/35-A_击球手感_制作排期.md` **§Day 9（`:220-230`）** |
| 前置 | 物理内核 I–III（Day 4–6）+ 参数外置与键盘可打（Day 7）+ **相机与指针瞄准（Day 8，已落地）**；灰盒场景 `PhysicsGreybox.unity` 可打、含 `PlayfieldRoot`（scale 1.2） |
| 基线 | **`HEAD = f697378`**；`git status --porcelain` 为空（实测）；**EditMode 源码 `[Test]` 计数 = 159 @ `f697378`**（实测，见 §9.1） |
| 坐标真源 | **ADR-0001**；唯一坐标系转换点 = `Runtime/Core/PoolGeometry.cs`；唯一"显示↔规范"映射点 = `Runtime/Core/PlayfieldView.cs` |
| 数据真源 | `PoolConstants`（米制）、`TableConfig.asset`（82 段 / 6 袋 / `footSpot`）、`PhysicsTuning.asset`（可由 `PhysicsTestDrive` 现场读出的**生效**参数） |
| 判据编号 | 自 **`T72`** 起。**已占用：`T41`–`T44`**（`GreyboxTuningWiringTests`）、**`T45`–`T49`**（`PointerAimTests`）、**`T50`–`T53`**（`PointerAimControllerTests` / `CameraViewportFitterTests`）、**`T54`–`T70`**（滚阻两段模型 / 出杆族）、**`T71`**（`CushionGizmoDisplayTests`）⇒ **本 spec 用 `T72`–`T83` + `T72b`**（共 **13** 条；`T72b` 是 `T72` 的**子判据**，沿用本工程既有的后缀式编号 `T32a`/`T39b`/`T42b` 先例；不复用、不跳号） |
| 待裁定 | 见 **§11**（`Q-14`…`Q-20`；其中 **`Q-14` 已由主理人批准**并附四条硬约束 **K-1…K-4**；仅 **`Q-20`** 仍待指认） |

> **阅读约定**：本文件是**排期产物**，正文里 `Day N` 是**主题词汇**，有意义。
> **但文中的代码片段，其注释立即适用 `Docs/注释规范.md`** —— 代码注释里不得出现开发日/批次标签；
> 检查项 ID、标准编号、提交哈希、实测数值**必须保留**。

---

## 0. 本轮变更摘要

### 0.1 权威范围（照抄排期，不放宽）

> **Day 9 — 瞄准辅助与击球预测（8 h）**
>
> | # | 任务 | 工时 |
> | --- | --- | --- |
> | 1 | 白球速度方向虚线（清晰、不糊） | 1.5 h |
> | 2 | **幽灵球（ghost ball）**：白球与首个目标球接触瞬间的白球位置 | 2 h |
> | 3 | 目标球预测出射方向线段 | 1.5 h |
> | 4 | **用物理前向模拟做预测**（而非解析几何）—— 含袋口与斜碰，更准 | 2.5 h |
> | 5 | 预测线开关（设置项预留接口） | 0.5 h |
>
> **验收**：**预测线与实际击打结果一致（含斜碰与擦库情形）**

**验收语气不改写、不放宽**。本 spec 的 §5 是把它**落成可判定判据**的方式（不是替换更弱的目标）。

### 0.2 一句话概括本轮要点

**每次瞄准**，把"当前全盘状态 + 期望初速"喂给一个**独立的内核实例**（与在跑内核**同参数、同步长、同解算**）向前推演，在**首次球-球接触**处取出**幽灵球**与**目标球出射方向**，并用**过显示层的虚线**画出来；**预测过程对在跑内核零扰动**；预测线开关关闭时**不在背后继续计算**。

### 0.3 本轮**不动**的东西（先声明，防越界）

- **不改** `PoolGeometry.cs` 的映射式（点 `(−px, h, −py)` / 方向 `(−dx, 0, −dy)`）—— 已冻结契约。
- **不改** `PlayfieldView.cs` / `PointerAimSolver.cs` / `PointerAimState.cs` / `PointerAimController.cs` / `CameraViewportFitter.cs`（Day 8 已落地并被 `T45`–`T53`、`T71` 钉住）。
- **不改** 物理参数语义（`PhysicsParams` 字段、两段滚阻、`RestSpeedThreshold` / `RestStableFrames`、袋口逐袋 `captureRadius`）。
- **例外 1**：`PoolSimulator.cs` 需**新增一个只读诊断**（"首次球-球接触"记录），见 §3.1 / `Q-14`（**已批准**，附四条硬约束）。**除该新增外不改内核任何解算路径**。
- **例外 2**：`PhysicsTestDrive.cs` 需**新增 1 个表现层开关字段**（`enableShotPrediction`）+ 其唯一读点属性，见 §3.5 / `Q-17`（**已裁定：最小形态、落驱动器组件**）。与既有 `aimIndicator*` 字段同处；**不改任何物理 / 交互语义**。
- **不改** 任何 `Docs/Reports/**` / `Docs/Reviews/**` / 其它 spec。

---

## 1. 范围与目标 / 明确不做（非目标）

### 1.1 目标（= §0.1 的 5 条）

1. **白球路径虚线**：从白球当前位置沿预测路径画到幽灵球（含库边反弹拐点）。
2. **幽灵球**：白球与首个目标球**接触瞬间**的白球位置（精确到内核 TOI）。
3. **目标球出射方向线段**：目标球被击中后的**出射方向**（由同一内核接触解算给出）。
4. **前向物理模拟**：以上三者**全部**来自对**同一内核语义**的向前推演（含库边与袋口），**不是**解析几何近似。
5. **开关**：预测线可开关，**预留设置项接口**，状态可观测，**关掉即不算**。

### 1.2 明确不做（**越界即算未完成**）

| 不做 | 归属 | 依据 |
| --- | --- | --- |
| **力度条 / 力度→初速曲线切换 / 球杆程序动画 / 击球状态机全量** | **Day 10** | 排期 `:232-243` |
| **音频 / VFX / HUD / 结算 / 埋点** | Day 11–14 | 排期 `:247-298` |
| **多库连锁推演（>1 次库边反弹后再接触）** 的**首接触** | 本日**不承诺** | 见 §1.3：本日只需"首个球-球接触"，不保证其后的完整走位预测 |
| **把预测线当"实际结果"回写**（预测不得改在跑内核 / 不得改球位） | 铁律：预测是**只读推演** | §5 `T72` |
| **预测的落点/走位（白球撞后去哪）** | 本日**只**出"白球路径 + 幽灵球 + 目标球出射方向" | 排期 Day 9 三条产出 |
| **端到端输入设备级验证**（真按鼠标拖拽看线） | Manual / PlayMode | EditMode 只测**纯逻辑 + 纯几何**（§5 各条盲带声明） |
| **`Main.unity` 的预测线落地** | 本日只在灰盒验证 | 与 Day 8 `Q-13` 同口径 |

> **⚠ 边界铁律**：本日**只交付**上述 5 条。任何"顺手把 Day 10 的力度条/球杆也做了"的改动，**按越界处理**（排期 §五 逐日范围不可增删）。

### 1.3 与"擦库连锁"的边界（**先钉死，防被判为漏做**）

排期验收写明"含**斜碰**与**擦库**情形"。本 spec 的口径：

- **斜碰**（球-球非正碰）：**必须**与实跑一致（§5 `T77`）。
- **擦库**：指"**白球在接触目标球之前先与库边发生 ≥1 次碰撞**"（银行球）⇒ 预测需能给出**经库边反弹后的**幽灵球与目标球出射方向，且与实跑一致（§5 `T78`）。
- **不承诺**：接触**之后**的多库/多球连锁推演（那属 Day 10+ 走位预测，排期未要求）。

---

## 2. 前置事实（**每条给 `文件:行号`**）

> 本 spec 依赖的既有实现。**读不到位的实现方不得开工**。

### 2.1 内核的**可读出口**（预测的输入全在这里）

| # | 事实 | 出处 |
| --- | --- | --- |
| F-1 | `PoolSimulator.Balls` = **只读视图**（`IReadOnlyList<BallState>`，元素为值类型副本） | `Runtime/Physics/PoolSimulator.cs:277` |
| F-2 | **生效参数的只读出口齐备**：`Params`（`PhysicsParams` 值副本）、`RestSpeedThresholdValue`、`RestStableFramesValue`、`PocketCaptureRadiusScaleValue` | `:284`、`:287`、`:290`、`:293` |
| F-3 | `PoolSimulator` 构造 = `(TableConfig, PhysicsParams, pocketCaptureRadiusScale, restSpeedThreshold, restStableFrames)`；`SubSteps` 由 `DeriveSubSteps(PoolConstants.MaxBallSpeed=8)` **推导**（对任何实例同值 ⇒ 3） | `:301`、`:338`、`:358`、`PoolConstants.cs:39` |
| F-4 | ⚠ **`LoadBalls` 会把**每颗球的 `_frozen = false`（回到场上）**与** `_capturedPocket = -1`（清掉入袋记录）—— 这是**"全新一局"**的定义，**不是"再出一杆"**。它是本工程**已登记的事实**（`T68`） | `:422`、循环体 `:439-444`（`{ }` 内 `:442`/`:443`） |
| F-5 | `PlaceBall(index, pos, vel)` **只改这一颗球**（位置/速度/在场/入袋记录/缓存），并把**表级** `TableAtRest=false`、`ConsecutiveRestFrameCount=0`；**不清任何每杆计数** | `:520`、`:538-539` |
| F-6 | `IsBallInPlay(i)` = 该球**在场上**（未因越喉平面冻结）；`CapturedPocketIndex(i)` = 落入哪个袋（未捕获 −1）。**两者不可互替**（擦袋角离台者 `frozen=true` 但 `captured=−1`） | `:550`、`:558` |
| F-7 | 每杆计数由 `ResetDiagnostics()` 归零（**不清逐球入袋记录**，也**不动 `_frozen`**） | `:464`、`:448-463` |
| F-8 | `Step()` = 3 个子步 + 1 次 `UpdateTermination()`；**只在 Step 粒度对外可见**（子步内的事件循环不对外暴露） | `:572-580`、`:584-713` |
| F-9 | 接触解算：**段接触在前、球-球对在后**；球-球法向 `n̂ = normalize(p_i − p_k)`；等质量冲量 `j = −(1+e_ball)·v_n/2`；随后 `SeparatePair` 把两球推到**恰好 `2r`** | `:1061-1072`、`:1138-1215`、`:1224-1241` |
| F-10 | ⚠ **首次球-球接触的"时刻/位置/落点"目前不可读** —— 内核只对外给**计数**（`BallBallContactCount` / `TotalContactCount`），**不给**"接触发生在子步内的哪一瞬、白球当时在哪"。这是本 spec 与内核的**唯一缺口**（`Q-14`） | `:178-179`、`:236`；**无**对应出口（`grep` 无 `FirstBallBall` 命中） |

### 2.2 驱动器与交互层（Day 7 / Day 8 已落地）

| # | 事实 | 出处 |
| --- | --- | --- |
| F-11 | `PhysicsTestDrive.Simulator`（public, 未初始化 `null`）—— 预测**只读**它，不得写 | `PhysicsTestDrive.cs:151` |
| F-12 | `Launch()`：**门控 `TableAtRest`**（未静止 ⇒ 返回 `Vector2.zero` 且**不改状态**）；白球初速 = `(cosθ, sinθ)·lerp(min,max,power)`；**只走 `PlaceBall(0, from, v0)`**（不重建球堆）；白球离台 ⇒ 显式放回 `footSpot`（留痕 `LastLaunchRespawnedCueBall`） | `:407`、`:415`、`:420-423`、`:426-429`、`:434` |
| F-13 | `AimDirectionUnity` = `PoolGeometry.PhysicsDirectionToUnity(cosθ, sinθ)` = **`(−cosθ, 0, −sinθ)`**（已由 `5189d4d` 修正，`T42`/`T47` 守卫） | `:107-114`；`PoolGeometry.cs:95-98` |
| F-14 | `SyncAimIndicator()`：球运动期间（`!TableAtRest`）指示物**隐藏**；静止时回到白球处 | `:599-639`、`:611-615` |
| F-15 | `PointerAimController` 只写 `drive.aimDegrees` 一个字段；纯函数核 `PointerAimState`（`Free`/`Dragging`/`Locked`）；细调系数常量 `FineAimStepFactor = 0.2f`（**唯一真源**） | `PointerAimController.cs:69`、`:43`、`PointerAimState.cs:41-81` |
| F-16 | 指针拾取纯核 `PointerAimSolver.TryPickPhysicsPoint`（无 `Input`、无状态）；世界方向入口 `WorldDirectionFromPhysics` → 委托 `PoolGeometry.PhysicsDirectionToUnity` | `PointerAimSolver.cs:53`、`:97` |

### 2.3 显示层（**绘制必须过的唯一落点**）

| # | 事实 | 出处 |
| --- | --- | --- |
| F-17 | **唯一"显示↔规范"映射点** `PlayfieldView`：`DisplayFromCanonical(root, canonical)` = `root.TransformPoint(canonical)`；`CanonicalFromDisplay` = `root.InverseTransformPoint`；`HasExpectedScale` 断言 uniform `1.2` | `PlayfieldView.cs:32`、`:41`、`:50` |
| F-18 | **绘制落点已单点化**：`CushionGizmo.DisplayPoint(root, canonicalWorld)` → `PlayfieldView.DisplayFromCanonical`（`null` ⇒ 原样返回＝scale 1 退化）；`CushionGizmo.DisplayScale(root)` 由显示映射导出（**不硬编码 1.2**） | `CushionGizmo.cs:113`、`:127`、`:120` |
| F-19 | `CushionGizmo` 的绘制坐标**曾经**直画规范世界坐标 ⇒ 与 1.2 的球错位（**已修**）；判据 `T71` 钉"绘制点 == 显示映射点"，并**源码守卫**禁止代码行出现字面量 `1.2` | `CushionGizmo.cs:19-25`；`Tests/EditMode/CushionGizmoDisplayTests.cs:76`、`:190-204` |
| F-20 | 方向**必须**走 `PoolGeometry.PhysicsDirectionToUnity`；**别处禁手写 `(−x,−y)`**（第二处转换点违铁律 16） | `PoolGeometry.cs:87-98` |
| F-21 | 球可视物挂在 `PlayfieldRoot` 下、写的是 **`localPosition` = 规范坐标**（世界坐标 = 缩放后的显示坐标） | `PhysicsTestDrive.cs:497-521`、`:506` |

### 2.4 场景 / 工具 / 台账

| # | 事实 | 出处 |
| --- | --- | --- |
| F-22 | 灰盒场景根 `PhysicsGreybox`；`PlayfieldRoot`（scale 1.2）由 `GreyboxSceneSetup` 幂等生成；`CushionGizmo.playfieldRoot` 已接线 | `Editor/Visual/GreyboxSceneSetup.cs`；`T71` 的场景接线断言 `CushionGizmoDisplayTests.cs:87-91` |
| F-23 | `PhysicsTestDrive` 序列化：`loadRackForBallBallDemo=1`、`aimDegrees=180`、`minLaunchSpeed=0.5`、`maxLaunchSpeed=4.5`、`aimStepDegrees=1`、`fineKey=LeftShift` | 场景 `PhysicsGreybox.unity`；由 `T43` 守"场景值 == 代码默认" |
| F-24 | 报告/证据路径**唯一单点** `ReportOutput.ResolveReportPath`；`ResolveTestEvidencePathOrNull` **未给 `-outputDir` 返回 `null`、绝不回落仓库** | `Editor/ProjectSetup/ReportOutput.cs`（Day 8 spec `F-24`） |
| F-25 | **EditMode 基线 = 159 @ `f697378`**（源码 `^\s*\[Test\]` 逐文件求和，**实测**；见 §9.1） | `Docs/测试数基线台账.md`；本单 `grep -cE` 实测 |
| F-26 | 全 15 球满堆、上界初速 8 m/s 的一次**整盘**开球（白球 `= MaxBallSpeed`、`Step()×1200`），接触解算次数实测达 **6746** ⇒ 预测的预算必须**正面回答这个量级**。⚠ **该数字的注册处 = 会话记忆 `.workbuddy/memory/2026-10-14.md:1660`（`.gitignore:77` 已忽略 ⇒ 不在仓库内）**；产生它的测试 = `T37_FullBreak_FifteenBallRack`（**该测试不含预算带断言**，故 6746 是**实测记录**而非仓库内冻结值）。本 spec 只把它当**量级参照** | `Tests/EditMode/PoolSimulatorTerminationAndBreakTests.cs:475`、`:486`；会话记忆 `2026-10-14.md:1660`/`:1696`；旁证 `工程标准.md §7` 第 10 条（`d6-full-break-sample.json` **零读者 / 不落盘**） |

---

## 3. 子任务拆解

> **程序集归属（铁律，`工程标准.md` §1 R1-1/R1-3）**：
> · 运行时资产进 **`Billiards.Runtime`**（命名空间前缀 `Billiards`，按层细分 `Core` / `Physics`）。
> · **编辑器 / 测试**代码进 **`Billiards.Editor`** 与 **`Billiards.Tests.EditMode`**。
> · **禁止** `Runtime → Editor/Diagnostics` 倒挂。命名空间**作前缀**。

### 3.0 总切法（**一句话**）

**纯核**（`ShotPredictor`：建独立内核 → 装快照 → 置初速 → 步进到首接触 → 出几何）与**胶水**（`AimAssistController`：读开关 / 取快照 / 触发重算 / 暴露结果）与**绘制**（`AimAssistView`：纯几何 + Gizmos / 表现层）**三层分离**，与 Day 8 的"纯状态 + 胶水"范式一致。

### 3.1 子任务 A — 内核新增**只读诊断**：首次球-球接触记录（**前提件**，`Q-14`）

**为什么必须**（否则任务 2/3 不可判定）：

- 内核对外**只在 `Step()` 粒度可见**（F-8）。若预测靠"每步后轮询 `BallBallContactCount`"来定位首接触，则取到的是**该 Step 末端**的位置 —— 白球已带**撞后速度**又走了 1/120 s。
  - 上界误差 = `|v_after| / 120`。**擦碰**（grazing）时白球几乎保留原速（4.5 m/s）⇒ 误差 **37.5 mm > 球半径 28.575 mm** ⇒ 幽灵球会**越过**目标球。
  - 且"接触瞬间"的定义（球心距 == 2r）在 Step 末端**不成立**（球已分离/已嵌入）。
- ⇒ **幽灵球的定义要被判定，内核必须把"接触那一瞬"吐出来。**

**新增（只读、不改解算路径）**：

```
// Runtime/Physics/PoolSimulator.cs —— 新增诊断（仅记录，不参与物理）
public struct BallBallContactRecord {
    public int    first;                 // 球索引（升序对的第一颗）
    public int    second;                // 球索引（第二颗）
    public Vector2 firstPosition;        // 推进到 TOI 时刻的位置（冲量施加前）
    public Vector2 secondPosition;
    public Vector2 firstVelocityAfter;   // 解算后速度
    public Vector2 secondVelocityAfter;
    public float  timeSeconds;           // 自本次记录清零起的累计仿真时间
}
public bool HasBallBallContact { get; }          // 是否已记录首次
public BallBallContactRecord FirstBallBallContact { get; }   // 仅当 Has… 为真有意义
```

- **记录时机（单点）**：`ResolveBallBallContact(i, k, n)` 的**入口**（本函数是 `BallBallContactCount` **唯一**递增之处，`:1177`/`:1207` 两处都在其内）—— 此刻"所有活跃球刚推进到 `earliest`、冲量**尚未**施加"，正是幽灵球要的"相切瞬间"。由私有布尔 `_hasFirstBallBallContact` 守卫，**只记第一次**，解算后的速度在同一函数内补进记录。
  - **单点理由**：`BallBallContactCount++` 有**两处**（`:1177` 的"只分离不施冲量"分支、`:1207` 的"施冲量"分支），但**都在** `ResolveBallBallContact` 内 ⇒ 在**函数入口**记录 = **一处**记录点，**不新开**第二个"检测球-球接触"的地方（铁律 16）。
  - **盲带**：若**首次**调用走 `vn ≥ 0`（深层重叠 / 球心重合，`:1172-1182`）分支，入口记录的位置满足 `|Δ| < 2r`（非相切）⇒ `T75a` 会红。**合法静止局面不含深层重叠**（球堆间距 `2r + 2e-4`）⇒ 本 spec 把该情形**排除在验收场景之外**并登记为盲带。
- **时间口径**：`timeSeconds` = `已完成的 Step 数 × FixedDeltaTime` + `本 Step 内已完成子步数 × SubStepDeltaTime` + `(SubStepDeltaTime − 该子步接触时刻的 remaining)`；**不读 `Time`**（不破坏确定性，`R5-3`）。
- **清零**：由 `ResetDiagnostics()` 一并清（⇒ 新实例天然为空；在跑内核在 `Launch()` 后会自然开始记录）。

**四条硬约束（主理人裁定，逐条落地，缺一不可）**：

| # | 约束 | 落地方式 | 判据 |
| --- | --- | --- | --- |
| **K-1** | **单点**：写在 `BallBallContactCount` 递增的**同一处**，不许另开第二个"检测球-球接触"的地方 | 记录点 = `ResolveBallBallContact` **函数入口**（两处递增都在该函数内）；**不新增**任何球-球 TOI 扫描 / 配对循环 | `T72b`**(a)**（源码守卫：`BallBallContactCount++` 恰 2 处，且记录语句只出现在该函数内） |
| **K-2** | **必须证明惰性**：加上诊断后**既有逐位证据一字不变** | 重跑 `T32b` 与 `T37`，**核 接触数 与 逐球终态 逐位相同**（同一串行化口径） | `T72b`**(b)**（逐位一致 + 计数不变） |
| **K-3** | 纳入 `ResetDiagnostics()` 的语义与既有族**一致**（每杆计数） | 与 `BallBallContactCount` 等**同处**归零 | `T72b`**(c)**（`ResetDiagnostics()` 后 `HasBallBallContact == false`） |
| **K-4** | ⚠ `T55`（白盒单点守卫）可能需跟着重钉 ⇒ **如实改并说明为什么不是放宽** | **判定：不触到** —— `T55` 只扫 `DecelerationAt(` / `_params.rolling*` / `AverageVelocity`(3 处) / `ApplyRollingDeceleration`(1 处)；新增记录位于 `ResolveBallBallContact`，**既不调用** `AverageVelocity`/`ApplyRollingDeceleration`，**也不读** `_params.rolling*`（记录字段是私有成员，不匹配 `_params.rolling[A-Za-z]*`）。**落地后必跑 `T55`**；若计数变化 ⇒ 如实重钉 + 说明理由（**不放宽**：那三处/一处计数是**物理不变量的守卫**，不得为了通过而删项） | `T72b`**(d)** + `T55`（**回归**，不新增判据） |

**判据**：`T72b`（单点 + 惰性 + 清零语义）+ `T75a`（幽灵球与目标球心距 == `2r`）。
**注入**：把记录时机从"函数入口"改成"`Step()` 末尾采样" ⇒ **`T75a` FAIL**（`|Δ| ≠ 2r`，偏差达 37.5 mm 量级）；把记录**另开**一个配对循环 ⇒ **`T72b` (K-1) FAIL**。

> **替代方案（已评估、未采纳）**：预测侧用**闭式 2 球 TOI** 在 Step 边界内插值。**不采纳**理由：那是**第二处**接触几何实现（铁律 16），会与内核 TOI 分叉；且排期明写"**用物理前向模拟做预测，而非解析几何**"。

### 3.2 子任务 B — 纯核 `ShotPredictor`（任务 1/2/3/4 的本体）

**新增交付物（运行时）**
- `Runtime/Physics/ShotPredictor.cs` · `namespace Billiards.Physics` · assembly `Billiards.Runtime`。**纯静态、无 `Input`、无 `MonoBehaviour`、无 `Time`**：

```
public static ShotPrediction Predict(TableConfig table, PoolSimulator source,
                                     Vector2 cueVelocity, int maxSteps)
```

**唯一算法（顺序固定）**：

1. **建独立实例**（**不得**用在跑内核 —— 见 `T72`）：
   `var probe = new PoolSimulator(table, source.Params, source.PocketCaptureRadiusScaleValue, source.RestSpeedThresholdValue, source.RestStableFramesValue);`
   ⇒ **参数/步长与在跑内核同源**（`T73`）。
2. **装快照**：`probe.LoadBalls(SnapshotInPlay(source))`
   - `SnapshotInPlay` = **只取 `source.IsBallInPlay(i) == true` 的球**（位置与速度**原样照抄**），丢掉已离台（frozen）的球。
   - **裁定（见 `Q-14b`）**：**已入袋 / 擦袋角离台的球，在快照里"不表示"（被排除）**。理由三条：① 内核**没有**公开的"重新冻结"入口（`LoadBalls` 必清 `_frozen`，`PlaceBall` 必解冻）⇒ "带着 frozen 装载"**结构上不可达**；② 离台球速度恒为 0 且位于喉平面之外，**本不参与**后续运动；③ 排除后 `probe` 的球数恰等于"在场上球数"，可被 `T74` 直接断言。
   - ⚠ **已知偏差（盲带，必登记）**：在跑内核里，"运动球 vs **frozen 球**"的对**不会**被 `!_active[i] && !_active[k]` 跳过（`:957`）⇒ 理论上白球仍可撞到**已在袋里的球**；`probe` 里没有它 ⇒ 预测与实跑在该窗口会分叉。窗口极窄（白球须自身也进入同一袋口区域），**`T78` 的擦库/斜碰场景刻意不含"贴近袋口的入袋球"**，并在 §5 `T74` 声明该盲带。
3. **置初速**：`probe.PlaceBall(cueLocalIndex, cueStart, cueVelocity)`（只动白球；`cueLocalIndex` = **白球在快照子集里的下标**（= 现场从 `source` 的白球索引映射得到）；`cueStart` = 白球快照位置；若白球不在场上 ⇒ 用 `table.footSpot`，**与 `Launch()` 的 respawn 口径一致**，`F-12`）。
4. **步进到首接触**：循环 `probe.Step()`，直到 `probe.HasBallBallContact` 或步数达 `maxSteps`；每步记录白球位置（画虚线用）。
5. **出几何**：
   - `GhostBall` = 记录里**白球那一侧**的 `Position`（冲量前）。
   - `TargetBallCenter` = 记录里**另一颗球**的位置。
   - `TargetOutgoingDirection` = `normalize(secondVelocityAfter)`（白球撞击后目标球速度方向）；退化（≈0）⇒ 回落到 `normalize(TargetBallCenter − GhostBall)`（**球心连线**，等质量无自旋下的精确出射方向）。
   - `CueOutgoingDirection` = 记录中白球的 `firstVelocityAfter` 归一化。
   - `CueWorldPath`（虚线折线，**规范坐标**）= `[cueStart, …每步白球位置…, GhostBall]`。
   - `CushionBounceCountBeforeContact` = 路径上白球速度方向发生**非球-球**改变（即与库段接触）的次数 —— 由 `probe.TotalContactCount` 与 `probe.BallBallContactCount` 的**差**在每步的增量判定。

- `Runtime/Physics/ShotPrediction.cs`（值类型结果）：`HasFirstContact`、`CueStart`、`GhostBall`、`TargetBallCenter`、`TargetOutgoingDirection`、`CueOutgoingDirection`、`TimeToFirstContactSeconds`、`StepsSimulated`、`CushionBounceCountBeforeContact`、`IReadOnlyList<Vector2> CueWorldPath`，**以及只读诊断出口** `BallBallContactCount` / `TotalContactCount`（= `probe` 结束时的同名计数）—— 供 `T83` 断言成本（**不得**要求测试去够 `Predict` 的局部 `probe`）。

**为什么这样切**：
- **纯静态 + 局部 `probe`** ⇒ 判据可直接驱动，不需要场景、不需要鼠标。
- **probe 是函数内局部变量** ⇒ "零扰动在跑内核"是**结构保证**（`T72` 把它变成可判定）。

> ⚠ **虚线折线的粒度（如实声明盲带）**：路径点按 `Step()`（1/120 s）采样 ⇒ **库边拐角最多 37.5 mm 的圆角误差**。它**只影响虚线观感**，`GhostBall` / `TargetOutgoingDirection` **不**受影响（它们来自内核 TOI 记录，精度 ~1e-6 m）。

### 3.3 子任务 C — 胶水 `AimAssistController`（触发 / 开关 / 缓存）

**新增交付物（运行时）**
- `Runtime/Physics/AimAssistController.cs` · `MonoBehaviour`，挂灰盒 `PhysicsGreybox` 根。
- 序列化字段：`PhysicsTestDrive drive`、`Camera aimCamera`（可空）、`Transform playfieldRoot`、`int maxPredictSteps`（**待实测定值**，见 §4.1）。**开关字段不在这里** —— 它落在驱动器组件上（`Q-17`，见 §3.5）。
- 每帧（`LateUpdate`）：
  - **开关关闭 ⇒ 直接返回**（`drive.PredictionEnabled == false` ⇒ **不得**调用 `Predict`；`T82`）。
  - **仅在 `drive.Simulator.TableAtRest` 且 `drive.Simulator != null` 时**重算（球运动期间预测**无意义**且**隐藏**，与 `F-14` 同口径）。
  - **脏标记**：仅当 `(aimDegrees, power, 在场上球快照指纹)` 变化时才重算 ⇒ 静止时**不每帧重算**（`T83`）。
  - 重算：`v0 = (cosθ, sinθ)·lerp(min,max,power)`（**与 `Launch()` 同一口径**，`F-12`）；`_prediction = ShotPredictor.Predict(drive.tableConfig, drive.Simulator, v0, maxPredictSteps)`。
- **只读出口**（可观测，`T82`）：`Prediction`（当前结果）、`PredictionComputeCount`（累计重算次数，诊断量）。**开关的唯一读点是 `drive.PredictionEnabled`**（→ `PhysicsTestDrive.enableShotPrediction`，§3.5）—— 本类**不另存**一份开关状态。
- **不写任何内核状态**：本类**不得**调 `drive.Simulator.Step/PlaceBall/LoadBalls/ResetDiagnostics`（`T72` 由源码 `grep` + 状态快照双重覆盖）。

### 3.4 子任务 D — 绘制 `AimAssistView`（任务 1/3 的表现层）

**新增交付物（运行时）**
- `Runtime/Physics/AimAssistView.cs` · `MonoBehaviour`，挂灰盒 `PhysicsGreybox` 根（或由 `AimAssistController` 驱动）。
- **纯几何函数（判据直接驱动，不必真画；与 `T71` 同风格）**：

```
public static IReadOnlyList<(Vector2 a, Vector2 b)> BuildDashSegments(Vector2 from, Vector2 to,
                                                                     float dashLength, float gapLength);
public static Vector3 DrawPoint(Transform playfieldRoot, Vector3 canonicalWorldPoint);   // 委托 CushionGizmo.DisplayPoint
public static float  DrawScale(Transform playfieldRoot);                                 // 委托 CushionGizmo.DisplayScale
```

- **绘制坐标**：一律经 `DrawPoint`（→ `CushionGizmo.DisplayPoint` → `PlayfieldView.DisplayFromCanonical`，**唯一落点**，`F-18`）。**不得**直画规范坐标；**不得**手写 `×1.2`。
- **方向**：一律 `PoolGeometry.PhysicsDirectionToUnity`（或 `PointerAimSolver.WorldDirectionFromPhysics`）。**不得**手写 `(−x,−z)`。
- **表现层**：Game 视图用 `LineRenderer`（**每条 dash 一段 2 点折线，多段拼成虚线**；不依赖贴图/材质宽度曲线），挂在 `PlayfieldRoot` 下、`useWorldSpace = false`（局部坐标 = 规范坐标 ⇒ 天然继承 1.2 显示缩放，等价于过 `DisplayFromCanonical`）；另加 `OnDrawGizmos` 的 Scene 视图预览（复用 `DrawPoint`）。
- **幽灵球标记**：`Gizmos.DrawWireSphere(DisplayPoint(root, PoolGeometry.PhysicsBallToUnity(ghost)), radius× DrawScale(root))`。
- **目标球出射线段**：从幽灵球位置沿 `TargetOutgoingDirection` 画**实线段**（长度 = `TargetLineLength`，§4.1）。

### 3.5 子任务 E — 开关与设置项预留接口（任务 5）

**裁定居所（`Q-17` 已批准：最小形态，**不建**独立 ScriptableObject 资产）**：

- **唯一写点**：`PhysicsTestDrive.enableShotPrediction`（`[Tooltip("瞄准辅助：预测线开关（设置项预留接口）。")]`），**与既有 `aimIndicator` / `aimIndicatorLength` / `aimIndicatorThickness` 同处**（`PhysicsTestDrive.cs` 的"瞄准指示物（Game 视图可见；无物理组件）"区）—— 它们是**表现 / 辅助**字段，本开关同族。
- **唯一读点**：`PhysicsTestDrive.PredictionEnabled`（只读属性 → 返回 `enableShotPrediction`）—— 未来设置菜单**只写这一个字段**，不新增第二处开关状态。
- **不得**塞进 `PhysicsTuning`（那是**物理手感入口**，混进表现开关会污染语义；且 `T44` 逐字段冻结该资产 ⇒ 会被判"偏离台账"）。
- **可观测**：`AimAssistController.Prediction` / `PredictionComputeCount` 公开只读；`PhysicsTestDrive.PredictionEnabled` 公开只读。
- **关闭即不算**：`AimAssistController.LateUpdate` 首行 `if (drive == null || !drive.PredictionEnabled) return;` ⇒ **不重算、不绘制**（`T82`）。
- **默认**：`enableShotPrediction = true`（开箱即用，与 `aimIndicator` 由 `Initialize` 按需自动创建同风格）。

> **协调影响（须复查，不自作主张）**：`T43`（场景序列化值 == 代码默认）是**显式字段清单**（`maxLaunchSpeed` / `minLaunchSpeed` / `rolling*` / `cushion*` / `ball*`），**不含**本新字段 ⇒ 本开关**不在其覆盖内**。本 spec **不**要求扩面（它是表现开关、非手感入口）；若主理人要求覆盖该 bool 的"场景漂移"，**请指认**（加一行 `Assert.AreEqual(probe.enableShotPrediction, drive.enableShotPrediction)`，属扩面、非放宽）。

---

## 4. 接口 spec

### 4.1 常量 / 字段落点表（实现不得自行造值）

| 常量 / 字段 | 落点 | 状态 | 值 / 语义 |
| --- | --- | --- | --- |
| `BallRadius` / `PlayfieldVisualScale` / `ClothHalfLength` / `ClothHalfWidth` / `MaxBallSpeed` | `PoolConstants` | **已存在，不动** | 0.028575 / 1.2 / 1.27 / 0.635054… / 8 |
| `maxPredictSteps` | `AimAssistController` | **需新增** | **待实测定值**（推导见 §8.3 `R-AL`；**不得编数字**） |
| `PredictionDashLength` / `PredictionGapLength` | `AimAssistView` | **需新增** | **待实测定值**（世界米；**必须满足 `gap > 0` 且屏幕 ≥1 px**，见 §5 `T80`） |
| `PredictionLineWidth` | `AimAssistView` | **需新增** | **待实测定值**（世界米；= 显示尺度 × 常数） |
| `TargetLineLength` | `AimAssistView` | **需新增** | **需选定**（米；建议与 `PhysicsTestDrive.aimIndicatorLength` 量级一致，参考 `0.55`） |
| `BallBallContactRecord` / `HasBallBallContact` / `FirstBallBallContact` | `PoolSimulator` | **需新增（只读诊断）** | 见 §3.1（`Q-14` 已批准 + K-1…K-4） |
| `enableShotPrediction`（字段）+ `PredictionEnabled`（只读属性） | **`PhysicsTestDrive`**（与 `aimIndicator*` 同处） | **需新增** | `bool`，出厂 `true`；**设置项预留接口的唯一写点/读点**（`Q-17`）；**不得**落 `PhysicsTuning`（见 §3.5） |

### 4.2 契约（在内核/引擎既有契约之上追加）

1. **预测只读**：预测过程**不得**改在跑内核的任何状态（含 `Balls` 的位置/速度、`_frozen`、`_capturedPocket`、每杆计数、`TableAtRest`）。**唯一**被预测读的内核状态 = `Balls` / `Params` / `RestSpeedThresholdValue` / `RestStableFramesValue` / `PocketCaptureRadiusScaleValue`（F-1/F-2）。
2. **同源参数**：`probe` 的 `Params` / `SubSteps` / 三个终止与袋口参数**逐字段**等于在跑内核（`T73`）。
3. **快照语义**：快照 = **在场上球的子集**（位置/速度原样）；**已离台球不表示**（§3.2 步 2 + `Q-14b`）。
4. **幽灵球定义**：= 内核记录的**首次球-球接触、冲量施加前**的白球位置 ⇒ 与目标球心距**恰为 `2r`**（`T75`）。**不得**取 "Step 末端采样" 或 "球心连线解析交点" 顶替。
5. **绘制单点**：绘制坐标**只**经 `PlayfieldView`（经 `CushionGizmo.DisplayPoint` 包装）；**不得**出现第二个 1.2 / 第二处轴手性转换（F-18/F-20）。
6. **方向单点**：世界方向**只**由 `PoolGeometry.PhysicsDirectionToUnity` 导出（F-20）。
7. **不引 PhysX**：新代码不得出现 `Rigidbody` / `Collider` / `PhysicsModule`（`R5-1`、`D4-PHY-01`）。
8. **无 `Time`/`Random` 进入推演**：`ShotPredictor` 与 `probe` 侧**不得**读 `Time`/`Random`（`R5-3`）；`AimAssistController` 可读 `Time`/`Input`（它不在内核侧）。

---

## 5. 测试族（**判据续 `T71`，自 `T72` 起**）

> **编号占用边界**：`T41`–`T44` / `T45`–`T49` / `T50`–`T53` / `T54`–`T70` / **`T71`** 已占用；**本 spec 的判据 = `T72`–`T83` + `T72b`（共 13 条）**（不复用、不跳号）。

> **坐标系**：物理平面 x 长轴 / y 短轴，单位米。测试运行在 **EditMode**。
> **期望值必须来自「被测对象之外」**（`工程标准.md` §7 第 8 条）。本 spec 的期望值分类：
> **甲 = 独立闭式 / 独立几何构造 / 引擎原语**；**乙 = 契约常量 / 场景序列化值 / 独立实例实跑**；
> **乙-系数选定 = 选定门限**；**乙-同源 = 与在跑内核同内核的独立实例实跑（盲带已声明）**；**丙 = 转写被测代码公式 = 0**。
> **每条必须声明**：盲带（抓不到什么） / 判别力来源 / **注入式负对照**（注入什么 ⇒ 期望哪条 FAIL）。

### T72 预测对在跑内核**零扰动**（+ 预测确实运行过）

**构造**：建 `PhysicsTestDrive`（球堆模式）+ 静止；对全盘取**逐位快照** `S_before`（`Balls` 位置/速度 + `CapturedPocketIndex` + `IsBallInPlay` + `TableAtRest` + `ConsecutiveRestFrameCount` + 每杆计数，全部 `ToString("R", Invariant)`，口径同 `PhysicsTestDriveLaunchStateTests.Snapshot`）。
调用 `ShotPredictor.Predict(...)`（≥3 组初速）；再取 `S_after`。

**判据（三条，缺一不可）**：
- （a）`S_before == S_after`（**逐位**，0 容差）；
- （b）预测**确实运行过**：返回的 `StepsSimulated > 0`（否则"零扰动"可被"什么都没跑"满足 —— 空转守卫，`R4-3`）；
- （c）同一输入**重复两次**结果逐位相同（确定性）。

**期望值来源**：**乙**（被测对象自身的**前后**状态，且要求**不变**；不是"期望值 = 被测对象产出值"）。
**盲带**：只覆盖"调用 `Predict` 这一动作"；不覆盖"`AimAssistView` 每帧顺手写内核"（由 §5 `T79` 的源码守卫 + 复核 grep 补）。
**判别力来源**：把"就地用在跑内核推演"这一**最省事的错误实现**变成可判定失败。
**注入式负对照**：
- 把 `Predict` 改成直接对 `source` 实例 `Step()`（省掉 `probe`）⇒ **（a）FAIL**（`Balls`/计数/`TableAtRest` 全变）。
- 让 `Predict` 提前 `return`（不跑）⇒ **（b）FAIL**。

### T72b 内核新增只读诊断的**单点**与**惰性**（`Q-14` 四条硬约束的牙）

**构造**：① 源码级：读 `Runtime/Physics/PoolSimulator.cs`（**去注释**后），对 `BallBallContactCount++` 的出现次数与"记录语句所在函数"做**白盒断言**（同 `T55` 的形态）。② 行为级：按 `T32b` / `T37` 的**既有场景与串行化口径**重跑一次，与**既有基线**（`t32bTotalContactCount`、`T37` 的接触数与**逐球终态**）比对。

**判据（四条，对应 K-1…K-4）**：
- （a）**K-1 单点**：去注释后 `BallBallContactCount++` 恰 **2 处**（`ResolveBallBallContact` 的"只分离"与"施冲量"分支），且**首接触记录语句只出现在 `ResolveBallBallContact` 内**（没有第二个球-球检测/扫描点）；
- （b）**K-2 惰性（逐位）**：`T32b` / `T37` 重跑后的 `TotalContactCount` / `BallBallContactCount` 与**逐球终态**（位置/速度 `ToString("R")`）与既有基线**逐位相同**；
- （c）**K-3 清零语义**：`ResetDiagnostics()` 后 `HasBallBallContact == false`（与 `BallBallContactCount == 0` 同处生效）；
- （d）**K-4 `T55` 回归**：`T55` 仍 PASS（新增记录不引入 `AverageVelocity`/`ApplyRollingDeceleration` 调用点、不引入 `_params.rolling*` 新名字）。

**期望值来源**：**乙**（既有基线 = 改动前实测；本判据断言"**不变**"）＋ **甲**（源码计数 = 直接计数）。
**盲带**：源码守卫是**文本级**的（改注释/换行风格可规避），故（a）**只**作为**单点性的辅助证据**；真正的判据是（b）的**逐位不变**与 `T75a` 的几何恒等式。**不声称**（a）能证明"实现者真的没另开检测点"。
**判别力来源**：把主理人的 K-1…K-4 四条硬约束变成**可失败**。（b）最重要 —— 它把"只读诊断"从**声明**变成**实测**：若诊断偷偷影响解算（或重构计数点导致顺序变化），既有逐位证据必改。
**注入式负对照**：
- 把首接触记录**另开**一个独立配对循环来检测（而非在 `ResolveBallBallContact` 内）⇒ **（a）FAIL**。
- 让诊断顺带**归一化/微调**两球位置（哪怕 1e-9）⇒ **（b）FAIL**（逐球终态不再逐位相同）。
- 把记录清零塞进 `ResetDiagnostics()` **之外**（例如只在构造函数清）⇒ **（c）FAIL**。
- 在 `AverageVelocity` 的某个调用点旁顺手读一次 `_params.rollingDecelerationSlide` 来实现"时间戳" ⇒ **（d）FAIL**（`T55` 的 `_params.rolling*` 名字集合 / 调用点计数变化）。

### T73 预测世界与在跑内核**同源**（参数 / 步长 / 终止 / 袋口）

**判据**：`probe` 的下列量与 `source` **逐字段位相等**（`ToString("R")` 比对）：
`Params`（**全部 7 个公开字段**：`cushionRestitution` / `cushionTangentialFriction` / `ballRestitution` / `ballTangentialFriction` / `rollingDecelerationSlide` / `rollingDecelerationRoll` / `rollingTransitionSpeed`）、`SubSteps`、`RestSpeedThresholdValue`、`RestStableFramesValue`、`PocketCaptureRadiusScaleValue`；且 `probe` 用的 `TableConfig` 与 `source` **同一资产**（段数 == `TableConfig.SegmentCount`、袋数 == `TableConfig.PocketCount`）。

**期望值来源**：**乙**（在跑内核的只读出口，F-2 —— 与 `probe` 是不同对象）。
**盲带**：不能证明 `probe` 真的**用到**这些参数（只证明"读进去了"）；"用到"由 `T76`/`T78` 的实跑一致兜底。
**判别力来源**：把"预测用 `PhysicsParams.SafeDefault` / 默认 `captureScale=1` / 默认终止参数"这类**静默不同源**变成可判定失败。
**注入式负对照**：
- `probe` 用 `PhysicsParams.SafeDefault`（覆盖 `e_ball=1.0`）⇒ **T73 FAIL**（与在跑的 0.95 不等）。
- `probe` 用 `pocketCaptureRadiusScale = 1f`、而在跑内核资产为 ≠1 ⇒ **T73 FAIL**。

### T74 快照语义：**只装在场上球**、**入袋球不复活**

**构造**：造一个**含已入袋球**的局面（打出白球入某个中袋，与 `PhysicsTestDriveLaunchStateTests` 的 `T70` 同法），此时 `IsBallInPlay(0) == false`；记录 `inPlayCount = #{i : IsBallInPlay(i)}`。再对**另一颗在场上球**（或把白球视为 `footSpot` respawn）调 `Predict`。

**判据（三条）**：
- （a）`probe` 装载后的球数 **== `inPlayCount`**（**不含**已入袋球）；
- （b）预测**不改**在跑内核的入袋记录：`CapturedPocketIndex(i)` 全盘逐位不变（含已入袋者仍 ≥0）；
- （c）**反证"不复活"**：若把**全部**球（含已入袋）喂给 `LoadBalls`，则该球在 `probe` 里 `IsBallInPlay == true`（证明 `LoadBalls` 的清 `_frozen` 行为真实存在，`F-4`），从而说明实现**必须**筛子集 —— 该反证是**判据的一部分**（不得省略）。

**期望值来源**：**乙**（在跑内核的 `IsBallInPlay` / `CapturedPocketIndex`）+ **甲**（`inPlayCount` 的直接计数）。
**盲带**：不覆盖"预测世界**与实跑一致**"（那由 `T76`–`T78` 承担）；只钉**装载语义**。
**判别力来源**：把 F-4 的"已登记事实"变成**必须被正面处理**的一步（本工程踩过 `T68` 同族缺陷）。
**注入式负对照**：
- 快照传 `source.Balls` **全集** ⇒ **（a）FAIL**（入袋球复活 ⇒ 球数 > `inPlayCount`）。
- 预测里顺手 `source.ResetDiagnostics()`（清入袋记录）⇒ **（b）FAIL**。

### T75 幽灵球 == **首次球-球接触瞬间**的白球位置（`|Δ| == 2r`）

**构造**：直球场景（白球 + 单目标球，无库），调 `Predict`。

**判据（三条）**：
- （a）`|GhostBall − TargetBallCenter| == 2·PoolConstants.BallRadius`（容差 **1e-6 m**；来源见下）；
- （b）`HasFirstContact == true` ∧ `StepsSimulated ≥ 1` ∧ `CueWorldPath` 非空 ∧ **`CueWorldPath` 末点 == `GhostBall`**（逐位，"幽灵球在路径终点"）；
- （c）**时间区间约束**：`0 < TimeToFirstContactSeconds ≤ StepsSimulated × PoolSimulator.FixedDeltaTime`（**区间**，非等值 —— 不得写成"时间 == 自己算的时间"）。

**容差 `1e-6 m` 的来源（可被证伪）**：内核在 TOI 处推进（`|d|² = (2r)²` 解析解），随后 `SeparatePair` 把间距**恰好**置为 `2r`（F-9）。float32 在 ~0.06 m 尺度的分辨率 ~7e-9 m，二次方程 + `sqrt` 累积几 ulp ⇒ 实测残差 ~1e-7 量级；取 `1e-6 m` 留 10× 余量。**判别力**：错误的幽灵球定义（漏 `2r`）偏差 **57.15 mm**，是容差的 **5.7e4 倍**；"Step 末端采样"偏差 37.5 mm 量级，是 **3.75e4 倍**。

**期望值来源**：**甲**（（a）的 `2r` 几何恒等式，与被测对象无关）＋ **乙**（（b）的路径终点一致性；（c）的区间）。
**盲带**：**（a）** 是唯一具**绝对**判别力的判据；（b）（c）是**内部一致性**（防"取错球/取错时刻/路径与幽灵球脱节"），**不**能检出内核缺陷。本 spec **不声称**（b）（c）证明物理正确。另：本判据只覆盖"白球与单目标球"，不覆盖"球堆里首接触是别的球对"（由 `T78` 的实跑一致覆盖）。
**注入式负对照**：
- 幽灵球改为"`Step()` 末端白球位置"⇒ **（a）FAIL**（`|Δ| ≠ 2r`；擦碰场景偏差约 37.5 mm）。
- 幽灵球改为"白球起点 + 方向 × 白球-目标球心距"（漏 `2r`）⇒ **（a）FAIL**（偏差 57.15 mm）。
- 幽灵球与路径终点取**不同**来源（例如路径取 Step 末端、幽灵球取 TOI）⇒ **（b）FAIL**（两者不逐位相等）。
- 把 `TimeToFirstContactSeconds` 置 `0` 或不填 ⇒ **（c）FAIL**（要求 `> 0`）。⚠ **（c）是弱判别**（只是区间），本 spec **不声称**它强 —— 主判别量是（a）与（b）。

### T76 **直球**（无库）闭式一致 —— 甲

**构造**：白球 `c0` 与单目标球 `t`，`d = |t − c0| > 2r`，初速方向 `u = normalize(t − c0)`（正碰）。

**判据**：
- （a）`GhostBall == c0 + u·(d − 2r)`（容差 **1e-6 m**）；
- （b）`TargetOutgoingDirection` 与 `u` 夹角 **< 0.05°**；
- （c）`CushionBounceCountBeforeContact == 0`。

**期望值来源**：**甲**（一维等质量正碰的**代数**；`d − 2r` 与速度、减速度**无关** ⇒ 不受滚阻影响）。
**盲带**：只覆盖**无库、单目标**；不覆盖斜碰（`T77`）与擦库（`T78`）。
**判别力来源**：把"幽灵球定义/距离"钉死到**解析几何**；任何"漏 2r / 用球心距当接触点"的实现在此必红。
**注入式负对照**：
- 幽灵球取 `c0 + u·d`（忽略 `2r`）⇒ **（a）FAIL**（偏差 2r = 57.15 mm）。
- 目标出射方向取 `u` 的**反向** ⇒ **（b）FAIL**（180°）。

### T77 **斜碰**（无库）闭式一致 —— 甲（**排期点名的情形之一**）

**构造**：白球 `c0`、单目标球 `t`，瞄准方向 `u` 使路径**偏离**球心连线（`sinφ = |cross(u, normalize(t−c0))| > 0`，取 `φ ≈ 5°`、`≈ 20°`、`≈ 30°` 三档，全部**仍能接触**：需 `d·sinφ ≤ 2r`）。

**判据**（闭式，**与速度无关** ⇒ 不受滚阻影响）：
- （a）`GhostBall` == 射线与"半径 `2r` 的圆（圆心 `t`）"的**首次交点**：
  `Δ = t − c0`；`s = Δ·u`；`per = |cross(u, Δ)|`；`s* = s − sqrt((2r)² − per²)`；`ghost = c0 + u·s*`（容差 **1e-6 m**）；
- （b）`TargetOutgoingDirection` == `normalize(t − ghost)`（**球心连线**；等质量无自旋下精确）夹角 **< 0.05°**；
- （c）`CueOutgoingDirection` == `normalize(u − ((1+e_ball)/2)·(u·n̂)·n̂)`，`n̂ = normalize(ghost − t)`（**等质量冲量的一半 + 法向 restitution**；切向分量不变）夹角 **< 0.05°**。`e_ball` **取自 `PhysicsTuning.asset`（契约常量 0.95）**，**不是**预测输出；
- （d）`CushionBounceCountBeforeContact == 0`。

**期望值来源**：**甲**（射线-圆求交 + 等质量弹性碰撞的**代数闭式**；**不**转写内核代码）＋ **乙**（`e_ball` 取资产常量值）。
**盲带**：不覆盖库边（`T78`）、不覆盖袋口；`0.05°` 对"解析近似 vs 真实 TOI"的分辨力有限（见注入）。
**判别力来源**：（b）（c）是**斜碰**的核心 —— 解析近似实现常把出射方向取成 `u` 或取成"球心连线但幽灵球用球心距"⇒ 方向偏差随 `φ` 增大而增大（`φ = 30°` 时 ≥ 数度）。
**注入式负对照**：
- 幽灵球改用"球心距 `d` 投影"（忽略横截 `per`，即 `s* = s`）⇒ **（a）FAIL**，且 **（b）FAIL**（连线方向被带偏）。
- 目标出射方向取 `u`（不做球心连线投影）⇒ **（b）FAIL**（`φ = 30°` 时偏差 30°）。
- 白球出射取**纯切向** `u − (u·n̂)n̂`（漏掉法向反弹项 `((1+e)/2)`）⇒ **（c）FAIL**（`e=0.95` 时法向项占大头）。

### T78 **擦库**（银行球）与实跑一致 —— 甲-结构 + 乙-同源（**排期点名的情形之二**）

**构造**：布置"白球 → 长库反弹 → 命中目标球"的**银行球**场景（瞄准方向刻意指向长库，使白球在接触目标球前**必须**先与库边碰撞）。

**判据（三条）**：
- （a）**甲-结构**：`HasFirstContact == true` ∧ `CushionBounceCountBeforeContact ≥ 1` ∧ `GhostBall` 到"直线段 `c0→t`"的距离 **> 阈值 `d_min`**（**待实测定值**，见 §4.1/§8.3）—— 即幽灵球**不在**直线上（证明预测**真的**跑了库边，而非解析直线）；
- （b）**乙-同源**：**两路都必须发生首接触**（`Predict` 的 `HasFirstContact == true` ∧ 实跑的 `Simulator.BallBallContactCount ≥ 1`），且由 `Predict` 得到的 `(GhostBall, TargetBallCenter, TargetOutgoingDirection, CueOutgoingDirection, TimeToFirstContactSeconds)` 与**生产路径实跑**（`PhysicsTestDrive.Launch()` + `Advance()` 直到 `Simulator.HasBallBallContact`）读到的**同名量**，**逐位相等**（0 容差；方向向量按 `ToString("R")` 逐分量）；
- （c）**非空转**：`StepsSimulated ≥ 1`。

> **★ 本判据的判别范围（写在判据内 —— 读判据者不必翻风险节）**：
> **`T78b` 的期望值与被测对象「同内核」** —— 它检的是**预测管线绑定正确性**（快照 / 参数 / 步长 / 触发 / 时间轴），
> **不能检出内核物理缺陷**（内核错则两路一起错）；**内核物理正确性由 Day 4–6 的 159 条判据承担**，本判据**不重复计数、不混淆两件事**。

**期望值来源**：（a）**甲**（直线距离的几何构造）；（b）**乙-同源**（**判别范围已写在上面**）。
**盲带**：见上"判别范围"框；另：`d_min` 是**选定门限**（待实测），只判"离直线足够远"，**不判具体落点**。
**判别力来源**：（a）把"**解析几何直线近似**"（排期明令禁止的路线）变成可判定失败；（b）把"步长 / 参数 / 时间轴不同源"变成可判定失败。
**注入式负对照（★ 主理人要求：第一条必须**同时**打红（a）与（b））**：
- **预测改用解析直线（沿瞄准方向直走、忽略库边）** ⇒ 在"银行球"几何下白球**打不到目标球** ⇒ `HasFirstContact == false` ⇒ **（a）FAIL**（缺 `≥1` 反弹）**且（b）FAIL**（缺"两路都发生首接触"与五个比对值）—— **两条同时红**；
- `Predict` 与实跑的**步长 / 参数不同源**（例如注入改变 `FixedDeltaTime` / `SubSteps`）⇒ **（b）FAIL**（数值不逐位相等）；
- 预测拿"实跑结束后"的位置当幽灵球 ⇒ **（b）FAIL**（时间轴错位）。

### T79 绘制坐标**过显示层**（`T71` 同族；**不得重犯直画规范坐标**）

**构造**：取 `PlayfieldRoot`（scale 1.2）+ 合成的**非 1.2 / 带平移 + 绕 Y 旋转**根（如 1.7× + `pos(0.3,−0.15,0.22)` + `rot 37°`），对幽灵球、路径折线两端、目标出射线端点，比较 `AimAssistView.DrawPoint(...)` 与三路期望：① `PlayfieldView.DisplayFromCanonical` ② `root.TransformPoint` ③ **独立闭式** `canonical × PlayfieldVisualScale`（仅 1.2 根）。

**判据**：
- （a）三路最大差均 **≤ 1e-6 m**（合成根只比 ①②）；
- （b）**源码守卫**：`AimAssistView.cs` **代码行**（跳过 `//` / `///`）不得出现字面量 `1.2`（同 `T71` 的守卫形态）；
- （c）`DrawScale(null) == 1f`（退化，不抛异常）。

**期望值来源**：**甲**（`PlayfieldView` 强制口径 = 引擎原语 = 独立闭式，三路独立 ⇒ 同源恒真不成立）。
**盲带**：无法捕获 `Gizmos.DrawLine` / `LineRenderer` 的**实际**顶点（引擎内部）⇒ "是否真的调用了 `DrawPoint`"由（b）的源码守卫 + 复核 grep 覆盖。
**判别力来源**：Day 8 切片 A 刚因"直画规范世界坐标"被修过一次（F-19）⇒ 本条是**防重犯**。
**注入式负对照**：
- `DrawPoint` 改为 `return canonicalWorldPoint;`（不过显示层）⇒ **（a）FAIL**。
- 代码行写死 `× 1.2f` ⇒ **（b）FAIL**（且合成根路必然错）。

### T80 虚线"清晰、不糊"的**可判定形式**

**构造**：纯函数 `BuildDashSegments(from, to, dash, gap)`，`L = |to − from|`。

**判据（四条，全部数值化，禁主观词）**：
- （a）**间隙非零**：`gapLength > 0`（⇒ 不是实线，"糊"的可判定反面）；
- （b）**段长/段数闭式**：段数 `n == max(1, ceil(L / (dash + gap)))`；每段长度 `≤ dashLength`，且除**末段**外每段长度 `== dashLength`（容差 1e-6 m）；
- （c）**与世界尺度成比例**：`AimAssistView.DrawScale(root) == PlayfieldVisualScale`（1.2 根），且"世界段长 == `dashLength_canonical × DrawScale(root)`"（容差 1e-6 m）；
- （d）**屏幕可辨下限**（**待实测定值**）：`gapLength_world × DrawScale(root)` **≥ `MinGapWorld`**，`MinGapWorld` = "在 16:9 基准取景下 ≥ 1 像素"所对应的世界米数（**实现后按实测填写，不得编数字**）。

**期望值来源**：**甲**（（b）的闭式计数）＋ **乙**（（c）的 `DrawScale` 与 `PoolConstants.PlayfieldVisualScale` 契约常量）＋ **乙-系数选定**（`MinGapWorld`）。
**盲带**：不判"**观感**好不好看"（主观，不作验收，`R3-4`）；只判"**非零间隙 + 段数闭式 + 随尺度**"。
**判别力来源**：把"清晰、不糊"翻译成**可失败的数值条件**（`gap=0` 即实线；尺度不跟随即 1.2 根下长度不变 ⇒ 屏幕变细/变糊）。
**注入式负对照**：
- `gapLength = 0`（拼成实线）⇒ **（a）FAIL**。
- 段长写死、不乘 `DrawScale` ⇒ **（c）FAIL**（1.2 根下世界段长 ≠ 1.2×）。

### T81 方向**单点** + **180° 反向回归守卫**

**判据**：
- （a）对 ≥3 个非轴向物理方向（含斜碰出射方向）：`AimAssistView` 用的世界方向 **== `PoolGeometry.PhysicsDirectionToUnity(physDir)`**（逐分量，容差 1e-6），且 **== `PointerAimSolver.WorldDirectionFromPhysics(physDir)`**；
- （b）**定值锚定**：`physDir = (cos30°, sin30°)` ⇒ 世界方向 == `(−cos30°, 0, −sin30°)`（**不是** `(cos30°, 0, sin30°)`）；
- （c）**源码守卫**：`AimAssistView.cs` / `AimAssistController.cs` 代码行**不得**手写 `(−x, −z)` / `(-x,-z)` 类映射（`grep` 无命中）。

**期望值来源**：**甲**（`PoolGeometry` 契约式，`F-20`）。
**盲带**：不覆盖"输入设备级拖拽是否真的改了方向"（Manual/PlayMode）。
**判别力来源**：`T47` 是 180° 反向的回归守卫；本条把预测侧也钉到**同一入口**，防"预测线方向自成一派"。
**注入式负对照**：
- 预测方向写成 `(cosθ, 0, sinθ)`（`5189d4d` **之前**的旧口径）⇒ **（a）（b）FAIL**，偏差 180°。
- 在 `AimAssistView.cs` 手写 `new Vector3(-d.x, 0, -d.y)` 替代入口 ⇒ **（c）FAIL**。

### T82 **开关**：关掉即不算、状态可观测、开回来恢复

**构造**：`AimAssistController` 在场景/合成宿主上，`drive.enableShotPrediction = true` → 记录 `PredictionComputeCount` = `n1`；置 `false` → 多次 `LateUpdate`（或直接多次调用其纯逻辑入口）→ 记录 `n2`；再 `false → true` → `n3`。

**判据（四条）**：
- （a）关闭期间 **`PredictionComputeCount` 逐位不变**（`n2 == n1`）—— **关闭即不算**（不得"只藏不算"）；
- （b）关闭期间 `Prediction` **逐位不变**（对幽灵球 / 目标出射方向 / `StepsSimulated` 取 `ToString("R")` 串，置 `false` 前后**相等**）；
- （c）**可观测**：`drive.PredictionEnabled` == `drive.enableShotPrediction`（单点读出口存在且真实反映；`AimAssistController` **不另存**开关副本）；
- （d）重新开启后 `n3 > n2`（**恢复计算**，非空转）。

**期望值来源**：**乙**（`PredictionComputeCount` 是**新增**诊断量；判据断言的是**变化/不变**，不是"值 == 自己"）＋ **契约**（开关语义）。
**盲带**：不覆盖真实 UI 菜单点击（设置项接口本日只做"预留"，见 `Q-17`）。
**判别力来源**：把"关掉仍每帧算"（最省事的实现：只不绘制）变成可判定失败。
**注入式负对照**：
- 把开关只用于"不绘制"而仍在 `LateUpdate` 里 `Predict` ⇒ **（a）FAIL**。
- 开关写反（关＝开）⇒ **（c）FAIL**。

### T83 **成本上限**（首次接触即停；正面回答 6746 量级）

**构造**：对**参考场景集**（直球 / 斜碰 / 银行球 / 4 球小堆）各调 `Predict`，读返回的 `StepsSimulated` / `BallBallContactCount` / `TotalContactCount`（`ShotPrediction` 的只读诊断出口），并记录**墙钟耗时**（`System.Diagnostics.Stopwatch`，**不**读 `Time`）。

**判据（四条）**：
- （a）**首次接触即停**：`prediction.BallBallContactCount == 1`（在 `HasFirstContact == true` 的场景）—— 预测**不得**跑到整盘静止；
- （b）**步数有上界**：每个场景 `StepsSimulated ≤ maxPredictSteps`（**待实测**填值），且未触发"无接触"分支时 `StepsSimulated` 有**实测值**记录；
- （c）**"无接触即停"**：构造一个白球打不到任何球的方向 ⇒ `HasFirstContact == false` 且 `StepsSimulated ≤ maxPredictSteps`（**有界**，不得无限跑）；
- （d）**成本量级**：把 `prediction.TotalContactCount` 与"一次**整盘满堆**开球的 6746"（F-26）并列记录，并断言预测侧 `TotalContactCount` **远小于** 6746（比值**待实测**记录；该断言的上界门限**待实测**填）。

**期望值来源**：**乙**（实跑计数）＋ **乙-系数选定**（`maxPredictSteps`、比值门限）。
**盲带**：**不**判"绝对性能是否达标"（无 CI、无固定机型）⇒ 只判"**有界** + **不跑到静止** + **量级远低于整盘**"；真实帧率留 **Manual**。
**判别力来源**：把"预测跑整盘"（成本爆炸）与"无接触时无限跑"（卡死）变成可判定失败。
**注入式负对照**：
- 把循环条件从"首接触即停"改成"跑到 `TableAtRest`" ⇒ **（a）FAIL** + **（d）FAIL**（`TotalContactCount` 逼近 6746）。
- 删掉步数上限 ⇒ 无接触场景 **（c）FAIL**（死循环 / 超时）。

### T72–T83 + T72b 汇总

| # | 判据 | 门槛（PASS 条件） | 期望值来源 |
| --- | --- | --- | --- |
| **T72** | 零扰动 | 预测前后在跑内核**逐位**一致 ∧ `StepsSimulated > 0` ∧ 幂等 | 乙（前后不变） |
| **T72b** | 诊断**单点 + 惰性**（`Q-14` K-1…K-4） | `BallBallContactCount++` 恰 2 处且记录只在 `ResolveBallBallContact` 内 ∧ `T32b`/`T37` **逐位不变** ∧ `ResetDiagnostics` 清 ∧ `T55` 仍 PASS | 乙（既有基线不变）+ 甲（源码计数） |
| **T73** | 同源参数/步长 | `Params`/`SubSteps`/终止/袋口**逐字段位相等** ∧ 同 `TableConfig` | 乙（只读出口） |
| **T74** | 快照语义 | `probe` 球数 == 在场上球数 ∧ 入袋记录不变 ∧ 反证清 `_frozen` | 甲（计数）+乙 |
| **T75** | 幽灵球定义 | `\|ghost − target\| == 2r`（1e-6 m）∧ 路径终点==幽灵球 ∧ 时间区间合理 | 甲（几何恒等式）+乙 |
| **T76** | 直球闭式 | `ghost == c0 + u(d−2r)` ∧ 出射 == u（<0.05°） | 甲 |
| **T77** | **斜碰**闭式（**验收**） | 射线-圆闭式 + 球心连线出射（<0.05°），三档 φ | 甲 |
| **T78** | **擦库**一致（**验收**） | 反弹≥1 ∧ 幽灵球离直线 > `d_min` ∧ 与实跑逐位一致 | 甲-结构 + 乙-同源 |
| **T79** | 绘制过显示层 | 三路 ≤1e-6 m ∧ 无字面量 1.2 ∧ 退化 | 甲 |
| **T80** | 虚线可判定 | `gap>0` ∧ 段数闭式 ∧ 随尺度 ∧ 屏幕下限 | 甲+乙+乙(选定) |
| **T81** | 方向单点/180° | == `PhysicsDirectionToUnity` ∧ `(cos30,sin30)→(−cos30,0,−sin30)` ∧ 无手写映射 | 甲 |
| **T82** | 开关键 | 关掉 `ComputeCount` 不变 ∧ 可观测 ∧ 恢复 | 乙 + 契约 |
| **T83** | 成本上限 | `BallBallContactCount==1` ∧ `Steps ≤ maxPredictSteps` ∧ 无接触有界 ∧ 远小于 6746 | 乙 + 乙(选定) |
| — | **EditMode 总数** | 全绿；总数 = 基线 **159 @ `f697378`** + 本 spec 新增 **13** 条（`T72`–`T83` 12 条 + `T72b` 1 条；**双向核对**，§9.1）；**终值待实测** | — |

> **「丙」= 0 声明**：本 spec §5 的期望值**无一条**为"转写被测代码公式"。
> 分类：**甲 = 独立闭式/几何构造/引擎原语**（`T72b` 源码计数、`T74` 计数、`T75a` 的 `2r`、`T76`、`T77`、`T78a`、`T79`、`T80`、`T81`）；
> **乙 = 契约常量/只读出口/独立实例实跑/既有基线不变**（`T72`、`T72b`、`T73`、`T74b`、`T75b`/`T75c`（内部一致性，弱）、`T78b`、`T82`、`T83`）；
> **乙-系数选定 = 选定门限**（`T80d` 的 `MinGapWorld`、`T83` 的 `maxPredictSteps`/比值门限）。
> **⚠ `T78b` 是「乙-同源」**（与在跑内核**同内核**的独立实例实跑）—— **判别范围已写在 `T78` 判据条目内**（不能检出内核物理缺陷，那由 Day 4–6 承担），
> **不**被引作"物理正确性"的证据。**`T75b`/`T75c` 是内部一致性**（弱判别），`T75a` 的 `2r` 恒等式才是绝对判别量。
> **⚠ 每条 = 一行注入式负对照**（§5 各条"注入式负对照"）：`T72`(就地用在跑内核/空转) / `T72b`(另开检测点 / 诊断微调位置 / 清零塞错处) / `T73`(SafeDefault 参数) / `T74`(传全集) / `T75`(Step 末端采样) /
> `T76`(漏 2r) / `T77`(解析近似出射) / `T78`(纯直线 ⇒ **结构+同源同时红** / 步长不同源) / `T79`(直画规范坐标) / `T80`(gap=0) / `T81`(旧 180° 口径) / `T82`(只藏不算) / `T83`(跑到静止)。
> **事前一击，非事后证明**（铁律 18）。

---

## 6. 证据清单

| 文件 | 内容 | 落盘位置 |
| --- | --- | --- |
| **无新验收器** | 本日**不新增**验收器（交互/表现层无独立场景几何不变量）⇒ **spec 只指向真实存在的证据**（Day 6 `D7-T01` 裁定）。守卫**全部进 EditMode 测试套件**（`R4-2`：本工程无 CI，验收器里的守卫 = 没人点菜单就不跑） | — |
| **`<outputDir>/d9-shot-prediction.json`** | 预测证据：逐场景 `aimDegrees`/`power`/`cueStart`、`ghostBall`、`targetBallCenter`、`targetOutDir`、`cueOutDir`、`stepsSimulated`、`ballBallContactCount`、`totalContactCount`、`elapsedMs`、`cushionBouncesBeforeContact`；`params`（**逐字段**）与 `subSteps`（**从只读出口读**）；`inPlayCount`；`predictionComputeCount`。**必填字段**见 §6.1 | **测试**经 `ReportOutput.ResolveTestEvidencePathOrNull` 写到 **`-outputDir`**（仓库外）；**未给 `-outputDir` ⇒ 只跳过落盘、不跳过任何断言**（`R3-5`/`R3-7`） |
| **不落盘项（明示）** | ① 屏幕截图/人工目视"虚线清不清晰"（Manual）；② 输入设备级拖拽（Manual/PlayMode）；③ 真实帧率（Manual）；④ §4.1 的"待实测定值" | — |

### 6.1 必填字段（`d9-shot-prediction.json`）

`schema`、`generatedAtUtc`、`unityVersion`、`projectRoot`、`isBatchMode`、`passCount`/`warnCount`/`failCount`、`overallStatus`、`checks[]`；
每个 `checks[]` 含 `id`、`name`、`expected`、`actual`、`detail`、`status`。
**数值一律 `ToString(InvariantCulture)` 显式调用**（`R3-8`）。

---

## 7. 风险与降级

### 7.1 风险登记（续 Day 8 `R-AG`，自 `R-AH` 起）

| # | 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- | --- |
| **R-AH** | ⭐ **幽灵球取到"Step 末端"而非"接触瞬间"** ⇒ 擦碰时偏差达 37.5 mm（> 球半径），且 `\|Δ\| ≠ 2r`；**而"存在/非空"型断言恒过** ⇒ **静默错**。**本 spec 判定这是最可能出错的一处** | **高** | **高（正是排期验收句）** | §3.1 内核只读诊断 + `T75`（`2r` 几何恒等式）；注入 = 改回 Step 末端采样 ⇒ 必红 |
| **R-AI** | **快照"复活"已入袋球**（`LoadBalls` 清 `_frozen`/`_capturedPocket`，F-4）⇒ 预测世界里袋球回到场上 | 中-高 | 高 | `T74`（球数 == 在场上球数 + 反证）+ §3.2 步 2 |
| **R-AJ** | **预测误用在跑内核实例** ⇒ 扰动在跑内核（球位/计数被推演改掉） | 中 | 高 | `T72`（前后逐位一致 + 非空转）+ §3.3 源码守卫（禁调 `Step/PlaceBall/LoadBalls`） |
| **R-AK** | **参数/步长不同源**（预测用 `SafeDefault` / 默认 `captureScale=1`）⇒ 预测与实跑静默分叉 | 中 | 中-高 | `T73`（逐字段位相等） |
| **R-AL** | **预测每帧重算 / 跑整盘**（成本爆炸；F-26 的 6746 量级） | 中 | 中-高 | `T83`（首接触即停 + 上限 + 远小于 6746）；`AimAssistController` 脏标记（§3.3）；**成本口径见 §7.2** |
| **R-AM** | **绘制直画规范坐标 / 第二处转换**（重犯 `T71` 修过的缺陷，F-19） | 中 | 中 | `T79`（三路一致 + 字面量 1.2 源码守卫）；`T81`（方向单点） |
| **R-AN** | **用解析几何近似**（直线/球心距）替代内核推演 ⇒ **银行球/斜碰**预测错 | 中 | 高 | `T77`（斜碰闭式）+ `T78`（擦库：反弹≥1 ∧ 幽灵球离直线 > `d_min`）；注入 = 纯直线 ⇒ 必红 |
| **R-AO** | 虚线**糊成实线**（`gap` 太小 / 不随显示尺度） | 低-中 | 低 | `T80`（`gap>0` ∧ 段数闭式 ∧ 随尺度 ∧ 屏幕下限 `MinGapWorld`） |
| **R-AP** | **离台（frozen）球被排除** ⇒ 白球撞"袋里的球"窗口与实跑分叉 | 低 | 低-中 | 已声明为**盲带**（§3.2 步 2 / `T74`）；场景刻意不含"贴近袋口的入袋球"；**不**声称全场景等价 |
| **R-AQ** | `PoolSimulator` 新增诊断扰动既有内核判据（`T20/T23/T24a/T26` 计数类） | 低-中 | 中 | 新增**只读**、**不改解算路径**、**不改既有计数**；落地后必须重跑**全量** EditMode（`G2`） |

**降级说明（如实）**：
- `T76`/`T77` 的 `1e-6 m` 与 `0.05°` 均取自**几何恒等式 + float 级联**，**远小于**任何"定义错误"的量级（`2r` = 57.15 mm；Step 末端 = 37.5 mm；方向错误 ≥ 度级）⇒ 判别力充足。
- `T78b` 的**同源盲带**无技术手段消除（Day 9 的设计就是"同一内核"）⇒ **明示**，不作"物理更准"的证据。
- `T83` 的绝对性能门限**不设**（无 CI/固定机型）⇒ 只设"有界 + 不跑到静止 + 量级远低于整盘"，真实帧率留 Manual。

### 7.2 对 F-26「6746」量级的正面回答

- **6746** = 一次**整盘（15 球）满堆、上界初速 8 m/s 开球、跑到整盘静止**的接触解算总次数（出处与"**非仓库冻结值**"的告诫见 F-26；本 spec 只作**量级参照**）。
- **预测的成本口径与之不同（更低）**：预测循环**在首次球-球接触即停**（`T83a`），**不跑整盘**。对常规一杆，白球从起点到首接触通常 `0.2–0.6 s`（24–72 个 `Step` @120 Hz）；银行球更长但仍有步数上限。
- ⇒ 预测侧的 `TotalContactCount` **量级 ≤ 首接触前白球与库边的接触数 + 若干球-球 TOI 候选扫描**，**远低于 6746**（比值**待实测**记录，`T83d`）。
- **每帧成本**：`AimAssistController` 用**脏标记**（仅方向/力度/球状态变化才重算）；静止瞄准时**不重算**。⇒ 拖拽瞄准时的重算频率 ≈ 输入事件频率，而非渲染帧率。
- **开关**：关闭 ⇒ **零成本**（`T82`）。

---

## 8. 交付物与入口分离

| # | 交付物 | 类型 |
| --- | --- | --- |
| 1 | `Runtime/Physics/PoolSimulator.cs`（**改**：新增只读诊断 `BallBallContactRecord` / `HasBallBallContact` / `FirstBallBallContact`，记录点 = `ResolveBallBallContact` **函数入口**（`Q-14` K-1 单点），并纳入 `ResetDiagnostics()`；**不改任何解算路径**） | 运行时代码（`Q-14` 已批准） |
| 2 | `Runtime/Physics/ShotPredictor.cs`（**新增**；纯静态推演核） | 运行时代码 |
| 3 | `Runtime/Physics/ShotPrediction.cs`（**新增**；结果值类型） | 运行时代码 |
| 4 | `Runtime/Physics/AimAssistController.cs`（**新增**；胶水：读开关 / 触发 / 缓存 / 只读出口） | 运行时代码 |
| 5 | `Runtime/Physics/AimAssistView.cs`（**新增**；纯几何 + 绘制，过显示层单点） | 运行时代码 |
| 6 | `Runtime/Physics/PhysicsTestDrive.cs`（**改**：新增 `enableShotPrediction` 字段 + `PredictionEnabled` 读点属性，与 `aimIndicator*` 同处；**不动任何物理/交互语义**） | 运行时代码（`Q-17` 已裁定） |
| 7 | `Editor/Visual/GreyboxSceneSetup.cs`（**改**：生成/接线 `AimAssistController` / `AimAssistView` 宿主；**只生成不校验**，`R2`） | 编辑器脚本 |
| 8 | 灰盒场景 `Assets/_Project/Scenes/PhysicsGreybox.unity`（**改**：由生成器重建 + 序列化 `enableShotPrediction`） | 场景 |
| 9 | `Tests/EditMode/PoolSimulatorBallBallContactDiagnosticTests.cs`（**新增**；`T72b` = **1** 条；白盒单点 + 既有基线逐位不变） | 测试 |
| 10 | `Tests/EditMode/ShotPredictorTests.cs`（**新增**；`T72`–`T78` + `T83` = **8** 条） | 测试 |
| 11 | `Tests/EditMode/AimAssistViewTests.cs`（**新增**；`T79`–`T82` = **4** 条） | 测试 |
| 12 | `Docs/35-A_Day9_瞄准辅助与击球预测_执行记录.md` | 执行记录 |

### 8.1 入口分离（沿用既有铁律）

| 入口 | 职责 | 禁止 |
| --- | --- | --- |
| `GreyboxSceneSetup` | 只生成灰盒场景 | 不得调用校验；不得写 `EditorBuildSettings`；不得改 `Main.unity` |
| （本日**无**新验收器） | — | — |

### 8.2 协调项（**先于编码**）

- **C-2**：`PoolSimulator.cs` 是本日**触碰的既有内核文件**（新增只读诊断，`Q-14` 已批准）。它是全工程共享资产 ⇒ **改动前须确认无他人占用**；落地后**必须**：① 重跑全量 EditMode（`G2`）+ 四验收器；② 核 `T32b` / `T37` 的**接触数与逐球终态逐位不变**（`Q-14` K-2）；③ 核 `T55` 仍 PASS（K-4）；④ 核既有计数判据（`BallBallContactCount` / `TotalContactCount` / `EnergyGainViolations`）**不受影响**（`R-AQ`）。
- **C-3**：`GreyboxSceneSetup` + 场景重建会改动 `PhysicsGreybox.unity`（与 Day 8 切片 A 同族操作）⇒ 生成后须核对 `PlayfieldRoot` / 球可视物 / `CushionGizmo.playfieldRoot` 接线**未漂移**（`T45`/`T71` 仍绿），并核 `T43`（场景序列化值 == 代码默认）仍绿。
- **C-4（★ 实现拆分，主理人定：两单**必须串行**）**：12+1 条判据对**一单太大**（本项目多次出现 worker 预算耗尽半途停下）⇒ 拆为
  - **D9-A** = `T72` / `T72b` / `T73` / `T74` / `T75` / `T76`（**内核只读诊断 + 预测核**：前向模拟 / 零扰动 / 诊断惰性 / 快照语义 / 幽灵球 / 直球闭式）；
  - **D9-B** = `T77` / `T78` / `T79` / `T80` / `T81` / `T82` / `T83`（**出射线 / 擦库 / 绘制 / 开关 / 性能**）。
  - ⚠ **两者必须串行，不得并行**：D9-A 与 D9-B **同动 `PoolSimulator.cs`**（D9-A 引入只读诊断、D9-B 的 `T78b` 依赖它）与**同一个预测/绘制落点**（`ShotPredictor` / `AimAssistView` 的几何口径）⇒ 并行会造出**两处实现**（铁律 16）。**D9-B 必须等 D9-A 的 `PoolSimulator.cs` 冻结后再开工**。

### 8.3 需实测定值（**实现后按实测填写，不得编数字**）

| 项 | 用途 | 填值时机 |
| --- | --- | --- |
| `maxPredictSteps` | 预测步数上限（`T83b/c`） | 实测"银行球首接触所需步数"后取 ≥2× 余量 |
| `PredictionDashLength` / `PredictionGapLength` | 虚线几何（`T80`） | 实测屏幕可辨后填 |
| `MinGapWorld` | 屏幕 ≥1 px 对应的世界米数（`T80d`） | 由 16:9 基准取景实测 |
| `d_min` | 银行球"幽灵球离直线"的下界（`T78a`） | 由银行球场景实测 |
| 参考场景的 `ghostBall` / 方向 / `StepsSimulated` / `TotalContactCount` / `elapsedMs` | 证据数值（§6.1） | 实跑记录 |
| `TotalContactCount` 与 6746 的比值门限 | `T83d` | 实测后填 |

---

## 9. 对既有质量门的影响

| 影响对象 | 变更 | 处理 |
| --- | --- | --- |
| `PoolSimulator.cs` | **新增只读诊断**（`Q-14` 已批准；记录点在 `ResolveBallBallContact` 入口，K-1 单点）；**解算路径不变** | 落地后重跑全量 EditMode + 四验收器；核 `T32b`/`T37` 逐位不变（K-2）、`T55` 仍 PASS（K-4）、`ResetDiagnostics` 语义（K-3）；核 `BallBallContactCount` / `TotalContactCount` / `EnergyGainViolations` / 终止判据**不受影响** |
| `PoolGeometry.cs` / `PlayfieldView.cs` / `CushionGizmo.cs` / `PointerAim*` / `CameraViewportFitter.cs` | **不改** | `T45`–`T53` / `T71` 仍绿 |
| `PhysicsTestDrive.cs` | **改 1 处**：新增 `enableShotPrediction` 字段 + `PredictionEnabled` 读点属性（与 `aimIndicator*` 同处，`Q-17`）；**`Launch()` / `Advance()` / `SyncVisual()` 语义一字不改**；预测只读 `Simulator` | `T65`–`T70` 仍绿；`T43`（**显式字段清单**，不含新字段）仍绿 —— 是否扩面覆盖该 bool 见 §3.5 注（**待指认**） |
| `GreyboxSceneSetup.cs` + 场景 | 增补接线 + 重建 | 生成器**仍只生成不校验**（`R2`/`R5-4`）；不得写 `EditorBuildSettings` |
| asmdef | **无需改动**（新运行时代码落 `Billiards.Runtime`；新测试落 `Billiards.Tests.EditMode` 同目录自动纳入） | 若新增子目录，`.meta` 与 `rootNamespace` 须一致 |
| PhysX 守卫 | 新运行时代码**不得**出现 `Rigidbody`/`Collider`/`PhysicsModule`（`R5-1`/`D4-PHY-01`） | 复用既有汇编级守卫 |

### 9.1 台账同步（同步至 `Docs/测试数基线台账.md`）

> **基线（本 spec 定稿时，实测）**：**EditMode = 159 @ `f697378`**（源码 `^\s*\[Test\]` 逐文件求和；
> 逐文件：`CameraViewportFitterTests`2 / `CushionGizmoDisplayTests`1 / `GreyboxSceneTests`5 / `GreyboxTuningWiringTests`5 /
> `MeshAlignmentAuditTests`6 / `MeshAlignmentSensitivityTests`5 / `ModelOrientationTests`5 / `PhysicsBanGuardTests`5 /
> `PhysicsTestDriveLaunchStateTests`6 / `PointerAimControllerTests`2 / `PointerAimTests`5 / `PoolConstantsTests`13 /
> `PoolSimulatorBallBallTests`21 / `PoolSimulatorIntegralFormatTests`6 / `PoolSimulatorInvariantTests`2 /
> `PoolSimulatorPocketAndMultiBallTests`4 / `PoolSimulatorPocketCaptureTests`12 / `PoolSimulatorPocketIndependenceTests`3 /
> `PoolSimulatorSegmentCoverageTests`1 / `PoolSimulatorTerminationAndBreakTests`7 / `PoolSimulatorTests`4 /
> `PoolSimulatorTwoStageRollingTests`7 / `TableConfigTests`24 / `TableImporterErrorPathTests`8 ⇒ **合计 159**）。
> **四验收器基线**：Day1/2/3/4 = **25 / 33 / 22 / 7**（本日**不新增验收器** ⇒ 期望 ±0）。

| 项 | 基线（带修订锚点） | 增量 | 新值（**待实测**） |
| --- | --- | --- | --- |
| EditMode 测试 | **159 @ `f697378`** | **+13**（`T72`–`T83` 12 条 + `T72b` 1 条；一条判据一个 `[Test]`，落 `PoolSimulatorBallBallContactDiagnosticTests.cs`（`T72b` = **1**）+ `ShotPredictorTests.cs`（`T72`–`T78` + `T83` = **8**）+ `AimAssistViewTests.cs`（`T79`–`T82` = **4**）） | **待实测填写** |
| 四验收器 | 25 / 33 / 22 / 7 | **±0**（不新增验收器） | **待实测填写**（FAIL=0） |

> **双向核对**：源码 `grep -cE "^\s*\[Test\]"` 逐文件求和 == Unity 实跑 XML `testcasecount`。**终值留空待实测，不得编数字。**

---

## 10. 复核安排

| 阶段 | 责任 | 内容 |
| --- | --- | --- |
| 独立评审（可验证性） | 复核方 | ① 逐条核对 §5 期望值**分类**（甲/乙/乙-系数选定/**乙-同源**/丙），确认 **丙 = 0**；② 核 `T75` 是否**真能打死**"Step 末端采样"（改回后 `\|Δ\| ≠ 2r` 应 FAIL）；③ 核 `T78` 的"解析直线"注入**确实同时**打红 (a) 与 (b)（**不得**被"找不到接触也算过"的写法掩盖）；④ 核 `T80` 的"清晰"未退化为**主观词**；⑤ 核 `T83` 未写成"绝对性能达标"（无 CI ⇒ 不可达条款）；⑥ 核 `T78b` 的**判别范围（同源盲带）确实在判据条目内** |
| 独立评审（可实现性） | 复核方 | ① `Q-14` 新增诊断是否**最小**、K-1…K-4 是否可落地；② §3.2 步 2 的快照语义（排除离台球）是否与 `T74` 自洽；③ 找出"照做会卡住"之处（**跨文件项 = `PoolSimulator.cs` 的只读诊断 + `PhysicsTestDrive.cs` 的开关字段**，C-2/C-3） |
| 独立复算（G5） | 复核方 | **不共享代码路径**地重算：① `T76`/`T77` 的**闭式**幽灵球（取 3 组几何，手算 + 独立脚本）；② `T75` 的 `2r` 恒等式在 float 下的残差量级；③ `T80` 的段数闭式 |
| 实现（**拆两单、串行**，C-4） | engineering-lead | **D9-A**（先在**冻结前**交付）：§8 交付物 1（内核诊断）+ 2 + 3 + 9 + 10（`T72`/`T72b`/`T73`/`T74`/`T75`/`T76`）；**D9-B**（**等 D9-A 的 `PoolSimulator.cs` 冻结后**）：4 + 5 + 6 + 7 + 8 + 11（`T77`–`T83`） |
| 实现方自验 | engineering-lead | 跑通 `T72`–`T83` + `T72b`（共 **13**），产出 §6 证据（`-outputDir` 指向**仓库外**）+ 全量 EditMode 回归；**D9-A 收口必须附 `T32b`/`T37` 逐位不变的实测** |
| 独立复核 | 复核方 | ① 冻结锚点核验（`git diff <提交> -- <冻结路径>` 为空）；② 复现 `T72`/`T75`/`T78`/`T82` 的注入反例**确实 FAIL**；③ 核 `T73`/`T74` 的"同源/计数"确被断言；④ 核无**第二处**坐标/方向转换（`grep`）；⑤ 核 `T83` 的实测成本记录 |

**复核必须能失败**：若把幽灵球改回"Step 末端采样"而 `T75` **仍通过** ⇒ `T75` 无效；
若把 `Predict` 改成就地用在跑内核而 `T72` **仍通过** ⇒ 零扰动守卫是空气；
若把首接触记录**另开**一个检测点 / 让诊断动一次位置而 `T72b` **仍通过** ⇒ 惰性守卫是空气；
若把预测换成解析直线而 `T78` **仍通过** ⇒ `T78` 无效（说明它读的是被测对象自身）。

---

## 11. 裁定与待裁定

> **已裁定（主理人已批准/裁定）**：`Q-14`（**允许**，附 **K-1…K-4** 四条硬约束）/ `Q-15` / `Q-16` / `Q-17`（**最小形态、落驱动器组件**）/ `Q-18` / `Q-19`（**采纳**"甲-结构 + 乙-同源"，**不**做额外解析反射闭式）。
> **仍待指认**：`Q-20`（`T43` 是否扩面覆盖新 bool —— 属**扩面**、非阻断）。

### 11.1 已裁定

| ID | 议题 | **裁定 / 决定** | 落地位置 |
| --- | --- | --- | --- |
| **Q-14** | 幽灵球"接触瞬间"如何取得 | **在 `PoolSimulator` 新增只读诊断**（`BallBallContactRecord` + `HasBallBallContact` + `FirstBallBallContact`），记录**首次球-球接触、冲量施加前**的两球位置与冲量后速度。**主理人已批准**（理由：① 同族诊断已有先例、`ResetDiagnostics` 已统一管理；② 不做则排期任务 2 成不可达条款；③ 闭式 TOI 替代 = 第二处接触几何，否；④ 不改解算路径）。**附 K-1…K-4 四条硬约束**（单点 / 惰性逐位 / 清零语义一致 / `T55` 回归） | §3.1（K-1…K-4）、§4.2 契约 4、`T72b`、`T75` |
| **Q-14b** | 快照里**已入袋/离台球**如何表示 | **不表示（排除）**。理由：内核**无**公开"重新冻结"入口（`LoadBalls` 必清 `_frozen`、`PlaceBall` 必解冻，F-4/F-5）⇒ 带 frozen 装载**结构上不可达**；离台球速度恒 0 且在喉平面外 ⇒ 不参与运动。**偏差登记为盲带**（`R-AP`） | §3.2 步 2、`T74`、`R-AP` |
| **Q-15** | 虚线的表现层实现 | **`LineRenderer`**（**每条 dash 一段 2 点折线、多段拼虚线**，不依赖贴图/材质宽度曲线）+ `OnDrawGizmos` 的 Scene 预览。挂在 `PlayfieldRoot` 下、`useWorldSpace=false`（局部坐标 = 规范坐标 ⇒ 天然过显示层） | §3.4 |
| **Q-16** | 绘制落点是否复用 `CushionGizmo.DisplayPoint` | **复用**（它是"显示映射"唯一落点的**公开包装**，已被 `T71` 钉住）⇒ 满足铁律 16 单点。**替代方案**（抽出独立 `PlayfieldDraw` 静态类）**未采纳**：会改动 `CushionGizmo` 并牵动 `T71` | §3.4、`T79` |
| **Q-17** | "设置项预留接口"的形态 | **主理人裁定：最小形态，不建独立 ScriptableObject 资产**（排期原文是"预留接口"，不是现在建设置系统；Day 13 才是最小 HUD）。**字段落点 = `PhysicsTestDrive`（驱动器组件）**，与既有 `aimIndicator` / `aimIndicatorLength` / `aimIndicatorThickness` 同处（同属"表现/辅助"而非物理参数）；**不得**塞进 `PhysicsTuning`（那会污染物理手感语义、且被 `T44` 逐字段冻结判"偏离台账"）。"预留接口" = `enableShotPrediction` **单一字段** + `PredictionEnabled` **单一读点属性** | §3.5、`T82`、§8 交付物 6 |
| **Q-18** | 预测的刷新时机 | **仅 `TableAtRest` 时**重算与显示（球运动期间**隐藏且不重算**，与 `F-14` 同口径）；**脏标记**（方向/力度/球状态变化才重算） | §3.3、`T83` |
| **Q-19** | **"擦库"验收的期望值同源问题** | **主理人裁定：采纳"甲-结构 + 乙-同源（明示同源盲带）"，不做额外解析反射闭式**。理由（与"否掉闭式 TOI"同一条原则，避免对同一原则两套标准）：① Day 9 验收问的是**预测管线**对不对，**不是**内核物理对不对（内核物理由 Day 4–6 的 159 条负责，再抓 = 重复计数 + 混淆两件事）；② 若为擦库另立解析反射闭式，就与"否掉幽灵球闭式 TOI"自相矛盾。**★ 附加要求已并入**：① 判别范围（同源盲带）**写进 `T78` 判据条目内**；② "解析直线"注入必须**同时**打红（a）与（b） | `T78`（判据内判别范围框 + 注入第 1 条） |
| **Q-14-K** | 主理人对内核诊断的四条硬约束 | 见 §3.1 表 K-1…K-4（**单点** / **惰性逐位不变** / **`ResetDiagnostics` 语义一致** / **`T55` 回归且不得放宽**），由 `T72b` 钉 | §3.1、`T72b` |

### 11.2 仍待 **team-lead / 主理人指认**（不猜）

| ID | 议题 | 现状 | 需要 |
| --- | --- | --- | --- |
| **Q-20** | 新字段 `enableShotPrediction` 是否纳入 `T43`（场景序列化值 == 代码默认）的覆盖？ | `T43` 是**显式字段清单**（不含新字段）⇒ 该 bool 的"场景漂移"**目前无守卫**。本 spec **不**自行扩面（它是表现开关、非手感入口；漂移后果 = 预测线默认隐藏/显示，非物理） | 若要求覆盖，需给 `T43` **加一行**（`Assert.AreEqual(probe.enableShotPrediction, drive.enableShotPrediction)`）—— 属**扩面**，**不影响**任何既有判据的强弱。**请指认是否要加** |

---

## 附：执行注意事项（本机环境，防踩坑）

1. **Unity 批处理必须钉死国际版路径**：`E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`
   （**不要**用 `E:/Unity_Deve/Unity_Tools/Editor/Unity.exe`＝中国版 ⇒ 会按 `packages.unity.cn` 强制重解析，
   破坏 `Library/PackageCache` 并改写 `packages-lock.json`）。见 `工程标准.md §7 第 9 条` 与 `§8 G8`。
2. **单实例**：跑批处理前确认 `Temp/UnityLockfile` 不存在。
3. **`-runTests` 不带 `-outputDir` 什么都不写**；要落证据必须**显式带** `-outputDir` 并指向**仓库外**（`ReportOutput` 单点，F-24）。
4. **跑前跑后各查一次** `git status --porcelain`（尤其 `Packages/packages-lock.json`）。
5. **本 spec 的提交**用 **path-limited**：`git commit -m '…' -m '…' -- Docs/35-A_Day9_瞄准辅助与击球预测_spec.md`
   （`-m` 必须在 `--` 之前；**不得** `git add -A`、**不得**动 `Docs/Reports/**` / `Docs/Reviews/**` 与他人文件）。
6. **`Runtime` 源码里不得出现 `Collider` / `Rigidbody` 名字**（`D4-PHY-01` 扫名 + 汇编级 PhysX 守卫）。

---

## 附录 A：本 spec 与既有文档的锚点

| 关联 | 文件 |
| --- | --- |
| 排期 | `Docs/35-A_击球手感_制作排期.md`（§Day 9 `:220-230`） |
| 同日前篇（格式范本） | `Docs/35-A_Day8_相机与指针瞄准_spec.md` |
| 坐标契约 | `Docs/adr/ADR-0001-坐标系与手性约定.md` |
| 标准 | `Docs/工程标准.md`（§1 分层 / §3 测试要求 / §4 守卫形态 / §5 架构铁律 / §6 评审门 / §7 错误模式 / §8 自查清单） |
| 铁律 | `Docs/项目纪律铁律.md`（10 / 11 / 16 / 17 / 18） |
| 注释规范 | `Docs/注释规范.md` |
| 台账 | `Docs/测试数基线台账.md` |
| 内核 | `Runtime/Physics/PoolSimulator.cs`（`LoadBalls` `:422` / `PlaceBall` `:520` / `IsBallInPlay` `:550` / `ResetDiagnostics` `:464` / 只读出口 `:277`–`:293`） |
| 交互层 | `Runtime/Physics/PointerAimSolver.cs` / `PointerAimState.cs` / `PointerAimController.cs` |
| 显示层 | `Runtime/Core/PlayfieldView.cs` / `Runtime/Physics/CushionGizmo.cs`（`DisplayPoint` `:113` / `DisplayScale` `:127`） |
| 既有守卫 | `Tests/EditMode/CushionGizmoDisplayTests.cs`（`T71`）/ `GreyboxTuningWiringTests.cs`（`T41`–`T44`） |
