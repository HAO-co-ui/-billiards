# 35-A Day 16 · 性能与打包

> 排期：`Docs/35-A_击球手感_制作排期.md` §Day 16 —— ①性能（DrawCall 合批 / 阴影范围收敛 / 球与桌 Renderer 优化；目标 **60 fps @ 1080p**）
> ②**物理耗时测量**（16 球 + 82 线段定长步进的单帧成本）③**Windows 桌面横屏构建 + 产物实机验证**。
> **验收 = 构建包可脱离编辑器独立运行 + 1080p 稳定 60 fps**。
> 本单同时修掉一个**会让验收直接失败**的配置缺陷（build 场景列表指向空场景，见 §3.3）。

---

## 1. 三路测量法（为什么不能只靠一个数字）

| 路 | 测什么 | 手段 | 局限（如实） |
| --- | --- | --- | --- |
| **A 物理单步成本** | 内核 `Step()` 的 CPU 时间 | EditMode 判据 `T119`（预热 + 固定步数计时） | 编辑器内计时 ≠ 构建产物（Mono vs IL2CPP、编辑器调试开销）⇒ 只作**量与趋势**，不作最终 fps 结论 |
| **B 渲染静态面** | URP 阴影/级联/分辨率、帧率策略、相机与光照设置、后处理是否生效 | 读资产 + 源码（判据 `T120` 锁"设置与意图一致"） | 静态设置**不等于**实测帧时 |
| **C 构建产物冒烟** | **真 1080p 下的逐帧毫秒**、渲染器/材质计数、分辨率与 vsync 实际值 | `-billiards-smoke <N>` 模式跑构建产物 → 写 JSON → 退出码 | 需要**有显示环境**（会短暂开窗）；单机单次采样，不作多机结论 |

⇒ **只有 C 能回答"是否 1080p 稳定 60 fps"**；A/B 负责在改坏时**快速定位**（且能在没有显示环境时先跑）。

---

## 2. 任务② 物理耗时测量（已完成测量，优化**登记不实施**）

**实测**（`T119`，本机；16 球 + 82 段，`SubSteps = 3`，`dt_sub = 2.7778 ms`）：

| 场景 | 单步耗时 | 占 60 fps 帧预算（16.67 ms） |
| --- | --- | --- |
| 满堆开球**忙期**（子步内多次 TOI 迭代） | **650.6 µs** | **3.90 %** |
| **整盘静止**空转（第 493 步后） | **370.1 µs** | 2.22 % |
| 单球自由滚动 | 26.5 µs | 0.16 % |

**结论**：物理**不是** 60 fps 的瓶颈（最坏只吃 3.9% 预算），故本日**不做**物理优化 —— 只把它登记为**已量化的后备项**：

- **空闲早退**（可把静止空转从 370 µs 降到 ~0）：`AdvanceSubStep` 里的 `ResolveDeepOverlaps` 是 **O(球×段)** 且**每子步无条件执行**（球停着也照样扫 16×82×3 ≈ 3936 次最近点）。可行性方案 = 加"待检查重叠"脏标记（由 `LoadBalls`/`PlaceBall`/`Launch` 置位、有修正时保持置位），全静止且无待检查时跳过该扫描。
  ⚠ **不实施的理由（风险 > 收益）**：本工程前一轮刚修掉"两球完全重合且静止 ⇒ 永不分离"（`T22b` 加固）—— 早退若漏掉"外部摆位造成的静态重叠"，**会把那个缺陷带回来**；而验收不需要这 4.4% 的 CPU。⇒ 登记，待 Day 17 之后按需再做。
- **广相剪枝**：`CollectCandidatePairs` 是既定的**唯一宽相位边界**（当前为暴力枚举），球数放大时只替换该函数体 ⇒ 同样登记，不在本日做。

---

## 3. 任务① 性能：阴影收敛 + 帧率策略 + 构建配置

### 3.1 阴影收敛（**改动**，收益最大）

**现状（实测）**：URP `Assets/Settings/PC_RPAsset.asset` = `m_ShadowDistance: 50` m、`m_ShadowCascadeCount: 4`、
`m_MainLightShadowmapResolution: 2048`；而球桌内库只有 **2.54 × 1.27 m**、场景里**只有 1 盏平行光**
⇒ 50 m 范围内做 4 级级联，等于**每帧渲染 4 次 2048² 阴影图**去覆盖一个 50 m 的世界，
而桌上那 2.5 m 拿到的有效阴影分辨率反而被摊薄。

**改法（走官方 API，不手改 `ProjectSettings/*`）**：`UniversalRenderPipelineAsset.shadowDistance = 6`、`shadowCascadeCount = 1`
（2048² 覆盖 6 m ⇒ 约 2.9 mm/texel，桌上阴影比现状更锐），`mainLightShadowmapResolution` 保持 2048。
⇒ 阴影 pass 由 **4 次 → 1 次**，且覆盖范围贴合场景。

**依据与边界**：6 m 的选点 = 桌面最大尺寸 2.54 m 的 2.4 倍（含桌腿落地与地面投影余量）；
`QualitySettings` 里的 `shadowDistance/shadowCascades` 在 URP 下**惰性**（URP 用自己资产上的值）⇒ **不改**，登记以免后人误调。

