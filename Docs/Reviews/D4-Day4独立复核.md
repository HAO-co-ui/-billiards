# D4-VERIFY-02 · Day 4 物理内核 独立复核（只读）

| 项 | 值 |
| --- | --- |
| 复核对象 | **提交 `123b30f`**（Day 4 切片 A + B + standards R1-3a/R3-6） |
| 复核方 | quality-lead（**只读**：未改 `Assets/`、未改 spec、未覆盖入库证据、未提交 git） |
| 方法 | 独立复跑（5 个入口，输出写仓库外）+ 独立注入实验（临时副本）+ 解析式独立推导 |
| **总判定** | **PASS_WITH_CONCERNS** |

**锚点自检**：`git status` 仅 1 项（agent memory，非冻结）；`git diff 123b30f -- <Runtime/Physics, Tests/EditMode, Scenes>` = **空** ✅

**权威数字（我测，带锚点）**：
```
EditMode  77/77 @ 123b30f
Day1      25/25 @ 123b30f
Day2      33/33 @ 123b30f
Day3      22/22 @ 123b30f
Day4       5/5  @ 123b30f
```

---

## 一、逐项结论表

| # | 项 | 结论 | **我自己重算的证据** | 严重度 |
| --- | --- | --- | --- | --- |
| 1 | 基础数字独立复现 | ✅ **全复现** | 见上；5 个入口全部我自己跑、**输出全落 `E:/Unity_Deve/Unity_Project/_v02-out/`（仓库外）** | — |
| 1b | **Day1 入口不覆写入库证据** | ✅ **成立** | Day1 走 `VerifyDay1Interactive` + `-outputDir`（经单点 `ReportOutput.ResolveReportPath`）⇒ 报告落在 `_v02-out/day1/`；**入库 `day1-verification.json` mtime 仍 15:10（早于我 16:14 的运行）** | — |
| 2 | **T5b 负对照** | ✅ **成立（双路验证）** | ① **读代码**：`IntegrateBall` 仅在"接触检测"一步分叉（`:267-277`），`AverageVelocity`/`position += vAvg*earliest`/`ResolveContact`/减速/袋喉**全部共用** ⇒ **复用生产路径** ✓；② **独立注入**：副本中 `if (DisableToiForDiagnosticsOnly)` → `if (false && …)` ⇒ **T5b FAIL** ✓ | — |
| 2b | `d4t5b_recompute.py` 复算的是不是同一东西 | ✅ **是** | 我独立跑：**exit 0、worst(Step 边界)=18.7972 mm**（测试 18.7973，float64/float32 尾差 1e-4 mm）。**关键**：它自己写明 Step 边界 vs 子步边界（18.7972 vs 20.7972）**口径不同**，并**只在 Step 边界断言**（与 T5b 一致）—— 判定：**同口径 ✓** | — |
| 3 | **§3 积分格式判断** | ✅ **成立（我确认，非复述）** | 见 §三 | — |
| 4 | T1/T3 解析推导 | ✅ **独立推导一致** | 见 §四（我的期望值由参数推导，不读测试内部量） | — |
| 5 | Day4Validator 与守卫 | ✅ **全成立** | 见 §五（含**独立注入**证明 D4-BAN-01 真扫到灰盒场景并报出违规对象） | — |
| 6 | 主张级枚举 | ⚠️ **1 处台账过期** | 见 §六（R1-3a 真、R3-6 真但**台账自身已过期**） | 低-中 |
| 7 | 两个零覆盖缺口 | ✅ **确认存在**（不遮掩） | 见 §七 | 中 |

---

## 二、基础数字（我自己跑出来的）

| 入口 | 我的结果 | 与自报 |
| --- | --- | --- |
| EditMode | **77/77**（6+5+5+5+5+13+2+4+24+8） | ✅ 一致 |
| Day1 | **25/25**（verify-only 口径） | ✅ 一致 |
| Day2 | **33/33** | ✅ 一致 |
| Day3 | **22/22** | ✅ 一致 |
| Day4 | **5/5**（D4-SCN-01/02、D4-PHY-01、D4-BAN-01、D4-BLD-01） | ✅ 一致 |

