# 卡 35-D：局域网 / 直连 IP 双人 8 球对战 · 技术方案

> **状态**：**主理人已裁定 Q1–Q6 与排期（见 §12.1），本文据此更新；文档与 ADR 待用户过目后才提交**。本文只写设计与判据，**不含代码补丁**。
> **范围口径**：双人、局域网或直连 IP、8 球、一局定胜负。匹配 / 账号 / 排行榜 / 观战 / 回放分享 **明确不做**（见 §1.2）。
> **技术路线**：**确定性锁步**（两端各跑同一套自研物理，网络只同步"这一杆的参数"），**摆球种子由房主决定并下发**。此路线已由用户拍板，本文**不再论证**。
> **读法**：每节都落到"改哪个文件的哪一行 / 用什么判据证明对"。引不到行号的地方写「**待定位**」并给出定位方法。
> **编号说明**：骨架按任务给定的 1–9 展开；**§10 破坏性假设**是按硬要求新增的一节，故「工作量分级」顺延为 **§11**。
> **关联**：`Docs/adr/ADR-0002-杆级锁步而非状态复制.md`（决策层，本文作依据、不重复）；`Docs/Tools/rack_fingerprint.ps1`（摆球指纹导出器，只读）。

---

## 0. 一句话目标

在现有单机 8 球（`PhysicsTestDrive` 单真源 + `PoolSimulator` 定长步进 + `EightBallRules` 纯函数判定）之上，加**一层极薄的会话层**：把"一杆"从"直接写两个 float 字段"改成**一个可序列化的命令对象**，让本地指针 / 远端消息两条来源走**同一个出杆入口**；规则与物理**两端各自跑、每杆用校验和互相验尸**，不一致以**房主**为准。

**本卡的技术核心不是网络，是输入抽象。** 网络部分是它的一个消费者。

---

## 1. 目标与非目标

### 1.1 目标（可判定）

| # | 目标 | 可判定形态 |
|---|---|---|
| G1 | 两名玩家各一台 PC，同一局域网或一端直连另一端 IP，打完一局 8 球 | 两台机器从"开球"到"胜负面板"，双方的 `MatchOver` / `Winner` / `EndReason` **逐字段相等** |
| G2 | 一杆的参数是**唯一的网络载荷**；球的位置/速度**不上网**（除握手、校验、重同步） | 抓包：一局中 `Shot`/`ShotResult` 之外**不得**出现任何携带 16 球状态的常规帧；总字节数 < 一局的 `LaunchCount × 400 B` |
| G3 | 摆球由房主定，客户端**不可能**摆出不同的球堆 | 握手期双方各算一次 `EightBallRackPositions` 的**球号→槽位表**并比对摘要；不等 ⇒ 拒绝开局（不许"先打着看"） |
| G4 | 每一杆结束后，双方对"这一杆干了什么"达成一致；不一致**必被检出**且**不静默** | 每杆 `ShotResult` 比对；检出 ⇒ `DesyncCount++` + 走重同步 + HUD 有可见提示 |
| G5 | 联机时 **AI 路径完全不可达** | 见 §9.6 的三道闸门 + 判据 |

### 1.2 非目标（明确不做，且**不许**留半个钩子）

| 非目标 | 为什么明确排除 |
|---|---|
| **匹配 / 房间列表 / 大厅** | 双人直连，不需要发现机制。真要做，属于另一张卡（需要中继或 NAT 穿透） |
| **账号 / 昵称服务端 / 排行榜** | 无服务端。玩家名是本地字符串，只在握手时交换 |
| **跨平台浮点一致性（定点数重写内核）** | 见 §5.5：**先检测、不先上定点数**。重写内核等于重写 `PoolSimulator` 全部算术（`FixedDeltaTime` :34 起的整条积分链），成本与风险远高于"检测 + 重同步" |
| **观战 / 第三人旁观** | 锁步要求每个参与者都跑物理；旁观者不跑就只能收状态流 = 另一套协议 |
| **回放分享 / 录像导出** | 本方案的 `Shot` 日志天然可回放，但导出格式、UI、文件管理都不做 |
| **三局两胜 / 多局比赛循环** | 35-C 的 D4 已砍（一局定胜负）；`Rematch` 只是"再来一局"，不是比赛循环 |
| **大厅内聊天 / 语音 / 表情** | 不做 |

---

## 2. 拓扑与权威模型

### 2.1 拓扑：一房主 + 一客户端，房主权威（host-authoritative）

```
        ┌──────────────────────────┐          ┌──────────────────────────┐
        │  Host（房主 / 玩家 P1）   │          │  Client（玩家 P2）        │
        │  PhysicsTestDrive        │          │  PhysicsTestDrive        │
        │  PoolSimulator（真源 A） │          │  PoolSimulator（真源 B） │
        │  EightBallRules.Judge    │          │  EightBallRules.Judge    │
        │  OnlineSession（权威）   │◄────────►│  OnlineSession（跟随）   │
        └──────────────────────────┘   TCP    └──────────────────────────┘
```

**权威划分（逐项定死，避免"双权威"）**

| 事项 | 谁说了算 | 依据 / 落地位置 |
|---|---|---|
| 摆球种子 | **房主** | `MatchStart{rackSeed}`；房主在 `Initialize()` 之前写 `rackSeed`（`PhysicsTestDrive.cs:70`），解析点 `:397` |
| 一杆的**最终生效参数**（含量化后的白球初速 `v0`） | **房主** | §5.3：`v0` 由房主算好下发，客户端不自己算三角函数 |
| 规则结论（`RuleVerdict`） | **两端各判，冲突时房主** | `EightBallRules.Judge`（`EightBallRules.cs:329`）纯函数，`RuleFacts`（`:231`）只从内核只读出口取；§5.4 |
| 回合归属 | **房主** | 房主把 `verdict.nextTurn` 与 `CurrentTurn` 一起广播；客户端 `SetTurn`（`PhysicsTestDrive.cs:1155`）只做回声 |
| 自由球落点 | **玩家自己放，房主校验** | §6 |
| 超时判负 | **房主**（只用房主自己的钟） | §7.3 |
| 每一杆的球状态 | **不是权威数据**，只是校验证据 | 这是锁步的本义 |

### 2.2 为什么不用 Netcode for GameObjects（NGO）

不是"偏好"，是四条**当时可查证**的事实：

1. **NGO 没装，而且正在被清理的清单上。** `Packages/manifest.json:8` 是 `com.unity.multiplayer.center`（模板遗留，项目铁律的「遗留模板包待清」里点名要删），**没有** `com.unity.netcode.gameobjects`。引入 NGO 等于先加一个包再删一个包。
2. **NGO 的模型是"状态复制"，本方案的模型是"输入的确定性重放"。** NGO 的 `NetworkTransform` / `NetworkVariable` 要把球的位置每 tick 发出去；16 球 × 120 Hz（`PoolSimulator.cs:34` 的 `FixedDeltaTime = 1/120`）≈ 每秒 7680 个 float。本方案一杆 ≈ 30 字节。差两个数量级。
3. **NGO 假设状态由 Unity 物理或你自己的 tick 驱动；本工程的状态由自研累加器驱动。** `PhysicsTestDrive.Advance(float)`（`:1932`）用 `_accumulator`（`:199`）把 `Time.fixedDeltaTime` 折算成整数步，还带**掉帧保护**（`:1942-1947`，超 `maxStepsPerFrame`(`:196`) 时**丢弃累计**）。NGO 的 `NetworkManager` 自带 tick 与时间轴，两套时钟并存必然打架；而本方案的锁步**在"杆"的粒度上同步**，对帧率差异完全免疫（见 §5.1）。
4. **NGO 的权威模型是"服务端拥有 NetworkObject"**，而本工程的"服务端"其实只是**同一个 `MonoBehaviour` 的另一个实例**。引入 NGO 就要把整局状态搬进 `NetworkBehaviour`，等于重写 35-C 花了一整轮才稳定的结算管线（`ApplyVerdict` `:831`）。

**结论**：用一个 `System.Net.Sockets.TcpListener` / `TcpClient` + 手写帧协议（§4），代价是一两百行、零新依赖、零包管理风险。

### 2.3 程序集边界（现在就定，避免以后返工）

`Billiards.Runtime` 的 asmdef 只引 `Unity.InputSystem / Unity.TextMeshPro / UnityEngine.UI`（`Assets/_Project/Scripts/Runtime/Billiards.Runtime.asmdef`）。`System.Net.Sockets` 属于 BCL，**不需要**新增 asmdef 引用；但为了守住工程标准 R1-1（依赖方向 `Editor/Diagnostics/Tests → Runtime`）与"Runtime 里不得放玩家构建不用的代码"（R1-2）：

**新增 asmdef：`Billiards.Net`**（`Assets/_Project/Scripts/Net/Billiards.Net.asmdef`），`references: ["Billiards.Runtime"]`，命名空间 `Billiards.Net`。理由：网络传输可以被替换/剔除（关掉联机 = 不引这个 asmdef），而 `PhysicsTestDrive` 必须仍然能编译。会话对象 `OnlineSession` 放这里，**不**放进 `PhysicsTestDrive`。

> ⚠ 待确认（主理人裁定）：是"新 asmdef"还是"塞进 Runtime 的 `Billiards.Net` 命名空间"。前者干净但多一个程序集（测试 asmdef 的 `overrideReferences: true` + `precompiledReferences` 要跟着改）；后者零成本但违反 R1-2 的"用途判定"精神。**我倾向新 asmdef**，因为联机可以被 ABA 掉（打包时就是个编译开关）。

---

## 3. 输入抽象改造（P1，最关键）

### 3.1 现状：一杆 = 两个公开字段 + 五处直接写 + 五个出杆点

**字段即状态**：

| 位置 | 内容 |
|---|---|
| `PhysicsTestDrive.cs:118` | `public float aimDegrees = 180f;` |
| `PhysicsTestDrive.cs:122` | `public float power = 0.5f;` |
| `PhysicsTestDrive.cs:125/:129` | `minLaunchSpeed` / `maxLaunchSpeed`（力度→速度的映射端点） |
| `PhysicsTestDrive.cs:2247` | `SetAimDegrees(float)` —— 公开写入口（AI 与测试用） |

**出杆点（当前 5 个，逐个点名）**：

| # | 位置 | 来源 | 备注 |
|---|---|---|---|
| L1 | `CueStickController.cs:146` `drive.Launch()` | 本地指针（有球杆时，前推动画到位帧） | 经 `PointerPowerController.cs:180` 的 `_strikeRequested` 传递 |
| L2 | `PointerPowerController.cs:184` `drive.Launch()` | 本地指针（无球杆时的兜底） | |
| L3 | `PhysicsTestDrive.cs:1872` `Launch()` | **本地键盘**（`launchKey`） | ⚠ 这是第三条本地入口，最容易被漏掉 |
| L4 | `PhysicsTestDrive.cs:1317` `return Launch();`（`TryLaunchAiShot`） | AI | |
| L5 | `PhysicsTestDrive.cs:1489` `if (Launch().magnitude > 0f)`（`AiShotPhase.Strike`） | AI | 分帧路径 |

**直接写字段（"字段即状态"的病灶）**：

| 位置 | 谁在写 | 性质 |
|---|---|---|
| `PointerAimController.cs:170` | 方向键微调 | 本地预览 |
| `PointerAimController.cs:199` | 指针拖拽瞄准 | 本地预览 |
| `PointerPowerController.cs:167` | 右键拖拽蓄力 | 本地预览 |
| `PointerPowerController.cs:190` | 取消蓄力归零 | 本地预览 |
| `PhysicsTestDrive.cs:1865-1868` | 键盘方向/力度 | 本地预览 |
| `PhysicsTestDrive.cs:1314-1315`（`TryLaunchAiShot`） | AI 意图落地 | **AI 出杆** |
| `PhysicsTestDrive.cs:1447-1449`（`AiShotPhase.Aiming`） | AI 分帧瞄准 | **AI 出杆** |
| `CueStickController.cs:126` | 读 `drive.power` 算后拉量 | 只读 |

**关键观察**：写字段的 8 处里，**6 处只是"预览/手感"**（真正要的是"屏幕上那根杆指哪、拉多长"），**2 处是 AI 出杆**。真正决定物理的只有 `Launch()` 里的两行：

```csharp
// PhysicsTestDrive.cs:2411-2412（现状）
float speed = LaunchSpeedFor(power);
Vector2 v0 = CueVelocityFor(aimDegrees, speed);
```

⇒ **抽象的切口恰好在这里**：把"`(aimDegrees, power)` 这对字段"换成"一个不可变命令对象"，其余 6 处降级为纯预览。

### 3.2 目标形状

