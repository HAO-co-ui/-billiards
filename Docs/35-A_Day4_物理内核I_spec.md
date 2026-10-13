# 35-A Day 4 — 物理内核 I：定长步进 + 球-库边 · 实施 spec

| 项 | 值 |
| --- | --- |
| 版本 | **v2.1**（v1 经两道独立评审 → v2；v2 自身漏吸收的 3 条见 §0.3） |
| 状态 | 待用户放行实现 |
| 卡 | 35-A 击球手感验证（P0） |
| 排期依据 | `Docs/35-A_击球手感_制作排期.md` §Day 4 |
| 前置 | Day 1 26/26、Day 2 33/33、Day 3 22/22，四轮整改（R1/R1e/R2/R3）全部关闭，HEAD `2c1ffdc` |
| 复核强度 | 标准 + 数据交叉复核 |
| 评审记录 | `Docs/Reviews/D4-spec_独立复核.md`（可验证性）、`Docs/Reviews/D4-spec_可实现性评审.md`（可实现性） |
| 证据 | `Docs/Reports/d4-cushion-geometry-probe.json`、`d4-cushion-chain-probe.json`、`d4-spec-review-probe.json`、`d4-pocket-anchor-probe.json` |
| 探针 | `Docs/Tools/d4_cushion_geometry_probe.py`、`d4_cushion_chain_probe.py`、`d4_spec_review_probe.py`、`d4_pocket_anchor_probe.py` |

---

## 0. 本 spec 为什么必须先出而不直接开工

排期 Day 4 的验收原文是：

> **验收**：单球撞库一次反射后方向角误差 < 1°；8 m/s 高速球无穿透

按前四轮整改反复踩到的同一类问题（**「通过」不等于「正确」**）逐条验算，第一条**单独使用没有判别力**：

| 检查项 | 能测出 | **测不出** |
| --- | --- | --- |
| 反射角 < 1° | 法向取错、符号取反 | **恢复系数 e 被忽略、切向摩擦 μ 被忽略** |
| 法向分量比 = e | e 取错 | 法向错、角度错 |
| 切向分量比 = μ（带符号） | μ 取错、**切向反向** | e 取错 |
| 8 m/s 不穿透 | TOI 缺失 / 取根取错 | 法向错 |
| 自由飞行位移精确性 | **积分格式错误**（见 §0.2 A） | 反射关系错 |

只要 e 被写死成 1，反射角测试照样全绿 —— 因为纯镜面反射本来角度就守恒。五项必须成套。

### 0.1 v1 → v2 修订总表

两道独立评审共提出 20 项，其中 **18 项采纳、2 项经我实测驳回**。逐条记录，不静默修改：

| # | v1 的说法 | 判定 | v2 处置 |
| --- | --- | --- | --- |
| **A** | §4.1 `position += velocity * remaining` | **缺陷（致命）** | 改**端点精确**更新，见 §2.4。实测见 §2.4 表 |
| **B** | §2.1「误差 ~½aΔ²，**不累积**」 | **证伪** | 实测**严格线性累积**；量级由 `a` 无关的 `½·v₀·Δ` 决定 |
| **C** | §2.1「判据 A = 离散回退界」 | **名义错误** | 判据 A 测的恰是 B 的误差，只差 2 倍系数；已重述 |
| **D** | §6 判据 3 阈值 `2e-2 m` | **非自守卫** | 改 `≥ 0.5·v·dt_sub`，并入 T6 |
| **E** | §2.2「球恒在内侧」归纳 | **实证不闭合** | 附具体反例；仍**不需要**逐段朝向标注（两者须分开，见 §2.2） |
| **F** | `PocketProximityEvents` 用半径判据 | **余量仅 24 µm，无判别力** | 改**喉平面判据**，余量 ≥ 3.2 mm |
| **G** | T7 与判据 9「PocketProximityEvents==0」 | **不可同时成立** | 判据分级 + 新增**正向**断言 T13 |
| **H** | §10.1 项 5/6 指向 `TableIntegrityValidator.cs` | **指错文件** | 改指 `TableConfigTests.cs:28-34`；并**明令禁止**删除 `TableIntegrityValidator.cs:29-33` |
| **I** | §6「Day 3 的 43 个测试」 | **错** | 实测 **53** |
| **J** | §1.4 标「探针实测」但证据不在所引文件 | **缺陷** | 新增 `d4-pocket-anchor-probe.py` 作为其证据 |
| **K** | `PoolMath` 为 `internal` | **不可行** | 改 `public`（测试程序集 `overrideReferences: true`，仅引 nunit） |
| **L** | 场景注册未提 | **遗漏** | 明确 `EditorBuildSettings` 处置 |
| **M** | 测试只给角度不给发射状态 | **缺陷** | 全部补具体位置 + 速度 |
| **N** | T1 切向断言只测模长 | **盲格** | 改**带符号**断言 |
| **O** | 无自由飞行位移判据 | **盲格** | 新增 T11 |
| **P** | `remaining -= earliest` 浮点终止未定 | **遗漏** | 明确终止阈值 |
| **Q** | `MaxBallSpeed` 无落点 | **遗漏** | 指定落点 + 标注「取值未经策划确认」 |
| **R** | — | **事实补充** | 6 条长段仅铺满标称内库线的 **83.6%**，见 §1.1 |
| ~~S~~ | 评审称「Day 3 验收项 22 实为 20」 | **驳回** | 实测 `day3-verification.json` 的 `checks` 长度 = **22** |
| ~~T~~ | 评审称「需新增 `Billiards.Runtime` → `Billiards.Table` 引用」 | **驳回** | `Runtime/` 下只有**一个** asmdef，`TableConfig` 本就在 `Billiards.Runtime` 内 |

### 0.2 v2 → v2.1：我在 v2 里漏吸收的 3 条

v2 声称已采纳全部评审意见，**实际漏了 3 条**。此处补上并留档（评审报告为
`Docs/Reviews/D4-spec_可实现性评审.md`，编号 H-1…H-8）：

| ID | 条目 | v2 的错误 | v2.1 处置 |
| --- | --- | --- | --- |
| **H-3** | `SubSteps` 接口缺口 | §3 只有 `DeriveSubSteps(...)`，**没有 `SubSteps` 成员**，而 T6 判据 1 要断言 `SubSteps == DeriveSubSteps(...)` ⇒ **T6 无法实现** | §3 补 `public int SubSteps { get; }` |
| **H-5** | `a_max` 三处口径不一致（正文写 0 / 注写 0 / T6 表写 5） | T6 判据 3 在 Day 4 会**空转或算错**，且判别力范围未声明 | §2.1 新增「`a_max` 单一口径」段，钉死一个值并声明其判别力范围 |
| **H-6** | 未禁止把 `PhysicsGreybox` 写进 `EditorBuildSettings` | §8 只写了「不动 Build Settings」，**没给禁令与后果** ⇒ 实现者可能顺手加进去 | §8 改为**明文禁令**，并写明理由：会使 **Day 1 的 26/26 变成 25/26，证据作废** |

另采纳评审 §7 的四条建议（不阻塞开工，但同批补）：常量落点表（§3.1）、
生成/校验入口分离（§10.3）、证据 JSON 字段要求（§7.1）、风险补两条（§9 R-J / R-K）。

### 0.3 本轮最重的一条：积分格式

