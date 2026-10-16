# 35-A 美术接入与 UI 重做（球/杆真模型 + 按 04_UI 设计稿重做界面）

> 来源：用户实机反馈三条 —— ①**「台球不是 15 个球加 1 个白球吗？你还没给我弄全」** ②**「美术资源你也没给我用上」**
> ③**「这个 UI 太丑了，根据 `04_UI` 里面的 UI 做」**，并要求**把美术包的 txt 全部认真读一遍**。
> 验收：①场上**恒为 16 颗**（白球 + 1–15 号，各带正确花色/号数），②球/杆/桌**用美术包模型**而非基元，
> ③对局界面（HUD + 状态 + 结算）**与 `04_UI/Reference_Screens` 的设计稿一致**（配色/字体/控件形态/信息层次）。

---

## 1. 美术包通读结论（先把事实摆清）

**读过的文档**（28 份 txt/md 全读过）：`00_交付总览/README_先读.md`、`打包要求_用户参考.txt`、`试玩版备注.md`、
`04_UI/UI全流程设计说明_参考.txt`、`Web_Implementation_Reference/只读摘录说明.md`、`05_VFX/音画设计与资源说明_参考.txt`、
`Dynamic_Reference/样片说明.md`、`03_Models_模型/Balls|Table/Editable/README.md`、`09_Reports/*`（Asset_Audit / Missing_Assets /
Technical_Issues / Delivery_Checklist / TA_Handoff / Blender 审计 / 历史证据 3 份）、两份 OFL 与 planck 许可（模板件）。

| 事实 | 对本案的意义 |
| --- | --- |
| **完整 16 球**已交付（`Ball_00`–`Ball_15`，半径 **0.028575 m** = 本工程球半径，48×24 平滑球、1,106 顶点/2,208 三角面，各自 1024×512 独立贴图，**无 atlas**） | 用户①「没弄全」= **本工程侧的实现问题**，不是资源缺失 |
| 球色与号数**已给定**（0 米白 `#f5f3e7`；1/9 黄 `#e4b52a`；2/10 蓝 `#256dc3`；3/11 红 `#b93231`；4/12 紫 `#77439c`；5/13 橙 `#e87926`；6/14 绿 `#1f9361`；7/15 深红棕 `#853a35`；8 黑 `#111b20`；1–15 有两个相对白底号码区） | 需按号取材质，**不得**全员套白球材质 |
| 球初始朝向 = 面向原型相机方向 `(3.55,2.65,3.55)`（**非 billboard**，跟随球自转） | 本工程相机不同 ⇒ 需按**本工程相机**换算初始朝向，否则看不到号 |
| **杆**：`PoolCue_TipOrigin.fbx`，**杆尖 = 局部原点、杆身 +Z、长 1.4 m** | 与 `CueStickController` 的既有约定**逐字一致** ⇒ 可直接替换基元圆柱，无需改控制器坐标系 |
| 桌：`PoolTable_Normalized.fbx`（19,954 三角面、6 袋、木框、**不透明桌体 + AlphaMask 袋网**、台呢 y=0、内库 2.54×1.270108866、桌腿底 y=−0.8635） | 已在用 ✓；袋网需 `AlphaClip`（cutoff 0.5）✓ Day 3 已处理 |
| **1.2 显示缩放只作用于整个可玩区域**，模型与二维碰撞参考保持规范米制（**不得同时放大模型+父级**） | 与既有设计一致 ✓ |
| **UI 全流程设计说明**：复古运动俱乐部；主色炭黑 `#102025`、桌布绿 `#174e46`、黄铜 `#d4b46c`、暖白；**酒红只用于犯规/失败、亮青只用于指定球袋**；标题 Barlow Condensed、正文 Inter；**字距 0、控件边角 1–3 px、禁 SaaS 式大圆角卡片**；徽章 `club-crest-v1.png` | UI 重做的**唯一设计依据** |
| **状态条契约** `data-state`：`aim / ai-turn / moving / ball-in-hand / call-pocket / foul / paused / result`；结算 `data-outcome: win|lose` + `data-stage: match|rack|practice` | 本工程对应 `ShotPhase` + 犯规/自由球/指定袋状态，需**一一映射** |
| **音画设计**：VFX 6 类 —— 击杆 0.12 s、碰球 0.11 s、撞库 0.15 s、**落袋 0.29 s（黑八延长 0.45 s）**、**犯规：白球周围 0.38 s 酒红短环 + 状态条 0.8 s 边线脉冲**、**结算：最后黑八袋位细环** | 本工程只做了前 3 + 落袋 ⇒ **犯规/结算两类缺失**（用户此前抱怨"反馈不够强"与此一致） |
| `Missing_Assets`：**P0=0、P1=3、P2=1**，四项全是 **E（无法确认）＝商业权属待人工确认**，不是资源缺件 | 权属确认属**用户/项目方**动作（见 §6 登记）|
| 字体：Barlow Condensed + Inter 的 **WOFF2 + OFL**，**仅 Latin 子集（不覆盖中文）** | 界面文案**保持英文**（现 HUD 即英文 ✓）；若改中文需另行确认字体 |

---

## 2. 缺口与修法

### 2.1 球：只有 4 颗且全是基元 + 同一个白球材质（**用户①的根因**）