```csharp
// 新增：Assets/_Project/Scripts/Runtime/Physics/ShotCommand.cs（namespace Billiards.Physics）
public readonly struct ShotCommand
{
    public readonly int   aimMilliDegrees;   // 量化：1/1000 度
    public readonly int   powerQ;            // 量化：0..1000（Q1000）
    public readonly int   spinQ;             // **必须为 0**（内核无角速度，见 §3.5）
    public readonly bool  hasPlacement;      // 本杆是否附带"先把白球放到这里"
    public readonly Vector2 placement;       // 已量化到 1e-4 m

    public static ShotCommand Aim(float aimDegrees, float power);   // 唯一构造点：量化在这里发生
}
```

**唯一入口**：

```csharp
// PhysicsTestDrive.cs：把 :2368 的 public Vector2 Launch() 改成
public Vector2 Launch(in ShotCommand command)     // ← 唯一的出杆入口
```

`Launch()` 内部**不再读 `aimDegrees` / `power` 字段**，只读 `command`。`aimDegrees` / `power` 保留为 `public` 字段（HUD、球杆、瞄准线、测试夹具都还在读），但语义降级为**"预览状态"**，并加注释写明"本字段不参与出杆；出杆只认 `ShotCommand`"。

### 3.3 三条来源都走它

| 来源 | 改哪里 | 改后 |
|---|---|---|
| **本地指针** | `CueStickController.cs:146`、`PointerPowerController.cs:184` | 改成 `drive.RequestLaunch(ShotCommand.Aim(drive.aimDegrees, drive.power))` —— `RequestLaunch` 是本地适配器（见下），它自己构造命令再调 `Launch(cmd)` |
| **本地键盘** | `PhysicsTestDrive.cs:1872` | 同上（同一条本地适配器） |
| **AI** | `PhysicsTestDrive.cs:1317`、`:1489` | `Launch(ShotCommand.Aim(intent.aimDegrees, intent.power))`；`:1314-1315` / `:1447-1449` 保留（它们只是把预览字段写成 AI 的意图，让玩家看见对手在瞄哪），**不再被 `Launch` 读取** |
| **远端** | 新代码 | `OnlineSession` 收到 `Shot` 后：`drive.Launch(in cmd)` |

**关于"唯一入口"的一处诚实妥协**：本地适配器 `RequestLaunch(ShotCommand)` 严格说也是 `public`。它**不是第二入口**，因为它的函数体只有一行 `return Launch(command);`，且判据要求它**不是**出杆侧的开关（无法绕过 `Launch` 的任何门控）。替代方案（把 6 处预览点都直接调 `Launch`）会把"预览"和"承诺"混在一起 —— 现在 `PointerPowerController` 的蓄力是"每帧写 power"，直接调 `Launch` 就变成"每帧出杆"。**故：`Launch(cmd)` 是唯一出杆入口；`RequestLaunch(cmd)` 是唯一的本地意图提交点，且只允许被 3 处（L1/L2/L3）调用。**

### 3.4 量化为什么在这里做（这条是整套方案的枢纽）

如果"本地出杆用原始 float、远端出杆用量化后的 int"，两条路径**从第一帧就不一样**。所以：

**量化点前移到 `ShotCommand.Aim()` —— 本地玩家也被量化。**

代价与收益：

- 代价：瞄准角步长 0.001°、力度步长 0.1%。在 1.27 m 的击球距离上，0.001° 的横向偏差 ≈ `1.27 × 0.001°×π/180` ≈ **0.022 mm**，远低于任何可感知阈值（球半径 28.575 mm，`PoolConstants.cs:28`）。
- 收益：**"同一杆"在网络两端、在本地与远端，是同一个 `ShotCommand` 值**。校验和（§5.2）从此只受浮点运算顺序影响，不受"谁打的那一杆"影响。
- 选十进制友好量化（1000 而不是 1024）的理由：**既有测试与既有夹具用的是十进制整数**（`aimDegrees = 180f`、`power = 0.5f`、`powerStep = 0.02f`，`PhysicsTestDrive.cs:118/:122/:172`）。`180 → 180000 → 180`、`0.5 → 500 → 0.5` **逐位往返无损** ⇒ 大量既有判据的 `v0` 断言**不需要改数值**。

### 3.5 `spin` 字段：诚实处理，不假装有

**事实**：内核**没有角速度**。`BallState`（`BallState.cs:12-19`）只有 `position` / `velocity`；全仓 `grep -rni "spin\|english\|侧旋"` 在 `Assets/_Project/Scripts` 下 **0 命中**。

**决定**：`ShotCommand.spinQ` **保留字段、恒定校验为 0**。

- 协议里带它（schema 里显式存在），房主收到 `spinQ != 0` ⇒ `ShotRejected{reason = unsupported-spin}`，**不静默降级为 0**（静默降级 = 客户端以为打了塞，实际没打，等于界面在说谎）。
- 加真旋转是**物理特性新增**（要在 `PoolSimulator.Step()` `:827` 的积分链里加角速度、台呢摩擦改为滑动/滚动/旋转三态），成本按"新物理特性"另计，**不在本卡**。

### 3.6 会被 P1 影响的既有判据（点名）

| 判据 | 位置 | 影响 | 处置 |
|---|---|---|---|
| **`T145` ④**「`TryLaunchAiShot` 与同 `aim`/`power` 手工出杆的事件流 1500 步逐条相同」 | `PhysicsTestDriveLaunchStateTests.cs:1923` 起（用例 `T145_...:1987`） | 手工侧若仍写原始 float，AI 侧走量化 ⇒ **会红**。**这是期望的红**：它正好证明"两条路都过了同一个量化点" | 判据两侧都改成 `ShotCommand.Aim(...)` 后必须**回绿**；回不绿说明量化没前移 |
| **`T145` ⑧**（源码守卫：`PointerPowerController` 里 `drive.TryConsumePlayerInput()` 的调用点数） | helper 在 `:1878-1903`（`StripCommentLines` / `CountOccurrences`） | 无直接影响，但**它给出了 P1 守卫的现成范式** | 照抄该范式写"出杆调用点计数"守卫（§8 P1 验收） |
| **`T65 (f)(g)`**（15 颗接线 + 同种子逐位可复现） | 台账 35-C P5-A 行 | `Launch` 签名变化**不影响**它们；但 `System.Random` 替换（§5.6）也**不会**被它们抓到（它们只测自一致性） | 补一条**金向量**判据（§8 P2 验收） |
| `GreyboxTuningWiringTests`（8 处 `Launch()`）、`PhysicsTestDriveLaunchStateTests`（48 处）等 **70 处测试调用点** | `grep -rn "Launch()" Assets/_Project/Tests \| wc -l` = 70 | 签名改动后全部编译失败 | 见 §3.7 |
| `PhysicsTestDrive.cs` 内 25 处 `Launch()`（多为注释引用） | — | 注释里的是文档引用，改签名后要同步措辞 | 逐处核 |

### 3.7 70 处测试调用点怎么改（不动 Runtime 的"唯一性"）

**方案**：在**测试程序集**里加一个扩展方法（`Assets/_Project/Tests/EditMode/ShotCommandTestExtensions.cs`）：

```csharp
internal static Vector2 LaunchFromFields(this PhysicsTestDrive d)
    => d.Launch(ShotCommand.Aim(d.aimDegrees, d.power));
```

70 处 `drive.Launch()` → `drive.LaunchFromFields()`，是**机械替换**（`sed` 可做，但按工程纪律应当逐文件 Review 而不是批量 sed —— 其中至少 `PhysicsTestDriveLaunchStateTests.cs:2335/2414/2495` 等处在断言 `magnitude <= 0f`，语义是"被门控拒"，改后仍成立）。

**为什么不用"在 Runtime 保留一个无参 `Launch()` 重载"**：那正好就是"第二入口"，与本卡的唯一性判据直接冲突。`LaunchFromFields` 住在测试程序集 ⇒ Runtime 里 `Launch` 的出现次数是**可数的常量**，守卫才能写。

### 3.8 "唯一入口"的机械判据（可注入必红）

**判据**（写成 `[Test]`，源码级）：

1. 读 `Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs`，剥注释（复用 `StripCommentLines`，`:1879`），断言 `"public Vector2 Launch("` 出现 **恰好 1 次**。
2. 断言 `"Launch("` 在 `Runtime/` 目录下的**全部** `.cs` 里，出现在**固定的 allow-list** 内：
   - 定义点 `PhysicsTestDrive.cs`（1 处定义 + `RequestLaunch` 内 1 处 + AI 侧 2 处）
   - `CueStickController.cs`（1 处，且必须形如 `RequestLaunch(` 而非 `Launch(`）
   - `PointerPowerController.cs`（1 处，同上）
   - `ShotCommandTestExtensions` 不在 Runtime。
   allow-list **写成显式的 (文件, 期望次数) 表**，不是"总数 ≤ N"—— 总数不变但位置漂移是漏检点。
3. **注入必红**（每条独立命令 + 还原后回读 sha1）：
   - `P1-INJ-1`：在 `PhysicsTestDrive` 里加回 `public Vector2 Launch() { return Launch(ShotCommand.Aim(aimDegrees, power)); }` ⇒ 判据 1 红。
   - `P1-INJ-2`：把 `CueStickController.cs:146` 改成 `drive.Launch(ShotCommand.Aim(drive.aimDegrees, drive.power));`（绕过 `RequestLaunch`）⇒ 判据 2 红。
   - `P1-INJ-3`：把 `ShotCommand.Aim` 里的量化改成恒等 `(int)(deg*1000f)` 但 `powerQ` 不量化（保留 float 精度）⇒ `T145 ④` **不红**（因为两侧都同一条路），但新增的"本地/远端同值"判据必须红 —— **这条注入专门验证"量化前移"确实被守住**。

---

## 4. 协议消息表

**所有传输都是这一层的封装；`ITransport` 是接口，故 P2 可以在 EditMode 里用内存传输测，不需要真网络**（见 §8）。

### 4.1 帧格式

```
[u16 length][u8 type][payload...]        小端；length = payload 字节数（不含自身与 type）
```

- **不用 JSON**：`float.ToString` / `double.Parse` 的舍入与区域设置都是确定性杀手；而且反射序列化在 IL2CPP 下不可靠。全整数编码，零歧义。
- **不用 `string.GetHashCode()` / `System.HashCode`**：.NET 的字符串哈希**按进程随机化**，两端必然不同。摘要一律自己实现 FNV-1a 32（§5.2）。

### 4.2 消息表

| 消息 | 方向 | 字段 | 语义 / 校验 |
|---|---|---|---|
| `Hello` | C→H | `protocolVersion:u16`、`contentHash:u32`、`playerName:utf8[≤24]`、`lastSeq:u16`（重连时用，首次 = 0） | `contentHash` = 对 §4.3 的"物理/桌/规则指纹"取 FNV-1a。不等 ⇒ `Bye{reason=build-mismatch}` |
| `JoinAccepted` | H→C | `playerId:u8`、`hostPlayerName:utf8[≤24]`、`sessionId:u32` | 立刻回 `MatchStart` |
| `JoinRejected` | H→C | `reason:u8`（`busy` / `build-mismatch` / `proto-mismatch`） | — |
| `MatchStart` | H→C | `rackSeed:i32`、`firstTurn:u8`、`rackDigest:u32`、`rulesetId:u16` | **`rackDigest` = 15 颗球的"球号→槽位"表摘要**（§5.6）。客户端自己算一遍，不等 ⇒ 立即 `Bye{reason=rack-mismatch}`，**不许**"先打着看"。⚠ 原本骨架里的 `tier?` **删除**：联机没有 AI 档位，`MatchTier`（`PhysicsTestDrive.cs:743`）只服务 AI（§9.6） |
| `Shot` | C→H **或** H→C | `seq:u16`、`cmd`（`aimMilliDegrees:i32`、`powerQ:u16`、`spinQ:i16`、`hasPlacement:u8`、`placementX:i32`、`placementY:i32`）、`checksumBefore:u32` | 出杆方发。**发出即开打**：先本地 `Launch(cmd)` 再发（本地优先，延迟 = 0）。房主收到后校验并广播（§5.3） |
| `ShotAccepted` | H→对方 | `seq:u16`、`cmd`（房主的**权威**版本，含量化后的 `v0`，见 §5.3）、`checksumBefore:u32` | 对方据此 `Launch(cmd)` |
| `ShotRejected` | H→发杆方 | `seq:u16`、`reason:u8`（`not-your-turn` / `power-range` / `spin-unsupported` / `placement-illegal` / `checksum-before`） | **必须可见**（HUD 提示 + `MatchTurnNotice` 同族出口）；不许静默 |
| `ShotResult` | 双方互发，以房主为准 | `seq:u16`、`checksumAfter:u32`、`balls[16]`：`xQ:i16 / yQ:i16 / vxQ:i16 / vyQ:i16`、`frozenMask:u16`、`capturedPocket[16]:i8`、`ruleDelta`（`nextTurn:u8`、`foul:u8`、`ballInHand:u8`、`groupState:u8`、`reassignGroup:u8`、`groupBall:i8`、`gameOver:u8`、`winner:u8`、`outcome:u8`、`mustShootForward:u8`、`reRack:u8`、`respotEightBall:u8`）、`factsDigest:u32` | 两端各算各的，**比对**。`factsDigest` = 对 `firstContactBall` + `pottedOrder` + `cushionsAfterFirstBallContact` 取 FNV（这些是**离散**事实，见 §5.4 为什么必须单独比） |
| `Resync` | H→C | `seq:u16`、`snapshot`（§5.7 的完整快照）、`ruleState`、`playerGroup:u8`、`pendingPlacement:u8`、`ballInHand:u8`、`launchCount:u16`、`checksum:u32` | 房主权威状态。客户端**整体覆盖** |
| `ResyncAck` | C→H | `seq:u16`、`checksum:u32` | 客户端回读摘要，房主核 |
| `CueBallPlaced` | 放球方→H→对方 | `seq:u16`、`x:i32`、`y:i32`（1e-4 m） | §6 |
| `PlacementRejected` | H→放球方 | `seq:u16`、`x:i32`、`y:i32` | 房主用 `CanPlaceCueBall`（`PhysicsTestDrive.cs:2251`）判过不合法 |
| `RematchRequest` | 任一方→H | `—` | 房主同意后重发 `MatchStart`（新 `rackSeed`） |
| `RematchDeclined` | H→请求方 | `reason:u8` | 不许静默 |
| `Ping` / `Pong` | 双向 | `tick:u32` | 1 s 一次；5 s 无任何帧 ⇒ 判掉线（§7.3） |
| `Bye` | 双向 | `reason:u8`（`normal` / `desync` / `timeout` / `peer-gone` / `rack-mismatch` / `build-mismatch`） | **对局终止的唯一干净出口**。附带一条 HUD 可见原因 |

