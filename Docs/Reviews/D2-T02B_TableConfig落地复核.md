# D2-T02B · TableConfig 落地交叉复核

| 项 | 值 |
| --- | --- |
| 任务 | D2-T02B（被 D2-T01 阻塞，已解除） |
| 复核人 | quality-lead · 严守真（独立于实现者） |
| 复核对象 | `Assets/_Project/Data/TableConfig.asset`（纯文本 Unity YAML，13,446 B）—— **直接解析原始构件，不接受任何中间导出** |
| 独立基线 | `Docs/Reports/d2t02a-baseline.json`（D2-T02A 产出） |
| **整体判定** | **PASS_WITH_CONCERNS**（数据落地逐位精确、43/43 测试通过、全工程仅一处轴变换；5 项非阻塞 CONCERN） |
| **是否阻塞 Day 3** | **不阻塞** |
| 产出物 | 本报告 · `Docs/Reports/d2t02b-crosscheck.json` · `Docs/Reports/d2t02b-editmode-tests.xml` · `Docs/Reports/d2t02b-mesh-audit.json` · 新增 QA 测试 `Assets/_Project/Tests/EditMode/D2T02B_IndependentAuditTests.cs` |

> 纪律：仅只读解析资产 + 新增测试；**未修改任何实现代码或资产**。所有结论附命令与真实输出。

---

## 0. 复核方法与证据链

```
PY = C:/Users/hp/.workbuddy/binaries/python/versions/3.13.12/python.exe
$PY Docs/Tools/d2t02b_crosscheck.py     # [1]数据比对 [2]CHR-01 [3]CHR-02 [4]反向用例实跑
$PY Docs/Tools/png_probe.py             # [7]贴图独立解码
Unity.exe -batchmode -runTests -testPlatform EditMode ...   # [4]测试
```

**溯源链（独立核验，全部相等）**：
- 资产记录 `sourceSha256 = ba60fc20…364398`
- `Docs/Reference/table-metadata-reference.json` sha256 = `ba60fc20…364398`（**与 F: 原始文件逐字节相同**）
- F: 原始 `table-metadata-reference.json` sha256 = `ba60fc20…364398`
⇒ 资产 ← 入库副本 ← F: 原始，**链条无损**。（注意：D2-T02A 已证 `gameplay-markers` 是 `table-metadata` 的同源产物。）

### 0.1 锚点指纹（**实测**，非采信）
复核期间复核对象被移动过（校验器 29→31、测试 35→37）。下表为我**实际计算的当前值**，用于锚定"本结论对应哪个版本"：

| 文件 | sha256[:16] | 字节 |
| --- | --- | --- |
| `TableConfig.asset` | `d7b0962a3c21c349` | 13,446 |
| `TableConfigTests.cs` | `650d2e0190eae0a1` | 24,939 |
| `TableIntegrityValidator.cs` | `8180ddd528eabc51` | 46,632 |
| `TableDataImporter.cs` | `19c6b8320f3b05ea` | 17,844 |
| `BilliardsAssetPostprocessor.cs` | `7f62fd04a59c144a` | 9,009 |
| `TableConfig.cs` | `7fcf7a98cf26510a` | 7,909 |
| `PoolGeometry.cs` | `b3f05bf52727508e` | 8,563 |
| `D2T02B_IndependentAuditTests.cs`（我新增） | `27494a89393d418c` | 17,320 |

⚠️ team-lead 首次给的指纹表中 `TableIntegrityValidator.cs`(`67b8b15e`/45,727) 与 `TableConfigTests.cs`(`b50be06f`/24,596) 为**过期值**（我实测为 `8180ddd5`/46,632 与 `650d2e01`/24,939，与 engineering-lead 的列表一致）。**`TableConfig.asset` 未变** ⇒ D2-T02A 的数据比对结论继续有效。

---

## 1. 数据落地正确性（索引对齐逐字段，1e-5 m）

