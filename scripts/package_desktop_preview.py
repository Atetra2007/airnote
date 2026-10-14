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
    docs = {"使用说明.md": ROOT / "docs/GUIDE.md", "验证记录.md": ROOT / "docs/GUIDE-VALIDATION.md",
            "游戏验证.md": ROOT / "docs/GAME-VALIDATION.md", "演奏节奏修复.md": ROOT / "docs/RHYTHM-VALIDATION.md"}
    catalog_status = ""
    if version.startswith("0.4.0"):
        docs.update({"曲库与校准状态.md": ROOT / "docs/RELEASE-0.4.0-STATUS.md",
                     "合并歌单.md": ROOT / "docs/RELEASE-0.4.0-SONG-LIST.md",
                     "曲库盘点.json": ROOT / "catalog/release-0.4.0-inventory.json"})
        catalog_status = (
            "本包包含曲库开发候选，不代表正式 0.4.0 已发行或全部歌曲已校准。\n"
            "新增来源和校正版需要在本机导入；具体缺口见 曲库与校准状态.md。\n\n"
        )
    if version == "0.4.2":
        docs.update({"本版更新说明.md": ROOT / "docs/RELEASE-0.4.2-NOTES.md",
                     "发行验证记录.md": ROOT / "docs/RELEASE-0.4.2-STATUS.md",
                     "曲库与校准状态.md": ROOT / "docs/CATALOG-STATUS.md",
                     "现行队列.md": ROOT / "docs/CALIBRATION-QUEUE.md",
                     "校对阶段处置.json": ROOT / "catalog/calibration-stage-20261014.json"})
        catalog_status = (
            "本阶段339个来源通过，形成340份修订编配；其余13个来源按用户决定移出总表。\n"
            "本包仅含来源与复核元数据及3份原创测试谱；第三方曲谱仍需单独导入。\n"
            "阶段结束不表示真人听辨或实机游戏验收通过。详见 本版更新说明.md。\n\n"
        )
    readme = (
        f"口琴小曲库 v{version} · Windows x64\n\n"
        "完整解压 ZIP 后，打开 App 文件夹，双击 HarmonicaDesk.exe。\n"
        "无需安装 .NET，不要只复制 EXE。已有应用请先正常退出。\n"
        "沿用 %LOCALAPPDATA%\\HarmonicaDesk 的个人曲谱和设置。\n\n"
        "跟谱手弹：F6 开始/暂停/继续，F7 隐藏/显示浮窗，F8 停止。\n"
        "首次 3 秒预备，恢复/循环 2 拍。等键模式要松开音键后重新按下。\n"
        "暂停后可调整倍率、A/B 片段和浮窗；背景不透明度只影响背景。\n"
        "开始或恢复时切到游戏口琴界面。优先使用窗口化或无边框。\n\n"
        "自动演奏：设置中的游戏换音留白默认 60 毫秒，快音自动限制附加留白。\n"
        "原谱 BPM 与休止保留；极短音会在开始前提示，请先核对。\n\n"
        "真实游戏、物理键鼠组合和多显示器兼容性仍需分别验收。\n"
        "详细说明见同目录 使用说明.md；发行边界见 App/Licenses。\n"
    )
    readme += catalog_status
    # Preserve an existing archive if verification fails before the final replacement.
    temp = output.with_suffix(output.suffix + ".tmp")
    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        with ZipFile(temp, "w", ZIP_DEFLATED, compresslevel=6) as archive:
            for record in records:
                name = safe_relative(record["path"])
                archive.write(publish / name, "App/" + name)
            for name, source in docs.items():
                text = source.read_text(encoding="utf-8")
                for original, packaged in (("GUIDE-VALIDATION.md", "验证记录.md"), ("GAME-VALIDATION.md", "游戏验证.md"), ("RHYTHM-VALIDATION.md", "演奏节奏修复.md")):
                    text = text.replace(f"({original})", f"({packaged})")
                for original, packaged in (("RELEASE-0.4.0-SONG-LIST.md", "合并歌单.md"),
                                           ("RELEASE-0.4.0-STATUS.md", "曲库与校准状态.md"),
                                           ("../catalog/release-0.4.0-inventory.json", "曲库盘点.json")):
                    text = text.replace(f"({original})", f"({packaged})")
                if version == "0.4.2":
                    for original, packaged in (("RELEASE-0.4.2-NOTES.md", "本版更新说明.md"),
                                               ("RELEASE-0.4.2-STATUS.md", "发行验证记录.md"),
                                               ("CATALOG-STATUS.md", "曲库与校准状态.md"),
                                               ("CALIBRATION-QUEUE.md", "现行队列.md"),
                                               ("../catalog/calibration-stage-20261014.json", "校对阶段处置.json"),
                                               ("RELEASE-0.4.1-STATUS.md", "https://github.com/Atetra2007/delta-force-harmonica-kit/blob/v0.4.1-preview.1/docs/RELEASE-0.4.1-STATUS.md")):
                        text = text.replace(f"({original})", f"({packaged})")
                archive.writestr(name, text)
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
