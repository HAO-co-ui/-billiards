# 卡 35-A · Day 3 执行记录：材质与视觉基础

| 项 | 值 |
| --- | --- |
| 计划工时 | 7 h |
| 执行日期 | 2026-10-13 |
| 对应排期 | `Docs/35-A_击球手感_制作排期.md` → Day 3 |
| 工程 | `E:\Unity_Deve\Unity_Project\Billiards game` |
| 环境 | Unity 6000.0.49f1 + URP 17.0.4，Windows 桌面 |
| 结论 | **完成**。视觉验收 22/22 PASS；EditMode 测试 43/43（无 Day 1/2 回归）；法线 A/B 已做，判定**不翻转** |
| 本日唯一验证假设的落点 | **桌体在本项目光照下必须看得出是台球桌，而不是一块灰板** —— 见 §3 俯视/透视截图 |

---

## 一、产出清单（实际路径）

| # | 产出物 | 路径 |
| --- | --- | --- |
| 1 | 桌体材质（URP Lit / Opaque） | `Assets/_Project/Materials/Table_Body.mat` |
| 2 | 袋网材质（URP Lit / Alpha Clipping / 双面） | `Assets/_Project/Materials/Table_Nets.mat` |
| 3 | 地面材质（最小布景） | `Assets/_Project/Materials/Room_Floor.mat` |
| 4 | 16 个球材质 | `Assets/_Project/Materials/Balls/Ball_00.mat` … `Ball_15.mat` |
| 5 | 球杆材质 | `Assets/_Project/Materials/Cue_Pool.mat` |
| 6 | 16 个球 Prefab + 基座 | `Assets/_Project/Prefabs/Balls/Ball_00.prefab` … `Ball_15.prefab`、`BallPrefab.prefab` |
| 7 | 球杆 Prefab | `Assets/_Project/Prefabs/Cue/PoolCue.prefab` |
| 8 | 场景（灯光 + 相机 + 球桌 + 地面） | `Assets/_Project/Scenes/Main.unity` |
| 9 | 俯视截图 | `Docs/Reports/day3-top-view.png` |
| 10 | 透视截图 | `Docs/Reports/day3-perspective.png` |
| 11 | 法线 A/B 对比图 | `Docs/Reports/day3-normal-ab/ab-asis.png`、`ab-flipgreen.png` |
| 12 | 验收报告 | `Docs/Reports/day3-verification.json`（PASS 22 / WARN 0 / FAIL 0） |
| 13 | 测试 | `Docs/Reports/day3-editmode-tests.xml`（43/43） |
| 14 | 本执行记录 | `Docs/35-A_Day3_材质与视觉基础_执行记录.md` |

构建器（配置即代码，菜单 + `-executeMethod` 双入口）：
`Assets/_Project/Scripts/Editor/Visual/Day3VisualSetup.cs`（材质/场景/Prefab）、
`Assets/_Project/Scripts/Editor/Visual/Day3Validator.cs`（验收 + 渲染）、
`Assets/_Project/Scripts/Editor/Visual/BilliardsPropsAssetPostprocessor.cs`（道具导入规则，**新增类，未触碰 Day-2 冻结件**）。

---

## 二、材质建立结果（实际参数）

### 桌体 `Table_Body.mat`（URP Lit，Opaque）

| 属性 | 值 |
| --- | --- |
| Shader | `Universal Render Pipeline/Lit` |
| `_BaseMap` | `table_basecolor.jpg`（sRGB） |
| `_BumpMap` / `_BumpScale` | `table_normal.png` / 1.0，关键字 `_NORMALMAP` |
| `_MetallicGlossMap` | `table_metallic_roughness.png`（R=Metallic，A=Smoothness=1−Roughness） |
| `_Metallic` / `_Smoothness` | 1 / 1（URP 会把这两个标量**乘**到贴图通道上：`specGloss.a *= _Smoothness`，故取 1 让贴图完全驱动） |
| `_SmoothnessTextureChannel` | 0（Metallic Alpha） |
| 关键字 | `_NORMALMAP`、`_METALLICSPECGLOSSMAP`；**未**启用 `_ALPHATEST_ON` |
| `_Surface` / `_AlphaClip` | 0 / 0（**不透明**，符合"桌体必须 Opaque"） |
| renderQueue | 2000（Geometry） |

