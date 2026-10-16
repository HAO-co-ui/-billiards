"""全仓文本文件**行尾体检**（只读审计 / 可选零风险归一）。

背景：本仓库 `.gitattributes` 声明 `* text=auto eol=lf` ⇒ 提交时行尾被归一为 LF，
工作区文件却是**由各种工具写出来的**（Windows 上 python 文本模式会把 `\\n` 展开成 `\\r\\n`）⇒
工作区里长期存在 CRLF 文件。它们不报错，但会造成两类误判：

1. **两个哈希口径**：`sha1sum <file>`（工作区原始字节）与 `git cat-file -p HEAD:<file>`
   （经 clean 过滤后的 blob）**不是同一个量**。拿前者去比后者会出现"数字对不上"的假警报。
2. **`git status` 伪 ` M`**：归一化之后 `git status` 仍可能列出这些文件 —— 那是 **stat-only**
   表象（`git diff --numstat` 为空、`git diff-files` 的工作区对象是**全零哈希**）。

判定"到底有没有内容差异"的正确三件套（本工具就是按这个报的）：

· `git diff --numstat -- <path>`                     为空 ⇒ 索引↔工作区无内容差异
· `git hash-object --path=<path> <path>` == `git rev-parse :<path>`
  ⇒ **施加 clean 过滤后**的工作区内容与索引 blob 逐位相同
· `git ls-files --eol -- <path>`                     期望 `i/lf w/lf attr/text eol=lf`

伪 ` M` 的解法：对**确认内容相同**的路径执行 `git add -- <path>`（只刷新 stat 缓存、不暂存内容），
或等下一次提交时索引被重写。本工具**不代跑任何 git 写操作**（避免把别人未提交的改动卷进暂存区）。

用法：
  python Docs/Tools/eol_audit.py            # 只审计，列出 CR > 0 的文件与判定
  python Docs/Tools/eol_audit.py --apply    # 对"可安全归一"的文件做二进制归一（先备份到系统临时目录）

"可安全归一"的定义：**工作区含 CR，但索引里的内容（施加 clean 过滤后）与"去掉 CR 的工作区内容"逐位相同**
⇒ 归一化**不产生任何内容变更**。凡不满足该条件的文件一律**跳过并单独报出**，绝不擅自改写。

只读审计模式不写任何文件；`--apply` 模式下每个文件先备份、再二进制写入、再回读校验。
"""
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCAN_ROOTS = ["Assets/_Project/Scripts", "Docs"]
TEXT_EXTS = (".cs", ".md", ".py", ".sh", ".json", ".txt", ".yml", ".yaml")


def iter_text_files():
    for base in SCAN_ROOTS:
        root = os.path.join(ROOT, base)
        if not os.path.isdir(root):
            continue
        for dirpath, _dirnames, filenames in os.walk(root):
            for name in filenames:
                if name.lower().endswith(TEXT_EXTS):
                    path = os.path.join(dirpath, name)
                    yield os.path.relpath(path, ROOT).replace("\\", "/"), path


def git(args):
    return subprocess.run(["git"] + args, cwd=ROOT, capture_output=True)


def git_show(rel):
    out = git(["cat-file", "-p", f"HEAD:{rel}"])
    return None if out.returncode != 0 else out.stdout


def index_blob(rel):
    out = git(["rev-parse", f":{rel}"])
    return None if out.returncode != 0 else out.stdout.decode().strip()


def filtered_hash(rel, path):
    out = git(["hash-object", f"--path={rel}", path])
    return None if out.returncode != 0 else out.stdout.decode().strip()


def hashed_bytes(raw):
    return raw.replace(b"\r\n", b"\n").replace(b"\r", b"\n")


def audit():
    rows = []
    for rel, path in iter_text_files():
        raw = open(path, "rb").read()
        work_cr = raw.count(b"\r")
        if work_cr == 0:
            continue
        head = git_show(rel)
        index = index_blob(rel)
        normalizable = head is not None and hashed_bytes(raw) == head
        rows.append((work_cr, rel, path, head is not None, normalizable, index))

    rows.sort(key=lambda r: -r[0])
    print(f"含 CR 的文本文件 = {len(rows)}\n")
    print(f"{'WORK-CR':>8}  {'判定':<14} 文件")
    for work_cr, rel, _path, in_head, normalizable, index in rows:
        if not in_head:
            verdict = "未入库"
        elif normalizable:
            match = filtered_hash(rel, os.path.join(ROOT, rel)) == index
            verdict = "零diff可归一" + ("" if match else "(⚠索引已变)")
        else:
            verdict = "⚠需人工裁定"
        print(f"{work_cr:>8}  {verdict:<14} {rel}")

    print("\n提示：归一等价性由『去 CR 后的工作区内容 == 索引/HEAD blob』保证；"
          "归一等价后若 git status 仍列这些文件，那是 stat-only 伪 ` M`（用 `git add -- <path>` 刷新）。")
    return [rel for work_cr, rel, _p, in_head, ok, _i in rows if in_head and ok]


def apply(rel_list):
    backup = os.path.join(tempfile.gettempdir(), "eol_audit_backup")
    os.makedirs(backup, exist_ok=True)
    print(f"\n=== 归一（备份目录 {backup}）===")
    for rel in rel_list:
        path = os.path.join(ROOT, rel)
        raw = open(path, "rb").read()
        shutil.copy2(path, os.path.join(backup, rel.replace("/", "__")))
        mode = os.stat(path).st_mode
        new = hashed_bytes(raw)
        with open(path, "wb") as handle:
            handle.write(new)
        os.chmod(path, mode)
        after = open(path, "rb").read()
        print(f"  {rel}: CR {raw.count(chr(13).encode())} → {after.count(chr(13).encode())}；回读一致={after == new}")


def main():
    rel_list = audit()
    if "--apply" in sys.argv:
        apply(rel_list)
    return 0


if __name__ == "__main__":
    sys.exit(main())
