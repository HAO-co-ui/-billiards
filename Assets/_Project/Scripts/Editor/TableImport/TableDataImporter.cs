using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Billiards.Core;
using Billiards.Diagnostics;
using Billiards.Table;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 把参考碰撞数据（Docs/Reference/*.json）导入为运行时真源
    /// <c>Assets/_Project/Data/TableConfig.asset</c>。
    ///
    /// 设计要点：
    ///  · **不依赖 F: 盘**。源数据已入库到 Docs/Reference/，导入器只读该副本，
    ///    因此任何机器 clone 后都能一句 -executeMethod 重跑。
    ///  · **幂等**。以"源 JSON 的 sha256 + 生成器版本 + 段/袋计数"作为跳过条件，
    ///    内容未变时不重写资产（避免无意义的 mtime 抖动与 diff 噪音）。
    ///  · **严格校验**。解析后立即核对 82 段 / 6 袋与关键标量，任何不符抛出带上下文的异常，
    ///    绝不静默产出半成品——把"碰撞数据错了"这类后果严重的错误挡在最早的一步。
    ///  · 菜单与 -executeMethod 双入口，走同一段代码（与 Day 1 同一哲学：配置即代码）。
    ///
    /// 数据来源（入库副本，非运行时内容，不会进构建）：
    ///    Docs/Reference/table-metadata-reference.json   （段/袋几何权威源）
    ///    Docs/Reference/gameplay-markers-reference.json （更完整的单一真源，用于交叉比对）
    /// </summary>
    public static class TableDataImporter
    {
        public const string GeneratorVersion = "TableDataImporter.v1";

        public const string TableJsonPath = "Docs/Reference/table-metadata-reference.json";
        public const string GameplayJsonPath = "Docs/Reference/gameplay-markers-reference.json";
        public const string OutputAssetPath = "Assets/_Project/Data/TableConfig.asset";
        public const string ImportReportFileName = "day2-import.json";

        // ── 菜单入口 ───────────────────────────────────────────────────────

        [MenuItem("Billiards/Data/Import Table Reference Data", false, 200)]
        public static void ImportInteractive()
        {
            try
            {
                ImportResult result = Import();
                Debug.Log(result.ToConsoleSummary());
                EditorUtility.DisplayDialog(
                    "Billiards Table Data Import",
                    $"导入{(result.Regenerated ? "完成" : "跳过（内容未变）")}。\n\n" +
                    $"段 {result.Config.SegmentCount}  袋 {result.Config.PocketCount}\n" +
                    $"资产：{OutputAssetPath}",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError("导入失败：" + ex.Message);
                EditorUtility.DisplayDialog("Billiards Table Data Import", "导入失败：\n" + ex.Message, "OK");
                throw;
            }
        }

        /// <summary>命令行入口：导入 → 写导入报告 → 退出码。报告先落盘再退出。</summary>
        public static void ImportFromCommandLine()
        {
            int exitCode = 0;
            string summary;
            try
            {
                ImportResult result = Import();
                summary = result.ToConsoleSummary();
                Debug.Log(summary);
            }
            catch (Exception ex)
            {
                Debug.LogError("Day-2 数据导入抛出异常：" + ex);
                summary = "IMPORT_FAILED: " + ex;
                exitCode = 1;
            }

            WriteImportReport(summary, exitCode);
            Debug.Log("DAY2_IMPORT_RESULT=" + (exitCode == 0 ? "OK" : "FAILED"));
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        // ── 核心 ───────────────────────────────────────────────────────────

        public struct ImportResult
        {
            public TableConfig Config;
            public bool Regenerated;
            public string SourceSha256;
            public string Message;

            public string ToConsoleSummary()
            {
                var sb = new StringBuilder();
                sb.AppendLine("================ Billiards Day-2 数据导入 ================");
                sb.AppendLine((Regenerated ? "[REGENERATED] " : "[SKIPPED-UPTODATE] ") + Message);
                if (Config != null)
                {
                    sb.AppendLine($"  sourceSha256 : {SourceSha256}");
                    sb.AppendLine($"  segments     : {Config.SegmentCount}");
                    sb.AppendLine($"  pockets      : {Config.PocketCount}");
                    sb.AppendLine($"  innerLength  : {Config.innerLength:R} m");
                    sb.AppendLine($"  innerWidth   : {Config.innerWidth:R} m");
                    sb.AppendLine($"  ballRadius   : {Config.ballRadius:R} m");
                    sb.AppendLine($"  asset        : {OutputAssetPath}");
                }

                sb.AppendLine("=========================================================");
                return sb.ToString();
            }
        }

        public static ImportResult Import()
        {
            string tableFull = ProjectPaths.ToFullPath(TableJsonPath);
            string gameplayFull = ProjectPaths.ToFullPath(GameplayJsonPath);

            RequireFile(tableFull, TableJsonPath);
            RequireFile(gameplayFull, GameplayJsonPath);

            byte[] tableBytes = File.ReadAllBytes(tableFull);
            string tableSha = Sha256Hex(tableBytes);
            string tableJson = Encoding.UTF8.GetString(tableBytes);
            string gameplayJson = File.ReadAllText(gameplayFull, Encoding.UTF8);

            TableMetaDto meta = JsonUtility.FromJson<TableMetaDto>(tableJson);
            if (meta == null || meta.collision == null)
            {
                throw new InvalidDataException(
                    $"无法解析 {TableJsonPath}：JsonUtility 返回 {(meta == null ? "null" : "缺少 collision")}。");
            }

            ValidateTopology(meta, TableJsonPath);

            // 单一真源交叉核对（段与袋逐点比对）。不一致时**不自行选一份了事**，直接抛错，
            // 让人工介入——见 Day 2 任务书要求。
            GameplayMarkersDto gameplay = JsonUtility.FromJson<GameplayMarkersDto>(gameplayJson);
            if (gameplay == null)
            {
                throw new InvalidDataException($"无法解析 {GameplayJsonPath}。");
            }

            ReferenceCrossCheck.Result cross = ReferenceCrossCheck.Compare(meta, gameplay);
            if (!cross.Identical)
            {
                throw new InvalidDataException(
                    $"{TableJsonPath} 与 {GameplayJsonPath} 不一致（{cross.Mismatches.Count} 项）：\n  "
                    + string.Join("\n  ", cross.Mismatches));
            }

            TableConfig existing = ProjectPaths.Exists(OutputAssetPath)
                ? AssetDatabase.LoadAssetAtPath<TableConfig>(OutputAssetPath)
                : null;

            if (existing != null
                && existing.sourceSha256 == tableSha
                && existing.generator == GeneratorVersion
                && existing.SegmentCount == PoolConstants.CushionSegmentCount
                && existing.PocketCount == PoolConstants.PocketCount)
            {
                return new ImportResult
                {
                    Config = existing,
                    Regenerated = false,
                    SourceSha256 = tableSha,
                    Message = "内容未变（源 sha256/生成器版本/计数均匹配），跳过重写。",
                };
            }

            TableConfig config = BuildConfig(meta, tableSha, gameplay);
            WriteAsset(config, existing);

            return new ImportResult
            {
                Config = config,
                Regenerated = true,
                SourceSha256 = tableSha,
                Message = existing == null ? "新建资产。" : "源/版本/计数变化，已重写资产。",
            };
        }

        private static TableConfig BuildConfig(TableMetaDto meta, string sha, GameplayMarkersDto gameplay)
        {
            var config = ScriptableObject.CreateInstance<TableConfig>();

            config.sourceFileName = Path.GetFileName(TableJsonPath);
            config.sourceSha256 = sha;
            config.generator = GeneratorVersion;
            config.generatedAtUtc = DateTime.UtcNow.ToString("o");
            config.runtimeNode = meta.runtime_node ?? string.Empty;
            config.sourceVertexCount = meta.vertices;
            config.sourceTriangleCount = meta.triangles;
            config.sourceUniformScale = meta.uniform_scale;
            config.sourceNonUniformScaleApplied = meta.nonuniform_scale_applied;

            config.innerLength = meta.inner_length;
            config.innerWidth = meta.inner_width;
            config.halfLength = meta.half_length;
            config.halfWidth = meta.half_width;
            config.clothHeight = meta.cloth_y;
            config.noseHeight = meta.nose_height;
            config.ballRadius = meta.collision.ballRadius;
            config.legBottomY = meta.lowest_y;

            if (meta.bounds_runtime != null
                && meta.bounds_runtime.min != null && meta.bounds_runtime.min.Length >= 3
                && meta.bounds_runtime.max != null && meta.bounds_runtime.max.Length >= 3)
            {
                config.outerBoundsMin = new Vector3(meta.bounds_runtime.min[0], meta.bounds_runtime.min[1], meta.bounds_runtime.min[2]);
                config.outerBoundsMax = new Vector3(meta.bounds_runtime.max[0], meta.bounds_runtime.max[1], meta.bounds_runtime.max[2]);
            }

            config.segments = new List<CushionSegment>(meta.collision.segments.Length);
            foreach (SegmentDto s in meta.collision.segments)
            {
                config.segments.Add(new CushionSegment
                {
                    a = new Vector2(s.a.x, s.a.y),
                    b = new Vector2(s.b.x, s.b.y),
                    rail = s.rail ?? string.Empty,
                });
            }

            config.pockets = new List<Pocket>(meta.collision.pockets.Length);
            foreach (PocketDto p in meta.collision.pockets)
            {
                config.pockets.Add(new Pocket
                {
                    index = p.index,
                    kind = p.kind ?? string.Empty,
                    center = ToVec2(p.runtimeCenter),
                    mouthA = ToVec2(p.runtimeMouth, 0),
                    mouthB = ToVec2(p.runtimeMouth, 1),
                    throatA = ToVec2(p.runtimeThroat, 0),
                    throatB = ToVec2(p.runtimeThroat, 1),
                    throatCenter = ToVec2(p.runtimeThroatCenter),
                    throatNormal = ToVec2(p.runtimeThroatNormal),
                    mouthWidth = p.runtimeMouthWidth,
                    throatWidth = p.runtimeThroatWidth,
                    openingRadius = p.openingRadius,
                    captureRadius = p.captureRadius,
                    captureNote = p.captureNote ?? string.Empty,
                });
            }

            if (gameplay.existing_engine_reference_parameters != null)
            {
                if (gameplay.existing_engine_reference_parameters.foot_spot_physics2d != null)
                {
                    Vec2Dto f = gameplay.existing_engine_reference_parameters.foot_spot_physics2d;
                    config.footSpot = new Vector2(f.x, f.y);
                }

                if (gameplay.existing_engine_reference_parameters.off_table_envelope_physics2d != null)
                {
                    OffTableDto o = gameplay.existing_engine_reference_parameters.off_table_envelope_physics2d;
                    config.offTableHalfExtents = new Vector2(o.halfX, o.halfY);
                }
            }

            List<string> consistency = config.ConsistencyErrors();
            if (consistency.Count > 0)
            {
                UnityEngine.Object.DestroyImmediate(config);
                throw new InvalidDataException(
                    "生成结果与 PoolConstants（单位制唯一真源）不一致：\n  " + string.Join("\n  ", consistency));
            }

            return config;
        }

        private static void WriteAsset(TableConfig config, TableConfig existing)
        {
            Directory.CreateDirectory(ProjectPaths.ToFullPath("Assets/_Project/Data"));

            if (existing != null)
            {
                EditorUtility.CopySerialized(config, existing);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(config);
            }
            else
            {
                AssetDatabase.CreateAsset(config, OutputAssetPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void ValidateTopology(TableMetaDto meta, string source)
        {
            if (meta.collision.segments == null || meta.collision.segments.Length != PoolConstants.CushionSegmentCount)
            {
                throw new InvalidDataException(
                    $"{source}: 段数 {((meta.collision.segments == null) ? -1 : meta.collision.segments.Length)}"
                    + $" != 预期 {PoolConstants.CushionSegmentCount}。");
            }

            if (meta.collision.pockets == null || meta.collision.pockets.Length != PoolConstants.PocketCount)
            {
                throw new InvalidDataException(
                    $"{source}: 袋数 {((meta.collision.pockets == null) ? -1 : meta.collision.pockets.Length)}"
                    + $" != 预期 {PoolConstants.PocketCount}。");
            }

            foreach (PocketDto p in meta.collision.pockets)
            {
                if (p.runtimeMouth == null || p.runtimeMouth.Length != 2
                    || p.runtimeThroat == null || p.runtimeThroat.Length != 2
                    || p.runtimeCenter == null || p.runtimeThroatCenter == null
                    || p.runtimeThroatNormal == null)
                {
                    throw new InvalidDataException($"{source}: 袋 {p.index} 的 mouth/throat/center/normal 字段缺失或长度异常。");
                }

                if (p.openingRadius <= 0f || p.captureRadius <= 0f)
                {
                    throw new InvalidDataException($"{source}: 袋 {p.index} 半径非正（opening={p.openingRadius}, capture={p.captureRadius}）。");
                }
            }
        }

        private static Vector2 ToVec2(Vec2Dto v)
        {
            if (v == null)
            {
                throw new InvalidDataException("期望一个 {x,y} 对象，但解析结果为 null。");
            }

            return new Vector2(v.x, v.y);
        }

        private static Vector2 ToVec2(Vec2Dto[] arr, int i)
        {
            if (arr == null || i >= arr.Length || arr[i] == null)
            {
                throw new InvalidDataException($"期望数组下标 {i} 处的 {{x,y}} 对象，但缺失。");
            }

            return new Vector2(arr[i].x, arr[i].y);
        }

        private static void RequireFile(string fullPath, string assetRelative)
        {
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"参考数据缺失：{assetRelative}（解析到 {fullPath}）。"
                    + "该文件应随仓库入库于 Docs/Reference/，不应依赖 F: 盘。");
            }
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        private static void WriteImportReport(string summary, int exitCode)
        {
            try
            {
                Directory.CreateDirectory(SetupReport.ReportsDirectory);
                string path = ProjectPaths.ToFullPath("Docs/Reports/" + ImportReportFileName);
                var payload = new ImportReport
                {
                    generatedAtUtc = DateTime.UtcNow.ToString("o"),
                    unityVersion = Application.unityVersion,
                    isBatchMode = Application.isBatchMode,
                    exitCode = exitCode,
                    succeeded = exitCode == 0,
                    summary = summary,
                };
                File.WriteAllText(path, JsonUtility.ToJson(payload, true), new UTF8Encoding(false));
                Debug.Log("导入报告已写入：" + path);
            }
            catch (Exception ex)
            {
                Debug.LogError("写导入报告失败：" + ex);
            }
        }

        [Serializable]
        private class ImportReport
        {
            public string schema = "billiards.day2-import.v1";
            public string generatedAtUtc;
            public string unityVersion;
            public bool isBatchMode;
            public int exitCode;
            public bool succeeded;
            public string summary;
        }
    }
}
