# 35-A 球房环境（Room Environment）规格与执行记录

> 目标：俯视球桌时，桌子周围要有**木地板 / 桌下地毯 / 深色墙 + 木墙裙 / 盆栽 / 皮长凳**，
> 以及边桌、墙上球杆架、相框、灯等摆件。**用美术包的真 FBX，不用图元。**

落点 = `Room`（由 `TableVisualBuilder.AttachRoom` 创建、挂在 `PlayfieldRoot` 视觉缩放根之下）。
唯一实现点 = `Assets/_Project/Scripts/Editor/Visual/RoomEnvironmentSetup.cs`。
本文件登记：**摆位真源 / 全部对权威数据的偏离 / 判据口径变更 / 预算与实测**。

---

## 1. 资产真源（全部在工程外，已拷入）

| 类别 | 工程内落点 | 数量 |
|---|---|---|
| 道具模型 | `Assets/_Project/Art/Models/Environment/*.fbx` | 13（Bench / Ceiling_Light / Chalk / Cue_Rack / Floor_Lamp / Painting / Potted_Plant / Rack_Triangle / Rug / Side_Table / Wall_Lamp / Wall_Trim / **Room_Walls**） |
| 贴图 | `Assets/_Project/Art/Textures/Environment/` | 12 张道具 jpg + `source_floor_atlas_1024.jpg` + `room_floor_planks.png`（生成物，见 §5） |
| 生成网格 | `Assets/_Project/Art/Models/Environment/Room_Walls_Combined.asset` | 1（墙 17 子网格合并产物） |
| 材质 | `Assets/_Project/Materials/Room/*.mat` | 9（见 §4） |

