# 卡 35-A · Day 1 执行记录：工程基建与决策固化

| 项 | 值 |
| --- | --- |
| 计划工时 | 7 h |
| 执行日期 | 2026-10-13 |
| 对应排期 | `Docs/35-A_击球手感_制作排期.md` → Day 1 |
| 工程 | `E:\Unity_Deve\Unity_Project\Billiards game` |
| 环境 | Unity 6000.0.49f1 + URP 17.0.4，Windows 桌面 |

---

## 一、产出清单

### 1. 目录规范

```
Assets/_Project/
├── Art/
│   ├── Models/            导入的 FBX
│   └── Textures/          贴图
├── Audio/
│   ├── BGM/               06_BGM/Late_Set/late-set.wav
│   └── SFX/               07_SFX/ 10 个
├── Data/                  ScriptableObject：TableConfig / PhysicsTuning
├── Materials/
├── Prefabs/
├── Scenes/                Main.unity
├── Scripts/
│   ├── Runtime/           Billiards.Runtime.asmdef  → 命名空间 Billiards
│   │   └── Core/          PoolConstants.cs
│   └── Editor/            Billiards.Editor.asmdef   → 命名空间 Billiards.EditorTools
│       └── ProjectSetup/  Day-1 配置应用与验收工具
├── Settings/
│   └── Input/             Billiards.inputactions
└── Tests/
    └── EditMode/          Billiards.Tests.EditMode.asmdef
```

全部 20 个目录均带 Unity 格式的 `<folder>.meta`，仓库自洽可提交。

### 2. 代码资产

| 文件 | 作用 |
| --- | --- |
| `Scripts/Runtime/Core/PoolConstants.cs` | 米制硬指标常量（单位制唯一真源） |
| `Scripts/Editor/ProjectSetup/BilliardsLayers.cs` | Layer 分配定义（索引与名称的唯一真源） |
| `Scripts/Editor/ProjectSetup/ProjectSetupReport.cs` | 验收结果模型 + JSON/Markdown 报告输出 |
| `Scripts/Editor/ProjectSetup/BilliardsProjectVerifier.cs` | 21 项工程配置验收 |
| `Scripts/Editor/ProjectSetup/BilliardsProjectSetup.cs` | 幂等的 Day-1 配置应用器（含菜单与命令行入口） |
| `Tests/EditMode/PoolConstantsTests.cs` | 13 项 EditMode 测试（证明测试链路可用 + 常量自洽） |

### 3. 程序集边界