### 4.3 `contentHash` 到底盖什么（这是"能不能开局"的硬门槛）

**原则**：只盖**有物理/规则读者**的字段。盖多了会造成假不一致（见下面"明确不盖"里的 `generatedAtUtc`），盖少了会漏掉真不一致。

#### A. 必须盖（逐项给出引用它的代码位置）

| 项 | 为什么必须盖 | 读者 |
|---|---|---|
| `TableConfig.segments[i].a` / `.b` | 82 段库边几何 | `PoolSimulator` 构造 `:445-456`（摊平成数组） |
| **`segments` 的 list 顺序** | 「顺序参与确定性，不得重排」 | `PoolSimulator.cs:419-420` 的构造注释明说 |
| `TableConfig.pockets[i].throatCenter` | 袋喉中心（入袋判据的基准点） | `:463-472` → `_pocketThroatCenter` |
| `TableConfig.pockets[i].throatNormal` | 喉平面法向 | `:467` → `_pocketThroatNormal` |
| `TableConfig.pockets[i].openingRadius` | **入袋判据唯一的边界量**（`ρ ≤ openingRadius`） | `:468` → `_pocketOpeningRadius`；另 `IsPlacementLegal` `:602` |
| **`pockets` 的 list 顺序** | 下标即袋号（`_pocketOpeningRadius[i]`） | `:463-472` |
| `TableConfig.halfLength` / `halfWidth` | 放球合法性（台内边界） | `PhysicsTestDrive.cs:2303-2304` → `IsPlacementLegal` `:572` |
| `TableConfig.footSpot` | 摆球顶点 / 白球开球位 / 黑 8 复位 | `:2013`、`:2039`、`:1048` |
| `PoolSimulator.FixedDeltaTime` / `SubSteps` | 步长与子步数 | `:34`、`:374` |
| `PoolSimulator.Params` | 生效物理参数（弹性/摩擦/恢复系数） | `:405` |
| `RestSpeedThresholdValue` / `RestStableFramesValue` / `PocketCaptureRadiusScaleValue` | 三个"不可观测参数"的只读出口 | `:408` / `:411` / `:414` |
| `PoolConstants` 里被内核读的标量（`BallRadius` `:28`、`MaxBallSpeed` `:39`、`ClothHalfLength`、`ClothHalfWidth`、`KitchenLineX`、`PocketOpeningRadius`…） | 单位制硬指标 | `PoolConstants.cs` |
| **`PhysicsTestDrive.minForwardComponent`** | **影响"这一杆是否被接受"**（头区自由球的前向门） | `PhysicsTestDrive.cs:806`（默认 `0.5f`）→ `:2416-2421` 的 `IsBreakShotForwardEnough` 拒杆门 |
| `rulesetId`（新增常量，放 `EightBallRules`） | 规则语义版本；任何 `Judge` 语义改动必须 bump | 新增 |

**特例：`minLaunchSpeed` / `maxLaunchSpeed`（`PhysicsTestDrive.cs:125/:129`，来源 `PhysicsTuning.cs:81/:84`）**

它们**只影响 `v0`**，不影响内核演化。因为**决策 D-Q2 已经批准"`v0` 由房主算好下发"**，客户端根本不用自己算 ⇒ **不需要进 `contentHash`**。

⚠ 但这条**是有前提的**：一旦有人回退 Q2（改成两端各自 `CueVelocityFor`），这两个字段**立刻变成必须盖**——否则两端 `v0` 不同，第一杆就散。**把这句话写进 `contentHash` 的实现注释里**，不要只留在文档。

**明确不进 `contentHash` 的"可调量"**

| 项 | 为什么 |
|---|---|
| `PhysicsTestDrive.maxStepsPerFrame`（`:196`，默认 8） | 只影响**掉帧时丢弃多少累计时间**（§5.1 第 5 行）⇒ 影响节奏、**不影响结果**。盖进去没有收益，只会把一个纯配置项绑进协议（`Advance` 的掉帧保护 `:1942-1947` 不改变最终状态，这一点是 §5.1 已论证的） |

#### B. 明确**不**盖（并给出"无读者"的证据）

不盖不是偷懒，是因为**盖了会造成假不一致**——两个构建几何完全相同却因为一个无关字段互拒开局。

| 项 | 不盖的理由 | 证据 |
|---|---|---|
| **全部 provenance**：`sourceFileName` / `sourceSha256` / `runtimeNode` / **`generatedAtUtc`** / `generator` / `sourceVertexCount` / `sourceTriangleCount` / `sourceUniformScale` / `sourceNonUniformScaleApplied` | **`generatedAtUtc` 绝对不能进摘要**：同一份几何在不同时间重新导入 ⇒ 摘要不同 ⇒ 两端拒绝开局。这是本节最容易踩的坑 | `TableConfig.cs:29-38` 声明；只有导入器写、无运行时读者 |
| `captureRadius` | **内核已不再读它**（边界改成了 `openingRadius`） | `PoolSimulator.cs:166` 原话：「**内核不再读取** `captureRadius`」 |
| `captureNote` | 备注字符串；语义在代码里 | 只有 `TableIntegrityValidator.cs:321-327` 读（Editor） |
| `innerLength` / `innerWidth` | 与 `halfLength` / `halfWidth` 冗余；无独立读者 | `ConsistencyErrors`（`TableConfig.cs:100-101`）只做两两互检；物理只用 half* |
| `clothHeight` / `noseHeight` / `legBottomY` / `ballRadius`（`TableConfig` 那个） | 渲染参考 / 一致性校验用；物理用的是 `PoolConstants.BallRadius` | `IsPlacementLegal` `:574` 取 `PoolConstants.BallRadius`，不取 `tableConfig.ballRadius` |
| `outerBoundsMin` / `outerBoundsMax` / `offTableHalfExtents` | **无运行时读者**（`offTableHalfExtents` 全仓只有声明 + 导入器写入） | `grep -rn "offTableHalfExtents" Assets/_Project/Scripts` ⇒ 仅 `TableConfig.cs:68` 与 `TableDataImporter.cs:264` |
| `pockets[i].index` / `.kind` / `.center` / `.mouthA` / `.mouthB` / `.throatA` / `.throatB` / `.mouthWidth` / `.throatWidth` | 物理不用（`PocketHoleCenter` 是**表现层**滑袋动画用，且由 `throatCenter` 派生） | `PoolSimulator.cs:788-795` 明说「只服务表现层……**不参与任何判定**」 |

#### C. 防"摘要清单腐烂"的守卫（强烈建议）

摘要清单是**人工维护**的，而"有人新读了一个字段但没盖进摘要"这件事**不会报错**（只会两端悄悄不一致）。⇒ 加一条源码守卫判据：

> 扫 `PoolSimulator.cs` / `PhysicsTestDrive.cs` / `PhysicsTuning.cs` 里所有 `tableConfig.<成员>` 与 `table.pockets[...].<成员>` 形式的读取，得到的成员集合**必须 ⊆ 摘要集合**；出现集合外的新成员 ⇒ 判据红，提示"你新读了一个桌字段，去把它盖进 `contentHash`"。

#### D. 判据

1. 改 `TableConfig.asset` 里任意一个 `openingRadius` 的第 4 位小数 ⇒ `contentHash` 必须变（防止有人把"桌数据没同步"当成"浮点漂移"去排查）。
2. **反向判据（同样重要）**：只改 `generatedAtUtc` / `captureRadius` / `captureNote` ⇒ `contentHash` **必须不变**。缺这一条，上面那张"不盖"表就只是注释。
3. 规则：`pockets` 列表**重排**（元素不动、顺序变）⇒ `contentHash` 必须变。

#### E. `ScriptableObject` 引用怎么参与哈希（**最容易在两端不一致的一类**）

先把事实摆出来（`grep -n "public " Runtime/Table/TableConfig.cs` 全量核过）：

**这一层没有任何 `UnityEngine.Object` 派生类型的字段。** `TableConfig` 自己的字段只有
`string` / `int` / `float` / `bool` / `Vector2` / `Vector3` / `List<CushionSegment>` / `List<Pocket>`；
子结构 `CushionSegment`（`TableConfig.cs:142`，`[Serializable] struct`）是 `Vector2 a/b` + `string rail`，
`Pocket`（`:155`，`[Serializable] class`）是 `int` / `string` / `Vector2` / `float`。
⇒ **不存在"引用了另一个资产"这件事**，所以**不涉及 asset GUID**。

但有 **两个 `ScriptableObject` 实例**是通过 `PhysicsTestDrive` 的序列化引用拿到的，这才是真正的风险面：

| 引用 | 位置 | 它携带什么 |
|---|---|---|
| `PhysicsTestDrive.tableConfig` | `PhysicsTestDrive.cs:26`（`TableConfig`，`:25` `: ScriptableObject`） | 82 段 + 6 袋几何 |
| `PhysicsTestDrive.tuning` | `PhysicsTestDrive.cs:114`（`PhysicsTuning`，`PhysicsTuning.cs:42` `: ScriptableObject`） | 7 个物理系数（→ `PhysicsParams` struct `PhysicsParams.cs:37`）+ `restSpeedThreshold` / `restStableFrames` / `pocketCaptureRadiusScale` + `minLaunchSpeed` / `maxLaunchSpeed` |

**怎么参与哈希 —— 只允许一种做法：**

> **按白名单逐字段取值，按固定顺序喂 FNV-1a。** 取的是**值**，不是引用。

**四种看着能用、实际是坑的做法（逐个说明为什么不能用）**：

| 做法 | 为什么不行 |
|---|---|
| **asset GUID** | **改值不改 GUID** ⇒ 桌数据被改了 GUID 照样一致 ⇒ **假绿**。GUID 只能判断"是不是同一个资产文件"，判断不了"内容是否相同"。另外 `TableConfig` 这一个层根本没有子资产引用，所以 GUID 连"引用重定向"都防不了 |
| `Object.GetInstanceID()` | 每次加载都可能不同 ⇒ 两端**必然**不一致 |
| `GetHashCode()` | `UnityEngine.Object` 覆写成**引用相等**语义 ⇒ 只证明"是同一个对象"，不证明内容 |
| `JsonUtility.ToJson(asset)` | ① 会**把 provenance 一起算进去**（含 `generatedAtUtc`）⇒ 直接违反上表 B 组第 1 条；② 依赖 Unity 序列化格式与浮点格式化，格式一变摘要就变，且**没有可自证的方法**。⇒ 白名单逐字段才是唯一能守住"只盖有读者的字段"的写法 |

⇒ 白名单写法的另一个好处：它**天然配合 §4.3-C 的守卫** —— 白名单是"人写的一份列表"，守卫是"机器核对这份列表没漏"，两者互为补集。

#### F. 什么字段"必须随 `MatchStart` 下发"——**一个都不下发**

| 下发 | 不下发 |
|---|---|
| `rackSeed`（摆球唯一需要的输入） | ❌ 桌几何、物理系数、阈值、`PoolConstants` —— **全都不发**，只用 `contentHash` **比对** |
| `rulesetId`（一个小整数） | ❌ 任何球的位置/速度 |

