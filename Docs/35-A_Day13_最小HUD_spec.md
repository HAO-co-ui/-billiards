# 35-A Day 13 最小 HUD —— 实施规格

> **来源**：`Docs/35-A_击球手感_制作排期.md` §Day 13（7 h）。本文件**只写规格**；实测结果与偏差记入
> `Docs/测试数基线台账.md` 的 Day 13 行。
> **纪律**：本单所有断言按 `Docs/工程标准.md` R3-2（禁恒真）、R4-2（守卫优先进测试套件）；
> 参考实现事实取自素材包只读摘录（`04_UI/Web_Implementation_Reference/`、`04_UI/UI全流程设计说明_参考.txt`）。

---

## 1. 范围（照抄排期，不得放宽）

| # | 任务 | 排期工时 |
| --- | --- | --- |
| 1 | 练习 HUD（参考 `04_UI/Reference_Screens/08-practice-hud.png`）：状态条（`aim` / `moving`）、力度条 + Shoot、剩余球数、本轮杆数 | 3.5 h |
| 2 | 字体接入：Barlow Condensed（标题）+ Inter（正文）→ TMP FontAsset | 2 h |
| 3 | 布局校验：1280×720 / 1920×1080 两档可读，HUD 不遮球桌主区 | 1.5 h |

**排期明写的约束**：两套字体**不覆盖中文** ⇒ 35-A 全部文案**英文**
（`New Rack` / `Shots` / `Pocketed` / `Fouls` / `Power` / `Shoot`）。中文方案属正式版决策项，本单**不做**。
**验收（照抄）**：HUD 在两档分辨率下**无遮挡、无溢出**。

---

## 2. 参考实现事实（只读摘录，权威）

### 2.1 设计 token（`Web_Implementation_Reference/style.css` 的 `:root`，逐字抄）

| 名字 | 值 | 用途（设计说明 §一） |
| --- | --- | --- |
| `--accent` | `#d4b46c` | 黄铜金（按钮 / 描边 / 数值强调） |
| `--green` | `#6dbda9` | 状态绿（自方回合 / 状态图标） |
| `--ink` | `#102025` | 炭黑（面板底 / 文字反白） |
| `--cloth` | `#174e46` | 桌布绿（**仅装饰，本单不用**） |
| `--muted` | `#a9b9b4` | 次级文字 |
| `--line` | `#d4b46c40` | 描边（黄铜 25% 透明） |
| 正文色 | `#f2efe4` | 暖白 |
| 酒红 / 亮青 | `#b66370` / `#7fcfd2` | **只用于犯规/失败** 与 **指定球袋** ⇒ **本单不得使用** |

**形态纪律（设计说明 §一）**：字距 = 0；控件圆角 **1–3 px**（不做现代 SaaS 圆角卡片）。

### 2.2 结构（`index.html` 摘录，逐字抄的语义）

```html
<div class="practice-meter"><strong id="pot-count">0 / 8</strong><span>balls pocketed</span><small id="shot-count">0 shots</small></div>
<footer class="shotbar">
  <div class="power-control"><label for="power">POWER <output id="power-value">45%</output></label><input id="power" type="range" min="5" max="100" value="45" /></div>
  <button id="shoot" class="shoot-button" disabled><span>Shoot</span></button>
</footer>
<div class="status-bar" data-state="..."><span class="status-symbol">…icon…</span><span id="turn-dot"></span><span id="status">…text…</span></div>
```

- `.status-bar`：`background #102025ed`、`border 1px #d4b46c45`、`border-radius 2px`、`padding 8px 15px`、`font-size 12px`；
  状态圆点 `#turn-dot` = **5×5 菱形**（`background var(--green)`，`rotate(45deg)`）。
- `.shoot-button`：`height 43px`、`background #d4b46c`、`color #102025`、`border 1px #ebd397`、`border-radius 2px`、
  `font-weight 700`、`font-size 12px`、`box-shadow inset 0 -3px #6e5a2a50`；**禁用态 `opacity .38`**。