解析 `TableConfig.asset`：**82 段 / 6 袋**，`rail` 标签齐全。

| 指标 | 值 |
| --- | --- |
| 原始 maxDelta（asset 解析值 − baseline 双精度） | **1.0577e-07 m**（0.106 µm），最坏处 `seg[61].b.x` |
| float32 域 maxDelta（asset − float32(baseline)） | **5.381e-08 m** |
| **解析值经 float32 舍入后 ≠ baseline(float32) 的点数** | **0** |
| 1e-6 容差下 findings | **0** |

**结论：落地误差 = 0（真实错误为零）。**
原始 0.106 µm 的差值是**纯 float32 打印舍入**：资产里的十进制字符串（如 `-1.3343427`）解析后四舍五入到 float32，**与真值的 float32 逐位相同（全部 170 个点无一例外）**。这正是 team-lead 要求区分的"存储舍入 vs 真实错误"——**是存储舍入，不是错误**。
（对照：内库 `innerWidth` 资产 `1.2701088` vs 真值 `1.270108866174597`；float32(1.270108866174597)=1.2701088，完全吻合。）

---

## 2. 手性 / 绕向自检（独立复算，未用实现者脚本）

- **CHR-01 袋口局部手性符号 = [1,−1,−1,−1,1,1]**，与期望一致 → **PASS**。
- **CHR-02 库边拼接绕向 shoelace/2 = 3.144285（CCW）**，gap = 177.8 mm → **PASS**。

**关于 CHR-02"改后判据是否变弱成恒真"的追问（team-lead 重点）**：
我**实跑注入**证明它**不是恒真**：对资产施加水平面镜像后，shoelace 变为 **−3.1443（符号翻转）** → CHR-02 会 FAIL（见 §3 RC-4）。所以"sign>0 且 ≈3.1443"这一改法**仍能抓镜像**，判据有力。
**但**：CHR-02 是 **proper 旋转不变量**——对 90°/180° 旋转**不响应**（符合几何事实，非缺陷）。且它依赖段 `a→b` 点序被保留、且拼接起点取自文件内首个 rail，故其 shoelace **数值**依赖文件 rail 顺序（当前确定，但非起点不变）；这是它作为"量级"断言的一个轻微脆弱点，已在 §6 C1 备注。

---

## 3. 反向用例实跑（RC-1..RC-7，真实观察拦截/漏检）

复刻实现的四项判据（**CMP-03** 索引对齐逐字段 1e-5 / **DAT-03** 公共标量 vs PoolConstants / **CHR-01** 手性 / **CHR-02** 绕向），把 RC 注入解析后的数据集实跑：

| 用例 | 注入 | 结果 | 触发判据 |
| --- | --- | --- | --- |
| clean | 无 | **PASS（应通过）** | 无 |
| RC-1 | `seg[40].b.x += 1mm` | **拦截** | CMP-03(vio=1, Δ=1.0e-3) |
| RC-2 | `ballRadius=0.03` | **拦截** | **DAT-03**(Δ=1.42e-3) |
| RC-3 | `pocket[0].captureRadius=1.0` | **拦截** | CMP-03(Δ=9.69e-1) |
| **RC-4** | 全量 `x→−x`（水平面镜像） | **拦截** | **CMP-03(Δ=2.67m) + CHR-01 + CHR-02(−3.1443)** |
| RC-5 | 全量 90° 旋转 | **拦截** | CMP-03(Δ=2.06m) |
| **RC-6** | 全量 180° 旋转 | **拦截** | **CMP-03(Δ=2.91m)**（仅此一条） |
| RC-7 | 全量 ×1.2 | **拦截** | CMP-03(Δ=0.291m) + CHR-02(4.5278) |

**漏检项：无。7 个反向用例全部被拦截。**

