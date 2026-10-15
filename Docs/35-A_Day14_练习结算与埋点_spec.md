# 35-A Day 14 练习结算与埋点 —— 实施规格

> **来源**：`Docs/35-A_击球手感_制作排期.md` §Day 14。本文件只写规格；实测与偏差记入
> `Docs/测试数基线台账.md` 的 Day 14 行。
> **纪律**：照 `Docs/工程标准.md` R3-2（禁恒真）、R4-2（守卫优先进测试套件）；每个判据自带注入负对照。

---

## 1. 范围（照抄排期）

| # | 任务 | 排期工时 |
| --- | --- | --- |
| 1 | 练习结算（参考 `04_UI/Reference_Screens/12-practice-result.png`）：`Table cleared` / `Rack ended` + 真实杆数 / 进球 / 犯规三列 | 3 h |
| 2 | 埋点：单局时长、出杆数、进球数（→ 进球率）、再来一局次数 | 2 h |
| 3 | 本地 JSON 持久化 + 一键导出（参考 `session-store.js` 与设置页导出） | 2 h |

**验收（照抄）**：入场景 → 打完一局 → 看结算 → 再开一局，**闭环无阻断**；
**埋点数据内容与真实操作一致（不写假数字）**。

---

## 2. 工程现状核查（**本单实测，决定哪些能做到"真"**）

| 事实 | 结论 |
| --- | --- |
| `BallState` 只有 `position` / `velocity`，**没有球号** | ⇒ 无法判"8 号球提前落袋" |
| 开球 `RackPositions(footSpot, rackBallCount = 3)` ⇒ **[白球] + 3 颗目标球** | ⇒ 灰盒不是标准 8 球摆法，**无 8 号球概念** |
| 全工程 grep `foul` / 规则判定 = **0 命中**（无规则引擎） | ⇒ 完整犯规口径属 **35-B** |
| `PhysicsTestDrive.LaunchCount` 公开只读 | ⇒ **杆数**可读，真 |
| `PoolSimulator.IsBallInPlay(i)` + `Balls` 公开 | ⇒ **进球数 / 剩余球**可读，真 |
| `PhysicsTestDrive.LastLaunchRespawnedCueBall` 公开只读 | ⇒ **白球落袋次数**可读，真 ⇒ 用它做 `FOULS` 口径 |
| 全工程输入走**旧版 `Input`**（`Input.GetMouseButton` / `KeyCode`），`activeInputHandler = 2`（新旧都开） | ⇒ 用 `EventSystem` + `StandaloneInputModule` 即可，**零新依赖** |

### 2.1 「结束原因」的口径缺口（**如实登记，不得编造**）

参考稿的副标题是 `8-ball potted too early` —— 那是**规则引擎判负**的产物。本工程**没有规则引擎、也没有球号**
⇒ **该支不可诚实实现**。本单只实现**两种可观测的结局**：

| 结局 | 判定（纯观测，不需要规则） | 副标题文案 |
| --- | --- | --- |
| `Table cleared` | 目标球**全部**落袋（`IsBallInPlay` 为假的非白球数 == 目标球总数） | `Every ball is off the table` |
| `Rack ended` | 玩家**主动重摆**（`R` 键 / 结算卡的 Re-rack） | `Re-racked by the player` |

**登记为 35-B**：规则判负原因（8 号提前落袋 / 错球先碰 / 无库边接触）、完整犯规分类。

### 2.2 「FOULS」列的口径（**真数据，语义收窄**）

本工程的 `FOULS` = **白球落袋（scratch）次数**，取自 `LastLaunchRespawnedCueBall` 的上升沿计数。
**这是真实观测值**，不是占位 0；但**语义窄于**参考稿的"犯规"。
UI 标签照参考写 `FOULS`；代码注释与本文档写明窄口径；完整分类登记为 35-B。

---

## 3. 参考实现的持久化语义（`00_交付总览/Playable_Prototype_Reference/source/src/session-store.js`，逐条抄）

**计数器键集**：`shots` `pots` `playerShots` `aiShots` `playerPots` `aiPots` `playerFouls` `aiFouls`
`racks` `matches` `reracks` `elapsedMs`。
本单只取**练习模式用得上的子集** + 排期点名的两项：
`shots` / `pots` / `fouls` / `racks` / `reracks` / `playAgain` / `elapsedMs`。
（`playAgain` 是**本单新增**：排期点名要"再来一局次数"，而参考的 `racks` 是"完成的局数"、
`reracks` 是"手动重摆"—— 二者都不等于"结算后选择再来一局"。差异已登记。）

**防御式读取（照抄语义，逐条）**：
- `id` 必须是非空 string；`startedAt` 必须能 `Date.parse`；`mode` 必须 ∈ {practice, match}；
  否则整条记录**判为无效并丢弃**。