- `.power-control label`：`font-size 9px`、色 `#acbbb4`，左侧文案 `POWER`、右侧 `output` 色 `#efdcaa` `font-size 12px`；
  轨道 `height 5px`、`border-radius 1px`、填充色 `#d4b46c`、空槽色 `#3f615a`。
- `.practice-meter`：数值 `font 600 29px 'Barlow Condensed'`；标签 `font-size 10px`。

---

## 3. 前置事实（**本单实测，非假定**）

### 3.1 字体（`04_UI/Fonts_Web/`）

素材包只给 **woff2**（Unity 的 FontEngine **不能读** woff2）。已实测可转：
`fontTools 4.66.1 + brotli`（托管 venv `C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe`）⇒
`TTFont(src).flavor = None; save(dst)` 即得 TTF；`inter-variable-latin.woff2` 是**可变字体**（`fvar`，`wght 100–900`，默认 400）
⇒ 必须 `fontTools.varLib.instancer.instantiateVariableFont(f, {"wght": N})` **实例化**成静态字重。

**已落到** `Assets/_Project/UI/Fonts/`（本单交付物，5 个 TTF + 2 个 OFL）：

| 文件 | 来源 | 家族名 | usWeightClass |
| --- | --- | --- | --- |
| `BarlowCondensed-Medium.ttf` | `barlow-condensed-500-latin.woff2` | Barlow Condensed Medium | 400 |
| `BarlowCondensed-SemiBold.ttf` | `barlow-condensed-600-latin.woff2` | Barlow Condensed SemiBold | 600 |
| `BarlowCondensed-Bold.ttf` | `barlow-condensed-700-latin.woff2` | Barlow Condensed | 700 |
| `Inter-Regular.ttf` | `inter-variable-latin.woff2` @ `wght=400` | Inter | 400 |
| `Inter-SemiBold.ttf` | `inter-variable-latin.woff2` @ `wght=600` | Inter | 600 |

**已自证**：5 个 TTF 的 `fvar` 均**不存在**（证明确非可变字体）；cmap 对
`0x20` / `0x2F` / `0x30–0x39` / `0x41–0x5A` / `0x61–0x7A` **零缺失**。

### 3.2 TMP

- `Assets/TextMesh Pro/` **不存在** ⇒ **TMP Essential Resources 未导入**（缺 `TMP_Settings`、SDF shader、默认字体资产
  ⇒ TMP 组件在运行期不可用）。
- 资源包已在本地：`Library/PackageCache/com.unity.ugui@9b7e7eef789d/Package Resources/TMP Essential Resources.unitypackage`
  ⇒ 用 `AssetDatabase.ImportPackage(path, interactive:false)` 导入（批处理可跑）。
- 程序集名（**实测**，`com.unity.ugui` 2.0.0 内）：运行时 `Unity.TextMeshPro`、编辑器 `Unity.TextMeshPro.Editor`。

### 3.3 球桌在屏幕上的投影（**本单实测**；判据的期望值来源）

相机（`GreyboxSceneSetup.BuildCamera`，逐字）：`position (0, 2.10, −0.75)`、
`LookRotation((0, 0.02, 0.06) − position, up)`、`fieldOfView = 52`、`near 0.05`、`far 60`。
可视野 `playfieldRoot.localScale = 1.2` ⇒ 内库矩形世界半长 **1.524 m**、半宽 **0.7620653196 m**，位于 `y = 0`。

以针孔投影（无 roll）独立复算（**外部工具**，不调 Unity），视口坐标 `(0,0)` = 左下：