包内路径：`F:\工作文件\idea35_台球\03_Models_模型\FBX_Exports\Recommended\Environment\`；
墙取 `...\FBX_Exports\SceneComponents\Reference_Existing_Walls.fbx`（跨目录，已获主理人裁定）。
⛔ **未使用** `03_Models_模型\GLB\` 与 `01_Scenes_场景\Runtime_Reference\room.glb`（本工程无 GLB 管线）。

**未拷入**（有意）：
- `Floor_Tile.fbx` —— 地板不逐块实例化，改为**复用既有 `Floor` 渲染器 + 平铺贴图**（预算，见 §6）。
- `Reference_Ground_Fill.fbx` —— 8×8 平面，被 30×30 的 `Floor` 完全遮住，白占 1 渲染器。
- `Ceiling_Light.fbx` —— **已拷入但未使用**，理由见 §3.2。

## 2. 摆位真源与姿态口径（**实测，非照文档**）

**权威真源** = `01_Scenes_场景\Runtime_Reference\asset-metadata.json` 的 `room.placements`（37 条 = 24 块地板 + 13 件道具），
逐条含 `bottom_center_runtime` / `dimensions_runtime_xyz`。本实现取其 13 件道具的**底面中心**。

**姿态（离线直读 FBX 二进制实测，工具复用 `Docs/Tools/fbx_mesh_probe.py` 的解析器）**：
- 全部环境 FBX（含球桌）`GlobalSettings` 一致：`UpAxis=Y / UpAxisSign=1`、`UnitScaleFactor=100`；
- 每个 `Model` 节点带 `Lcl Rotation = (-90.000009°, 0, 0)`，**源顶点为 Z-up、底面在 Z=0**
  ⇒ 该节点旋转即"扳成立姿"的那一步，与球桌 FBX 同源（球桌 `SCN-04` 实测 `rootRot=(270.02,0,0)`）。
- **⇒ 本实现不叠加任何手动旋转**；`bakeAxisConversion=false`、`useFileScale=false`、`globalScale=1`。

**Unity 内实测复核**（`rebuild-main-base` 日志，逐件打印包围盒）：

| 道具 | 实测(规范) vs 权威 `dimensions_runtime_xyz` | Δ落位 |
|---|---|---|
| Rug 3.650/0.025/2.650｜Bench 1.600/0.531/0.600｜CueRack 0.880/1.520/0.270｜PottedPlant 0.731/1.500/0.800｜SideTable 0.798/0.720/0.798｜RackTriangle 0.430/0.053/0.430｜Chalk 0.063³｜FloorLamp 0.361/1.520/0.360｜WallLamp×2 0.430/0.530/0.360｜Painting 0.890/0.760/0.102｜WallTrim 1.220/0.720/0.104 | **12/12 全部 Δ落位=(0,0,0)；Δ尺寸 ≤ 0.002 m** |

⇒ "立姿/朝向正确"由此**独立佐证**（若躺倒/贴反，Y 向尺寸会与权威值对不上）。

**落位方式**：植入后按**实测世界包围盒**把根平移，使 AABB 的 `(center.x, min.y, center.z)` 落到目标底面中心 ⇒
对 FBX 自带的节点平移/旋转**免疫**（不依赖"重置位置"能否清干净）。

## 3. 对权威数据的**偏离**（共 3 处，逐条登记）

### 3.1 整组下移 `FloorOffsetY = TableLegBottomY − (−0.78) = −0.0835`（**主理人裁定：批准**）
参考房间地面 `floor_y = −0.78`，本工程 `PoolConstants.TableLegBottomY = −0.8635167…`
（`SCN-07` 断言"地面世界 y == 桌子 AABB min.y"）。若照 −0.78 摆，12 件道具**整体浮空 8.35 cm**。
⇒ 对**整组环境**（墙 + 全部道具）施加**同一个**偏移，**相对排布完全不变**（墙裙/相框/壁灯的相对高度不动）。
实测：落地件 `AABB.min.y` 与地板 y 之差 ≤ 1 mm（`Rug` 有意抬 1 mm 防 z-fighting）。

### 3.2 `Ceiling_Light` **不纳入**（主理人裁定：去掉）
理由：美术包**没有天花板网格**（17 块墙里无顶面），该件底面在整个房间的上沿、**顶面高出墙顶 0.70 m**
⇒ "悬在开口上方的灯"，物理上**无处可挂**。另：它在**玩法取景**里整件落在画外（13 件里唯一"框外 + 无法解释"的一件）。
登记：**将来若补天花板/顶棚**，按 `asset-metadata` 的 `bottom_center (0, 1.42, −1.58)` 与
`dimensions (1.580, 0.700, 0.530)` 加回即可（FBX 已在库内）。**另注**：它悬在打球区正上方，实测会在台呢上
投一大块阴影（`day3-top-view.png` 可见）—— 这是去掉它的**附加收益**（本来需单独关投影）。

### 3.3 `Bench` 的 z：`−1.90 → −1.85`（**主理人裁定：挪掉**）
理由：权威值使长凳背面与墙上墙裙板（`WallTrim`）在 z 向**互穿 5.2 cm**（离线 AABB 实测）。
**互穿是缺陷类别，不该因"参考数据自带"而放行**；5 cm 的水平位移在观感上不可见，但"长凳嵌进墙裙"看得见。
⇒ 仅改这一处常量，登记为**对权威摆位的唯一位置偏离**。

**其余不动的重叠**（离线体检结论）：`Rug×TABLE`（地毯在桌下）= 设计如此；
`CueRack×PottedPlant` / `PottedPlant×WallLampL` / `SideTable×FloorLamp` / `Rug×SideTable` / `RackTriangle×Chalk`
= 圆/锥形体用 AABB 判交的**假阳性**（实际是细杆/收口，不真穿）；`WallTrim` 离地 9 cm = 参考原样。

## 4. 材质：**按"能不能被看见"分级**（主理人裁定口径）

不是一刀切共享 —— **参考图里清晰可见的件各自独立**，次要/小件才共享：

| 材质 | 贴图 | 使用件 |
|---|---|---|
| `Room_Walls`（纯色深墙） | —（无 UV 的参考盒） | `Walls` |
| `Room_Rug` | ornate_rug | `Rug` |
| `Room_Plant` | potted_plant | `PottedPlant` |
| `Room_Bench` | tufted_leather_ottoman | `Bench` |
| `Room_WallTrim` | wooden_chalkboard | `WallTrim` |
| `Room_SideTable` | wooden_round_table | `SideTable` |
| `Room_Painting` | framed_landscape_painting | `Painting` |
| `Room_WoodSmall` | wooden_triangle_frame | `CueRack` + `RackTriangle` + `Chalk` |
| `Room_Metal` | brass_wall_lamp | `FloorLamp` + `WallLampLeft` + `WallLampRight` |

**地面复用既有 `Room_Floor.mat`**（只换贴图为 `room_floor_planks.png` + `_BaseMap_ST` 平铺 24 次）⇒ **0 新增**。
⇒ **新增材质 = 9**。初版"一刀切共享"（`Room_Wood` / `Room_Brass` / `Room_Chalk`）已由构建入口
`AssetDatabase.DeleteAsset` **自动清除**（避免留孤儿资产）。

## 5. 地板贴图：**可复现 + 可追溯**

生成脚本（入库）：`Docs/Tools/make_room_floor_planks.py`（范式同 `make_ui_sprites.py`）。
- **输入**：`wood.jpg`（美术包自述 "A flat wood face cropped directly from the real source UV atlas"，
  即**图集派生物**，不引入工程外新美术）；`source_floor_atlas_1024.jpg` 只用于生成对照图。
- **输出**：`Assets/_Project/Art/Textures/Environment/room_floor_planks.png`（1024²，LFS）
  + `Docs/Reference/room_floor_texture_choice.png`（对照图：左=原图集 3×3 平铺**发花**，右=本产物平铺）。
- **为什么不能直接用原图集**：它是 24 块地板 tile 的 **UV 打包图集**（板条散落、带烘焙接缝与透视），
  3×3 平铺会得到**明显重复的杂乱花纹 + 接缝**（见对照图左）。参考图要的是**成片木板地面**。
- **尺度依据**：1024² 视作 **1.5 m 见方** ⇒ 板条 0.75 m × 0.19 m（与真实实木地板 0.1–0.2 m 板宽同量级）；
  地面世界 36 m 见方 ÷ 1.5 = **24 次平铺**；约 683 px/m，本工程取景下不糊。
- **确定性**：`random.seed(35)` 固定 ⇒ **与入库 PNG 逐字节一致**（sha256 `21498245ac33…`，395094 B）。

## 6. 预算与实测（`SmokeProbe`：≤60 渲染器 / ≤30 材质）

- 渲染器：墙 **17 → `CombineMeshes` 合 1**（408 顶点），12 件道具各 1 ⇒ **新增 13**。
- 材质：**新增 9**（§4）。
- **实测（最终 A 档口径下，`smoke.json` / `smoke-aim.json`）**：

| 档 | rendererCount | 材质数 | meanMs | fpsMean | 掉帧 | homeStartup |
|---|---|---|---|---|---|---|
| static | **49 / 60** | **30 / 30** | 6.9442 | 144.00 | 0 | suppressed |
| drag | **55 / 60** | **30 / 30** | 6.9442 | 144.01 | 0 | suppressed |

⚠ **材质 30/30 = 压线，余量 0**。
- **再加道具 / 材质必须走图集**（把多件贴图打进一张图 + 构建期重映射各件网格 UV 到子矩形）
  —— 否则必然破 `MaxDistinctMaterialCount`。
- **不许**为省一格把 `Chalk` 并进 `Room_Metal`（粉笔会变金色 = **可见的错**，比"余量紧"更糟）；
  **不许**擅自放宽 `MaxDistinctMaterialCount`。
- **已知的观感妥协（如实登记）**：`Room_WoodSmall` 让 **球杆架 + 三角架 + 粉笔**共用一张木纹贴图
  （`wooden_triangle_frame`）⇒ 这三件的木色细节不是各自的原始贴图。选择理由：三者都是小件、
  且"图集方案"的工程量与风险显著更高；若将来要恢复逐件贴图，走图集（见上）。

## 7. 取景（**用户已裁定放宽；已实施 A 档**）

**变更前（冻结相机）下房间整片在画外** —— 相机 `(0,2.10,−0.75)` 朝 +Z、俯角 68.7°、离桌 2.25 m，
画面只覆盖桌远库外约 0.6 m ⇒ **12 件道具的包围盒 8 个角全部落在视锥外**（离线逐角投影实测）。
根因：参考房间的装饰全在 **−Z 一侧**（后墙 z≈−2.2），而相机站在 z=−0.75 朝 +Z 看 ⇒ 后墙在**背后**。

用户裁定：**放宽取景让房间入画**。**采纳档 = A 档（桌子优先）**。候选与实测（按 `CAM` 精确口径：aspect 1.6 + fitter 抬后的有效 fov）：

| 档 | 相机位 / 注视点 / fov | 横向占比 | 外框裁切 | 房间可见 |
|---|---|---|---|---|
| 基线（旧冻结） | (0, 2.10, −0.75) / (0, 0.02, 0.06) / 52 | 0.900 | 3.9% | ❌ 只地毯 |
| **A 桌子优先（已实施）** | **(0, 2.10, +2.80) / (0, 0.05, −1.00) / 53** | **0.616** | **4.7%** | ✅ 12/12 |
| B 平衡（候选） | (0, 2.80, +2.60) / (0, 0.05, −1.00) / 52 | 0.556 | 2.1% | ✅ 12/12 |
| C 房间优先（候选） | (0, 3.90, +2.60) / (0, 0.05, −1.00) / 50 | 0.448 | 0.0% | ✅ 12/12 |

**选 A 的理由（主理人裁定）**：它**同时**满足三件事 —— 房间 12/12 件可见、外框裁切 4.7% 仍在
`CAM-03` 的 8% 内、桌子仍有 61.6% 宽（参考图约七成）⇒ **只需改 `CAM-04` 一条阈值**。
另一候选"占比 0.72 + 房间全可见"要把 `CAM-03` 也放宽（裁切 14%）⇒ **不批**：
"一条口径变更能解决的事，不要变成两条"。

### 7.1 相机依赖判据清单（已逐项核对）
**自动跟随（读同一真源常量，无需改）**：`CAM-01`、`CAM-02`、`CAM-03`、`CAM-05`、`T95 (f)`/`T95b`、
`T52`/`T53`、`MaterialVisualSetup` / `MaterialVisualValidator`。
**已同步刷新**：**`T95 (d)`**（三组桌面视口矩形）+ **`CAM-04`**（阈值）。

### 7.2 `CAM-04` 重定（**用户裁定 / 为什么 / 前后值 / 旧口径意图**）
- **谁裁定**：用户（2026-10-16，经主理人转达）——"要能看见球房环境"。
- **为什么**：旧口径要求"打球区横向占比 ≥0.85（灰盒参照 0.900）"，与"房间入画"**数学上互斥**
  （任何能把房间收进画面的取景都会把该占比压到 0.45~0.62）。
- **旧意图**：`CAM-04` 标题即"**防'缩成中间一张小桌'**"。
- **前后值**：`ClothWidthFractionMin` **0.85 → 0.50**。实测 A 档 **0.616** ⇒ **余量 0.116**（不贴实测值定）。
- **意图保留**：新下限 0.50 仍抓得住"缩成小桌"（占比掉到 0.50 以下意味着视锥水平半宽 > 3.05 m）。
- **`CAM-03` 外框裁切阈值 0.08 不动**（A 档实测 4.7% ≤ 8%，无需放宽）。

### 7.3 `T95 (d)` 期望值刷新（**已实施**）
`PracticeHudLayout.TableViewportRect` 投影**内库四角**，`T95 (d)` 把结果硬编码。
用**独立模型**（纯 Python 视锥投影，不读 Unity、不读被测实现）**先复现旧口径三组值（误差 4.8e-7）**
证明模型可信，再算新值。A 档新值（已写入 `PracticeHudTests`，并附"相机口径变更必须重新推导"注释）：

| 宽高比 | 新期望（xMin, xMax, yMin, yMax） |
|---|---|
| 16:9 | (0.191939, 0.808061, 0.183542, 0.461902) |
| 4:3 | (0.191939, 0.808061, 0.262656, 0.471427) |
| 21:9 | (0.268954, 0.731046, 0.183542, 0.461902) |

**验证**：新口径下 `tests` **222/222 全通过**（`T95 (d)` 由测试自身的计算复核，
`T95 (b)`「HUD 不压桌」也通过 —— 桌子下移到 `y≤0.46`，HUD 三块在顶带 ⇒ **意图未变、更宽松**）。

**实测吻合**：验收器实测 `CAM-04` 占比 **0.616**、`CAM-03` 裁切 **4.7%**，与离线模型**逐位一致** ⇒ 模型可信。

## 8. 跨会话重建：**语义等价，但字节不稳定**（实测量化）

同一代码连续两轮全量重建：

| 轮次 | Main.unity | PhysicsGreybox.unity |
|---|---|---|
| 第 2 轮 | 132464 B @14:40:49 | 132564 B @14:40:35 |
| 第 3 轮 | 132480 B @14:41:29 | 132512 B @14:41:15 |

**判定**：把 `fileID: <数字>` 归一化为 `fileID: #`、块头 `--- !u!<class> &<id>` 归一化后**再排序**，
两轮**逐行完全相同**（3478 行 × 2）⇒ 差异是**纯 fileID / 块顺序漂移，语义零漂移**。
登记：**场景重生成"语义等价但字节不稳定"**；`T128` 只证**同会话**幂等，**跨会话逐字节可复现性不成立**（已知属性）。