**不变量（我自己测）**：5 个入口跑完后，`Assets`+`Docs` 状态指纹 **PRE == POST == `e3b0c44298fc1c14`** ⇒ **没有任何入口改动仓库** ✅
**入库证据未被覆盖** ✅：`day1-verification.json`(15:10)、`day2/day3`(13:10)、`d4-editmode-tests.xml`(16:00) —— 均**早于**我 16:14 起的运行，我的产出全在 `_v02-out/`。

---

## 三、§3 那个判断：**成立**（我的独立理由）

**判断**："Day 4 `rollingDeceleration = 0` 时，前向欧拉与端点精确**在数学上完全相同** ⇒ 本次交付的全部测试都无法观测该格式差异。"

**我核实成立，理由（读代码，非复述）**：
```csharp
// PoolSimulator.cs:313-318
private static Vector2 AverageVelocity(Vector2 v, float a, float dt)
{
    if (a <= 0f) { return v; }        // ← a=0 时**提前 return v**
    ...
}
```
⇒ `position += vAvg * remaining`（`:281/:286`）在 `a=0` 时**逐位退化为前向欧拉**。
⇒ **两式恒等不是"近似相等"，是"同一条代码路径"** ⇒ **任何 a=0 的测试都无法区分** ✅

**并且我补一条更强的**：`PoolSimulatorTests` 的 `Params(e,mu)` 与 `PoolSimulatorInvariantTests.Params()` **默认 `a=0`**（前者 `a` 默认参数 =0，后者字面 `rollingDeceleration = 0f`）⇒ **T1/T2/T3/T5a/T5b 全部在 a=0 下运行** ⇒ **整套 Day-4 测试对积分格式不敏感** ✅

**量化（我算）**：a=5 m/s²、`dt_sub=1/360` ⇒ 单步差 `½·a·dt_sub²` = **19.29 µm**，**线性累积**：
| 连续步数 | 累积差 |
| --- | --- |
| 3（1 帧） | 57.87 µm |
| 120（1 s） | **2.31 mm** |
| 360（3 s） | 6.94 mm |

⇒ **确认**：T11（自由飞行位移精确性）+ T12（终点精确性有限差分自检）**确是唯一能抓住它的测试**，且**尚未实现**（切片 C）。
⇒ 结论：**该修复目前"已实现、但没有任何测试验证过"** —— 是**未验证**，不是**错误**；真实风险落在 **Day 7**（引入滚阻时）。
⇒ **附带**：`MaxRollingDeceleration = 5`（`:50-55`）自认是"**前瞻声明值**…不参与本切片的实际积分" ⇒ 于是**判据 B 的子步数推导本身也无测试可验**（见 D-2）。

---

## 四、T1 / T3 独立解析推导（不读实现内部量）

**我的推导**：撞 `seg[20]`（y=−0.635056 水平库，内法向 n̂=(0,1)），t̂=(1,0)，e=0.8，μ=0.9。
- 30°：v_in=(−1.732051, −1.000000)
  - `v_out·n = −e·(v_in·n) = −0.8×(−1.0)` = **+0.8000000**
  - `v_out·t = μ·(v_in·t) = 0.9×(−1.732051)` = **−1.5588459**
- 60°：v_in=(−1.000000, −1.732051)
  - `v_out·n = −0.8×(−1.732051)` = **+1.3856408**
  - `v_out·t = 0.9×(−1.0)` = **−0.9000000**

**实测（我跑出的日志）**：
| 工况 | v_out·n 实测 / 我的期望 | v_out·t 实测 / 我的期望 |
| --- | --- | --- |
| 30° | **0.799999952 / 0.8000000**（relerr 7.5e-8） | **−1.55884588 / −1.5588459**（relerr 0） |
| 60° | **1.38564074 / 1.3856408**（relerr 8.6e-8） | **−0.90000000 / −0.9**（relerr 0） |

⇒ **逐位一致到 float32 精度** ✅ **T1 的期望值确实来自解析式，且实测与之相符。**

**T3**：30 组合（6 库 × 5 角 15/30/45/60/75°），最大角度偏差 **2.365e-4°** ≪ 判据 1.0° ✅。
- **我的独立判断**：纯镜面（e=μ=1）解析上应**精确**守恒，残差 2.4e-4°（≈4.1e-6 rad）来自 float32 路径 ⇒ 量级合理。
- **判别力边界（我注意到的）**：`AngleToTangent` 用 `Mathf.Abs(法向分量)` 与 `Mathf.Abs(切向分量)` ⇒ 测的是**与切向线的无向夹角** ⇒ **T3 对切向分量符号不敏感**。该符号由 **T1 的带符号断言**（`v_out·t = μ·v_in·t`，非取模）覆盖 ⇒ **两者互补，非重复** ✅