### 3.2 帧率策略（**改动**）

现状：`QualitySettings.vSyncCount = 0`（关）且**没有任何 `Application.targetFrameRate` 设置** ⇒ 不限帧：
在高刷屏上白烧 GPU、在 60 Hz 屏上撕裂（验收要的是"**稳定** 60 fps"）。

**改法**：运行时设 `QualitySettings.vSyncCount = 1`（垂直同步，不撕裂）**并且** `Application.targetFrameRate = 60`
（VSync 生效时该值被忽略，但作为"VSync 不可用/被驱动禁用"时的兜底）。语义写死为
"**目标 ≥ 60 fps 且无掉帧**"，**不**声称"锁定 60.00"。

### 3.3 构建配置（**改动：修掉会让验收直接失败的缺陷**）

**实测缺陷**：`ProjectSettings/EditorBuildSettings.asset` 的场景列表指向 **`Assets/_Project/Scenes/Main.unity`** ——
而该场景是**空壳**（1 个 MeshRenderer + 1 Camera + 1 Light、**0 个 MonoBehaviour**）⇒ 今天打出来的包**会是黑屏死包**。
真正有全部接线、且判据族（`T98`/`T103`/`T104`/`T41`/`T43`）一直在守的是 **`Assets/_Project/Scenes/PhysicsGreybox.unity`**。

**改法**：构建前把场景列表设为灰盒场景（`EditorBuildSettings.scenes`，官方 API），并在构建脚本里**断言**列表恰为该场景。
**登记**：`Main.unity` 的空壳状态**保留原样**（不删、不改 —— 它是历史资产），后续（35-B 或 Day 17 收尾）决定它作为
"正式入口场景"还是删除；本日只保证**打出来的包是可玩的**。

### 3.4 顺带确认（不动）

- **后处理未生效**：场景里**没有 `Volume` 组件** ⇒ `Assets/Settings/DefaultVolumeProfile.asset`（URP 模板残留，含
  `MotionBlur`/`FilmGrain`/`CopyPasteTestComponent2`/`TestAnimationCurveVolumeComponent` 等**模板组件**）**不参与渲染** ⇒ 无成本、无观感影响。
  登记：该资产是模板残留，**不删**（动它无收益且有连带风险）。
- **MSAA**：URP `m_MSAA: 1` = **关**（相机 `m_AllowMSAA: 1` 在 URP 下不决定最终值）。细线特效（世界 9.6 mm）在无 MSAA 下会有锯齿 ⇒
  **登记为可选画质项**（开 2× 的代价与收益需实机对比，本日不动，保 60 fps 优先）。
- **相机远裁 60 m**：对俯视看 2.5 m 桌的相机偏大，但它由场景构建器写入（改它要重建场景）⇒ **登记不动**（收益小、动场景风险大）。

---

## 4. 任务③ 打包 + 实机验证：把"能独立运行 + 60 fps"变成**可判定证据**

### 4.1 构建（`-executeMethod`）

`Billiards.EditorTools.BuildWindows.BuildFromCommandLine`：
`BuildTarget.StandaloneWindows64`、产物 → `Build/Windows/Idea35PoolRoom.exe`（仓库外亦可，本日放仓库内并 gitignore 产物）、
`-outputDir` 兼容（报告 JSON）。构建后**自检**：产物存在 + 大小 > 0 + 同目录 `_Data/` 与 `UnityPlayer.dll` 齐备。

### 4.2 冒烟模式（运行期开关，**不打进产品逻辑**）

`-billiards-smoke <N> [-billiards-smoke-out <path>]`：

- 由 `[RuntimeInitializeOnLoadMethod]` 探测命令行参数（**不改场景**、不动任何现有组件）；
- 跑满 N 帧后写 JSON 并 `Application.Quit(通过?0:1)`；**不通过**（掉帧超阈值/异常/未达标）退 **非 0**；
- JSON 字段（全部实测、非推算）：`width/height/fullscreen/vSyncCount/targetFrameRate`、`frames`、`meanMs/p95Ms/maxMs`、
  `fpsMean`、`droppedOver16_67ms`、`rendererCount`、`distinctMaterialCount`、`physicsSteps`、`unityVersion`。
- **判定口径（写死，不得事后放宽）**：`fpsMean ≥ 60`（按帧时均值）且 `droppedOver16_67ms / frames ≤ 2%`。

### 4.3 判据（新增）

