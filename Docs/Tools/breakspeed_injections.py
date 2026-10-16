"""开球初速标定的**注入式反例**（负对照；只需 EditMode，不需重生成任何产物）。

用法：
  python Docs/Tools/breakspeed_injections.py apply     # 备份 + 注入
  python Docs/Tools/breakspeed_injections.py restore   # 从备份逐字节还原
  python Docs/Tools/breakspeed_injections.py verify    # 校验与备份逐字节一致

注入清单：
  t133-speed   把 `PhysicsTuning.asset` 的 `maxLaunchSpeed` 由 **8** 改回 **4.5** ⇒ `T133` 红。

为什么这条注入是"判别力证明"而不是"为了红而红"：
  `T133` 锁的不变量是"**满力开球必须把球堆打散**"，而它的输入 v0 取自**资产生效值**。
  若这条注入**不红**，说明该判据要么没读资产、要么阈值定得比 4.5 的产出还松
  ⇒ 判据形同虚设（"有守卫 ≠ 守卫会跑"）。反向也成立：这条注入红了，
  才证明"把初速调低"这种退化**会被人看见**。

期望（实测标定，同一夹具）：
  v0 = 8.0 ⇒ 位移 > 0.30 m 的球 = **9** 颗（≥ 8 ⇒ 绿）；
  v0 = 4.5 ⇒ **5** 颗（< 8 ⇒ 红）。

纪律：注入前先备份，`restore` 逐字节还原并打印 sha1；`verify` 与备份比对。
**绝不使用 `git checkout --`** 还原（改动未提交时它会把工作区修复一起回滚）。
"""
import hashlib
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BACKUP = os.environ.get("INJECT_BACKUP", os.path.join(ROOT, "_inject_backup_breakspeed"))

ASSET = "Assets/_Project/Data/PhysicsTuning.asset"

FILES = [ASSET]

PATCHES = [
    ("t133-speed", ASSET, "\n  maxLaunchSpeed: 8\n", "\n  maxLaunchSpeed: 4.5\n"),
]


def full(rel: str) -> str:
    return os.path.join(ROOT, rel.replace("/", os.sep))


def sha1(path: str) -> str:
    with open(path, "rb") as handle:
        return hashlib.sha1(handle.read()).hexdigest()


def eol_of(text: str) -> str:
    return "\r\n" if "\r\n" in text else "\n"


def apply() -> None:
    os.makedirs(BACKUP, exist_ok=True)
    for rel in FILES:
        shutil.copy2(full(rel), os.path.join(BACKUP, rel.replace("/", "__")))

    for tag, rel, old, new in PATCHES:
        path = full(rel)
        text = open(path, "rb").read().decode("utf-8")
        o = old.replace("\n", eol_of(text))
        n = new.replace("\n", eol_of(text))
        count = text.count(o)
        if count != 1:
            raise SystemExit(f"注入 [{tag}] 未命中/不唯一（count={count}）：{rel}")
        with open(path, "wb") as handle:      # ⚠ 二进制写入：文本模式会把 \n 展开成 \r\n（行尾回归）
            handle.write(text.replace(o, n).encode("utf-8"))
        print(f"injected [{tag}] {rel}  (sha1 {sha1(path)[:12]})")


def restore() -> None:
    for rel in FILES:
        src = os.path.join(BACKUP, rel.replace("/", "__"))
        if not os.path.exists(src):
            raise SystemExit(f"缺少备份：{src}")
        shutil.copy2(src, full(rel))
        print(f"restored {rel}  (sha1 {sha1(full(rel))[:12]})")


def verify() -> None:
    bad = 0
    for rel in FILES:
        src = os.path.join(BACKUP, rel.replace("/", "__"))
        a = sha1(src)
        b = sha1(full(rel))
        ok = a == b
        bad += 0 if ok else 1
        print(f"{'OK ' if ok else 'BAD'} {rel}  backup={a[:12]} work={b[:12]}")
    if bad:
        raise SystemExit(f"{bad} 个文件与备份不一致")
    print("verify OK（逐字节一致）")


def main() -> None:
    if len(sys.argv) != 2 or sys.argv[1] not in ("apply", "restore", "verify"):
        raise SystemExit("用法：breakspeed_injections.py apply|restore|verify")
    {"apply": apply, "restore": restore, "verify": verify}[sys.argv[1]]()


if __name__ == "__main__":
    main()
