# D4-spec 可实现性评审（实现方视角）

| 项 | 值 |
| --- | --- |
| 评审对象 | `Docs/35-A_Day4_物理内核I_spec.md`（11 节） |
| 评审人 | engineering-lead（**实现方**视角；独立复核另见 `Docs/Reviews/D4-spec_独立复核.md`） |
| HEAD | `2c1ffdc`（工作树干净） |
| 方法 | **只读**。逐条把 spec 的每个类型/常量/文件/行号落到既有代码上核对；不运行任何写盘入口 |
| 声明 | **未创建/修改 `Assets/` 下任何文件，未改 spec**。本文件是唯一产出 |

---

## 0. 逐项结论表

| # | 评审项 | 结论 | 关键证据 | 严重度 |
| --- | --- | --- | --- | --- |
| 1 | 接口可实现性 | **基本可实现，但有 1 处接口缺口、2 处语义未定** | `TableConfig.cs:59,142-147`；`Billiards.Runtime.asmdef` | 中 |
| 2 | 悬空引用 | **发现 3 处**（1 处确认 + 1 处自相矛盾 + 1 处陈旧数字） | `PoolConstants.cs`（全文无 `MaxBallSpeed`）；`PoolConstants.cs:79`；测试实测总数 53 | 中 |
| 3 | 既有范式可套用 | **可套用**；asmdef **无需改动**；但有 3 处代价/不一致未写明 | `Day3VisualSetup.cs:236,301`；`ProjectSetupReport.cs:16-51,165-171,176` | 低-中 |
| 4 | §10.1 四项残留位置 | **项 5、项 6 的文件指错**；项 8 的最小构造已给出 | `TableIntegrityValidator.cs:29-33`；`TableConfigTests.cs:31,32-33`；`TableDataImporter.cs:162-166` | **高** |
| 5 | 落地冲突与遗漏 | **发现 1 处硬冲突**（§2.3 vs T7）+ 1 处会破 Day 1 的危险动作 + 若干成本低估 | `BilliardsProjectVerifier.cs:183`；`BilliardsProjectSetup.cs:303`；spec §2.3 vs T7 | **高** |

---

## 1. 接口是否真的可实现

### 1.1 核对通过的部分

| spec 主张 | 实地核对 | 结论 |
| --- | --- | --- |
| 新增 `namespace Billiards.Physics`，隶属 `Billiards.Runtime` | asmdef 无 `includePlatforms` 限制，覆盖 `Scripts/Runtime/**` 全部子目录 | ✅ 成立，**无需改 asmdef** |
| `TableConfig` 暴露 `List<CushionSegment>` | `TableConfig.cs:59` `public List<CushionSegment> segments` | ✅ |
| 段为 `struct` 含 `Vector2 a, b` 与 `string rail` | `TableConfig.cs:141-147` `[Serializable] public struct CushionSegment { Vector2 a; Vector2 b; string rail; }` | ✅ 与 spec §3 描述**逐字一致** |
| `Vector2` 可用 | `TableConfig.cs:4` 已 `using UnityEngine`；Runtime asmdef `noEngineReferences: false` | ✅ |
| "摊平成 `Vector2[164]`（82×2）" | `segments.Count = 82`，每段 2 端点 | ✅ 164 成立 |

### 1.2 问题

