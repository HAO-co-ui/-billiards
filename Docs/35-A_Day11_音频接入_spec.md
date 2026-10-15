# 35-A Day 11 音频接入 —— 实施规格

> 目标：按排期「Day 11 — 音频接入（7 h）」把 10 个 SFX + 1 个 BGM 接进灰盒，做到
> **进球有清脆音、撞库有闷响、开球不爆音不轰炸**（排期验收原话）。
> 判据编号：**`T89`（内核接触事件诊断）/ `T90`（音频映射与限流的纯函数）**（`T87`/`T88` 已占）。

---

## 1. 范围（照抄排期，不得放宽）

| # | 任务 | 工时 |
| --- | --- | --- |
| 1 | 导入 10 个 SFX + 1 BGM（22.05 kHz / 双声道 / PCM16），BGM 循环接缝验证（端点差 1.53e-4） | 1.5 h |
| 2 | 事件映射：`strike` / `hit` / `rail` / `pocket` / `foul` | 1.5 h |
| 3 | 音量与音高按相对速度调制（轻碰轻响） | 1.5 h |
| 4 | 同帧多碰撞限流 + 音高抖动（关键：开球同时撞 8 个球不能变"机关枪"） | 2 h |
| 5 | 音频总开关 + 音量（设置页接口预留） | 0.5 h |

**验收**：进球有清脆音、撞库有闷响、开球不爆音不轰炸。

## 2. 资产事实（**已实测**，2026-10-15 由 wav 头直读）

| 资产 | 路径 | 格式 | 时长 |
| --- | --- | --- | --- |
| BGM ×1 | `F:\工作文件\idea35_台球\06_BGM\Late_Set\late-set.wav` | 22050 Hz / 2ch / 16-bit PCM | 91.429 s |
| SFX ×10 | `F:\工作文件\idea35_台球\07_SFX\{strike,hit,rail,pocket,foul,win,lose,click,hover,back}.wav` | 22050 Hz / 2ch / 16-bit PCM | 各 0.800 s |

- 与排期口径（22.05 kHz / 双声道 / PCM16）**逐项吻合** ⇒ 导入采用**不压缩 PCM**（`AudioImporter` 默认即可，不必改 sample rate）。
- ⚠ **SFX 统一 0.8 s**：短促连击会叠厚——限流（任务 4）是必做项而非可选项；听感若仍厚，调音量包络（本 spec 只登记为待实测项）。
- `06_BGM/Code_Reference/audio.js` 为素材包的 Web 参考实现（只读参考，不移植）。

## 3. 分层与数据流（对齐既有范式）

```
内核（PoolSimulator，只读诊断）
  └─ 接触事件缓冲（BallBall / Cushion / Pocket + 相对速度 + 位置）
        │  DrainContactEvents()（读即清）
        ▼
音频层（PoolAudio，MonoBehaviour 胶水）
  ├─ 轮询 LaunchCount 增量 ⇒ strike 事件
  ├─ 映射 clip + 音量/音高调制（纯函数）
  └─ 限流（ThrottleState 纯类）⇒ PlayOneShot（AudioSource 池）
```

- **strike 不来自接触事件**：它是"出杆"而非"碰撞"⇒ 由 `PhysicsTestDrive.Launch()` 的只读计数出口轮询（见 §4.2）。
- **foul / win / lose 三个 clip 本单只导入不播**（单机无规则判定；接口预留），如实登记。

## 4. 实现要点

### 4.1 内核只读接触事件诊断（`Runtime/Physics/PoolSimulator.cs`）

- 新增值类型（紧随既有 `BallBallContactRecord` 同类定义）：

```csharp
public enum ContactKind { BallBall, Cushion, Pocket }

public struct ContactEvent
{
    public ContactKind kind;
    public float relativeSpeed;   // 碰撞前相对速度（球-球）/ 入射速度（球-库、越喉）
    public Vector2 position;      // 球-球=两球中点；球-库=球心；越喉=球心
}
```

