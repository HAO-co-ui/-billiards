# 35-A Day 8 · 相机与指针瞄准 —— 执行记录（**切片一**）

> 规格：`Docs/35-A_Day8_相机与指针瞄准_spec.md`（定稿 `9d8c5af` + 后续修订）。
> 本记录对应 **切片一**（交付物 1 / 2 / 5 / 6 / 7 + ADR 补注），判据 **`T45`–`T49`**。
> **`T50`**（拖拽瞄准状态机）/ **`T51`**（微调粒度）/ **`T52`**/ **`T53`**（视口适配）**不在本单**，留待下一单。

| 项 | 值 |
| --- | --- |
| 基线 | `HEAD = 6a531f0`（跑前 `git status --porcelain` **为空**） |
| EditMode 基线（旧） | 本 spec 定稿锚点 **135 @ `7f76dba`** |
| EditMode 基线（**实现时实测**） | **149 @ `6a531f0`** |
| EditMode（本切片后，**实测**） | **154 / 154 @ 工作区（未提交）**，failed 0 / skipped 0 |
| 四验收器（**实测**） | **25 / 33 / 22 / 7，FAIL = 0**（`-outputDir` 全部指向仓库外） |
| Unity | `6000.0.49f1`（国际版：`E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`） |

---

## 1. 交付物（本切片）

| # | 文件 | 类型 | 说明 |
| --- | --- | --- | --- |
| 1 | `Assets/_Project/Scripts/Runtime/Core/PlayfieldView.cs` | **新增** | 「显示世界 ↔ 规范世界」**唯一**映射（`DisplayFromCanonical` / `CanonicalFromDisplay` / `HasExpectedScale`）；只用 `TransformPoint` / `InverseTransformPoint`，**不硬编码 1.2** |
| 2 | `Assets/_Project/Scripts/Runtime/Physics/PointerAimSolver.cs` | **新增** | 纯静态拾取核：`TryPickPhysicsPoint`（β 顺序）/ `AimDirectionPhysics` / `WorldDirectionFromPhysics`（**委托** `PoolGeometry.PhysicsDirectionToUnity`） |
| 5 | `Assets/_Project/Scripts/Editor/Visual/GreyboxSceneSetup.cs` | **改** | 生成 `PlayfieldRoot`（`localScale = 1.2`）；球可视物挂其下；**相机 / 光 / Gizmo / 驱动器仍留 `PhysicsGreybox` 根下** |
| 6 | `Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs` | **改** | **仅** `SyncVisual` 写入点：`visual.position` → `visual.localPosition`（滚动自转 `ApplyRollingRotation` 未动 —— 「位移/半径」比值在等比缩放下不变） |
| 7 | `Assets/_Project/Tests/EditMode/PointerAimTests.cs` | **新增** | `T45`–`T49`（5 条）+ `d8-pointer-aim.json` 证据落盘 |
| 10 | `Docs/adr/ADR-0001-坐标系与手性约定.md` | **补注** | 新增 **§2.5**（两条正交单点 + 禁止复合）；表头 `关联产物` 补 `PlayfieldView.cs` |

> 未做（按边界）：交付物 3（`PointerAimController`）/ 4（`CameraViewportFitter`）属 `T50`–`T53`，**本单不碰**。

---

## 2. 场景重建 churn（**实测**）

新增场景对象 ⇒ 用生成器**强制重建**（`GreyboxSceneSetup.RebuildFromCommandLine`，本工程「配置即代码」姿势；`Apply` 是幂等 no-op，不适用）。

**对象名集合（`m_Name` 非空行，排序后 diff）**：

```
旧（`git show HEAD:...`）：9 个
  CueBall / CushionGizmo / Directional Light / Main Camera /
  PhysicsGreybox / PhysicsTestDrive / RackBall_1 / RackBall_2 / RackBall_3
新：10 个
  上述 9 个 + PlayfieldRoot
diff：仅 `> PlayfieldRoot`（一行）
```

**层级变化（实测）**：

| 归属 | 旧 | 新 |
| --- | --- | --- |
| `PhysicsGreybox` 根的直接子物体 | 8（CueBall / CushionGizmo / PhysicsTestDrive / RackBall_1..3 / Main Camera / Directional Light） | **5**（CushionGizmo / PhysicsTestDrive / **PlayfieldRoot** / Main Camera / Directional Light） |
| `PlayfieldRoot` 的直接子物体 | — | **4**（CueBall / RackBall_1..3） |