`GreyboxSceneSetup.BuildRackVisuals` 现在调 `PhysicsTestDrive.RackPositions(config.footSpot, **3**)`（Day 7 球-球演示的遗留）
⇒ 场上**只有白球 + 3 颗**，且 `BuildBallObject` 对**每一颗**都套 `Ball_00.mat`（白球材质）⇒ 四颗全白。
而驱动器默认 `loadRackForBallBallDemo = true` ⇒ 内核里是 **16 颗**且在跑，**12 颗没有可视物**（幽灵球）。

**修法**：可视物**由内核球表驱动**（`BallCount` 与球号一一对应），逐球取 `Materials/Balls/Ball_NN.mat`；
球模型用 `Art/Models/Balls/Ball_NN.fbx`（**替换 `CreatePrimitive(Sphere)`**）；**删除基元自带的碰撞体**（自研物理铁律）；
按本工程相机换算初始朝向使号面可见。

### 2.2 杆：基元圆柱（`CueStickController.EnsureStick` 运行时建）

**修法**：场景里建 `PoolCue_TipOrigin.fbx` 实例（杆尖原点 + 局部 +Z 长 1.4 m，与控制器约定一致），控制器优先用该实例、
**保留基元兜底**（未接线时不至于没有杆）。

### 2.3 UI：默认 uGUI 外观（用户③）

以原型 CSS 的**精确令牌**为准（见 §3），把「练习 HUD + 状态条 + 结算卡」重做到设计稿形态；
本卡范围**不含**载入/首页/赛前/设置/暂停/重摆确认（那属 35-B 的完整流程），但**状态语义**（犯规/自由球/指定袋/运动/结果）**全部实现**。

### 2.4 VFX：补两类（音画设计明确要求、本工程缺失）

- **犯规环**：白球周围 **0.38 s 酒红短环** + 状态条 **0.8 s 边线脉冲**（酒红**只**用于犯规/失败 ✓ 设计约定）；
- **结算环**：最后黑八袋位**细环**；并把**落袋**在黑八时从 0.29 s 延长到 **0.45 s**。

---

## 3. UI 设计令牌（**代码里的单一真源**，取自原型 `style.css`）

| 令牌 | 值 |
| --- | --- |
| 墨底 / 布绿 / 黄铜 / 静音灰 | `#102025` / `#174e46` / `#d4b46c` / `#a9b9b4` |
| 面板底（三级不透明） | `#102025e8`(91%) / `#102025ed`(93%) / `#102025f2`(95%) |
| 细边线 | `1px` `#d4b46c45`（半透明黄铜）；强调 `#d4b46c60` |
| 圆角 | 面板 **2–3 px**、按钮 **1–2 px**、**禁大圆角** |
| 面板内距 | 状态条 `8×15`；shotbar `16×22`（高 **77**） |
| 文字 | 标题族 **Barlow Condensed**（wordmark / 大数字 / 结算标题）；正文族 **Inter**；字距 **0** |
| 字号阶梯 | 大数字 18、品牌 23/29、标题 13、正文 12、次 10、微 8–9 |
| 主按钮（Shoot / Play again） | **112×43**、底 `#d4b46c`、字 `#102025`、边 `1px #ebd397`、**700/12px**、内下阴影 |
| 力度条 | 轨道高 **5**、填充黄铜、底 `#3f615a`、滑块 **14×14** `#e9d096` + 3px 亮边；数值 `#efdcaa` |
| 图标按钮 | **38×38**、底 `#102025e8`、边 `1px #d4b46c45`、字/图标 `#e9e6d9` |
| 模式切换（Practice / AI Match） | 外框 `radius 3` + `padding 3`，内钮 `radius 1`、`padding 9×24`、`12px`；选中 = 黄铜底 + 深字 |
| 头像 / 迷你球 | 46 圆（`#14312d` 底，黄铜细边）／15 圆（带内阴影与白边） |
| 结算卡 | 居中面板 + 遮罩（约 65% 黑）+ 顶部圆形奖杯 + 小 chip + **大标题（Barlow 粗）** + 副标题(muted) + 细分隔线 + **三列统计**（大数字 + 8px 标签）+ 主按钮（全宽黄铜）+ 次按钮（描边） |

**图标**：设计稿用线性小图标（图形各异）。uGUI 无矢量渲染 ⇒ 由**编辑器期程序化生成**一组 64×64 线性图标
（准星 / 手掌 / 旗帜 / 警告 / 机器人 / 齿轮 / 房子 / 右箭头 / 奖杯），落成 PNG 资产（确定性、可判据）。

---

## 4. 判据（新增）

| 判据 | 位置 | 内容 |
| --- | --- | --- |
| **`T122`** 球可视物与内核一一对应 | `GreyboxSceneSetup` 相关测试（新文件） | 场内可视球数 **== 内核球数 == 16**；第 0 颗 = 白球材质 `Ball_00`、第 i 颗 = `Ball_i` **逐号对应**；不得出现"同材质套全员"；**不得**残留基元 Sphere（球体网格名/尺寸可判）；无基类碰撞体 |
| **`T123`** 杆用真模型且约定不变 | 同上 | 场景里的杆实例来自 `PoolCue_TipOrigin`（按 name/网格判）；**杆尖 = 局部原点、+Z、长 1.4 m** 三条尺寸断言（与控制器约定一致）；未接线时有基元兜底 |
| **`T124`** UI 设计令牌单一真源 | `PracticeHudLayout` 相关测试 | 所有令牌值来自**唯一常量类**（`ClubUiTokens`）：逐条断言 `#102025/#174e46/#d4b46c/#a9b9b4`、边线 `1px`、圆角 ∈[1,3]、正文/标题字族；**注入**：把圆角改成 8 ⇒ 红 |
| **`T125`** 状态语义与设计契约一一映射 | 同上 | `data-state` 八态 ↔ 本工程状态：`aim/ai-turn/moving/ball-in-hand/call-pocket/foul/paused/result` 每态有确定的文案与配色（酒红**只**用于 foul/lose、亮青**只**用于 call-pocket）；结算 `data-outcome/data-stage` 映射齐全 |
| **`T126`** 犯规/结算 VFX 存在且时长合设计 | `PoolVfxTests` | 犯规环 **0.38 s** 酒红、落袋黑八延长 **0.45 s**（其余 0.29 s）、结算环存在；注入：把犯规色改成非酒红 ⇒ 红 |