**team-lead 重点问答**：
- **RC-4（水平面镜像）是否被抓到、被哪条抓？** 是。被 **三条**抓：CMP-03（索引对齐逐点 2.67 m）+ **CHR-01**（手性整列翻转）+ **CHR-02**（绕向翻号）。这正是 D2-T02A 所强调的"镜像必须靠手性/绕向或逐点基线"，实现者已补齐。
- **RC-6（180° 旋转）？** 被抓，但**仅由 CMP-03**（索引对齐）抓到。CHR-01/CHR-02 对 proper 旋转不响应——**符合几何事实**（180° 旋转保手性、保绕向），不是判据缺陷。结论：**只要 CMP-03 在位，180° 旋转无漏网**；若去掉 CMP-03，则无任何自包含判据能抓 180°（本桌近自对称）。

**落地为 EditMode 测试（已新增，未改实现代码）**：`D2T02B_IndependentAuditTests.cs` 中 4 项负向/变异测试直接喂给**实现者自己的纯函数 `ReferenceCrossCheck.Compare`** 一个被损坏的输入，断言它**报错**：
- `CrossCheck_DetectsMirroredB`（镜像 B）→ 断言 `!Identical` 且端点误差 > 1 m
- `CrossCheck_DetectsScaledB`（×1.2 B）
- `CrossCheck_DetectsSingleEndpointOffsetB`（单点偏移 1 mm）
- `ChiralityProbe_FlipsUnderHorizontalMirror` / `WindingProbe_FlipsSignUnderHorizontalMirror`
全部 **Passed** ⇒ 证明判据"有牙"，不是恒真。

---

## 4. 独立重跑测试

命令（自跑，未复用实现者 xml）：
```
Unity.exe -batchmode -projectPath "<proj>" -runTests -testPlatform EditMode \
  -testResults Docs/Reports/d2t02b-editmode-tests.xml -logFile Logs/d2t02b-tests.log
```
**观察到的运行**（我自跑，非复用实现者 xml；**只报真实运行并观察到的结果**）：

| 运行(UTC) | 结果 | 说明 |
| --- | --- | --- |
| 04:08 | **37/37 PASS** | 未加我的测试（13+24），作基线 |
| 04:10 | 编译失败(CS0136) | 我新增测试首版 `best` 名冲突 → 整程序集中止，**此 6 项当时从未运行** |
| 04:11 | 42/43 | 编译修复后；轴枚举测试的断言写错（把"次优符号变体"当成"次优排列"），**1 项 FAIL** |
| **04:12** | **43/43 PASS** | 断言修正后；**本次即最终证据** |

**最终结果：43 / 43 PASS（0 failed）**（`Docs/Reports/d2t02b-editmode-tests.xml`，`start-time 04:12:43Z`）：
- `PoolConstantsTests` 13（Day 1 回归）
- `TableConfigTests` 24（**哈希 `650d2e01` = 容差改到 0.5mm 后的冻结版**）
- `D2T02B_IndependentAuditTests` 6（QA 新增）

> **覆盖的修订版本**：该 04:12Z 运行**晚于** engineering-lead 04:05–04:07Z 的容差修改 ⇒ 覆盖的是当前冻结修订。**实现者自报的 37/37（13+24）由此获得复核方独立复现**（team-lead 已确认）。
> **与 team-lead 预期的差异**：您说"35/35"。**实测实现者原有为 37**（13 + 24），加我新增 6 = **43**。不是 35。
> **纪律留痕**：我一度把"写了 6 项检查"与"跑通 6 项"混同——首版**编译不过**，6 项实际从未执行。以上表格区分了"写"与"跑"，最终只以 04:12Z 的真实运行取证。

---

## 5. 轴向"只做一次"的实质检验（team-lead 最关心项之一）

### 5.1 独立复现实现者的 48 候选枚举（未调用其脚本）
我用自己的枚举 + 我 D2-T02A 的基线端点，独立复算（`d2t02b-mesh-audit.json`）：