**churn 说明（如实）**：本工程已实测**场景重建非幂等** —— Unity 为运行期创建的对象分配随机会话内 `fileID` 并按该 ID 排序落盘 ⇒ 同一代码重建出的 `.unity` **逐字节必然不同**。
本次重建后 `.unity` 的**每个对象的 fileID 都变了**（例如根 `97929637` → `814252034`）。**但对象名集合与对象数不变（9 → 10，仅 +`PlayfieldRoot`）**，且既有序列化值同步刷新（`aimDegrees=180` / `maxLaunchSpeed=4.5` / `rollingDeceleration*=0.5/0.25` / `tuning` 接线等 —— 与代码默认一致，由 `T41`/`T43` 持续守住）。

`PlayfieldRoot` 序列化值（实测）：`m_LocalPosition = {0,0,0}`、`m_LocalRotation = {0,0,0,1}`、`m_LocalScale = {1.2, 1.2, 1.2}`。

---

## 3. 判据与实测（`T45`–`T49`）

跑法：`-runTests -testPlatform EditMode -testFilter PointerAimTests`（**不带 `-quit`**）+ `-outputDir` 指向**仓库外**。

| 判据 | 结果 | **实测值**（`TestContext` 原文） |
| --- | --- | --- |
| `T45` 缩放锚定 | PASS | `PlayfieldRoot.localScale=(1.20, 1.20, 1.20)（意图 1.2）、rot=id、pos=0` |
| `T46` 视觉/拾取互逆 | PASS | `反解物理位 (0.51, 0.00) vs 模拟器位 (0.51, 0.00)，误差 0.000002 m`；**口径实测**：`球心像素拾取偏移 = 0.013394 m` |
| `T47` 方向无偏移（验收） | PASS | `(a) 最大夹角 0.0000°；(b) 出射夹角 0.0000°（覆盖 4 个采样点）` |
| `T48` 顺序恒等 | PASS | `α ≡ β ⇒ 最大差 8.170E-008 m（覆盖 6 个像素）` |
| `T49` 精度 + 拒绝域 | PASS | `精度最大误差 7.089E-006 m（覆盖 6 点）；近平行射线均被拒绝` |

**证据 JSON**（`<outputDir>/d8-pointer-aim.json`，§7.1 必填字段齐全）：`playfieldScale=1.2`、`cameraFov=40`、`cameraAspect=1.3333`（**批处理**下相机 `pixelRect` 为 640×480 ⇒ 非 16:9；本切片判据均为**一致性/绝对值**判据，不依赖 aspect）、`alphaBetaMaxDeltaM=2.98e-8`、`maxAngleErrDeg=0`、`launchVsExpectedAngleDeg=0`，4 个采样点的 `screenPx` / `pickedPhysics*` / `expectedPhysics*` / `angleErrDeg`。
> 「6 档宽高比的四角 `viewport`」属 `T52` 视口适配（**下一单**），本切片**不产出**该段。

---

## 4. 注入负对照（**本单最重要**：每条判据都能被打死）

跑法：改 SUT/场景 → `-testFilter PointerAimTests`（`T47` 那条另跑 `GreyboxTuningWiringTests`）→ 记失败原文 → **逐字节还原**（仓库外备份 + `sha1sum` 复核 + `git status` 复核）。

