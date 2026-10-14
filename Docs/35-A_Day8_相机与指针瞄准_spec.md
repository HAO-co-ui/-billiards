# 35-A Day 8 — 相机与指针瞄准 · 实施 spec

| 项 | 值 |
| --- | --- |
| 版本 | **v1**（定稿级；待独立评审后可修订） |
| 状态 | **待实现**（未写代码） |
| 卡 | 35-A 击球手感验证（P0） |
| 排期依据 | `Docs/35-A_击球手感_制作排期.md` **§Day 8（`:206-216`）** |
| 前置 | 物理内核 I–III（Day 4–6）+ 参数外置与键盘可打（Day 7）已交付；灰盒场景 `PhysicsGreybox.unity` 可打 |
| 坐标真源 | **ADR-0001**（`Docs/adr/ADR-0001-坐标系与手性约定.md`）；唯一坐标系转换点 = `Runtime/Core/PoolGeometry.cs` |
| 数据真源 | `PoolConstants`（米制硬指标）、`TableConfig.asset`（`footSpot` / 内库）、`Assets/_Project/Settings/Input/Billiards.inputactions` |
| 判据编号 | 自 **`T45`** 起。**`T43` / `T44` 已被占用**（`Tests/EditMode/GreyboxTuningWiringTests.cs` = 场景漂移 / 出厂口径冻结）⇒ **本 spec 用 `T45`–`T53`**（Day 6 用到 `T37`、`T38`；Day 7 用到 `T39`/`T39b`/`T40`/`T41`/`T42`） |
| 待裁定 | 见 **§13**（`Q-8`…`Q-13`；含 2 项**需用户指认**的外部引用） |

> **阅读约定**：本文件是**排期产物**，正文里 `Day N` 是**主题词汇**，有意义。
> **但文中的代码片段，其注释立即适用 `Docs/注释规范.md`** —— 代码注释里不得出现开发日/批次标签；
> 检查项 ID、标准编号、提交哈希、实测数值**必须保留**。
>
> **修订 v1.1（据 `HEAD = 7f76dba`）**：① 判据编号**整体 +2**（`T43`–`T51` → **`T45`–`T53`**，避让 `GreyboxTuningWiringTests` 已占用的 `T43`/`T44`）；② `Q-11`（瞄准方向 180°）**已由 `5189d4d` 落地** ⇒ 本 spec 该条改为**回归守卫**（勿重复改）；③ 协调项 `C-1` **已解除**（`950c78f` 场景重建 + `5189d4d`）；④ `F-10` 序列化值陷阱**已关闭**（场景重建后同步，由 `T43` 守）；⑤ EditMode 基线 `133 @ a969f64` → **`135 @ 7f76dba`**；⑥ §2.2 行号按**新场景**重核。

---

## 0. 本轮变更摘要

### 0.1 权威范围（照抄排期，不放宽）

> **Day 8 — 相机与指针瞄准（8 h）**
>
> | # | 任务 | 工时 |
> | --- | --- | --- |
> | 1 | `PlayfieldRoot` 视觉缩放 1.2，物理坐标 ↔ 视觉坐标反向映射 | 1.5 h |
> | 2 | 指针射线拾取台呢平面 → 白球指向点 → 方向向量 | 2 h |
> | 3 | 瞄准交互：拖拽瞄准 / 松开锁定方向（对齐设计说明第 2 条） | 2.5 h |
> | 4 | 方向键微调（粒度约 0.2°） | 1 h |
> | 5 | 视口适配：16:9 基准 + 窄窗回落（参考 `browser-qa` 的多尺寸策略） | 1 h |
>
> **验收**：指针方向与白球朝向一致无偏移；窄窗不裁切球桌

**验收语气不改写、不放宽**。本 spec 的 §5/§6 是把它**落成可判定判据**的方式（不是替换更弱的目标）。

### 0.2 一句话概括本轮要点

把**指针的位置**（屏幕像素）经**相机 → 显示世界 → 规范世界（去 1.2）→ 物理二维平面**唯一链路，变成**物理平面上的瞄准方向**，并让**白球的出射方向**与它**一致、无偏移**；同时保证**窄窗不裁切球桌**。

### 0.3 本轮**不动**的东西（先声明，防越界）

- **不改** `PoolGeometry.cs` 的坐标映射式（`PhysicsToUnity = (−px, h, −py)`、`UnityToPhysics = (−x, −z)`）—— 它是已冻结的契约。
- **不改** `TableConfig` / `PoolSimulator` 的碰撞与物理解算（本日不碰物理内核）。
- **不改** 任何 `Docs/Reports/**` / `Docs/Reviews/**`。

---

## 1. 范围与目标 / 明确不做（非目标）

### 1.1 目标（= §0.1 的 5 条）

1. 引入 `PlayfieldRoot`（视觉缩放 `1.2`），并给出**唯一**的「显示 ↔ 规范」映射（**反向映射**供指针拾取）。
2. 指针**射线拾取台呢平面**，得到物理平面上的**指向点**，进而得**方向向量**。
3. **拖拽瞄准 / 松开锁定方向**的交互（按下跟随指针、松开冻结方向）。
4. **方向键微调**，粒度**约 0.2°**。
5. **视口适配**：16:9 基准 + 窄窗回落（窄窗不裁切球桌）。

### 1.2 明确不做（**越界即算未完成**）

| 不做 | 归属 | 依据 |
| --- | --- | --- |
| **瞄准辅助线 / 幽灵球 / 目标球出射预测线 / 预测线开关** | **Day 9** | 排期 `:220-231`；本日**只做"方向"，不做"预测"** |
| **力度控制（力度条 1:1 / 力度→初速曲线）与球杆程序动画** | **Day 10** | 排期 `:234-243` |
| **击球状态机**（`aim→charging→shooting→moving→settle`）的全量 | **Day 10** | 排期 `:241`；本日只做 `aim` 段的输入→方向，不做 `charging/shooting` |
| **音频 / VFX / HUD / 结算 / 埋点** | Day 11–14 | 排期 `:247-298` |
| **相机自由控制**（轨道 / 缩放 / 平移 / 切镜） | **不在本日 5 条内** | 本日的"相机"= **视口适配**（子任务 5），不是相机操控 |
| **`Main.unity` 的 1.2 视觉缩放落地** | **本轮不做**（见 `Q-13`） | `MaterialVisualValidator` 的 `SCN-02`（`:233-242`）断言 `Main.unity` 的 `PlayfieldRoot` 缩放 **= 1**；本日只动灰盒 |
| **白球落袋重置 / 自由球摆位** | 后续交互层 | 排期 §二"最小例外" |
| **端到端输入设备级验证**（真的按鼠标） | 手动 / PlayMode | EditMode 只测**纯逻辑**（§5 `T50` 盲带声明） |

> **⚠ 边界铁律**：本日**只交付**「指针 → 方向」与「视口适配」。任何"顺手把 Day 9 的预测线也做了"的改动，**按越界处理**（排期 §五 逐日范围不可增删）。

---

## 2. 前置事实（**每条给 `文件:行号`**）

> 本 spec 依赖的既有实现。**读不到位的实现方不得开工**。

### 2.1 坐标与常量

| # | 事实 | 出处 |
| --- | --- | --- |
| F-1 | **唯一坐标系转换点**：`PoolGeometry`。点映射 `PhysicsToUnity(px,py,h) = (−px, h, −py)`、逆 `UnityToPhysics(w) = (−w.x, −w.z)`；**方向入口 `PhysicsDirectionToUnity(d) = (−d.x, 0, −d.y)`**（`5189d4d` 新增）—— 三者均**线性、无平移** | `Runtime/Core/PoolGeometry.cs:52`、`:70-78`、`:95-98`、`:101-105` |
| F-2 | 物理二维平面 = **x 长轴 / y 短轴**，单位米；Unity 世界把该平面嵌入 **XZ 水平面**，`y = 0` 为台呢 | `Runtime/Core/PoolGeometry.cs:11-15`；`PoolConstants.ClothHeightY`（`:62`） |
| F-3 | 内库 2.54 × **1.270108866** m；球半径 **0.028575** m；台呢 **y=0** | `Runtime/Core/PoolConstants.cs:44`、`:53`、`:62`、`:28` |
| F-4 | **`PlayfieldVisualScale = 1.2f` 已存在但零消费者**（仅定义 + 一处测试） | 定义 `PoolConstants.cs:100`；引用仅 `Tests/EditMode/PoolConstantsTests.cs:146` |
| F-5 | `PlayfieldRoot` **在 `Main.unity` 已存在且 scale=1**（"预留视觉缩放层"）；**灰盒场景中没有 `PlayfieldRoot`** | `Editor/Visual/MaterialVisualSetup.cs:25`、`:239`；灰盒根节点见 F-8 |

### 2.2 灰盒场景现状（`Assets/_Project/Scenes/PhysicsGreybox.unity`）

