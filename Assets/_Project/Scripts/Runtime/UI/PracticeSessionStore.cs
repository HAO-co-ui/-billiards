using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **一条练习会话的持久化记录**（字段名即导出 JSON 的键）。
    ///
    /// 与运行期的 <see cref="PracticeSessionSnapshot"/> **刻意分开**：记录多带身份与校验字段
    /// （`id` / `startedAt` / `mode`）—— 那是"这条存档能不能信"的判据，运行期不需要。
    /// 该拆分照抄参考实现 `session-store.js`：它的 `newSession()` 同样把身份字段与计数器放在同一条记录里，
    /// 而 `restoreSession()` 用身份字段决定**整条丢弃**还是**逐字段归一**。
    /// </summary>
    [Serializable]
    public struct PracticeSessionRecord
    {
        public string id;
        public string startedAt;
        public string mode;

        public int shots;
        public int pots;
        public int fouls;
        public int racks;
        public int reracks;
        public int playAgain;
        public float elapsedMs;
        public int outcome;
    }

    /// <summary>
    /// **本地 JSON 持久化 + 一键导出**（照 `session-store.js` 的语义重写，规格 §3）。
    ///
    /// ── 防御式读取的三条纪律（逐条对应参考实现）──────────────────────────────
    /// ① **身份字段不合格 ⇒ 整条丢弃**：`id` 非空、`startedAt` 可解析、`mode` 在枚举内；
    /// ② **计数值不合格 ⇒ 逐字段归 0**（不因一个坏字段丢掉整条）；负数一律归 0；
    /// ③ **任何解析失败都不得抛异常**（坏 JSON / `null` / 半截串 ⇒ 返回 false）。
    ///
    /// ── 为什么不落工程目录 ────────────────────────────────────────────────
    /// 导出路径取 `Application.persistentDataPath` ⇒ 跑测试**不会**在仓库里产生改动（工程标准 R3-5）。
    /// </summary>
    public static class PracticeSessionStore
    {
        /// <summary>导出文件名（落在 <see cref="ExportPath"/>）。</summary>
        public const string ExportFileName = "practice-sessions.json";

        /// <summary>历史条数上限（照参考实现 `readHistory` 的"最近 30 条"）。</summary>
        public const int MaxHistoryEntries = 30;

        /// <summary>练习模式标识（`mode` 的合法值之一）。</summary>
        public const string ModePractice = "practice";

        /// <summary>对战模式标识（本单不产生，但**保留为合法值** —— 参考实现的记录里存在）。</summary>
        public const string ModeMatch = "match";

        public static string ExportPath => Path.Combine(Application.persistentDataPath, ExportFileName);

        /// <summary>把运行期快照装成一条**带身份**的记录。</summary>
        public static PracticeSessionRecord NewRecord(PracticeSessionSnapshot snapshot, string mode = ModePractice)
        {
            return new PracticeSessionRecord
            {
                id = Guid.NewGuid().ToString("N"),
                startedAt = DateTime.UtcNow.ToString("o"),
                mode = mode,
                shots = Mathf.Max(0, snapshot.shots),
                pots = Mathf.Max(0, snapshot.pots),
                fouls = Mathf.Max(0, snapshot.fouls),
                racks = Mathf.Max(0, snapshot.racks),
                reracks = Mathf.Max(0, snapshot.reracks),
                playAgain = Mathf.Max(0, snapshot.playAgain),
                elapsedMs = Mathf.Max(0f, snapshot.elapsedMs),
                outcome = (int)snapshot.outcome,
            };
        }

        public static string ToJson(PracticeSessionRecord record)
        {
            return JsonUtility.ToJson(record);
        }

        /// <summary>
        /// 解析一条记录。**任何不合格都不抛异常**：身份字段不合格 ⇒ `false`（整条丢弃）；
        /// 计数值不合格 ⇒ 归 0 后仍返回 `true`。
        /// </summary>
        public static bool TryParse(string json, out PracticeSessionRecord record)
        {
            record = default;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            PracticeSessionRecord parsed;
            try
            {
                parsed = JsonUtility.FromJson<PracticeSessionRecord>(json);
            }
            catch (Exception)
            {
                return false;   // 坏 JSON（含 `"{"` 这类半截串）
            }

            // ① 身份字段（不合格 ⇒ 整条丢弃）
            if (string.IsNullOrEmpty(parsed.id))
            {
                return false;
            }

            if (!DateTime.TryParse(parsed.startedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out _))
            {
                return false;
            }

            if (parsed.mode != ModePractice && parsed.mode != ModeMatch)
            {
                return false;
            }

            // ② 计数值（不合格 ⇒ 逐字段归 0，其余保留）
            parsed.shots = Normalize(parsed.shots);
            parsed.pots = Normalize(parsed.pots);
            parsed.fouls = Normalize(parsed.fouls);
            parsed.racks = Normalize(parsed.racks);
            parsed.reracks = Normalize(parsed.reracks);
            parsed.playAgain = Normalize(parsed.playAgain);
            parsed.elapsedMs = float.IsFinite(parsed.elapsedMs) && parsed.elapsedMs >= 0f ? parsed.elapsedMs : 0f;
            if (parsed.outcome < 0 || parsed.outcome > (int)RackOutcome.RackEnded)
            {
                parsed.outcome = (int)RackOutcome.None;
            }

            record = parsed;
            return true;
        }

        /// <summary>把一条记录并入历史。**空会话不入档**（照参考实现的 `archiveSession`）；超出上限只保留最近 N 条。</summary>
        public static string AppendToHistory(string historyJson, PracticeSessionRecord record, int maxEntries = MaxHistoryEntries)
        {
            var history = ReadHistory(historyJson);
            if (!IsExportable(record))
            {
                return ToHistoryJson(history);
            }

            history.RemoveAll(existing => existing.id == record.id);
            history.Add(record);

            int limit = Mathf.Max(1, maxEntries);
            if (history.Count > limit)
            {
                history.RemoveRange(0, history.Count - limit);
            }

            return ToHistoryJson(history);
        }

        /// <summary>历史数组的序列化（`ToJson` 只收单条记录，故历史另走这一处）。</summary>
        private static string ToHistoryJson(List<PracticeSessionRecord> entries)
        {
            return JsonUtility.ToJson(new HistoryWrapper { entries = entries.ToArray() });
        }

        /// <summary>读历史（坏串 ⇒ 空历史，不抛）。</summary>
        public static List<PracticeSessionRecord> ReadHistory(string historyJson)
        {
            var result = new List<PracticeSessionRecord>();
            if (string.IsNullOrWhiteSpace(historyJson))
            {
                return result;
            }

            HistoryWrapper wrapper;
            try
            {
                wrapper = JsonUtility.FromJson<HistoryWrapper>(historyJson);
            }
            catch (Exception)
            {
                return result;
            }

            if (wrapper?.entries == null)
            {
                return result;
            }

            foreach (PracticeSessionRecord entry in wrapper.entries)
            {
                // 逐条走同一套校验 ⇒ 一条坏记录不会毒掉整份历史。
                if (TryParse(ToJson(entry), out PracticeSessionRecord valid))
                {
                    result.Add(valid);
                }
            }

            return result;
        }

        /// <summary>`shots == 0 && racks == 0` ⇒ 没什么可留的（照参考实现的空会话判据）。</summary>
        public static bool IsExportable(PracticeSessionRecord record)
        {
            return record.shots > 0 || record.racks > 0;
        }

        /// <summary>一键导出：写 <see cref="ExportPath"/>。**写失败返回 false 而不抛**（同参考实现的 `writeSave`）。</summary>
        public static bool TryWriteExport(string historyJson, out string error)
        {
            error = null;
            try
            {
                string directory = Path.GetDirectoryName(ExportPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(ExportPath, historyJson ?? string.Empty);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static int Normalize(int value)
        {
            return value < 0 ? 0 : value;
        }

        [Serializable]
        private sealed class HistoryWrapper
        {
            public PracticeSessionRecord[] entries;
        }
    }
}