| 分辨率 | 宽高比 | 实际 vFOV | 内库投影 bbox `x` | 内库投影 bbox `y` |
| --- | --- | --- | --- | --- |
| 1920×1080 | 1.777778 | 52.000000° | [0.049899473, 0.950100527] | [0.093983871, 0.764701906] |
| 1280×720 | 1.777778 | 52.000000° | [0.049899473, 0.950100527] | [0.093983871, 0.764701906] |
| 1920×1200 | 1.600000 | 56.908751° | [0.049899473, 0.950100527] | [0.134585484, 0.738231716] |
| 1024×768 | 1.333333 | 66.072714° | [0.049899473, 0.950100527] | [0.195487904, 0.698526430] |
| 2560×1080 | 2.370370 | 52.000000° | [0.162424605, 0.837575395] | [0.093983871, 0.764701906] |
| 1024×1280 | 0.800000 | 94.608462° | [0.049899473, 0.950100527] | [0.317292742, 0.619115858] |

**由该表得出的两条硬约束（本单布局只能这样选）**：

1. **左右边距最紧只有 0.0499**（16:9 系）⇒ **放不下任何侧栏**（参考实现的右侧 132 px 竖栏在 1280×720 需 0.103 宽）
   ⇒ **侧栏方案被排除**。
2. **内库上沿最高 = 0.764702**（16:9）⇒ **顶带可用高度 = 0.235298**（169 px @720p / 254 px @1080p），
   这是**六个档里最小的一档** ⇒ HUD **全部元素必须落在顶带**。
   底带仅 0.093984（68 px @720p），且球桌**近侧的库边/木框会画在这里** ⇒ 不作为 HUD 区。

> **对参考实现的偏离（如实登记）**：参考把 `practice-meter` 放**左下角**、`shotbar` 放**底部居中**。
> 本工程的相机构图（球桌几乎充满视口）**几何上不允许** ⇒ 本单改为**全部落在顶带**的三块式顶栏。
> 与参考的"右侧竖栏 / 底部横条"是**同一信息的不同摆放**，不是漏做。

---

## 4. 布局设计（**判据直接驱动**；单位 = 视口比例）

### 4.1 三块（互不重叠，全部 ⊂ [0,1]²，全部落在顶带）

| 块 | id | `x` 范围 | `y` 范围 | 内容 |
| --- | --- | --- | --- | --- |
| 计数组 | `Counters` | [0.020, 0.300] | [0.780, 0.982] | `BALLS LEFT` / `SHOTS` / `POCKETED` 三列 |
| 状态条 | `Status` | [0.360, 0.640] | [0.780, 0.982] | 状态图标 + 状态文案 |
| 力度块 | `Power` | [0.700, 0.980] | [0.780, 0.982] | `POWER` + `nn%` + 横条 + `Shoot` |

- **顶带余量**：`0.780 − 0.764702 = 0.015298`（11 px @720p）；`0.982 < 1`（上边距 0.018 = 13 px @720p）。
- **块间距**：`0.300 → 0.360`（0.060）与 `0.640 → 0.700`（0.060）；`0.982 − 0.780 = 0.202`（145 px @720p / 218 px @1080p）。

### 4.2 可读性下限（"两档可读"的可计算口径）

画布用 `CanvasScaler.ScaleWithScreenSize`、`referenceResolution = (1920, 1080)`、`matchWidthOrHeight = 0.5`。
两档目标分辨率均为 16:9 ⇒ 缩放系数恒为 `min(w,h)/1080`（1280×720 ⇒ **2/3**）。

**声明下限**：任何**文本**在 **1280×720** 下的实际像素高 **≥ 9 px** ⇒ 设计字号（1080 域）**≥ 13.5 px**。
本单一律取设计字号 `≥ 14`（数值大字取 26–30）⇒ 720p 下实际 ≥ 9.33 px。

### 4.3 文案（全英文，逐字；**不得增改**）

| 位置 | 文案 |
| --- | --- |
| 计数组列 1 | 标签 `BALLS LEFT`，值 = 剩余目标球数（整数） |
| 计数组列 2 | 标签 `SHOTS`，值 = 本轮杆数（整数） |
| 计数组列 3 | 标签 `POCKETED`，值 = `n / m`（已进球 / 目标球总数） |
| 状态条 | `Aim` / `Moving` |
| 力度块 | 标签 `POWER`；值 `nn%`；按钮 `Shoot` |