| # | 事实 | 出处 |
| --- | --- | --- |
| F-6 | 根节点 = `PhysicsGreybox`（scale 1，rot 0，pos 0）；**无 `PlayfieldRoot`**（现有 8 个直接子物体 = CueBall / CushionGizmo / PhysicsTestDrive / RackBall_1..3 / Main Camera / Directional Light，**无一是缩放根**） | 名 `:263`；子物体表 `:281-289` |
| F-7 | 相机 `Main Camera`：**透视**，pos `(0, 2.1, −0.75)`，rot `x≈0.5644,w≈0.8255`（俯角 ≈ 68.6°），`field of view: 40`，near 0.05 / far 60。**相机是 `PhysicsGreybox` 的子节点，不在任何缩放根之下** | 名 `:550`；rot `:564`；pos `:565`；near/far `:602-603`；FOV `:604` |
| F-8 | `PhysicsTestDrive` 宿主：`tableConfig` / `cushionGizmo` / `ballVisuals`（4 个）/ `ballVisual` / `tuning` 已接线；**序列化值**：`loadRackForBallBallDemo=1`、`rollingDeceleration=0.9`、`aimDegrees=180`、`minLaunchSpeed=0.5`、`maxLaunchSpeed=4.5`、`aimStepDegrees=1`、`fineKey=304` | 名 `:389`；字段 `:430`/`:433`/`:437`/`:439`/`:440`/`:444`/`:446` |
| F-9 | **球可视物**：`CueBall`(x=−0.51) + `RackBall_1..3`(x=0.30/0.3497/0.3497)，**父节点 = `PhysicsGreybox` 根**，layer 7，scale 0.05715 | CueBall `:179`(pos `:194`)；RackBall_2 `:304`(pos `:319`)；RackBall_1 `:465`(pos `:480`)；RackBall_3 `:731`(pos `:746`) |
| F-10 | ⚠ **序列化值陷阱（曾发生、现已关闭 —— 留作制度）**：曾出现「场景 `maxLaunchSpeed: 8` / `rollingDeceleration: 0`」与代码默认（`4.5` / `0.9`）**不一致** ⇒ 实机用旧值而测试全绿。**场景重建 `950c78f` 后已同步**（现 `:440`=`4.5`、`:433`=`0.9`），由 `T43`（场景漂移）持续守住。**本 spec 的 `T45` / `T51` 仍各自钉"场景值 == 意图值"** | `:433`/`:440` vs `PhysicsTestDrive.cs:53`/`:81`；`Tests/EditMode/GreyboxTuningWiringTests.cs:109` |

### 2.3 当前瞄准实现（`Runtime/Physics/PhysicsTestDrive.cs`）

| # | 事实 | 出处 |
| --- | --- | --- |
| F-11 | `aimDegrees`（度，`0° = 指向 +x`，逆时针为正）、`power`、`minLaunchSpeed/maxLaunchSpeed` | `:70`、`:74`、`:77`、`:81` |
| F-12 | `AimDirectionUnity`（`getter` `:100-107`）**现已经 `PoolGeometry.PhysicsDirectionToUnity`（`:105`）返回 `(−cosθ, 0, −sinθ)`** —— **180° 反向缺陷已由 `5189d4d` 修正**（此前曾返回 `(cosθ, 0, sinθ)`；见 `Q-11`）。**方向一律走该入口，别处禁手写** | `PhysicsTestDrive.cs:93-107`；`PoolGeometry.cs:95-98` |
| F-13 | `aimStepDegrees = 1f`（基础步长）；`fineKey = LeftShift`；细调系数 **×0.2** ⇒ **细调 = 0.2°** | `:110`、`:116`、`:211` |
| F-14 | 键盘：`←/→` 改 `aimDegrees`（`←` **+**、`→` **−**）；`↑/↓` 改 `power`；`Space` 出杆 | 键位 `:118-126`、Update `:214-224` |
| F-15 | `Launch()`：**白球初速在物理平面** = `(cosθ, sinθ) · lerp(min,max,power)`；**门控 `PoolSimulator.TableAtRest`**（整盘未静止 ⇒ 返回 `Vector2.zero` 且不改状态）；**不再重建球堆** —— 只把白球放回场上（走 `PoolSimulator.PlaceBall`，**不动其他球**），**白球离台时显式放回 `footSpot`**（留痕出口 `LastLaunchRespawnedCueBall`）⇒ 语义 = **"球滚到哪就从哪打"** | `PhysicsTestDrive.cs` 的 `Launch()`；`PoolSimulator.PlaceBall`；**判据 `T65`–`T69`**（`Tests/EditMode/PhysicsTestDriveLaunchStateTests.cs`） |
| F-16 | `SyncVisual()`：把 `ballVisuals[i].position`（**世界坐标**）设为 `PoolGeometry.PhysicsBallToUnity(...)` ⇒ **球可视物一旦挂到 1.2 的 `PlayfieldRoot` 下，世界坐标写入不会随缩放**（见 §3.1 交付物 6 / 风险 `R-AB`） | `SyncVisual` `:393`、写入点 `:413` |
| F-17 | `SyncAimIndicator()`：按需创建细长立方体，`position`（**世界**）= 白球世界位置 + `dir·len/2`；`rotation = LookRotation(dir)`；无碰撞体；**球运动期间隐藏** | `:464`、`position` `:501`、`rotation` `:503` |
| F-18 | 键盘瞄准用**旧版 `Input.GetKey`**；项目 `activeInputHandler: 2`（**Both**）⇒ 新旧输入可共存 | `:214-224`；`ProjectSettings/ProjectSettings.asset:917` |

### 2.4 输入资产与交互语义

| # | 事实 | 出处 |
| --- | --- | --- |
| F-19 | `Gameplay` 映射含 `PointerPosition`(`<Pointer>/position`)、`AimLock`(`<Mouse>/leftButton`)、`AimNudge`(`→`/`←`invert/`D`/`A`invert)、`PowerAdjust`、`Shoot`(`Space`)、`CancelAim`(`<Mouse>/rightButton`、`Esc`)、`ToggleAimGuide`(`Tab`)、`RestartRack`(`R`) | `Settings/Input/Billiards.inputactions:7-236`（`AimLock` = `:18`+`:100`；`CancelAim` = `:54`+`:199/:206`；`AimNudge` = `:27`+`:111/:119/:133/:141`） |
| F-20 | 既有执行记录把 `AimLock` 的语义写成 **"按下拖拽瞄准，松开锁定方向"** —— 这是本仓库内**唯一**对该交互的具体陈述 | `Docs/35-A_Day1_工程基建_执行记录.md:165` |

### 2.5 生成器 / 验收器 / 证据姿势

| # | 事实 | 出处 |
| --- | --- | --- |
| F-21 | 灰盒场景由 **`GreyboxSceneSetup`** 幂等生成；生成器中**没有** `PlayfieldRoot`，也不写 `EditorBuildSettings`；球可视物直接挂根 | `Editor/Visual/GreyboxSceneSetup.cs:41-45`、`:147-181`、`:222-237` |
| F-22 | `MaterialVisualValidator` 的 `SCN-02` 断言 **`Main.unity`** 的 `PlayfieldRoot` 缩放 == 1（"1.2 未误用"），`SCN-03` 断言球桌挂在 `PlayfieldRoot` 之下 | `Editor/Visual/MaterialVisualValidator.cs:220-247` |
| F-23 | `GreyboxTuningWiringTests` 现有 4 条：`T41`（接线 —— 全部已接线参数与资产一致）+ **`T43`（场景漂移 —— 场景序列化值 == 代码默认）** + **`T44`（出厂口径冻结 —— 资产字段 == 台账表）** + `T42_AimIndicator_FollowsRules`（存在/可渲染/无碰撞体；全静止可见、运动中隐藏；朝向 == **独立闭式** `(−cosθ,0,−sinθ)`；位置闭式；**经验交叉校验**）。**⇒ `T43` / `T44` 编号已被占用，本 spec 自 `T45` 起** | `Tests/EditMode/GreyboxTuningWiringTests.cs`：`T41` `:46`、`T43` `:109`、`T44` `:161`、`T42` `:190` |
| F-24 | 报告路径**唯一单点**：`ReportOutput.ResolveReportPath`（接受 `-outputDir`）；测试证据 `ResolveTestEvidencePathOrNull` **未给 `-outputDir` 返回 `null`、绝不回落仓库** | `Editor/ProjectSetup/ReportOutput.cs:29-106` |
| F-25 | EditMode 基线（源码 `[Test]` 计数）= **135 @ `7f76dba`**（`133` 是 `d99aaf1` 时的数；`950c78f` 又 +2 = `T43`/`T44`）。四验收器 Day1/2/3/4 = 25 / 33 / 22 / 7 | `Docs/测试数基线台账.md`（顶部注）；`grep -cE "^\s*\[Test\]" Assets/_Project/Tests/EditMode/*.cs` |

---

## 3. 子任务拆解

> **程序集归属（铁律，`工程标准.md` §1 R1-1/R1-3）**：
> · 运行时资产进 **`Billiards.Runtime`**（全平台；命名空间前缀 **`Billiards`**，按层细分 `Core` / `Physics`）。
> · **编辑器 / 测试**代码进 **`Billiards.Editor`**（`Billiards.EditorTools`）与 **`Billiards.Tests.EditMode`**（`includePlatforms:["Editor"]`，`Billiards.Tests`）。
> · **禁止** `Runtime → Editor/Diagnostics` 倒挂。命名空间**作前缀**（`Billiards.Core` / `Billiards.Physics`），与既有 3 个例外一致。

### 3.1 子任务 1 — `PlayfieldRoot` 视觉缩放 1.2 + 反向映射（1.5 h）

**切法**：`1.2` 是**表现层等比缩放**（"桌面扩大 20%" = `05_VFX/音画设计与资源说明_参考.txt` 的"保留 **120% 显示比例**"，是**显示**语义）。物理保持**规范米制**，故必须有一个**唯一**的「显示 ↔ 规范」映射。

