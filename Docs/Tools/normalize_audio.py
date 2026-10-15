# -*- coding: utf-8 -*-
"""音频素材归一化（Task D15-AU-02，规格 `Docs/35-A_音频反馈强化_spec.md` §2 / §7.2）。

作用：对 10 个 SFX + BGM 施加**纯增益**（无后处理、无软拐点、无硬限幅），
把逐文件峰值抬到规格 §2.2 的分类目标峰值；BGM 按 §7.2 取 "+10 dB"（保持混音关系）。

口径（与规格逐字一致）
─────────────────────
  峰值   = 两声道逐样本绝对最大值（归一化到 [-1,1)）。
  增益   = 目标峰值 dBFS − 现峰值 dBFS；float 缩放后 `round` 回 16-bit。
  Lgate  = 仅统计 `|s| > −50 dBFS` 的**有源段**样本的 RMS（跨时长可比的响度代理）。
  硬约束 = 归一化后峰值 ≤ −3 dBFS（规格 §2.1）。

可复现性
────────
  只重写 `data` 块里的 16-bit 采样；**RIFF 头的其它字节原样保留**（不改格式、不改长度）。
  ⚠ 一次运行后再次运行 ⇒ 增益 ≈ 0（幂等）；`--check` 只测不写。

用法
────
  python Docs/Tools/normalize_audio.py --check        # 只打印现值（不写）
  python Docs/Tools/normalize_audio.py                # 施加纯增益并写回
  python Docs/Tools/normalize_audio.py --only strike  # 只处理某个文件

⚠ 备份：原始素材在 `E:/Unity_Deve/_d9r_out/audio_original_backup/`（仓库之外）。
   写入出错时用 `cp -f <备份>/<名> <目标>` 还原 —— **不要** `git checkout`。
"""

import argparse
import math
import os
import struct
import sys

# ── 规格 §2.2 逐文件目标峰值（dBFS）。键 = 文件名（不含目录）。──────────────
SFX_TARGETS_DBFS = {
    "strike.wav": -7.0,
    "hit.wav": -7.5,
    "rail.wav": -9.0,
    "pocket.wav": -5.0,
    "foul.wav": -7.0,
    "win.wav": -5.0,
    "lose.wav": -8.0,
    "click.wav": -9.0,
    "back.wav": -9.0,
    "hover.wav": -11.0,
}

# BGM：规格 §7.2 建议整体 +10 dB（BGM 比 SFX 家族低约 10–12 dB 的混音口径）。
BGM_NAME = "late-set.wav"
BGM_GAIN_DB = 10.0

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SFX_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "SFX")
BGM_DIR = os.path.join(ROOT, "Assets", "_Project", "Audio", "BGM")


# ── 最小 RIFF/WAVE 读写（只碰 data 块）─────────────────────────────────────
def parse_wav(path):
    with open(path, "rb") as f:
        raw = bytearray(f.read())

    if raw[0:4] != b"RIFF" or raw[8:12] != b"WAVE":
        raise ValueError("不是 RIFF/WAVE：" + path)

    pos = 12
    fmt = None
    data_off = data_len = None
    while pos + 8 <= len(raw):
        cid = bytes(raw[pos:pos + 4])
        csize = struct.unpack_from("<I", raw, pos + 4)[0]
        body = pos + 8
        if cid == b"fmt ":
            (audio_format, channels, sample_rate, _byte_rate,
             block_align, bits) = struct.unpack_from("<HHIIHH", raw, body)
            fmt = dict(audio_format=audio_format, channels=channels,
                       sample_rate=sample_rate, block_align=block_align, bits=bits)
        elif cid == b"data":
            data_off = body
            data_len = csize
        pos = body + csize + (csize & 1)  # 块按偶字节对齐
    if fmt is None or data_off is None:
        raise ValueError("缺 fmt/data 块：" + path)
    if fmt["bits"] != 16 or fmt["audio_format"] != 1:
        raise ValueError("只支持 16-bit PCM：" + path)

    n = data_len // 2
    samples = list(struct.unpack_from("<%dh" % n, raw, data_off))
    return raw, data_off, samples, fmt


def write_wav(path, raw, data_off, samples):
    struct.pack_into("<%dh" % len(samples), raw, data_off, *samples)
    with open(path, "wb") as f:
        f.write(raw)


# ── 度量（与规格 §1 逐字一致）───────────────────────────────────────────────
def peak_dbfs(samples):
    peak = max(abs(s) for s in samples) if samples else 0
    if peak == 0:
        return float("-inf")
    return 20.0 * math.log10(peak / 32768.0)


def lgate_dbfs(samples, floor_dbfs=-50.0):
    thr = 32768.0 * (10.0 ** (floor_dbfs / 20.0))
    active = [s for s in samples if abs(s) > thr]
    if not active:
        return float("-inf")
    ms = sum(float(s) * float(s) for s in active) / len(active)
    return 10.0 * math.log10(ms / (32768.0 ** 2))


# ── 主流程 ─────────────────────────────────────────────────────────────────
def process(path, target_dbfs, gain_db=None, apply=True):
    raw, data_off, samples, fmt = parse_wav(path)
    before_peak = peak_dbfs(samples)
    before_lgate = lgate_dbfs(samples)

    if not apply:
        gain_db = 0.0  # --check：只测不写 ⇒ 报告现值

    if gain_db is None:
        gain_db = target_dbfs - before_peak
    factor = 10.0 ** (gain_db / 20.0)

    new = [max(-32768, min(32767, int(round(s * factor)))) for s in samples]
    after_peak = peak_dbfs(new)
    after_lgate = lgate_dbfs(new)

    name = os.path.basename(path)
    print("%-12s %5dHz/%dch/%dbit/%d帧  "
          "峰值 %+7.2f -> %+7.2f dBFS  增益 %+6.2f dB  "
          "Lgate %+7.2f -> %+7.2f dBFS%s"
          % (name, fmt["sample_rate"], fmt["channels"], fmt["bits"], len(samples) // fmt["channels"],
             before_peak, after_peak, gain_db, before_lgate, after_lgate,
             "  [check]" if not apply else ""))

    if apply and after_peak > -3.0 + 1e-6:
        raise SystemExit("!! 归一化后峰值 %+0.2f > -3 dBFS（硬约束）：%s" % (after_peak, name))

    return raw, data_off, new


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只测量，不写回")
    ap.add_argument("--only", default=None, help="只处理该文件名（如 strike.wav）")
    args = ap.parse_args()

    print("=== SFX（目标 = 规格 §2.2 分类目标峰值）===")
    for name, target in SFX_TARGETS_DBFS.items():
        if args.only and args.only != name:
            continue
        path = os.path.join(SFX_DIR, name)
        raw, data_off, new = process(path, target, apply=not args.check)
        if not args.check:
            write_wav(path, raw, data_off, new)

    if not args.only or args.only == BGM_NAME:
        print("=== BGM（%.0f dB，规格 §7.2）===" % BGM_GAIN_DB)
        path = os.path.join(BGM_DIR, BGM_NAME)
        raw, data_off, new = process(path, None, gain_db=BGM_GAIN_DB, apply=not args.check)
        if not args.check:
            write_wav(path, raw, data_off, new)

    print("=== %s ===" % ("只测量（未写盘）" if args.check else "已写回（纯增益，零后处理）"))


if __name__ == "__main__":
    main()
