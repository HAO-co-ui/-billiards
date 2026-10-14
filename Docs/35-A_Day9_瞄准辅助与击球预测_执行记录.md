# 35-A Day 9 · 瞄准辅助与击球预测 —— 执行记录（**切片 A + 切片 B**）

> 规格：`Docs/35-A_Day9_瞄准辅助与击球预测_spec.md`（v1，定稿 `b23a6e8` + 修订 v1.1 `f9c56a9`）。
> 本记录覆盖 **两个切片**：**切片 A**（内核只读诊断 + 预测核，`T72`–`T76` + `T72b`）与
> **切片 B**（出射线 / 擦库 / 绘制 / 开关 / 性能，`T77`–`T83`）。切片 B 由本单交付。

| 项 | 值 |
| --- | --- |
| 基线（切片 A） | **`HEAD = f697378`**；`git status --porcelain` 为空；**EditMode 源码 `[Test]` 计数 = 159** |
| 基线（切片 B） | **`HEAD = d83b941`**；`git status --porcelain` 为空；**EditMode 源码 `[Test]` 计数 = 165**（实测） |
| EditMode（切片 A 后，实测） | **165 / 165 @ `d83b941`**；failed 0 / skipped 0 |
| EditMode（切片 B 后，实测） | **172 / 172 @ 工作区**；failed 0 / skipped 0；源码 `[Test]` 计数 **172**（**双向一致**） |
| 四验收器（切片 B 后，实测） | **25 / 33 / 22 / 7**，FAIL = 0，`overallStatus = PASS`（`-outputDir` 全指仓库外） |
| Unity | `6000.0.49f1`（国际版：`E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`） |

---

## 1. 交付物

### 1.1 切片 A（`d83b941`，已在库）

| # | 文件 | 类型 | 说明 |
| --- | --- | --- | --- |
| 1 | `Runtime/Physics/PoolSimulator.cs` | 改 | 新增只读诊断 `BallBallContactRecord` / `HasBallBallContact` / `FirstBallBallContact`（记录点 = `ResolveBallBallContact` **入口**，K-1 单点；不改解算路径） |
| 2 | `Runtime/Physics/ShotPredictor.cs` | 新增 | 纯静态推演核（独立探测内核 + 首次接触即停） |
| 3 | `Runtime/Physics/ShotPrediction.cs` | 新增 | 结果值类型（含只读诊断出口） |
| 9 | `Tests/EditMode/PoolSimulatorBallBallContactDiagnosticTests.cs` | 新增 | `T72b`（单点 + 惰性 + 清零语义 + `T55` 回归） |
| 10 | `Tests/EditMode/ShotPredictorTests.cs` | 新增 | `T72`–`T76` |

### 1.2 切片 B（**本单**）

| # | 文件 | 类型 | 说明 |
| --- | --- | --- | --- |
| 4 | `Runtime/Physics/AimAssistController.cs` | **新增** | 胶水：读开关 → 脏标记 → `ShotPredictor.Predict` → 缓存；**只读**内核；只读出口 `Prediction` / `PredictionComputeCount` |
| 5 | `Runtime/Physics/AimAssistView.cs` | **新增** | 纯几何（`BuildDashSegments`）+ 显示层单点（`DrawPoint` / `DrawScale`）+ 方向单点（`WorldDirection`）+ Scene 预览 + PlayMode `LineRenderer` 虚线 |
| 6 | `Runtime/Physics/PhysicsTestDrive.cs` | 改 | 新增 `enableShotPrediction` 字段 + `PredictionEnabled` 唯一读点属性（与 `aimIndicator*` 同处）；**不动任何物理/交互语义** |
| 7 | `Editor/Visual/GreyboxSceneSetup.cs` | 改 | 生成并接线 `AimAssist` 宿主（`AimAssistController` + `AimAssistView`）；仍**只生成不校验** |
| 8 | `Scenes/PhysicsGreybox.unity` | 改 | 重建（对象 11 → **12**，+`AimAssist`；序列化 `enableShotPrediction: 1` / `maxPredictSteps: 240`） |
| 11 | `Tests/EditMode/AimAssistViewTests.cs` | **新增** | `T79` / `T80` / `T81` / `T82` |
| 10 | `Tests/EditMode/ShotPredictorTests.cs` | 改（**追加**） | 追加 `T77` / `T78` / `T83`（**`T72`–`T76` 一字未动**） |
| 12 | 本文件 | 新增 | 执行记录 |