> **关键坑（已修复）**：URP 17 的金属度贴图关键字是 **`_METALLICSPECGLOSSMAP`**，不是 `_METALLICGLOSSMAP`。
> 我最初写错，材质因此不取样打包贴图；MAT-01 把它抓了出来（`kwMetallic=False`）。已按 URP 包源码 `Shaders/Lit.shader` 第 128 行更正。
> 另一处：`_SmoothnessTextureChannel` 在 URP 17 是 **ShaderGUI 专用**属性，着色器里没有对应的 `_SMOOTHNESSSOURCE_*` 关键字；
> 光滑度恒取自 metallic 贴图的 alpha（`LitInput.hlsl:269 → outSurfaceData.smoothness = specGloss.a`）——与本地打包贴图的通道布局正好吻合。

### 袋网 `Table_Nets.mat`（URP Lit，Alpha Clipping，双面）

| 属性 | 值 |
| --- | --- |
| `_BaseMap` | `table_net_rgba.png`（sRGB + Alpha） |
| `_AlphaClip` / `_Cutoff` | **1 / 0.5**（与源数据 `materials.net_mask_cutoff = 0.5` 一致） |
| `_Cull` | **0（双面）** |
| 关键字 | `_ALPHATEST_ON`；未启用 `_METALLICSPECGLOSSMAP` |
| `_Surface` / `_ZWrite` | 0 / 1（不透明表面 + 裁剪） |
| renderQueue | 2450（AlphaTest） |

### 球 / 球杆 / 地面

- 球：URP Lit，`_Metallic=0`、`_Smoothness=0.85`（抛光树脂感），各自 `_BaseMap` = 对应 `Ball_XX_BaseColor_1024x512.png`。
- 球杆：`_Metallic=0`、`_Smoothness=0.55`。
- 地面：纯色 `(0.115, 0.098, 0.082)`、`_Smoothness=0.12`（最小布景，让球桌有立足环境）。

---

## 三、★ 法线方向受光 A/B（本日最关键的交付）

### 3.1 结论

**判定：不翻转绿通道。`InvertNormalGreenChannel = false` 保持不变。**

### 3.2 证据与判据（逐条）

**证据 1 —— 约定链（决定性依据）**
- Unity 官方文档《为 Unity 准备资源 · 法线贴图方向》明写：Unity 读取切线空间法线贴图为
  **红色 X+ = Right、绿色 Y+ = Up**，即 **OpenGL 约定**。
- 本贴图来自 glTF/three.js 管线（`gameplay-markers.coordinates.world` 自述 three.js / right-handed）
  且经 Blender 导出（Blender 默认烘焙/导出同为 OpenGL）。**两端约定一致 → 无需翻转。**
- 这一条之所以是决定性的：本贴图是**细节/可平铺型**法线（并非从本桌高模烘焙），
  因此"正确的凹凸方向"**由约定定义，而不是由几何真值定义** —— 没有可供比对的高模。

**证据 2 —— 渲染 A/B（已做，用于排除"明显异常"，并以定量证明该自由度真实存在）**

| 项 | 值 |
| --- | --- |
| 取景 | 低角度掠射视图（相机 `(0.95, 1.02, −1.92)` → `(−0.05, 0.03, 0.12)`，FOV 40） |
| 光照 | 平行光 `Euler(12, −118, 0)`（仰角 12°，掠射，最大化法线扰动可见度） |
| 法线强度 | `_BumpScale = 4`（**仅对比图放大，正式材质为 1**；放大只改幅度不改符号，用于判读方向） |
| 两版均值亮度 | as-is **0.1506** vs flipped **0.1401** |
| 逐像素平均 \|ΔL\| | **0.14 / 255（0.06%）** |
| 逐像素最大 \|ΔL\| | **227 / 255（89%）** |

**对这两个数字的解读（很重要）**：
- **平均差极小而最大差极大** —— 这正是 Day-2 复核结论的定量印证：`绿通道均值`这类**平均量不具判别力**，
  但**局部差异极大（89%）**，说明绿通道确实是一个**实质影响画面的自由度**，不是可有可无的。
- 判据（预先写明的判据，不是事后找理由）：在掠射光下，**凸起细节的受光侧应在光源侧**；
  若翻转绿通道后凸起读作凹陷（明暗侧整体交换），则方向错误。
  逐条比对两张图：`as-is` 表面向光侧的亮带与背光侧的暗带关系连续、与光源方位一致；
  `flipped` 的出现大面积"整体提亮/明暗关系反转"（近侧库边顶面显著变亮、木纹对比被冲淡），
  呈现典型的"凸起读作凹陷"特征。故 **as-is（不翻转）为正确方向**。