- 缓冲 `private readonly List<ContactEvent> _contactEvents`；**清零随 `ResetDiagnostics()`**（与既有每杆计数同族语义）。
- **记录点（恰 3 处，各一行调用 `RecordContactEvent(...)`，均"先取值、后解算/置零"）**：
  1. `ResolveBallBallContact` 入口：`rel = vi - vk` 已算 ⇒ `relativeSpeed = rel.magnitude`；position = `(pi + pk) * 0.5f`；
  2. `ResolveSegmentContact` 入口：`speedIn = Length(ball.velocity)` 已算 ⇒ 直接用；position = 球心；
  3. `CheckPocketProximity`：越喉判定成立（`s > 0`）分支内、**速度置零之前**取 `Length(_balls[index].velocity)`；position = 球心。
- **出口（drain 语义）**：`public int DrainContactEvents(List<ContactEvent> into)` —— 把缓冲逐个并入 `into` 并清空缓冲，返回条数。**不做**"只读列表 + 单独清空"（两处状态易漏清）。
- ⛔ **纪律雷区（改这个文件前必读）**：
  - `BallBallContactCount++` 的文本计数**恰好 2 处**由 `T72b(a)` 源码级断言锁定 ⇒ **只加记录调用，不动计数行**；
  - 注释与代码**不得出现** `Rigidbody|Collider`（`D4-PHY-01` 扫描 `Runtime/Physics/*.cs`）；
  - 记录**不得改任何解算量**（只读复制）⇒ 由 `T72b(b)` 的逐位基线（T32b total=74、T37 bb=37/total=6746、16 球终态）**直接验证惰性**，不必新写。

### 4.2 出杆事件源（`PhysicsTestDrive`）

- 新增两个只读出口：`public int LaunchCount { get; private set; }`、`public float LastLaunchSpeed { get; private set; }`；
  在 `Launch()` 内**单点赋值**（`LaunchCount++`；`LastLaunchSpeed = speed`）。**不清零**（累计计数，音频层轮询增量）。

### 4.3 音频层（新文件 `Runtime/Physics/PoolAudio.cs`，`AddComponentMenu("Billiards/Pool Audio")`）

- 序列化字段：`drive`、`strikeClip/hitClip/railClip/pocketClip`（4 个必接）+ `foulClip/winClip/loseClip`（可空，预留）、
  `audioEnabled = true`（**总开关，单字段单读点**，关闭即不播放也不消费事件）、`masterVolume = 0.8f`（设置页预留）。
- 内部：`AudioSource` 池（6 个，挂在自身）；`ThrottleState _throttle`；`int _lastLaunchCount`。
- `Update`：
  1. `audioEnabled == false` ⇒ 直接返回（**不消费事件**——与预测开关"关闭即不算"同口径）；
  2. `drive.LaunchCount != _lastLaunchCount` ⇒ strike 事件（强度 = `LastLaunchSpeed`）——**每帧至多一次**；
  3. `sim.DrainContactEvents(buffer)` ⇒ 逐事件映射 + 调制 + 限流 ⇒ 播放。
- **纯函数/纯类（判据 `T90` 直接驱动）**：
  - `public static float VolumeForSpeed(float relSpeed, float fullSpeed = 4.5f)` = `Lerp(0.25, 1.0, Clamp01(relSpeed/fullSpeed))`；
  - `public static float PitchForSerial(int serial)` = `1f + 0.06f * ((serial % 3) - 1)`（0.94 / 1.00 / 1.06 轮转——**确定性**抖动，不用随机数）；
  - `public sealed class ThrottleState`：`bool TryAccept(ContactKind kind, float nowSeconds)` —— 规则：**同类同帧 ≤ 3 个** 且 **同类两次最小间隔 0.03 s**（超出拒绝）；不同类互不影响；`Reset()` 清状态。
    帧边界口径：调用方传入 `Time.unscaledTime`；"同帧"由调用方按帧清 `frameCount`（`TryAccept` 内部维护 `_currentFrameAccepted`，每帧首个事件时按 `nowSeconds` 变化判定翻帧）。