**为什么 `rulesetId` 要显式下发而不是只算进 `contentHash`**：为了让 `JoinRejected` / `Bye` 的**错误信息能指名道姓**（"你是 ruleset 7，我是 ruleset 8"）。只给一个摘要，玩家和我们都只能知道"不一致"，不知道"哪一类不一致"——而联机的失败态本来就多，能指名的必须指名（这是"不许静默"的延伸：**不许含糊**）。

---

## 5. 确定性保障与校验

### 5.1 先划清"什么时候可能不一致"

| 阶段 | 是否确定 | 依据 |
|---|---|---|
| 摆球（seed → 15 个位置） | **当前不确定**（跨运行时） | `PhysicsTestDrive.cs:2151` `new System.Random(seed)` —— 见 §5.6，**本卡最危险的一处** |
| 一杆的 `v0`（`aimDegrees`/`power` → 速度） | **当前有一处未收口** | `PhysicsTestDrive.cs:537-542` 的 `Mathf.Cos` / `Mathf.Sin`：`PoolMath`（`PoolMath.cs:17`）只有 `Sqrt`（`:20`）和 `Atan2`（`:26`），**没有 Sin/Cos** ⇒ 违反 R5-3「开方/三角走单一口径」（`Docs/工程标准.md:167`） |
| 一杆的演化（`Step()` 循环） | **强确定** | `PoolSimulator.cs` 全仓**零个** `Mathf.Sin/Cos/Atan2/Pow/Acos/Asin/Tan`（实测 `grep` = 0 命中）；只有 `Min×7 / Max×7 / Abs×6 / Clamp×2 / CeilToInt×1` + `PoolMath.Sqrt`。IEEE-754 保证 `+ − × ÷ sqrt` 正确舍入 ⇒ **跨 ISA 位相同**（除 FMA 收缩，见下） |
| 一杆的判据（`Judge`） | **强确定** | `EightBallRules` 纯函数、不读时间/随机（`:209` 的类注释明说）；输入 `RuleFacts` 只来自内核只读出口 |
| 每帧推进的**节奏** | **不确定，但无害** | `Advance()`（`:1932`）用 `_accumulator` 折算；掉帧保护 `:1942-1947` 会**丢弃累计**。结果不受影响：`Step()` 是纯状态函数，"走多少步"由**内容**（球停下来）决定，与"分几批走"无关 |

⇒ 需要防的只有三件事：**摆球 RNG**、**一处三角函数**、**FMA 收缩**。

### 5.2 校验和怎么算

**量化步长（逐项给理由）**：

| 量 | 步长 | 编码 | 理由 |
|---|---|---|---|
| 球心位置 | **1 mm** | `i16` = `round(pos / 0.001)`；范围 ±32.767 m ≫ 台面 ±1.27 m | `float32` 的 ulp 在 1.27 m 处 ≈ **0.12 µm**；一杆几千步的随机游走累积量级 ≈ `√N × ulp` ≈ **10 µm**（0.01 mm）⇒ 1 mm 留约 **100×** 余量，几乎不可能假阳性；同时 1 mm = `1/28.6` 个球直径 ⇒ **任何真实分歧当杆必被检出** |
| 球心速度 | **1 mm/s** | `i16` = `round(v / 0.001)`；范围 ±32.767 m/s ≫ `MaxBallSpeed = 8`（`PoolConstants.cs:39`） | 同上；8 m/s 处 float ulp ≈ 0.5 µm/s |
| 自由球落点 | **0.1 mm** | `i32` = `round(p / 1e-4)` | 放球是玩家手势，1 mm 步长在 4K 屏上有可见颗粒；0.1 mm 无感，且两端拿到的是**同一个 int** |
| 瞄准角 | **0.001°** | `i32` = `round(deg × 1000)` | 见 §3.4 |
| 力度 | **0.1%** | `u16` = `round(power × 1000)`，钳到 `[0, 1000]` | 见 §3.4 |

**摘要算法**：自己实现 **FNV-1a 32-bit**，输入是**定长、整数、小端**的字节序列（16 球 × 8 字节 = 128 B，再加 `frozenMask`(2) + `capturedPocket`(16) + 规则摘要）。

**为什么 `ShotResult` 同时带"每球量化向量"而不只带 hash**：

- 只带 hash ⇒ 只知道"某处不一样"，排查等于大海捞针；
- 带 `balls[16]` ⇒ 收到后**先算每球的实际差值**，日志能直接写"3 号球偏了 4.7 mm、7 号球速度差 12 mm/s"，且**阈值可以按实测校准**，而不是靠猜；
- 成本：**128 B/杆**。一局 100 杆 ≈ 12.8 KB。可以忽略。

**额外要求**：即使**没超阈值**，也要把 `maxBallDeltaMm` 记进日志与诊断出口（`DesyncMaxDeltaMm`）—— "余量有多大"是**实测数据**，不是设计断言。若实测余量长期 < 10×，说明 1 mm 步长该收紧（届时改一个常量，判据口径不变）。

### 5.3 白球初速 `v0` 由房主算好下发（把三角函数差异在协议层消灭）

`ShotAccepted` 携带的 `cmd` 是**房主版本**，其中新增两个字段（或者更干净：`ShotAccepted` 直接带 `v0x:i16` / `v0y:i16`，单位 mm/s）。

**语义**：**客户端不自己算 `v0`**，一律用房主给的。

- 好处：跨平台超越函数差异（`Mathf.Sin/Cos` 的 libm 实现）**在协议层被彻底消除**。剩下的只有 `+ − × ÷ sqrt`，那是 IEEE 保证正确舍入的。
- 客户端的 `CueVelocityFor` 仍然存在（瞄准预测线要用它），但**它算出来的东西只是画给玩家看的**，不进物理。
- **副作用（免费的好处）**：客户端可以把"我自己算的 `v0`"和"房主给的 `v0`"比一下，差值超过 1 个量化步 ⇒ **在开杆前就发现 libm 不一致**，比等一杆打完再靠 checksum 发现要早得多。这个比较**不阻塞**（只记 `PreLaunchMathMismatchCount`），但它是最好的现场诊断信号。

> 若主理人认为"下发 `v0` 就不算纯锁步了"：那就在 P3 阶段同时收口 `CueVelocityFor` —— 把 `Mathf.Cos/Sin` 换成 `PoolMath.Cos/Sin`（`PoolMath.cs` 加两个方法，实现用 `Mathf.Cos/Sin`）。这**不消除**跨平台差异，只是把口径收成一处（满足 R5-3），便于将来换成查表或定点。**两件事不冲突，建议都做。**

### 5.4 不一致时的判定与恢复顺序

**判定（按顺序，先便宜后贵）**：

1. `checksumBefore` 不等 ⇒ **这一杆根本不开始**。这是"上一杆就已经分叉了"的确定信号。→ `Resync`。
2. `ShotResult.ruleDelta`（离散事实）不等 ⇒ **硬分歧**。比分已经不可信 → `Bye{reason=desync}`。
3. `factsDigest` 不等 ⇒ **硬分歧**（`firstContactBall` 差一颗、进袋顺序不同、碰库集合不同 —— 这些即使球位置差 < 1 mm 也可能翻转，**必须单独比**）。
4. 只有 `checksumAfter`（位置/速度）不等，但 1/2/3 全等 ⇒ **软分歧** ⇒ 自动 `Resync`，`DesyncCount++`，**继续打**。

**恢复顺序（谁重算、以谁为准）**：

```
Client: 发现不一致 → 停本地推进（OnlineSession 置 AwaitingResync）→ 发 ResyncRequest{seq}
Host:   收到 → 用自己当前状态打 Resync{snapshot, checksum}
Client: 整体覆盖 → 重算 checksum → 不等 ⇒ 再来一次（最多 2 次）
        2 次仍不等 ⇒ Bye{reason=desync}（绝不死循环）
```

- **以房主为准**，无例外。客户端**没有任何路径**能把自己的状态推给房主（协议里 `Resync` 是 H→C 单向，`ResyncRequest` 无载荷）。
- **同一局内第 3 次软分歧** ⇒ 升级为硬中止。理由：连续 3 次说明是系统性不一致（很可能就是 `contentHash` 漏盖的某项），继续打只会产生"两个人都觉得自己赢了"。

### 5.5 浮点不一致的检测与降级策略（先检测、不先上定点）

**分三级降级**：

| 级别 | 触发 | 动作 |
|---|---|---|
| L0 | 无 | — |
| L1（软） | 只有位置/速度超步长，离散事实全等 | 自动 `Resync`，继续对局；计入 `DesyncCount` |
| L2（硬） | 离散事实不等，或同局第 3 次 L1 | `Bye{reason=desync}`，HUD 说明"两端物理不一致，本局作废"，**不判任何人胜** |
| L3（不实现） | 上线后实测 L1 频繁（**阈值：同一局 > 2 次**） | **才**启动定点数改造。改造面 = `PoolSimulator` 全部算术 + `PoolMath`；按"重写内核"估价 |

**为什么不上来就定点**：

1. 内核**零三角函数**（实测），且除 `Sqrt` 外只有加减乘除 ⇒ 已经是"定点友好"的形态，真需要时改造路径清楚，但不是现在。
2. `PoolConstants.cs:18` 的类注释已经写明：「跨平台浮点确定性若在未来成为需求，再评估切换到 double 定长积分」—— 也就是说工程**刻意**把这个决定留给"需求出现时"。本卡的需求是"局域网双人"，**先验证需求是否真的咬人**。
3. 定点化会改掉 `RestSpeedThreshold`（`PoolSimulator.cs:114`）、`StoppedSpeedEpsilon`（`:91`）、`ContactArcErrorBound`（`:68`）等一整套阈值的量纲 ⇒ 35-C 调好的手感要**重新标定**。这是真实成本，不能白付。

**唯一"现在就做"的确定性修补**：`System.Random`（§5.6）。它不属于"浮点不一致"，它是**硬的分歧源**，且成本极低。

### 5.6 摆球 RNG：依赖了一项**没有契约**的实现细节（P0 级，必须先修）

**事实**：`PhysicsTestDrive.cs:2151` 是

```csharp
var rng = new System.Random(seed);
```

而工程**自己的规矩**在另外两个地方明确禁止这件事：

- `EightBallAi.cs:129-130`：「本层**自持**确定性 RNG（`Rng`，xorshift32），**绝不**用 `UnityEngine.Random` / `System.Random`」
- `EightBallAi.cs:636`：「⚠ **禁止**换成 `UnityEngine.Random` / `System.Random`（种子语义跨版本不保证一致）」

#### 5.6.1 实测：跑了**两个真实运行时**，逐位一致

工具：`Docs/Tools/rack_fingerprint.ps1`（只读；完整复刻 `EightBallRackPositions` 的取数顺序与 `RackSlot` 的几何算式；输出"槽位→球号"表 + 每颗球的 `float32` 位模式 + `rackDigest`）。

**这次不是"拿一个重实现去比"，是真的两个运行时各跑一遍**：

| 运行时 | 怎么跑到的 | 版本串 |
|---|---|---|
| **.NET Framework** | `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`（Windows PowerShell 5.1） | `.NET Framework 4.8.9345.0` |
| **.NET (CoreCLR)** | `pwsh`（PowerShell 7.6.6） | `.NET 10.0.12` |

**结果**：7 个种子（`0 / 1 / 7 / 42 / 12345 / -99999 / 2024`）的

- 槽位→球号表：**逐项相同**；
- 15 颗球的 30 个 `float32` 位模式（`x` / `y`）：**逐位相同**；
- `rackDigest`（1 mm 量化后 FNV-1a 32）：**逐个相同**；
- 全文内容行（113 行）`Compare-Object` ⇒ **零差异**；`FP`（内容 sha256）= `08da1941…` **两边一致**。

**范例（`seed = 42`，两个运行时并排，15×2 个位模式全部相同）**：

| 球 | `x` 位模式 | `y` 位模式 |
|---|---|---|
| 1 | `0xBEB3077F` | `0x3CEAE7D5` |
| 2 | `0xBEFF512E` | `0x00000000` |
| 3 | `0xBEFF512E` | `0x3D6AE7D5` |
| 4 | `0xBEB3077F` | `0xBCEAE7D5` |
| 5 | `0xBEFF512E` | `0x3DEAE7D5` |
| 6 | `0xBEE5E349` | `0xBCEAE7D5` |
| 7 | `0xBECC7564` | `0x3D6AE7D5` |
| 8 | `0xBECC7564` | `0x00000000` |
| 9 | `0xBEFF512E` | `0xBDEAE7D5` |
| 10 | `0xBEFF512E` | `0xBD6AE7D5` |
| 11 | `0xBEE5E349` | `0x3DB02DE0` |
| 12 | `0xBECC7564` | `0xBD6AE7D5` |
| 13 | `0xBE99999A` | `0x00000000` |
| 14 | `0xBEE5E349` | `0x3CEAE7D5` |
| 15 | `0xBEE5E349` | `0xBDB02DE0` |

