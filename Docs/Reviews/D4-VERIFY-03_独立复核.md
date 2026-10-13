# D4-VERIFY-03 · 独立复核报告（冻结修订 `a87ff8e`）

| 项 | 值 |
| --- | --- |
| 复核对象 | 提交 `a87ff8e7d5b82ab7efa4663e0a322e4ce5891df7`（Day 4 切片 B+C） |
| 复核方 | quality-lead（**只读**；产出全在仓库外 `_d4verify03/`；**未提交**；注入实验在临时副本） |
| 总判定 | **有条件通过** |

---

## 1. 冻结锚点结果（三条命令）

```
$ git diff a87ff8e -- .      →  （空）
$ git status --porcelain     →  （空）
$ git rev-parse HEAD         →  a87ff8e7d5b82ab7efa4663e0a322e4ce5891df7   （附注）
```
⇒ **锚点成立**（工作区 == `a87ff8e`）。

> ⚠️ **锚点曾被破坏一次，由我复原**：我的**第一次 EditMode 跑**（未带 `-outputDir`）把
> `Docs/Reports/d4-segment-coverage.json` 写脏（仅 `generatedAtUtc` 一行不同）。
> 我已 `git checkout --` 还原，并用 `git diff a87ff8e -- .` = 空 复核。**根因见 §4-D1**（这是缺陷，不是我误操作）。

---

## 2. 权威数字（全部我实测，R3-6 格式）

| # | 项 | 我的实测 | 入口 |
| --- | --- | --- | --- |
| 1 | EditMode | **85 / 85 @ a87ff8e** | `-runTests`（我跑两次：带/不带 `-outputDir`，均 85/85） |
| 2 | Day1 | **25 / 25 @ a87ff8e** | `BilliardsProjectSetup.VerifyDay1Interactive`（**verify-only**） |
| 3 | Day2 | **33 / 33 @ a87ff8e** | `TableIntegrityValidator.ValidateFromCommandLine` |
| 4 | Day3 | **22 / 22 @ a87ff8e** | `Day3Validator.ValidateFromCommandLine` |
| 5 | Day4 | **5/5，warn=0，fail=0**（逐条见下） | `Day4Validator.ValidateFromCommandLine` |
| 6 | 加和验算 | 见下 | — |

**EditMode 分文件构成（从 XML 的 `<test-case fullname>` 逐条计数，非目测源码）**：
```
 6 D2T02B_IndependentAuditTests      5 Day4GreyboxTests
 5 MeshAlignmentSensitivityTests     5 ModelOrientationTests
 5 PhysicsBanGuardTests             13 PoolConstantsTests
 4 PoolSimulatorIntegralFormatTests  2 PoolSimulatorInvariantTests
 3 PoolSimulatorPocketAndMultiBallTests  1 PoolSimulatorSegmentCoverageTests
 4 PoolSimulatorTests               24 TableConfigTests
 8 TableImporterErrorPathTests
 ⇒ 合计 85
```
**三方一致**：我跑的 / 入库终态 `d4-sliceC-editmode-tests.xml`（85/85）/ 档证据 `…PHY01STRING.xml`（85/85）✓

**Day4 逐条**（`d4-verification.json`，我重跑复现 pass=5/warn=0/fail=0）：
`D4-SCN-01 PASS`｜`D4-SCN-02 PASS`｜`D4-PHY-01 PASS`｜`D4-BAN-01 PASS`｜`D4-BLD-01 PASS`

**加和验算**：53+5=58 ✓／58+8=66 ✓／66+6=72 ✓／72+13=85 ✓（**四条全对**）
**但**：台账**无 `77` 行**，且把切片 B 记作"未提交"，而 **77 实测于提交 `123b30f`** ⇒ 见 §4-D2。

**Day1 的 25 vs 26 —— 我自己确认了（未采信那句话）**：
- 我实测 **verify-only = 25/25，且 `RUN-01` 不在其中** ✓
- 我读**入库证据** `Docs/Reports/day1-verification.json` = **26/26，且含 `RUN-01`** ✓
- 我读源码：`RUN-01` **只在 `BilliardsProjectSetup.cs:127`（`ApplyDay1` 内）产生** ✓
⇒ **两个数字确有不同来源，25 是 verify-only 口径** ✓
**边界（我主动选择）**：**我没有跑 Apply 入口** —— 它会改 `ProjectSettings/`，**必然破坏冻结锚点**。故 26 侧我用入库证据作第二来源，而非自己跑。