| | 我的复算 | 实现者报告 |
| --- | --- | --- |
| 最优排列 | **021** | 021 |
| 最优误差 | **0.2611 mm** | 0.26114 mm |
| 次优排列 | **012 @ 640.722 mm** | 640.722 mm |
| 所选变换 `(−m.x, m.z, m.y)` 误差 | **0.2613 mm** | 0.26127 mm |

**数字逐位吻合，独立复现成立。**

### 5.2 🔎 我发现的实质性问题：**端点对齐判据对"水平符号/手性"不敏感**
在最优排列 021 内，**8 个符号变体的误差跨度仅 0.00027 mm（0.27 µm）**：

```
rank1 021 sign(-1,-1,-1) = 0.26114 mm   ← 数值最优，但它是"上轴翻转"的镜像（腿朝上）
rank4 021 sign(-1, 1, 1) = 0.26127 mm   ← 实现者所选（正确，det=+1）
```
其中 **rank1 的 `sign(-1,1,1)` 之镜像 `(-1,·,-1)` = 0.26114 mm 甚至略优于所选**。

**含义**：`AXL-04/AXL-05`（水平面最近顶点）**单独不能证明轴向正确/未镜像**——镜像变换在近对称的桌面上同样"对齐"到 0.26 mm。**真正把变换钉死的是别的证据**：
1. **`AXL-02` 包围盒不对称**：X 端 `−1.412686` vs `+1.412409`（0.277 mm 不对称）→ 钉死 X 符号；Y 端腿底 `−0.8635` vs 顶 `+0.0610` → 钉死"上"方向；
2. **修正必须是纯旋转（det=+1）**（`ModelAxisCorrection_MapsModelUpToUnityUp_AndPreservesChirality` 已断言）→ 钉死短轴符号。

⇒ **变换本身正确且唯一**（三项约束联立唯一解）。**但 `AXL-04` 的 detail 文案"同时证明轴向映射正确"属过度声明**——它只证明了"排列"与亚毫米对齐，符号证据在 AXL-02。**建议**：修正该文案，并在 AXL-02 旁明确"X 不对称符号 + det=+1 才是手性判据"。→ **CONCERN C1（中）**。

### 5.3 是否存在第二处轴/手性/缩放变换？—— **否**
全工程 `Assets/_Project/**/*.cs` 搜索 `new Vector3` / `localScale` / `.scale=` / `Quaternion` / `Euler` / `Rotation` / `Instantiate` / `SetParent`，坐标变换**全部集中在 `PoolGeometry.cs` 一处**：
- `PhysicsToUnity = (px, h, py)`、`UnityToPhysics`、`ModelVertexToUnity = (−m.x, m.z, m.y)`、四元数 `ModelAxisCorrection`；
- **无任何 `localScale` 或 `.scale` 赋值**；
- 场景 `Main.unity` 仅 1 相机 + 1 平行光（scale 均 1），**尚无球桌对象**；
- FBX 导入设置（`.meta` 实证）：`bakeAxisConversion: 0`、`globalScale: 1`、`useFileScale: 0` → **导入器不做二次转换/缩放**。
- `PoolConstants.PlayfieldVisualScale = 1.2f` **已定义但全工程未使用**（grep：仅定义处 + 测试断言）→ **当前不存在双重缩放**。

⇒ **"只做一次"成立**。美术文档最担心的"双重转换"在代码层面不存在。（提醒：球桌尚未入场景，`ModelAxisCorrection` 目前仅被测试调用；Day 3 instantiate 时须确保恰好应用一次。→ CONCERN C4。）

### 5.4 FBX 二进制**直解**（绕过 Unity，补齐 D2-T02A 缺口）
我**直接解析 `PoolTable_Normalized.fbx` 二进制**（`Docs/Tools/fbx_mesh_probe.py`，自写 FBX 解析器，version 7400）：
- Geometry=1，顶点 **10572**（与源 JSON `vertices=10572` 一致）；
- FBX 原始局部 bbox `min=(-1.412686,-0.797634,-0.863517) max=(1.412409,0.797634,0.061022)`；
- 用我 D2-T02A 的 188 端点做 48 候选枚举：**最优 perm021 @ 0.2611 mm、次优排列 640.722 mm** —— 与实现者、与我 Unity 侧枚举**三方一致**。