---

## 5. 交付顺序（本轮内）

1. **球 + 杆真模型接入**（§2.1/§2.2）→ 重建场景 → `T122`/`T123`；
2. **UI 令牌 + HUD/状态/结算重做**（§3）→ `T124`/`T125`；
3. **VFX 补两类**（§2.4）→ `T126`；
4. 全量 + 四验收器 + 重新出包 + 冒烟（沿用 Day 16 的流水线）→ 台账/提交。

## 6. 盲带与登记（如实）

1. **商业权属待人工确认**（美术包 `Missing_Assets` 的 ART-E01/E02/E03/E04 全是 E 类）：下载模型/生成徽章/原创音频的
   商业使用授权**需要你或项目方**出确认记录 —— 我**不**声称已获授权，也不代为声明。
   ⇒ ✅ **已结案（用户确认）**：用户原话「**美术包里面全部都是我可以使用的**」⇒ 美术包
   `F:\工作文件\idea35_台球` 的**全部资产由用户确认可用**，ART-E01–E04 的权属风险**结案、不再列为待办**；
   本行保留为"当时未确认"的实录，**不得**据此再声称"未获授权"。
2. 界面文案**保持英文**（字体仅 Latin 子集，美术包明确"不覆盖中文，改中文需团队确认字体"）。
3. 本卡**不做**载入/首页/赛前/设置/暂停/重摆确认（35-B 的完整流程）；不做布景 13 类房间模型（本卡只到桌+地面+灯）。
4. 图标为**程序化线性近似**（不是 Lucide 逐字还原，Lucide 的 SVG 与本工程 uGUI 无矢量渲染路径，已登记）。
5. 球号朝向按**本工程相机**换算，与原型相机不同 ⇒ 观感可能略有差异；滚动的球会带着号一起转（设计如此，非 billboard）。

---

## 7. 实施结果与偏离（**as-built**，逐条可核）

### 7.1 新增/改写的文件

| 文件 | 作用 |
| --- | --- |
| `Runtime/UI/ClubUiTokens.cs`（新） | **设计令牌单一真源**：色值 / 面板透明度 / 细边线族 / 八态配色 / 圆角 / 尺寸 / 字号阶梯 / 语义色族登记（`ClaretFamily` / `CyanFamily`）+ 具名清册 `ColorLedger` |
| `Runtime/UI/ClubUiSprites.cs`（新） | **界面精灵清单**：13 语义图标 + 5 几何形状（圆 / 环 / 1–3 px 九宫格圆角），序号 ↔ 文件名 ↔ 精灵名的唯一映射 + 槽位校验 |
| `Runtime/UI/ClubBallChips.cs`（新） | **16 个球号贴片清单** + 球色表 + 判据采样点（`TopBandSampleUv`） |
| `Runtime/UI/PracticeHudState.cs`（改） | 新增 `HudVisualState` **八态**（= 设计契约 `data-state`）、每态的标题/说明/边线色/指示色/图标映射、`ResolveVisualState` 优先级单点、`UnreachableStates` 登记、`FoulPulseBlend`（0.8 s 一次性 ease-out） |
| `Runtime/UI/PracticeHudView.cs`（重写） | 按设计稿建 品牌 / 记分牌（头像 + 大数字 + 15 球行）/ 状态条 / 力度块（含滑块）/ 模式切换 / 两个真开关 |
| `Runtime/UI/PracticeResultView.cs`（重写） | 设计稿的模态结算卡：遮罩 + 顶部 3 px 色条 + 圆形奖杯环 + chip + 大标题 + 副标题 + 三列统计 + 主次两键；**胜 = 黄铜 / 负 = 酒红** |
| `Runtime/UI/PracticeResultLayout.cs`（改） | 新增卡内各元素的**比例常量**（从设计稿量得，与既有两键同口径） |
| `Runtime/UI/PracticeHudLayout.cs`（改） | `ShootButtonWidth/Height` 改为**转发设计令牌**（原 150 是估读值，设计稿是 112 ⇒ 取消第二份数） |
| `Runtime/Physics/PoolVfxView.cs`（改） | **补两类反馈**：犯规环（0.38 s 酒红）、结算环（黑八 0.45 s 细环）；落袋环 0.30 → **0.29 s**；槽容量 16 → **48**（设计稿值，消除溢出丢弃） |
| `Runtime/Physics/PoolVfxView.cs` | 犯规色**直接引用** `ClubUiTokens.FoulEdge`（同一个设计色不许有第二份） |
| `Editor/Visual/UiSpriteImportPostprocessor.cs`（新） | 精灵导入设置即代码（`Sprite` / 关 mip / 关压缩 / 九宫格边框 / 贴片可读） |
| `Editor/Visual/GreyboxSceneSetup.cs`（改） | 新增 `LoadUiSpritesOrThrow` / `LoadBallChipsOrThrow`（**任一格缺失即构建期抛异常**），并接线 `uiSprites` / `ballChips` / `vfxView` / `poolAudio` |
| `Tests/EditMode/ClubUiTests.cs`（新） | `T124` / `T125` / `T126` |