## 9. 证据与命令

- 生成入口：`Billiards.EditorTools.RoomEnvironmentSetup.BuildMaterialsFromCommandLine`
  （**含一次 `AssetDatabase.ImportAsset(EnvDir, ForceUpdate|ImportRecursive)`** —— 这批资产可能在后处理器
  编译生效前就被扫过一遍，不重导则 `useFileScale/isReadable/materialImportMode` 全不对且**不报错**）；
  `...RoomEnvironmentSetup.RenderRoomPreviewFromCommandLine`（**只写仓库外**的房间尺度/候选档渲染，
  复用场景主相机并**原样还原位姿** ⇒ 不新增第二台常驻相机）。
- 全链路：`unity_batch.sh rebuild-scene` / `rebuild-main-base` / `tests` / `validators`(+`git checkout -- Docs/Reports/`) /
  `build` / `smoke` / `smoke-aim`。
- 实测（**全部在最终 A 档口径下重跑**）：EditMode **222/222**（含刷新后的 `T95 (d)`）；
  `day1 26/26`、`day2 33/33`、`day3 26/26`、`d4 7/7`；
  `SCN-01/03/04/05/06/07`、`CAM-01..05`、`LGT-01`、`T103/T120/T127/T128/T131` 全 PASS；
  `CAM-04` 实测 **0.616**、`CAM-03` 裁切 **4.7%**（与离线模型逐位一致）；
  出包 0 错 0 警；冒烟两档见 §6。