**新增交付物（运行时）**
- `Runtime/Core/PlayfieldView.cs` · `namespace Billiards.Core` · assembly `Billiards.Runtime`。
  - 职责：**唯一**的「显示世界 ↔ 规范世界」映射。**只管等比缩放，不做轴/手性转换**（轴/手性**只**走 `PoolGeometry`）。
  - API：
    ```
    public static class PlayfieldView {
        // 规范世界 → 显示世界（= 挂 PlayfieldRoot 的子节点局部坐标 → 世界）
        public static Vector3 DisplayFromCanonical(Transform playfieldRoot, Vector3 canonicalWorld);
        // 显示世界 → 规范世界（= playfieldRoot.InverseTransformPoint）
        public static Vector3 CanonicalFromDisplay(Transform playfieldRoot, Vector3 displayWorld);
        // 断言用：playfieldRoot 的等比缩放是否为期望值（uniform）
        public static bool HasExpectedScale(Transform playfieldRoot);
    }
    ```
  - 实现即 `TransformPoint` / `InverseTransformPoint`（**通用逆变换**，天然含"去平移/去旋转/去缩放"），**不得**手写 `× 1/1.2` 之类硬编码（那会在 root 带平移时静默错）。

**新增交付物（编辑器）**
- `Editor/Visual/GreyboxSceneSetup.cs` **增补**：生成 `PlayfieldRoot`（`localPosition = 0`、`localRotation = identity`、`localScale = Vector3.one * PoolConstants.PlayfieldVisualScale`），并把**可视野内容**（球可视物）挂到其下；`Main Camera` **保持在 `PhysicsGreybox` 根下**（**不得**放进缩放根，否则相机被缩放）。

**为什么这样切**：
- 缩放交给 `Transform`（唯一真相是 `PlayfieldRoot.localScale`），映射交给 `PlayfieldView`（唯一函数）⇒ 不产生第二份 1.2。
- 常量 `PlayfieldVisualScale` 已在 `PoolConstants`（`Core`），函数落 `Core` ⇒ 同层，无倒挂。

**⚠ 与 R5-2 / ADR §7.1 的关系（边界，见 `Q-8`）**：
`ADR-0001 §7.1` 写"禁止在 `PoolGeometry.cs` 之外的任何位置做轴 / 手性 / **缩放**转换"。本 spec 的口径：
**`PoolGeometry` 仍是唯一"坐标系（轴/手性/单位）"转换点；1.2 是"表现层等比缩放"，是另一条正交的、单点的关注点**，落 `PlayfieldView`。
⇒ **需要 ADR-0001 补注**（新增 §2.5，记两条单点与"二者不得复合"），**该补注是本日的落地件之一**（由实现方在实现提交里完成，不由本 spec 修改 ADR）。

### 3.2 子任务 2 — 指针射线拾取台呢平面 → 白球指向点 → 方向向量（2 h）

**切法**：把"屏幕像素 → 物理平面点"做成**纯函数**（可单测、无输入设备依赖），`MonoBehaviour` 只做胶水。

**新增交付物（运行时）**
- `Runtime/Physics/PointerAimSolver.cs` · `namespace Billiards.Physics` · assembly `Billiards.Runtime`。**纯静态、无 `Input`、无状态**：
  ```
  // 屏幕像素 → 物理平面点（唯一口径：先反缩放再投影，见 §13 Q-10）
  public static bool TryPickPhysicsPoint(Camera cam, Transform playfieldRoot,
                                         Vector2 screenPosition, out Vector2 physicsPoint);
  // 白球物理位 + 指向点 → 物理平面方向（单位向量）
  public static Vector2 AimDirectionPhysics(Vector2 cueBallPhysics, Vector2 targetPhysics);
  // 物理平面方向 → Unity 世界方向（**委托** PoolGeometry.PhysicsDirectionToUnity；不含平移）
  public static Vector3 WorldDirectionFromPhysics(Vector2 aimPhysics);
  ```
- 拾取实现（唯一顺序，β）：
  ```
  Ray ray = cam.ScreenPointToRay(screenPosition);
  Vector3 O = playfieldRoot.InverseTransformPoint(ray.origin);          // 规范空间射线原点
  Vector3 D = playfieldRoot.InverseTransformVector(ray.direction);      // 规范空间射线方向（不带平移）
  if (Mathf.Abs(D.y) < eps) return false;                               // 射线与台呢平面近平行 ⇒ 拒绝
  float t = (PoolConstants.ClothHeightY - O.y) / D.y;
  Vector3 canon = O + t * D;                                            // 规范 Unity 世界（未缩放）
  physicsPoint = PoolGeometry.UnityToPhysics(canon);                    // 唯一坐标系转换点
  ```
- 物理方向 → 世界方向：**一律走官方方向入口 `PoolGeometry.PhysicsDirectionToUnity(Vector2 physicsDirection)`**（`PoolGeometry.cs:95-98`；`5189d4d` 新增，`(−dx, 0, −dy)`）。`PointerAimSolver` 只做 `normalize` / `Atan2`，**不自行实现方向映射**（别处手写 `(−x,−y)` = 第二处转换点，违铁律 16）。

**为什么这样切**：与内核既有纯函数（`PhysicsTestDrive.WholeSteps` / `RackPositions`）**同一风格** —— 测试可直接断言纯函数，不需要真的移动鼠标。

### 3.3 子任务 3 — 拖拽瞄准 / 松开锁定方向（2.5 h）

**切法**：把**交互状态机**与**输入设备**分离。状态机 = 纯逻辑（可单测）；输入读取 = 薄胶水。

**新增交付物（运行时）**
- `Runtime/Physics/PointerAimController.cs` · `namespace Billiards.Physics` · assembly `Billiards.Runtime`（`MonoBehaviour`）。
  - 序列化字段：`Camera aimCamera`、`Transform playfieldRoot`、`PhysicsTestDrive drive`（**只写它的 `public float aimDegrees`**，见下"不许改的文件"）。
  - 读 **Input System 的 `Gameplay` 映射**（`PointerPosition` / `AimLock` / `AimNudge` / `CancelAim`）；`activeInputHandler=2`（Both，F-18）⇒ 与既有旧输入共存。
  - 状态：`Aiming`（未锁定，指针移动即更新 `aimDegrees`）/ `Locked`（已锁定，指针移动**不改** `aimDegrees`）。
  - 语义（依 F-20 与 `AimLock` 绑定）：**`AimLock` 按下 ⇒ 进入跟随（拖拽瞄准）；松开 ⇒ 冻结当前方向（锁定）**；`CancelAim` ⇒ 解锁（可再次拖拽）。
  - 每帧：`Aiming` 时 `drive.aimDegrees = Atan2(dirPhys.y, dirPhys.x) · Rad2Deg`。
- **纯逻辑核**（同文件或 `PointerAimState` 小类）：`Advance(bool lockHeld, Vector2 pointerPx)` → 返回是否更新方向（供单测直接驱动，不碰 Input）。

**为什么这样切**：`AimLock` 的绑定语义（"按下拖拽瞄准，松开锁定方向"）是**交互契约**，与"指针在哪"（几何）正交；拆开后 `T50` 可只测状态机，避免把"没插鼠标"变成不可测。

**⚠ 关于 `PhysicsTestDrive.cs`（协作约束 —— 已解除）**：该文件曾被另一 worker 占用（初速资产化 + 场景重建），**其工作已提交（`950c78f` / `5189d4d`）、现无人占用**（`HEAD = 7f76dba`，工作区干净）。
⇒ 本子任务**只**通过 `drive.aimDegrees`（既有 **public 字段**）注入方向 ⇒ **交互本体不必改 `PhysicsTestDrive.cs`**。
⇒ §3.1 里"球可视物改走**局部坐标**"的 1 行改动落在 `PhysicsTestDrive.SyncVisual`（写入点 `:413`）—— **现已可做**（见 §10.2 协调项 C-1，**已解除**）。

### 3.4 子任务 4 — 方向键微调（0.2°）（1 h）

**口径裁定（见 `Q-9`）**：
- **基础步长 = `aimStepDegrees = 1.0°`；细调步长 = `aimStepDegrees × 0.2 = 0.2°`**（按住 `fineKey`）。
- 排期写"方向键微调（粒度约 **0.2°**）"：**"微调"即细调路径**；且现状 `1.0 × 0.2 = 0.2` 恰得该值 ⇒ **0.2° 是"细调步长"，不是"基础步长"**。
- **键向**：`AimNudge`（`←` / `→`）改 `aimDegrees`，`←` 为 **+**、`→` 为 **−**（沿用现状 `:203-204`，与 `inputactions` 的 `invert` 一致）。

**落地**：`PointerAimController` 读 `AimNudge` 轴；`aStep = fine ? 1.0° × 0.2 : 1.0°`。**不得**改动 `aimStepDegrees` 的语义（基础 = 1.0°）。
**待实测定值**：`→` 在**屏幕上**使瞄准线顺时针还是逆时针，取决于相机方位（`Q-9` 附注）——本 spec 只钉**物理平面**的符号，屏幕手感留**待实测**。

### 3.5 子任务 5 — 视口适配：16:9 基准 + 窄窗回落（1 h）

**切法**：把"基准取景"与"窄窗回落"都收敛到**一处**（相机适配器），并**保持水平 FOV**，从结构上保证"窄窗不裁切球桌"。