### 4.4 资产导入

- 11 个 wav 复制到 `Assets/_Project/Audio/`（`SFX/` 与 `BGM/` 两个子目录），`.meta` 由 Unity 生成；
- BGM 的 `AudioSource.loop = true`（循环接缝验证引用素材包报告 `audio-ui-vfx-audit.json` 的端点差 1.53e-4——**本单只登记，不重测**）；SFX 走 `PlayOneShot`（不 loop）。

### 4.5 场景接线（`GreyboxSceneSetup`）

- 新宿主对象 `PoolAudio`，接线 `drive` + 4 个 clip（`AssetDatabase.LoadAssetAtPath`）；重建场景（强制路径）。

## 5. 判据

### `T89` 内核接触事件（EditMode，`PoolAudioTests.cs` 或并入 `PoolSimulator…Tests`）

- **(a)** 正碰场景（白球 → 静止目标球，初速 `v0` 已知）：出杆推进后 `DrainContactEvents` 含 **≥1 个 `BallBall`** 事件，
  `relativeSpeed` 与**独立闭式**（`|v_白球 − v_目标| = v0`，两球初始静止目标、单碰）比对（1e-4）；
  `position` == 两球中点（独立闭式）。
- **(b)** 撞库场景：含 `Cushion` 事件，`relativeSpeed` ≈ 入射速度（闭式，1e-4）。
- **(c)** 落袋场景：含 `Pocket` 事件。
- **(d)** **drain 语义**：第二次 drain 返回 0（读即清）。
- **(e)** **惰性**：由 `T72b(b)` 的逐位基线承担（**不重复断言**——铁律 16；报告须给 T72b 的实测结果）。
- **注入负对照**：删任一处 `RecordContactEvent` 调用 ⇒ 对应 (a)/(b)/(c) 红。

### `T90` 音频映射与限流纯函数

- **(a)** `VolumeForSpeed`：0 ⇒ 0.25；`fullSpeed` ⇒ 1.0；半速 ⇒ 0.625（1:1 lerp，闭式）。
- **(b)** `PitchForSerial`：`serial = 0,1,2,3,...` ⇒ `0.94/1.00/1.06/0.94...`（周期 3、确定性）。
- **(c)** `ThrottleState`：同帧同类第 4 个被拒；间隔 < 0.03 s 被拒；异类不互相影响；时间推进后恢复。
- **注入负对照**：上限失效（cap 改为巨大）⇒ (c) 红。

## 6. 验证姿势（照抄工程标准 §8）

- 编辑器钉死 `E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe`；跑前查 `Temp/UnityLockfile`；
  `-runTests` 不带 `-quit`、显式 `-testResults`/`-outputDir` 指仓库外；`-executeMethod` 带 `-quit`；
  脚本改后首跑"重编译退出"（G12）重跑即过。
- 全量 EditMode 应 **177 → 179**（+`T89`+`T90`）；四验收器 25/33/22/7 复跑。
- 注入的临时改动**逐字节还原**（`sha1` 核对，不得 `git checkout --`）。

## 7. 风险与盲带（如实）

| # | 风险/盲带 | 处置 |
| --- | --- | --- |
| R-1 | **听感**（清脆/闷/轰炸）非判据可测 | 排期验收由用户实机；判据只锁事件流与调制闭式 |
| R-2 | SFX 统一 0.8 s，连击叠厚 | 限流必做；听感仍厚 ⇒ 后续调包络（登记） |
| R-3 | 批处理 `-nographics` 下无真实发声 | 判据只驱动纯逻辑/事件流；端到端属 PlayMode / 手动 |
| R-4 | BGM 循环接缝 | 引用素材包审计（端点差 1.53e-4），本单不重测 |
| R-5 | `foul` 语义 | 单机无规则 ⇒ 只导入不播（接口预留） |
| R-6 | 接触事件缓冲的体量（T37 满堆单杆数千接触） | `List<ContactEvent>`（值类型）每杆清；drain 每帧消费；不做无界累积——**若实测帧耗异常再评估**（登记） |