（`0xBE99999A` 就是 `RackApexX = -0.30f`，`0x00000000` 是 `y = 0` —— 与源码里"`[4]` = 第三排正中、`y = 0`"的约定吻合，是一处顺手的内自检。）

**另做了一次算法同一性核对**（`-VerifyLegacy`）：用 mscorlib(1999) 的 Knuth 减法算法**独立重实现**，与真实运行时逐值对照 7 种子 × 24 次 `Next(...)`（`maxValue` 轮换 `{2,6,7,12,1000000}`）⇒ **0/7 不一致**。

#### 5.6.2 结论：**"两种实现逐位一致"这一档**

按"三种情况"口径落档：

> **逐位一致 ⇒ 风险低，但仍须登记"依赖运行时实现"。**

而且要把 premable 纠正掉：**"`System.Random` 在 .NET Framework 与 .NET Core 3.0+ 算法不同"这句话，对带种子实例不成立**。实测显示**带种子的 `System.Random` 25 年未变**；变的是**无参** `new Random()`（.NET 6 起换 xoshiro256\*\*）。本工程的摆球用的是**带种子**那条 ⇒ 不在那个变化面上。

**那为什么仍然要修（两个**今天**就成立的理由）**：

1. **它让一句对外承诺依赖没有契约的实现细节。** 35-C 规格写着 `rackSeed`「真随机并 `Debug.Log` 留痕，**可回读复现该局**」（`Docs/35-C_AI对战与8球规则_spec.md:222`；留痕点在 `PhysicsTestDrive.cs:400`）。玩家把日志里的种子贴回来、在**换过 BCL 或后端的构建**上复现 ⇒ 一旦变了，日志就成了假话。工程铁律是"不留假陈述"。
2. **工程内部自相矛盾。** `EightBallAi.cs:130` / `:636` 明令「**绝不**用 `System.Random`」，理由正是"种子语义跨版本不保证一致"；而摆球这条**更该守**的路径上偏偏用了它。**同一仓库两套口径**，这本身就够立项，与"今天会不会漂"无关。

**仍未验证的那一面（如实登记，不许拿"两运行时一致"冒充"全部一致"）**：

- 上面两个运行时都是 **Microsoft 的 BCL**。Unity 用的是 **Mono 的 BCL**（Mono 与 IL2CPP 共用同一套，故这两者互相同意，但**与 Microsoft 是否逐值相同未验证**）。
- 本机**没有 Mono**，本轮**不跑 Unity** ⇒ **测不了**。
- 仪器已就位：在 Unity 里（或任何 Unity 构建里）跑一次 `rack_fingerprint.ps1`，比对 `FP` 行即可钉死。**这是 P0 落地后的第一件事**。

#### 5.6.3 顺带实测出来的两条新事实（本机可复核）

**F1 —— `seed` 与 `-seed` 摆法完全相同。** 老算法第一步是 `Math.Abs(seed)`。实测 `seed=1` 与 `seed=-1` 的槽位表**逐位相同**（`4,3,7,15,8,10,12,1,14,5,2,6,11,13,9`），`rackDigest` 也相同。

- 后果：**有效种子空间只有 2^31，不是 2^32**。而 `ResolvedRackSeed` 来自 `System.Guid.NewGuid().GetHashCode()`（`PhysicsTestDrive.cs:397`），**可为负** ⇒ 生产确实在用这半个空间。
- 不是确定性缺陷，是"看着有 4G 种摆法、其实只有 2G 种"的**质量缺陷**，且极易被误诊成"两个不同种子给出同一个球堆，是不是 RNG 坏了"。
- 换 xorshift32（`(uint)seed` 直接做状态）后 `-1 ≠ 1` ⇒ **该缺陷自动消失**。这是 P0 的第三个理由，且**不依赖任何假设**。

**F2 —— `seed` 与 `seed+1` 的摆法完全不相关。** 实测 `42` vs `43`、`12345` vs `12346` 两张表没有任何共同结构。

- **好消息**：万一 RNG 真变了，握手期的 `rackDigest`（§4.2）**必然在开局前抓到**，不会"细微不同、打了半局才发现"。⇒ 握手校验从"锦上添花"升级为**必需**：它把一类灾难性故障降级为一次干净的拒绝开局。

#### 5.6.4 修法（P0，P2 的开工前置）

1. 把 `EightBallAi.Rng`（`EightBallAi.cs:638-655`，xorshift32）**提取**成 `Runtime/Physics/RackRng.cs`，供摆球路径用；
2. `PhysicsTestDrive.cs:2151` 改成 `var rng = new RackRng(seed);`；
3. `rng.Next(n)` 的**有偏取模保持原样**（改偏差 = 改摆球分布，属于另一件事，不在本卡）；
4. **补金向量判据**：对固定种子断言"槽位→球号"表 == 硬编码期望表。这条是**唯一能抓住"RNG 换实现"**的东西（`T65 (f)(g)` 只测自一致性；`Docs/测试数基线台账.md:334` 已实测"两条摆位路径的位置集合相同 ⇒ 既有 15 颗用例与种子无关"，即既有用例**对映射不敏感**）。选"槽位→球号表"而不是坐标位模式作金向量，因为它**纯整数、不依赖浮点**，不会因桌数据微调而假红。
5. 握手期 `rackDigest` 比对（§4.2）是**运行时**的第二道网。

**⚠ P0 的副作用（必须写进那一笔的提交说明）**：换 RNG 会**改变 `seed → 摆法` 的映射**（`Abs` 语义消失）。`T65(g)` 不会红也不该红；但 **金向量必须在 P0 之后取**（先取再换 = 自己造一条假红）；且 35-C 期间任何"按记录种子复现某局"的历史记录自 P0 起失效。

**⚠ 另一条纪律**：`rackDigest` / `checksum` 的 FNV-1a **只允许有工程内一处实现**。本次核对的副产物是：我先后用两份自己写的 FNV 算同一组数据，得到了**不同的 digest**（`uint32` 乘法溢出边界处理不同）⇒ **摘要算法本身也会被"各写一遍"引入漂移**。⇒ 摘要实现放 Runtime 一处、两端共用，并用金向量钉住（`T175`）。

**代价**：`RackRng` 提取 + 改一行 + 一条判据 + 一条摘要金向量 ≈ **半天到一天**。

#### 5.6.5 工具自身踩到的三个坑（登记，避免下一个人重踩）

写 `rack_fingerprint.ps1` 的过程中实测到三条**与算法无关、但会让脚本静默失真**的坑：

1. **无 BOM 的 UTF-8 会被 Windows PowerShell 5.1 按系统 ANSI 解码** ⇒ 中文字节被拆坏 ⇒ 报 `TerminatorExpectedAtEndOfString`。已改为 UTF-8 **with BOM**。
2. **`[BitConverter]::SingleToInt32Bits` 在 .NET Framework 4.8 上不存在**（该 API 是 .NET Core 2.0 / netstandard2.1 才加的）⇒ 已改用 `ToInt32(GetBytes(v), 0)`。⚠ 这条对**产品代码**也有意义：若 P0 的金向量判据用 `SingleToInt32Bits`，在 Unity 的 **.NET Standard 2.1** 兼容级别下可以编译；但若把 Api Compatibility Level 切成 ".NET Framework"，就会编译不过。
3. **`FP` 一开始把 `RUNTIME`/`PSVER` 两行也算进摘要了** ⇒ 两台机器、甚至同一台机器的两个运行时，`FP` **永远不同**，指纹等于作废。已改为只对**内容**取摘要，`RUNTIME`/`PSVER` 只作元数据打印。

> 这三条的共同点：**校验工具本身必须先被证明是对的**。与"摘要各写一遍会漂"是同一类错误。

### 5.7 完整快照（`Resync` 的载荷）——现有 API **结构上做不到**

**事实**：`PoolSimulator` 的状态**不止** `position` / `velocity`：

| 字段 | 位置 | 参与演化？ |
|---|---|---|
| `_balls[]`（`position`/`velocity`） | `:398` 只读暴露 | 是 |
| `_frozen[]` | `:208` **private** | 是（`Step()` 里 `_active[i] = !_frozen[i] && …`，`:851`） |
| `_capturedPocket[]` | `:211` **private** | 是（`IsBallInPlay` `:805` 与 `CapturedPocketIndex` `:813` 都读它） |
| `_vAvg[]` | `:214` **private** | 注释说明每子步开头重算（`:755-760`）⇒ 当前**不可观测**，但快照里带上更安全 |
| `TableAtRest` / `ConsecutiveRestFrameCount` | `:327` / `:330` | 是（`UpdateTermination` `:1847`） |
| 诊断计数（`_shotContactEvents` 等） | `:1993` 等 | 只影响 `RuleFacts` |

而 `LoadBalls`（`:656`）会把**所有**球的 `_frozen` 清 0、`_capturedPocket` 清 −1（`:676-677`）—— 这正是 `PlaceBall` 的注释（`:748-753`）警告过的"已入袋的球会复活"。

⇒ **`Resync` 不能只发 16 个位置+速度**。必须给内核加一对**加法式**接口（不动物理路径）：

```csharp
// PoolSimulator 新增（不改任何既有方法）
public struct SimSnapshot { /* 16×BallState + bool[16] frozen + int[16] capturedPocket + int consecutiveRest + bool tableAtRest */ }
public SimSnapshot CaptureSnapshot();
public void RestoreSnapshot(in SimSnapshot s);   // 逐字段覆盖，末尾不作废 TableAtRest（要恢复的就是它）
```

**判据**：`CaptureSnapshot()` → 跑 300 步 → `RestoreSnapshot(快照)` → 再跑 300 步 ⇒ 两次终态**逐位相同**（包括 `CapturedPocketIndex` 与 `IsBallInPlay`）。这条判据同时是"快照有没有漏字段"的**唯一**检验手段 —— 漏 `_frozen` 就会红。**注入必红**：把 `RestoreSnapshot` 里的 `_frozen` 拷贝删掉，判据必须红。

---

## 6. 回合与自由球

### 6.1 谁决定放球点

**决定**：**拿到自由球的那名玩家在自己屏幕上放；只传坐标；房主校验合法性。**

理由：让房主替客户端算落点等于让房主做一次拾取（相机/投影都在客户端），协议得把相机矩阵传过去 —— 荒谬。只传坐标是最小载荷。

**落地**：

| 端 | 动作 | 代码位置 |
|---|---|---|
| 放球方（本地） | 拖动/点击 → 物理坐标 → `CanPlaceCueBall(pos)` **只判不施** → 合法则**立即本地乐观放置**（手感零延迟）并发 `CueBallPlaced{x,y}` | `CanPlaceCueBall`（`PhysicsTestDrive.cs:2251`）是**只读**的（只构造 occupied 表，不改状态，`:2258`）⇒ 可以安全拿来"预览判定" |
| 房主 | 收到 `CueBallPlaced` ⇒ `CanPlaceCueBall` 复核 ⇒ 通过则 `TryPlaceCueBall`（`:2312`）并广播；不通过则 `PlacementRejected` | **注意**：房主若自己就是放球方，则跳过网络直接 `TryPlaceCueBall` |
| 对方 | 收到 `CueBallPlaced` ⇒ `TryPlaceCueBall(x,y)`（**用同一对纯函数**，故必过） | `TryPlaceCueBall` 成功才退出待放态（`:2326`） |

**乐观放置的回滚问题（必须显式处理）**：客户端先本地 `TryPlaceCueBall` ⇒ 若房主后来拒绝，客户端状态已经变了。处置：

- 协议上**房主拒绝是极罕见的**（同一对纯函数、同一份桌数据、`contentHash` 已核对）；
- 因此**允许乐观放置 + 被拒则发 `Resync`**，而不是做"放球事务"。选"简单 + 罕见路径走重同步"，不选"为一条几乎不会走的分支写回滚"。
- 判据：注入一个"房主与客户端桌数据不一致"的夹具 ⇒ 客户端必须真的收到 `PlacementRejected`/`Resync` 并对齐，**不许**静默继续。

### 6.2 犯规与 kitchen 在哪端判