| 注入 | 改哪里 | 期望 FAIL | **实测**结果 |
| --- | --- | --- | --- |
| **INJ-1** 双缩放 | 场景 `PlayfieldRoot.localScale = 1.44` | `T45` | **仅 `T45` FAIL**；`T46`/`T47`/`T48`/`T49` **仍过**（视觉端缩放由 `T45` 独有覆盖 —— 与各条**盲带声明**一致） |
| **INJ-2** 漏缩放 | 场景 `PlayfieldRoot.localScale = 1.0` | `T45` | **仅 `T45` FAIL** |
| **INJ-3** 拾取端漏反缩放 | `PointerAimSolver`：`origin/direction` 不反缩放 | `T46`/`T47`/`T48`/`T49` | **`T46`/`T47`/`T48`/`T49` 全 FAIL**（`T45` 过） |
| **INJ-4** 瞄准方向 180° 反向（`Q-11` 回归守卫） | `PhysicsTestDrive.AimDirectionUnity` 改回 `(cosθ,0,sinθ)` | `T47(a)`（+ 既有 `T42`） | **`T47(a)` FAIL=180.0000°（4 个采样点全部）**；**`T42` FAIL=180.0°**；`T47(b)` 仍过（(b) 用 `Launch()` 的物理角，不含该属性） |
| **INJ-5** 非零 `localPosition` | 场景 `PlayfieldRoot.m_LocalPosition = (0.1,0.05,0.2)` | `T48`（+`T45`） | **`T45` FAIL**（`localPosition (0.10,0.05,0.20) ≠ 0`）、**`T48` FAIL**（`|α−β|` 达 `2.562E-002 m`）、`T47(a)(b)` / `T49` 连带 FAIL；`T46` 仍过 |
| **INJ-6** 删近平行检查 | `PointerAimSolver`：删 `|D.y| < eps` 块 | `T49` 拒绝用例 | **仅 `T49` FAIL** |
| **INJ-7** 旧映射式 | `PointerAimSolver`：`UnityToPhysics` → `(x, z)` | `T49`（+`T46`/`T47`/`T48`） | **`T46`/`T47`/`T48`/`T49` FAIL**（符号翻转，内库角误差 `2.840E+000 m`） |

**注入失败原文（节选，逐字）**：

- `INJ-1` →`T45`：`缩放 (1.44, 1.44, 1.44) ≠ 意图 1.2（逐分量，容差 1e-6）⇒ 双缩放 / 漏缩放。`
- `INJ-3` →`T46`：`屏幕反解物理位 (0.61, 0.00) 与模拟器位 (0.51, 0.00) 误差 0.102001 m 超容差 2e-4 m（反缩放互逆性被破坏）。`
- `INJ-4` →`T47`：`（a）像素 (320.00, 221.31)：指示物朝向与独立世界方向夹角 180.0000° ≥ 0.1°（180° 反向 / 拾取偏移 / 链路不一致）。`（4 个采样点各一条）
- `INJ-5` →`T48`：`像素 (320.00, 360.00)：|α−β| = 2.562E-002 m > 1e-6 m（β=(0.08, -0.26)，α=(0.08, -0.28)）。`
- `INJ-6` →`T49`：`近平行射线（像素 (320.00, 240.00)）应被拒绝，却返回 (0.00, 1292487.00)（NaN / 爆炸点外泄）。`
- `INJ-7` →`T49`：`内库角 (1.27, 0.64)：拾取 (-1.27, -0.64) 误差 2.840E+000 m > 2e-4 m。`

**还原核验（实测）**：`INJ-1/-2/-5` 注入后场景 `sha1` 还原为 `bc75f9738a22f38e5f838cdfba2b1c09c34ee5bb`；`INJ-3/-6/-7` 后 `PointerAimSolver.cs` 还原为 `9febbc9c5a2bfe5b7e3952a42a92fb1bb82684f5`；`INJ-4` 后 `PhysicsTestDrive.cs` 还原为 `ac65dccc33872538f506b63ff1713eb0575a7eb2`。全部为**注入前备份回拷 + sha1 逐字节相等**（**未用** `git checkout --`）。还原后 `git status --porcelain` 只剩本节交付物。

---

## 5. 与 spec 的**偏差**（如实列出，附实测）

1. **`T46` 构造**：spec `§5 T46` 原文用 `screen = cam.WorldToScreenPoint(ballVisuals[0].position)`（**球心**，离台呢高一个球半径 r），但拾取器求的是**台呢平面 `y=0`** 的交点 ⇒ 球心像素的射线与 `y=0` 的交点沿视线方向偏 `r / tan(俯角)`。
   **实测偏移 = `0.013394 m`（约 13.4 mm）≫ spec 容差 `2e-4 m`** ⇒ 原文构造在**正确实现上也会 FAIL**。
   ⇒ 本切片按同一**构造目的**（视觉 1.2 ↔ 拾取反向 1.2 互逆）改用**球的台呢切点**像素（球心 − 世界半径），实测误差 `0.000002 m`。
   **盲带（已在注释声明）**：该构造下「视觉端漏 1.2」**抓不到**（切点位置不随等比缩放变）——由 `T45` 补（`INJ-1/-2` 已证仅 `T45` 红）。