### 7.2 生成器（可复现）

| 脚本 | 产物 |
| --- | --- |
| `Docs/Tools/make_ui_sprites.py` | `Assets/_Project/UI/Icons/*.png`（18 个：13 图标 + `ui-disc` / `ui-ring` / `ui-panel-1..3`），纯白 + alpha，供 `Image.color` 上色 |
| `Docs/Tools/make_ball_chips.py` | `Assets/_Project/UI/Balls/ball-00..15.png`（64×64，球色 + 号码，花色球上下 22% 暖白带；字体用工程自带的 Barlow Condensed Bold） |

### 7.3 与规格的**偏离**（如实）

1. **`IconLocal` 用比例常量而非像素**：结算卡的奖杯环直径由 `IconLocal.width × CardWidthPx` 算出（= 60 设计 px），
   不另写一个 60 的像素常量 —— 避免"卡尺寸变了、徽章没跟着变"。
2. **状态条是两行（标题 + 说明）**，设计稿是一行。理由：本工程只有顶带 0.202 视口高可用，
   两行在同等高度里承载了"当前状态 + 该怎么应对"两条信息；**没有**删掉任何设计契约里的状态。
3. **出杆栏放在右侧分区而非右上竖向**：设计稿的竖向出杆栏最小高 356 px，而本工程顶带只有 218 px
   （相机取景与 HUD 分区的既有硬约束，见 `PracticeHudLayout` 的注释）⇒ 采用设计稿在
   窄屏下的**同款横排形态**（`@media (max-width: 899px)` 分支），元素与层级不变。
4. **模式切换的 `AI Match` 是禁用态**（0.38 不透明度、不挂按钮）：35-A 无 AI 对手，做可点的假入口等于欺骗。
5. **主按钮只有 `Play again` 带箭头图标**；`Export data` 的图标位留空（设计稿的次按钮本就无图标）。
6. **犯规色在界面与 VFX 两处引用同一个令牌**（`ClubUiTokens.FoulEdge`），依赖方向 = 表现层 → 纯数据令牌，无环。
7. **槽容量 16 → 48 会改变单帧顶点上界**（608 → 1824）：这是**容量派生量**，真正的最坏并发约束
   （`MaxConcurrentSlots()` = 17）仍远小于容量。`T117 ③` 的期望值随之更新并注明原因。

### 7.4 本轮**未做**（登记，不在本卡）

- 载入页 / 首页 / 赛前对阵 / 设置 / 暂停 / 重摆确认（35-B 的完整流程；本卡只做**对局内**界面）。
- 界面文案仍为**英文**（字体仅 Latin 子集）；中文需另行确认字体。
- 原型 CSS 的 `inset` 内阴影 / `backdrop-filter: blur` / `text-shadow` / `@keyframes` 淡入
  在 uGUI **无对应能力** ⇒ 遮罩为纯色、卡片瞬时出现、按钮无内下阴影（不伪造说明）。
- 美术包的商业权属确认（`Missing_Assets` 的 ART-E01–E04 全是 E 类）**仍需你或项目方**出确认记录。
  ⇒ ✅ **已结案（用户确认"美术包里面全部都是我可以使用的"）** —— 见 §6 第 1 条的结案记录。

---

## 8. 实跑结果（**本轮实测**，逐条带对象与锚点）

| 项 | 结果 | 证据 |
| --- | --- | --- |
| 场景重建 | **成功**（44,619 → 91,174 字节；无异常） | `GreyboxSceneSetup.RebuildFromCommandLine`，rc=0 |
| 场上球数与材质 | **16 颗球、16 个互不相同的材质、逐号对应**（`CueBall→Ball_00`、`RackBall_N→Ball_N`） | 场景 YAML 静态解析：17 条材质覆盖、17 个不同 guid（16 球 + 球桌） |
| 球杆模型 | 在场（`CueStick` → `CueStickRoot` → `CueModel`，带修正旋转） | `T123` 通过 |
| 界面精灵接线 | `uiSprites` **18** 槽 + `ballChips` **16** 槽 | 场景 YAML |
| **全量 EditMode** | **215 / 215 通过、0 失败、0 跳过** | `_d9r` 外置输出，结果 XML mtime **01:16:09** |
| 四验收器 | **26 / 33 / 22 / 7**，FAIL=0 WARN=0，四个 rc=0 | 各验收器日志 `DAY1/2/3/4_RESULT=PASS` |
| 注入式反例 | **6 档全部实测红**（V1/V3/V4/V5/V6 直接红；V2 按"生成物类"协议拆成 V2a 红、V2b 恢复绿） | `Docs/Tools/artui_injections.sh`（跑完 6 个文件 sha1 逐字节复原） |
| 出包 | **Succeeded**，errors 0 / warnings 0，artifactsOk | `Build/Windows/build-report.json`（01:16） |
| 冒烟（1920×1080） | **144 fps、丢帧 0、rc=0**、renderer 61 / 材质 21 / 物理步 592 | `smoke.json`（`passed: true`） |

### 8.1 本轮修掉的**真缺陷**（判据抓出来的，不是自查发现的）