**由此独立确认了两件事**：
1. **Unity 确实只对 X 取反**（FBX 原始 X`[-1.412686,+1.412409]` → Unity 网格 X`[-1.412409,+1.412686]`，Y/Z 不变）。此前提此前只能靠"包围盒不对称"佐证、属推断；现由 FBX 直解 + 网格 bbox **双证**。
2. 48 候选结论**不依赖 Unity 导入**（FBX 原始数据同样得到 perm021 @0.2611 mm）。**D2-T02A 的"未核 JSON vs 真实网格"缺口就此补齐。**

### 5.5 🔬 AXL 容差：可证伪检验（对"轮廓近似残差"的裁决）
team-lead 要求我**独立判断**该解释是否成立，并做可证伪检验。我做了——

**"轮廓近似残差"这一解释：被证伪。**

对同一 188 端点，分别测量到**最近离散顶点**与到**最近三角形面上点**的水平距离（FBX 直解，含完整三角索引）：

| 度量 | 最坏值 | 分布（>1e-4m） |
| --- | --- | --- |
| 端点 → 最近**顶点** (AXL-04 现用) | **0.261330 mm** | 32 / 188 |
| 端点 → 最近**网格面点**（我实测） | **0.000000 m** | **0 / 188** |

**解读**：188 个端点**全部精确落在网格面上（0.000000 m）**。所以：
- ❌ 不是"82 段直线段逼近曲率袋角"的近似残差——**线段本身就是精确的**；
- ✅ 0.2613 mm **完全来自 AXL-04 采用"最近离散顶点"的采样下限**：最坏端点 `(-0.0711,-0.6975)` 落在边长 2.5~3.6 mm 的三角形内部，离最近顶点自然有 ~0.26 mm。

**对 team-lead 裁决的独立结论**：
1. **"轮廓近似残差"解释不成立**（已证伪，见上）。
2. **0.5 mm 阈值：不算错，但次优/脆弱。** 依据：
   - 该度量对**真实错位有 ~0.26 mm 盲区**（端点恰好落在 2.5–3.6 mm 三角形内时，最近顶点天然 ~0.26 mm 远）⇒ 弱检出；
   - 阈值绑定**网格细分密度**：当前 0.2613 mm 只是**这一个网格**的下限；若日后重导出网格变粗，下限上升可能 >0.5 mm → **误报**。
   - **我的建议**：把 AXL-04/05 的度量从"最近**顶点**"改为"最近**网格面点**"（当前精确为 **0**），阈值收紧到 **1e-4 m（0.1 mm）**。这样既**恢复真实检出能力**（从 0.26 mm 盲区 → 0.1 mm），又不再依赖网格密度。若坚持用最近顶点，阈值应由**网格局部顶点间距**导出（如 ≤ 2× 中位间距），而非拍一个 0.5 mm。

### 5.6 DAT-09 / DAT-10 独立复核
- **DAT-09**（库边端点包围盒）：资产实测 `x[-1.334342800, 1.334343000] y[-0.699095300, 0.699095300]`，对 double 基线最大误差 **8.9e-08 m**（float32 打印舍入）→ **PASS**。（实现者报 err=0 是 float32 域对 float32 常量的比较；两者不矛盾。）
  - **自我限定核验：成立。** x 左右不对称仅 **2.0e-07 m**、y 不对称 0 ⇒ **确实抓不到镜像**；但长宽比 1.9087 ≠ 1 ⇒ **能抓轴交换/缩放**。定位准确。
- **DAT-10**（`openingRadius == runtimeInscribedRadius`）：源 JSON 6 袋最大偏差 **0.000e+00（精确相等）** → **PASS**。

