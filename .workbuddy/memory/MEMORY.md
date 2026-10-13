# MEMORY.md — Billiards game（Idea #35 台球）

## 项目定位
- 对标 **8 Ball Pool**（GP 4.79 / 3150 万评分）。核心：一杆一杆把球打进袋，物理精准 + 真人对战。
- 本轮形态：**Unity 单机 8 球 + AI 对战**（AI 与联机均后置）。
- 策划拆两张卡：**35-A 击球手感验证**（P0 核心）、**35-B AI 对战验证**（P0 长期拉力）。
- 美术资源包位置：`F:\工作文件\idea35_台球`（另有"打包要求_用户参考"原始任务的说明）。

## 卡 35-A 的验收标准（照抄策划，不可放宽）
- 唯一验证假设：击球瞄准的物理手感 + 进球瞬间的爽感，能不能让人"再来一杆"。
- 必须保质保量到 80 分：**瞄准线清晰、力度控制跟手、物理弹库真实、进球有"清脆"反馈**。
- 可以糙：美术 3D 低模球桌即可；先 1 张球桌。
- 成功看的数据：单局时长、进球率、再来一局率、次日留存。
- 备注（决定了排期重心）：**"物理不真就'台球乱弹'"** —— 这是最核心的一张卡。

## 卡 35-B 的验收标准（下一轮）
- 必须保质保量：**AI 要有合理失误率（别太强）、局数循环（三局两胜）、奖励结算有仪式感**。
- 美术工作量：低。联机后置；AI 先验证单人对战成立。

## 技术栈与环境
- Unity **6000.0.49f1** + URP **17.0.4**，PC 桌面横屏（最新交付边界，早期"手机"表述已废弃）。
- 已装包：Input System 1.14.0、Test Framework 1.5.1、Timeline 1.8.7、UGUI 2.0.0、Rider/VS IDE 包。
- 起点是**裸 URP 模板**（仅 SampleScene + TutorialInfo），无任何业务代码。

## 硬性架构约定（不要推翻）
1. **台球物理自研 2D 定长步进，禁用 Unity PhysX。** 原因：球半径 0.028575 m + 初速 2–8 m/s，
   PhysX 离散检测在球堆开球时穿透；且无确定性 → 瞄准预测 / AI 推演 / 回放全废。
2. 物理在**规范米制二维平面**（长轴 × 短轴）运算，渲染层映射 `(x, 0, y)` 到三维。
3. **单位 = 米**。硬指标：内库 2.54 × 1.270108866 m、球半径 0.028575 m、台呢 y = 0、
   杆长 1.4 m（杆尖为局部原点、杆身 +Z）。
4. **"桌面扩大 20%" 只做视觉**：`PlayfieldRoot` 缩放 1.2，物理保持规范米制，指针拾取反向映射。
5. 所有手感参数外置为 ScriptableObject（库边弹性/台呢滚阻/球-球恢复/袋口半径/力度曲线），
   目标是 5 分钟内改一个值重测。

## 资源取用约定
- 模型一律用 `03_Models_模型/FBX_Exports/Recommended/`（Table / Balls / Cue / Environment）。
  **禁用 `GLB/` 目录** —— Unity 原生不支持 .glb。
- 碰撞数据源：`08_Gameplay_Markers/table-metadata-reference.json`（82 库边段 + 6 袋 + captureRadius）。
- 球位与场景装配参考：`08_Gameplay_Markers/scene-assembly-reference.json`。
- UI 设计稿在 `04_UI/Reference_Screens/`（16 张，含 flow-report.json），**是设计稿不是可交互切图**，需自行实现。
- 字体 Barlow Condensed + Inter 为 **Latin 子集，不覆盖中文** → 35-A 用英文 UI；中文字体属正式版待确认项。
- 音频 11 个（`06_BGM/Late_Set/late-set.wav` 91.4286s + `07_SFX/` 10 个 0.8s），22.05 kHz 双声道 PCM16。
- VFX 无序列帧/Prefab，只有 `05_VFX/Code_Reference/pool-vfx.js` 程序源码，按意图重建。