**新增交付物（运行时）**
- `Runtime/Physics/CameraViewportFitter.cs` · `namespace Billiards.Physics` · assembly `Billiards.Runtime`（`MonoBehaviour`，挂 `Main Camera`）。
  - 序列化字段：`float baseAspect = 16f/9f`、`float baseVerticalFovDeg = 40f`（= 场景现值，F-7）、`float minSupportedAspect`。
  - 规则（**保持水平 FOV 恒定**）：
    ```
    tanHalfH = tan(baseVerticalFovDeg/2) * baseAspect          // 16:9 基准的水平半角正切
    if (cam.aspect >= baseAspect)  vFov = baseVerticalFovDeg   // 更宽 ⇒ 不动（显示更多，不裁切）
    else                           vFov = 2*atan(tanHalfH / cam.aspect)  // 更窄 ⇒ 抬垂直 FOV，保住水平覆盖
    vFov = min(vFov, maxVerticalFovDeg)                        // 兜底，防 180°
    cam.fieldOfView = vFov
    ```
  - 触发：`OnEnable` + 宽高比变化（`Screen.width/height` 或 `cam.aspect` 变化时重算）。

**为什么这样切**：水平 FOV 恒定 ⇒ 只要 16:9 基准能放下整桌，任何更窄的窗口**水平方向**必定仍放得下 ⇒ "窄窗不裁切球桌"成为**结构保证**，而不是"调参调出来的"。

> **`browser-qa` 引用**：**未在仓库中定位到**（仅排期 `:214` 提及）⇒ 本 spec 采用**自定策略**（16:9 基准 + 保持水平 FOV 的窄窗回落），并把该外部引用标为**不可达**（`Q-12`）。

---

## 4. 接口 spec

### 4.1 常量 / 字段落点表（实现不得自行造值）

| 常量 / 字段 | 落点 | 状态 | 值 / 语义 |
| --- | --- | --- | --- |
| `PlayfieldVisualScale` | `PoolConstants` | **已存在，不动** | `1.2f`（`PoolConstants.cs:100`） |
| `ClothHeightY` / `ClothInnerLength` / `ClothInnerWidth` / `BallRadius` | `PoolConstants` | **已存在，不动** | 0 / 2.54 / 1.270108866 / 0.028575 |
| `aimStepDegrees` | `PhysicsTestDrive` | **已存在，语义不变** | `1.0°`（基础步长） |
| **细调系数** | `PointerAimController` | **需新增（常量）** | `0.2f`（⇒ 细调 = 0.2°） |
| **`baseAspect` / `baseVerticalFovDeg`** | `CameraViewportFitter` | **需新增** | `16/9` / `40f`（= 场景 F-7 现值） |
| **`minSupportedAspect` / `maxVerticalFovDeg`** | `CameraViewportFitter` | **需新增** | 见 §8 降级；`maxVerticalFovDeg` 取 `60f`（**乙-系数选定**） |
| `PlayfieldRoot` 缩放 | 灰盒场景 | **需新增** | `Vector3.one * 1.2`（uniform） |

### 4.2 契约（在内核/引擎既有契约之上追加）

1. **坐标系转换单点不变**：轴/手性/单位**只**走 `PoolGeometry`（`R5-2`）。本日新增的 `PlayfieldView` **只做表现层等比缩放**，**不得**做轴/手性转换，**不得**在别处再出现第二个 1.2（`Q-8`）。
2. **反双重缩放**：规范坐标**不得**再被放进 1.2 缩放的根下（`ADR-0001 §2.4`、交付文档 `transforms.anti_double_scaling`）。规范坐标进 `PlayfieldRoot` 的**局部空间**；显示世界 = `PlayfieldRoot.TransformPoint(规范局部)`。
3. **拾取顺序唯一**：**先反缩放（射线 → 规范空间）→ 再投影到 `y=0` → 再 `UnityToPhysics`**（`Q-10`）。
4. **方向单点**：世界瞄准方向**只**由 `PoolGeometry.PhysicsDirectionToUnity` 导出（`Q-11`）；指示物朝向与白球出射方向**必须**由同一物理角度导出；**别处禁手写 `(−x,−y)`**。
5. **不引 PhysX**：新代码不得出现 `Rigidbody` / `Collider` / `UnityEngine.PhysicsModule`（`R5-1`，`D4-PHY-01`）。
6. **无 `Time`/`Random` 进入物理**：本日不碰内核；`CameraViewportFitter` 可读 `Screen`，但**不得**把任何输入喂进 `PoolSimulator`（`R5-3`）。

### 4.3 数值锚定示例（供实现与测试对照）

以场景现值（F-7，相机 pos `(0,2.1,−0.75)`、俯角 68.6°、`FOV=40`、16:9）为例，**屏幕中心像素**的射线方向 = 相机 forward。该射线与 `y=0` 的交点、及其经 `PlayfieldView` 去缩放、`UnityToPhysics` 后的物理点，**由测试按相机位姿独立闭式算出**（`T47`/`T49`）。**本 spec 不写死该数值** —— 相机位姿**随场景重建可能变化** ⇒ 测试须**从场景序列化值读相机**，而非硬编码（`R3-6` "带修订锚点"）。

---

## 5. 测试族（**判据续 `T42`，自 `T45` 起**）

> **编号占用边界**：`T43`（场景漂移）/ `T44`（出厂口径冻结）已归 `Tests/EditMode/GreyboxTuningWiringTests.cs`；**本 spec 的判据 = `T45`–`T53`**（不复用、不跳号）。

> **坐标系**：物理平面 x 长轴 / y 短轴，单位米。测试运行在 **EditMode**（`Billiards.Tests.EditMode`，`includePlatforms:["Editor"]`）。
> **每条期望值必须来自「被测对象之外」**：契约常量 / 场景**序列化**值 / 独立闭式 / 手工推导数值。**禁止自洽式断言**
> （`工程标准.md` §7 第 8 条；本工程已登记实例 = `T40` 的 `Assert.AreEqual(drive.maxLaunchSpeed, v0.x, …)` —— 期望值 = 被测对象自身字段 ⇒ 恒真）。
> **每条必须声明「能测出什么 / 测不出什么（盲带）/ 判别力来源 / 注入式负对照（注入什么 ⇒ 期望哪条 FAIL）」。**

### T45 `PlayfieldRoot` 缩放 == 意图值（锚定"界 == 场景序列化值"）

**判据（PASS/FAIL）**：
- 灰盒场景中存在名为 `PlayfieldRoot` 的节点；
- 其 `localScale` **逐分量** == `PoolConstants.PlayfieldVisualScale`（容差 1e-6）；
- 且 `PlayfieldRoot.localRotation == identity` 且 `localPosition == 0`（否则 `PlayfieldView` 的"即缩放"假设不成立）。

**期望值来源**：`PoolConstants.PlayfieldVisualScale`（**契约常量**）+ 场景**序列化**值。
**盲带（抓不到什么）**：只证明"界 == 意图"，**不证明 1.2 真的落到了球可视物上**（由 `T46` 承担）；也不证明"别处没有第二个 1.2"（由 `T49` 的注记 + `T48` 承担）。
**判别力来源**：本项目刚踩过"改了代码默认值、场景序列化覆盖值没同步 ⇒ 实机用旧值而测试全绿"（F-10）。本条把**场景里的值**钉到意图值。
**注入式负对照**：
- 注入 `localScale = Vector3.one * 1.2f * 1.2f`（**双缩放**）⇒ **T45 FAIL**（与常量不符）。
- 注入 `localScale = Vector3.one`（**漏缩放**）⇒ **T45 FAIL**。

### T46 指针点中「球在屏幕上出现的位置」→ 反解出「球的物理位置」（**1.2 正/反向一致性的决定性判据**）

**构造**：取白球物理位 `c_phys`（读 `drive.Simulator.Balls[0].position`）。计算它在屏幕上的**实际渲染像素**：
`screen = cam.WorldToScreenPoint(ballVisuals[0].position)`（读**渲染**位置 —— 被测对象是**拾取器**，不是这个 Transform）。
再把这个 `screen` 喂给 `PointerAimSolver.TryPickPhysicsPoint(...)`，得 `p_phys`。

**判据**：`|p_phys − c_phys| ≤ 2e-4 m`（**2e-4 m ≈ 0.2 mm**，取 scene 直方图量化 + float 余量；见 §8 降级）。

**期望值来源**：`c_phys`（**模拟器** —— 与拾取器不同的对象）+ 相机投影（引擎原语）。
**盲带**：① 若"球的渲染位置"与"拾取"**同向同幅**偏移（同时漏 1.2），本判据可能仍过（理论盲带）—— 由 `T45`（界 == 1.2）与 `T47`（绝对方向）补；② 只覆盖白球，不覆盖堆球。
**判别力来源**：**它同时约束"视觉 1.2"与"拾取反向 1.2"必须互为逆** —— 任一边写错（0.833×、1.44×、漏缩放），`screen` 与 `p_phys` 就不再对应。
**注入式负对照**：
- 拾取端**漏反缩放**（`p_phys` 偏 20%）⇒ **T46 FAIL**。
- 视觉端**漏 1.2**（球位置未随缩放）⇒ **T46 FAIL**。
- 把 `PlayfieldRoot.localScale` 设 `1.2²`（双缩放）⇒ **T46 FAIL**。

### T47 指针方向 == 白球朝向（**无偏移**）—— 排期验收判据之一

**构造**：对若干屏幕像素 `s`（含屏幕中心、以及台面内的若干采样点），求
`expected_phys = normalize(PickPhysics(s) − c_phys)`；`expected_world = AimDirectionUnity(expected_phys)`。

**判据（两条，缺一不可）**：
- （a）`Vector3.Angle(drive.AimDirectionUnity, expected_world) < 0.1°`（**指示物朝向**与独立算得的世界方向一致）；
- （b）`Vector2 v0 = drive.Launch()`（Launch 后读返回的物理初速）满足 `Vector2.Angle(v0, expected_phys) < 0.05°`（**白球实际出射方向**与期望一致）。