**字体分工**（排期任务 2）：**数值与按钮**（`Shoot` / `POWER` / `nn%` / 三个数值）用 **Barlow Condensed**；
**小标签与状态文案**（`BALLS LEFT` / `SHOTS` / `POCKETED` / `Aim` / `Moving`）用 **Inter**。

---

## 5. 实现设计

### 5.1 新增文件

| 文件 | 类型 | 职责 |
| --- | --- | --- |
| `Runtime/UI/PracticeHudLayout.cs` | **纯静态**（无 MonoBehaviour） | 针孔投影 + 三块矩形求解 + 视口→像素 |
| `Runtime/UI/PracticeHudState.cs` | **纯静态** | 击球流程状态 → 状态条文案 / `Shoot` 可用性；力度 → `nn%`；计数 → `n / m` |
| `Runtime/UI/PracticeHudView.cs` | MonoBehaviour | 建 TMP 控件树 + 每帧从驱动器/流程读值刷新 |
| `Editor/Visual/HudAssetBuilder.cs` | Editor | ① 缺则导入 TMP Essential Resources；② 由 5 个 TTF 建 5 个 TMP FontAsset |

命名空间 `Billiards.UI`（与既有 `Billiards.Physics` / `Billiards.Core` / `Billiards.Table` 同构）。

### 5.2 程序集引用（**必改**，否则编译不过）

- `Assets/_Project/Scripts/Runtime/Billiards.Runtime.asmdef`：`references` 追加 `"Unity.TextMeshPro"`。
- `Assets/_Project/Tests/EditMode/Billiards.Tests.EditMode.asmdef`：`references` 追加 `"Unity.TextMeshPro"`。

### 5.3 纯函数签名（判据直接驱动，**不得内联进 MonoBehaviour**）

```csharp
// PracticeHudLayout
public static Vector2 ProjectToViewport(Vector3 worldPoint, Vector3 cameraPosition,
                                       Vector3 cameraForward, float verticalFovDeg, float aspect);
public static Rect TableViewportRect(Vector3 cameraPosition, Vector3 cameraForward,
                                     float verticalFovDeg, float aspect, float visualScale);
public static PracticeHudRects Resolve(float aspect);
public static Rect ToScreenPixels(Rect viewportRect, int screenWidth, int screenHeight);

// PracticeHudState —— 入参是既有枚举 ShotPhase（`ShotFlowState`，五个值：Aim/Charging/Shooting/Moving/Settle）
public static string StateLabel(ShotPhase phase);             // Aim|Charging ⇒ "Aim"；Shooting|Moving|Settle ⇒ "Moving"
public static bool ShootEnabled(ShotPhase phase);             // 仅 Aim 为 true
public static string PowerLabel(float normalizedPower);       // 四舍五入到整数百分比，"0%".."100%"
public static string PocketedLabel(int pocketed, int total);  // "n / m"
```

`PracticeHudRects` = 三个命名 `Rect`（`counters` / `status` / `power`）+ `TableRect`（信息用）。

**投影口径**（唯一落点；不得用 `Camera.WorldToViewportPoint` 代替 —— 判据要能在无相机对象时算）：
`f = normalize(lookTarget − position)`、`right = normalize(cross(up, f))`（`up = +Y`，无 roll）、`upCam = cross(f, right)`；
`v = p − position`；`z = dot(v,f)`、`x = dot(v,right)`、`y = dot(v,upCam)`；
`halfH = tan(vFov/2)`、`halfW = halfH · aspect`；`viewport = (0.5 + 0.5·x/z/halfW, 0.5 + 0.5·y/z/halfH)`。
**vFOV 必须复用 `CameraViewportFitter.ResolveVerticalFovDeg`**（铁律 16：同一不变量单点实现；不得再写一份）。

### 5.4 视图与接线

- `PracticeHudView` 运行时从 **`Camera.main`** 取实际位姿与 `fieldOfView` 求解 `TableRect`（⇒ 与相机**构造上不漂移**）；
  块矩形由 `Resolve(aspect)` 给。