| 判定 | 在哪端 | 依据 |
|---|---|---|
| 是否犯规 / 下一杆谁打 / 自由球形态 / 是否重摆 / 是否黑 8 复位 / 是否结束 | **两端各自判**，冲突以房主为准 | `EightBallRules.Judge`（`EightBallRules.cs:329`）纯函数；`RuleFacts`（`:231`）只由内核只读出口算 |
| kitchen 约束（头区自由球只能放线后、只能往线前打） | **两端各自判**，以房主为准 | 放球：`TryPlaceCueBall` → `CanPlaceCueBall`（`:2297-2301`）里走 `IsPlacementLegalInKitchen`（`PoolSimulator.cs:528`）；击球方向：`Launch`（`PhysicsTestDrive.cs:2416-2421`）走 `IsBreakShotForwardEnough` |
| 施加（切回合 / 发自由球 / 置 `MatchOver`） | **房主决定，双方执行** | `ApplyVerdict`（`:831`）两端都调；房主的 `ruleDelta` 是权威版本 |

**为什么"两端各自判"是对的（而不是客户端上报结果）**：客户端上报"我赢了"是**零成本作弊**。锁步的整个价值就是"结果由输入推出，不由声称推出"。协议里**没有**任何"客户端声明状态/结果"的消息 —— 这是设计上刻意的空缺，不是遗漏。

### 6.3 一个必须点名的语义陷阱：`ShotTurn.Ai` 在线下不是 AI

`ShotTurn`（`EightBallRules.cs:20-24`）只有 `Player` / `Ai` 两个值。**联机时"对手"这个位置由远端人类占据 ⇒ 那个人类在代码里就是 `ShotTurn.Ai`。**

这**不是**改名问题（`Player`/`Ai` 被 `ApplyVerdict`、`SyncGroupsForTurn`、UI、AI 全部依赖），但它带来两处**必须显式加固**的地方：

1. `IsAiTurn`（`PhysicsTestDrive.cs:749`）在线下的正确含义是**"不是我的回合"**。现有输入门控已经是这个用法（`PointerAimController.cs:135`、`PointerPowerController.cs:124`）⇒ **可以原样复用**。
2. `TickAiTurn`（`:1358`）会在 `IsAiTurn` 为真时**真的让 AI 出一杆**（`:1336` `AiTurnArmed`）⇒ **必须闸门**（§9.6）。

### 6.4 ⚠ `AiOpponentEnabled` 一旗三用 —— 联机的地基裂缝

**事实**：这一个 `bool`（`:764`）同时承担三件互不相同的事：

| 用途 | 位置 | 需要的取值（练习 / AI 对战 / **联机**） |
|---|---|---|
| ①「要不要强制自由球」（练习模式不判） | `:857` `bool practiceNoHandBall = !AiOpponentEnabled;` | false / true / **true** |
| ②「这局结束了要不要置 `MatchOver`」 | `:896-904` `if (!AiOpponentEnabled) { return; } MatchOver = true; …` | false / true / **true** |
| ③「AI 要不要动 + 玩家输入要不要让位」 | `:1218`、`:1336`、`PointerAimController.cs:135`、`PointerPowerController.cs:124` | false / true / **一半一半** |

**联机需要的组合是 `①=true, ②=true, ③(输入让位)=true, ③(AI 动)=false` —— 这个组合用现在的单个 bool 表达不出来。**

- 若联机把 `AiOpponentEnabled` 设成 `false`（为了关掉 AI）：**`MatchOver` 永远不会置真**（`:896` 直接 return）⇒ 一局打完没有胜负、没有结算面板、`_shotInFlight` 相关语义全乱。
- 若设成 `true`（为了能结算）：`TickAiTurn`（`:1358`）会替远端人类出杆 ⇒ 对局失控。

**这是本方案最可能翻车的一处。** 必须拆成两个字段：

```csharp
public bool HasOpponent        { get; private set; }   // ① ② 与"输入是否让位"读它
public bool AiControlsOpponent { get; private set; }   // 只给 TickAiTurn / AiTurnArmed 读
// AiOpponentEnabled 保留为派生属性（= HasOpponent && AiControlsOpponent 的旧语义只用于练习判定）
```

拆分后 **7 个读点**必须逐处改成对应语义（`grep -n "AiOpponentEnabled" Runtime/` 找全）：
`:380`、`:764`、`:857`、`:896`、`:1172`、`:1176`、`:1198`、`:1218`、`:1336`、`PointerAimController.cs:135`、`PointerPowerController.cs:124`。

**判据**：一张 3×3 真值表（练习 / AI 对战 / 联机 × ①②③ 的期望值）写成 `[Test]`，逐格断言。**注入必红**：把 `:896` 改回 `if (!AiOpponentEnabled) return;` ⇒ 联机格的"能结算"必须红。

> ⚠ 本节与 §9.6 是同一件事的两面：§6.4 是**语义**（字段该表达什么），§9.6 是**可达性**（AI 代码路径必须不可达）。两者都要做。

---

## 7. 断线 / 重连

### 7.1 缓存粒度：**每杆结算后一帧**

房主维护一个**长度 1 的滚动快照**：`(seq, snapshotAfter, ruleStateAfter, playerGroup, pendingPlacement, ballInHand, launchCount)`。

- **为什么粒度是"杆"而不是"步"**：锁步的同步单位就是杆（§2.2）。每步存一次 = 120 次/秒 × 180 B = 21 KB/s，且没有对应用途。
- **为什么要 `launchCount`**：它是 `PoolAudio`（击杆声）与 `PoolVfxView`（击杆特效）的单调计数器（`:473-474` 注释），重连后必须对齐，否则重连那一下会"叮"一声或者静默（都是可见缺陷）。
- **为什么长度只要 1**：`seq` 是单调的，重连时要么"最后一杆已结算"（用这 1 个快照），要么"最后一杆还在飞"（那个快照就是"这一杆之前"，需要把这一杆重放一次 —— 见 7.2）。

### 7.2 重连时从哪一杆重放

```
Client 重连 → Hello{lastSeq = 我最后收到的 seq}
Host: if (lastSeq == 我的 seq)          → 直接用滚动快照发 Resync（不重放）
      else if (lastSeq == 我的 seq - 1) → 发 Resync（seq-1 的快照）+ 重放最后那一杆的 ShotAccepted
      else                              → 差距 > 1 杆 ⇒ 快照取"当前"，不重放（旧杆的事件流已丢弃，重放也没人看）
```

- **不需要"从开球重放整局"**：因为每杆结束都是静态（`TableAtRest`），状态是自足的。这是把锁步做成"杆级同步"换来的最大红利 —— 重连成本与已打杆数**无关**。
- 客户端收到 `Resync` 后**必须**把本地 `_shotInFlight`（`:728`）置 false、`_accumulator`（`:199`）清零（快手路径在 `:2452` 做过同样的事），否则 `SettleShotIfAtRest`（`:1123`）会在一帧后被误触发，凭空结算出一杆。

### 7.3 超时阈值

| 计时 | 阈值 | 依据 | 谁计时 |
|---|---|---|---|
| `Ping` 间隔 | 1 s | LAN 的 RTT < 5 ms，1 s 足够稀疏 | 双方 |
| 心跳缺失判掉线 | **5 s**（5 次未回） | 容忍一次 Unity 主线程卡顿（切场景 / GC） | 双方 |
| 单杆思考时限 | 警告 **30 s**，判负 **90 s** | 8 球一杆通常 < 20 s；90 s 足够"想清楚"，也够折磨人的对手不至于卡死对局 | **只看房主的钟** |
| 重连等待 | 60 s | 超时则房主 `Bye{reason=peer-gone}` 并**自己判胜** | 房主 |
| `Resync` 重试上限 | 2 次 | 第 3 次直接 `Bye{reason=desync}` | 客户端发起，房主核 |

**时间同步**：**不需要**。所有时限判定只用**房主自己的 `Time.realtimeSinceStartup`**，客户端从不做超时判定（它只显示房主广播的剩余时间）。这直接绕开了"两台机器时钟不同"的整个问题域。代价：客户端看到的是**结算后**的倒计时（约 1 个 RTT 的偏差），在 LAN 上是 5 ms 级，无人能感知。

---

## 8. 分阶段落地

> 每阶段都**可独立验收**，且 P0 / P0-b / P1 完全不联网。
> **顺序按主理人裁定**：P0 → P0-b → P1 → P2-a/c 为第一张卡；**P0 单独一笔提交**（好回退）；**P0-b 与 P1 必须串行**（都改 `PhysicsTestDrive.cs`）。
> **判据编号从 `T170` 起**（避开 144–169 已用段），最终分配见 §12.2 Q8，须与 quality-lead 对账。

### P0 — 摆球 RNG（**单独一笔提交**，最先做）

**改动**：新增 `Runtime/Physics/RackRng.cs`（把 `EightBallAi.cs:638-655` 的 xorshift32 提取出来）；`PhysicsTestDrive.cs:2151` 改用它；新增"槽位→球号"金向量判据；新增摘要金向量（`rackDigest` 的 FNV-1a 只允许一处实现，§5.6.4）。

**为什么必须先单独做**：它**改变 `seed → 摆法` 的映射**（`Abs(seed)` 语义消失）。若与 P0-b/P1 混在一笔提交里，一旦摆球相关的历史证据失效，分不清是哪一处引起的。

**可判定验收**：

| 编号 | 判据 | 形式 |
|---|---|---|
| **T172** | **槽位→球号金向量**：固定 8 个种子 ⇒ 表 == 硬编码期望表 | `[Test]`。这是**唯一能抓"RNG 换实现"**的判据（`T65(f)(g)` 只测自一致性、对映射不敏感，见 `Docs/测试数基线台账.md:334`）。选整数表而非坐标位模式：**纯整数、不依赖浮点，不会因桌数据微调而假红** |
| **T175**（建议） | **摘要金向量**：`FnV1a32` 对已知输入 == 硬编码期望值 | `[Test]`。理由是本轮实测发现的：我先后用两份自己写的 FNV 算同一组数据**得到了不同的 digest**（`uint32` 乘法溢出边界处理不同）⇒ **摘要算法本身也会被"各写一遍"引入漂移** |
| P0-A3 | `seed` 与 `-seed` **不再**给出同一摆法 | `[Test]`：`seed=1` 与 `seed=-1` 的槽位表 **必须不同**（现状是相同，见 §5.6.3 F1，实测） |
| P0-A4 | 既有判据不因换 RNG 而变红（且**不许**为求绿去改它们） | 全量 EditMode `failed=0`；`T65(f)(g)` 应保持绿（它们只测自一致性） |

**⚠ 提交说明必须写**：本笔**改变 `seed → 摆法` 的映射**；35-C 期间任何"按记录种子复现某局"的历史记录自本笔起失效。

### P0-b — `AiOpponentEnabled` 拆三个语义位（**与 P1 串行，不得并行**）

**改动**：`HasOpponent` / `AiControlsOpponent` 两个字段 + `AiOpponentEnabled` 转派生属性；7 个读点逐处改成对应语义（`:380` `:764` `:857` `:896` `:1172` `:1176` `:1198` `:1218` `:1336` + `PointerAimController.cs:135` + `PointerPowerController.cs:124`）。

**可判定验收**：

| 编号 | 判据 | 形式 |
|---|---|---|
| **T176**（建议） | **3×3 真值表**（练习 / AI 对战 / 联机 × ①强制自由球 ②置 `MatchOver` ③AI 动 / 输入让位）逐格断言 | `[Test]`。联机格的 ② 必须为真（现状 `:896` 直接 return ⇒ **一局打完永远不结束**） |
| P0b-A2 | **注入必红**：把 `:896` 退回 `if (!AiOpponentEnabled) return;` ⇒ T176 的联机格必须红 | 注入 + 全量 + 还原后回读 sha1 |
| P0b-A3 | 练习 / AI 对战两种既有模式行为**逐位不变** | `T143`–`T147` 全绿 |

### P1 — 输入抽象（不联网也能验收）

**改动**：新增 `ShotCommand.cs`；`PhysicsTestDrive.Launch()` → `Launch(in ShotCommand)`；新增 `RequestLaunch(ShotCommand)`；5 个出杆点改造（§3.1 的 L1–L5）；70 处测试调用点走 `LaunchFromFields`；`spinQ` 恒零校验。

**可判定验收**：

| 编号 | 判据 | 命令 / 形式 |
|---|---|---|
| **T170** | **`Launch(ShotCommand)` 是唯一出杆入口** | 源码守卫 `[Test]`：`StripCommentLines` 后 `"public Vector2 Launch("` 出现恰 1 次；`Launch(` 的出现位置 ⊆ 显式 (文件, 期望次数) allow-list（§3.8） |
| **T170**（注入） | **注入第二入口必红** | `P1-INJ-1`（加回无参 `Launch()`）⇒ 红；`P1-INJ-2`（`CueStickController` 绕过 `RequestLaunch`）⇒ 红 |
| **T171** | **量化前移**：`ShotCommand.Aim(180f, 0.5f)` 往返无损 | `[Test]`：`aimMilliDegrees == 180000 && powerQ == 500`；且 `Aim(180.0004f,0.5f).aimMilliDegrees == 180000` |
| **T177**（建议） | **`spinQ != 0` 被拒且留痕 + 可见原因** | `[Test]`：`Launch(cmd with spinQ=1)` ⇒ 返回 `Vector2.zero` 且 `RefusedSpinCount++`，并走 `MatchTurnNotice` 同族出口给出可见原因（**不许静默**） |
| **T178**（建议） | **预览不再驱动物理** | `[Test]`：写 `drive.aimDegrees/ power` 后不开火 ⇒ `Simulator.Balls` 逐位不变；再 `Launch(ShotCommand.Aim(...))` 才动 |
| P1-A4 | **`T145` 全绿**（含 ④"AI 出杆 vs 手工出杆事件流逐条相同"） | `filter T145` ⇒ `2/2`（`T145` + `T143`，同现有 filtered 流程） |
| P1-A5 | **既有测试数不得下降** | 全量 EditMode，`failed=0` 且总数 ≥ 改动前（工程标准 G2：不得以删测试求绿） |