1. **花色球贴片白底被抹掉**（`T124 ⑥` 抓到）：生成器用 `paste`（替换）而不是 `alpha_composite`（合成），
   色带之外被换成一整片透明 ⇒ 记分牌上 9–15 号球只剩中间一条色带（实测采样到 `alpha=74` 的透明黑）。
2. **球杆朝向约定不成立**（`T123` 抓到）：README 写"杆身 +Z"是 **Blender 源工程口径**，
   实测 FBX 长轴 = **Y**（`min=(-0.016148, -1.400000, -0.015760)`，长 1.400000 m，杆尖在原点、杆身朝 −Y）
   ⇒ 需一层 **−90° 绕 X** 的修正旋转；并因此新增 `CueStickRoot` 中间层（控制器写绝对位姿，修正只能落在子物体）。
3. **构建场景列表的口径冲突**（`T120` 抓到）：见 §8.2。

### 8.2 `T120 ③` 的口径变更（**登记，不是"改检查求绿"**）

- 冲突事实：`SCN-02`（Day1）与 `D4-BLD-01`（Day4）都断言**提交态** build list **恰好**是 `[Main.unity]`
  （且 `BilliardsProjectSetup` 每次 Apply 会写回它）；而"出包不得黑屏"要求用灰盒场景 ⇒ **两条口径互斥**。
- 实测：出包入口 `BuildWindows.BuildFromCommandLine` **自带三重保护**（先跑 `PerformanceSetup` →
  列表不对就拒绝出包 → `BuildPlayerOptions.scenes` **显式**指定场景）⇒ 出包正确性与持久列表**无关**。
- 处置：`T120 ③` 改为断言**那三重保护 + 灰盒场景非空壳**（源码级 + 场景内容级），
  **不再**断言与另一验收互斥的持久值；`ProjectSettings/EditorBuildSettings.asset` **保持提交态不变**。
- 未做（留给 35-B）：把 `Main.unity` 做成真正的游戏入口。届时应把 `T120 ③` 改为
  "首个 build 场景不是空壳"，并同步修订 `SCN-02` / `D4-BLD-01`。

### 8.3 被改动的**既有判据与其理由**（全部登记）

| 判据 | 改动 | 理由 |
| --- | --- | --- |
| `T65` | 期望球数 4 不变；新增"正式球数 = 15"字面量断言；测试内显式 `rackBallCount = 3` | 正式球数由 3 → 15（用户要求 16 颗）；单元用例的摆位是在 4 球下量得的 ⇒ 显式小堆 |
| `T78` | 显式 `rackBallCount = 3` | 满堆会让白球在撞库前先碰堆球 ⇒ 判据按设计红（它守"预测真的跑了库边"）；银行球性质与球堆无关 |
| `T95 (e)` | 不变；`ShootButtonWidth` 回到 150 并注明它是"720p 可读性下限"（≠ 设计稿的 112） | 两个数是不同的量，曾误合并 ⇒ 112 在 720p 只 74.7 px，低于 90 px 下限 |
| `T46` | 球世界半径改为 `PoolConstants.BallRadius × playfield 缩放` | 旧式 `lossyScale*0.5` 只对**单位直径基元球**成立；真模型半径烘在网格里 ⇒ 算出 0.6 m 假半径 |
| `T117 ③` | 顶点上界期望 608 → **1824**（= 48 × 38） | 槽容量按设计稿 16 → 48；② 的最坏并发约束（17）才是真约束 |
| `T120 ③` | 见 §8.2 | 口径互斥 |

### 8.4 本轮**新增的坑**（写给下一个接手的人）

- **生成物类判据的注入协议**：断言"产物"的判据（`T122` 断言场景里的球/材质）在**只改生成器代码**时
  **不会红** ⇒ 注入必须"改码 → **重跑生成** → 断言"；且**还原后也要重新生成**，否则注入会留在产物里。
- **`rm` 在本机守卫下会静默失败**：带盘符路径的 `rm` 被拒（`SAFE_DELETE_INVALID_PATH`），
  而 `rm … && run` 会**整条短路** ⇒ 我因此把上一轮的测试结果 XML 当成本轮读了一次。
  ⇒ 跑批工具已改为"用 python 删 + 跑后打印结果文件 mtime"。
- **`Sprite.name` 不带扩展名**：拿 `FileName()`（含 `.png`）去校验会把正常精灵判成错位。
- **`paste` vs `alpha_composite`**：Pillow 的 `paste(src, box, mask)` 是**替换**语义，
  透明区域会把目标抹掉；"把色带盖到白底上"必须用 `alpha_composite`。

## 9. 开始界面（首页）—— as-built 与偏离（后续增量）

> 起因：用户要求"开始界面（首页）照参考图做"。参考稿真源 = `04_UI/Reference_Screens/home-1920x1080.png`
> + `04_UI/Web_Implementation_Reference/style.css` 的 `.home-*` 段 + `home-ui.js` + `index.html` 的 `#home-screen`。

### 9.1 新增 / 修改的文件