## 10. 透视证据图机位口径修正（**已按主理人裁定实施**）

**问题（本卡上报、主理人裁定"要改"）**：`MaterialVisualValidator.RenderAll` 的第 2 张证据图（"透视"）
用**硬编码旧机位** `(0.15, 1.15, −2.15)` 注视 `(0, 0.02, 0.05)`、fov 38 —— 那是按**旧玩法取景
（相机在 −Z 侧朝 +Z 看）**调的。用户裁定放宽取景后主相机搬到 **+Z 侧**（`(0, 2.10, +2.80)`）⇒
本机位变成"**从后墙那一侧看桌子**"：它仍能判"是不是台球桌"（`RND-01` 照常 PASS），
但**它自称"透视证据"、实际已不代表玩家观感** —— 属"**证据与它的声称脱节**"。

**修法**：按 z=0 **镜像** ⇒ pos **`(0.15, 1.15, +2.15)`**、look **`(0, 0.02, −0.05)`**（fov 38 不变）。
**为什么是"镜像"而不是"改成主相机机位"**：本项的价值在**独立机位**（能看到主相机看不到的侧面/入射角，
这是它对"材质/法线"的判别力来源）；改成主相机机位就退化成 `day3-top-view.png` 的重复
—— 主理人明确**不批**这种顶替。镜像后仍是"低机位 / 离轴 / 贴近台面"的独立视角，
只是换到房间**开口那一侧**，顺带把后墙与摆件纳入背景。

**入库证据同批刷新**（本次是**取景口径变更**，属正当触发条件；四份 JSON + 四张 PNG）：

| 证据 | 刷新前（HEAD） | 刷新后 |
|---|---|---|
| `CAM-01` | pos=(0.00, 2.10, **−0.75**), fov=**52** | pos=(0.00, 2.10, **+2.80**), fov=**53** |
| `CAM-04` | 宽占比 **0.900** / 下限 **0.85** | 宽占比 **0.616** / 下限 **0.50** |
| `CAM-03` | 外框超量 3.9% | 外框超量 **4.7%**（仍 ≤8%） |
| `day3-perspective.png` | distinctColors **37051**（旧机位，从后墙侧看） | distinctColors **50948**（+Z 侧，含后墙/长凳/盆栽背景） |
| `day1/2/3/d4` | 26 / 33 / 26 / 7 全 PASS | **26 / 33 / 26 / 7 全 PASS** |
| `RND-01` / `NAB-01` | PASS / PASS | **PASS / PASS**（重新度量；A/B 差异 57.64%） |

⚠ **既有属性（不因本次刷新改变）**：`day3-top-view.png` **逐字节不可复现**（跨次渲染 sha 会变）
⇒ **不要**把它当稳定锚点；`RND-01` 只判"落盘 + 非退化"。

---

## 11. 复核方（`quality-lead`）的能力边界 —— **如实登记，不是缺陷**

> 以下两条由主理人转达、要求**原样记录**，用于界定"本轮证据/结论各自覆盖到哪里"。

### 11.1 环境 12 件的落位证据是"**离线 FBX 探针**"性质，**不在** EditMode 套件 / 四验收器内
本卡的落位与朝向结论来自两处：
1. **离线 FBX 二进制探针**（复用 `Docs/Tools/fbx_mesh_probe.py` 的解析器）—— 读源 FBX 的
   `GlobalSettings` / `Model` 节点变换 / 顶点包围盒，**不经过 Unity**；
2. **Unity 内实测**（`rebuild-main-base` 日志逐件打印包围盒，见 §2）。

⇒ **复核方只核了"存在性与自洽"**（资产在、`.meta` 在、场景里对象在、JSON 里的数自洽），
**未独立复算**摆位/朝向 —— 要独立复算**需要跑 `fbx_mesh_probe.py` 并复现摆位规则**（算法在
`RoomEnvironmentSetup`，非纯函数、依赖实测包围盒）。**登记为边界，不作为缺陷。**