> **未被触碰（冻结）**：`ShotPredictor.cs` / `ShotPrediction.cs` / `PoolSimulator.cs` —— 本单**未改其一行**
> （`git diff d83b941 -- <这三个路径>` 为空）。全部注入均为**临时**并已逐字节还原（sha1 见 §4）。

---

## 2. 待实测定值（**实测回填**，spec §8.3）

| 项 | 用途 | **实测值** | 依据 |
| --- | --- | --- | --- |
| `maxPredictSteps` | `AimAssistController` 步数上限（`T83b/c`） | **240** | 银行球首接触实测需 **78** 步 ⇒ 取 ≈3.1× 余量（`T83` 实测 `StepsSimulated`：直球 15 / 斜碰 2 / 银行球 78 / 4 球堆 21 / 打不到 240） |
| `PredictionDashLength` / `PredictionGapLength` | 虚线几何（`T80`） | **0.06 / 0.025（规范米）** | 1.2 根下世界段长 0.072 m / 世界间隙 0.030 m；见 §5 偏差 3（空间口径） |
| `MinGapWorld` | 屏幕 ≥1 px 对应的**世界米数**（`T80d`） | **0.0017 m** | 灰盒 16:9 基准（FOV 52）桌面中心实测 `viewport/米 = 0.310438871` ⇒ 1920 px 宽下 **596.04 px/米** ⇒ 1 px = **0.00167773 m** |
| `d_min` | 银行球"幽灵球离直线"下界（`T78a`） | **0.02 m** | 银行球场景实测幽灵球离直线 **0.041232 m** ⇒ 下界取约一半 |
| `TotalContactCount` 与 **6746** 的比值门限 | `T83d` | **≥ 10×** | 实测比值：直球 **2248×** / 斜碰 **3373×** / 银行球 **2248×** / 4 球堆 **963×** / 打不到 **1686×** |
| 参考场景的 `ghostBall` / 方向 / `StepsSimulated` / `TotalContactCount` / `elapsedMs` | 证据数值（§6.1） | 见 §3 实测行 | 实跑记录 |

### 2.1 银行球场景的实测几何（`T78` 的构造依据）

白球 `footSpot (0.51, 0)`、目标球 `(−0.40, 0.30)`、瞄准 **172.50°**、力度 1（初速 4.5 m/s）⇒ 先撞**左长库**（`x = −1.27`）再命中目标。

| 量 | 实测 |
| --- | --- |
| 首次接触前库边反弹次数 | **1** |
| 幽灵球 | `(−0.42467466, 0.35154888)` |
| 目标球心 | `(−0.40000000, 0.30000000)` |
| 幽灵球到直线 `c0→t` 的距离 | **0.041232 m** |
| `StepsSimulated` / 到首接触时间 | **78** / `0.6453641 s` |
| 目标球出射 / 白球出射 | `(0.431752384, −0.9019922)` / `(0.905297, 0.4247792)` |

---

## 3. 判据与实测（`T77`–`T83`）

跑法：`-runTests -testPlatform EditMode -testFilter <判据名>`（**不带 `-quit`**），`-outputDir` 指向**仓库外**。