| 文件 | 性质 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/UI/HomeView.cs` | **新增**：首页视图（建在既有 `PracticeHudCanvas` 上，与结算卡同构） |
| `Assets/_Project/Scripts/Runtime/UI/HomeLayout.cs` | **新增**：首页几何单点（设计像素 → 视口归一化，唯一换算点 `Px()`） |
| `Assets/_Project/UI/Icons/ui-club-crest.png` | **新增**：美术包 `Independent_Assets/club-crest-v1.png` 原样拷入（1254×1254，1.93 MB，md5 与源一致） |
| `ClubUiTokens.cs` | 加：首页 8 个色令牌 + 5 个字号常量（并入 `ColorLedger`） |
| `ClubUiSprites.cs` | 加：`UiSprite.ClubCrest = 18`（**只在末尾追加**，未插中间） |
| `UiSpriteImportPostprocessor.cs` | 加：徽章（彩色成品位图）单独放宽 `maxTextureSize` 至 512 |
| `PracticeHudState.cs` | 加：首页 12 条文案常量；全部并入 `AllDisplayCopy()` |
| `PracticeHudView.cs` | 加：`SetChromeVisible(bool)`（首页打开时隐藏三块 HUD 分区） |
| `PointerAimController.cs` / `PointerPowerController.cs` / `AimAssistController.cs` | 改：让位判据由"仅结算卡"扩为"结算卡 **或** 首页" |
| `PracticeSessionStore.cs` | 加：`ReadHistoryFromDisk()`（首页三项统计的真源；缺失/坏文件 ⇒ 空列表，不抛） |
| `GreyboxSceneSetup.cs` | 加：`HomeObjectName = "Home"` 宿主 + 接线（`BuildGameplayHosts` 是唯一构建点） |
| `MainSceneTests.cs` | 改：`T127 ②` 的必需宿主清单加 `Home` |
| `ClubUiTests.cs` | 改：`T124 ④` 的 `ViewSourceFiles` 加 `HomeView.cs` |

### 9.2 三处**偏离参考稿**（全部登记，不包装成"照做"）

1. **两个列表按钮同款式**（深色半透明底 + 描边 + 左文字 + 右箭头）。
   参考稿把 `AI Match` 画成**黄铜实底的主按钮**（设计里的首选动作）；但 35-A **没有 AI 对手**
   ⇒ 把不能用的入口画成整屏最亮的点，正是"假的可点态"。故两行同款，`Practice` 是首页唯一可用入口。
   > **这是一处可逆的、有触发条件的偏离**：参考稿是**上亮下暗**（`AI Match` = `.home-action` 黄铜实底，
   > `Practice` = `.home-secondary` 描边）。本工程把 `AI Match` **一并压暗**，唯一理由是"它没有真实功能"。
   > ⇒ **AI 落地（35-B）时必须恢复参考稿的上亮下暗**（改 `HomeView.BuildActionRow` 的 fill/edge 令牌
   > + 只给主入口换主色，2 个令牌；压暗的那层 `CanvasGroup` 同时移除）。
2. **难度分段（`Casual | Club | Expert`）整体禁用**：三档难度在本工程**没有承接方**
   （选谁都不改变任何东西）⇒ 保留参考稿的几何与"`Club` 选中"的配色位置（信息仍在），
   但整段套 `CanvasGroup`（`alpha = DisabledOpacity`、`interactable = false`、`blocksRaycasts = false`）
   且**三格都不挂 `Button`**（点击无效）。
   **何时能接上**：需要真承接方（AI 强度 / 让球规则）才有意义 ⇒ **35-B**。接上时只需移除那一层 `CanvasGroup` 并挂按钮。
3. **右上两个按钮 = VFX / SFX**（复用 HUD 上已在写的两个真开关），而参考稿是 `music-2` + `settings-2`：
   本工程**没有设置页**（做出来就是"点了没反应"的假齿轮），曲目开关也**没有独立宿主**
   （BGM 与音效同在 `PoolAudio.audioEnabled` 下）。
   ⇒ 只放真的能改状态的两个；**接上设置页 / 独立曲目开关属 35-B**。
   右下曲目行的音乐图标同理复用 `Volume` 精灵：参考稿的 `music-2` 本工程没有对应槽，
   而 `♫`（U+266B）**5 个源字体全都没有**（离线读 5 个 TTF 的 `cmap` 实测）⇒ 写成字符会直接打红 `T97 (c)`。

### 9.3 显隐门（`HomeView.IsOpen`）与冒烟让位（**已裁定：方案 1 + 可验证**）

- 首页**默认启动即显示**（`shownOnStart = true`，用户"开始界面"的意图），关闭后 HUD 与可玩性才可用。
  `HomeView.IsOpen` 是**第二个模态判据点**（第一个是 `PracticeResultView.IsOpen`），三个输入控制器都读它
  —— 否则在首页上右键拖拽会蓄力、松手出杆（结算卡当年就是这么被咬过一次）。
- **冲突**：两个冒烟（`-billiards-smoke` / `-billiards-smoke-aim`）都是**无人操作**跑 600 帧。
  启动即显示首页 ⇒ ① 渲染负载变成"菜单 + 静态桌面"，帧时数字不再代表游戏内负载；
  ② **更硬的**：`AimAssistController.ShouldShow` 因首页让位而恒假 ⇒ **拖动瞄准的预测路径根本不执行**，
  `-billiards-smoke-aim` 测到的是"**没有预测的帧**" ⇒ **测错了对象**（不是"数字偏乐观"）。
- **裁定（team-lead）= 方案 1：挂在冒烟入口里抑制，不用命令行开关。**
  理由正是"防遗忘"：显式开关（方案 2）一旦有人手跑冒烟时忘了带，就会**静默**去测菜单 —— 那正是要避免的陷阱；
  挂在入口上则不可能被忘掉。（方案 3「出包不显示首页」被否：等于把用户要的首页阉掉。方案 2 记为**备选**。）
- **实现（时序是硬约束）**：
  · `SmokeProbe.ParseEarly()`（`RuntimeInitializeLoadType.BeforeSceneLoad`）解析命令行并置
    `SmokeProbe.IsSmokeMode` —— **必须在场景加载前**，因为场景对象的 `Awake` 早于 `AfterSceneLoad`；
    若只在 `Bootstrap`（AfterSceneLoad）里解析，首页的 `Awake` 早已跑完 ⇒ 抑制**静默失效**。
  · `HomeView.Awake` 读 `SmokeProbe.IsSmokeMode`：为真则**不显示首页**并上报。
  · **依赖方向**：UI → Core（`HomeView` 读 Core 的中性事实）。反向（Core 去引用 `Billiards.UI.HomeView`）会形成反向依赖，禁止。
- **可验证证据（不许只写注释）**：`smoke.json` 新增字段 **`homeStartup`**，取值三态：

  | 取值 | 含义 | 冒烟时应为 |
  | --- | --- | --- |
  | `suppressed` | 首页被冒烟态抑制（`HomeView.Awake` 上报） | ✅ **期望值** |
  | `shown` | 首页确实显示了（非冒烟态的常规启动） | ❌ 冒烟里出现即"测到了菜单" |
  | `unreported` | **没有任何一方上报**（场景里没有首页宿主 / 接线缺失） | ❌ 出现即"抑制这件事没人证明" |

  同时冒烟日志末尾打 `home=<取值>`，且当取值 **不是** `suppressed` 时额外打一条 `[SMOKE] ⚠` 警告。
  该字段**不参与判定**（与 `passed` 无关）。
- **待实测**：现有闸门基线（静置/拖动 **6.944 ms / 144 fps / 掉帧 0**）是"启动即进游戏"下测的
  ⇒ 抑制后**必须重测一次**，与基线**同量级**才能称"口径逐位一致"；**若变了要如实报**（这一步需 Unity，属待放行项）。

### 9.4 本轮**未做**（如实登记）

- **"回到首页"没有实现**：参考稿局内顶栏有一个 `#home` 房子按钮，本工程 HUD 里没有它
  ⇒ 首页只在**启动时**出现一次。局内返回首页要有暂停 / 离开语义 ⇒ 35-B。
