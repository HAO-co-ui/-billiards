<#
.SYNOPSIS
  摆球指纹导出器（只读）：把 "seed → 15 颗球的球号落点" 打印成可跨机 diff 的规范文本。

.DESCRIPTION
  用途：证明/否证 "`new System.Random(seed)` 让摆球可复现" 这件事在当前运行时上成立。
  做法：完整复刻 `PhysicsTestDrive.EightBallRackPositions`（:2131-2205）与 `RackSlot`（:2103-2107）
        的取数顺序与几何算式，输出
          · 槽位→球号 表（纯整数，**不含任何浮点**，故不受浮点差异影响）
          · 每颗球的 x/y 及其 float32 位模式（`SingleToInt32Bits`）
          · `rackDigest` = 对 1 mm 量化后的 (x,y) 取 FNV-1a 32 —— **与联机协议 §4.2 的 `rackDigest` 同一算法**
        再给一行 `FP <sha256>`，两台机器的输出 `fc` 一下即可判定。

  纪律：
    · 只读。不写任何文件（除非显式给 -OutFile），不触碰 Assets/。
    · 不启动 Unity、不依赖任何工程内类型。
    · 判定"跨运行时是否一致"必须**两台机器/两个运行时各跑一次**再比；
      单机跑出来的绿**不能**证明跨版本一致（见 -VerifyLegacy 的说明）。

.PARAMETER Seeds
  要导出的种子列表。默认取一组固定值（含 0 / 正 / 负 / 常见测试值）。

.PARAMETER OutFile
  把规范文本另存一份（默认只打到 stdout）。

.PARAMETER VerifyLegacy
  额外做一件事：用 mscorlib(1999) 的算法独立重实现一遍 `System.Random(seed)`，
  与本机真实运行时逐值对照。用于回答"带种子的 Random 是否还走老算法"。
  ⚠ 这只能验证**当前运行时**；Mono（Unity）与 CoreCLR 是否一致，必须在 Unity 里跑一次本脚本才知道。

.EXAMPLE
  # 本机基线（把输出留档，换机器后再跑一遍，diff 两份输出）
  powershell -File Docs/Tools/rack_fingerprint.ps1 -OutFile "$env:TEMP\rack_before.txt"

.EXAMPLE
  # 顺带验证"带种子 Random 是否仍是老算法"
  powershell -File Docs/Tools/rack_fingerprint.ps1 -VerifyLegacy
#>
[CmdletBinding()]
param(
    [int[]] $Seeds = @(0, 1, 7, 42, 12345, -99999, 2024),
    [string] $OutFile,
    [switch] $VerifyLegacy
)

$ErrorActionPreference = 'Stop'

# ────────────────────────────── float32 助手 ──────────────────────────────
# 模拟 C# 的 float 运算：每一步 round 到 single。双舍入边界上可能与真实 float 差 1 ULP，
# 故位模式列只作"指纹对照"用（跨机比对必须用**同一份脚本**）。
function S1($x) { return [single]$x }

# float32 位模式。⚠ 不用 `[BitConverter]::SingleToInt32Bits`：它是 .NET Core 2.0 / netstandard2.1
# 才加的 API，**.NET Framework 4.8 上不存在**（实测：Windows PowerShell 5.1 报 MethodNotFound）。
# 这里走 GetBytes + ToInt32，两者等价（小端）。
function Get-SingleBits([single]$v) {
    return [BitConverter]::ToInt32([BitConverter]::GetBytes($v), 0)
}

$BallRadius = S1 0.028575
$BallDiameter = S1 ($BallRadius * (S1 2.0))
$RackApexX = S1 -0.30
$RackStep = S1 ($BallDiameter + (S1 0.0002))
$RackRowGap = S1 ($RackStep * (S1 0.8660254))
$Half = S1 0.5

function Get-RackSlot([int]$r, [int]$k) {
    $x = S1 ($RackApexX - (S1 ($r * $RackRowGap)))
    $y = S1 ((S1 ($k - (S1 ($r * $Half)))) * $RackStep)
    return @($x, $y)
}