---

## 3. 四条声称的证伪尝试

### 声称 1 — R-C 修复只是"计数器修复"，不是物理改动 ⇒ **未能推翻（成立）**
**实验**：写 Python 脚本**真正读取**三个 JSON 并逐字段 diff（**不是**转写脚本；`_d4verify03/diff_coverage.py`）。
**观察**：`prerc` vs `final` 共 **15818 个扁平字段，338 个不同**，按族聚合**全部**落在：
`hits[].guardTrips`(326) + `checks[].detail`(2) + `checks[].actual/status`(2) + `generatedAtUtc` + `passCount` + `warnCount` + `overallStatus` + `note` + `toiIterationGuardTrips` + `guardTrippedLaunchCount` + `maxGuardTripsPerLaunch`。
**关键字段点名**：
| 字段 | prerc | nearzero | final |
| --- | --- | --- | --- |
| `maxPenetrationMetres` | 6.146729e-08 | 6.146729e-08 | **6.146729e-08（逐位相同）** |
| `perSegmentHitCounts`（246 个逐项字段） | — | — | **不同 0 个** |
| `segmentCount` / `launchCount` / `frozenLaunchCount` / `energyGainViolations` | 82/1230/80/0 | 同 | **同** |
| `toiIterationGuardTrips` | 1810 | 1196 | **0** |
⇒ **物理量逐位不变、82 段命中计数完全一致** ✓ ⇒ **未能推翻** ⇒ 修复确为纯计数器侧。

### 声称 2 — `D4-PHY-01` 与汇编级检查**两向互补** ⇒ **未能推翻（我自己重做出同样结果）**
**实验**：**另起临时副本**，A/B **分副本、分次跑**（各跑 EditMode + Day4Validator）。
| 档 | 注入 | 汇编级 | `D4-PHY-01` |
| --- | --- | --- | --- |
| **A** | 真 `UnityEngine.Physics.Raycast(o,d)` | **FAIL**（EditMode **84/85**，唯一失败 = `RuntimeAssembly_DoesNotReferencePhysXModule`） | **PASS（漏）** `hits=0` |
| **B** | `System.Type.GetType("UnityEngine.Rigidbody, UnityEngine.PhysicsModule")` | **PASS**（EditMode **85/85**） | **FAIL（命中）** `hits=1 [_NegB.cs:8]` |
⇒ **与声称逐格一致** ⇒ **两向互补成立** ⇒ `D4-PHY-01` 有独有覆盖。
**附带否定性结论（已用第二法）：它扫不扫 `Physics.`？—— 不扫。**
- 法一（读源码）：正则 = `\b(Rigidbody|Collider)\b` ⇒ 无 `Physics.`
- 法二（档 A 注入）：真 `Physics.Raycast` 注入后 `hits=0` ⇒ **实证**不抓 ✓
**另观察到**：`commentLinesSkipped=263` 且 B 档 `_NegB.cs:8`（字符串）**仍被命中** ⇒ **收窄口径（跳整行注释、保留字符串）已落实** ✓

### 声称 3 — 场景重建字节级非确定 ⇒ **成立，且其判据我验证过"能为假"**
**实验**：我自算 sha1；自己跑幂等工具**及其 `--self-check`**。
| 文件 | 我算的 sha1(16) | 字节 | 与声称 |
| --- | --- | --- | --- |
| 入库 `PhysicsGreybox.unity` | `aa09ad097b095400` | 14163 | ✓ |
| `rebuild-a.unity` | `3ceeb2c04805fc46` | 14139 | ✓ |
| `rebuild-b.unity` | `febbf399509c3eef` | 14134 | ✓ |
⇒ **三者互异** ✓
**工具默认跑**：3 份样本，归一化行数 **525 / 525 / 525**，两两 **diffLines=0** ✓
**`--self-check`（我亲自跑）**：把 `0.05715→0.05716` 改 1 格 ⇒ **diffLines=2（>0）**、`SELFCHECK_PASS`，并带**内容相同副本 diffLines=0** 的对照 ⇒ **判据能失败、非恒真** ✓；临时目录在仓库外并已清理 ✓
**我的判断（性质）**：这是**序列化层**的非确定性（fileID 随机分配 + 按 ID 排序落盘），**语义等价**（归一化后逐行相同） ⇒ **不是语义差异**。
**会不会影响 Day 5+ 可复现性？** —— **不影响物理/行为**；**但**任何**按字节哈希**的生成场景门/证据**不可用**（必须走归一化比较）。这与 `Main.unity` 是同族现象 ✓