**v1 的 §4.1 写 `position += velocity * remaining`，这是前向欧拉。** 实测（`d4-spec-review-probe.json`）：

| a (m/s²) | 解析真值 (m) | v1 方案实测 | 误差 | v2 方案实测 | 误差 |
| --- | --- | --- | --- | --- | --- |
| 0.5 | 4.000000 | 4.002778 | **+2.7778 mm** | 4.000000 | **0.0000** |
| 1.0 | 2.000000 | 2.002778 | **+2.7778 mm** | 2.000000 | **0.0000** |
| 5.0 | 0.400000 | 0.402778 | **+2.7778 mm** | 0.400000 | **0.0000** |

误差**与减速度无关**，恒为 `½·v₀·Δ_sub`；随 T **严格线性增长**（0.25T→0.6944 mm、0.5T→1.3889 mm、0.75T→2.0833 mm、1.0T→2.7778 mm，与累积上界逐位吻合）。

| v₀ | SubSteps=3 | SubSteps=4 | SubSteps=6 |
| --- | --- | --- | --- |
| 2 m/s | 2.7778 mm (9.7% 球半径) | 2.0833 mm | 1.3889 mm |
| 8 m/s | **11.1111 mm (38.9% 球半径)** | 8.3333 mm | 5.5556 mm |

**加大子步数救不回来** —— 压到 1e-5 m 需 `SubSteps ≥ 3333`。唯一出路是 §2.4 的端点精确更新。

---

## 1. 既定事实（探针实测，非推断）

### 1.1 82 段的真实结构：6 段直库 + 76 段袋角侧壁

| 指标 | 实测 |
| --- | --- |
| 段长 min / 中位 / max | **4.286 / 13.958 / 1061.882 mm** |
| 短于球直径（57.15 mm）的段 | **76 / 82** |
| 短于球半径（28.575 mm）的段 | **60 / 82** |
| 76 段短段中不同长度 | 65 种（非等距离散化） |

6 段长段即 6 条直库边，距内库线 **1.03–1.50 µm**：

| index | rail | 长度 (m) | a | b |
| --- | --- | --- | --- | --- |
| 6 | end_negative | 1.061407 | (−1.270001420, +0.530703551) | (−1.270001418, −0.530703170) |
| 20 | long_negative_positive | 1.061882 | (+0.107023065, −0.635055580) | (+1.168905297, −0.635055615) |
| 33 | long_positive_positive | 1.061882 | (+1.168905443, +0.635055466) | (+0.107023116, +0.635055698) |
| 47 | long_positive_negative | 1.061882 | (−1.168905652, +0.635055929) | (−0.107023162, +0.635055737) |
| 61 | end_positive | 1.061406 | (+1.270001260, +0.530703092) | (+1.270001106, −0.530703166) |
| 74 | long_negative_negative | 1.061882 | (−1.168905658, −0.635055623) | (−0.107023339, −0.635055619) |

**两条必须明写的推论**：

1. 「82 段库边」= 6 段直库 + 76 段袋角侧壁（jaw）。**不得把袋角段按直库处理**。
2. 长库线 **x ∈ (−0.107, +0.107) 无任何段覆盖**（中袋缺口）；端库线两端各缺 **104.4 mm**。
   6 条长段合计仅铺满标称内库线周长的 **83.6%**（2×1.061882 / 2.54）。
   **一条库线可能由多段拼成、也可能有缺口 —— 禁止假设单段铺满。**

### 1.2 绕序不携带全局内侧信息（决定性）

- 以台心 (0,0) 判定 a→b 侧向：**左 39 / 右 43 / 共线 0**，不一致。
- 6 条链**链内**绕序完全统一（F=13 或 14，B=0），但**链间不统一**，且不表示内外。
- 决定性反例：`seg[33]` 与 `seg[47]` **同在 y = +0.635 这条库线上**，锚段侧号 **+1 / −1** 相反。

**结论**：数据里不存在可推导的逐段「内侧」标注。本 spec 因此**不引入**逐段朝向，也不改 schema（见 §2.2）。

### 1.3 拓扑与袋口锚点

- 88 个唯一端点，度数直方图 **{1: 12, 2: 76}** → **恰好 6 条开链**（82 + 6 = 88）。
- 每条链 = 1 段直库 + 12~13 段袋角侧壁，两端悬空于袋口。
- **12 个悬空端点距各袋关键点**（`d4-pocket-anchor-probe.json` G2）：

| 端点位置 | 距最近关键点 | 距最近 throatCenter | 喉平面判据 |
| --- | --- | --- | --- |
| (\|1.334343\|, ±0.573508) ×4 | 24.942 mm (throatB) | 90.047 mm | 台侧 −13.583 mm |
| (\|1.208454\|, ±0.699095) ×4 | 24.881 mm (throatA) | **89.868 mm** | **进网侧 +4.032 mm** |
| (\|0.094997\|, ±0.698306) ×4 | 27.626 mm (throatA/B) | **95.052 mm** | **进网侧 +3.243 mm** |

### 1.4 袋口 mouth 端点与直库段端点重合（**证据已补齐**）

`d4-pocket-anchor-probe.json` G1：6 条长段共 12 个端点 → 最近 mouth 顶点的偏差
**1.427e-6 ~ 2.097e-6 m**（最大 2.097 µm），全部落在 Day 2 已确立的 `1e-5 m` 容差内。

| 长段 | 端点 → 最近 mouth 顶点 | 偏差 |
| --- | --- | --- |
| seg[20] | a → pocket[1].mouthB / b → pocket[2].mouthA | 1.427 / 1.580 µm |
| seg[33] | a → pocket[5].mouthA / b → pocket[4].mouthB | 1.685 / 1.560 µm |
| seg[47] | a → pocket[3].mouthA / b → pocket[4].mouthA | 2.001 / 1.605 µm |
| seg[74] | a → pocket[0].mouthA / b → pocket[1].mouthA | 1.895 / 1.471 µm |
| seg[6] | a → pocket[3].mouthB / b → pocket[0].mouthB | 2.097 / 1.987 µm |
| seg[61] | a → pocket[5].mouthB / b → pocket[2].mouthB | 1.766 / 1.656 µm |

G3 全 164 端点 → 最近 mouth 顶点：仅 **24** 个在 1e-5 m 内（即 12 长段端点 ×2 的复用），
其余 140 个均 > 1e-3 m。**袋角段端点是「口内自由端」，不与 mouth 顶点重合** —— 它们指向袋喉。

### 1.5 包围盒外扩 64 mm 来自袋角段

| 极端 | 值 | 来源段 | 段长 |
| --- | --- | --- | --- |
| maxX | +1.334342911 | seg[55] | 11.057 mm |
| minX | −1.334342835 | seg[12] | 11.057 mm |
| maxY | +0.699095331 | seg[41] | 11.039 mm |
| minY | −0.699095331 | seg[26] | 11.039 mm |

---

## 2. 设计判断

### 2.1 三者的职责必须分离：TOI 抗穿透 / 积分格式决定精度 / 子步数决定回退界

**v1 在这里犯过两次错，两次都记录在案。**