---

## 6. 字段覆盖审计

### 6.1 两个疑点的结论
- **`rail` 是否被静默丢弃？——没有，保留完好。** `TableConfig.asset` 中每段都带 `rail`（`end_negative` / `long_negative_positive` / … 共 6 类，与源一致）。**team-lead 的 grep `rail:` 得 0 属误报**（字段确实存在，可能是 grep 模式/编码问题）。对 Day 4–7 物理内核与 Day 6 库边音效差异化**无影响**。
- **`lowestY` 归属？——合理。** 它以 `legBottomY = −0.86351675` 落在 `TableConfig`（源 `lowest_y`），同时也在 `PoolConstants.TableLegBottomY`。作为"全局硬指标"放 `PoolConstants`、作为溯源副本放 `TableConfig`，二者由 `ConsistencyErrors()` 断言一致。**归属合理，非"该进未进"。**

### 6.2 源字段 → TableConfig 覆盖审计
- **完整落地**：`rail`、`captureNote`、`throatNormal`、`throatCenter`、`mouthWidth`、`throatWidth`、`openingRadius`、`captureRadius`、`ballRadius`、内库 4 标量、`cloth_y`、`nose_height`、`lowest_y`、`bounds_runtime`、溯源 5 项、`footSpot`、`offTableHalfExtents`。
- **改名后落地（非丢失）**：`runtimeCenter→center`、`runtimeMouth[0/1]→mouthA/B`、`runtimeThroat[0/1]→throatA/B`、`runtimeThroatCenter→throatCenter`、`runtimeThroatNormal→throatNormal`、`runtimeMouthWidth→mouthWidth`、`runtimeThroatWidth→throatWidth`。
- **未落地（逐类理由）**：
  - 源空间测量明细：`sourceInnerRim/sourceCenter/sourceProjectedMin|Max|InscribedRadius/sourceMouth/sourceThroat` —— 建模中间量，非运行时需要；
  - 运行时测量明细：`runtimeInnerRim`（12 点袋网内沿）、`runtimeProjectedMin|MaxRadius`、`runtimeInscribedRadius` —— 校验器 DAT-10 改为**直接读源 JSON** 复算（未把值搬进资产，避免双份漂移）；`runtimeInscribedRadius == openingRadius` 已由 openingRadius 承载；
  - 建模元数据：`meshComponent`、`topRimFaceCount`；
  - 冗余：`recommendedCaptureRadius`（`== captureRadius`）；
  - **坐标语义字符串**：`coordinate_mapping`、`collision_coordinates`、`notes` —— **仅存于代码注释，未随资产持久化**（→ CONCERN C5，追踪性略弱）。
- **净影响**：**无 gameplay 关键字段丢失**。`runtimeInnerRim` 若 Day 3 需渲染袋网开口可考虑补入（网格本身已含该几何，非硬需求）。

---

## 7. 材质与导入设置

### 7.1 通道打包（独立解码，纯 Python，未用实现者脚本）
`table_metallic_roughness.png`（1024²）逐像素核对：
- **R == 源 metallic：不匹配像素 = 0 / 1,048,576（maxΔ=0）**
- **A == 255 − 源 roughness：不匹配像素 = 0 / 1,048,576（maxΔ=0）**
- **G≠0 像素 = 0；B≠0 像素 = 0**
⇒ **打包精确无误**（R=Metallic、A=Smoothness、G=B=0），与 URP Lit 的 Metallic-Alpha 约定完全一致。

### 7.2 尺寸与导入设置
尺寸全部符合规格（BaseColor/Normal/Net 2048²、Metallic/Roughness/Packed 1024²）；法线 `textureType=NormalMap`、`sRGB=false`、`flipGreen=false`；袋网 `alphaFromInput + sRGB`（供 Alpha Clipping，cutoff 0.5 在材质侧）；Metallic/Roughness 线性且 `readable`（供打包）。**FBX 无二次缩放**（见 §5.3）。