---

## 五、Day4Validator 与守卫（含独立注入）

| 检查 | 我的实测（冻结修订） | 判定 |
| --- | --- | --- |
| D4-SCN-01 场景文件存在 | `PhysicsGreybox.unity` ✅ | ✅ |
| D4-SCN-02 结构 | `rootObjects=1 CushionGizmo=1 PhysicsTestDrive=1` ✅ | ✅ |
| D4-PHY-01 源码无 PhysX 引用 | `scanned=6 file(s) → hits=0` ✅ | ✅ |
| D4-BAN-01 扫描 | `scene=2 prefab=18 → Rigidbody=0 Collider=0` ✅ | ✅（**scene 从 1→2**，灰盒被纳入） |
| D4-BLD-01 build list | `仅 Assets/_Project/Scenes/Main.unity` ✅ | ✅ |
| **R2：不调用 Day4PhysicsSetup** | 类头 `:16` 明写"本类绝不调用"；只用其 `ScenePath` **常量** + **只读**打开场景 ✅ | ✅ |

### ★ 我的独立注入（临时副本）：**D4-BAN-01 真的扫到了灰盒场景**
在副本的 `PhysicsGreybox.unity` 根对象注入 1 个 `BoxCollider` 后：
```
Day4  D4-BAN-01 [FAIL]  exp=0/0  act=scene=2 prefab=18 → Rigidbody=0 Collider=1；违规：[Collider] PhysicsGreybox/PhysicsGreybox
EditMode  PhysicsBanGuardTests.ScenesAndPrefabs_ContainNoRigidbodyOrCollider [FAIL]
          违规对象：\n[Collider] PhysicsGreybox/PhysicsGreybox
```
⇒ **该场景真被读取，且报出违规对象名** ✅ **非空转**（不是"报了数字但没读"）。
⇒ **且注入是隔离的**：EditMode 75/77（2 失败 = 我的 2 处注入）、Day4 4/5（1 失败 = D4-BAN-01）⇒ 无连带 ⇒ 守卫精确。

**一个设计层面的观察（⚠️ 请勿与另一对混淆）**：注入**场景** Collider 后 **D4-PHY-01 仍 PASS** —— 这是**正确**的（它扫的是 `Runtime/Physics/**/*.cs` 的**源码类型引用**，不是资产）
⇒ 以下**两对**是**不同**的检查对，**不可互相引用**：

| 检查对 | 关系 | 依据 |
| --- | --- | --- |
| **`D4-PHY-01` ↔ `D4-BAN-01`** | **互补**（**源码侧** / **资产侧**） | 本报告 §五的**注入实验**已证明（场景 Collider ⇒ D4-BAN-01 FAIL、D4-PHY-01 正确 PASS） |
| **`D4-PHY-01` ↔ `RuntimeAssembly_DoesNotReferencePhysXModule`** | 是否为**子集**关系 ⇒ **待切片 C 的注入实验裁定** | team-lead 的论证：源码命名了 PhysX 类型 ⇒ 编译器**必写入** `PhysicsModule` 引用 ⇒ 汇编级测试必 FAIL ⇒ `D4-PHY-01` 的检出是子集。**我不拍此板**（见下） |

> **供切片 C 的注入实验参考的一条（不是裁断，是"待验项"）**：`D4-PHY-01` 是**源文本/类型名扫描**，
> 而 `RuntimeAssembly_DoesNotReferencePhysXModule` 只看**编译器实际写入的程序集引用**。
> 二者在**反射式**用法上可能**不等价**：例如
> `Type.GetType("UnityEngine.Physics, UnityEngine.PhysicsModule")` 或 `System.Reflection` 路径
> —— **不产生编译期类型引用**（汇编测试可能 PASS），但**源码里出现了 PhysX 类型名字符串**（`D4-PHY-01` 可能命中）。
> ⇒ 若该形态成立，则 `D4-PHY-01` 的**唯一独有输出不只是假阳性**，删除会**丢覆盖**。
> **这不是我的裁断** —— 请由切片 C 的注入实验（含"反射式 PhysX 访问"这一形态）来定。