## 来自美术交付包的硬约束（不可违反）
- 包内 `Playable_Prototype_Reference` 试玩版**仅参考**：可能有 Bug，**不作为物理/UI/音画/性能的验收依据**，
  参数与规则不要求照搬。
- 单位/轴向转换：**不要同时应用导入器转换和二次手动转换**。three 为右手 Y-up、Blender 为 Z-up、Unity 为左手 Y-up。
- 桌体材质必须 Opaque；袋网用 Alpha Clipping（参考 cutoff 0.5），桌体不能全透明。
- Normal 为 OpenGL 切线约定；Metallic/Roughness 为分离贴图，URP Lit 需打包为 Metallic(R)+Smoothness(A)。
- 审计结论：`Missing_Assets.md` P0 = 0，美术无阻断项。待确认项均为**商业权属**类（ART-E01 下载模型 /
  ART-E02 原创音频 / ART-E03 生成徽章 / ART-E04 吊灯贴图），不阻塞 35-A，但阻塞发布。

## 工作流约定
- 排期文档置于 `Docs/`（工程根目录下，Unity 不导入）。
- 交付物迭代：`Docs/35-A_击球手感_制作排期.md` 为 35-A 的主排期文件，变更直接改此文件。
- 执行记录按天落在 `Docs/35-A_DayN_*.md`。

## 工程配置的铁律（Day 1 建立，务必遵守）
1. **绝不用手改 `ProjectSettings/*.asset`。** Unity 把工程设置常驻内存，编辑器运行期间手改磁盘
   YAML 会在它下次写回时被静默覆盖（实测：`ProjectSettings.asset` 在编辑器启动时被自动重写）。
   一切配置改动写进 `Assets/_Project/Scripts/Editor/ProjectSetup/BilliardsProjectSetup.cs`，
   通过官方 API 应用。该脚本幂等、可复现，并且是这套配置的唯一文档。
2. **验收与配置同源**：`BilliardsProjectVerifier.cs` 提供 26 项检查，配置器执行后自动调用。
   命令见 `BilliardsProjectSetup.ApplyDay1FromCommandLine`（也可用菜单 `Billiards → Setup → ...`）。
3. **判断资产是否存在必须查文件系统**（`ProjectPaths.Exists`），不要用
   `AssetDatabase.LoadAssetAtPath<Object>() != null`。后者在资产因依赖脚本被删而变为
   "不可反序列化"时返回 null，会造成**清理与验收同时静默失效**的假通过。
4. **`Temp/UnityLockfile` 陈旧残留**会让批处理在"加载程序集"后静默退出（退出码 1、日志无错）。
   无 Unity 进程运行时可直接删除该文件后重试。
5. 批处理下 `EditorSceneManager.NewScene` 必须用 `NewSceneMode.Single`（起始场景未命名未保存，
   Additive 会被拒）。

## 已建立的可复用资产
- `PoolConstants.cs`（Runtime/Core）：**单位制与几何硬指标的唯一真源**。改数值必须同步
  `PoolConstantsTests`，否则测试会拦下（13 项，已全绿）。
- `BilliardsLayers.cs`（Editor）：Layer 索引与名称的唯一真源。
- `ProjectSetupReport.cs`：验收结果模型 + JSON 报告 + `ProjectPaths` 路径工具。
- `Docs/Tools/generate_input_actions.py`：输入映射生成器，可重跑、可追溯。
- 验收证据固定在 `Docs/Reports/day1-verification.json` 与 `day1-editmode-tests.xml`。

## 版本控制
- Git 仓库已初始化（`main` 分支），首个提交 `f3e29ea`。
- **刻意不使用自定义 Tag**：物体身份由组件表达（类型安全、可重构、拼错会编译失败）。
  验收项 `LYR-03` 持续断言自定义 Tag 数为 0，请勿破坏该约定。
- `.gitattributes` 为 `* text=auto eol=lf`（Unity 本就写 LF，强制 CRLF 会制造 diff 噪音）。
