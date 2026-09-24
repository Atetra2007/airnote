"""Build and verify a local preview ZIP from the exact desktop payload manifest."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from check_desktop import ROOT, require, safe_relative, validate_payload


def package(publish: Path, manifest: Path, output: Path) -> None:
    records = validate_payload(publish)
    metadata = json.loads(manifest.read_text(encoding="utf-8"))
    require(metadata["files"] == records, "Payload differs from its manifest; rerun the build checks.")
    version = metadata["version"]
    docs = {"使用说明.md": ROOT / "docs/GUIDE.md", "验证记录.md": ROOT / "docs/GUIDE-VALIDATION.md"}
    readme = (
        f"口琴小曲库 v{version} · Windows x64 本地预览\n\n"
        "完整解压 ZIP 后，打开 App 文件夹，双击 HarmonicaDesk.exe。\n"
        "无需安装 .NET，不要只复制 EXE。已有应用请先正常退出。\n"
        "沿用 %LOCALAPPDATA%\\HarmonicaDesk 的个人曲谱和设置。\n\n"
        "跟谱手弹：F6 开始/暂停/继续，F7 隐藏/显示浮窗，F8 停止。\n"
        "首次 3 秒预备，恢复/循环 2 拍。等键模式要松开音键后重新按下。\n"
        "暂停后可调整倍率、A/B 片段和浮窗；背景不透明度只影响背景。\n"
        "开始或恢复时切到游戏口琴界面。优先使用窗口化或无边框。\n\n"
        "此包是本地功能预览；真实游戏、物理键鼠组合和多显示器兼容性仍待验收。\n"
        "详细说明见同目录 使用说明.md；发行边界见 App/Licenses。\n"
    )
    # Preserve an existing archive if verification fails before the final replacement.
    temp = output.with_suffix(output.suffix + ".tmp")
    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        with ZipFile(temp, "w", ZIP_DEFLATED, compresslevel=6) as archive:
            for record in records:
                name = safe_relative(record["path"])
                archive.write(publish / name, "App/" + name)
            for name, source in docs.items():
                archive.writestr(name, source.read_text(encoding="utf-8").replace("(GUIDE-VALIDATION.md)", "(验证记录.md)"))
            archive.writestr("开始使用.txt", readme)
            archive.writestr("publish-manifest.json", manifest.read_bytes())
        expected = {"App/" + record["path"] for record in records} | set(docs) | {"开始使用.txt", "publish-manifest.json"}
        with ZipFile(temp) as archive:
            require(set(archive.namelist()) == expected and len(archive.namelist()) == len(expected), "Unexpected ZIP contents.")
            require(archive.testzip() is None, "ZIP CRC verification failed.")
            for record in records:
                data = archive.read("App/" + record["path"])
                require(len(data) == record["size"] and hashlib.sha256(data).hexdigest() == record["sha256"], "ZIP payload hash mismatch: " + record["path"])
        temp.replace(output)
    finally:
        temp.unlink(missing_ok=True)
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(f"{digest}  {output.name}\n", encoding="ascii")
    print(f"PASS: verified {len(records)} application files and preview instructions; {output.stat().st_size} bytes; SHA256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    package(args.publish, args.manifest, args.output)