- 画布：`Canvas`（`ScreenSpaceOverlay`）+ `CanvasScaler(ScaleWithScreenSize, 1920×1080, match .5)` + `GraphicRaycaster`。
- 数据源：状态与 `Shoot` 可用性来自 `CueStickController` 的流程状态（**只读出口，不得另建状态**）；
  力度来自 `PointerPowerController`；三个计数来自 `PhysicsTestDrive` / `PoolSimulator` 的既有只读出口。
- `GreyboxSceneSetup` 追加 `PracticeHud` 宿主接线（与既有 `PoolVfx` / `PoolAudio` 同层，**不进缩放根**）并重建场景。

---

## 6. 判据（**必须进测试套件**，工程标准 R4-2）

### `T95` HUD 布局不变量（纯函数，六档宽高比）

对 `{(1280,720), (1920,1080), (1920,1200), (1024,768), (2560,1080), (1024,1280)}` 六档逐档：

- **(a) 无溢出**：`counters` / `status` / `power` 三个矩形**全部 ⊆ [0,1]²**（四条边逐条判）。
- **(b) 不遮主区**：三个矩形与 `TableViewportRect` **两两无交集**（`Rect.Overlaps`）。
- **(c) 互不重叠**：三个矩形两两无交集。
- **(d) 投影闭合验算（甲类期望值，来源 = §3.3 的外部工具复算）**：16:9 下 `TableViewportRect`
  `≈ x[0.049899, 0.950101]`、`y[0.093984, 0.764702]`，容差 `1e-4`；
  1024×768 下 `≈ y[0.195488, 0.698526]`；2560×1080 下 `≈ x[0.162425, 0.837575]`。容差 `1e-4`。
- **(e) 可读下限**：`min(w,h)/1080` 缩放下，状态条与计数组的**设计字号 14px** 在 1280×720 实际 ≥ 9 px
  （`14 · 2/3 = 9.333`）；力度块的 `Shoot` 按钮在 1280×720 的实际宽 ≥ 90 px、高 ≥ 28 px。
- **(f) 相机漂移守（@编辑器，场景级）**：载入 `PhysicsGreybox.unity`，`Main Camera` 的
  `m_LocalPosition` / 朝向 / `fieldOfView` 必须与 `GreyboxSceneSetup` 的公开相机常量一致
  （口径同 `T43` 的场景加载式漂移守；本项是 §3.3 期望值的前提，缺它则 (d) 变成对空气断言）。

**注入负对照（必须实测红）**：① 状态条 `y.max` 改 `1.02` ⇒ (a) 红；
② 状态条整体下移到 `y ∈ [0.60, 0.80]` ⇒ (b) 红（与球桌相交）；③ 计数组 `x.max` 改 `0.40` ⇒ (c) 红。

### `T96` 状态与文案映射（纯函数）

- **(a)** `ShotPhase.Aim` ⇒ `StateLabel = "Aim"`、`ShootEnabled = true`；`ShotPhase.Charging` 同上。
- **(b)** `ShotPhase.Shooting` / `Moving` / `Settle` ⇒ `StateLabel = "Moving"`、**`ShootEnabled = false`**。
- **(c)** `PowerLabel`：`0 → "0%"`、`0.45 → "45%"`、`1 → "100%"`、`0.456 → "46%"`（四舍五入到整数）。
- **(d)** `PocketedLabel(0, 15) = "0 / 15"`、`PocketedLabel(7, 7) = "7 / 7"`。

**注入负对照**：把 `ShootEnabled` 对 `Moving` 的返回翻成 `true` ⇒ (b) 红。

### `T97` 字体与字符覆盖（资产级）