**期望值来源**：独立闭式（相机射线 → 平面 → 去缩放 → `PoolGeometry` 线性部分）。
**盲带**：`s` 若落在台面外（射线不与台呢相交）⇒ `TryPickPhysicsPoint` 返回 `false`，本判据**不覆盖**该情形（由 `T49` 的拒绝用例覆盖）。
**判别力来源**：**（a）就是"180° 反向"的判决量** —— 该缺陷已由 `5189d4d` 修正 ⇒ 本条现为**其回归守卫**（把 `AimDirectionUnity` 改回 `(cosθ, 0, sinθ)` 即 FAIL）；（a）+（b）把"指示物"与"白球"锁到**同一**物理角度上。
**注入式负对照**：
- 把 `AimDirectionUnity` 改回 `(cosθ, 0, sinθ)`（`5189d4d` **之前**的旧口径）⇒ **（a）FAIL，偏差 180°**。
- 只改拾取、不改 `Launch()`（两者用了不同角度）⇒ **（b）FAIL**。
- `PlayfieldRoot.localScale = 1.2²` ⇒ 拾取点偏移 ⇒ **（a）（b）均 FAIL**。

### T48 「先反缩放再投影」≡「先投影再反缩放」（**顺序恒等**的守卫；把"选错顺序"变成可判定）

**判据**：对若干屏幕像素 `s`，比较
- β（实现口径，§3.2）：`p_β = TryPickPhysicsPoint(s)`；
- α（测试侧**独立**按"先与显示 `y=0` 平面求交 → 再 `InverseTransformPoint` → 再 `UnityToPhysics`"算）：`p_α`。

断言 `|p_α − p_β| ≤ 1e-6 m`。

**期望值来源**：测试侧独立实现的 α（与被测 β **不同的代码路径**）。
**盲带**：若 α 与 β **同向同幅**都错（例如两条都漏 1.2）⇒ 相等但都错 ⇒ 本判据**抓不到**；由 `T46`/`T47` 的**绝对**期望补。
**判别力来源**：见 §8 的推导 —— 对**任意可逆线性映射** `S`（无平移）与**被 `S` 固定的平面** `Π`，有 `S⁻¹(Project_{S(Π)}(ray)) = Project_Π(S⁻¹ ray)`。⇒ **只**当实现里存在**平移项丢失 / 双缩放 / 两条路径用了不同的 s** 时，α ≠ β。**故"顺序选错"在本工程不是可能事件，而是 bug 症状**。
**注入式负对照**：
- 只让 β 端**漏反缩放**（α 端用正确的 1/1.2）⇒ **T48 FAIL**。
- 给 `PlayfieldRoot` 加一个**非零 `localPosition`**、而 β 端用 `InverseTransformVector` 却**忘了** `InverseTransformPoint` 处理平移 ⇒ **T48 FAIL**。

### T49 拾取精度与拒绝域（独立闭式 + 边界）

**构造**：对一组屏幕像素（含屏幕中心、四分之一处、台面四角投影点）：
- 逐点比较 `TryPickPhysicsPoint` 与**测试侧独立闭式**（用 `cam.ScreenPointToRay` 取射线，**手工**做平面求交 + 去缩放 + `UnityToPhysics`）——容差 `2e-4 m`；
- 另取"**射线与台呢近平行**"的像素（把相机 FOV 临时拉到 ~179° 或选极端像素）与"**射线落在台面 XZ 范围之外**"的像素，断言 `TryPickPhysicsPoint == false`（**拒绝**，不得返回 NaN/Inf/越界点）。

**期望值来源**：独立闭式 + 契约常量。
**盲带**：不覆盖"**镜头被另一 worker 改位姿**"后的回归（每次跑须从**场景序列化值**读相机，见 §4.3）。
**判别力来源**：`2e-4 m` 远小于 1.2 缩放带来的 20% 误差（0.2 m 量级）⇒ 对缩放错误**极具判别力**；拒绝域防止"方向键乱转"。
**注入式负对照**：
- 把 `PoolGeometry` 替换为 `(px, py)`（**旧口径**，`ADR-0001 §2.3` 记载的错误式）⇒ **T49 FAIL**（符号错）。
- 删掉 `TryPickPhysicsPoint` 的 `|D.y| < eps` 检查 ⇒ 平行射线返回 NaN ⇒ **T49 FAIL**（拒绝用例）。

### T50 拖拽瞄准 / 松开锁定（**交互状态机**）

**构造**：对纯逻辑核 `PointerAimState` 喂**脚本化输入序列**（`(lockHeld, pointerPx)` 列表，不碰 Input 设备）：
1. `lockHeld=true` + 指针移动 ⇒ `aimDegrees` **随指针更新**；
2. `lockHeld=false`（松开）+ 指针继续移动 ⇒ `aimDegrees` **逐位不变**（冻结）；
3. `CancelAim` ⇒ 解锁；`lockHeld=true` 再次按下 + 指针移动 ⇒ 恢复更新。

**判据**：三段的 `aimDegrees` 序列分别满足"变 / 不变 / 又变"（`不变` 段要求**逐位相等**）。
**期望值来源**：**契约语义**（F-20 `AimLock` = "按下拖拽瞄准，松开锁定方向"）。
**盲带**：**不测 Input System 设备绑定**（真鼠标按下/松开）—— 那是 Manual/PlayMode 项（§1.2 已声明）。
**判别力来源**：直接把"松开后仍在跟随"这一常见缺陷变成可判定失败。
**注入式负对照**：
- 注入"松开后仍更新方向"（漏冻结）⇒ **T50 第 2 段 FAIL**。
- 注入"按下时不更新"（把状态写反）⇒ **T50 第 1 段 FAIL**。

### T51 方向键微调粒度（0.2° 细调 / 1.0° 基础；**且界 == 场景值**）

**判据**：
- 场景序列化 `PhysicsTestDrive.aimStepDegrees == 1.0`（容差 1e-6）—— **锚定"场景值 == 意图值"**（防 F-10 型假绿）；
- 细调系数常量 == `0.2`；
- 一次 `AimNudge`（细调，按住 `fineKey`）使 `aimDegrees` 变化 `±0.2°`（容差 1e-3°）；非细调变化 `±1.0°`。

**期望值来源**：排期"粒度约 0.2°" + `PoolConstants`/契约常量（**非**读 `PointerAimController` 自身字段导出）。
**盲带**：**不测**"屏幕上的顺/逆时针手感"（依赖相机方位，留**待实测定值**，`Q-9` 附注）。
**判别力来源**：把"0.2° 是基础还是细调"钉死；并防"场景把基础改成 0.2° ⇒ 细调变 0.04°"的静默退化。
**注入式负对照**：
- 把场景 `aimStepDegrees` 改成 `0.2` ⇒ 基础变 0.2° ⇒ **T51 第 1/3 段 FAIL**（基础不再 == 1.0°）。
- 把细调系数改成 `0.5` ⇒ 细调变 0.5° ⇒ **T51 第 3 段 FAIL**。

### T52 窄窗不裁切球桌（多宽高比；**排期验收判据之二**）

**构造**：对宽高比集合 `{16/9, 16/10, 3/2, 4/3, 1/1, 3/4}`，把**内库四角**（物理 `(±ClothInnerLength/2, ±ClothInnerWidth/2)`，**契约常量**）经
`PoolGeometry.PhysicsToUnity` → `PlayfieldView.DisplayFromCanonical`（模拟 `PlayfieldRoot` 缩放）→ `cam.WorldToViewportPoint`
投到视口坐标；按 `CameraViewportFitter` 的规则设置 `cam.aspect` 后逐一校验。

**判据**：每个宽高比下，四角的 `viewport.x`、`viewport.y` 均落在 `[m, 1−m]`（`m = 0.02` 边距），且 `viewport.z > 0`（在相机前方）。

**期望值来源**：`PoolConstants`（内库）→ 引擎投影（相机由**场景序列化值**提供）。
**盲带**：① 只覆盖 **≥ `minSupportedAspect`** 的比例；更窄的极端比例超出支持域（见 §8 降级）；② 不判"球桌是否**太小看不清**"（排期只要求"不裁切"）。
**判别力来源**：直接投影**内库四角**（球桌在物理上可玩的边界）—— 裁切即 FAIL。
**注入式负对照**：
- 把适配器退回"**固定垂直 FOV**"⇒ 4:3 / 1:1 / 3:4 下四角 `viewport.x` 越界 ⇒ **T52 FAIL**。
- 把"窄窗"分支写成 `cam.fieldOfView = baseVerticalFovDeg`（该调不调）⇒ **T52 FAIL**。

### T53 「16:9 基准」是**不动点**（适配器在基准比例下为 no-op）

**判据**：在 `cam.aspect == 16/9` 时，适配器产出的 `cam.fieldOfView == baseVerticalFovDeg`（= 场景序列化 FOV，容差 1e-4）。
**期望值来源**：场景序列化 FOV（F-7）。
**盲带**：不证明"基准取景真的装得下整桌"（那由 `T52` 在 16:9 档覆盖 —— **若 16:9 就装不下，`T52` 会在此档 FAIL，属真实发现**）。
**判别力来源**：防"适配器在宽窗也开始乱改 FOV"（会让"基准"名不副实）。
**注入式负对照**：把规则写成"窄窗和宽窗都改 FOV"⇒ 16:9 时 FOV ≠ 序列化值 ⇒ **T53 FAIL**。