**第一次（名义错误）**：v1 把「判据 A」写成「为采样点式检查兜底」。实测表明，判据 A
`v_max·dt/SubSteps ≤ 0.5·D` 所约束的量（`v·dt_sub`）与**前向欧拉的累积误差** `½·v·dt_sub`
**只差 2 倍系数** —— 我那条「兜底」判据实际测的正是真实误差，只是被我贴错标签并放宽 2 倍。

**第二次（结论错误）**：v1 声称误差「不累积」。实测（§0.2）**严格线性累积**。

**v2 定下的角色分工**：

| 机制 | 负责 | 不负责 |
| --- | --- | --- |
| **步内解析 TOI** | 抗穿透、抗嵌入 | 不负责长期位置精度 |
| **端点精确积分格式**（§2.4） | **自由飞行位置精度（误差恒为 0）** | 不负责接触点弦-弧误差 |
| **子步数 `SubSteps`** | 离散回退界、接触弦-弧误差 | **不负责自由飞行误差**（实测：加大子步数救不回来，需 n≥3333） |

**子步数推导**（`MaxBallSpeed = 8 m/s`，`dt = 1/120 s`，`D = 0.05715 m`）：

| 判据 | 计算 | 要求 | Day 4 是否生效 |
| --- | --- | --- | --- |
| A 离散回退界 | `8 × (1/120) / (0.5 × 0.05715) = 2.3333` | `SubSteps ≥ 3` | ✅ |
| B 接触弦-弧误差界 | `(1/120) / √(2×5e-5/5) = 1.8634` | `SubSteps ≥ 2` | ❌（a = 0，Day 7 生效） |

取 `SubSteps = max(3, 2) = 3`（`dt_sub = 1/360 s`）。核验：子步位移 @8 m/s = **22.222 mm ≤ 28.575 mm** ✅。

> **判据 B 的地位说明（不可省略）**：判据 B 的解析式 `½·a·dt_sub²` 只是**量级估计**。
> 实测（`d4-spec-review-probe.json` 1b，方案 B，a=0.5，3 次接触）残差 **9.4 µm**，
> 而解析估计为 `aΔ²/8 × 3 ≈ 1.4 µm` —— **解析式低估约 6.5 倍**。
> 因此 **判据 B 不得当作保证使用**；Day 7 引入真实减速度后必须以**实测漂移**替代，
> 见 §5 T14。Day 4 把它写进 T6 只是为了固定式子与断言位置。

**`MaxBallSpeed` 落点**：`PoolConstants`（单位制与全局硬指标唯一真源），取值 **8 m/s**，
依据策划卡「初速 2–8 m/s」的上界。**⚠ 该值未经策划确认**，Day 7 闸门需回头核对；
T6 的不变量式保证它一旦被改动，子步数推导会同步失败（自守卫）。

**`a_max` 的单一口径（v2 三处不一致，H-5 —— 本节为唯一真源）**：

| 用途 | 取值 | 说明 |
| --- | --- | --- |
| **运行时积分**（`PhysicsParams.rollingDeceleration`） | **Day 4 = 0** | Day 4 不实现滚阻，球不减速 |
| **T6 判据 3 的断言** | **5 m/s²** | 这是**前瞻性**声明的上界，用来固定式子与断言位置，**不参与 Day 4 的运行时计算** |

两者的关系必须写清：判据 3 断言的是「**若** `a` 取到声明上界 5，本 `SubSteps` 仍满足界」，
因此它在 Day 4 **不是空转** —— 它约束的是 `SubSteps` 的选取，而不是当下的仿真行为。

**判别力范围声明**：`a_max = 5 m/s²` 是一个**未经实测校准的保守猜测**（真实滚阻量级更接近
0.2–0.5 m/s²）。它只影响 `SubSteps` 的下界。若 Day 7 实测 `a` 超 5，判据 3 失败 ⇒ 提示重推子步数；
若远低于 5，则 `SubSteps` 可能偏保守（浪费算力，不影响正确性）。**不构成正确性风险。**


### 2.2 接触法向来自接触几何 —— 但归纳不闭合，两件事必须分开说

**结论 A（仍然成立，且是本 spec 的核心简化）**：
> 圆 vs 线段的碰撞法向 = `normalize(C − Q)`，`Q` 为线段上距球心最近点。
> 球从内侧撞上来时该向量恒为内法向，**无需任何预计算朝向** ⇒ **不改 schema**。

**结论 B（v1 的归纳，已被实证推翻）**：
> ~~「球恒在库边内侧」~~

**具体反例（`d4-spec-review-probe.json` 任务 2 实测）**：
```
起点 (0, 0)，v = (0, −2) m/s，沿 x = 0 直下，正对 side pocket[1]
  到全部 82 段的最小距离 = 0.0675617 m  (seg[15] @ y = −0.695060)
  球半径 = 0.028575 m  ⇒  0.06756 > 0.028575  ⇒  全程无接触
  neverContactsAnySegment = True
```
中袋缺口 `x ∈ (−0.107, +0.107)` 宽 214.048 mm，球径仅 57.15 mm ⇒ **直接穿出，无阻拦**。

**为什么结论 A 不受影响**：该球在袋口区域内**不与任何段接触**，因此 `C − Q` 根本不被求值。
「法向来自接触几何」只在**发生接触**时被使用，而那时球必然在内侧。结论 A 成立。

**结论 B 不成立带来的实际后果**（必须在 spec 中显式处理）：

1. §3 的红线「球位—段距离 ≥ r」**不能用抛异常实现** —— 合法的贴库静止球、开球点
   （`TableConfig.asset:372` `footSpot = {x: 0.51, y: 0}`）都可能落在边界上。
   改为：**Validator 拒绝 `< r − 1e-4 m` 的初始摆放**（真重叠），**允许 `≥ r − 1e-4 m`**。
2. **越界必须有确定处置**（v1 完全没写）：球心越过任一袋喉平面后，
   **冻结该球（速度置 0）、停止其积分、累计 `PocketProximityEvents`**，
   并保留其最后位置供 Day 6 接管。**不得静默继续积分**。

### 2.3 袋口区域的判据：喉平面，不是半径

v1 定义 `PocketProximityEvents` 为「球心进入某袋 `throatCenter` 的 `openingRadius + BallRadius` 内」
（半径 = 89.892 mm）。实测该判据**没有判别力**：

| 12 个悬空端点 | 半径判据 | 喉平面判据 |
| --- | --- | --- |
| (\|1.334343\|, ±0.573508) ×4 | 90.047 mm → 不触发 | −13.583 mm → 台侧 |
| (\|1.208454\|, ±0.699095) ×4 | **89.868 mm → 触发** | +4.032 mm → 进网侧 |
| (\|0.094997\|, ±0.698306) ×4 | 95.052 mm → 不触发 | +3.243 mm → 进网侧 |
| **触发数** | **4/12，边界余量仅 24 µm** | **8/12，余量 3.2–4.0 mm** |

半径判据在边界上等于掷硬币（余量 24 µm）；喉平面判据的余量大 **130–160 倍**。

**v2 定义**：
```
PocketProximityEvents 在球心满足 dot(C − pocket.throatCenter, pocket.throatNormal) > 0 时 +1
```
理由：这正是数据集自带 `captureNote` 的语义（「球心穿过袋喉朝网方向」），
且对本题几何有 3.2 mm 以上的余量。**注意：这是诊断项，不是错误信号** ——
袋角侧壁本身就有 8/12 位于进网侧，球擦袋角是正常击球。