- 每个计数器：`Number.isFinite(v) && v >= 0` 才采用，否则**归 0**（不抛、不拒绝整条）。
- `events` 只保留**最后 3000 条**；`rackResults` 只保留 string 项。
- `readHistory` 只保留**最近 30 条**；`archiveSession` 对**空会话**（无 shots 且无 racks）**不入档**。
- `writeSave` **返回 bool**（写失败不抛）。

---

## 4. 实现设计

### 4.1 新增文件

| 文件 | 类型 | 职责 |
| --- | --- | --- |
| `Runtime/UI/PracticeSession.cs` | **纯逻辑** | 会话统计累积 + 结算判定 + 派生指标（进球率） |
| `Runtime/UI/PracticeSessionStore.cs` | 纯逻辑 + 一个写盘门面 | JSON 序列化 / 防御式读取 / 历史截断 / 导出 |
| `Runtime/UI/PracticeResultLayout.cs` | **纯逻辑** | 结算卡与其内部的矩形求解（视口比例） |
| `Runtime/UI/PracticeResultView.cs` | MonoBehaviour | 结算卡建树 + 显示/隐藏 + 两个按钮的点击处理 |
| `Tests/EditMode/PracticeSessionTests.cs` | 判据 | `T100` / `T101` / `T102` |

### 4.2 纯函数签名（判据直接驱动）

```csharp
// PracticeSession —— 结算判定（不读 Unity）
public enum RackOutcome { None, TableCleared, RackEnded }
public static RackOutcome ResolveOutcome(int objectBallsPocketed, int objectBallsTotal, bool reRackRequested);
public static string OutcomeTitle(RackOutcome outcome);       // "Table cleared" / "Rack ended"
public static string OutcomeReason(RackOutcome outcome);      // §2.1 的两条文案
public static float PocketRate(int pots, int shots);          // shots<=0 ⇒ 0

// PracticeSessionTracker（可变，纯 C#）：每帧喂观测值
public void Observe(int shots, int pots, int fouls, float nowSeconds);
public void RequestReRack();          // 玩家主动重摆
public void NotePlayAgain();          // 结算后点 Play again
public PracticeSessionSnapshot Snapshot();

// PracticeSessionStore
public static string ToJson(PracticeSessionSnapshot snapshot);
public static bool TryParse(string json, out PracticeSessionSnapshot snapshot);   // 防御式，见 §3
public static string AppendToHistory(string historyJson, PracticeSessionSnapshot snapshot, int maxEntries = 30);
public static bool IsExportable(PracticeSessionSnapshot snapshot);                // 空会话不入档
public static string ExportPath { get; }   // Application.persistentDataPath/practice-sessions.json

// PracticeResultLayout
public static Rect CardRect(float aspect);            // 居中模态卡，⊆ [0,1]²
public static Rect PlayAgainButton(float aspect);
public static Rect ExportButton(float aspect);
```

### 4.3 视图与接线

- 结算卡：复用 `PracticeHudView` 的 token（`PanelColor` / `AccentColor` / `TextColor` / `MutedColor`）与
  `CanvasScaler` 口径；**不新建第二份画布**（挂到同一个 `PracticeHud` 宿主下的第二个 `Canvas` 子物体，
  或复用 HUD 的画布 —— 取后者，避免两个 Overlay 画布争 `sortingOrder`）。
- **按钮可点**：场景需补 `EventSystem` + `StandaloneInputModule`（旧输入）+ 画布补 `GraphicRaycaster`。
  这是本单**唯一**新增的运行时组件；`T102` 守它。
- 两个按钮（**偏离参考稿，登记**）：参考的 `Back to club` 在本工程**无处可去**（没有首页/俱乐部场景）
  ⇒ 改为 **`Play again`**（布铜实心）+ **`Export data`**（描边）——
  后者正好承接排期任务 3 的"一键导出"，比藏在尚不存在的设置页更可达。
- 触发时机：清台或主动重摆 ⇒ 显示结算卡并**冻结击球输入**（`drive` 侧不新状态；结算卡显示期间
  忽略重摆/出杆）。关闭结算卡 ⇒ 复位会话重新计时（`Play again` ⇒ `playAgain += 1`）。

---

## 5. 判据

### `T100` 会话统计与结算判定（纯函数）

- (a) `ResolveOutcome(3, 3, false) == TableCleared`；`(2, 3, false) == None`；`(0, 3, true) == RackEnded`；
      **`(3, 3, true) == TableCleared`**（清台优先于重摆 —— 语义不得含糊）。