| ID | 问题 | 证据 | 严重度 | 建议（**只写 spec 该写什么**） |
| --- | --- | --- | --- | --- |
| **I-1** | **§3 接口缺 `SubSteps` 成员**。spec §2.1 与 T6 都要断言 `SubSteps == DeriveSubSteps(MaxBallSpeed)`，但 §3 的 `PoolSimulator` 只有 `SegmentCount`/`Balls`/诊断计数器 —— **没有任何暴露 `SubSteps` 的成员** | spec §3 vs §2.1/§5-T6 | **中（阻塞 T6）** | §3 补一行：`public int SubSteps { get; }`（构造时 `= DeriveSubSteps(MaxBallSpeed)`），并写明"T6 直接断言它，故必须可达" |
| **I-2** | **`Balls` 的可变性未定**。`BallState` 是 struct，若内部直接 `return _balls;`（`List<BallState>` 作为 `IReadOnlyList`），调用方可用 `(List<BallState>)sim.Balls` 改写内部状态 —— 与"契约 2：状态推进必须固定顺序"冲突 | spec §3 `IReadOnlyList<BallState> Balls { get; }` | 中 | 明确二选一并写进 spec：**（a）返回只读包装/副本**；或 **（b）接受并写明"调用方不得向下转型，测试只读"**。建议 (a) |
| **I-3** | **`namespace Billiards.Physics` 与契约 1"不得引用 `Physics`（PhysX）"同名**。实现者会犹豫是否要 `using UnityEngine;`（会引入 `UnityEngine.Physics` 歧义） | spec §3 契约 1 | 低（可读性） | 契约 1 改写为"不得引用 `UnityEngine.Physics` / `Rigidbody` / `Collider`"，并说明命名空间同名不是禁令对象 |
| **I-4** | **构造期前置条件未规定**：`table == null`、`table.SegmentCount != 82`、`parameters` 非法（`e`/`μ` 越界）时应如何 | spec §3 构造函数 | 中 | 补"构造期断言清单"（抛异常 vs 记 FAIL），并与 §2.2 的"Validator 拒绝初始重叠摆放"归到同一处 |
| **I-5** | **`PoolMath` 是 `internal`** ⇒ 测试程序集（`Billiards.Tests.EditMode`）**不可见** | spec §3 | 低（且**与 §11 一致**：复核要求"不与实现共享代码路径"） | 建议 spec 明写：**T9 只做端到端逐位比较，不直接调用 `PoolMath`**；如需直接断言，须加 `InternalsVisibleTo`（建议不加） |

---

## 2. spec 里的「悬空引用」

| ID | 引用 | 实地核对 | 严重度 |
| --- | --- | --- | --- |
| **D-1** | **`MaxBallSpeed`**（§2.1 数值推导、§4、§6 判据 6、§9 R-E 均用） | **确认悬空**：`grep -rn "MaxBallSpeed" Assets/_Project/ --include=*.cs` **0 命中**；`PoolConstants.cs` 全文 102 行无此常量 | **中** |
| **D-2** | **`a_max` / `RollingDeceleration`**（T6 判据 3 写"a_max 取 5"） | 常量不存在；且 **spec 自相矛盾**：§2.1 明写"Day 4 `rollingDeceleration` 恒为 **0**"，§2.1 的 Day-4 注又说"判据 B 的 `a_max` 在 Day 4 取 **0**"，而 T6 表格写"**a_max 取 5**" | **中** |
| **D-3** | **"Day 3 的 43"**（§6 判据 10） | **陈旧**：当前 EditMode 实测 **53**（`PoolConstantsTests 13 + TableConfigTests 24 + D2T02B 6 + ModelOrientationTests 5 + MeshAlignmentSensitivityTests 5`）。43 是 Day 3 当时值，R1e/R2/R3 已各加测试 | 低-中 |

### 核对通过（**存在**，不是悬空）

| 常量/文件 | 位置 |
| --- | --- |
| `PocketOpeningRadius` | `PoolConstants.cs:79` ✅ |
| `PocketCaptureRadius` | `PoolConstants.cs:76` ✅ |
| `BallRadius` / `BallDiameter` | `PoolConstants.cs:28,31` ✅ |
| `ClothHalfWidth` / `ClothHalfLength` / `CushionNoseHeight` / `ClothHeightY` | `PoolConstants.cs:51,48,57,54` ✅ |
| `CushionSegmentCount` / `PocketCount` | `PoolConstants.cs:65,68` ✅ |
| 4 个探针/证据文件（§0 表） | 全部存在 ✅ |
| `Docs/Reviews/D2-T03_AXL度量边界与盲带扫描.md` | **存在** ✅（是"§10.1 项 5 该指的原始证据"） |
| `ADR-0001 §9.5` | **存在**（`ADR-0001:258`）✅ |

### D-1 与 D-2 的落点与取值建议（**不含实现代码**）

**`MaxBallSpeed`**
- **落点**：`PoolConstants`（该类的定位是"单位制与全局硬指标的唯一真源"，`PoolConstants.cs:20`；放这里才能被 Validator/测试/未来 `PhysicsTuning` 共同引用）。
- **取值依据**：排期原文为"**初速 2–8 m/s**"。→ 必须取**上界 8**，因为 §2.1 的两个判据都是**上界推导**：
  - 判据 A：`MaxBallSpeed·dt/SubSteps ≤ 0.5·BallDiameter` —— 取 8 才能覆盖最快球；
  - 判据 B：`½·a_max·(dt/SubSteps)² ≤ 5e-5 m` —— 与 `MaxBallSpeed` 无关，但同理应取参数上界。
  - **取中值或下界会使判据在 8 m/s 时静默失效** —— 正是 §2.1 要防的那类"调高速度却不重推"。