### 11.2 跨会话"两轮字节对照"**未被复核方复现**
§8 那条"**语义等价、字节不稳定**"的结论，依据是**同一代码连续两轮全量重建**的字节对比
（`Main.unity` 132464 / 132480；`PhysicsGreybox.unity` 132564 / 132512）。
**复核方手上只有 `HEAD vs 工作区` 这一对**（工具 `Docs/Tools/scene_semantic_diff.py`：文档 96 → 127 行、
语义差异块 41 = **本卡新增的球房对象**）；要复现"两轮"必须**再生成一次场景** —— 那属它的**禁止动作**
⇒ **只能报"未复现"**。

> **原样记下这句**：那条"**语义等价、字节不稳定**"的结论，**目前是我们一侧的单次观测**；
> `T128` 仅证**同会话**幂等。⇒ 引用它时**必须**带"**未独立复现**"的限定，**不得**当作已复现的既有属性。

---

## 12. 双取景（首页观景 / 打球上帝视角）+ 点开始切换

**用户原话**：「这个视角开始游戏打球不太舒服，**点开始游戏就切换为上帝视角**」。

⇒ 本卡先前把**唯一**的取景从 `play` 放宽到 `home`（"要能看见球房环境"，见 §7）；
本条把房间可见性**交给首页取景承担**、**打球回到 `play`** ⇒ "一套取景"变成"**两套预设 + 运行期切换**"。
**两套都保留、各司其职**（不是"改错了又改回来"）。

### 12.1 真源位置（**必读：程序集方向决定了它不能放 `GreyboxSceneSetup`**）
`Billiards.Runtime.asmdef` **不引用** `Billiards.Editor`（方向不可逆），而运行期切换组件必须在 Runtime
（`HomeView` 在 Runtime）⇒ 数值**只能放 Runtime**。⇒ 新建
`Assets/_Project/Scripts/Runtime/Core/CameraPresets.cs` 作为**唯一真源**：

| 预设 | 位置 / 注视点 / 基准 fov | 用途 |
|---|---|---|
| `Play` | `(0, 2.10, −0.75)` / `(0, 0.02, 0.06)` / **52** | **打球（上帝视角）**；**场景序列化态** |
| `Home` | `(0, 2.10, +2.80)` / `(0, 0.05, −1.00)` / **53** | **首页观景**（房间 12/12 件可见） |

`GreyboxSceneSetup` 的既有公开名**反向派生**自真源 ⇒ 既有引用（`CAM-01..05` / `T95` / `MaterialVisualSetup`）
**一个都不用改**，且**数字仍只有一处**：
```csharp
public static readonly Vector3 CameraPosition = CameraPresets.PlayPosition;
public static readonly Vector3 CameraLookTarget = CameraPresets.PlayLookTarget;
public const float CameraBaseVerticalFovDeg = CameraPresets.PlayFovDeg;
```

### 12.2 运行期切换
`Assets/_Project/Scripts/Runtime/UI/PlayViewCamera.cs`（薄组件）每帧读 `HomeView.IsOpen`
（**同一程序集，读静态状态有先例**：`PointerAimController`/`PointerPowerController`/`AimAssistController`）
⇒ 经 `CameraPresets.Resolve(homeOpen)` 得预设 ⇒ 施加。
- **同一台相机换姿态**，**不加第二台**（`T127` 断言恰 1 台）。
- **单向依赖**：UI 报状态、相机自己跟随；**不让 `HomeView` 反向去拿相机**。
- 接线在 `GreyboxSceneSetup.BuildGameplayHosts`（**唯一实现点**，覆盖 Main 与灰盒两个场景；
  `BuildCamera` 只服务灰盒自建路径，放那里会漏掉 Main）。**挂在相机自身**，不新增宿主 GameObject（不动 `T127` 的必需宿主清单）。
- ⚠ **必须同步 `CameraViewportFitter.baseVerticalFovDeg`**：适配器只在 `OnEnable` 与**宽高比变化**时按
  **它自己的基准**覆写相机 fov ⇒ 只改相机 fov 不改基准，**窗口比例一变 fov 就被弹回**。
  该协同收进唯一实现点 `CameraPresets.ApplyTo(camera, preset)`（运行期组件与构建期渲染工具都调它）。

### 12.3 `CAM-04` 口径漂移（**如实记录：一跳放宽、一跳回推**）
| 阶段 | 触发 | 打球区横向占比 | `ClothWidthFractionMin` |
|---|---|---|---|
| 起点 | — | 0.900 | **0.85**（余量 0.05） |
| 本卡先前 | 用户："要能看见球房环境" ⇒ 唯一取景放宽 | 0.616 | **0.50**（理由：与"占比 ≥0.85"数学互斥） |
| **本次** | 用户："点开始就切上帝视角" ⇒ 房间交给首页、打球回 `play` | **0.900** | **0.85**（按 play 重新推导，余量 0.05） |

- **谁裁定**：用户（2026-10-16，两条反馈先后到达，经主理人转达）。
- **旧意图**：`CAM-04` 标题即"**防'缩成中间一张小桌'**"。
- ⚠ **本次 0.50 → 0.85 是"回到原意图"，不是"把数字改到过"**：
  完整链条 =「先因"要看见房间"把唯一取景放宽 ⇒ `0.85 → 0.50`」→
  「用户裁定"打球改上帝视角" ⇒ **房间可见性移交首页取景** ⇒ `CAM-04` 回到 **play** 口径重推 ⇒ `0.50 → 0.85`」。
  两条改动都只让"桌子该占多大"随**打球口径**走，**判据意图（防缩成小桌）始终未变**。