| 判据 | 结果 | **实测值**（`TestContext` 原文） |
| --- | --- | --- |
| `T77` 斜碰闭式 | PASS | `φ=5°: Δghost=6.05E-009 ∠tgt=0.0000° ∠cue=0.0000°；φ=20°: Δghost=4.17E-009 ∠tgt=0.0000° ∠cue=0.0000°；φ=30°: Δghost=2.33E-009 ∠tgt=0.0000° ∠cue=0.0000°` |
| `T78` 擦库与实跑一致 | PASS | `反弹 1 次；幽灵球离直线 0.041232 m (> d_min 0.02)；Steps=78；幽灵球/目标球心/时间**逐位**等于实跑；两方向逐分量差 ≤1e-6` |
| `T79` 绘制过显示层 | PASS | `绘制点 == 显示映射（max 0.00E+000）/ == TransformPoint（max 0.00E+000） / == ×1.2（max 0.00E+000）；合成根 1.7× 通过；源码无字面量 1.2` |
| `T80` 虚线可判定 | PASS | `gap=0.025 > 0；L=0.02000001⇒n=1 L=0.08500001⇒n=2 L=0.099999994⇒n=2 L=0.2⇒n=3 L=0.5⇒n=6 L=1⇒n=12 L=2.5⇒n=30；DrawScale=1.2；世界间隙 0.0300000012 m ≥ MinGapWorld 0.0017 m` |
| `T81` 方向单点 / 180° 守卫 | PASS | `4 个方向 == PoolGeometry 契约式 == PointerAimSolver；30° ⇒ (-0.87, 0.00, -0.50)` |
| `T82` 开关：关掉即不算 | PASS | `开启 n1=1（steps=38，has=True）；关闭 5 次仍 n2=1 且 Prediction 逐位不变；重开 n3=2` |
| `T83` 成本上限 | PASS | `上界 maxPredictSteps=240；直球: steps=15 bb=3 tot=3 bounce=0 ratio≈2248 ms=1.78；斜碰20°: steps=2 bb=2 tot=2 bounce=0 ratio≈3373 ms=0.33；银行球: steps=78 bb=2 tot=3 bounce=1 ratio≈2248 ms=8.74；4球小堆: steps=21 bb=7 tot=7 bounce=0 ratio≈963 ms=4.16；打不到: steps=240 bb=0 tot=4 bounce=4 ratio≈1686 ms=27.26` |

---

## 4. 注入负对照（**本单最重要**：每条判据都能被打死）

> 注入均落 **`ShotPredictor.cs` / `AimAssistView.cs` / `AimAssistController.cs`**（**临时**；备份在仓库外）。
> 每条注入后**逐字节还原**（sha1 相等；**未用** `git checkout --`），最终 `git diff` 对冻结文件为空。

| 注入 | 改哪里 | 期望 FAIL | **实测**结果 |
| --- | --- | --- | --- |
| **INJ-77** 幽灵球漏横截项（`s* = s`） | `ShotPredictor`：幽灵球取"起点沿初速方向的直线上离目标球心最近的点" | `T77(a)(b)` | **`T77` FAIL**（a/b/c 三档全红） |
| **INJ-78** **解析直线近似**（忽略库边） | 同上 + 反弹数置 0 | `T78(a)` **与** `(b)` **同时红** | **`T78` FAIL，且 (a)(b) 同时红**（见下原文） |
| **INJ-79** 绘制点绕过显示层 | `AimAssistView.DrawPoint` → `return canonicalWorldPoint;` | `T79(a)` | **`T79` FAIL**（4 条子判据） |
| **INJ-80** 间隙置 0（拼成实线） | `PredictionGapLength = 0f` | `T80(a)` | **`T80` FAIL**（(a) 与 (d)） |
| **INJ-81** 旧口径方向（180° 反向） | `WorldDirection` → `new Vector3(d.x, 0f, d.y)` | `T81(a)(b)` | **`T81` FAIL**（4 个方向全红） |
| **INJ-82** 只藏不算 | `AimAssistController`：关闭时照样 `Predict`，只是不发布 | `T82(a)` | **`T82` FAIL**（`1 → 6`） |
| **INJ-83** 跑到整盘静止 | `ShotPredictor`：循环条件改 `!probe.TableAtRest`、删 `break` | `T83(a)` | **`T83` FAIL**（4 个接触场景的"最后一步窗口"全红） |