- **语义须写明**：它是"**策划给的初速上界**，用于自守卫推导"，**不是运行时参数**；运行时初速仍取自击球输入（Day 8+）。

**`a_max`（T6 判据 3）**
- spec 内部三处不一致（§2.1 正文 / §2.1 待办注 / T6 表格）。建议**明确二选一**并写进 T6 标题：
  - **(甲) Day 4 用 `a_max = 0`**（= 实际 `rollingDeceleration`）⇒ 判据 3 左式为 0，**恒真**。此时必须**在 spec 里注明"判据 3 在 Day 4 是空转的，其判别力 Day 7 才生效"**（否则又是"看起来有条判据、实际测不出东西"）。
  - **(乙) Day 4 用 `a_max = 5` 作 Day 7 预期上界做"预演"** ⇒ 判据 3 真的在算，但**测的是式子的形状，不是 Day 4 的行为**。此时也必须注明"5 是 Day 7 的计划值，非 Day 4 参数"。
- **我的建议**：取 **(乙) + 显式注明**，因为 (甲) 让 T6 少一条有判别力的判据，而 (乙) 能提前抓住"SubSteps 推导式写错"（例如把平方写成立方）。

**D-3 的数字**：判据 10 应改为"**Day 4 起点的 53** + 本 spec 新增"，并显式列出 53 的构成，避免"43+新增"这种会误导实现的表述。

---

## 3. 既有范式能否直接套用

### 3.1 建场景范式（`Day3VisualSetup`）—— **可直接复用**

- `Day3VisualSetup.cs:236` `EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)` + `:301` `EditorSceneManager.SaveScene(scene, path)` ⇒ Day 4 把 `MainScenePath` 换成 `PhysicsGreybox.unity` 即可。
- **但有 3 项代价 spec 未写，必须补**：

| ID | 代价 | 证据 | 建议 |
| --- | --- | --- | --- |
| **P-1** | **`NewSceneMode.Single` 会丢弃当前场景**（未保存改动直接丢）。batch 无碍，**交互式菜单调用有风险**（Day 3 已继承此隐患） | `Day3VisualSetup.cs:236` | spec 写明"建场景入口仅用于 batch / 已保存状态"，或改用 `Additive` 后逐场景保存（成本更高，不建议 Day 4 改） |
| **P-2** | **场景文件不幂等**：每次重建 **fileID 重排** ⇒ git 里 160/160 行噪音（与 `Main.unity` 同病，已在 R2 实测） | R2 实测（`Main.unity` 内容等价、字节不等） | spec 明写"**接受该噪音**，或记为已知成本"；不要为此做确定性序列化（成本远超收益） |
| **P-3** | 场景生成入口**不得调用任何校验逻辑**（R2 原则的推广：生成器不得在校验路径里） | R2 结论 | spec 明写 `Day4PhysicsSetup` 是**生成入口**、`Day4Validator` 是**校验入口**，两者不得互调 |

### 3.2 验收门范式（`ProjectSetupReport` / `CheckResult`）—— **结构够用，但有 3 处不一致**

| ID | 项 | 证据 | 结论 |
| --- | --- | --- | --- |
| **P-4** | `SetupReport` 能否承载 `d4-verification.json` | `ProjectSetupReport.cs:100-196`：`schema` / `generatedAtUtc` / `unityVersion` / `activeBuildTarget` / `passCount` / `warnCount` / `failCount` / `overallStatus` / `List<CheckResult>` | ✅ **够用**，直接沿用 |
| **P-5** | 落盘路径与 `-outputDir` 口径 | `ProjectSetupReport.cs:165-171` `WriteToDisk` **固定写 `Docs/Reports`**，**不认 `-outputDir`** | ⚠️ Day 4 必须像 Day 3 一样改用 `ResolveReportPath`（`Day3Validator.cs` 已示范），否则又出现"菜单/命令行口径分叉" |
| **P-6** | `ToConsoleSummary` 的标题**硬编码** "Day-1 工程配置验收" | `ProjectSetupReport.cs:176` | ⚠️ Day 4 复用会打出错误标题（Day 2/3 已如此）。建议参数化标题（或 spec 明说"接受"） |
| **P-7** | §7 的另外 4 份 JSON（`d4-penetration-scan` / `d4-segment-coverage` / `d4-degeneracy`）**不是 `SetupReport` 结构** | spec §7 | ⚠️ 需各自定义 `[Serializable]` DTO。spec 未给字段清单 ⇒ **建议列出每份的结构要点**（否则实现时会临场决定，复核方无从对照） |