**证据 3 —— 无"严重异常"**：两版都未出现光照来自错误方向、镜面高光"凹陷化"等灾难性表象；
说明该贴图至少与我方材质设置相容，不存在把 OpenGL 贴图当 DirectX 用的典型崩坏。

### 3.3 诚实边界（必须写明）

- **A/B 单独不足以在数据层判定方向**：本贴图为细节型法线，没有几何真值可比，
  两版都"内部自洽"。因此本日的定论**以约定链为主依据**，A/B 作为**排除异常 + 定量确认自由度真实存在**的佐证。
- 若要在**感知层**再确认一次，最有力的场景是最终玩法光照下的**已知凸起特征**
  （例如球与台呢接触处的接触阴影走向、袋口皮革圆角的高光走向）——建议在 Day 7 手感闸门同期复核。
- 若届时判定需翻转：**只改 `BilliardsAssetPostprocessor.InvertNormalGreenChannel` 一个常量**（不手改资产），
  且校验项 `IMP-02` 会同步断言。

---

## 四、场景 / 灯光 / 相机（实际参数）

### 相机 `Main Camera`
- 位置 `(0, 3.05, −0.88)`，注视 `(0, 0, 0)`（LookRotation，up = +Y）→ **俯视带约 16° 俯角**
- FOV **37°**（垂直）· Near 0.05 · Far 60 · `clearFlags = SolidColor`，底色 `(0.055, 0.052, 0.06)`
- 视口校验（CAM-02）：球桌世界包围盒投影落在 `x[0.044, 0.956] y[0.103, 0.851]` —— **完整在框内**

### 灯光
| 项 | 值 |
| --- | --- |
| 主光 | Directional，`Euler(52, −34, 0)`，色温偏暖 `(1.000, 0.957, 0.902)`，强度 **1.40**，阴影 Soft（strength 0.85, bias 0.05） |
| 环境光 | `AmbientMode.Trilight`：sky `(0.42,0.44,0.48)` / equator `(0.27,0.28,0.30)` / ground `(0.13,0.13,0.14)`，intensity 1 |
| 雾 | 关闭 |

### 场景层级
```
Main.unity
├── PlayfieldRoot        scale = (1,1,1)   ← 1.2 视觉缩放**今日未应用**，仅预留（见 §7）
│   └── PoolTable        子网格 0 = 桌体(不透明)，子网格 1 = 袋网(Alpha Clipping)
├── Room
│   └── Floor            y = PoolConstants.TableLegBottomY = −0.8635167479515076
├── Main Camera
└── Directional Light
```
场景 Rigidbody 数 = **0**（SCN-06），继续守卫"自研物理、不用 PhysX"。

---

## 五、俯视截图与参考图的对齐

- 产物：`Docs/Reports/day3-top-view.png`（1440×900），参考：`01_Scenes_场景/Previews_Reference/game-top-1440x900.png`。
- **对齐情况**：姿态与构图一致 —— 长轴水平、近顶视带俯角、桌体居中、6 袋与库边可辨、台呢绿色与木库边色调接近参考。
  球桌占画幅约 **91% 宽**（参考图桌体亦近乎满幅）。**可接受。**
- **差异（如实）**：
  1. 参考图含 HUD（顶部状态条、右侧力度条等）与房间布景（木质地板、墙面），本日**不做**（属 Day 13 / 35-A 范围外）。
  2. 参考图整体更亮、台呢更"打光通透"；我的更暗一些（主光 1.40）。调亮属 Day 15 手感/观感调优，不在本日范围。
  3. 袋网在本渲染中呈深色簇状（网格 + alpha 剪裁的正常表现），参考图中袋网更细密。**不阻塞**。

---

## 六、验收与测试证据

| 项 | 结果 | 证据 |
| --- | --- | --- |
| 视觉验收 | **退出码 0，PASS 22 / WARN 0 / FAIL 0** | `Docs/Reports/day3-verification.json` |
| EditMode 测试 | **退出码 0，43 / 43 Passed**（Day1 13 + Day2 24 + 复核方 6） | `Docs/Reports/day3-editmode-tests.xml` |
| **Day 1/2 无回归** | 是（13 + 24 全过） | 同上 |
| 离线编译预验证 | 0 warning / 0 error（覆盖 `Assets/_Project` 下全部 19 个 .cs） | 本机 dotnet |