### 2.4 积分格式是硬要求，不是实现细节

**位置更新必须写成端点精确形式**：

```
Δ = 子步长
a 沿 −v̂ 方向（减速度）
p += (v − ½·a·Δ·v̂) · Δ        ← 梯形/中点，等价于 p += v·Δ − ½·a·Δ²·v̂
v -= a·Δ·v̂
```

**并且用于 TOI 求解的必须是同一个均速 `v_avg = v − ½·a·Δ·v̂`**，否则积分与接触求解不一致。

实测：自由飞行误差 **恒为 0**（§0.2 表）；多库弹跳残差 **9.4 µm**（a=0.5、e=0.95、3 次接触）。

**反向警示**：此题的自然错误模式是「改了一处、漏了另一处」——
例如只在位置更新里加了 `−½aΔ²` 而 TOI 仍用 `v_start`，或反之。§5 T12 专测此事。

---

## 3. 接口 spec

新增目录 `Assets/_Project/Scripts/Runtime/Physics/`。

> **程序集说明（v1 表述有误已修正）**：`Assets/_Project/Scripts/Runtime/` 下**只有一个**
> `Billiards.Runtime.asmdef`，`Core/`、`Table/` 均无自己的 asmdef ⇒ `TableConfig`
> **本就在 `Billiards.Runtime` 内**。因此 `PoolSimulator` 引用 `TableConfig` **不需要新增 asmdef 引用**。
> 测试程序集 `Billiards.Tests.EditMode` 已引用 `Billiards.Runtime`，新增测试文件同目录即可。

```csharp
namespace Billiards.Physics
{
    public struct BallState { public Vector2 position; public Vector2 velocity; }

    public struct PhysicsParams
    {
        public float cushionRestitution;        // e
        public float cushionTangentialFriction; // μ
        public float rollingDeceleration;       // a，m/s²；Day 4 = 0
    }

    /// 所有开方/三角运算的唯一出口。
    /// **必须是 public**：Billiards.Tests.EditMode 设了 overrideReferences=true 且
    /// precompiledReferences 仅 nunit.framework.dll，internal 对本项目测试不可见
    /// （走 InternalsVisibleTo 需额外改 asmdef，不值得）。
    public static class PoolMath { /* Sqrt, Atan2 ... 统一单精度 */ }

    public sealed class PoolSimulator
    {
        public const float FixedDeltaTime = 1f / 120f;
        public const int   MaxToiIterations = 16;
        public const float ToiTimeEpsilon   = 1e-9f;   // 同刻接触归并窗口
        public const float ToiRemainingEpsilon = 1e-9f; // 子步剩余时间终止阈值

        public static int DeriveSubSteps(float maxBallSpeed);

        /// **T6 判据 1 的断言对象（v2 缺失，H-3）**。值 = DeriveSubSteps(PoolConstants.MaxBallSpeed)。
        public int SubSteps { get; }
        public float SubStepDeltaTime { get; }     // = FixedDeltaTime / SubSteps

        public PoolSimulator(TableConfig table, PhysicsParams parameters);

        public int SegmentCount { get; }
        public void LoadBalls(IReadOnlyList<BallState> states);
        public IReadOnlyList<BallState> Balls { get; }
        public void Step();                        // 推进一个 FixedDeltaTime

        // ── 诊断（分级见 §6 判据 9）────────────────────────────
        public int ToiIterationGuardTrips { get; }   // 硬断言为 0
        public int EnergyGainViolations   { get; }   // 硬断言为 0
        public int PocketProximityEvents  { get; }   // 分级：台面内测试断言 0；袋口区测试断言 >0
        public int TotalContactCount      { get; }
        public void ResetDiagnostics();

        // 仅供 T5b 负对照；**必须复用生产推进路径**（见 §5 T5b）
        public bool DisableToiForDiagnosticsOnly { get; set; }
    }
}
```

**契约**：

1. **不得**引用 `Time.deltaTime`、`UnityEngine.Random`、`Physics`（PhysX）。时间由外部累加器驱动。
2. 状态推进**固定顺序**（球索引升序、段索引升序），禁 `HashSet`/`Dictionary` 迭代序。
3. 开方/三角走 `PoolMath`。
4. **TOI 循环终止条件必须是 `remaining <= ToiRemainingEpsilon`**，不能写 `remaining > 0` ——
   浮点下 `remaining -= earliest` 可能永不归零（v1 遗漏）。
5. 构造时把 `List<CushionSegment>` 摊平为 `Vector2[164]`（顺序 = 资源顺序 = JSON 顺序，参与确定性）。
6. 球心越过袋喉平面后**冻结该球**并计数，不得继续积分（§2.2 后果 2）。

### 3.1 常量落点表（评审 §7-2 建议；v1/v2 均缺）

Day 4 依赖的**全部**常量及其真源。实现时不得自行造值。

| 常量 | 落点 | 状态 | 值 |
| --- | --- | --- | --- |
| `BallRadius` / `BallDiameter` | `PoolConstants` | **已存在** | 0.028575 / 0.05715 m |
| `ClothInnerLength` / `Width` | `PoolConstants` | **已存在** | 2.54 / 1.270108866 m |
| `CushionSegmentCount` / `PocketCount` | `PoolConstants` | **已存在** | 82 / 6 |
| `PocketOpeningRadius` / `PocketCaptureRadius` | `PoolConstants` | **已存在** | 0.061316636 / 0.030741636 m |
| 82 段端点、6 袋几何 | `TableConfig.asset` | **已存在** | 见 §1 |
| `footSpot` | `TableConfig.asset` | **已存在** | (0.51, 0) |
| **`MaxBallSpeed`** | **`PoolConstants`** | **需新增** | **8 m/s**（未经策划确认） |
| `FixedDeltaTime` | `PoolSimulator` | 本 spec 定义 | 1/120 s |
| `SubSteps` | `PoolSimulator`（推导） | 本 spec 定义 | 3 |
| `MaxToiIterations` / `ToiTimeEpsilon` / `ToiRemainingEpsilon` | `PoolSimulator` | 本 spec 定义 | 16 / 1e-9 s / 1e-9 s |
| `cushionRestitution` / `cushionTangentialFriction` | `PhysicsParams` | 本 spec 定义 | Day 4 取值见 T1（测试注入）；正式值 Day 7 |
| `rollingDeceleration` | `PhysicsParams` | 本 spec 定义 | Day 4 = 0 |
| `a_max`（仅 T6 断言用） | `PoolSimulator` 常量 | 本 spec 定义 | 5 m/s²（§2.1） |

---

## 4. 求解过程 spec

### 4.1 单个子步（v2 修正版）