- (b) `OutcomeTitle` / `OutcomeReason` 两条文案逐字；`None` 时标题为空（不得给出误导性标题）。
- (c) `PocketRate(0, 0) == 0`；`PocketRate(3, 6) == 0.5`；`PocketRate(3, 0) == 0`（除零不得产生 NaN/Inf）。
- (d) `Tracker`：喂 6 杆 / 3 进 / 1 scratch ⇒ 快照 `shots=6 pots=3 fouls=1`；
      `RequestReRack()` ⇒ `racks=1 reracks=1`；`NotePlayAgain()` ⇒ `playAgain=1`；
      `elapsedMs` 单调不减且 == `(最后一次 observe 的 now − 首次 observe 的 now) × 1000`。

**注入负对照**：把 `ResolveOutcome` 的"清台优先"改成"重摆优先" ⇒ (a) 红。

### `T101` 持久化与防御式读取（照 §3 语义）

- (a) **往返**：`ToJson` → `TryParse` 得到逐字段相等的快照（含 `elapsedMs`）。
- (b) **防御**：`""` / `"{"` / `"null"` / 缺 `id` / `id` 为空串 / `startedAt` 不可解析 / `mode` 不在枚举内
      ⇒ `TryParse` 返回 **false**（整条丢弃），**且不抛异常**。
- (c) **计数值归一**：`shots = -5` / `shots = "abc"` / 缺 `shots` ⇒ 解析后 `shots == 0`，**其余字段仍保留**。
- (d) **历史**：`AppendToHistory` 对空会话（`Shots==0 && Racks==0`）**返回原串不变**；
      超 `maxEntries`（默认 30）时**只保留最近 N 条**。
- (e) 导出路径 == `Application.persistentDataPath/practice-sessions.json`（不得写工程目录）。

**注入负对照**：把 `TryParse` 的 `mode` 校验去掉 ⇒ (b) 红。

### `T102` 结算卡布局与接线

- (a) 六档宽高比 `{(1280,720),(1920,1080),(1920,1200),(1024,768),(2560,1080),(1024,1280)}`：
      `CardRect` **⊆ [0,1]²**；且**画布单位尺寸恒为 583 × 635 设计 px**
      （= 参考稿量得的 `328 × 357 px ÷ 0.5625` 画布缩放；容差 `1e-3`）。
      ⚠ 实现初期曾把参考卡误算成 `0.302 × 0.328` 视口比例 —— **那是把高度按 1080 而非 675 折算**得出的错误值；
      正确口径见 `PracticeResultLayout` 的类注释。
- (b) 卡内两键（`Play again` / `Export data`）：都 **⊆ `CardRect`**、**互不重叠**、高度 ≥ 40 设计 px。
- (c) 场景里**恰好一个** `EventSystem`，其上有 `StandaloneInputModule`（旧输入，与本工程既有输入口径一致）；
      且**恰好一个画布带 `GraphicRaycaster`**（结算卡所在画布）—— HUD 画布本身只显示、不接收射线，
      两者共用同一个画布 ⇒ 只有一个 `CanvasScaler` 口径。
- (d) 场景里存在 `PracticeResultView` 宿主，且其 `drive` / `hud` 已接线。

**注入负对照**：把卡高常量的 y 上界推出屏外（高度改 `1200` 设计 px）⇒ (a) 红。

---

## 6. 验证姿势与边界

- 编辑器路径、`-runTests` 姿势、注入还原、台账格式一律照 `Docs/工程标准.md` §8 与既有各单。
- **改完先离线编译**（`dotnet build <程序集>.csproj`）再跑批 —— 用户可能开着编辑器（批处理会退 21）。

### 盲带（如实）

- **结算卡的观感**（间距、字号舒服度）不可判；判据只锁**几何**与**文案常量**。
- **按钮点击的端到端**（真鼠标点到按钮）属 PlayMode / 手动验收；判据只锁 `EventSystem`/`Raycaster` 在场
  与布局矩形 —— **点击是否真的响应，需用户实机确认**。
- **`FOULS` 语义窄于参考**（§2.2）；**规则判负原因不存在**（§2.1）—— 两者都登记，不做假。
- **`Back to club` 无处可去** ⇒ 换为 `Export data`（§4.3 已登记）。
- 次日留存需真实分发渠道，35-A **只埋点、不出留存结论**（照抄排期原话）。

---

## 7. 完成定义（DoD）

1. `T100` / `T101` / `T102` 三条全绿；全量 EditMode **189 → 192**（+3）。
2. 每条判据一组**实测红**的注入负对照，且逐字节还原（sha1 一致）。
3. 四个验收器（Day1/2/3/4）在改动后重跑，**25 / 33 / 22 / 7，FAIL=0 WARN=0**。
4. 场景已重建（EventSystem + `PracticeResult` 宿主接线）；`git status` 干净。
5. 台账追加 Day 14 行（带提交哈希锚点）。
