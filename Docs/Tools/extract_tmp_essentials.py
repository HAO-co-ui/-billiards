#!/usr/bin/env python3
"""解包 `com.unity.ugui` 包内的 **TMP Essential Resources.unitypackage** 到工程 `Assets/`。

**为什么不用 `AssetDatabase.ImportPackage`**（实测结论，2026-10-15）：
该 API 在 **`-batchmode`** 下**静默什么都不做** —— 返回后既无报错也无资产落盘
（日志里只有一条 `Asset Pipeline Refresh`，`Assets/TextMesh Pro/` 根本没生成），
随后 `TMP_FontAsset.CreateFontAsset` 因缺 `TMP_Settings` 抛 `NullReferenceException`
（`TMP_Settings.get_clearDynamicDataOnBuild`）。该 API 依赖编辑器交互流程，批处理不可靠。

**本工具的做法**：`.unitypackage` 就是 **gzip 压缩的 tar**，条目形如
`<guid>/pathname`（目标资产路径）、`<guid>/asset`（文件内容）、`<guid>/asset.meta`（**带 GUID 的元数据**）。
逐条还原 ⇒ **GUID 与包内一致**，引用不会断，且完全确定性、可重复、无编辑器交互。

用法：
    <venv python> Docs/Tools/extract_tmp_essentials.py            # 缺则解包，已在位则跳过
    <venv python> Docs/Tools/extract_tmp_essentials.py --force    # 强制重写
退出码：0 = 成功（含"已在位"跳过）；2 = 找不到资源包；3 = 写入后仍缺 TMP Settings。
"""

import argparse
import glob
import os
import sys
import tarfile

SETTINGS_REL = os.path.join("Assets", "TextMesh Pro", "Resources", "TMP Settings.asset")


def project_root() -> str:
    return os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def find_package(root: str):
    pattern = os.path.join(root, "Library", "PackageCache", "com.unity.ugui*",
                           "Package Resources", "TMP Essential Resources.unitypackage")
    hits = sorted(glob.glob(pattern))
    return hits[0] if hits else None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--force", action="store_true", help="已在位也重新解包")
    args = parser.parse_args()

    root = project_root()
    settings = os.path.join(root, SETTINGS_REL)

    if os.path.exists(settings) and not args.force:
        print(f"[skip] 已在位：{SETTINGS_REL}")
        return 0

    package = find_package(root)
    if package is None:
        print("[error] 找不到 com.unity.ugui 包内的 TMP Essential Resources.unitypackage")
        return 2

    print(f"[info] 资源包：{os.path.relpath(package, root)}")

    files_written = 0
    dirs_created = 0
    metas_written = 0

    with tarfile.open(package, "r:gz") as archive:
        by_guid = {}
        for member in archive.getmembers():
            # ⚠ 条目名带 `./` 前缀（实测：`./<guid>/pathname`）——不去掉就会把 `"."` 当成 guid、一条都还原不出。
            name = member.name[2:] if member.name.startswith("./") else member.name
            parts = name.split("/")
            if len(parts) < 2:
                continue
            guid, kind = parts[0], "/".join(parts[1:])
            by_guid.setdefault(guid, {})[kind] = member

        for guid, members in sorted(by_guid.items()):
            if "pathname" not in members:
                continue

            pathname = archive.extractfile(members["pathname"]).read().decode("utf-8")
            pathname = pathname.replace("\ufeff", "").strip()
            if not pathname:
                continue

            destination = os.path.join(root, pathname)

            payload = members.get("asset")
            if payload is not None:
                os.makedirs(os.path.dirname(destination), exist_ok=True)
                with open(destination, "wb") as handle:
                    handle.write(archive.extractfile(payload).read())
                files_written += 1
            else:
                os.makedirs(destination, exist_ok=True)
                dirs_created += 1

            meta = members.get("asset.meta")
            if meta is not None:
                os.makedirs(os.path.dirname(destination), exist_ok=True)
                with open(destination + ".meta", "wb") as handle:
                    handle.write(archive.extractfile(meta).read())
                metas_written += 1

    print(f"[ok] 文件 {files_written} 个、目录 {dirs_created} 个、meta {metas_written} 个")

    if not os.path.exists(settings):
        print(f"[error] 写入后仍缺 {SETTINGS_REL}")
        return 3

    print(f"[ok] {SETTINGS_REL} 已在位")
    return 0


if __name__ == "__main__":
    sys.exit(main())