| 判据 | 位置 | 内容 |
| --- | --- | --- |
| **`T119`** 物理步预算 | 新文件 `PoolSimulatorPerformanceTests` | 预热后计时：开球忙期 **≤ 5 ms/步**（实测 0.65，7.7× 余量）、静止空转 **≤ 3 ms/步**（实测 0.37，8×）；打印三个场景实测 µs。**用途 = 拦"量级失控"（如历史上 199 次接触那种爆炸），不声称 fps** |
| **`T120`** 性能设置与意图一致 | 同上 | 读 `PC_RPAsset`：`shadowDistance ≤ 8`、`shadowCascadeCount == 1`、`mainLightShadowmapResolution == 2048`；读源码常量：`targetFrameRate == 60`、`vSyncCount == 1`；读 `EditorBuildSettings.scenes`：**恰含且仅含灰盒场景**（防回归到空场景）。**注入**：把 cascade 改回 4 ⇒ 红 |
| **`T121`** 冒烟纯逻辑 | 同上 | 纯函数：① CLI 解析（缺 N / N 非法 / 带 out 路径 / 只有开关）；② 帧时统计（mean/p95/max、掉帧计数）对**构造样本**的闭式期望；③ 判定函数（达标/不达标/边界 60.0）。**不含**任何 `Random` |

---

## 5. 验证（实测）

| 项 | 结果 |
| --- | --- |
| EditMode 全量 | **210 / 210**（failed=0 skipped=0；`_d9r_out/v12/full16.xml`）※207 + `T119`/`T120`/`T121` |
| 四验收器 | **26 / 33 / 22 / 7**（FAIL=0 WARN=0，四档 exit 0；`_d9r_out/v12/validators/`） |
| **`T119` 物理步预算** | 开球忙期 **678.3 µs**（上界 5000）、静止空转 **373.5 µs**（上界 3000）、单球 **29.4 µs**；三档非空转证据齐备 |
| **`T120` 性能与构建配置** | 阴影 **6 m / 级联 1 / 2048²**；帧率 **vSync=1 / target=60**（读实际值）；构建场景 **`PhysicsGreybox.unity`**（恰 1 个且启用） |
| **`T121` 冒烟纯逻辑** | 7 例解析 + 统计闭式 + 5 例判定 + JSON 16 字段/不变文化 ✓ |
| **构建（任务③）** | `Succeeded`｜**90.7 s**｜exe **672,256 B**｜总 **133,450,743 B**｜**错误 0 / 警告 0**｜产物自检 **OK**（exe + `_Data/` + `Managed/` + `UnityPlayer.dll` 齐备）；报告 `Build/Windows/build-report.json` |
| **冒烟（真 1080p，任务③验收）** | **1920 × 1080**、窗口模式、**600 帧**、`mean 6.937 ms` / `p95 6.937` / `max 6.937`、**fpsMean 144.15**、**超预算帧 0**、`passed = true`、**退出码 0**；`rendererCount 52`、`distinctMaterialCount 6`、`physicsSteps 592`（≈120 步/s ⇒ 物理时间口径 1:1 正确） |

**⇒ Day 16 两条验收均达成**：①**构建包可脱离编辑器独立运行**（冒烟由 `Build/Windows/Idea35PoolRoom.exe` 独立跑出，全程无编辑器）；
②**1080p 稳定 ≥ 60 fps**（均值 144 fps、600 帧零掉帧；远高于 60）。

**⚠ 本轮最有价值的实测坑（已写进代码注释与台账）**：`SmokeProbe.MeetsTarget` 的边界最早写成
`(float)dropped / frames <= MaxDroppedFraction`，在"占比恰等于上限"这一格**判为不通过** ——
而位模式证明两边**都等于 `0x3CA3D70A`**、单独比较为 **true**；把除法结果**先存进 `float` 局部**再比较则是 **true**。
⇒ **Mono 会让 float 单表达式里的中间结果保持更高精度参与比较**（差 1 ULP）。
处置 = 口径真源改成 **`2/100` 这对整数**，判定走**整数比较**（`dropped × 100 ≤ 2 × frames`），
`MaxDroppedFraction` 降级为派生值（仅供报告展示）。**口径本身一字未改**（仍是"≤ 2%"）。

## 6. 盲带与登记（如实）

1. **"稳定 60 fps"只在**有显示环境的机器上**、由 §4.2 的**单次采样**证明 —— 不作多机、长时间、温度/驱动差异结论；
   掉帧阈值 2% 是本日**写死的口径**，不是行业标准。
2. 冒烟跑会**短暂开窗口**（真渲染不能无窗口），期间会抢一次焦点。
3. **A/B 两路不替代 C**：编辑器内计时与静态设置都可能"看起来很好"而实机掉帧。
4. 未做（登记）：MSAA 画质取舍、相机远裁收敛、URP 模板残留资产清理、`Main.unity` 归属、阴影 1024 vs 2048 的取舍、IL2CPP vs Mono 的构建对比。
5. 本单**不碰**物理内核行为（`PoolSimulator` 语义零改动，只加"测量判据"），**不碰**音效/特效（上一轮已收口）。
6. **冒烟只跑"静置 + 空转"的 600 帧**（未打杆）⇒ 它证明的是**渲染与稳态帧率**；击球瞬间（VFX 满屏 + 物理忙期）
   的帧率未单独采样 —— 击球期的物理成本已由 `T119` 覆盖（忙期 678 µs），但**渲染侧的瞬时峰值未测**，登记。
7. `Build/` 已加入 `.gitignore`（产物 133 MB 不入库）；`Build/Windows/build-report.json` 与仓库外 `_d9r_out/v12/smoke.json`
   是**跟着产物走**的证据（不是当前状态的断言）。