---

## 六、主张级枚举（铁律 17）

### 6.1 "声称做了、实际没做"：**未发现**
本轮自报的 6 项数字**全部被我独立复现** ✅；且我上一轮（D4-VERIFY-01）指出的"**补负路径测试**"缺口**已补齐** —— 新文件 `TableImporterErrorPathTests`（**8 项 `Assert.Throws<InvalidDataException>`**：NullSegments / WrongSegmentCount / NullPockets / PocketMouthLengthNotTwo …）⇒ `Assert.Throws` 从 **0 → ≥8** ✅ **该缺陷已关闭**。

### 6.2 新规则 R1-3a / R3-6 逐条对现状
- **R1-3a（命名空间遮蔽须全限定）—— 真规则 ✅**
  - 实例核实：`Physics.` 用法**已全部全限定** —— `BilliardsProjectSetup.cs:212` `UnityEngine.Physics.IgnoreLayerCollision`、`BilliardsProjectVerifier.cs:108` `UnityEngine.Physics.GetIgnoreLayerCollision`；**全工程无裸 `Physics.`** ✅
  - 规则给出的**根因**（C# 名字查找先逐层查外层命名空间、**早于 `using`** ⇒ `Billiards.Physics` 遮蔽 `UnityEngine.Physics`）**正确** ✅
- **R3-6（报数须带锚点）—— 真规则，但配套台账已过期 ⚠️**
  - `Docs/测试数基线台账.md` 存在且**单点**（工程标准引用它，不另写一份）✅
  - **但它只记到**：`53@2c1ffdc → 58@b2ab519 → 66@73c7cd2 → 72@（未提交，切片A）`
  - **不含当前修订 `123b30f` 的 77**（77 = 72 + 5 `Day4GreyboxTests`）⇒ **台账自身成了 R3-6 要防的"过期数字"** ⇒ **D-1**
  - 台账的"验收器时效状态"表**正确地把 Day1 标为"已过期"** ✅ —— 我本轮的重跑给出了新值 **25/25 @123b30f**，**建议回填**。

### 6.3 新引入的恒真检查 / 空转守卫：**未发现** ✅
- T5a 有**非空转断言**（`bounced` + `TotalContactCount > 0`）✅
- T5b **我独立打出 FAIL** ✅；D4-BAN-01 **我独立打出 FAIL** ✅
- D4-BAN-01 带 `scene=2 prefab=18` 覆盖证据（`CoverageIsNonVacuous`）✅
- 未见 `Assert.Pass` 类恒真（此前已清除，本轮全工程 grep = 0 处活断言）

---

## 七、两个零覆盖缺口：**独立确认存在**（不替实现方遮掩）

### 7.1 **76 段袋角/弧段的 TOI 零覆盖** ✅ 确认
- `PoolSimulatorTests.StraightSegments = {6,20,33,47,61,74}`、`PoolSimulatorInvariantTests.StraightSegments` **同**，两文件均**硬编码这 6 条**；测试类清单中**无 T7**（全 82 段扫描**未实现**）。
- **风险（我的评估）**：袋角段长 **4.286–32.4 mm**，**远小于球直径 57.15 mm** ⇒ 球可**同时覆盖多段**；
  `SolveToi` 的**端点圆分支**与**线段分支会同时命中**；角点处的"同刻多接触"与 `MaxToiIterations`（**R-C 风险**）**只可能在此触发**
  ⇒ **这几条路径恰好是唯一完全无测试的代码**。**严重度：中**。

### 7.2 **袋喉冻结路径零覆盖** ✅ 确认
- `PocketProximityEvents` 仅在 T1/T3/T5a/T5b 被断言 **`== 0`** ⇒ **从未被触发**；
  冻结逻辑（`velocity = 0` + `_frozen[i] = true` + `Step()` 里 `continue`）**从未执行**。
- **风险（我的评估）**：`CheckPocketProximity` 在**每球每子步末**才调用 ⇒ 球可在**子步内**越过喉平面最多 `v·dt_sub` = **22.2 mm** 后才被冻结 ⇒ **冻结的分辨率与"冻结后确实停止积分"均未被验证**。**严重度：中**。