### 3.3 asmdef 引用 —— **spec 没漏，且确实不需要改**

| 新文件 | 归属 asmdef | 是否需改 asmdef |
| --- | --- | --- |
| `Tests/EditMode/PoolSimulatorTests.cs`、`PoolSimulatorInvariantTests.cs`（交付物 9/10） | `Billiards.Tests.EditMode`（asmdef 递归包含该目录全部 .cs） | **不需要**；它已引用 `Billiards.Runtime` ✅ |
| `Scripts/Editor/Visual/Day4PhysicsSetup.cs`、`Day4Validator.cs`（交付物 7/8） | `Billiards.Editor` | **不需要**；`Billiards.Editor.asmdef` 已引用 `Billiards.Runtime` ✅ |
| `Scripts/Runtime/Physics/*.cs`（交付物 1–6） | `Billiards.Runtime` | **不需要**（目录在 asmdef 覆盖范围内）✅ |

⚠️ **唯一需要注意**：`PoolMath` 是 `internal`，测试不可见（见 I-5）。

---

## 4. §10.1 四项残留 —— 位置核对与最小可区分构造

### 4.1 位置核对：**项 5、项 6 的文件写错了**

| spec §10.1 原文 | 实地核对结果 | 判定 |
| --- | --- | --- |
| 项 5：**`TableIntegrityValidator.cs` 中**指向已删除对象的墓碑注释 #2 → 删除该指针 | `TableIntegrityValidator.cs:29-33` 的墓碑 **#1 已含指针**：`详见 ADR-0001 §9.5。`（ADR §9.5 **确实存在**，`ADR-0001:258`）。**缺指针的墓碑 #2 在 `TableConfigTests.cs:31`** | ❌ **文件指错**。照原文改会**删掉一个正确且应保留的指针** |
| 项 6：**同一文件内**重复的 `<summary>` 块 → 合并 | 重复 `<summary>` 在 **`TableConfigTests.cs:32-33`**（`/// </summary>` 紧跟 `/// <summary>端点 → 最近**网格面点**…</summary>`），**不在** `TableIntegrityValidator.cs` | ❌ **文件指错** |
| 项 7：同类残留的**主张级**再枚举 | 方向正确。我已做关键词级扫描：`轮廓近似残差` 剩 **2 处**（均为有意墓碑，非主张）；`沿面内滑动全盲` **0 处** | ✅ 但"主张级"需逐条问"哪些结论被推翻/撤回" |
| 项 8：构造「陈旧资产」证明两入口可区分 | 成因已查明 + 最小构造已给出（见 4.2/4.3） | ⚠️ spec 只写了目标，未写构造 |

**项 5 的正确表述建议**：
> `TableConfigTests.cs:31` 的墓碑 #2 **缺出处指针** ⇒ 补 `详见 ADR-0001 §9.5 与 Docs/Reviews/D2-T03_AXL度量边界与盲带扫描.md`；
> `TableIntegrityValidator.cs:29-33` 的墓碑 #1 **已完整，不动**。

### 4.2 R3 判据退化的具体成因

两个入口的**唯一差别**是"要不要重建"，而重建走 `TableDataImporter.Import()`，其**跳过判定只看 4 个记录标量 + 2 个计数**：

```
TableDataImporter.cs:162-166
  existing.sourceSha256 == tableSha
  && existing.generator == GeneratorVersion
  && existing.SegmentCount == PoolConstants.CushionSegmentCount
  && existing.PocketCount == PoolConstants.PocketCount
```

资产**最新**时 `Import()` 也跳过 ⇒ **两个入口对资产都不写** ⇒ 观测不到差异（判据退化）。

### 4.3 最小可区分构造（**可直接执行**）