- **(a)** `Assets/_Project/UI/Fonts/` 下 5 个 TTF 全部可 `AssetDatabase.LoadAssetAtPath<Font>` 载入（非 null）。
- **(b)** 5 个 TMP FontAsset 全部在场，路径与命名**逐个钉死**（`AssetDatabase.LoadAssetAtPath<TMP_FontAsset>` 非 null）：
  `Assets/_Project/UI/Fonts/BarlowCondensed-Medium SDF.asset`
  `Assets/_Project/UI/Fonts/BarlowCondensed-SemiBold SDF.asset`
  `Assets/_Project/UI/Fonts/BarlowCondensed-Bold SDF.asset`
  `Assets/_Project/UI/Fonts/Inter-Regular SDF.asset`
  `Assets/_Project/UI/Fonts/Inter-SemiBold SDF.asset`
- **(c) 字符覆盖**：每个 FontAsset 对其**被指派的全部文案**（§4.3 逐字，含标签）经
  `TMP_FontAsset.HasCharacters(text, out missing)` 检查，`missing` **必须为空**。
- **(d) 字体分工**：数值/按钮资产家族名含 `Barlow Condensed`；小标签/状态资产家族名 = `Inter`。
- **(e) cmap 前置自检**：直接读 TTF 字节、断言 `0x20`/`0x2F`/`0x41`/`0x61` 存在
  （防"FontAsset 建了但源字体是子集"这一失效模式）。

**注入负对照**：把正文资产的指派改成 Barlow 资产 ⇒ (d) 红。

---

## 7. 验证姿势与边界（照抄工程标准 §8）

- 跑批一律用**国际版**编辑器 `E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`；
  `-runTests` **不带** `-quit`，显式带 `-testResults` / `-outputDir` 指向**仓库外**。
- `-testFilter` 分隔符用 `;`；本 NUnit 叉**无** `Assert.Multiple`。
- **`-runTests` 后必须重跑全量 + 四个验收器**（本单动 `PhysicsGreybox.unity` 与工程程序集引用）。
- 每个判据的注入**必须实测红**并**逐字节还原**（sha1 一致，禁 `git checkout --`）。
- 导入 TMP Essential Resources 会新增 `Assets/TextMesh Pro/**`（**第三方资产树**）——
  这是**本单最大的工程副作用**，必须在报告里明写，并确认四个验收器仍然 FAIL=0。

### 盲带（如实登记，不得声称已覆盖）

- **观感**非判据可测（批处理 `-nographics` 无渲染）：颜色是否耐看、字号是否舒服、块间距是否美观，
  判据只能保证**几何不变量**与**字号下限**，不能保证"好看"。
- **TMP 实际排版结果**（字宽/换行/溢出面板）在同一画布下随字体资产而变；判据只覆盖"字符存在"与"矩形不越界"，
  **不覆盖文字在块内的实际像素溢出** ⇒ 该风险由**用户实机截图**兜（排期验收的"无溢出"口径按**块矩形**执行）。
- 底带（0.094）**刻意不用** ⇒ 球桌近侧木框区域不承载 HUD。
- 3:4 档成广角/鱼眼（`CameraViewportFitter` 既有降级，非本单引入）。
- **`Shoot` 按钮本单只做显示态**（可用/禁用随 `ShotPhase`），**不接点击** —— 排期 Day 13 只要求 HUD 元素；
  出杆通路仍是既有的右键拉杆 + `Space`（`PhysicsTestDrive.launchKey`）。**点击接线登记为后续项**，
  本单不得声称"按钮可用"。

---

## 8. 完成定义（DoD）

1. 本单三条判据 `T95` / `T96` / `T97` **全绿**；全量 EditMode **183 → 186**（+3）。
2. 三条判据各有一组**实测红**的注入负对照，且逐字节还原（sha1 一致）。
3. 四个验收器（Day1/2/3/4）在含 TMP 资产的工程上重跑，**25 / 33 / 22 / 7，FAIL=0 WARN=0**。
4. 场景已重建（HUD 宿主接线）；`git status` 干净（测试不产仓库改动，R3-5）。
5. 台账 `Docs/测试数基线台账.md` 追加 Day 13 行（带提交哈希锚点）。