| 程序集 | 引用 | 平台 |
| --- | --- | --- |
| `Billiards.Runtime` | `Unity.InputSystem` | 全平台 |
| `Billiards.Editor` | `Billiards.Runtime`, `Unity.InputSystem` | Editor |
| `Billiards.Tests.EditMode` | `Billiards.Runtime`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` | Editor，`UNITY_INCLUDE_TESTS` |

先建边界再写代码，避免后期逆向拆分（Day 4 的物理单测直接落在 EditMode 程序集里）。

### 4. 版本控制

- `git init -b main`，首次提交待 Unity 生成 `.meta` 后执行
- `.gitignore`：105 个文件纳入版本控制，`Library/`（22,829 文件）、`Temp/`、`Logs/`、`UserSettings/`、`*.csproj`、`*.sln` 已正确排除
- `.gitattributes`：Git LFS 追踪 30 类二进制资产（FBX / 贴图 / 音频 / 字体 / 视频）；Unity 文本资产强制 `text` 保持可 diff
- `git lfs install --local`（仅写 `.git/config`，不动用户全局配置）

---

## 二、三个关键技术决策

### 决策 1：工程配置必须通过 Unity API 应用，不能手改 `ProjectSettings/*.asset`

**这不是偏好，是有证据的硬约束。** 本次执行中实测到：

```
ProjectSettings/ProjectSettings.asset        2026-10-13 10:49:31   ← Unity 启动时自行写入
ProjectSettings/EditorSettings.asset         2026-10-13 10:49:07   ← 同上
ProjectSettings/ShaderGraphSettings.asset    2026-10-13 10:49:25   ← 同上
```

Unity 编辑器将工程设置常驻内存，编辑器运行期间手改磁盘 YAML 会在它下次写回时被**静默覆盖**。
因此本项目的工程配置全部落在 `BilliardsProjectSetup.cs` 里，通过官方 API 修改。

**这个选择带来三个额外收益**：① 配置本身成为可复现、可评审的代码，而非不可追溯的手工操作；
② 新机器 clone 后一句 `-executeMethod` 即可还原全部配置；③ 验收器与配置器同源，不会漂移。

### 决策 2：刻意不使用自定义 Tag

Day-1 计划原文写的是"规划 Tag/Layer"，实际执行**只做 Layer，不引入任何自定义 Tag**。

理由：物体身份（是球？是袋口？是库边？）在本项目中是**关键路径判定**。Tag 是字符串，
拼错不会编译失败，重构时不会报错，运行时才静默失效。改用组件（Day 4 起的 `BallBody`、
`PocketMarker` 等）表达身份，具备类型安全、可静态检查、随程序集重构。

验收项 `LYR-03` 会持续断言"自定义 Tag 数量 = 0"，防止后来者无意破坏该约定。

### 决策 3：把玩法层的物理碰撞两两隔离

本项目自研二维定长步进物理，**完全不使用 PhysX**。为避免任何误加的 `Rigidbody`/`Collider`
产生隐形接触力、破坏模拟器的确定性，把 Layer 6–11 两两设为 `IgnoreLayerCollision`。

「Default 层与玩法层之间」保持原样不隔离，避免影响未来可能的调试用射线拾取。
另外主场景设有守卫验收项 `SCN-04`：`Rigidbody` 数量必须为 0。

---

## 三、Layer 分配

| 索引 | 名称 | 用途 |
| ---: | --- | --- |
| 6 | `Table` | 桌体、台呢、库边 |
| 7 | `Ball` | 16 颗目标球 + 白球 |
| 8 | `Cue` | 球杆 |
| 9 | `AimGuide` | 瞄准线、幽灵球（可整体开关渲染，不参与拾取） |
| 10 | `VFX` | 击杆/碰球/撞库/入袋特效 |
| 11 | `Environment` | 房间布景（长椅、落地灯、挂画等，35-A 大多不做） |

---

## 四、米制硬指标固化（`PoolConstants.cs`）

单位约定 **1 Unity unit = 1 米**。Unity 没有显式单位设置，该约定由常量 + FBX 导入缩放（1:1）共同保证。

| 常量 | 值 | 来源 |
| --- | --- | --- |
| `BallRadius` | 0.028575 m | `table-metadata-reference.json` |
| `ClothInnerLength` | 2.54 m | 同上（9 尺台 100 英寸，精确值） |
| `ClothInnerWidth` | 1.270108866 m | 同上（**见下方发现**） |
| `ClothHeightY` | 0 | 同上，全局几何基准平面 |
| `CushionNoseHeight` | 0.044982 m | 同上 |
| `PocketCaptureRadius` | 0.030741635767169012 m | 同上 |
| `CueLength` | 1.4 m | 杆模型（杆尖为局部原点，杆身 +Z） |
| `PlayfieldVisualScale` | 1.2 | "桌面扩大 20%"，仅表现层 |

### 执行中发现的问题：短轴内沿比标称值长 0.109 mm

9 尺台标称台面为 100 × 50 英寸 = 2.54 × **1.27** m。但美术包实测的短轴内沿为 **1.270108866 m**，
比标称值长 **0.000108866 m ≈ 0.109 mm**（相对偏差 0.0086%）。

处理方式：**采用实测值，不"修正"为标称值**。因为物理碰撞轮廓必须与渲染网格严格对齐——
如果物理用 1.27 而网格是 1.270108866，球贴库时会与网格产生约 0.05 mm 的可见缝隙。

该偏差已由 `PoolConstantsTests.ClothWidth_DeviatesFromNominalByUnderOneMillimetre` 显式钉住：
若有人把常量改回 1.27，或数据源被替换，测试立刻失败。

0.109 mm 对台球玩法无任何实质影响，但记录在案，避免它被反复当作"新发现的问题"重复排查。

---

## 五、输入映射（`Billiards.inputactions`）

保留了 URP 模板中的 `UI` 映射（38 个绑定）与全部 controlSchemes —— `InputSystemUIInputModule`
依赖 UI 映射，删除会导致 Day 13 的 HUD 无法交互。**替换**掉与本项目无关的 `Player` 映射。

### `Gameplay` 映射（8 个动作 / 14 个绑定）

| 动作 | 类型 | 绑定 | 用途 |
| --- | --- | --- | --- |
| `PointerPosition` | Value · Vector2 | `<Pointer>/position` | 指针位置 → 瞄准方向 |
| `AimLock` | Button | `<Mouse>/leftButton` | 按下拖拽瞄准，松开锁定方向 |
| `AimNudge` | Value · Axis | `→` / `←`(invert) / `D` / `A`(invert) | 方向微调 |
| `PowerAdjust` | Value · Axis | `↑` / `↓`(invert) / `<Mouse>/scroll/y` | 力度微调（主力度由 Day 13 的滑条驱动） |
| `Shoot` | Button | `Space` | 出杆 |
| `CancelAim` | Button | `<Mouse>/rightButton`、`Esc` | 解锁瞄准 |
| `ToggleAimGuide` | Button | `Tab` | 瞄准辅助开关 |
| `RestartRack` | Button | `R` | 重摆（开发期） |

**手柄支持不在 35-A 范围内**，绑定只挂 `;Keyboard&Mouse`。生成脚本 `Docs/Tools/generate_input_actions.py`
保留在仓库中，可重新生成与追溯。

### 资产迁移方式

Unity 模板把该资产放在 `Assets/InputSystem_Actions.inputactions`，它同时是**项目级动作**（由
`EditorBuildSettings` 的 `com.unity.input.settings.actions` config object 引用）。
迁移使用 `AssetDatabase.MoveAsset` 而非手工复制 —— MoveAsset **保留 GUID 并自动更新全库引用**，
因此迁移到 `Assets/_Project/Settings/Input/Billiards.inputactions` 后项目级引用不会失效。
验收项 `INP-02` 专门检查这一点。

---

## 六、PlayerSettings 变更

| 项 | 变更前 | 变更后 | 理由 |
| --- | --- | --- | --- |
| `defaultScreenWidth/Height` | 1024 × 768 | **1920 × 1080** | 桌面横屏基准 |
| `fullScreenMode` | FullScreenWindow | **Windowed** | 开发期便于对照调参 |
| `resizableWindow` | false | **true** | 便于测多分辨率（Day 8 视口适配） |
| `runInBackground` | false | **true** | 调参时不因失焦暂停模拟。**发布前需复核** |
| `allowFullscreenSwitch` | true | true（保持） | 允许 Alt+Enter |
| `productName` | Billiards game | **Idea35 Pool Room** | 与美术包原型命名一致 |
| `colorSpace` | Linear | Linear（**已满足**，未写入） | URP 硬性要求 |

### 已满足项（模板本身即正确，本次仅验证）

| 项 | 状态 | 证据 |
| --- | --- | --- |
| 色彩空间 = Linear | ✅ | `m_ActiveColorSpace: 1` |
| Graphics 默认管线 = `PC_RPAsset` | ✅ | guid `4b83569d67af61e458304325a23e5dfd` |
| 当前画质等级 = `PC`（非 Mobile） | ✅ | `m_CurrentQuality: 1`，且 Mobile 等级已排除 Standalone 平台 |
| API 兼容级别 = .NET Standard 2.1 | ✅ | `apiCompatibilityLevel: 6` |

---

## 七、离线编译验证（已完成）

本机 Unity 编辑器运行中，无法启动批处理模式，因此先做**离线编译验证**。

方法：从 Unity 生成的 `Assembly-CSharp-Editor.csproj` 派生临时校验工程，
把 6 个新文件纳入编译（Unity 生成的 csproj 用显式文件列表，新增文件不在其中），
引用真实程序集 `Unity.InputSystem.dll`、`UnityEngine.TestRunner.dll`、`UnityEditor.TestRunner.dll`、`nunit.framework.dll`。

```
dotnet build _verify_compile.csproj
→ 已成功生成。   0 个警告   0 个错误
```

**双重证据（防止空跑假绿）**：

1. 编译产物 `Billiards-Verify.dll` 中确认存在：`PoolConstants`、`BilliardsLayers`、
   `BilliardsProjectVerifier`、`BilliardsProjectSetup`、`PoolConstantsTests`、
   `ApplyDay1FromCommandLine`、`ApplyDay1Interactive`、`RequiredGameplayActions`、
   `TableTestAssembly_IsWired`、`CaptureRadius_IsOpeningRadiusMinusBallRadiusMinusTwoMillimetres` 等符号。
2. 编译前若不含通配符替换，`ReadmeEditor.cs` 是唯一编译目标，上述类型均不存在。

**已排除的两个假信号**（记录以免重复排查）：
- `IgnoreLayerCollision` 在 DLL 中"查不到" → 实为 .NET `#Strings` 堆的**后缀共享**，
  堆里只存更长的 `GetIgnoreLayerCollision`，短名指向其中部。**不是漏编。**
- `ProjectSetupReport` 查不到 → 它是**文件名**不是类型名，C# 元数据不记录文件名。

临时产物已清理（`_verify_compile.csproj`、`Temp/bin/Verify/`）。

### 未越界改动核验

```
最近改动的 .unity / .prefab / .asset（排除 _Project）  → 空
ProjectSettings/ 被本次操作修改的文件                  → 无
```

既有场景与预制体零改动；`ProjectSettings` 的最近写入时间均为 10:48–10:49（Unity 启动时自身写入），
与本次操作无关。

---

## 八、待执行：Unity 内配置应用与验收

因为 Unity 编辑器持有工程锁（`Temp/UnityLockfile`），配置应用必须在 Unity 内执行一次。

**两种执行方式（任选其一）**：

1. **关闭 Unity 后由批处理自动执行并提交**（推荐，全程可追溯）
   ```
   Unity.exe -quit -batchmode -projectPath "<工程路径>" \
             -executeMethod Billiards.EditorTools.BilliardsProjectSetup.ApplyDay1FromCommandLine \
             -logFile -
   ```
2. **在 Unity 内点菜单**：`Billiards → Setup → Apply Day-1 Project Configuration`

两种方式执行的是**同一段代码**，结果一致。

验收结果写入 `Docs/Reports/day1-verification.json`（含 21 项检查的 expected / actual / status）。

### 验收项清单

| 编号 | 检查 |
| --- | --- |
| CFG-01 ~ 06 | 色彩空间、分辨率、窗口模式、可调整大小、后台运行、产品名 |
| LYR-01 ~ 03 | Layer 6-11 命名、物理碰撞全隔离、无自定义 Tag |
| RND-01 ~ 04 | PC 管线资产可加载、Graphics 默认管线、画质等级、等级管线覆盖 |
| SCN-01 ~ 04 | 主场景存在、构建列表仅含主场景、模板残留已清理、主场景无 Rigidbody |
| INP-01 ~ 03 | 输入资产含必需动作表、项目级引用完好、Gameplay 含全部必需动作 |
| ASM-01 ~ 04 | 三个 asmdef 存在、Editor 可访问 Runtime 常量（引用链验证） |
| TGT-01 | 活动构建目标 = StandaloneWindows64 |
| RUN-01 | 配置应用流程无异常 |

---

## 九、与排期的偏差

| 项 | 排期 | 实际 | 说明 |
| --- | --- | --- | --- |
| 排期 Day 1「仅确认 Linear / PC_RPAsset」 | 视为待办 | **模板本身已满足** | 省下的时间投入到可复现配置脚本与离线编译验证 |
| 「清理 SampleScene 并重置」 | 重置 | **替换为 `Main.unity`** | 模板场景含 TutorialInfo 依赖，新建更干净 |
| 「规划 Tag」 | 含 Tag | **刻意不做** | 见决策 2，改用组件，并以 `LYR-03` 锁定该约定 |
| `Assets/Settings/` URP 资产位置 | 未提及 | **保持原位不迁移** | 迁移零收益且需改 Graphics/Quality 引用，风险不对等 |

Day 1 新增的两项非计划内产出（可复现配置脚本、离线编译验证）挤占了约 1.5 h，
但这部分投入会在 Day 2–3 的导入校验中收回——`BilliardsProjectVerifier` 的框架可直接复用于
Day 2 的 `TableIntegrityValidator`。

---

## 十、下一步

Day 2（数据落地与美术导入）：

1. 写 `TableDataImporter`：解析 `table-metadata-reference.json` → `TableConfig.asset`
   （82 段库边 + 6 袋 + captureRadius）
2. 确定 three(右手) → Unity(左手) 的映射规则，**只做一次**转换
3. 导入 `FBX_Exports/Recommended/Table/PoolTable_Normalized.fbx`
4. 写 `TableIntegrityValidator`：断言内库 2.54 / 1.270108866、球半径 0.028575、cloth y=0、6 袋中心与网格袋口重合