```
前提：先备份 Assets/_Project/Data/TableConfig.asset

步骤 1  只改「记录的 sourceSha256」一行：
         在 .asset 的 YAML 里把
             sourceSha256: ba60fc204c155881127b500bcce2f388066151757984659f89e681c2ef364398
         改成
             sourceSha256: deadbeef0000000000000000000000000000000000000000000000000000dead
         ⚠ 不要改几何数据（见下"已知边界"）

步骤 2  跑校验入口（输出到仓库外，避免污染证据）：
         Unity.exe -quit -batchmode -projectPath "<root>" \
           -executeMethod Billiards.EditorTools.TableIntegrityValidator.ValidateInteractive \
           -outputDir "<root 之外>/d4-stale-demo-a"
         预期：REF-03（"资产记录的源 sha256 与实际文件一致"）**FAIL**；
               且 `git status -- Assets Docs` 显示 TableConfig.asset **仍是被篡改状态**

步骤 3  跑重建入口（同一输出目录或另一个）：
         Unity.exe -quit -batchmode -projectPath "<root>" \
           -executeMethod Billiards.EditorTools.TableIntegrityValidator.RebuildAndValidateInteractive \
           -outputDir "<root 之外>/d4-stale-demo-b"
         预期：REF-03 **PASS**；且 TableConfig.asset 被**修复**（git status 恢复干净）

结论：两个入口的行为**可观测且不同**（一个留下陈旧资产并报 FAIL，另一个修复它并报 PASS）
```

### 4.4 附：一个**顺带发现的已知边界**（建议写进 spec）

> **`Import()` 的幂等判定不校验资产自身完整性。**
> 若只篡改**几何数据**（例如把某段端点挪 1 mm）而**不动 `sourceSha256`**，
> `Import()` 会判定"内容未变"并**跳过**，**不会修复**；两个入口依旧不可区分。
> ⇒ §10.1 项 8 的构造必须**篡改记录标量**（`sourceSha256` 或 `generator`），而不是几何。
> 这一条也说明：`TableConfig.asset` 的**几何正确性**只由 `CMP-03`（索引对齐 vs 源）负责，
> `Import()` 的幂等守卫**不承担**该校验职责 —— 两者分工应在 spec 里写明。

---

## 5. 落地冲突与遗漏

### 5.1 「不改 `Main.unity`，新建 `PhysicsGreybox.unity`」—— **可行，但有一条必须显式禁止的动作**

- ✅ **可行**：`EditorBuildSettings.scenes` 由 `BilliardsProjectSetup.cs:303` 一次性设为
  `new[] { new EditorBuildSettingsScene(MainScenePath, true) }`；**新建场景资产不会自动进构建列表**。
  Day 3 已验证"改场景不碰 build settings"，Day 4 照做即可。
- ❌ **危险动作（spec 必须明文禁止）**：**不得把 `PhysicsGreybox` 加进 `EditorBuildSettings`**。
  因为 Day 1 的 `SCN-02` 断言"构建场景列表**仅含**主场景"：
  ```
  BilliardsProjectVerifier.cs:183
    scenes.Length == 1 && scenes[0].path == MainScenePath && scenes[0].enabled
  ```
  ⇒ 一旦 `PhysicsGreybox` 进列表，**Day 1 的 26/26 立刻变 25/26**（Day 1 证据作废）。
  实现者为了"在 build 里能跑灰盒"很自然地会去加 —— **spec 必须先堵住**。
- ✅ 播放模式验证灰盒**不需要** build list（打开场景直接 Play）。

### 5.2 硬冲突：**§2.3 的 `PocketProximityEvents == 0` 与 T7 的袋角段扫描**

- §2.3 定义：球心进入任一袋 `throatCenter ± (openingRadius + BallRadius)` ⇒ 计 `PocketProximityEvents`，**判据 9 要求 == 0**。
- T7 要求：从法定内点**朝 82 段各自中点与两个端点各发一球**。而 §1.1 实测 **76/82 段是袋角侧壁（jaw）**，且 §1.3 说"链端点距最近袋的 throat/center 仅 **24.9–27.6 mm**"。
  `openingRadius + BallRadius = 0.06132 + 0.028575 = 0.08989 m ≈ 89.9 mm`。
  ⇒ **朝袋角段发射的球，其球心在到达碰撞点前几乎必然进入 89.9 mm 的袋口邻域** ⇒ 计数器必然非 0 ⇒ **与判据 9 直接冲突**。