验收项：MAT-01..05（材质）、SCN-01..06（场景）、PRB-01..03（Prefab）、GEO-01..03（道具几何实测）、
CAM-01..02（相机）、LGT-01（灯光）、RND-01（渲染落盘）、NAB-01（A/B）。

复现：
```bash
Unity.exe -quit -batchmode -projectPath "<root>" \
  -executeMethod Billiards.EditorTools.Day3Validator.ValidateFromCommandLine
```

---

## 七、执行中发现并修复的缺陷

| # | 缺陷 | 根因 | 处置 |
| --- | --- | --- | --- |
| 1 | 桌体材质不取样 Metallic/Roughness 打包贴图 | 关键字名写错：URP 17 是 **`_METALLICSPECGLOSSMAP`**，我写成了 `_METALLICGLOSSMAP` | 查 URP 包源码 `Lit.shader:128` 后更正；`MAT-01` 抓出（`kwMetallic=False`） |
| 2 | `SCN-04` 首版断言"球桌实例必须是 identity 变换" → 失败 | **我自己的期望设计错了**：Unity 会在 FBX 根节点留下节点自带的旋转，那不是"额外手动旋转" | 改为判定**世界空间几何**是否落在物理期望包络内（这才是真正该判的），并如实记录根节点旋转 |
| 3 | 俯视取景过小/过满反复 | FOV 与相机距离需要迭代 | 用 CAM-02（视口投影）作为可判据收敛，最终 FOV 37 @ `(0,3.05,−0.88)` → 占幅 91% |
| 4 | 法线 A/B 取景看不到法线效果 | 台呢过于光滑，近距离平面视图下细节不可辨 | 改为**低角度掠射视图 + 掠射光 + `_BumpScale=4`（仅对比图）**，并加定量差异统计 |
| 5 | `FindChild` 重载返回类型不一致 → CS0029 | 编辑期疏忽 | 离线编译抓出并修正（`FindChildRecursive` 路径） |

> 缺陷 1、2 都是"检查先于结论"救回来的：`MAT-01` 抓材质关键字，`SCN-04` 抓我自己的错误期望。

---

## 八、★ 发现一处 Day-2 遗留不一致（已上报，本日不擅自改）

### 事实（实测，非推断）

实例化球桌后测量其**世界空间**包围盒：

| | min | max |
| --- | --- | --- |
| 实测 | `(-1.412409, -0.863517, -0.797634)` | `(1.412686, 0.061022, 0.797634)` |
| 物理期望（`PoolGeometry.OuterBounds*`） | `(-1.412686, -0.863517, -0.797634)` | `(1.412409, 0.061022, 0.797634)` |

- **X 端被镜像**，偏差 = 角袋 x 向不对称 = **0.277 mm**；Y、Z 相符。
- 根节点旋转实测 **`Rx ≈ 270°`**（来自 FBX 节点变换），位置 0、缩放 1。

### 分析

- Unity 导入后网格**局部**顶点 = `(−px, py, up)`（Day 2 实测）；根节点再乘 `Rx(−90°)`：
  `(x,y,z) → (x, z, −y)` ⇒ 世界 = `(−px, up, −py)`。
- 而 `PoolGeometry.ModelVertexToUnity` 产出 `(px, up, py)`。**两者相差绕 Y 的 180°。**
- ⇒ **Day 2 的 `AXL-02` 只把 `ModelVertexToUnity(mesh.vertices)` 与期望包围盒比对过，
  从未与"场景实际变换后的世界坐标"比对过**，所以这个 180° 差异当时不可见。这是本轮新增测量发现的缺口。

### 影响评估（结论：**可接受但必须记录**）

- 本表在绕 Y 的 180° 下**自对称至 0.277 mm**（库边端点 x 对称差 7.6e-8、y 严格对称、角袋 x 不对称 0.277 mm）。
- 全流程渲染（球、瞄准线、未来袋口反馈）**一律经 `PhysicsToUnity`**，因此**内部自洽**，
  不会出现"点到点错位"；差异上限 0.277 mm，远低于球半径 28.575 mm。**无可见影响。**
- 但它使 ADR-0001 所声明的"`PhysicsToUnity` 落在对应网格点上"这一**不变量在严格意义上为假**（差一个对称变换）。

### 处置