> **⚠ 待实测定值（本 spec 不编数字）**：
> - **16:9 基准是否恰好容纳整桌（含库边外沿与桌腿）** —— 需 Unity 相机投影实测（`T52` 的 16:9 档即探针）。
> - **`minSupportedAspect` 的可用门限** —— 需实测（`maxVerticalFovDeg=60f` 是**乙-系数选定**的兜底，非推导）。
> - **方向键"→"在屏幕上的旋转朝向** —— 需实测（依赖相机方位）。

---

## 6. 验收判据汇总

| # | 判据（= 测试 ID） | 门槛（PASS 条件） | 期望值来源 |
| --- | --- | --- | --- |
| 33 | **`T45`** 缩放锚定 | 灰盒 `PlayfieldRoot.localScale == PoolConstants.PlayfieldVisualScale`（1e-6）且 rot=id/pos=0 | 契约常量 + 场景序列化值 |
| 34 | **`T46`** 视觉/拾取互逆 | 屏幕反解回球的**物理**位误差 ≤ `2e-4 m` | 模拟器位（异对象）+ 引擎投影 |
| 35 | **`T47`** 方向无偏移（**验收**） | （a）指示物朝向与独立世界方向 ∠ < 0.1° 且 （b）`Launch()` 初速与期望物理方向 ∠ < 0.05° | 独立闭式 |
| 36 | **`T48`** 顺序恒等 | α 与 β 拾取差 ≤ `1e-6 m` | 测试侧独立 α |
| 37 | **`T49`** 拾取精度 + 拒绝域 | 逐点 ≤ `2e-4 m`；平行/越界 ⇒ `false` | 独立闭式 + 契约常量 |
| 38 | **`T50`** 拖拽/锁定 | 三段（变 / 逐位不变 / 又变） | 契约语义（F-20） |
| 39 | **`T51`** 微调粒度 | 场景 `aimStepDegrees==1.0` ∧ 细调 `==0.2°` ∧ 基础 `==1.0°` | 排期 + 契约常量 |
| 40 | **`T52`** 窄窗不裁切（**验收**） | 6 档宽高比下内库四角 `viewport∈[0.02,0.98]` | `PoolConstants` + 场景相机 |
| 41 | **`T53`** 基准不动点 | 16:9 时 `fieldOfView ==` 场景序列化 FOV | 场景序列化值 |
| 42 | `R5-1` 无 PhysX | 新代码 `Rigidbody`/`Collider`/`PhysicsModule` 命中数 == 0（复用既有守卫） | — |
| 43 | **EditMode 总数** | 全绿；总数 = 基线 **135** @ `7f76dba` + 本 spec 新增（**双向核对**，§11.1） | — |

> **「丙」= 0 声明**：本 spec §6 的期望值**无一条**为"转写被测代码公式"。
> 分类：**甲 = 独立闭式/引擎原语**（`T46`/`T47`/`T49`/`T52`）；**乙 = 契约常量/场景序列化值**（`T45`/`T51`/`T53`）；**乙(系数选定) = 选定门限**（`T48` 容差、`T52` 边距 `0.02`、`minSupportedAspect`）。
> **⚠ `T42` 已由 `5189d4d` 落地（**勿重复改**）**：现名 `T42_AimIndicator_FollowsRules`，独立期望式 = `(−cosθ, 0, −sinθ)`（`PoolGeometry` 方向入口），并含**位置闭式** + **经验交叉校验**（显示方向 vs 白球仿真位移）+ **运动中隐藏**。`Q-11` 与该 hotfix **结论一致**（两处独立得出，互为佐证）。本 spec 的 `T47` 是**同类判处量的回归守卫**（见 §5 `T47`）。
> **⚠ 注入式负对照 = 每条判据一行**（§5 各条"注入式负对照"）：`T45`(双缩放/漏缩放) / `T46`(漏反缩放/漏视觉缩放) / `T47`(180°/链路不一致) / `T48`(单边漏缩放/漏平移) / `T49`(旧映射式/删平行检查) / `T50`(漏冻结/写反) / `T51`(场景改 0.2/系数改 0.5) / `T52`(退固定 FOV) / `T53`(宽窗也改 FOV)。**事前一击，非事后证明**（铁律 18）。

---

## 7. 证据清单

| 文件 | 内容 | 落盘位置 |
| --- | --- | --- |
| **无新验收器** | 本日**不新增**验收器（交互层无独立场景几何不变量）⇒ 准则：**spec 只指向真实存在的证据**（Day 6 `D7-T01` 裁定）。守卫**全部进 EditMode 测试套件**（`R4-2`：本工程无 CI，验收器里的守卫 = 没人点菜单就不跑） | — |
| **`<outputDir>/d8-pointer-aim.json`** | 拾取/方向证据：逐采样点 `screenPx`、`pickedPhysicsX/Y`、`expectedPhysicsX/Y`、`angleErrDeg`；α vs β 差；6 档宽高比的四角 `viewport`；`playfieldScale`；`cameraFov/aspect`（**从场景序列化读**）。**必填字段**见 §7.1 | **测试**经 `ReportOutput.ResolveTestEvidencePathOrNull` 写到 **`-outputDir`**（仓库外）；**未给 `-outputDir` ⇒ 只跳过落盘、不跳过任何断言**（`R3-5`/`R3-7`） |
| **不落盘项（明示）** | ① 屏幕截图/人工目视"指针方向对不对"（Manual）；② 输入设备级按下/松开（Manual/PlayMode）；③ §5 的"待实测定值"三条（需 Unity 相机实测） | — |

### 7.1 必填字段（`d8-pointer-aim.json`）

`schema`、`generatedAtUtc`、`unityVersion`、`projectRoot`、`isBatchMode`、`passCount`/`warnCount`/`failCount`、`overallStatus`、`checks[]`；
每个 `checks[]` 含 `id`、`name`、`expected`、`actual`、`detail`、`status`。
**数值一律 `ToString(InvariantCulture)` 显式调用**（`R3-8`：内插格式符在 Unity 下不生效会产出非法 JSON）。

---

## 8. 风险与降级

### 8.1 接 R5（排期 §三 · 责任日 = Day 8）

**R5「"桌面扩大 20%"的语义歧义：视觉缩放还是物理比例？」** 本日**承接**并**裁定**：**只做视觉**（`PlayfieldRoot` 缩放 `1.2`），**物理保持规范米制**，**指针拾取反向映射回物理坐标**（§3.1/§3.2）。
外部佐证：美术包 `F:\工作文件\idea35_台球\05_VFX\音画设计与资源说明_参考.txt` 明写"**保留 120% 显示比例**" —— "显示"= 表现层，非物理。

### 8.2 「1.2 缩放」与「指针拾取」的**顺序**（唯一确定的一个 + 推导）

**钉死的口径**：**β = 先把射线变到规范空间（`InverseTransformPoint` / `InverseTransformVector`）→ 再与 `y = 0` 求交 → 再 `PoolGeometry.UnityToPhysics`**（§3.2）。

**推导（α ≡ β 的证明）**：设 `PlayfieldRoot` 无平移、无旋转、等比缩放 `S = s·I`（`s = 1.2`）；台呢平面 `Π = { y = 0 }`，且 `S(Π) = Π`（过原点的 `y=0` 被缩放固定）。
- **α（先投影再反缩放）**：世界射线 `R` 与**显示**平面交得 `W`，再取 `P_规范 = S⁻¹W`。
- **β（先反缩放再投影）**：取 `S⁻¹R`（`O' = S⁻¹O`、`D' = S⁻¹D`），与**规范**平面交得 `P_规范`。
- 对**任意可逆线性映射 `S`**：`{ S⁻¹(O + tD) : S⁻¹(O+tD) ∈ Π } = { O' + tD' : O'+tD' ∈ Π }` —— 这是"**射线-平面求交与线性映射可交换**"的恒等式（**无平移项**时）。
- ⇒ **`P_规范` 唯一，α 与 β 恒等**。**故"顺序选错"在本工程不是可能事件**；若实现里 α ≠ β，那**必是**：平移项被漏、1.2 被乘两次（1.44）、或两条路径用了**不同的 s**。

**⚠ 唯一会让 α ≠ β 的情形**：`S` 在**物理平面内非等比**（如只缩世界 X）。此时世界 `(x,z)` 被不同比例缩放 ⇒ 物理 `(x,y)` 不再等比 ⇒ **α ≠ β**，顺序才有意义。
⇒ 本工程**钉死 `S` 为等比 1.2**（`PlayfieldVisualScale` 是**单个 float**；`SCN-02` 逐分量检查 ⇒ 隐含 uniform）⇒ **不进入该分支**。若将来改非等比，须重新裁定顺序（仍取 β）。

**判据**：`T48`（α ≡ β，1e-6 m）。

### 8.3 风险登记（续 Day 6 `R-Z`，自 `R-AA` 起）