```
StepSubStep(dtSub, params):
    for ball in Balls (索引升序):
        if ball 已冻结 or ball.v == 0: continue

        Δ  = dtSub
        a  = params.rollingDeceleration                        # Day 4 = 0
        v̂  = normalize(ball.velocity)
        v_avg = ball.velocity - 0.5 * a * Δ * v̂               # ★ 均速，位置与 TOI 共用
        if dot(v_avg, ball.velocity) < 0: v_avg = Vector2.zero # 本子步内会停住

        remaining = Δ
        iterations = 0
        while remaining > ToiRemainingEpsilon:
            if ++iterations > MaxToiIterations:
                ToiIterationGuardTrips++; break                # 不静默，见 §6

            earliest = +inf; group = []
            for i in 0..81 (升序):
                t = SolveToi(ball.position, v_avg, segment[i], BallRadius)   # ★ 用 v_avg
                if t in [0, remaining]:
                    if t < earliest - ToiTimeEpsilon: earliest = t; group = [i]
                    elif |t - earliest| <= ToiTimeEpsilon: group.append(i)

            if group is empty:
                ball.position += v_avg * remaining                 # ★ 端点精确
                break

            ball.position += v_avg * earliest                      # ★ 精确推进到接触时刻
            remaining -= earliest
            ResolveContact(ball, group, params)                    # 见 4.2
            # 接触后按新速度重取 v_avg（子步内可多次接触）
            if a > 0:
                v̂ = normalize(ball.velocity)
                v_avg = ball.velocity - 0.5 * a * remaining * v̂
                if dot(v_avg, ball.velocity) < 0: v_avg = Vector2.zero

        if a > 0:
            v̂ = normalize(ball.velocity)
            dv = a * Δ
            ball.velocity = Vector2.zero if |ball.velocity| <= dv else ball.velocity - dv * v̂

        CheckPocketProximity(ball)                                 # §2.3
```

### 4.2 接触解算

对组内每段（**按段索引升序**）：

```
Q   = ClosestPointOnSegment(ball.position, seg)
n̂   = normalize(ball.position - Q)
      ├─ 若 |ball.position - Q| < 1e-9 → 退化（§2.2 情形 1）：n̂ = -v̂
      └─ 否则若 n̂ 与上一次接触法向相反 → 记录（供 §2.3 统计）
v_n = dot(v, n̂);  v_t_vec = v - v_n * n̂;  t̂ = normalize(v_t_vec)

v = v - (1 + e) * v_n * n̂            # 法向：独立赋值
if |v_t_vec| > 0:
    v = v - (1 - μ) * v_t_vec         # 切向：独立赋值（注意：**不是** v = reflect(v) * μ）
```

> **切向写法的陷阱（v1 未写，为 N/E 两项盲格的根因）**：
> `v = reflect(v) * μ` 会把 μ 也乘到法向分量上，产生「切向测试通过、法向 e 被污染」的假通过。
> **法向与切向必须各自独立赋值。** T1 同时断言两者。

### 4.3 不变量（v2 分成两组，消除 v1 的量纲混用）

**组 ①：模型内自洽**（模拟器 vs 它自己的线性模型）—— **必须精确成立**

| 不变量 | 断言 |
| --- | --- |
| 接触后球心距该段 = r | `|dist(C, seg) − r| ≤ 1e-6 m` |
| 接触后法向速度反向且衰减 | `sign(v·n̂)` 翻转；`|v·n̂| ≤ e·|v_in·n̂| + 1e-6` |
| 无升能 | `|v_out| ≤ |v_in|·(1 + 1e-6)` |
| 自由飞行位移精确 | `p_end = p_start + v_avg·Δ`（浮点级） |

**组 ②：模型 vs 真值**（模拟器 vs 连续解析解）—— **只能实测，不能仅凭公式声称**

| 量 | v1 声称 | v2 实测/估计 |
| --- | --- | --- |
| 自由飞行累积误差 | ~~½a·dt_sub²「不累积」~~ | **已消除**（端点精确更新）→ 实测 0.0000 mm |
| 单次接触弦-弧误差 | ½a·t*² | 解析估计 `aΔ²/8`；**实测值高约 6.5 倍** |
| 多次接触累积漂移 | ~~有界~~ | **实测 9.4 µm**（a=0.5, e=0.95, 3 次接触）|

> v1 把组 ② 的量写进 §4.3 组 ① 的位置，造成「1e-6 m 与 5e-5 m 相差 20 倍不能同时成立」的表述冲突。
> 二者测的是不同的东西，v2 显式分列。

---

## 5. 测试族 —— 每条含**具体发射状态**、声明**能测出/测不出**

> 坐标系：规范二维物理平面，x 长轴、y 短轴，单位米。
> 除非另有说明，球半径 0.028575 m，`dt = 1/120 s`，`SubSteps = 3`。
> **每条测试的期望值必须来自解析式或独立推导，不得读取模拟器内部量。**

### T1 分量分解守恒（核心项）

**发射状态**（撞击 `seg[20]`，y = −0.6350556，x ∈ [0.107023, +1.168905]，接触点取 x = 0.6）：

| 入射角 | 起点 | 速度 (m/s) |
| --- | --- | --- |
| 30° | (0.859808, −0.485056) | (−1.732051, −1.000000) |
| 60° | (0.750000, −0.375248) | (−1.000000, −1.732051) |

**注入** `e = 0.8`、`μ = 0.9`（**必须都不是 1**）。`n̂ = (0, 1)`。

**期望（解析）**：`v_out·n̂ = −e·(v_in·n̂)`；`v_out·t̂ = μ·(v_in·t̂)`（**带符号**）。

30° 情形：`v_in·n̂ = −1.0` → `v_out·n̂ = +0.8`；`v_in·t̂ = −1.732051` → `v_out·t̂ = −1.558846`。

- **判据**：相对误差 ≤ 1e-4
- **能测出**：法向方向错、符号反、n/t 分解错、e 未应用、μ 未应用、**μ 污染法向**、**切向方向反转**
- **测不出**：e/μ 的取值是否合理（Day 7 职责）

### T2 e 扰动判别（证明 T1 不是空转）

同 T1 的 30° 发射，跑两次：`e = 1.0` 与 `e = 0.8`，`μ` 固定。
断言法向分量之差 = `0.2 × |v_in·n̂|`（容差 1e-4）。
**能测出**：把 e 写死为 1 的实现（两次结果完全相同 → 失败）。

### T3 镜面角守恒（排期原文验收项，附边界声明）

`e = 1`、`μ = 1`；入射角 15°/30°/45°/60°/75°，撞击 6 条直库各一次（接触点取各段中点）。
替换 T1 的 `e`、`μ` 即得发射状态。
**判据**：`|θ_out − θ_in| < 1.0°`（相对库边切向）。
**测不出**：**e 或 μ 的任何错误** —— 这是排期原文那条验收的已知盲区，由 T1/T2/T4 补齐。

### T4 μ 扰动判别

同 T2，`μ = 1.0` vs `0.8`，断言切向分量之差 = `0.2 × |v_in·t̂|`（**带符号**）。

### T5 无穿透 / 无嵌入（含负对照）

**T5a 正向**：`v = 8 m/s`，自各直库中点的内法向 30 mm 处正撞。断言：
- 球心到**该段**的带符号距离符号**从不翻转**（**断言对象是该段，不是「库线」** ——
  袋角段不共线，v1 的「库线」表述在几何上无定义）；
- 全轨迹最大嵌入量 ≤ **1e-5 m**；
- `ToiIterationGuardTrips == 0`、`EnergyGainViolations == 0`。

**T5b 负对照（关键）**：`DisableToiForDiagnosticsOnly = true`，同一发射。
断言最大嵌入量 **≥ 0.5 × v × dt_sub**。

| SubSteps | `v·dt_sub` | 判据 `0.5·v·dt_sub` |
| --- | --- | --- |
| 3（当前） | 22.222 mm | **≥ 11.111 mm** |
| 4 | 16.667 mm | ≥ 8.333 mm |
| 6 | 11.111 mm | ≥ 5.556 mm |