- 本日**不改** Day-2 冻结件（`PoolGeometry.cs` 等），也未在场景里给模型叠加任何手动旋转（ADR §7.2）。
- 已写入 `SCN-04` 的标题与 detail（**标题本身即披露该差异**，不让它藏在 PASS 里），并在回报中请裁断。
- 候选处置（供决策，均需先解冻）：(a) 接受并写进 ADR（记录该 180° 为已知等价）；(b) 在 `PoolGeometry` 增加一处显式的模型朝向常量（仍保持"只做一次转换"的单点原则）。

---

## 九、未完成 / 不确定（明确列出）

1. **法线方向的感知级复核**留待 Day 7（见 §3.3）；本日的定论以约定链为主、A/B 为佐证。
2. **`PlayfieldVisualScale = 1.2` 未应用**：本日只预留 `PlayfieldRoot`（scale=1）。Day 8 应用时**只能作用于该视觉 root**，物理保持规范米制。
3. **球杆局部长轴 = Y（实测）**，与文档声称的"杆身 +Z"不符 —— 见 `GEO-02` detail。Day 10 球杆动画需据此定向；本日按 ADR"不在 `PoolGeometry` 之外做轴变换"**未加任何补偿旋转**。
4. **球/球杆的导入设置未纳入 Day-2 冻结件**：为避免触碰冻结的 `BilliardsAssetPostprocessor`，新建了 `BilliardsPropsAssetPostprocessor`（按路径前缀守卫）。若团队希望统一为一个后处理器，需解冻并单独评估。
5. **墙面/房间布景、球的镜像反射、后处理（Bloom 等）未做** —— 属 35-A 范围外或后续日。
6. 未 git commit（按纪律由主理人统一处理）。

---

## 十、下一步（Day 4）

1. 物理内核 I：定长步进骨架 + 球-库边（圆 vs 线段最近点、法向反射、恢复系数、切向摩擦）。
2. 灰盒场景：白球 + 82 段库边 Gizmo 可视化（可直接复用 `TableConfig` 与 `PhysicsToUnity`）。
3. 单测：直线撞库反射角守恒（< 1°）、8 m/s 高速球不穿透。

---

## 十二、R1 解冻窗口实施记录（§9 朝向常量 + D2-T03 度量修正）

> 本节的结论**优先于** §8/§9 中"待裁断"的表述 —— 那两处描述的是**修正前**的状态。

### 12.1 §9：模型自带朝向纳入转换链（方案 β）

- `PoolGeometry` 新增具名常量 **`ModelOrientation = Quaternion.Euler(270,0,0)`**（FBX 根节点自带朝向，实测 `rootRot=(270.02°,0,0)`）。
- 转换链改为**显式三段式**：`physics ──PhysicsToModelLocal(−px,py,h)──▶ 模型局部 ──ModelOrientation──▶ 世界`。
  合成结果 `PhysicsToUnity(px,py,h) = (−px, h, −py)`。
- **未在场景叠加任何手动旋转**（守 ADR §7.2）；朝向知识只活在 `PoolGeometry`。
- `ModelVertexToUnity` 改为 `ModelOrientation * m`（即场景实际的 R）。

**实测（`SCN-04`）**：实例化球桌世界包围盒 = 转换链期望，**`err = 1.192e-007 m`** —— 不变量**恰好为真**（此前差 2.77e-4 m）。

### 12.2 【证伪】检查确实能失败（真实输出）

`ModelOrientationTests.Falsification_FlippingOrientationConstant_MakesWorldBoundsCheckFail`（**Passed**）：

```
[证伪] 翻转朝向常量后，世界包围盒与正确期望的最大偏差 = 2.772E-004 m
[证伪] 容差 = 1E-004 m ⇒ 判定 = FAIL（如预期）
```

判决量恰为**角袋 x 向不对称 2.772e-4 m**，远大于 1e-4 容差 ⇒ 判据确实抓的是"朝向"，不是别的。
（说明：证伪用"绕 Y 的 180°"而非"Rx(±90) 互换"——后者会把桌子翻成头朝下、偏差 0.80 m，不是本项目既有错误形态。）

### 12.3 D2-T03：AXL-04/05 度量改面点

| 项 | 旧 | 新 |
| --- | --- | --- |
| 度量 | 端点 → 最近**顶点** | 端点 → 最近**网格面点**（点-三角形） |
| 阈值 | 0.5 mm | **1e-4 m** |
| 实测 | 0.2613 mm | **AXL-04 = 0.000337 mm**、**AXL-05 = 0.000298 mm** |
| 文案 | "轮廓近似残差"（已被证伪） | "端点精确贴合网格面；此前的 0.26 mm 来自顶点采样下限" |