- ⚠ **余量 `0.05` 是"历史口径的余量"，不是贴着实测值取的**：
  它来自 `CAM-04` 最初的定法（参照实测 `0.900` ⇒ 取 `0.900 − 0.05 = 0.85`），本次**原样沿用**，
  而**不是**按今天的实测现算一个"刚好能过"的数。
  （反面教训：本项目在 `T133` 上吃过"**8 压 8**、Mono/CoreCLR 差 1"的亏 ⇒ 阈值不许贴着实测定。）
- ⚠ 本阈值**只随打球口径变更**而变，**不得**为让某次取景过而单独下调；
  **本条不测首页取景** —— 首页那套由 `T134` 与切换判据守，**不靠放宽本阈值来容纳**。
- **`CAM-01..05` 全部按 `play` 度量**（场景序列化态 = play）⇒ **`CAM-05`（Main 与灰盒逐项一致）继续成立**。

### 12.4 新增判据（`PlayViewCameraTests.cs`）
- **`T134`**：① **两套预设必须"在几何上"真的不同**（`CameraPresets.PresetsDiffer()`）—— 防"两套写一样"的假实现：
  一旦有人把 `Home` 写成 `Play`，**首页"看得见房间"静默失效，而 `CAM-*`/`T95`/`SCN-*` 没有一条会红**（它们只测序列化态）。
  **判定口径 = 几何**：位置距离与注视点距离**都必须 > `MinPresetSeparationMetres = 0.25 m`**（实测 3.550 m / 1.061 m，余量 14× / 4.2×）。
  ⚠ **fov 差不再能顶替它**（复核方 CONCERN 后加严）：注入 **(i)-1**（把 `HomePosition`/`HomeLookTarget` 改成与 `Play` 相同、只留 `HomeFovDeg=53`）
  实测**旧口径下 ① 仍绿**，只是**被 ② 兜住** ⇒ ① 单独看弱于其标题。现实现**移除 fov 子句**：
  **改坏位置/注视点，① 必须自己红**，不再依赖 ② 兜底。
  ② **场景序列化态必须是 `Play`**（比对**运行期真源**而非 Editor 侧别名 ⇒ **非恒真**：把别名改指 `Home` 即红），
  并反向断言**不是 `Home`**（防"两个别名一起改"蒙混）。
- **`T135`**：切换规则的**纯函数** `Resolve(homeOpen)`（`true ⇒ Home`、`false ⇒ Play`）+ 位姿取值 + `ApplyPreset` 施加动作逐项断言。
  （走纯函数而不是改 `HomeView.IsOpen`：它是 `{ get; private set; }`，测试改不动 —— 这正是把它抽成纯函数的原因。）
- **可被打死**：`HomePosition` 改成 `PlayPosition` ⇒ `T134①` 红；`GreyboxSceneSetup.CameraPosition` 改指 `Home` ⇒ `T134②` 红；
  `Resolve` 两分支写反 ⇒ `T135` 红；`ApplyPreset` 写成 no-op ⇒ `T135③` 红。

### 12.5 入库证据同批刷新（**本次触发条件 = 打球口径变更**）
| 证据 | 刷新前（口径 = home） | 刷新后（口径 = play） |
|---|---|---|
| `CAM-01` | pos=(0.00, 2.10, **+2.80**), fov=**53** | pos=(0.00, 2.10, **−0.75**), fov=**52** |
| `CAM-03` | 外框超量 4.7% | 外框超量 **3.9%** |
| `CAM-04` | 宽占比 0.616 / 下限 0.50 | 宽占比 **0.900** / 下限 **0.85** |
| `day1/2/3/d4` | 26 / 33 / 26 / 7 全 PASS | **26 / 33 / 26 / 7 全 PASS** |
| `T95 (d)` | home 口径三组 | **play 口径三组**（独立模型**完整复现**原值，误差 ≈5e-7） |

### 12.6 证据图取景统一（**已按主理人裁定实施**）

**问题（本卡上报、主理人裁定"统一，算进本卡"）**：`MaterialVisualValidator.RenderToPng` 此前
**不经过** `CameraViewportFitter` ⇒ 证据图按**序列化 fov** 在 16:10 下渲染，而判据 `CAM-02/03/04`
是**先设 aspect、再经 fitter 解析 fov** 的 ⇒ **图里看到的取景与判据量的取景不是同一个**
（"证据与它的声称脱节"，正是本卡在清的那类问题）。

**修的是什么**：让证据图渲染路径与 `CAM-02/04` **同一套**（先设宽高比 ⇒ 经 fitter 解析 fov）；
三处取景（俯视 / 透视 / 法线 A-B）的 fov 一律**改适配器基准**而不是直接写相机，
渲染前再 `fitter.Apply()`；渲染后还原基准。

**前后数值（同一 `play` 机位、1440×900）**：

| 量 | 修前 | 修后 |
|---|---|---|
| `day3-top-view.png` 台呢横宽（绿像素实测） | **0.708** | **0.956** |
| `day3-top-view.png` distinctColors | 12406 | **39451** |
| `day3-perspective.png` distinctColors | 50028 | **55248** |
| `ab-asis` / `ab-flipgreen` distinctColors | 42081 / 38640 | **43580 / 39820** |
| A/B 差异像素占比 | 57.64% | **53.85%** |
| `CAM-01 / CAM-03 / CAM-04` | — | **完全不变**（本修正只动渲染路径，不动判据度量） |
| `day1/2/3/d4` | 26 / 33 / 26 / 7 | **26 / 33 / 26 / 7 全 PASS** |

**为什么现在才修**：本卡把取景口径改成"**两套预设 + 运行期切换**"后才暴露
（运行期 `PlayViewCamera` 恰恰**必须**与 fitter 协同，见 §12.2）——在此之前
"序列化 fov"与"fitter 解析值"差得不明显，掩盖了这条。