### 7.3 法线绿通道（独立复算 + 结论评估）
- **独立复算绿均值 = 125.73**（实现者称 125.7，吻合）；R=127.42、B=251.59。
- **但对三条依据的评估（team-lead 要求）**：
  - "Unity 官方：绿 Y+ = Up" —— ✅ 真实且相关；
  - "Blender 默认导出 OpenGL" —— ✅ 一般成立，但**本资产是否如此导出未独立证实**（Blender 也可导出 DirectX）；
  - "**实测绿均值 125.7≈中性**" —— ⚠️ **判别力弱**：翻转后绿均值约 **129.3**，**同样接近中性**。该指标**不能区分是否翻转**。
  ⇒ "不翻转"的结论**主要靠约定链**，非视觉定论。**该结论被实现者正确标注为"非视觉定论、留 Day 3 A/B"，我认同这一残余风险处置**（归入 CONCERN C2，低）。

---

## 8. 补齐 D2-T02A 未验证项：JSON vs 真实网格贴合

**已独立复现**（`d2t02b-mesh-audit.json`，我自己的 48 候选枚举，未用实现者脚本）：
- 段端点对齐最优 **0.2611 mm**（实现者 AXL-04 报 0.2613 mm）——**独立复现**；
- 袋口 mouth/throat 对齐：实现者 AXL-05 报 0.2231 mm；我的整体 worst（含段+袋口，188 端点）为 **0.2611 mm**，与之一致（袋口 mouth 实测为 0）。

⇒ **"物理数据 ↔ 渲染网格"贴合在 0.26 mm 量级，已由我独立复现确认**，D2-T02A 的"未核对 JSON vs 网格"缺口**已补齐**。
⚠️ 但须附注 §5.2 的发现：**该贴合度（0.26 mm）与桌面镜像不对称（0.28 mm）同量级**，故"贴合良好"**不能**作为"未镜像"的证明。

---

## 9. 整体判定与 CONCERN 清单

### 判定：**PASS_WITH_CONCERNS** · 不阻塞 Day 3

**PASS 项**：数据落地逐位精确（真实误差 0）｜CHR-01/CHR-02 通过且非恒真｜7 个反向用例零漏检｜43/43 测试通过（覆盖冻结修订）｜全工程唯一轴变换、无双重转换/缩放｜通道打包逐像素精确｜溯源链无损｜**FBX 直解独立复现 48 候选（perm021 @0.2611mm）**｜**端点→网格面 = 0.000000 m（几何精确贴合）**｜**DAT-09/10 通过**。

| ID | 严重度 | CONCERN | 建议 |
| --- | --- | --- | --- |
| C1 | **中** | `AXL-04/05` 水平面最近顶点对**水平符号/手性不敏感**（sign spread 0.27 µm），镜像同样"对齐"0.26 mm；其 detail 文案"证明轴向映射正确"属**过度声明**。手性实际由 `AXL-02` 包围盒不对称 + `det=+1` 钉死。 | 修正 AXL-04 文案；在 AXL-02 明确"X 不对称 + det=+1 是手性判据"。**无需改实现逻辑**（变换本身正确）。 |
| **C6** | **中** | `AXL-04/05` 用"最近**离散顶点**"度量，对真实错位有 **~0.26 mm 盲区**（实测端点→网格面 = **0.000000 m**，0.2613 mm 纯属顶点采样下限）。**"轮廓近似残差"解释已被证伪**；0.5 mm 阈值弱且依赖网格密度。 | 度量改为"最近**网格面点**"，阈值收紧至 **1e-4 m**，恢复真实检出能力。 |
| C2 | 低 | 法线绿通道"不翻转"主要靠约定链；绿均值 125.7 对是否翻转**无判别力**。 | 维持 Day 3 受光 A/B 定论（实现者已标注）。 |
| C3 | 低 | `CMP-03` 只逐点比对段与袋，**未直接比对全局标量**（innerWidth 等）；标量由 `DAT-03` 对照 PoolConstants 覆盖（间接）。 | 可选：CMP-03 增补全局标量 vs 源 JSON 的直接断言。 |
| C4 | 信息 | `PlayfieldVisualScale=1.2` 已定义但**未使用**；球桌尚未入场景，`ModelAxisCorrection` 仅测试调用。 | Day 3 instantiate 时确保轴修正**恰好应用一次**、1.2× **仅作用于视觉 root**。 |
| C5 | 信息 | 坐标语义字符串（`coordinate_mapping`/`collision_coordinates`）未随资产持久化，仅在代码注释。 | 可选：入 asset 或 Day 1 文档，便于日后审计。 |