- **severity：高**（这是 spec 内部两处要求互斥，照做必然 FAIL 或必然"为了让测试过而放宽断言"——后者正是要防的）。

**建议（二选一，写进 spec）**：
- **(甲) 豁免**：T7 对**袋角段**的发射不计入 `PocketProximityEvents`，并写明理由（"袋角段的可达路径本身经过袋口邻域，属 Day 4 已知不覆盖区"）；`== 0` 的断言只对**直库段**与**不含袋口邻域的路径**生效。
- **(乙) 收紧定义**：把 `PocketProximityEvents` 的触发条件从"进入袋口邻域"改为"**从袋口邻域逃逸到库边外侧**"（即 §2.2 情形 2 的"球跑到外侧"），这样 T7 的正常撞击不触发。

**我倾向 (乙)** —— 它才是 §2.2 真正想观测的那件事（"球从袋口逃逸"），而 (甲) 会在"哪些发射豁免"上留自由解释空间。

### 5.3 其余遗漏 / 成本被低估

| ID | 问题 | 证据 | 严重度 | 建议 |
| --- | --- | --- | --- | --- |
| **P-8** | **T5b 的退化路径未定义**。`DisableToiForDiagnosticsOnly = true` 后"在**子步末位**做一次采样点检测" —— 若实现者理解成"连采样检测也关掉"，球直接飞出，嵌入量判据失去意义（会被算成"负对照通过"） | spec §3 注释 + §5 T5b | 中 | spec 明写：**关 TOI 后必须仍然做子步末采样检测**，只撤掉"步内解析 TOI"这一层 |
| **P-9** | **T7 的规模与归属未定**。5 个内点 × 82 段 × 3 目标（中点 + 2 端点）= **1230 次发射**，每次跑满轨迹。放在 **EditMode 测试**（交付物 9/10）可能显著拖慢测试；而 §7 又要求产出 `d4-segment-coverage.json`（**验收门**的证据文件） | spec §5 T7 + §7 | 中 | spec 明确 T7 的**归属**：建议**放验收门**（可长跑、可产出证据文件），EditMode 侧只留一个"少量段的冒烟版" |
| **P-10** | **`CushionGizmo.cs` / `PhysicsTestDrive.cs` 在 Runtime 程序集**，`autoReferenced: true` 且无平台限制 ⇒ **会进玩家构建**（gizmo 绘制通常应仅编辑器） | spec §10 交付物 5/6 + `Billiards.Runtime.asmdef` | 低-中 | spec 写明：`CushionGizmo` 用 `#if UNITY_EDITOR` 包裹，或移到 Editor 程序集；`PhysicsTestDrive` 也说明是否随包发布 |
| **P-11** | **T6 判据 1 的"当前值 3 == 3"是**结果**不是**判据**。判据应写成"`SubSteps == DeriveSubSteps(MaxBallSpeed)`"，spec 表格已如此 → 但要在 spec 里强调**不得把 3 写成常量断言**，否则调 `MaxBallSpeed` 时该测试仍绿 | spec §5 T6 表 + §2.1 末句 | 低 | 已在 §2.1 末句提到（"必须是推导值而非硬编码"）→ 建议在 T6 表标题里也写一遍 |
| **P-12** | **§1 的实测值我未逐条重算**（段长、链拓扑、包围盒外扩等）。这些来自已产出的探针 JSON，属**复核方**的数值重算范围 | spec §1.1–1.5 | — | 本条**留给独立复核**（`Docs/Reviews/D4-spec_独立复核.md` 已存在）。我只核对"引用的文件存在" ✅ |

---

## 6. 必须先改 spec 才能开工的条目（按阻塞性排序）