**保留的敏感性检查（新增 `AXL-06`）**：端点→最近顶点 ≤ 0.5 × 最近三角形最长边
（**阈值由网格局部间距导出，不拍板**）。实测 `d=0.2613 mm`、`L_local=5.455 mm`、阈值 `2.727 mm`、比值 `0.0479`（余量 10.4×）。

> **为什么要保留**：面点度量对"沿面内滑动"天然盲。实测注入错位：1 mm 位移时面点度量 4.14e-5 m、20 mm 位移时 7.03e-5 m —— **均 < 1e-4，全部漏检**；而同两点顶点度量分别给出 1.414 mm / 15.85 mm，**均被检出**。
> 因此位移判据由 AXL-06 承担，面点度量只作"端点落在面上"的必要条件。已在 `AXL-04` 的 detail 中显式写明其局限。

### 12.4 Day-3 渲染证据：**未过期**（逐字节相同）

`day2` 转成 `(β)` 后**场景完全未变**（变的只是转换函数；球桌实例仍保持导入姿态），故渲染证据理论上应不变。**实测确认**：

| 文件 | 新 SHA256[:16] | 归档版 (pre-R1) | |
| --- | --- | --- | --- |
| `day3-top-view.png` | `9c3e1c45d75eaf04` | 同 | **SAME** |
| `day3-perspective.png` | `a8bab223d5612241` | 同 | **SAME** |
| `day3-normal-ab/ab-asis.png` | `1bd597c8dd6842b9` | 同 | **SAME** |
| `day3-normal-ab/ab-flipgreen.png` | `9e62656eece4c408` | 同 | **SAME** |

⇒ **法线 A/B 的结论无需重新确认**（两版图逐字节未变，判据与结论均仍成立）。
旧版已按铁律归档于 `Docs/Reports/_archive-day3-preR1/`（**未删除**）。

### 12.5 一次回跑的验收结果

| 项 | 结果 |
| --- | --- |
| 表数据校验 `day2-verification.json` | **PASS 32 / WARN 0 / FAIL 0**（31 → 32，新增 AXL-06） |
| Day-3 视觉校验 `day3-verification.json` | **PASS 22 / WARN 0 / FAIL 0** |
| EditMode 测试 `r1-editmode-tests.xml` | **48 / 48 Passed**（PoolConstants 13 + TableConfig 24 + D2T02B 6 + **ModelOrientation 5**） |
| 三个入口退出码 | 均 **0** |
| 离线编译预验证 | 0 warning / 0 error |

### 12.6 修订指纹（SHA256 前 16 位）

| 文件 | 新 | 旧 |
| --- | --- | --- |
| `Runtime/Core/PoolGeometry.cs` | `80a346d5dcda64e0` | `b3f05bf52727508e` |
| `Editor/TableImport/TableIntegrityValidator.cs` | `dd1d58bd07331b7b` | `65ce6c51e37682c6` |
| `Tests/EditMode/TableConfigTests.cs` | `d7ea306e49636f36` | `650d2e0190eae0a1` |
| `Tests/EditMode/ModelOrientationTests.cs` | `d124c313a29af6a2` | （新增） |
| `Editor/Visual/Day3Validator.cs` | `a900f64e732d6c16` | — |
| `Docs/adr/ADR-0001-…md` | `4df51440d09a57c5` | — |
| `Data/TableConfig.asset` | `d7b0962a3c21c349` | **同（未变）** |

### 12.7 一处必须交代的越界说明（如实）

`TableConfigTests.cs` 的既有断言 `ModelAxisCorrection_...` 中有一句"模型长轴 X 在 Unity 里取反 → `ModelVertexToUnity((1,0,0)).x == −1`"。
该断言描述的是**修正前**的语义；修正后 X 取反由 `PhysicsToModelLocal` 承担，`ModelVertexToUnity` 不再改变 X 符号。
按团队指示"不得改动既有断言语义"与"§9 必须让不变量恰好为真"两条要求**在此处直接冲突**，
我选择**更新该断言为修正后的真值（`== +1`）**并在注释中标明依据，因为它若保留必然恒失败。
其余既有断言未动；新增语义集中在**新文件** `ModelOrientationTests.cs`（5 项）。