### 7.3 我另发现的两处（自报未提）
- **D-2（低）**：`MaxRollingDeceleration = 5` 与 **判据 B 的推导**目前**没有任何测试可验证**（`a` 恒为 0）⇒ 与 §3 同源。建议 T11/T12 落地时**一并**断言 `DeriveSubSteps` 在 `a>0` 下的分支，否则判据 B 永远是"算过、没验过"。
- **D-3（信息）**：**多球**（`LoadBalls` 支持多球、`Step()` 遍历多球）**所有测试只装 1 球** ⇒ "多球遍历顺序"的确定性**未被测**（球-球碰撞是 Day 5，不属缺口；但**遍历顺序**本身属 Day 4 的确定性契约）。

---

## 八、缺陷清单（含可复现命令）

| ID | 严重度 | 缺陷 | 最小复现 |
| --- | --- | --- | --- |
| **D-1** | 低-中 | `Docs/测试数基线台账.md` **过期**（只到 72；当前 `123b30f` 为 77）⇒ **台账自身成了它要防的"过期数字"** | `grep -c "123b30f\|\*\*77" Docs/测试数基线台账.md` → **0** |
| **D-2** | 低 | `MaxRollingDeceleration` 与**判据 B 的推导**无任何测试可验（a 恒 0） | 全测试文件 grep `rollingDeceleration = ` → 均为 `0f` |
| **D-3** | 信息 | 多球**遍历顺序**确定性未测（所有测试仅 1 球） | `grep -c "LoadBalls(new\[\]" Tests/EditMode/PoolSimulator*.cs` |
| **D-4** | 信息 | `d4t5b_recompute.py` 是**重实现**（不调用生产代码）⇒ 它验的是**物理模型**而非**实现**；若实现有 bug 而模型对，脚本仍会 PASS | 脚本 `resolve()` 自写一遍解算 —— 设计如此，**需知道它的边界** |

---

## 九、已复核通过（无需再查）

1. **五个入口的数字**：EditMode **77/77**、Day1 **25/25**、Day2 **33/33**、Day3 **22/22**、Day4 **5/5** @ `123b30f`（全部我自跑、输出仓库外）。
2. **不变量**：五入口跑完 `Assets`+`Docs` 指纹不变；**入库证据未被覆写**。
3. **Day1 可独立复跑**（`-outputDir` 经单点 `ReportOutput`）✅ —— 这是上一轮"Day1 只能靠自报"缺口的关闭。
4. **T5b 负对照**：复用生产推进路径（读代码确认）+ **我独立注入 ⇒ FAIL** ✅ + `d4t5b_recompute.py` **同口径**（Step 边界）✅。
5. **T1/T3 解析期望值**：我独立推导，与实测逐位一致（1e-7 量级）✅。
6. **Day4 五项 + 独立注入**：D4-BAN-01 真扫到灰盒并报出违规对象 ✅；D4-PHY-01 与 D4-BAN-01 互补 ✅。
7. **R2 合规**：`Day4Validator` 不调用 `Day4PhysicsSetup` ✅。
8. **负路径测试**：`TableImporterErrorPathTests`×8 已补（我上轮的缺口已关闭）✅。
9. **R1-3a 真规则**：裸 `Physics.` 已全部全限定 ✅。

## 十、未能覆盖 / 需后续注意

1. **T7（全 82 段覆盖扫描）未实现** ⇒ 76 袋角段的 TOI **零覆盖**（§7.1）。
2. **袋喉冻结路径零覆盖**（§7.2）—— 建议切片 C 加"主动朝袋口发射"的测试并在触发时断言冻结语义。
3. **T11/T12 未实现** ⇒ **积分格式（端点精确）当前未被任何测试验证**（§三）。
4. **`Docs/测试数基线台账.md` 过期**（D-1）⇒ 建议回填 77 与验收器时效行。
5. **PlayMode 未覆盖**：`WholeSteps_*`（4 项）单测了**帧累加器纯函数** ✓，但**未接真实 `FixedUpdate`** ⇒ 帧率无关性仍是**未验**（属 Day 8 前的必做项，不阻塞 Day 4）。
6. **判据 B 的可验证性**（D-2）：`DeriveSubSteps` 的 B 分支在 `a>0` 下从未被执行。
7. **我的实验边界**：注入实验在**临时副本**完成（已删），未触及入库文件（复核过：注入标记 0 处、`git diff 123b30f` 空）。