**失败原文（节选，逐字）**：

- `INJ-77` →`T77`：`(a) φ=5°：幽灵球 0.04924039;0.008682408 ≠ 闭式 -0.007026192;0.0037597206（差 5.648E-002 m）。`
  `(b) φ=5°：目标球出射 0.9978337;-0.06578688 与球心连线 0.08715537;-0.9961947 夹角 81.2280° ≥ 0.05°。`
  `(c) φ=5°：白球出射 0.224521279;0.974469244 ≠ 闭式 0.9961948;0.08715539（夹角 72.0253° ≥ 0.05°）。`
- `INJ-78` →`T78`：`(a) 首次接触前的库边反弹次数 0 < 1（银行球必先撞库；0 说明预测走的是直线）。`
  `(b) 幽灵球不逐位相等：预测 -0.423319042;0.1228739 / 实跑 -0.42467466;0.35154888。`
- `INJ-79` →`T79`：`(a) 绘制点 ≠ PlayfieldView 显示映射点（最大差 1.020E-001 m）⇒ 绘制未过显示层。`（+ 引擎/闭式/合成根各一条）
- `INJ-80` →`T80`：`(a) 间隙 0 不为正 ⇒ 拼成实线（"糊"）。` / `(d) 世界间隙 0 m < MinGapWorld 0.0017 m（屏幕 <1 px）。`
- `INJ-81` →`T81`：`(a) WorldDirection((0.94, 0.34)) = (0.94, 0.00, 0.34) ≠ PoolGeometry 契约式 (-0.94, 0.00, -0.34)。`
- `INJ-82` →`T82`：`(a) 关闭期间 PredictionComputeCount 变了：1 → 6（关闭仍在算）。`
- `INJ-83` →`T83`：`(直球) 首接触时间 0.121452957 不在最后一步窗口 (1.99166679, 2] 内 ⇒ 预测**没有**在首接触那一步停下（跑过头了）。`（4 个场景各一条）

**还原 sha1（实测）**：`ShotPredictor.cs = 8fa576cbe05f4e28ccb721f211078a1b23f04ed7`、
`AimAssistView.cs = 0f9fb464a0a6d322f36c7fe832a809f249a39b1e`、
`AimAssistController.cs = a255dcffe95c3bfb552cff07cfe9190913d2cb16`（= 各注入前基线，逐字节相等）。

---

## 5. 与 spec 的**偏差**（如实列出，附实测）

1. **`T83a` 的 "`BallBallContactCount == 1`" 不可达** —— 一个 `Step` 含 **3 个子步**，首接触那一步内可解算**多次**球-球接触。
   实测：直球 **3** / 斜碰 **2** / 银行球 **2** / 4 球堆 **7** / 打不到 **0**。
   ⇒ 按 spec 的**构造目的**（"首次接触即停、不跑到静止"）改用**可判定的结构形式**：
   `BallBallContactCount ≥ 1` **且** `(StepsSimulated−1)·dt < TimeToFirstContact ≤ StepsSimulated·dt`
   （接触恰落在**最后一步**内）。**未放宽**：该形式对被注入的"跑到静止"必红（`INJ-83` 已证）。
2. **`T77` 的三档 φ 在 spec 举例的 `d = 0.6 m` 下不可达** —— `d·sinφ ≤ 2r` 要求 `φ ≤ 5.46°`（`2r = 0.05715`）。
   ⇒ 按 spec 同一条约束（"全部仍能接触"）改用 **`d = 0.10 m`** ⇒ 最大 `φ ≈ 34.8°` ⇒ `φ = 5° / 20° / 30°` 三档**全部可接触**。