> v1 写死 `≥ 2e-2 m`：SubSteps=3 时余量仅 11%，**SubSteps 升到 4 即落空** ——
> 而 §2.1 同时要求 SubSteps 是推导值（可能随 `MaxBallSpeed` 变化）
> ⇒ 会制造「不可达条款」式假通过。v2 改为公式化并入 T6。

**T5b 有效性硬要求**：禁用模式**必须复用生产推进函数**，仅替换接触检测为「子步末采样」。
若实现为另一套独立路径，则它测出的穿透量**不能证明**生产路径缺 TOI 会穿透，负对照退化为
「另一个实现的失败」。**T5 声称能测「TOI 缺失/取远根」，其判别力完全借 T5b 之口 —— 故此项不可放宽。**

### T6 不变量（自守卫）

| # | 不变量 | 当前值 |
| --- | --- | --- |
| 1 | `SubSteps == DeriveSubSteps(MaxBallSpeed)` | 3 == 3 |
| 2 | `MaxBallSpeed·dt/SubSteps ≤ 0.5·BallDiameter` | 22.222 ≤ 28.575 mm |
| 3 | `½·a_max·(dt/SubSteps)² ≤ 5e-5 m` | 19.29 ≤ 50 µm（`a_max = 5`，唯一真源见 §2.1；**Day 4 运行时 a = 0，本条只约束 SubSteps 选取**） |
| 4 | `DeriveSubSteps` 边界语义 | 比值**恰为整数**时取该整数（`ceil` 语义），不以 `<` 取代 |
| 5 | T5b 判据 `0.5·v·dt_sub` 随 SubSteps 联动 | 断言 `0.5·v·dt_sub < v·dt_sub` 且 > 0 |

**能测出**：调高 `MaxBallSpeed` 却不重推；把 `SubSteps` 改成硬编码；SubSteps 变化后 T5b 判据失效。

### T7 全 82 段覆盖扫描（与判据 9 的关系已重新界定）

**瞄准点**：每段中点 + 两端点 = **246 个**。自台心与 4 个象限内点共 5 个种子出发。

**产出**：`Docs/Reports/d4-segment-coverage.json` —— 逐段命中次数、首次命中段、最大嵌入量、
升能触发、迭代守卫触发。

**判据**：
- 6 条直库段每段命中 > 0（**硬门槛**）；
- 76 条袋角段命中数**逐条枚举**，未命中的段列明原因（被遮挡 / 仅袋口区可达 → 归 Day 6）；
  **不允许只报总数**（铁律 17）。
- **`PocketProximityEvents` 在 T7 中不作硬断言，只作报告项。**

> **为什么分级（实测依据）**：246 个瞄准点中 **60 个已位于某袋喉平面之进网侧**
> （涉及 26 个段，超出量 0.000–4.032 mm）。T7 必然打到袋口区域，
> 与「`PocketProximityEvents == 0`」**不可同时成立**。把冲突改成两条各自可满足的断言：
> T7 报告（本项）+ T13 正向断言。

**特别价值**：每个内部段端点都是两段共享的角点，本扫描**直接实测角点同时接触的行为**，
是 `EnergyGainViolations` 的主要来源探测器。

### T8 端点退化

球心正对某段端点发射，使 `Q` 落在端点上。断言法向有限、模长为 1、无 NaN、位置不跳变。

### T9 确定性

同初始状态跑两次 `Step()` × 1000，最终 `position`/`velocity` 逐位一致（`float` 的 `R` 格式比较）。

### T10 接触点解析校验（复核方独立推导项）

球自 `(0.5, −0.4)` 以 `v = (0, −2)` 垂直撞 `seg[20]`。
期望（纯几何）：`t = (|−0.4 − (−0.635056)| − r) / 2`；接触点 y 恒为 `−0.635056 + r`。
判据：`t` 误差 ≤ 1e-9 s；接触点 y 误差 ≤ 1e-9 m。

### T11 自由飞行位移精确性（v2 新增 —— 补 N/O 盲格）

`a = 0`；球自 `(−0.5, 0.3)`，`v = (1.3, −0.7)`；跑 `N = 120` 步（1 s）。
断言 `|p_end − (p₀ + v·N·dt)| ≤ 1e-6 m`。
再加一组 `a = 1.0`（合成参数）跑 `T = 0.5 s`，断言
`|p_end − (p₀ + v·T − ½·a·T²·v̂)| ≤ 1e-6 m`。

**能测出（v1 完全无覆盖）**：`remaining` 递减写错、漏一轮子步、
**误用 `dt` 而非 `dt_sub`**、位置更新漏掉 `−½aΔ²` 项 —— 即纯位移量级的 bug。

### T12 积分格式的终点精确性（有限差分自检）

对同一初态，用「位移法」与「速度法」各推进一个子步（`a = 1.0` 合成）：
`(p₁ − p₀)/Δ` 必须等于 `(v₀ + v₁)/2`，容差 1e-9。

**能测出**：只在位置更新里加 `−½aΔ²` 而 TOI 仍用 `v_start`（或反之）—— §2.4 点名的自然错误模式。

### T13 袋口区域（v2 新增 —— 正向断言）

**发射状态**（即 §2.2 的实证反例）：自 `(0, 0)`，`v = (0, −2) m/s`。
断言：`PocketProximityEvents ≥ 1`；球最终被冻结；末态位置在 pocket[1] 喉平面之进网侧。

**能测出**：§2.3 的判据实现错误；越界处置缺失；球穿出后仍被继续积分（速度未冻结）。

### T14 长期漂移实测（Day 7 生效，Day 4 先建骨架）

`a > 0`，跑一次多库多次接触的长局面，与**解析解逐次接触**对比，把**实测最大漂移**写进
`Docs/Reports/d4-drift-measured.json`。
**Day 4 用 a = 0 跑，断言漂移 = 0**（此时无减速度，任何非零漂移都是 bug）。

---

## 6. 验收判据

| # | 判据 | 门槛 |
| --- | --- | --- |
| 1 | T3 镜面角误差（排期原文） | < **1.0°** |
| 2 | T5a 8 m/s 最大嵌入量 | ≤ **1e-5 m** |
| 3 | T5b 负对照最大嵌入量 | **≥ 0.5·v·dt_sub**（公式化，当前 = 11.111 mm） |
| 4 | T1 分量相对误差（带符号） | ≤ **1e-4** |
| 5 | T2 / T4 扰动差值 | 与解析差 ≤ **1e-4** |
| 6 | T6 五条不变量 | 全部成立 |
| 7 | T7 六条直库段命中 | 每段 > 0 次 |
| 8 | T8 / T9 / T10 | 无 NaN、逐位一致、解析误差 ≤ 1e-9 |
| 9 | **T11 / T12 位移精确性** | 误差 ≤ **1e-6 m** / **1e-9** |
| 10 | `ToiIterationGuardTrips` / `EnergyGainViolations` | **全部 == 0**（硬断言，全测试族） |
| 11 | `PocketProximityEvents` | **分级**：T1–T5、T8–T12 硬断言 **== 0**；T7 **报告 + 逐点枚举**；T13 硬断言 **≥ 1** |
| 12 | EditMode 测试总数 | 全绿；总数 = 实测基线 **53** + 本 spec 新增 |
| 13 | T14（Day 4 口径，a = 0） | 漂移 **== 0** |