**`RND-01`/`NAB-01` 语义不变**（判"非退化 / A≠B"，与取景无关）⇒ 重新度量后仍 PASS。
⚠ 刷新后**四张图不当稳定锚点**（`day3-top-view.png` 早已登记为**逐字节不可复现**）。

---

## 12.7 两条登记（**已知重复** + **一条方法论踩坑**）

### 12.7.1 已知重复：运行期与两个渲染工具各有一份"经 fitter 改 fov"的实现
| 位置 | 角色 |
|---|---|
| `CameraPresets.ApplyTo`（Runtime） | 运行期施加预设（被 `PlayViewCamera` 调用） |
| `MaterialVisualValidator.SetBaseFov` / `RestoreBaseFov`（Editor） | 证据图渲染路径 |
| `RoomEnvironmentSetup.SetBaseFov`（Editor） | 房间预览渲染工具 |

**三处逻辑等价，且同遵循同一不变量**：**先改 `fitter.baseVerticalFovDeg`，再 `Apply()`**
（只改相机 fov、不改适配器基准 ⇒ 窗口一变会被旧基准覆写回去）。
**不抽共享函数是主理人的裁定**：那要同时动 Runtime + Editor 两条链，且必须**重跑 `validators`
证明渲染输出逐字节未变**（等于再刷一次入库证据）—— 消一处重复的收益不值这个代价。
⇒ ⚠ **将来若再改这条链路，必须三处同改**。

### 12.7.2 方法论踩坑：**Unity 场景按 guid 引用脚本，类名不出现在 YAML**
`Main.unity` / `PhysicsGreybox.unity` 里的 `MonoBehaviour` 块只写
`m_Script: {fileID: 11500000, guid: <脚本的 guid>, type: 3}` —— **类名（如 `PlayViewCamera`）根本不出现在场景文件里**。
⇒ **按类名 `grep` 场景会得 0**，看起来像"组件没被接上"（复核方第一遍就据此误判过一次）。
**正确核法**：先读 `<脚本>.cs.meta` 里的 `guid:`，再拿该 guid 去场景里 `grep`（本卡实测：两场景**各命中 1 次** ⇒ 组件确实接上了）。
📌 **同时**：脚本自身的 `.meta`（`guid` 的载体）**必须与脚本一起入库**，否则 Unity 下次会重新生成 guid
⇒ **场景里已接好的引用全部断掉**（静默变空引用，比编译错误更难查）。

---

## 13. 用户实机反馈两条：转场丝滑 + 开球距离对齐现实

### 13.1 转场（`T136`）

**用户原话**：「开始游戏的视角转场要"丝滑"」——此前 `PlayViewCamera` 是**硬切**（一帧跳变）。

**实现**：
- 新增 `Runtime/Core/CameraTransition.cs`（**全纯函数**）：`Weight(t)` = smoothstep `t²(3−2t)`、`Interpolate(起止位姿, t)`、`ShouldAnimate(hasApplied, from, to)`、`SampleWeights(n)`。
- `PlayViewCamera` 改为状态机：**首帧硬切** / **预设变化 ⇒ 起一段转场**（位置 + 注视点 + fov **同步**插值）/ **`t ≥ 1` 时改走 `ApplyPreset(want)`** 落终态。
- `CameraPresets` 抽出 **`ApplyPose(camera, pos, look, fov)`** 作为"施加位姿"的**唯一实现点**（`ApplyTo` = `GetPose` + `ApplyPose`；转场每帧也走它）⇒ 转场终态与稳态**同一行代码** ⇒ **逐位一致**。
- 时长：**默认 0.45 s**（备选 0.55 s，字段 `transitionSeconds`）。

**关键性质（`T136` 断言）**：
- **端点精确**：`t ≤ 0` 原样返回起点、**`t ≥ 1` 原样返回终点**（不走插值）⇒ 终态**逐位等于真源预设**（`CAM-01..05` 判的是**稳态**，不许"插到 99% 就停"）。
- **缓动**：smoothstep 在 `[0,1]` 单调 ∧ **两端速度为 0**（`w'(0)=w'(1)=0`）⇒ 起步/收尾不突兀；**线性缓动在"两端速度"断言上会红**。
- **不回头、不越界**：中途位置距目标**单调不增**，且不越出两端点的包围盒。

**两档时长关键帧（Home→Play）**：

| 时长 | 项 | t=0 | t=0.25 | t=0.5 | t=0.75 | t=1 |
|---|---|---|---|---|---|---|
| **0.45 s**（27 帧@60） | 权重 w | 0 | 0.1562 | 0.5000 | 0.8438 | 1 |
| | 位置 z | 2.8000 | 2.2453 | 1.0250 | −0.1953 | **−0.7500** |
| | fov | 53.000 | 52.844 | 52.500 | 52.156 | **52.000** |
| **0.55 s**（33 帧@60） | 同时刻（0 / 0.1375 / 0.275 / 0.4125 / 0.55 s）**数值同上** | | | | | |

（位置 x 恒 0、y 恒 2.10；朝向由 `LookRotation(插值后的 look − 插值后的 pos)` 求得。）

**⚠ 冒烟不做过渡（论证）**：`ShouldAnimate` 在**首帧**（`hasApplied=false`）与**状态未变**（`from==to`）两种情形都返回 `false`。
冒烟全程**无人操作**且首页被抑制（`homeStartup=suppressed`）⇒ `HomeView.IsOpen` 恒 `false` ⇒ 解出预设恒为 **`play`**
⇒ `from == to` ⇒ **一次过渡都不会起**（那两档测的是"无人操作 600 帧的帧成本"，起始帧被过渡污染就不可比）。
**⚠ 验收器不受影响**：`CAM-01..05` 在**编辑态、生成场景后直接量**（不进 play 模式）⇒ 组件不运行 ⇒ 量到的始终是**序列化稳态**。