- **首页文字压在 3D 画面上 ⇒ 需要实机 / 截图确认对比度**（面向用户验收项）：
  参考稿 `.home-screen` 给了 `text-shadow: 0 2px 10px #0009` 把文字从桌布上"托"起来，而 uGUI **无对应能力**
  ⇒ 本工程**不做**该阴影，也**没有**用任何伪造手段代替（**没有**贴一张假阴影图、
  **没有**给文字加人为描边/半透明底板去冒充 `text-shadow`）。⇒ 文字与桌布的对比度**只能由眼睛判定**，
  请以实机截图验收；若不够，应作为**设计变更**提出（例如给左侧菜单加一层底板），而不是在实现里偷偷补阴影。
- 参考稿的三个 `@media` 断点是**重排**；uGUI 无流式布局 ⇒ 非 16:9 档按锚点等比拉伸（表现差异）。
- 按钮 `hover` / `focus` 视觉态不做（本工程既有控件一律不做）。
- **三项统计的口径限制（面向用户验收，必须讲清）**：`PracticeResultView.OnExport` 目前以
  `AppendToHistory(string.Empty, record)` 写入 ⇒ 磁盘历史**最多只留最近一次导出**
  ⇒ 首页的 `SHOTS / POCKETED / RACKS` 反映的是"**已导出的那一次**"，
  **不是累计的"俱乐部记录"（不是累计账）**。
  首页侧读的是**真存储**（`PracticeSessionStore.ReadHistoryFromDisk()` 逐条累加，与参考实现
  `home-ui.js` 的 `sum(history)` 同语义）、**没有伪造数字**；把 `OnExport` 改成累加口径
  ⇒ **35-B**（改完这三项才真正成为累计记录）。

### 9.5 本轮离线核验（**没跑 Unity**；Unity 侧跑批由 team-lead 按顺序放行）

| 项 | 结果 |
| --- | --- |
| 离线编译（Runtime / Editor / Tests 三链，`dotnet build -t:Rebuild`） | **0 error**（Editor 0 warning；Tests 5 个 warning 全是既有文件里的 `CS0162`） |
| 新类型进产物 | `HomeView` / `HomeLayout` / `SetChromeVisible` / `ReadHistoryFromDisk` / `ClubCrest` 等均在 `Billiards.Runtime-verify.dll` 元数据中命中 |
| `T124 ④` 零颜色字面量（脚本复演） | `HomeView.cs` 0 处 `new Color(32)?(`、0 处十六进制色串（三个视图文件同判） |
| `T97 (c)` 字符覆盖（脚本复演 5 个 TTF 的 `cmap`） | **PASS**：新增文案的非 ASCII 字符只有 `·`（U+00B7），5 个字体全覆盖 |
| 首页几何自检 | 全部元素落在 1920×1080 内、无越界；菜单列底 754 与统计行顶 984 不重叠 |
| 改动边界 | `git status` 只有 `.cs` + 新增 PNG；`.meta` / `.prefab` / `.unity` / `.asset` **零内容改动**；行尾全 LF |