**判据 3 与判据 11 是本 spec 里仅有的两条「分级/反向」判据**，其存在理由已分别在 §5 T5b 与 §5 T7 说明 ——
缺少它们，对应判据无法自证不是空转。

---

## 7. 证据文件

| 文件 | 内容 | 状态 |
| --- | --- | --- |
| `Docs/Reports/d4-verification.json` | 验收门结果 | 待产出 |
| `Docs/Reports/d4-editmode-tests.xml` | EditMode 结果 | 待产出 |
| `Docs/Reports/d4-penetration-scan.json` | T5 逐段嵌入量 + T5b 负对照 | 待产出 |
| `Docs/Reports/d4-segment-coverage.json` | T7 覆盖率 + 未命中段枚举 + 袋口区射击枚举 | 待产出 |
| `Docs/Reports/d4-drift-measured.json` | T14 漂移实测 | 待产出 |
| `Docs/Reports/d4-cushion-geometry-probe.json` | §1.1 / 1.2 / 1.5 | **已产出** |
| `Docs/Reports/d4-cushion-chain-probe.json` | §1.2 链结构与绕序 | **已产出** |
| `Docs/Reports/d4-pocket-anchor-probe.json` | **§1.3 / 1.4（证据归属修正）** | **已产出** |
| `Docs/Reports/d4-spec-review-probe.json` | **§0.2 / §2.2 / §2.3 的实测** | **已产出** |
| `Docs/Reviews/D4-T01_独立确认.md` | 复核方独立确认 | 待产出 |

### 7.1 每份证据 JSON 的必填字段（评审 §7-5 建议；v1/v2 均缺）

复核方需按此逐字段对照，字段缺失即视为证据不完整。

| 文件 | 必填字段 |
| --- | --- |
| 全部 | `schema`、`generatedAtUtc`、`unityVersion`、`projectRoot`、`isBatchMode`、`passCount`/`warnCount`/`failCount`、`overallStatus`、`checks[]`（沿用 `ProjectSetupReport`） |
| 全部 `checks[]` 条目 | `id`、`name`、`expected`、`actual`、`detail`、`status` |
| `d4-penetration-scan.json` | 逐段 `segmentIndex`、`maxPenetration`、`signedDistanceSignFlipped`、`negativeControlMaxPenetration`、`threshold_0p5vdt`、`subSteps`、`subStepDeltaTime` |
| `d4-segment-coverage.json` | 246 个瞄准点逐点 `segmentIndex`/`aimTag`/`seed`/`hitSegment`/`maxPenetration`/`energyGain`/`guardTrips`/`pocketPlaneNetSide`；汇总 `unhitSegments[]` 且每条带 `reason` |
| `d4-drift-measured.json` | `a`、`e`、`contactCount`、`measuredMaxDrift`、`analyticEstimate`、`ratio` |
| 探针类（`d4-*-probe.json`） | 已产出，含 `source`、`sourceSha256` 与源路径；**`sourceSha256` 必须标明是「源 FBX 哈希」还是「参考文件哈希」**（复核 ① 指出 `d4-cushion-geometry-probe.json` 的 `sourceSha256Declared` 语义歧义，需拆成两个字段） |

---

## 8. 明确非目标（Day 4 不做的）

| 不做 | 归属 |
| --- | --- |
| 球-球碰撞 | Day 5 |
| 袋口捕获判定、袋角偏折 | Day 6（Day 4 只**冻结**越界球并记录） |
| 运动终止判定（速度阈值 + 稳定帧数） | Day 6 |
| 8 球规则 | Day 7 之后 / 35-B |
| 台呢滚阻、初始阻尼、自旋/塞 | Day 7 |
| `PhysicsTuning.asset` 参数外置 | Day 7 |
| 瞄准 UI、力度条、击球操作 | Day 8+ |
| 球杆与球的碰撞 | Day 8+ |
| 中文字体 | 正式版待确认项 |

**场景策略**：新建 `Assets/_Project/Scenes/PhysicsGreybox.unity`，**不修改 `Main.unity`**
（后者已由 Day 3 验收 22/22 冻结）。

> **明文禁令（H-6，v2 只写「不动」未给禁令）**：
> **`Day4PhysicsSetup` 严禁把 `PhysicsGreybox.unity` 写入 `EditorBuildSettings`。**
> 理由：Day 1 的验收项 **`SCN-02`** 断言 build list 中恰好只有 `Main.unity` 一个场景。
> 一旦加入灰盒场景，**Day 1 的 26/26 会变成 25/26，其验收证据立即作废** ——
> 而 Day 1 是本卡全部后续工作的基准。灰盒场景**通过编辑器直接双击打开即可**，无需进 build list。
> 若将来确实需要注册，**必须作为独立变更同时修订 Day 1 验收口径**，不得夹带在 Day 4 内。

---

## 9. Day 4 风险登记

| # | 风险 | 概率 | 影响 | 缓解 |
| --- | --- | --- | --- | --- |
| R-A | **积分格式写成前向欧拉**，v=8 时系统性多走 11.11 mm | **高** | **致命** | §2.4 硬要求 + T11/T12 专测 + §0.2 实测数据留档 |
| R-B | 球从袋口穿出后仍被积分，产生乱数 | 中 | 高 | §2.2 后果 2（冻结）+ §2.3 喉平面判据 + T13 |
| R-C | 角点同时接触两段时顺序解算升能 | 中 | 中 | `EnergyGainViolations` 计数 + T7 逐端点扫描 |
| R-D | TOI 循环不收敛 / `remaining` 浮点不归零 | 中 | 中 | `MaxToiIterations` + `ToiRemainingEpsilon` + **计数器断言为 0** |
| R-E | 反射角测试全绿但 e/μ 未生效 | **高** | 高 | T1 用非 1 的 e/μ + 带符号断言 + T2/T4 扰动判别 |
| R-F | `C == Q` 退化产生 NaN | 低 | 高 | `−v̂` 回退 + Validator 用 `< r − 1e-4` 判重叠 + T8 |
| R-G | Day 3 场景被改动导致 22/22 证据作废 | 中 | 中 | 新建 `PhysicsGreybox.unity` |
| R-H | 局部坐标分量是间距（非绝对），float 精度被低估 | 低 | 中 | 已核实：局部坐标 1e-7 相对精度 ⇒ 位移分辨 1.2e-7 m，远低于阈值，**不构成风险**（评审正确项） |
| R-I | 判据 B 的解析式被当作保证使用（实测低估 6.5 倍） | 中 | 中 | §2.1 显式标注「不得当作保证」+ T14 实测替代 |
| R-J | `PhysicsGreybox.unity` 误入 `EditorBuildSettings` ⇒ **Day 1 的 26/26 变 25/26，证据作废** | 中 | **高** | §8 明文禁令 + 理由；Day 4 验收门增加一条断言：build list 场景数 == 1 |
| R-K | `SubSteps` / `MaxBallSpeed` 的关系被改动后失去同步 | 中 | 中 | §3 同时暴露 `SubSteps` 与 `DeriveSubSteps`；T6 判据 1 断言二者一致 |

---

## 10. 交付物清单