### 声称 4 — 工作区未被污染、已入库证据未被覆写 ⇒ **对 day1/2/3 成立；对"套件自身"不成立（见 §4-D1）**
**跑完后我自算 sha1**：
| 文件 | 我算的 sha1（40） | 声称 | 结论 |
| --- | --- | --- | --- |
| `day1-verification.json` | `311bff52af7ae0bfcca4c0bdeeb5036069fae388` | 同 | ✓ **未被覆写** |
| `day2-verification.json` | `a7627f3fcb854fb75a9e3dcaae1be08726e22214` | 同 | ✓ |
| `day3-verification.json` | `d294bfba920f63193ff8b9fcb4186285f4b819e2` | 同 | ✓ |
| `d4-segment-coverage.json` | `ee151953b458309b3cfef78cbc0e2e24015a041f` | — | 现等于冻结版 ✓（我曾被写脏，已还原） |
**`.workbuddy` 未受版本控制** ✓：`git ls-files | grep -c workbuddy` = **0** ✓
**`.gitignore` 未误伤项目文件** ✓：只忽略 `Library/Temp/obj/Build/Logs/UserSettings/Recordings` 与 IDE 产物，且**显式保留 `Assets/**/*.meta`** ✓

---

## 4. 我发现的、实现方没说的

### ★ D1（**本卡内应修**）EditMode 测试会**写进仓库** —— 我的第一次 G2 跑就把已入库证据改脏
- `PoolSimulatorSegmentCoverageTests.cs:682-683`（`T7_AllSegmentCoverage_ScanAndReport`）经 `ReportOutput.ResolveReportPath` 写 `Docs/Reports/d4-segment-coverage.json`；
  **`-runTests` 不带 `-outputDir` 时它回落到入库路径** ⇒ **我的第一次跑改了它**（实测 diff：仅 `generatedAtUtc` 一行）。
- **⇒ 违反工程标准 §3 R3-5「测试不得产生仓库改动」**，且**与冻结纪律直接冲突**（"跑 G2"本身会破坏冻结修订）。
- **我已复原**（`git checkout --`），并**验证修复路径**：`-runTests … -outputDir <仓库外>` ⇒ `git status` **HOLDS** ✓，且该 JSON **落到** `_d4verify03/run_with_outdir/` ✓。
- **另一处更硬**：`D2T02B_IndependentAuditTests.cs:386` 用 `Path.Combine(ProjectRoot, …)` **硬编码**仓库路径 ⇒ **`-outputDir` 无法重定向它**（本次它内容稳定故未显形，但一旦其算法变动，跑测试就会写脏入库证据）。
- **建议**：让测试证据**默认**写仓库外/被忽略目录；或至少给 D2T02B 那条补 `-outputDir` 支持；并把它写成 G2 的前置条件。

### D2（低）台账**仍缺 `77` 行**，且切片 B 的锚点归属写错
`Docs/测试数基线台账.md` 行 31/32：`72 @*未提交*` → `85 @工作区（切片 C，未提交）`，并注"两者**同一个修订锚点（均未提交）**"。
**但切片 B 已提交为 `123b30f`**（我在 D4-VERIFY-02 实测 **77/77 @123b30f**）⇒ **77 没有行**，其锚点被记作"未提交" ⇒ **R3-6「报数带锚点」对 77 未满足**（对 85 满足）。