**测试接缝**：`PlayViewCamera.homeOpenSource`（**有文档**）：`null` ⇒ 读真实 `HomeView.IsOpen`（产品路径）；
非 `null` ⇒ 用它。存在理由：`HomeView.IsOpen` 是 `{ get; private set; }`，**判据改不动** ⇒ 没有接缝就只能测纯函数、测不到"转场真的会起、终态精确"。

### 13.2 开球距离对齐现实（白球 0.808 m → **1.27 m**）

**用户原话**：「开球时白球离球堆再远一点，接近现实开球距离」。

**真源（一处定义、多处引用；不留旧契约值）**：`PhysicsTestDrive`
- `RackApexX = −0.30f`（球堆顶点，抽成常量）
- `BreakClearanceMetres = 1.27f`（白球到球堆顶点的间距；现实 ≈ 半台长）
- **`CueBallHomePosition(footSpot)`** = `(RackApexX + BreakClearanceMetres, footSpot.y)`

**五处引用全部改用同一真源**：`BuildRack`（经 `RackPositions[0]`）/ `BuildSingleBall` / **落袋重摆**（`Launch()`）/ **`ShotPredictor` 的幽灵白球**（×2）/ **白球可视物**（`GreyboxSceneSetup` ×2）。
**`RackPositions[0]` 也改为返回该地标**（不留"旧契约值"——否则 `T38` 会变成"守着没人再用的摆设值"的守卫）。

**档位是实测选的（`RackPositions(footSpot,15)` + 资产 `maxLaunchSpeed`，位移 > 0.30 m 计"打散"）**：

| 白球 x | 距顶点 | 打散数 | 静止步数 | contacts(bb) | guard | 结论 |
|---|---|---|---|---|---|---|
| 0.508（旧） | 0.808 m | 8/15 | 493 | 49(37) | 0 | 基准 |
| 0.750 | 1.050 m | 8/15 | 496 | 50(37) | 0 | 合规备选 |
| 0.850 | 1.150 m | **6/15** | 493 | 42(32) | **1** | ❌ 余量 0 且触发 TOI 守卫（会打红 `T111`） |
| **0.970（选定）** | **1.270 m** | **8/15** | **499** | **50(37)** | **0** | ✅ 现实半台距离，余量 2 |

**`MinScattered` 维持 6（不上调到 8）**——不贴实测取、留余量。**初速 8 m/s 不动**（上一卡用户拍板）。

### 13.3 教训登记（**反直觉，值得记一笔**）

> **打散数不随白球距离单调**：0.85 档（更远）只有 **6/15**，而 0.97 档（更远）却是 **8/15**；
> 且 **0.85 档触发 TOI 迭代守卫 1 次**。

⇒ 结论两条：
1. **"越远越差 / 越近越好"这类直觉在本系统里不成立**（球堆是强非线性/混沌系统）⇒ **档位只能看实测**。
2. **改白球位这类几何量会牵动 TOI 守卫计数** ⇒ 每次都要**连带看 `T111`/`T37` 的 `guard`**（本卡正是靠这一条否掉了 0.85 档）。
3. **⚠ 核对"位置/引用"前先判定它落在「序列化态」还是「运行期态」**：球/杆这类可视物的位置**每帧由物理同步回写**（`SyncBallVisual(... PhysicsBallToUnity(balls[i].position))`）⇒ 用 **YAML 搜坐标必然 0 命中**；同理场景对脚本是**按 guid** 引用 ⇒ 用**类名搜也必然 0 命中**。今日两次假阴性均属此类。

### 13.4 判据重钉（**授权范围内，逐条写依据**）

| 判据 | 改的是什么 | 为什么 | 前后值 |
|---|---|---|---|
| **`T38`** | `[0]` 期望由 `footSpot` → 开球位（**独立字面量** `-0.30f + 1.27f`，不调产品函数 ⇒ 非恒真） | 用户裁定开球距离改 1.27 m ⇒ 按新口径**重新钉**（不是绕过） | `(0.508,0)` → `(0.97,0)` |
| **`T70`** | 重摆期望由 `footSpot` → `CueBallHomePosition`（**仍容差 1e-6**） | "放回**开球区**"的语义不变，此前那个"开球区"恰好 = `footSpot` | `(0.508,0)` → `(0.97,0)` |
| **`T66`** | 前提参照点 `footSpot` → `CueBallHomePosition` | 否则该前提会**恒真**（起点本身已离 `footSpot` 0.46 m），失去判别力 | 参照点位移 |
| **`T74`** | 预测幽灵白球起点 → `CueBallHomePosition`（仍 1e-6） | 与 `Launch()` 重摆点、`T70` **同一个值**（预测与出杆不许分叉） | `(0.51,0)` → `(0.97,0)` |
| **`T72b`** | T37 基线 `bb 37→35`、`total 49→48`、**16 颗终态全部重钉** | 几何输入改变 ⇒ 首碰前行程变长 ⇒ 接触数略降、终态微移（最大约 1.2 cm）；`captured/proximity/jaw/guard/energy/maxIter` **均未变** | 见文件内第五轮登记 |
| **`T78`** | 夹具**显式固定**白球起点为 `footSpot` | 该场景的瞄准角/目标位是**在旧起点下实测选定**的；本判据只测"银行球预测与实跑一致"，不该被"默认起点"牵动（默认起点由 `T38`/`T70`/`T133` 覆盖） | 夹具固定（非放宽） |
| **`T133`/`T37`/`T111`** | **仅登记**实测基线（打散 8/15、settle 499、contacts 50） | 断言口径**未变**（`MinScattered` 仍 6、settle 上限仍 1800、`guard==0`） | 见各自注释 |