**修复归属**：C1/C3/C5 为文案或可选增强；C2/C4 为 Day 3 处置项。**均不阻塞 Day 3，无人需即刻返工。**

> ⚠️ **勘误（R3 增量确认，由复核方自查发现 · 保留原文）**
> 本报告 §5.5 与 **C6** 中"**建议改用'最近网格面点'度量、阈值收紧至 1e-4 m ⇒ 恢复真实检出能力**"这一**处方已被撤回**：
> D2-T03/T02B 后续实测证明「**"到面"度量对足迹内的沿面滑动天然盲**」（1/2/20 mm 内移 → 面距恒 0）⇒ **它不能"恢复真实检出"**。
> 正确处置为：**L1 面点仅作"必要条件层"**（不作位移判据）、**L2 边距作高灵敏层**（沿边方向盲）、**L3 顶点作全向层**（含周期性盲带），见 `D2-T03_AXL度量边界与盲带扫描.md`。
> 另：本报告 §5.5/§7.3 中"0.2613 mm 是顶点采样下限"的**结论成立**，其**依据**已由无效的"端点→面点=0"更换为**边距证据**（端点→最近**边** = 0.0001 mm）。
> **原文保留以留下判断过程；以本勘误块为准。**

---

## 10. 我未能验证 / 不确定的部分（诚实边界）

1. ~~源 FBX 二进制未独立解析~~ → **已补齐**：见 §5.4，我直接解析 FBX 二进制（10572 顶点）完成 48 候选枚举，绕过 Unity 得到一致结论（perm021 @0.2611 mm）。
2. ~~Unity 导入器"仅翻转 X"属推断~~ → **已确认**：FBX 原始 X`[-1.412686,+1.412409]` vs Unity 网格 X`[-1.412409,+1.412686]`，Y/Z 不变 ⇒ 仅 X 取反，双证成立。
3. **法线绿通道的"正确朝向"未做视觉判定**（无渲染环境）；仅完成数值复算与依据评估（§7.3）。
4. **我新增 6 项 QA 测试的性质**：负向 4 项直接调用实现者纯函数 `ReferenceCrossCheck.Compare`（真实验证其有牙）；手性/绕向 2 项为**我自己的判据复现**（非实现者私有校验器内部），已在代码注释与本报告如实标注。且如上 §4 所述——**首版编译失败时这 6 项从未运行**，最终结论仅取自 04:12Z 的真实运行。
5. **未运行 Play / 运行时**：所有验证均为 EditMode / 静态；运行时物理与手感的最终确认属 Day 4+。
6. **我的 FBX 解析器的独立实现**：为自写（`fbx_mesh_probe.py`），已与 Unity 侧的顶点数/包围盒/枚举结论三方交叉一致，但**解析器本身**未由第三方校验（无第二套 FBX 库可比对）。

---

*附：可复用件 — `Docs/Tools/d2t02b_crosscheck.py`（资产解析+比对+CHR+反向用例）、`Docs/Tools/png_probe.py`（纯 Python PNG 解码/通道核对）、`Assets/_Project/Tests/EditMode/D2T02B_IndependentAuditTests.cs`（负向+枚举审计，43 项测试的一部分）。*