# ── 与 EightBallRackPositions 逐行对应：solids/stripes 抽取 → 底角 → Fisher-Yates → 按槽位填几何 ──
function Get-RackSlotBallTable([int]$seed) {
    $rng = New-Object System.Random($seed)
    $solids = New-Object 'System.Collections.Generic.List[int]'
    $stripes = New-Object 'System.Collections.Generic.List[int]'
    1..7 | ForEach-Object { $solids.Add($_) }
    9..15 | ForEach-Object { $stripes.Add($_) }

    $si = $rng.Next($solids.Count)
    $solidCorner = $solids[$si]
    $solids.RemoveAt($si)

    $ti = $rng.Next($stripes.Count)
    $stripeCorner = $stripes[$ti]
    $stripes.RemoveAt($ti)

    $leftCornerIsSolid = ($rng.Next(2) -eq 0)

    $slotBall = New-Object 'int[]' 15
    for ($i = 0; $i -lt 15; $i++) { $slotBall[$i] = -1 }
    $slotBall[4] = 8
    if ($leftCornerIsSolid) { $slotBall[10] = $solidCorner; $slotBall[14] = $stripeCorner }
    else { $slotBall[10] = $stripeCorner; $slotBall[14] = $solidCorner }

    $rest = New-Object 'System.Collections.Generic.List[int]'
    foreach ($b in $solids) { $rest.Add($b) }
    foreach ($b in $stripes) { $rest.Add($b) }
    for ($i = $rest.Count - 1; $i -gt 0; $i--) {
        $j = $rng.Next($i + 1)
        $tmp = $rest[$i]; $rest[$i] = $rest[$j]; $rest[$j] = $tmp
    }

    $cursor = 0
    for ($slot = 0; $slot -lt 15; $slot++) {
        if ($slotBall[$slot] -eq -1) { $slotBall[$slot] = $rest[$cursor]; $cursor++ }
    }

    return $slotBall
}

function Get-Fnv1a32([byte[]]$bytes) {
    $h = [uint64]2166136261
    foreach ($b in $bytes) {
        $h = [uint64]($h -bxor [uint64]$b)
        $h = [uint64](($h * [uint64]16777619) -band [uint64]4294967295)
    }
    return [uint32]$h
}

function Get-RackFingerprint([int]$seed) {
    $slotBall = Get-RackSlotBallTable $seed

    $posByBall = @{}
    $idx = 0
    for ($r = 0; $r -lt 5; $r++) {
        for ($k = $r; $k -ge 0; $k--) {
            $posByBall[[int]$slotBall[$idx]] = (Get-RackSlot $r $k)
            $idx++
        }
    }

    $bytes = New-Object 'System.Collections.Generic.List[byte]'
    $lines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($ball in 1..15) {
        $p = $posByBall[$ball]
        $bx = Get-SingleBits $p[0]
        $by = Get-SingleBits $p[1]
        $lines.Add(('    ball {0,2}  x={1,10:F6} 0x{2:X8}   y={3,10:F6} 0x{4:X8}' -f $ball, $p[0], $bx, $p[1], $by))
        $qx = [int][Math]::Round([double]$p[0] / 0.001)
        $qy = [int][Math]::Round([double]$p[1] / 0.001)
        foreach ($v in @($qx, $qy)) {
            $bytes.Add([byte]($v -band 0xFF))
            $bytes.Add([byte](($v -shr 8) -band 0xFF))
        }
    }

    return [pscustomobject]@{
        SlotBall = $slotBall
        Lines    = $lines
        Digest   = (Get-Fnv1a32 $bytes.ToArray())
    }
}