**验收证据路径**：`Assets/_Project/Tests/EditMode/PhysicsTestDriveLaunchStateTests.cs`（新增用例，编号见 §12.2 Q8，**须与 quality-lead 对账避免重号** —— 历史上 `T144` 已被占用、`T145` 是避让后的结果）+ 全量结果 XML 留到仓外。

### P2 — 直连 TCP

**改动**：新增 `Billiards.Net` asmdef（`references: ["Billiards.Runtime"]`）；`ITransport` 接口 + `TcpTransport` + `InMemoryTransport`（测试用）；帧编解码；`OnlineSession`；`contentHash`（字段清单见 §4.3）。⚠ P0（`RackRng`）与 P0-b（字段拆分）**已在前置阶段完成**，本阶段不重复。

**为什么 TCP 而不是 UDP**：

1. **回合制 + 每秒最多 1–2 条消息**：UDP 的全部优势（低延迟、可丢）在这里无价值。
2. **丢一条 `Shot` = 对局卡死**。用 UDP 就必须自己在应用层做序号 + 确认 + 重传 —— 那就是**重新实现一遍 TCP，而且只实现得比 TCP 差**。
3. **有序性是刚需**：`Shot(seq=n)` 必须先于 `Shot(seq=n+1)` 被处理；TCP 免费给你。
4. **队头阻塞无意义**：这一层只跑控制流，没有需要抢在别人前面到达的实时流。

**端口策略**：房主 `TcpListener` 绑 `0.0.0.0:7777`；被占用则依次试 `7778 / 7788 / 7799`，并把实际端口显示在房主的 UI 上（**不许**静默换端口让客户端连不上）。客户端输入 `IP:port`，默认端口 7777。

**可判定验收**：

| 编号 | 判据 | 形式 |
|---|---|---|
| **T174** | **协议可在 EditMode 端到端跑通，不碰真网络** | `[Test]`：两个 `PhysicsTestDrive` + 两个 `OnlineSession` + 一对 `InMemoryTransport`；A 出杆 ⇒ 两端 `ShotResult.checksumAfter` **相等**且 `ruleDelta` 逐字段相等 |
| ~~P2-A2~~ | **摆球金向量已前移到 P0（`T172`）** | 不在此阶段重复；本阶段只需保证 `contentHash` 独立于它 |
| **T179** | **`contentHash` 覆盖 & 不覆盖（两向）** | `[Test]`：① 改 `TableConfig` 任一 `openingRadius` 第 4 位小数 ⇒ 摘要**必须变**；② **只改 `generatedAtUtc` / `captureRadius` / `captureNote` ⇒ 摘要必须不变**（缺②那张"不盖"表就只是注释，§4.3-D）；③ `pockets` 列表重排 ⇒ 摘要必须变 |
| **T180** | **帧编解码往返无损** | `[Test]`：1000 条消息 `Encode→Decode` ⇒ 字段逐一相等；含边界值（`i16` 极值、`powerQ = 0/1000`、`spinQ = ±1`） |
| P2-A5 | **注入必红** | `P2-INJ-2`：帧里把 `powerQ` 写成 `u8` ⇒ `T180` 红；`P2-INJ-3`：把 `generatedAtUtc` 塞进 `contentHash` ⇒ `T179②` 红 |

**手工验收**（真两台机器）：`Docs/Reports/35-D_联机实机清单.md`（由 quality-lead 出，本方案只给条目口径：开局、互相看得见对方的杆、进袋同步、自由球放置、打完一局胜负一致、拔网线重连）。

### P3 — 校验和与重同步

**改动**：`PoolSimulator.CaptureSnapshot/RestoreSnapshot`（§5.7）；`ShotResult` 全量比对；`Resync` 流程；`DesyncCount` / `DesyncMaxDeltaMm` / `PreLaunchMathMismatchCount` 出口。

**可判定验收**：

| 编号 | 判据 | 形式 |
|---|---|---|
| **T173** | **快照不漏字段** | `[Test]`：`Capture → 300 步 → Restore → 300 步` ⇒ 终态逐位相同（含 `IsBallInPlay` / `CapturedPocketIndex`）。**注入必红**：删 `_frozen` 拷贝 ⇒ 红 |
| P3-A2 | **软分歧被检出并自愈** | `[Test]`：客户端 `PlaceBall(3, pos+2mm, v)` 人为注入分歧 ⇒ `DesyncCount == 1`、走过 `Resync`、终态与房主相同、`MatchOver == false` |
| P3-A3 | **硬分歧必中止** | `[Test]`：把客户端 `ruleDelta.nextTurn` 篡改 ⇒ 必须 `Bye{reason=desync}`，且**不**判任何人胜 |
| P3-A4 | **不静默** | `[Test]`：任何 `Bye` / `ShotRejected` / `PlacementRejected` ⇒ HUD 有可见原因（读那个 reason 的可判定出口，**不读像素**） |
| P3-A5 | **良性噪声不误报** | `[Test]`：同进程同构建，连打 30 杆 ⇒ `DesyncCount == 0`；同时报告 `DesyncMaxDeltaMm` 的实测最大值（**这条是"1 mm 步长够不够"的唯一证据**） |

### P4 — 重连 / 再来一局

**改动**：滚动快照 + `Hello{lastSeq}` 重连路径；`RematchRequest` / `MatchStart` 复用；超时判负。

**可判定验收**：

| 编号 | 判据 | 形式 |
|---|---|---|
| P4-A1 | **重连后状态全等** | `[Test]`：打 5 杆 → 断开传输 → 重连 → 断言完整状态摘要（`Balls` 逐位 + `ruleState` + `CurrentTurn` + `PendingCueBallPlacement` + `LaunchCount` + `ShotInFlight == false`）与断开前相同 |
| P4-A2 | **重连不产生幽灵结算** | `[Test]`：重连后推进 1 s ⇒ `SettleShotIfAtRest()` 必须返回 `false`（`_shotInFlight` 已复位） |
| P4-A3 | **超时判负可判定** | `[Test]`：注入可控时钟，模拟 90 s 无 `Shot` ⇒ 房主置胜负 + `Bye{reason=timeout}`；30 s 时必须有警告出口 |
| P4-A4 | **再来一局不复用旧种子** | `[Test]`：`RematchRequest` 后 `ResolvedRackSeed`（`:82`）必须变化（对齐 `StartMatch` `:1191` 的既有语义） |

---

## 9. 风险与缓解

按"会不会让这张卡失败"排序，不按"听起来严不严重"排序。

### 9.1 【中】摆球依赖 `System.Random`（实测：**两个真实运行时逐位一致，但契约是假的**）—— 见 §5.6
**实测（本机，两个真实运行时各跑一遍）**：`.NET Framework 4.8.9345.0`（Windows PowerShell 5.1）与 `.NET 10.0.12`（PowerShell 7）—— 7 个种子的槽位表、**15×2 个 `float32` 位模式**、`rackDigest`、内容 sha256 **全部逐位相同**；另用 mscorlib(1999) 算法独立重实现对照，**0/7 不一致**。
⇒ **带种子的 `System.Random` 25 年未变**（变的是**无参**构造函数，.NET 6 起 xoshiro）。故定级 **【高】→【中】**，且**不是"马上就要炸"的风险**。
**仍需修的两个理由**（都不依赖"它会漂"）：① `35-C` 规格承诺的"日志种子可回读复现该局"（`Docs/35-C_AI对战与8球规则_spec.md:222`）建立在无契约的实现细节上；② 工程自己在 `EightBallAi.cs:130/:636` 明令禁用 `System.Random`，这里自相矛盾。
**额外实测缺陷**：`seed` 与 `-seed` 摆法相同（`Math.Abs`）⇒ 有效种子空间只有 2^31（§5.6.3 F1）。
**缓解**：P0 换 xorshift32 + 槽位表金向量 + 握手 `rackDigest`（后者把"万一"降级为一次干净的拒绝开局，§5.6.3 F2）。
**未验证面（如实登记，不许拿"两运行时一致"冒充"全部一致"）**：上面两个都是 **Microsoft 的 BCL**；Unity 用的是 **Mono 的 BCL**（Mono 与 IL2CPP 共用同一套）。本机无 Mono、本轮不跑 Unity ⇒ **测不了**。仪器已就位：`Docs/Tools/rack_fingerprint.ps1`（已修好 PS 5.1 兼容），在 Unity 里跑一次、比对 `FP` 行即可钉死 —— **P0 落地后的第一件事**。

### 9.2 【高】`AiOpponentEnabled` 一旗三用，联机组合表达不出来 —— 见 §6.4
**缓解**：拆成 `HasOpponent` / `AiControlsOpponent`，7 个读点逐处改，3×3 真值表判据。**这是唯一一个"能写出代码但会静默错"的风险**（症状是"打完一局没有结算"）。

### 9.3 【中】跨机浮点漂移（FMA 收缩 / libm）
**缓解**：
- 三角函数收口 + `v0` 由房主下发（§5.3）⇒ 消除 libm 差异；
- **FMA 收缩**是剩下的唯一真风险（IL2CPP 优化可能把 `a*b+c` 熔成一条 FMA，改变末位）。**不关优化**（性能代价不可接受），靠 per-shot checksum 检出（§5.2）。
- 判据：P3-A5 的 30 杆零误报 + `DesyncMaxDeltaMm` 实测余量。
- **知识缺口（如实登记）**：本方案**没有实测**过 Windows-x64（Mono/IL2CPP）× macOS-arm64 的实际漂移量。P2 的真机验收必须**跨平台跑一次**才能给这条风险定性；两个 Windows 机器测不出来。

### 9.4 【中】NAT / 防火墙
**缓解**：只做 LAN / 直连，**不做 NAT 穿透**。具体：
- 房主绑 `0.0.0.0`（不是 `127.0.0.1`）；
- Windows Defender 首次监听会**弹窗**，实机清单里必须有一条"确认允许"，并在 UI 里写明"若对方连不上，先检查防火墙"；
- 客户端连接超时给**明确文案**（区分"连不上"与"对方拒绝"），**不许**只转圈；
- 不做 UPnP / 不打洞 —— 那是另一张卡。

### 9.5 【中】时间同步
**缓解**：见 §7.3 —— **根本不做时间同步**：所有时限只用房主的钟，客户端只显示房主广播的值。风险降级为"显示偏差 ≤ 1 RTT"。

### 9.6 【高】作弊面 + AI 路径必须不可达

**作弊面（逐个封）**：

| 攻击 | 封法 |
|---|---|
| 发一个超范围的力度 | 房主钳 `powerQ ∈ [0,1000]`，超范围 ⇒ `ShotRejected{power-range}` |
| 发一个 `spinQ != 0` | 拒收（§3.5） |
| 不是自己的回合也发 `Shot` | 房主核 `CurrentTurn` ⇒ `ShotRejected{not-your-turn}` |
| 发一个假的 `checksumBefore` | 房主核 ⇒ `ShotRejected{checksum-before}` |
| 把白球放到袋口/别人球上 | 房主 `CanPlaceCueBall`（`:2251`）复核 ⇒ `PlacementRejected` |
| 声称自己赢了 | **协议里没有这条路**（§6.2）：没有"客户端声明状态/结果"的消息 |
| 重复发 `Shot{seq}` 刷杆 | 房主核 `seq` 单调递增，重号直接丢弃 |
| 发超大 `seq` 卡死 | `u16` + 房主核 `seq == 期望值` |

**AI 必须不可达（三道闸门 + 判据）**：

| 闸门 | 位置 | 动作 |
|---|---|---|
| G-AI-1 | `AiTurnArmed`（`:1332`，getter 里 `:1336`） | 追加 `&& AiControlsOpponent` |
| G-AI-2 | `TryLaunchAiShot`（`:1304`） | 首行加 `if (!AiControlsOpponent) return Vector2.zero;`（它不是 `private`，外部可调） |
| G-AI-3 | `SetAiOpponentEnabledForDiagnostics`（`:1172`） | 联机会话期间**拒绝**改这个开关（否则判据/调试代码能把 AI 打开） |