2. **`T49` 拒绝域**：spec 原文第二个拒绝情形写「射线落在台面 **XZ 范围之外**」，但实现口径（spec `§3.2`）里 β **没有**台面矩形越界拒绝（`R-AD` 也只登记「平行射线」一条），且**加矩形越界会把本条注入掩盖**（巨型坐标天然"越界" ⇒ 仍返回 `false` ⇒ 删 `|D.y|<eps` 抓不到）。
   ⇒ 拒绝域统一取 `§3.2` 的**唯一**条件：**射线与台呢平面近平行**；两个用例 = 相机水平前视下①中心像素②同扫描线左 1/4 像素（证明拒绝不依赖"恰在像素中心"）。
   **实测行为**：台面外像素返回**有限**点、非 `false`、无 NaN —— 即**未做**矩形拒绝（如需，请下一单显式裁定）。
3. **近平行阈值 `ParallelEpsilon`**：spec 未给值。取 **`1e-4`**（不是更小的 `1e-6`）：Unity 相机投影由 `pixelRect` 的浮点宽高比驱动，**实测**「水平前视相机 + 屏幕中心像素」的 `|D.y| ≈ 1.35e-6`（**不是**精确 0）⇒ 阈值取 `1e-6` 会被自身噪声击穿（该相机本该被拒却通过，实测 FAIL）；`1e-4` 留约 74× 余量，且规范量级上对应交点约 `1.75e4 m`，远超任何有效拾取。
4. **β 的 `t > 0` 兜底**：初版 β 曾额外加「交点须在相机前方（`t ≤ 0` ⇒ 拒绝）」。**实测其会掩盖 spec `§5 T49` 指定的注入**（水平前视时 `D.y = ±0` ⇒ `t = ∓Inf` ⇒ 被该兜底拒绝 ⇒ 删 `|D.y|<eps` 后 `T49` 仍绿）。⇒ **移除该兜底**，使 β 与 spec `§3.2` 逐条一致、且注入 6 能精确打死 `T49`（`INJ-6` 已证「仅 `T49` FAIL」）。

---

## 6. 未测 / 明确不做

- **端到端输入设备级**（真鼠标按下/松开、指针真实移动）：EditMode 只测纯逻辑；`T50`（拖拽/锁定）本就属下一单。
- **屏幕截图 / 人工目视**「指针方向对不对」：Manual。
- **16:9 基准是否恰好容纳整桌**、`minSupportedAspect` 门限、方向键屏幕旋转手感：需 Unity 相机实测，属 `T52`/`T53` 与 `Q-9` 附注。
- **`Main.unity` 的 1.2 落地**：本轮**不动**（`SCN-02` 仍绿，实测 Day3 验收器 22/22）。
- **`T50`–`T53`**：未实现（越界即未完成）。

---

## 7. 工具链与收尾（实测）

- 编辑器：`E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`（国际版）。跑前查 `Temp/UnityLockfile` **不存在**（无用户在跑）；无 `taskkill`。
- `-runTests` **不带 `-quit`**；`-executeMethod` **带 `-quit`**；`-outputDir` 全部指向 `E:/Unity_Deve/_d8aim_out/`（**仓库外**）。注入备份亦在仓库外。
- **四验收器（实测）**：Day1 `25`（**无 `RUN-01`**，verify-only 入口，**未走 Apply**）、Day2 `33`、Day3 `22`、Day4 `7`（`D4-SCN-01`/`D4-SCN-02`/`D4-PHY-01`/`D4-BAN-01`/`D5-PHY-01`/`D6-PHY-01`/`D4-BLD-01`）—— **FAIL = 0**，`overallStatus = PASS`。
- **全量 EditMode（实测）**：`154 / 154`，failed 0 / skipped 0（`_d8aim_out/editmode-final.xml`）。
- 跑前 `git status --porcelain` **为空**；跑后**仅**本节交付物（见 §1）—— 无 `Docs/Reports/**` 改动、无 `Packages/packages-lock.json` 改动。