### D3（低）`D4-PHY-01` 只跳**整行**注释 —— 行尾注释仍会命中
实现只 `text.TrimStart().StartsWith("//")` ⇒ `foo(); // Rigidbody` 这类**行尾注释**仍计入 hits ⇒ 残余假阳性面。
（其 doc 已自述"不剥离行尾注释"，属**诚实边界**；若要消，需注意**别连带削掉字符串字面量** —— 那是它的覆盖来源。）

### D4（信息）"归属关系被覆盖"这一声明我未能证实
幂等工具 doc 自述边界：**行多重集比较不验证归属**，并称归属由 `D4-SCN-02` + "场景内各对象的 YAML 结构"覆盖。
`D4-SCN-02` 我确认为**组件计数**（`rootObjects=1 / CushionGizmo=1 / PhysicsTestDrive=1`）——**计数不是结构**；而"各对象 YAML 结构"这类检查**我未找到** ⇒ 该覆盖声明**可能偏强**（建议核）。

### 正面：我上一轮指出的三个零覆盖缺口**已全部补上**
`PoolSimulatorIntegralFormatTests`（**T11a** 自由飞行 / **T11b** **合成减速度 a≠0** / **T12** 端点精确）｜
`PoolSimulatorPocketAndMultiBallTests`（**T13a** 袋喉冻结+停止积分 / **T13b** 中央不冻结对照 / **D3** 16 球遍历序逐位一致）｜
`PoolSimulatorSegmentCoverageTests`（**T7** 全 82 段扫描）⇒ 与我 D4-VERIFY-02 的 §⑦ 缺口逐条对应 ✓

---

## 5. 我未能验证的项

1. **未跑 Day1 的 Apply 入口**（会改 `ProjectSettings/` ⇒ 破坏冻结）⇒ 26 侧以入库证据为第二来源（见 §2）。
2. **未在日志中直接观察到 `T7: 证据写入 <path>` 一行**（我的 grep 未命中）⇒ 我改用**文件落点**作为 `-outputDir` 生效的证据（`_d4verify03/run_with_outdir/d4-segment-coverage.json` 存在）—— 这属于"用另一种可观察量替代"，**已如实标注**。
3. **未审 `Docs/Reports/d4-sliceC-editmode-*.xml` 其余 8 份归档**（INJECT/INJECT2/AMAX20/BUDGET64/128/RCDIAG 等）的各自用途与数字。
4. **未复跑 T11/T12/T13/T7 的算理**（只核了它们**存在且在绿**、类名与覆盖面与我的缺口对应）；其期望值推导未独立重做（本轮时间用在了四条声称的证伪上）。

---

## 6. 建议

| # | 建议 | 定级 |
| --- | --- | --- |
| 1 | 修 **D1**：测试证据默认写仓库外/忽略目录（至少 D2T02B 那条支持 `-outputDir`）；把"跑 G2 不污染仓库"写成 G2 前置 | **本卡内应修** |
| 2 | 修 **D2**：台账补 `77 @123b30f` 行，并把切片 B 的"未提交"改为该提交 | **本卡内应修** |
| 3 | 核 **D4**：`归属关系` 是否真有非计数的检查在守；若无，收窄该声明 | 可延后 |
| 4 | 考虑消 **D3** 的行尾注释假阳性（须保留字符串字面量） | 可延后 |

**无阻断发布项。**

---

## 7. 实验后还原证明

```
$ git diff a87ff8e -- .      →  （空）
$ git status --porcelain     →  （空，0 行）
$ ls Assets/_Project/Scripts/Runtime/Physics/*.cs | wc -l  →  6
$ ls …/Runtime/Physics/ | grep -c _Neg                      →  0
$ sha1sum Docs/Reports/d4-segment-coverage.json             →  ee151953b458309b3cfef78cbc0e2e24015a041f（= 冻结版）
```
（临时副本 `_d4v03-negcopy` 已删除；`_v02b-negcopy` 早前已删。）

---

**结论：有条件通过 —— 理由：权威数字 `85/85、25/25、33/33、22/22、5/5 @ a87ff8e` 全部由我实测复现，四条声称三条未能推翻、一条在"测试自身写仓库"这一点上不成立（我已抓到并复原）；我实际观察到的总测试数 = 85。**