| # | 交付物 | 类型 |
| --- | --- | --- |
| 1 | `Runtime/Physics/BallState.cs` | 运行时代码 |
| 2 | `Runtime/Physics/PhysicsParams.cs` | 运行时代码 |
| 3 | `Runtime/Physics/PoolMath.cs`（public） | 运行时代码 |
| 4 | `Runtime/Physics/PoolSimulator.cs` | 运行时代码 |
| 5 | `Runtime/Physics/CushionGizmo.cs` | 运行时代码 |
| 6 | `Runtime/Physics/PhysicsTestDrive.cs` | 运行时代码 |
| 7 | `Runtime/Core/PoolConstants.cs` **增补 `MaxBallSpeed`** | 运行时代码（改动既有文件） |
| 8 | `Editor/Visual/Day4PhysicsSetup.cs` | 编辑器脚本（幂等建场景） |
| 9 | `Editor/Visual/Day4Validator.cs` | 编辑器脚本（验收门） |
| 10 | `Tests/EditMode/PoolSimulatorTests.cs`（T1–T4、T8–T13） | 测试 |
| 11 | `Tests/EditMode/PoolSimulatorInvariantTests.cs`（T5–T7、T14） | 测试 |
| 12 | `Scenes/PhysicsGreybox.unity` | 场景（代码生成） |
| 13 | §7 全部证据文件 | 证据 |
| 14 | `Docs/35-A_Day4_物理内核I_执行记录.md` | 执行记录 |

### 10.1 Day 4 窗口内结转的 4 项残留（**v2 已更正指向**）

v1 把项 5/6 指向 `TableIntegrityValidator.cs`，**经实测为误**：

| # | 残留 | 正确位置 | 处置 |
| --- | --- | --- | --- |
| 5 | 墓碑注释（第 2 处） | **`Assets/_Project/Tests/EditMode/TableConfigTests.cs:31`** | 该行位于**重复的 `<summary>`** 中 |
| 6 | 重复 `<summary>` | **`TableConfigTests.cs:28-34`**（28-32 与 33-34 两段紧邻） | 合并为一段，保留证伪内容 |
| — | `TableIntegrityValidator.cs:29-33` | **不是墓碑，是现行有效的证伪警告**（引 ADR-0001 §9.5） | **严禁删除**。v1 若照原文执行会删掉一个正确警告 |
| 7 | 同类残留的**主张级**再枚举 | 全仓 | 逐条问「哪些主张已被推翻/撤回？」，不是关键词匹配 |
| 8 | 构造「陈旧资产」场景，证明 `ValidateInteractive()` 与 `RebuildAndValidateInteractive()` **行为可区分** | `TableIntegrityValidator.cs` | 见 §10.2 |

### 10.2 项 8 的可执行方案（评审要求给步骤，非思路）

R3 判据退化的成因：`TableDataImporter.Import()` **幂等**，资产最新时两个入口都不写文件 ⇒
「菜单入口不改产物」这条断言**对两个版本都成立**，无判别力。

**最小可区分构造**：

1. 备份 `Assets/_Project/Data/TableConfig.asset` 到 `Temp/`（**不放进版本库**）。
2. 制造陈旧态：用一个临时脚本把 `TableConfig.asset` 的 `sourceSha256` 字段改成一个假值
   （或减小某段端点 1e-3 m —— 取**不影响合法性检查**的一项，以便两个入口的差异只体现在「是否重写」）。
3. 记录文件 mtime 与 sha256。
4. 调用 `TableIntegrityValidator.ValidateInteractive()`。
   **期望**：**FAIL**（资产与源不一致）、**文件 mtime 不变**（校验入口不修数据）。
5. 记录 mtime 与 sha256，断言与步骤 3 相同。
6. 调用 `TableIntegrityValidator.RebuildAndValidateInteractive()`。
   **期望**：**PASS**、**mtime 变化**、sha256 回到与源一致。
7. 从备份恢复原资产，断言 sha256 与步骤 3 一致（清理确认）。

**判别力自证**：步骤 4 与步骤 6 断言**相反**（一个 mtime 不变、一个变；一个 FAIL、一个 PASS）。
若两个入口仍不可区分，步骤 4 或 6 必有一项失败。

**已知边界（评审 §4.4 顺带发现，建议写入）**：`TableDataImporter.Import()` 的**幂等判定
不校验资产完整性** —— 它只比对溯源字段，不校验 82 段几何是否仍与源一致。
因此「陈旧资产」如果不改溯源字段、只改几何，`Import()` 会**跳过**，两个入口仍不可区分。
§10.2 步骤 2 因此明确要求改**溯源字段**（`sourceSha256`）。该边界本身是 Day 2 的已知取舍，
Day 4 不改动它，仅在 spec 中留档。

### 10.3 生成入口 / 校验入口分离（评审 §7-7 建议；沿用 Day 3 与 R2 的结论）

| 入口 | 职责 | 禁止 |
| --- | --- | --- |
| `Day4PhysicsSetup.cs` | **只生成**（建 `PhysicsGreybox.unity`），幂等 | 不得调用任何校验逻辑；不得写 `EditorBuildSettings` |
| `Day4Validator.cs` | **只校验**（跑 T1–T14 的产物与证据断言） | **不得调用生成入口**（R2 铁律：校验器不得重建被校验对象） |

两者的报告路径都必须走 `ProjectPaths.ResolveReportPath`（R2 已确立），
且 `Day4Validator` 额外承担一条 **R-J 断言：`EditorBuildSettings` 中启用的场景数 == 1（且为 `Main.unity`）**。

---

## 11. 对既有质量门的影响（v1 完全未提）

| 影响对象 | 变更 | 处理 |
| --- | --- | --- |
| Day-1 验收器（当前 **26** 项） | `PoolConstants` 新增 `MaxBallSpeed` | 本 spec **不要求**扩展 Day-1 验收器（v1 §10.1 曾隐含此意，已撤）。新常量的守护由 T6 承担 |
| Day-3 验收（22 项） | 不动 `Main.unity` ⇒ 证据保持有效 | 无 |
| EditMode 测试基线 | 实测 **53**（v1 写的 43 已过期） | 新总数为 53 + 新增 |
| `EditorBuildSettings.asset` | 不注册新场景 | 无 |
| `Billiards.Runtime.asmdef` | **无需改动**（`TableConfig` 已在其中） | 无 |
| `Billiards.Tests.EditMode.asmdef` | **无需改动**（同目录新文件自动纳入） | 无 |

---

## 12. 复核安排（标准 + 数据交叉复核）

| 阶段 | 责任 | 内容 |
| --- | --- | --- |
| 实现 | engineering-lead | §10 交付物 1–14 + §10.1 / §10.2 |
| 实现方自验 | engineering-lead | 跑通 T1–T14，产出 §7 证据 |
| 独立复核 | quality-lead | ① 独立重算 T5a/T5b/T7；② **独立解析推导** T1/T10/T11 期望值，不共享代码路径；③ 核验 T5b 负对照确以生产路径实现；④ 核验 T12 能捕捉「只改一处」；⑤ 主张级检查本 spec 有无残留说法 |

**复核必须能失败**：若 quality-lead 无法复现 `ToiIterationGuardTrips == 0`、
`EnergyGainViolations == 0`，或 T11 的自由飞行断言失败，即为实现缺陷，不得以「小概率」放行。