| 序 | ID | 条目 | 为什么阻塞 | 修一句还是重写 |
| --- | --- | --- | --- | --- |
| 1 | **H-1** | **§10.1 项 5/项 6 的文件指错**（应为 `TableConfigTests.cs`，不是 `TableIntegrityValidator.cs`） | 照做会**改错文件**、并**删掉一个正确的指针**（墓碑 #1） | 改文件路径（1 行） |
| 2 | **H-2** | **§2.3 `PocketProximityEvents == 0` 与 T7 袋角段扫描互斥** | 两处要求不可能同时满足；照做必然 FAIL 或被迫放宽断言（= 假通过） | 改 §2.3 定义或 T7 豁免（1 段） |
| 3 | **H-3** | **`SubSteps` 接口缺口**（§3 无该成员，T6 却要断言它） | T6 无法实现 | §3 补 1 行 |
| 4 | **H-4** | **`MaxBallSpeed` 悬空**（§2.1/§4/§6/§9 均用，代码中不存在） | 编译不过 / 实现者自定值 | 补落点 + 取值依据（1 段） |
| 5 | **H-5** | **`a_max` 三处不一致**（§2.1 正文 0 / §2.1 注 0 / T6 表 5） | T6 判据 3 在 Day 4 会空转或算错 | 钉死一个口径并注明判别力范围（1 段） |
| 6 | **H-6** | **禁止把 `PhysicsGreybox` 加进 `EditorBuildSettings`** | 否则 **Day 1 的 26/26 变 25/26**（证据作废） | 加一句禁令 |
| 7 | **H-7** | **T5b 退化路径须"仍做采样检测"** | 否则负对照测的不是"无 TOI 会怎样" | 加一句限定 |
| 8 | **H-8** | **§6 判据 10 的"43"应改"53"**（并列出构成） | 误导实现者的工作量与"新增"口径 | 改数字 |

**可并行（不阻塞开工，但建议同批补）**：I-2/I-3/I-4/I-5、P-1…P-11。

---

## 7. 我建议补齐的内容（**只写 spec 该写什么，不含实现代码**）

1. **§3 接口签名的完备性清单**：补 `SubSteps`、`MaxBallSpeed` 的引用关系；补构造期前置条件断言清单；明确 `Balls` 的可变性契约。
2. **常量落点表**：新增一节列出 Day 4 依赖的**全部**常量及其真源（`PoolConstants` / `TableConfig` / 未来 `PhysicsTuning`），逐个标注"已存在 / 需新增"。至少覆盖 `MaxBallSpeed`、`a_max` 的口径。
3. **§2.3 计数器定义与 T7 的关系**：明确袋角段路径是否豁免，或改用更精确的"逃逸"定义。
4. **T5b 退化路径的精确语义**：关掉的是哪一层（TOI），保留的是哪一层（采样检测）。
5. **§7 每份证据 JSON 的字段要点**（结构 + 必填项），使复核方可对照。
6. **§8 场景策略补一句**：`Day4PhysicsSetup` **不得修改 `EditorBuildSettings`**（并说明原因：Day 1 `SCN-02`）。
7. **§8 补"生成入口 / 校验入口分离"**（沿用 Day 3 与 R2 的结论）：`Day4PhysicsSetup` 只生成、`Day4Validator` 只校验，且校验入口**不得调用**生成入口；两者的报告路径都走 `ResolveReportPath`。
8. **§6 判据 10 的起点数字修正为 53 并列出构成**。
9. **§10.1 的 4 项残留**：修文件路径 + 给出"陈旧资产"最小构造（可直接用本评审 §4.3 的步骤）+ 写入 §4.4 的"幂等判定不校验资产完整性"这一已知边界。
10. **§9 风险登记补两条**：**R-H** `PhysicsGreybox` 误入 build list ⇒ Day 1 证据作废（缓解：明文禁止）；**R-I** T7 与 `PocketProximityEvents` 互斥 ⇒ 断言永久 FAIL（缓解：先改定义）。

---

## 8. 只读声明与未覆盖事项

- **只读**：本次评审**未创建/修改 `Assets/` 下任何文件**，**未修改 spec**，**未运行任何 Unity 入口**（无写盘动作）。产出仅本文件。
- **我实测过的**：常量存在性（grep）、`TableConfig` 字段形态、asmdef 内容与依赖、`Import()` 跳过条件、`SCN-02` 断言、`ProjectSetupReport` 结构、墓碑/重复 `<summary>` 的行号、测试总数（53）、spec 引用文件存在性。
- **我未覆盖的**（留给独立复核）：
  - §1.1–1.5 的**数值**是否与探针 JSON 一致（段长统计、链拓扑、包围盒外扩、mouth 与段端点重合）；
  - §2.1 的**判据推导数值**（2.3333 / 1.8634 / 22.222 mm / 19.29 µm）是否算对；
  - §5 T1/T10 的**解析期望值**是否与几何一致（§11 要求复核方独立推导，我不应参与）；
  - T7 的**可达性**（某袋角段是否真的从法定内点可达）。
