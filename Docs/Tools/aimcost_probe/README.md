# aimcost_probe —— 内核成本 / 逐位等价 的离线核验探针

**它为什么在仓库里**：本工程"**批次 C 内核等价预筛**"最硬的一条验收判据是
「**改前改后物理结果逐位不变**」（CoreCLR 逐位哈希）。这条判据的**仪器**就是这个探针 ——
源码不入库，那次等价性验证将来**无法复现**，只能"相信结论"。故：**源码入库、构建产物不入库**。

## 两个入口

| 入口 | 用途 |
| --- | --- |
| 成本探针 | 量内核单步成本、**一次拖动预测实际跑多少步**、以及**球-段预筛跳过率**（判"预筛非空转"） |
| `EqBaseline.cs`（`eq` 子命令） | 采集 / 比对**等价性对照基线**：15 个场景、652 个字段、`ToString("R")` 规范化后取 **SHA-256** |

**为什么必须"两端同一运行时"**：同一份内核在 **CoreCLR 与 Mono 上会分叉**（实测 `T111` 收敛步数
CoreCLR **488** vs Mono **493**、`T37` 接触数 **50** vs **49**）—— float 中间精度与 JIT 的 FMA 收缩差异。
⇒ **跨运行时逐位比对必然假红**；本探针**只用于 headless ↔ headless** 的同运行时比对。
（附带实测：同一 CoreCLR 运行时下，**Unity 的 Roslyn 与 dotnet SDK 的 Roslyn 产出逐位一致**
⇒ 该基线可**脱离 Unity** 复现，省锁、可无限复跑。）

## 项目文件为什么写在 README 里（而不是 `.csproj` 文件）

本仓 `.gitignore` 有一条**全局**规则 `*.csproj`（Unity 会在仓库根生成同名工程文件，属生成物 ⇒ 一律不入库）。
探针这两个工程文件是**手写**的，但同样被该规则覆盖。**本工程不去改动那条全局规则**（改了会让
子目录里 Unity 生成的 csproj 也可能被提交）⇒ 改为**把内容登记在此**，需要时手工落成文件即可。

`aimcost.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>aimcost</AssemblyName>
    <RootNamespace>aimcost</RootNamespace>
    <InvariantGlobalization>true</InvariantGlobalization>
    <RestoreSources></RestoreSources>
    <NoWarn>CS0618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Remove="rewrite/**" />
    <Compile Remove="bin/**" />
    <Compile Remove="obj/**" />
    <Compile Remove="patched/**" />
    <Compile Remove="control/**" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Billiards.Runtime-verify">
      <HintPath>control\Billiards.Runtime-verify.dll</HintPath>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>E:\Unity_Deve\Unity_Tools\Editor\6000.0.49f1\Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

`rewrite/rewrite.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>rewrite</AssemblyName>
    <NoWarn>CS0618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Unity.Cecil">
      <HintPath>E:\Unity_Deve\Unity_Tools\Editor\6000.0.49f1\Editor\Data\Managed\UnityEngine\Unity.Cecil.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

> `Include` 名**必须 == 目标 DLL 的程序集内部名**（此处 `Billiards.Runtime-verify`）。
> 若写成别的名字，MSBuild 会**静默**改用 bin 里残留的同名旧 DLL ⇒ **"改了源码却在跑旧二进制"**
> （本工程实测踩过：三次"扰动无效"的真因就是这个）。另：**必须 `-t:Rebuild`** ——
> MSBuild 增量编译不认源码改动，甚至可能报"已成功生成"而产物目录是空的。

## 不入库的构建产物（可重建）

| 目录 | 内容 | 怎么来 |
| --- | --- | --- |
| `control/` | `Billiards.Runtime-verify.dll` | 由 `Docs/Tools/make_verify_csproj.py` 生成验证版 csproj 后 `dotnet build -t:Rebuild` 产出 |
| `patched/` | `Billiards.Runtime.dll` | 用本目录的 `rewrite` 工具把 `UnityEngine.Object == null` 这个 ECall 运算符改写成 `ReferenceEquals` 后的副本（内核要在 Unity 之外跑，必须绕开 ECall） |
| `bin/`、`obj/` | 探针自身构建输出 | `dotnet build` |

（以上四类已在 `.gitignore` 中忽略，避免每次跑探针都脏 `git status` —— 本工程的"冻结锚点"依赖工作区干净。）

## 用法

完整跑法与判据口径见 `Docs/35-B_Main入口与瞄准性能_spec.md`（批次 C 一节，含基线 `SHA-256`、
正对照协议、以及"扰动必须落在被走到的分支内且幅度足以改变结果"那条规程）。
本探针**只读**产品代码/资产，**不改任何产品文件**。