# ────────────────────────────── 主输出 ──────────────────────────────
$canonical = New-Object 'System.Collections.Generic.List[string]'
$canonical.Add('RACK-FINGERPRINT v1')
$canonical.Add('RUNTIME ' + [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)
$canonical.Add('PSVER ' + $PSVersionTable.PSVersion.ToString())

foreach ($seed in $Seeds) {
    $fp = Get-RackFingerprint $seed
    $canonical.Add(('SEED {0} TABLE {1} DIGEST 0x{2:X8}' -f $seed, ($fp.SlotBall -join ','), $fp.Digest))
    foreach ($l in $fp.Lines) { $canonical.Add($l) }
}

# ⚠ FP 只对**内容**取摘要，**不含** RUNTIME / PSVER 两行。
# 否则两台机器（甚至两个运行时）的 FP 永远不同 —— 那等于这个指纹没有用。
$contentLines = $canonical | Where-Object { $_ -notmatch '^(RUNTIME|PSVER) ' }
$joined = ($contentLines -join "`n")
$sha = [System.Security.Cryptography.SHA256]::Create()
$fpBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($joined))
$fpHex = -join ($fpBytes | ForEach-Object { $_.ToString('x2') })
$canonical.Add('FP ' + $fpHex)

$text = ($canonical -join "`n")
Write-Output $text

if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $text + "`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Output ("`n[written] " + $OutFile)
}

# ────────────────────────────── 可选的算法同一性核对 ──────────────────────────────
if ($VerifyLegacy) {
    Write-Output "`n============ -VerifyLegacy：带种子 Random 是否仍走 mscorlib(1999) 算法 ============"

    function New-LegacyState([int]$seed) {
        $MBIG = [long]2147483647; $MSEED = [long]161803398
        if ($seed -eq [int]::MinValue) { $sub = [long]2147483647 } else { $sub = [long][Math]::Abs([long]$seed) }
        $mj = $MSEED - $sub
        $sa = New-Object 'long[]' 56
        $sa[55] = $mj
        $mk = [long]1
        for ($i = 1; $i -lt 55; $i++) {
            $ii = (21 * $i) % 55
            $sa[$ii] = $mk
            $mk = $mj - $mk
            if ($mk -lt 0) { $mk += $MBIG }
            $mj = $sa[$ii]
        }
        for ($k = 1; $k -lt 5; $k++) {
            for ($i = 1; $i -lt 56; $i++) {
                $sa[$i] = $sa[$i] - $sa[1 + (($i + 30) % 55)]
                if ($sa[$i] -lt 0) { $sa[$i] += $MBIG }
            }
        }
        return [pscustomobject]@{ SA = $sa; INext = 0; INextp = 21; MBIG = $MBIG }
    }

    function Invoke-LegacySample($st) {
        $a = $st.INext; $b = $st.INextp
        $a++; if ($a -ge 56) { $a = 1 }
        $b++; if ($b -ge 56) { $b = 1 }
        $rv = $st.SA[$a] - $st.SA[$b]
        if ($rv -eq $st.MBIG) { $rv-- }
        if ($rv -lt 0) { $rv += $st.MBIG }
        $st.SA[$a] = $rv
        $st.INext = $a; $st.INextp = $b
        return $rv
    }

    function Invoke-LegacyNext($st, [int]$maxValue) {
        $s = (Invoke-LegacySample $st) * (1.0 / [double]$st.MBIG)
        return [int][Math]::Floor($s * $maxValue)
    }

    $bad = 0
    foreach ($seed in $Seeds) {
        $real = New-Object System.Random($seed)
        $st = New-LegacyState $seed
        $same = $true
        for ($n = 0; $n -lt 24; $n++) {
            $mv = @(2, 6, 7, 12, 1000000)[$n % 5]
            if ($real.Next($mv) -ne (Invoke-LegacyNext $st $mv)) { $same = $false; break }
        }
        if (-not $same) { $bad++ }
        Write-Output ('  seed={0,-9} 前 24 个 Next(...) 与 mscorlib 重实现一致 = {1}' -f $seed, $same)
    }

    Write-Output ('  ---> 不一致种子数 = {0} / {1}' -f $bad, $Seeds.Count)
    if ($bad -eq 0) {
        Write-Output '  结论：本运行时上，带种子的 System.Random 仍与 1999 年 mscorlib 算法逐值相同。'
        Write-Output '        ⇒ 这一条**不能**单独证明 Unity(Mono) 也相同；必须在 Unity 里跑本脚本再比 FP。'
    }
    else {
        Write-Output '  结论：本运行时的带种子 System.Random **已偏离** mscorlib 算法 —— 跨运行时确定性的直接反例。'
    }
}