| # | 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- | --- |
| **R-AA** | **瞄准方向 180° 偏移**（指示物指向与白球出射**相反**）：旧 `AimDirectionUnity` 返回 `(cosθ,0,sinθ)`，而白球在物理平面沿 `(cosθ,sinθ)` ⇒ 世界方向**应是** `(−cosθ,0,−sinθ)`（`Q-11`）。**已由 `5189d4d` 修正**（走 `PoolGeometry.PhysicsDirectionToUnity`） | ~~高~~ **已消除（留回归守卫）** | **高（正是排期验收句）** | 判决量 = `T47`（a）（+ 既有 `T42` 的经验交叉校验）；**方向一律走 `PoolGeometry.PhysicsDirectionToUnity`，别处禁手写**（`PoolGeometry.cs:95-98`） |
| **R-AB** | **1.2 双重缩放**（`anti_double_scaling`）：把规范坐标又放进 1.2 根下 ⇒ 1.44× | 中 | 高 | `PlayfieldView` **单点** + `T45`（界 == 1.2）+ `T46`（互逆）+ `T48`（顺序恒等） |
| **R-AC** | **"代码默认值 ≠ 场景生效值"**（F-10 曾发生：`maxLaunchSpeed` 场景 8 vs 代码 4.5；**`950c78f` 场景重建后已同步**，由 `T43` 守） | 中-高 | 中 | 本 spec 的 `T45`（`PlayfieldRoot` 缩放）/ `T51`（`aimStepDegrees`）/ `T53`（相机 FOV）**各自**钉"场景序列化值 == 意图值" |
| **R-AD** | 拾取**平行射线**（相机 FOV 大 / 极端像素）⇒ `t` 爆炸 ⇒ NaN 传播到 `aimDegrees` | 中 | 中 | `T49` 拒绝域（`|D.y| < eps ⇒ false`）；返回 `false` 时**不得改** `aimDegrees` |
| **R-AE** | 绕开 `PoolGeometry` 自写 `(−x,−z)` 映射（第二处坐标转换） | 中 | 高 | `R5-2` 守卫（`grep` 唯一转换点）；`T49` 的"旧映射式"注入反例 |
| **R-AF** | **协作冲突**（**已解除**）：`PhysicsTestDrive.cs` 曾被并行占用；`SyncVisual` 的局部坐标改动 / 场景重建有撞车风险 | ~~中~~ **低（已解除）** | 中 | §10.2 协调项 C-1（**已解除**：占用方已提交 `950c78f`/`5189d4d`，`HEAD = 7f76dba` 工作区干净）；`SyncVisual` 局部坐标改动**现已可做** |
| **R-AG** | 窄窗回落过度（垂直 FOV 逼近 180°）⇒ 取景畸变 / 近裁 | 低-中 | 低 | `maxVerticalFovDeg = 60f` 兜底（**乙-系数选定**）；`minSupportedAspect` 之外声明**降级**（可下探到 letterbox，本日不做） |

**降级说明（如实）**：
- `T46`/`T49` 容差取 `2e-4 m`：像素量化（1080p 下一像素 ≈ 1.4 mm @ 2 m 距离）+ float 余量。**该容差远小于 1.2 误差量级（0.2 m）** ⇒ 判别力充足。
- `minSupportedAspect` 与 `maxVerticalFovDeg` 为**选定**（无独立推导）；超域行为（letterbox）**本日不做**。

---

## 9. 外部引用定位结果（**须用户指认项**）

### 9.1 「设计说明第 2 条」（排期 `:212`）

**结论：未能在仓库中定位到**（`Q-12`）。
- 字面串 `设计说明` 在**全仓库仅出现 1 次**，即 `Docs/35-A_击球手感_制作排期.md:212` 自身。**其指代的文档不在仓库内**。
- **候选（已核，不吻合）**：美术包 `04_UI/UI全流程设计说明_参考.txt`（标题含"设计说明"）—— 其"二、界面与状态 **第 2 条 = 首页**"，**与"拖拽瞄准/松开锁定"无关**；`05_VFX/音画设计与资源说明_参考.txt` 系音画说明，无编号条目。
- **仓库内唯一**对"拖拽瞄准/松开锁定"的**具体陈述** = `Docs/35-A_Day1_工程基建_执行记录.md:165`（`AimLock` = "按下拖拽瞄准，松开锁定方向"）。本 spec 以它作**佐证**（§3.3 语义），但**不声称**它就是"设计说明第 2 条"。
- ⇒ **明写：未定位到，需用户指认**（**不猜**）。

### 9.2 「`browser-qa` 多尺寸策略」（排期 `:214`）

**结论：未在仓库中定位到**（`Q-12`）。字面串 `browser-qa` 全仓库**仅出现 1 次**，即排期 `:214`。⇒ 本 spec 用**自定策略**（16:9 基准 + 保持水平 FOV，§3.5），并把该引用标为**不可达**。

### 9.3 「竖屏也要保证台面完整」（子任务 5 的原始需求佐证）

美术包 `09_Reports/Historical_Evidence/Idea35策划与资源检查.md:59`："横屏更适合判断球线，**竖屏也要保证台面完整**和控制区域可用。" ⇒ 与本日验收"**窄窗不裁切球桌**"**同义**，可作 `T52` 的需求出处。

---

## 10. 交付物与入口分离

| # | 交付物 | 类型 |
| --- | --- | --- |
| 1 | `Runtime/Core/PlayfieldView.cs`（**新增**；显示↔规范唯一映射） | 运行时代码 |
| 2 | `Runtime/Physics/PointerAimSolver.cs`（**新增**；纯函数：拾取 + 方向） | 运行时代码 |
| 3 | `Runtime/Physics/PointerAimController.cs`（**新增**；Input System 胶水 + 交互状态机） | 运行时代码 |
| 4 | `Runtime/Physics/CameraViewportFitter.cs`（**新增**；视口适配） | 运行时代码 |
| 5 | `Editor/Visual/GreyboxSceneSetup.cs`（**改**：生成 `PlayfieldRoot`；球可视物挂其下；相机**留在根下**） | 编辑器脚本 |
| 6 | `Runtime/Physics/PhysicsTestDrive.cs`（**改**：**仅** `SyncVisual` 写入点（`:413`）改走**局部坐标**，使球可视物随 `PlayfieldRoot` 的 1.2 缩放；`AimDirectionUnity` 的方向修复**已由 `5189d4d` 落地**，**勿重复改**） | 运行时代码 |
| 7 | `Tests/EditMode/PointerAimTests.cs`（**新增**；`T45`–`T53`） | 测试 |
| 8 | `Tests/EditMode/GreyboxTuningWiringTests.cs`（**不改**：`T42` 已由 `5189d4d` 更新为 `T42_AimIndicator_FollowsRules`；本日**不碰**该文件，也不动其 `T41` / `T43` / `T44`） | 测试 |
| 9 | `Docs/35-A_Day8_相机与指针瞄准_执行记录.md` | 执行记录 |
| 10 | **ADR-0001 补注 §2.5**（两条单点 + 禁止复合；**`Q-8`**） | 文档（在实现提交内） |

### 10.1 入口分离（沿用既有铁律）

| 入口 | 职责 | 禁止 |
| --- | --- | --- |
| `GreyboxSceneSetup` | 只生成灰盒场景 | 不得调用校验；不得写 `EditorBuildSettings`；不得改 `Main.unity` |
| （本日**无**新验收器） | — | — |

### 10.2 协调项（**先于编码**）

- **C-1（已解除）**：`PhysicsTestDrive.cs` / `PhysicsTuning.cs` / `PhysicsGreybox.unity` 曾同时被另一 worker 占用（初速资产化 + 场景重建）。**该工作已提交**（`950c78f` 场景重建 + `5189d4d` `Q-11` hotfix），`HEAD = 7f76dba`、**工作区干净** ⇒ **本日可直接在其新修订上开工**。
  · 影响：交付物 5（生成器加 `PlayfieldRoot`）/ 6（`SyncVisual` 局部坐标）**现已可做**；交付物 8（改 `T42`）**已作废**（`5189d4d` 已做）。
  · **场景已重建** ⇒ §2.2 的行号已按**新场景**重新核对（F-6…F-9）。

---

## 11. 对既有质量门的影响

| 影响对象 | 变更 | 处理 |
| --- | --- | --- |
| `PoolGeometry.cs` | **不改**（映射式冻结） | `TableConfigTests` + 数据完整性验收器**不受影响** |
| `PhysicsTestDrive.cs` | **仅** `SyncVisual` 局部坐标（`:413`）；`AimDirectionUnity` **已由 `5189d4d` 修**（勿重复改） | `D4-PHY-01` 与汇编级 PhysX 守卫**仍须通过**（不得引入碰撞体/PhysX 名） |
| `GreyboxSceneSetup.cs` | 增补 `PlayfieldRoot` | 生成器**仍只生成不校验**（`R2`/`R5-4`）；不得写入 `EditorBuildSettings` |
| `MaterialVisualValidator`（`SCN-02`） | **不改**（`SCN-02` 只管 `Main.unity`） | 本日**不动** `Main.unity` ⇒ `SCN-02` 仍绿（`Q-13`） |
| asmdef | **无需改动**（新运行时代码落 `Billiards.Runtime`；新测试落 `Billiards.Tests.EditMode` 同目录自动纳入） | 若新增子目录，`.meta` 与 `rootNamespace` 须一致 |
| EditMode 测试基线 | **135**（@ `7f76dba`，源码 `[Test]` 计数） | **双向核对**（§11.1） |

### 11.1 台账同步（**占位，实现后按实测填写**）

| 项 | 基线（带修订锚点） | 增量 | 新值（预计） |
| --- | --- | --- | --- |
| EditMode 测试 | **135 @ `7f76dba`**（源码 `[Test]` 计数；`133` 是 `d99aaf1` 时的数，`950c78f` 又 +2 = `T43`/`T44`） | **+N，N = 本 spec 判据条数 = 9**（`T45`–`T53`；**一条判据一个 `[Test]`** ⇒ 约 **+9**）—— ⚠ **实现后按实测填写，不得编数字** | 预计 **144**（= 135 + 9）**@ 实现提交** —— ⚠ **待实测** |
| 四验收器 | 25 / 33 / 22 / 7 | **±0**（本日**不新增验收器**；`D4-PHY-01` 等若因改动 `PhysicsTestDrive` 而触碰，须重跑并记新锚点） | **待实测** |