3. **`PredictionDashLength` / `PredictionGapLength` 的空间口径**：spec §4.1 标为"**世界米**"，但 `T80c` 要求
   "**世界段长 == 规范段长 × `DrawScale`**"。二者不能同时成立 ⇒ 本实现统一取 **规范米**（与 `T80c` 一致，
   也与 `BuildDashSegments` 与路径点同空间的语义一致）；`T80d` 的断言写作 `gap × DrawScale ≥ MinGapWorld`。
4. **`AimAssistView` 的 `LineRenderer` 只在 PlayMode 生效**（`Application.isPlaying` 守卫）——
   避免 EditMode 打开场景时产生子对象、污染场景快照（`T45`/`T71` 都读场景）。表现层的 Game 视图效果属 **Manual**。
5. **`T43` 是否扩面覆盖 `enableShotPrediction`（`Q-20`）**：**未扩**（本单不自行决定）。
   `T43` 是**显式字段清单**，不含新字段；本 bool 的"场景漂移"目前**无守卫**（漂移后果 = 预测线默认隐藏/显示，非物理）。
   若要求覆盖，只需加一行 `Assert.AreEqual(probe.enableShotPrediction, drive.enableShotPrediction)`（**属扩面**）。

---

## 6. 未测 / 明确不做

- **观感**（虚线好不好看）与**真实帧率**：Manual（无 CI / 无固定机型；`T83` 只判"有界 + 停在首接触步 + 量级远低于整盘"）。
- **输入设备级**（真鼠标拖拽看预测线）：Manual / PlayMode。
- **`T78b` 的同源盲带**：期望值与被测对象**同内核** ⇒ 检的是**预测管线绑定**，**不**能检出内核物理缺陷
  （内核物理由 Day 4–6 的判据承担）。该范围已**写在 `T78` 判据条目内**。
- **`T83d` 的判别力受场景规模限制（实测发现，如实登记）**：实测 `INJ-83`（"跑到静止"）**只打红 (a)**，
  **(d) 未红** —— 因为参考场景最多 4 球，接触总量（≤7）本就远小于 15 球满堆的 6746；
  ⇒ `(d)` 的"远小于 6746"在本场景集下**恒过**，其判别力需在 15 球场景才有意义。**未**为此放宽任何阈值。
- **`Main.unity` 的预测线落地**：本日只在灰盒验证（与 Day 8 `Q-13` 同口径）。
- **方向量比对的容差**：`T78b` 的**位置与时间**为**逐位**（`ToString("R")` 相等）；
  两个**方向向量**因"实跑侧需自行归一化"而用**逐分量 1e-6** 容差（同一归一口径的 float 级联差异）。**如实登记**。

---

## 7. 工具链与收尾（实测）

- 编辑器：`E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`（国际版）。跑前查 `Temp/UnityLockfile` **不存在**；无 `taskkill`。
- `-runTests` **不带 `-quit`**；`-executeMethod` **带 `-quit`**；`-outputDir` 全部指向 **`E:/Unity_Deve/_d9b_out/`（仓库外）**。
- **Day1 只跑 verify-only 入口** `BilliardsProjectSetup.VerifyProjectConfigInteractive`（**25 项，无 `RUN-01`**）。
- **四验收器（实测）**：Day1 **25** / Day2 **33** / Day3 **22** / Day4 **7** —— **FAIL = 0**，`overallStatus = PASS`。
- **全量 EditMode（实测）**：**172 / 172**，failed 0 / skipped 0；源码 `[Test]` 计数 **172**（双向一致）。
- **冻结内核回归（实测）**：`T32b` / `T37` / `T72b` **全绿** ⇒ 本单未触碰内核（`git diff d83b941 -- PoolSimulator.cs ShotPredictor.cs ShotPrediction.cs` 为空）。
- 跑前 `git status --porcelain` **为空**；跑后**仅**本节交付物 —— 无 `Docs/Reports/**` 改动、无 `Docs/Reviews/**` 改动、无 `Packages/packages-lock.json` 改动。