**仍需 Unity 才能验（未验）**：首页的真实渲染与观感与**对比度**（需截图）、按钮点击链路、
`T124 ⑤`（19 槽精灵在场景里的接线）、`T127 ②`（`Home` 宿主在 Main 场景里）、
场景重生成与幂等（`T128`）、**徽章 PNG 的首次 Sprite 导入**、全量 EditMode、
**两档冒烟 + 与基线（6.944 ms / 144 fps / 掉帧 0）的重测比对**（见 §9.3）。

### 9.6 本轮新增的坑（写给下一个接手的人）

- **`*.csproj` 是 Unity 生成的本地物（不在仓库里），但它是显式的 Compile 列表** ⇒
  **新增 `.cs` 不在列表里**时，离线编译会报"找不到名称/类型"，而**Unity 真实编译不受影响**
  （Unity 自己会收录）。本轮因此踩了两次同类坑，两次都是"**新文件只进了一半**"：
  · `HomeView.cs` 进了、`HomeLayout.cs` 没进 ⇒ 30+ 处 `CS0103 名称"HomeLayout"`（看起来像代码缺陷，其实是收录缺口）；
  · 环境那路 `MaterialVisualSetup.cs` / `TableVisualBuilder.cs` 引用了 `RoomEnvironmentSetup`，
    但 `RoomEnvironmentSetup.cs` 没进 `Billiards.Editor.csproj` ⇒ 3 处 `CS0103`。
  ⇒ **诊断口令**：`CS0103/CS0246 指向的正好是你新加的名字` ⇒ 先查 Compile 列表，**别先怀疑代码**。
  ⇒ **做整仓离线编译前**，先把两边的新文件都补进各自链的 Compile 列表（补完两链 **0 error / 0 warning**）。
- **`RuntimeInitializeLoadType` 的时序会决定"抑制类改动"是否真的生效**：启动门（开始界面）在
  `Awake` 里读事实 ⇒ 事实必须由 **`BeforeSceneLoad`** 置位；用 `AfterSceneLoad` 会**静默失效**
  （首页已显示出来了）。这类"看起来做了、其实没做"的坑，判据也抓不到（要实跑冒烟看 `homeStartup`）。
- **并发写同一批脚手架文件会静默丢改动**：本轮 `room-env` 与我在同一分钟内各写了一次
  `_verify_Billiards.Editor.csproj`（后写者赢）。⇒ **共享的 `_verify_*.csproj` 不要并发改**；
  各自的验证请各用自己的副本，并在**跑前复核、跑后复跑**。

---

### 9.7 跑批后的**产物流水观测**（离线读产物，非我运行）

> 我的分工不跑 Unity。下列是**读 Unity 跑批留下的产物**得到的观测（读的是文件内容，不是我的推断）。
> 观测时间：2026-10-16 14:19–14:22。

1. **徽章导入已完成且设置正确** —— `Assets/_Project/UI/Icons/ui-club-crest.png.meta`（新生成，guid `a4d6c2f5…`）：
   `textureType: 8`（Sprite）、`alphaIsTransparency: 1`、`spritePixelsToUnits: 100`、`spriteBorder` 全 0、
   `enableMipMap: 0`、`isReadable: 0`、`DefaultTexturePlatform.maxTextureSize: 512`。
   与既有 `ui-disc.png.meta` **同形**，唯一差异正是本轮有意的那一处（Default 128 → **512**）
   ⇒ **`UiSpriteImportPostprocessor` 的徽章分支确实生效**。
2. **两个场景都已按新口径重生成**（`Main.unity` / `PhysicsGreybox.unity` 各自）：
   · Sprite 引用数 **35** = `uiSprites` **19** + `ballChips` **16**（旧口径是 18+16=34）；
   · 场景里存在名为 **`Home`** 的宿主对象；
   · 徽章精灵 guid `a4d6c2f5…` 在场景中出现 **1 次**（= 接进了 `uiSprites` 的第 19 槽）。
3. **`Home` 宿主的三处接线已填**：`HomeView` 组件块里 `hud:` / `vfxView:` / `poolAudio:` 均为本场景内的
   fileID，`shownOnStart: 1`。
   · 交叉印证：`hud: {fileID: 1381200564}` 在场景里出现 **2 次** —— 一次是 `HomeView.hud`，
     另一次是既有的、已知可用的 `PracticeResultView.hud` ⇒ **两处指向同一个 `PracticeHudView`**（与既有范式一致）。
   · `HomeView` / `PracticeHudView` / `PoolVfxView` / `PoolAudio` 的脚本 guid 在场景中各出现 **1 次**（无重复/无缺失）。
4. ⇒ §9.5 里原先列为"待放行"的 **徽章导入** 与 **场景重生成** 两项目前**已在产物中完成**
   （因此 `T124 ⑤` / `T127 ②` 的产物前提已具备；**判定仍需跑一次 EditMode**）。
5. **仍未做**：按新口径**重跑两档冒烟**并与基线（6.944 ms / 144 fps / 掉帧 0）比对 —— 需要出包 + 运行，
   属 team-lead 的跑批顺序。另：`Docs` 之外的两个 TMP 字体 `.asset` 出现"预热落盘"式脏改动
   （`m_SourceFontFileGUID` 未变，见 §9.3/§9.4 相关登记），需在收口时决定保留或还原。
6. 新增三个 `.meta`（`HomeView.cs.meta` `16e648ee…` / `HomeLayout.cs.meta` `64915dd7…` /
   `ui-club-crest.png.meta` `a4d6c2f5…`）**必须一并入库**（否则 Unity 会重新随机生成 guid，
   场景里已接好的引用会断）。