> **数目以 `grep -cE "^\s*\[Test\]"` 逐文件求和 + Unity 实跑 XML 双向核对为准**（`R3-6`：报数须带修订锚点，权威数字由复核方在冻结修订上测）。**本表为占位，不得被引用为实测。**

---

## 12. 复核安排

| 阶段 | 责任 | 内容 |
| --- | --- | --- |
| 独立评审（可验证性） | 复核方 | ① 逐条核对 §6 期望值**分类**（甲/乙/乙-系数选定/**丙**），确认 **丙 = 0**；② 检查**盲带**未被误当检出保证；③ 核 `T47` 是否**真能打死 180° 偏移**（把 `AimDirectionUnity` 改回 `(cosθ,0,sinθ)` 应 FAIL）；④ 核 `T48` 的顺序恒等论证（§8.2）是否成立、`T48` 是否**不恒真** |
| 独立评审（可实现性） | 复核方 | ① `Q-8` 的"两条单点"是否与 `ADR-0001 §7.1` 相容（或补注是否充分）；② `Q-10` 的 β 顺序是否与 `PoolGeometry`/`PlayfieldView` 的边界一致；③ 找出"照做会卡住"之处（**唯一跨文件项 = `PhysicsTestDrive.SyncVisual` 的局部坐标 1 行**；C-1 **已解除**） |
| 独立复算（G5） | 复核方 | **不共享代码路径**地重算 §8.2 的 α vs β 恒等（用独立脚本/手算，取 3 个屏幕像素）；重算 `T46`/`T49` 的独立闭式期望 |
| 实现 | engineering-lead | §10 交付物 1–10（C-1 **已解除**，可直接做） |
| 实现方自验 | engineering-lead | 跑通 `T45`–`T53`，产出 §7 证据（`-outputDir` 指向仓库外） |
| 独立复核 | 复核方 | ① 冻结锚点核验（`git diff <提交> -- <冻结路径>` 为空）；② 复现 `T47`/`T48`/`T50` 的注入反例**确实 FAIL**；③ 核 `T45`/`T51`/`T53` 的"场景值 == 意图值"确被断言；④ 核无**第二处**坐标转换（`grep`）；⑤ 核 `T52` 六档比例实测 `viewport` 值 |

**复核必须能失败**：若把 `AimDirectionUnity` 改回 `(cosθ,0,sinθ)` 而 `T47` **仍通过** ⇒ `T47` 无效（说明它读的是被测对象自身）；
若把 `PlayfieldRoot.localScale` 设 `1.2²` 而 `T45`/`T46` **仍通过** ⇒ 缩放守卫是空气；
若把"松开冻结"删掉而 `T50` **仍通过** ⇒ 状态机判据无效。

---

## 13. 裁定与待裁定

> **已裁定（本 spec 定稿口径）**：`Q-8` / `Q-9` / `Q-10` / `Q-11` / `Q-13`。
> **需用户/主理人指认**：`Q-12`（两项外部引用未定位到）。

### 13.1 已裁定

| ID | 议题 | **裁定 / 决定** | 落地位置 |
| --- | --- | --- | --- |
| **Q-8** | 1.2 映射**是否**走 `PoolGeometry` 单点 | **不并入**。`PoolGeometry` 仍是唯一**坐标系（轴/手性/单位）**转换点；`1.2` 是**表现层等比缩放**，落**新单点** `PlayfieldView`（`Billiards.Core`）。**边界**：`PlayfieldView` 位于 `PoolGeometry` **下游**（先 `PhysicsToUnity` 得规范世界坐标，再缩放；反向先 `CanonicalFromDisplay`，再 `UnityToPhysics`）；**两者不得复合**。**需 ADR-0001 补注 §2.5**（在实现提交内完成）。备选（并入 `PoolGeometry`）未采纳：会把表现层混进坐标核心，且强触 `ADR §7.5` 的回归门 | §3.1、§4.2 契约 1/2 |
| **Q-9** | 0.2° 是**基础**还是**细调**？ | **细调步长**。基础 `aimStepDegrees = 1.0°`，细调 = `×0.2 = 0.2°`（按住 `fineKey`）。依据：排期用词"**微调**"= 细调路径；现状 `1.0×0.2=0.2` 恰得该值。**键向**沿用 `←`=+、`→`=− | §3.4、§5 `T51` |
| **Q-10** | 1.2 缩放与指针拾取的**顺序** | **钉 β：先反缩放（射线→规范空间）→ 再投影 `y=0` → 再 `UnityToPhysics`**。**并证明 α ≡ β**（§8.2：对任意可逆线性 `S`、被 `S` 固定的平面，二者恒等）⇒ **"顺序选错"不是可能事件；α ≠ β 只是 bug 症状**。**唯一分叉条件**：`S` 在物理平面内**非等比**（本工程取 uniform ⇒ 不进入）。**判据**：`T48` | §3.2、§8.2、§5 `T48` |
| **Q-11** | 瞄准方向的**唯一口径** | **世界瞄准方向 = `PoolGeometry.PhysicsDirectionToUnity(物理方向)`** ⇒ `(cosθ,sinθ)_物理 → (−cosθ, 0, −sinθ)_世界`（`PoolGeometry.cs:95-98`）。**已落地**：`5189d4d` 修 `AimDirectionUnity`（`PhysicsTestDrive.cs:100-107`）+ 更新 `T42`（期望式 `:217`）⇒ **勿重复改**；本 spec 的 `T47` 为其**回归守卫**。**方向一律走该入口，别处禁手写** | §3.2、§3.3、§5 `T47`、§6 |
| **Q-13** | `Main.unity` 的 `PlayfieldRoot` 是否也上 1.2 | **本轮不动 `Main.unity`**（其 `PlayfieldRoot` 保持 scale=1，`SCN-02` 仍绿）。1.2 只在**灰盒**落地（本日的相机与指针瞄准在灰盒验证）。**若将来 `Main.unity` 要上 1.2**，须**另开变更**同步修订 `SCN-02`（`:233-242`）——**不得**夹带在本日内 | §1.2、§11、§3.1 |

### 13.2 需**用户/主理人指认**（不猜）

| ID | 议题 | 现状 | 需要 |
| --- | --- | --- | --- |
| **Q-12a** | 排期 `:212` 的「**设计说明第 2 条**」指哪份文档的哪一条？ | **未定位到**（§9.1） | 用户指认文档路径与条目号 |
| **Q-12b** | 排期 `:214` 的「**`browser-qa`** 多尺寸策略」出自何处？ | **未定位到**（§9.2） | 用户指认，或确认采用本 spec 的自定策略 |

---

## 附：执行注意事项（本机环境，防踩坑）

1. **Unity 批处理必须钉死国际版路径**：`E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`
   （**不要**用 `E:/Unity_Deve/Unity_Tools/Editor/Unity.exe`＝中国版 ⇒ 会按 `packages.unity.cn` 强制重解析，
   破坏 `Library/PackageCache` 并改写 `packages-lock.json`）。见 `工程标准.md §7 第 9 条` 与 `§8 G8`。
2. **单实例**：跑批处理前确认 `Temp/UnityLockfile` 不存在。
3. **`-runTests` 不带 `-outputDir` 什么都不写**；要落证据必须**显式带** `-outputDir` 并指向**仓库外**（`ReportOutput` 单点，F-24）。
4. **跑前跑后各查一次** `git status --porcelain`（尤其 `Packages/packages-lock.json`）。
5. **本 spec 的提交**用 **path-limited**：`git commit -m '…' -m '…' -- Docs/35-A_Day8_相机与指针瞄准_spec.md`
   （`-m` 必须在 `--` 之前；**不得** `git add -A`、**不得**动 `Docs/Reports/**` 与他人文件）。
6. **`Runtime` 源码里不得出现 `Collider` / `Rigidbody` 名字**（`D4-PHY-01` 扫名 + 汇编级 PhysX 守卫）——
   新写入 `Runtime/Physics/*` 的代码须遵守（既有 `PhysicsTestDrive.EnsureAimIndicator` 用字符串拼接规避，见 `:432-444`）。

---

## 附录 A：本 spec 与既有文档的锚点

| 关联 | 文件 |
| --- | --- |
| 排期 | `Docs/35-A_击球手感_制作排期.md`（§Day 8 `:206-216`；R5 `:72`） |
| 坐标契约 | `Docs/adr/ADR-0001-坐标系与手性约定.md`（§2.1 `:50-61`、§2.3 `:79-86`、§2.4 `:92-97`、§7 `:182-191`） |
| 标准 | `Docs/工程标准.md`（§1/§3/§4/§5/§6/§7/§8） |
| 铁律 | `Docs/项目纪律铁律.md`（16 `:179-190`、17 `:191-205`、18 `:207-211`） |
| 格式范本 | `Docs/35-A_Day6_袋口捕获与终止条件_spec.md`、`Docs/35-A_Day5_球-球碰撞_spec.md` |
| 台账 | `Docs/测试数基线台账.md` |
| 交互佐证 | `Docs/35-A_Day1_工程基建_执行记录.md:155-174`；`Assets/_Project/Settings/Input/Billiards.inputactions` |
| 外部需求佐证 | `F:\工作文件\idea35_台球\05_VFX\音画设计与资源说明_参考.txt`（"120% 显示比例"）；`…\09_Reports\Historical_Evidence\Idea35策划与资源检查.md:59`（"竖屏也要保证台面完整"） |