**判据（建议编号 `T181`）**：`[Test]` 在联机会话夹具里 `SetTurn(ShotTurn.Ai)` + `Advance(1f)` × 60 ⇒ 断言 `AiLaunchCount`（`:688`）**不变**、`AiPhase == AiShotPhase.None`（`:716`）、`AiTurnYieldedCount` 不变。**注入必红**：删 G-AI-2 那一行 ⇒ 必须红（否则说明 AI 真的能在线下出杆）。

### 9.7 【低】两驱共享静态诊断口
`SmokeProbe.ReportPhysicsSteps(TotalSteps)`（`PhysicsTestDrive.cs:1964`）是**全局静态**：同进程两个 `PhysicsTestDrive` 会互相覆盖。`T145` 已经在用两个驱动器（`:1990-1999` 的 `drive` / `goManual`）且没受影响，但**新的双端判据**若依赖这个口会串。处置：联机判据**只读驱动器自身的 `TotalSteps`**（`:1992`），不读 `SmokeProbe`。

### 9.8 【低】渲染/音频/UI 在两端不一致
不影响正确性（都从同一 `Balls` 派生），但 `PoolAudio` 的事件 drain 语义（`DrainContactEvents` `:2017`，被音频独占）在两端会产生不同时序的**声音**。这属于"观感差异"，明确**接受**（不为此做音频同步）。

---

## 10. 本方案会破坏的既有假设（必读）

**这一节是本方案最应该被评审的部分。** 每一条都是"现在成立、改完就不成立"的事实。

| # | 现在的假设 | 证据 | 为什么会被破坏 | 谁受影响 |
|---|---|---|---|---|
| B1 | **整局只有一个真源：`PhysicsTestDrive` 的那一份 `_simulator`** | `PhysicsTestDrive.cs:222` `Simulator`、`:793` `_playerGroup`、`:746` `CurrentTurn` 等全部是单实例状态 | 联机后有**两份**状态（房主 / 客户端），且需要**权威**概念 | §2.1 的权威表；§5.4 的胜负规则 |
| B2 | **`seed` 能决定球堆** | `PhysicsTestDrive.cs:2131` `EightBallRackPositions` + `:2151` `new System.Random(seed)` | 单个构建内**成立**（实测：与 mscorlib 算法逐值相同，§5.6.1），但**没有契约**：换 BCL / 换后端就会变；且 `seed` 与 `-seed` 同摆法（§5.6.3 F1）。**35-C 那句"可回读复现"（`spec:222`）因此是无保证的承诺** | 摆球、`T65 (f)(g)` 的"可复现"论证、日志复现 |
| B3 | **`aimDegrees` / `power` 就是"这一杆"** | `:2411-2412` `Launch()` 只读这两个字段 | 改成 `ShotCommand` 后它们只是**预览状态** | 6 处预览写点（§3.1）、`CueStickController.cs:126` 的后拉量、调试叠加层 `:2930` |
| B4 | **`ShotTurn.Ai` 意味着"有 AI 在打"** | `EightBallRules.cs:20-24`；`:749` `IsAiTurn`；`:1358` `TickAiTurn` | 联机时它是**远端人类** | `TickAiTurn`、`AiTurnArmed`、`ApplyVerdict` 的 `SyncGroupsForTurn` |
| B5 | **`AiOpponentEnabled` 意思是"有没有对手"** | `:857` / `:896` / `:1218` / `:1336` 四个语义不同的读点 | 需要"有对手但不由 AI 打"这个组合（§6.4） | 结算管线（`:896` 会让联机永远不结束） |
| B6 | **`PoolSimulator` 的状态 = 16×(位置, 速度)** | `BallState.cs:12-19` 只有两个字段 | 实际还有 `_frozen`(`:208`) / `_capturedPocket`(`:211`) / `_vAvg`(`:214`) / `ConsecutiveRestFrameCount`(`:330`) | `Resync` 的载荷；`LoadBalls`(`:656`) **不能**用来恢复状态 |
| B7 | **`Launch()` 可以随处调用** | Runtime 5 处 + 测试 70 处 | 单入口后只允许 4 个调用点 | §3.8 的 allow-list 守卫 |
| B8 | **物理推进节奏与结果无关这件事没被写下来** | `Advance()` `:1932` 的掉帧保护 `:1942-1947` 会丢累计 | 现在**必须**显式成立（否则锁步的整个前提没了） | 本文 §5.1 第 5 行；真机若出现"两端杆数不同但状态相同"要能立刻归因 |
| B9 | **三角函数的实现口径不重要** | `PoolMath.cs` 只有 `Sqrt`/`Atan2`；`CueVelocityFor`(`:537-542`) 直接 `Mathf.Cos/Sin` | R5-3（`Docs/工程标准.md:167`）要求单一口径；跨机时它变成一等公民 | `PoolMath` 扩 `Sin/Cos`；或走 §5.3 的"`v0` 由房主下发" |
| B10 | **`MatchOver` 只能被"开新局"清掉** | `:1185` / `:1202`（`StartMatch` / `EndAiMatchSession`） | 联机加了 `Bye` / 断线 / 重连三条新出口，**每一条都要显式决定要不要清 `MatchOver`** | `:2377` 那道"本局已结束绝不再出杆"的闸门 |
| B11 | **`com.unity.multiplayer.center` 只是个待删的模板包** | `Packages/manifest.json:8` | 一旦有人以为"这不是有联机包吗"，就会去用它的向导装 NGO —— 与本方案路线冲突 | 建议：这张卡开工时**顺手删掉**这个包（本来就在待清清单上） |

---

## 11. 工作量分级

按"半天 / 一天"粒度给区间。**不含**真机联调与返工缓冲。

| 阶段 | 内容 | 估价 |
|---|---|---|
| **P0（前置，单独一笔提交）** | `RackRng` 提取（`EightBallAi.cs:638-655` → 新文件）+ 改 `PhysicsTestDrive.cs:2151` + 槽位表金向量（`T172`）+ 摘要金向量（`T175`） | **0.5–1 天**（多出来的半天是"摘要只允许一处实现 + 用金向量钉住它"，本轮实测证明摘要自己也会被各写一遍引入漂移） |
| **P0-b** | `HasOpponent` / `AiControlsOpponent` 拆分 + 7 读点 + 3×3 真值表判据（`T176`） | **0.5–1 天**（读点散，容易漏） |
| **P1** | `ShotCommand` + `Launch(cmd)` + `RequestLaunch` + 5 个出杆点 + 70 处测试改签 + allow-list 守卫（`T170`）+ 量化往返（`T171`）+ 2 条注入 | **1–1.5 天**（改签是机械活，但 70 处要逐个 Review） |
| **P2-a** | asmdef + `ITransport` + 帧编解码（`T180`）+ `InMemoryTransport` + `contentHash`（`T179`） | **1–1.5 天** |
| **P2-b** | `TcpTransport` + 房主/客户端 `OnlineSession` + 端口策略 + 错误文案 | **1–1.5 天** |
| **P2-c** | 双端 `InMemoryTransport` 端到端判据（`T174` + 注入） | **0.5–1 天** |
| **P3** | `CaptureSnapshot`/`RestoreSnapshot` + `ShotResult` 全量比对 + `Resync` + 3 个诊断出口 + 5 条判据（含 `T173`） | **1.5–2 天**（快照那一对接口最容易漏字段） |
| **P4** | 滚动快照 + 重连路径 + 超时判负 + `Rematch` + 4 条判据 | **1–1.5 天** |
| **UI（最小）** | 房间入口（输入 IP / 显示本机 IP 与端口）+ 连接中 / 被拒 / 断线 / 重同步四态提示 | **1–1.5 天**（**必须**做：不许静默是本工程的铁律，而联机的失败态比单机多得多） |
| **合计（不含真机）** | | **≈ 8.5–12 天** |
| **真机联调（不含在内）** | 两台 Windows + ≥1 次跨平台（若要做跨平台） | **2–3 天**（历史上联机类改动的真机返工率最高） |

**切卡（按主理人裁定）**：
- **第一张**：P0（单独一笔提交）→ P0-b → P1 → P2-a/P2-c。**P0-b 与 P1 串行**（都改 `PhysicsTestDrive.cs`，同一个函数被两个改动同时碰则出问题分不清是谁的）。
- **第二张**：P2-b（真 socket）+ P3 + P4 + 最小 UI。
- 理由：P0/P0-b/P1 的错误会污染后面的一切，先让它们在**没有网络噪声**的环境里绿。

---

## 12. 待主理人裁定 / 已裁定 / 待定位

### 12.1 已裁定（主理人，本文据此更新）

| # | 事项 | 裁定 |
|---|---|---|
| D-Q1 | `Billiards.Net` 独立 asmdef | **批准**。测试 asmdef 的 `overrideReferences` 跟着改，改完先跑全量确认没漏 |
| D-Q2 | 白球初速 `v0` 由房主下发（§5.3） | **批准，且优先级高于"纯锁步"的美感**。同时按 R5-3 把 `Cos/Sin` 收进 `PoolMath` —— **两件事都做，不互相替代** |
| D-Q3 | 判据编号 | **从 `T170` 起**，避开 144–169 已用段；与 quality-lead 登记、冲突由它裁；写判据时**只引用号、不引用"第几条"** |
| D-Q4 | 文档落点 | 正文留本文；另出 `Docs/adr/ADR-0002-杆级锁步而非状态复制.md`，只写两条决策与理由（"只传杆参数不传球状态"、"每杆 checksum + 房主兜底"），**不重复正文** |
| D-Q5 | `spin` | **批准**：恒定 0 + 非零即 `ShotRejected{unsupported-spin}`；**拒绝时必须给可见原因**（不许静默） |
| D-Q6 | `com.unity.multiplayer.center` | **批准删除**（留着会让人误以为项目已有联机能力从而去装 NGO） |
| D-S1 | 排期 | P0 → P0-b → P1 → P2-a/c 为第一张；**P0 单独一笔提交**（好回退）；**P0-b 与 P1 必须串行**（都改 `PhysicsTestDrive.cs`，同一个函数被两个改动同时碰则出问题分不清是谁的） |

### 12.2 待定位 / 待办（有明确办法的）

| # | 事项 | 办法 / 状态 |
|---|---|---|
| Q5 | ~~`_vAvg` 是否需要重置~~ **已结清（读码）** | `LoadBalls` 只在 `PoolSimulator.cs:670` 处 `_vAvg = new Vector2[balls.Count]`（**零初始化**），**不计算**；值由 `Step()` 每个子步开头重算（`:755-757`：重算先于任何读取）⇒ **快照可以不携带 `_vAvg`**。⚠ 该结论依赖"重算先于读取"这个**实现事实**，故 `RestoreSnapshot` 注释必须写明，并由 P3-A1 判据（跑 300 步后逐位相同）守卫 |
| Q6 | ~~`TableConfig` 参与 `contentHash` 的字段清单~~ **已结清（读码 + grep 读者）** | 清单见 **§4.3 的 A/B 两张表**（逐项带"谁在读"的证据）。关键结论：`captureRadius` 已被内核弃用（`PoolSimulator.cs:166`），`offTableHalfExtents` / `outerBounds*` 无运行时读者，**provenance 全部不盖、尤其 `generatedAtUtc`**。并给出了"清单腐烂"的源码守卫（§4.3-C） |
| Q7 | Unity(Mono) 的 `System.Random` 是否与 Microsoft BCL 逐值相同 | **已实测两个 Microsoft 运行时一致**（FW 4.8 / .NET 10，逐位相同，§5.6.1），**但 Mono 未验证**（本机无 Mono、不跑 Unity）。仪器：`Docs/Tools/rack_fingerprint.ps1`（已修好 PS 5.1 兼容 + 3 个工具坑），在 Unity 内跑一次、比对 `FP` 行即钉死。**这是 P0 落地后的第一件事** |
| Q8 | 判据编号具体分配（`T170` 起的映射表） | 与 quality-lead 对账后填。建议先订：`T170`=ShotsCommand 唯一入口守卫、`T171`=量化往返、`T172`=摆球槽位表金向量、`T173`=快照逐位、`T174`=双端内存传输端到端 |

### 12.3 辅助工具（本轮新增，只读）

| 文件 | 用途 |
|---|---|
| `Docs/Tools/rack_fingerprint.ps1` | 摆球指纹导出器。完整复刻 `EightBallRackPositions` 的取数顺序与几何算式，输出"槽位→球号"表 + float32 位模式 + `rackDigest`（与 §4.2 协议同算法）+ 一行 `FP <sha256>` 供跨机 diff。`-VerifyLegacy` 附带做"带种子 Random 是否仍是 mscorlib 算法"的独立重实现对照。**不启动 Unity、不写 Assets/** |
