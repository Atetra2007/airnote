"""Generate metadata-only catalog status and calibration queue documents."""

import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CATALOG = ROOT / "catalog/songs.json"
DEFAULT_OUTPUTS = {
    ROOT / "catalog/catalog-status.json": "json",
    ROOT / "docs/CATALOG-STATUS.md": "status",
    ROOT / "docs/CALIBRATION-QUEUE.md": "queue",
}
REFERENCE_RHYTHM_STATES = {"not-reviewed", "source-timing-verified"}
STAGE_FILENAME = "calibration-stage-20261014.json"


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def summarize_catalog(catalog, source_sha256, stage=None):
    """Summarize catalog metadata without copying score/source contents."""
    if not isinstance(catalog, dict) or catalog.get("schema_version") != 3:
        raise ValueError("catalog must be a schema_version 3 object")
    songs = catalog.get("songs")
    if not isinstance(songs, list):
        raise ValueError("catalog songs must be a list")

    work_songs = defaultdict(list)
    rhythm_counts = Counter()
    missing_bpm = 0
    null_bpm = 0
    known_bpm = 0
    seen_ids = set()
    seen_variant_ids = set()

    for song in songs:
        if not isinstance(song, dict):
            raise ValueError("every catalog song must be an object")
        song_id = song.get("id")
        work_id = song.get("work_id")
        if not isinstance(song_id, str) or not song_id:
            raise ValueError("every catalog song must have a non-empty id")
        if song_id in seen_ids:
            raise ValueError(f"duplicate catalog song id: {song_id}")
        seen_ids.add(song_id)
        if not isinstance(work_id, str) or not work_id:
            raise ValueError(f"song {song_id} must have a non-empty work_id")

        variant_id = song.get("variant_id") or song_id
        if not isinstance(variant_id, str) or not variant_id:
            raise ValueError(f"song {song_id} has an invalid variant_id")
        if variant_id in seen_variant_ids:
            raise ValueError(f"duplicate catalog variant_id: {variant_id}")
        seen_variant_ids.add(variant_id)

        verification = song.get("verification") or {}
        if not isinstance(verification, dict):
            raise ValueError(f"song {song_id} verification must be an object")
        rhythm = verification.get("rhythm")
        if rhythm not in REFERENCE_RHYTHM_STATES | {"reviewed", "deferred"}:
            raise ValueError(f"song {song_id} has an unknown rhythm status: {rhythm!r}")
        rhythm_counts[rhythm] += 1
        work_songs[work_id].append((song, variant_id, rhythm))

        if "bpm" not in song:
            missing_bpm += 1
        elif song["bpm"] is None:
            null_bpm += 1
        else:
            bpm = song["bpm"]
            if isinstance(bpm, bool) or not isinstance(bpm, int) or not 20 <= bpm <= 300:
                raise ValueError(f"song {song_id} has an invalid BPM value: {bpm!r}")
            known_bpm += 1

    reviewed_works = {
        work_id
        for work_id, versions in work_songs.items()
        if any(rhythm == "reviewed" for _, _, rhythm in versions)
    }
    queue = []
    for work_id, versions in work_songs.items():
        if work_id in reviewed_works:
            continue
        title_counts = Counter(
            str(song.get("title") or song.get("id") or work_id)
            for song, _, _ in versions
        )
        title = sorted(title_counts, key=lambda item: (-title_counts[item], item.casefold(), item))[0]
        variant_ids = sorted(variant_id for _, variant_id, _ in versions)
        deferred_ids = sorted(
            variant_id for _, variant_id, rhythm in versions if rhythm == "deferred"
        )
        queue.append({
            "work_id": work_id,
            "title": title,
            "version_count": len(versions),
            "variant_ids": variant_ids,
            "deferred_variant_ids": deferred_ids,
        })
    queue.sort(key=lambda item: (item["title"].casefold(), item["work_id"]))

    reference_count = sum(rhythm_counts[state] for state in REFERENCE_RHYTHM_STATES)
    version_count = len(songs)
    if sum((rhythm_counts["reviewed"], reference_count, rhythm_counts["deferred"])) != version_count:
        raise ValueError("rhythm status counts do not match the catalog version count")
    if known_bpm + missing_bpm + null_bpm != version_count:
        raise ValueError("BPM counts do not match the catalog version count")

    summary = {
        "schema_version": 1,
        "source": {
            "path": "catalog/songs.json",
            "sha256": source_sha256,
            "catalog_schema_version": catalog["schema_version"],
            "updated_at": catalog.get("updated_at"),
        },
        "counts": {
            "versions": version_count,
            "works": len(work_songs),
            "reviewed_excerpt_versions": rhythm_counts["reviewed"],
            "works_with_reviewed_excerpt": len(reviewed_works),
            "reference_versions": reference_count,
            "not_reviewed_versions": rhythm_counts["not-reviewed"],
            "source_timing_verified_versions": rhythm_counts["source-timing-verified"],
            "deferred_versions": rhythm_counts["deferred"],
            "works_without_reviewed_excerpt": len(queue),
        },
        "bpm": {
            "known_versions": known_bpm,
            "unknown_versions": missing_bpm + null_bpm,
            "unknown_field_missing": missing_bpm,
            "unknown_explicit_null": null_bpm,
        },
        "calibration_queue": {
            "work_count": len(queue),
            "items": queue,
        },
    }
    if stage is not None:
        summary["calibration_stage"] = validate_stage(stage, songs, seen_variant_ids)
    return summary


def validate_stage(stage, songs, variant_ids):
    """Keep an explicit user exclusion separate from musical acceptance."""
    if not isinstance(stage, dict) or stage.get("schema_version") != 1:
        raise ValueError("calibration stage must be a schema_version 1 object")
    if stage.get("status") != "completed-with-user-exclusions":
        raise ValueError("calibration stage has an unsupported status")
    count_keys = (
        "original_scope_works", "original_scope_source_versions", "accepted_source_versions",
        "accepted_arrangements", "excluded_source_versions", "remaining_source_versions",
    )
    for key in count_keys:
        value = stage.get(key)
        if isinstance(value, bool) or not isinstance(value, int) or value < 0:
            raise ValueError(f"calibration stage has an invalid count: {key}")
    if stage["remaining_source_versions"] != 0:
        raise ValueError("completed calibration stage cannot contain remaining sources")
    if (stage["accepted_source_versions"] + stage["excluded_source_versions"]
            != stage["original_scope_source_versions"]):
        raise ValueError("calibration stage source counts do not match its original scope")
    if stage.get("all_original_sources_musically_accepted") is not False:
        raise ValueError("user exclusions must not claim all original sources musically accepted")
    excluded = stage.get("excluded")
    if not isinstance(excluded, list) or len(excluded) != stage["excluded_source_versions"]:
        raise ValueError("calibration stage exclusion count does not match its list")
    seen = set()
    for item in excluded:
        if not isinstance(item, dict) or set(item) != {"source_variant_id", "work_id", "title", "reason"}:
            raise ValueError("calibration stage exclusions must contain only metadata")
        for key in ("source_variant_id", "work_id", "title"):
            if not isinstance(item[key], str) or not item[key]:
                raise ValueError(f"calibration stage exclusion has invalid {key}")
        source_id = item["source_variant_id"]
        if source_id in seen:
            raise ValueError(f"duplicate calibration stage exclusion: {source_id}")
        seen.add(source_id)
        if source_id in variant_ids:
            raise ValueError(f"excluded source is still in current catalog: {source_id}")
        if item["reason"] != "user-excluded":
            raise ValueError("calibration stage exclusions must have reason user-excluded")
    catalog_version = stage.get("accepted_catalog_version")
    if not isinstance(catalog_version, str) or not catalog_version:
        raise ValueError("calibration stage must identify its accepted catalog version")
    arrangements = [song for song in songs if song.get("version") == catalog_version]
    if len(arrangements) != stage["accepted_arrangements"]:
        raise ValueError("calibration stage accepted arrangement count differs from current catalog")
    accepted_sources = set()
    for song in arrangements:
        source_id = song.get("source_record_id")
        if not isinstance(source_id, str) or not source_id:
            raise ValueError("calibration stage arrangement must identify its source")
        if source_id in seen:
            raise ValueError(f"excluded source cannot have an accepted stage arrangement: {source_id}")
        if song.get("verification", {}).get("rhythm") != "reviewed":
            raise ValueError("calibration stage accepted arrangement must remain reviewed")
        accepted_sources.add(source_id)
    if len(accepted_sources) != stage["accepted_source_versions"]:
        raise ValueError("calibration stage accepted source count differs from current catalog")
    return {
        "path": f"catalog/{STAGE_FILENAME}",
        "status": stage["status"],
        **{key: stage[key] for key in count_keys},
        "accepted_catalog_version": catalog_version,
        "all_original_sources_musically_accepted": False,
        "excluded": excluded,
    }


def render_json(summary):
    return (json.dumps(summary, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def _markdown_cell(value):
    return str(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def render_stage_markdown(summary, include_exclusions=False):
    stage = summary.get("calibration_stage")
    if stage is None:
        return ""
    text = f"""## 当前校准阶段处置

2026-10-14 的当前阶段已完成处置：原固定范围为 **{stage['original_scope_works']} 个作品组 / {stage['original_scope_source_versions']} 个来源**；其中 **{stage['accepted_source_versions']} 个来源对应 {stage['accepted_arrangements']} 个已通过编配**，剩余 **{stage['excluded_source_versions']} 个来源由用户主动移出当前总表**，本阶段待处理来源为 **{stage['remaining_source_versions']}**。

阶段结束不等于原范围全部来源音乐通过。主动移出的来源记为 `user-excluded`，没有升级为已通过；同作品的已通过编配继续保留。原材料、历史冻结队列和音乐证据保留在本地，历史台账仍描述当时的音乐验收范围，不能当作当前待办队列。精确处置清单见 [calibration-stage-20261014.json](../catalog/{STAGE_FILENAME})。人工实际试听未进行，真实游戏验收未进行。

"""
    if include_exclusions:
        text += "| 已移出的来源 ID | 作品 | 处置原因 |\n| --- | --- | --- |\n"
        for item in stage["excluded"]:
            text += f"| `{item['source_variant_id']}` | {_markdown_cell(item['title'])} | `user-excluded` |\n"
        text += "\n"
    return text


def render_status_markdown(summary):
    counts = summary["counts"]
    bpm = summary["bpm"]
    source = summary["source"]
    return f"""# 当前曲库状态

本页由 `scripts/catalog_status.py` 根据当前 `catalog/songs.json` 自动生成。统计只描述仓库目录快照，不代表个人安装库，也不替代听音或游戏验证。

目录 SHA-256：`{source['sha256']}`

目录版本更新时间：`{source['updated_at'] or '未填写'}`

| 指标 | 当前目录数量 |
| --- | ---: |
| 作品组 | {counts['works']} |
| 曲谱版本 | {counts['versions']} |
| 已复核片段版本 | {counts['reviewed_excerpt_versions']} |
| 有已复核片段的作品组 | {counts['works_with_reviewed_excerpt']} |
| 参考谱·未校准 | {counts['reference_versions']}（其中来源时值已核对 {counts['source_timing_verified_versions']}，未复核 {counts['not_reviewed_versions']}） |
| 暂缓校对 | {counts['deferred_versions']} |
| 没有已复核片段的作品组 | {counts['works_without_reviewed_excerpt']} |

{render_stage_markdown(summary)}## 未知 BPM

共 **{bpm['unknown_versions']}** 个版本没有已知 BPM：目录中 `bpm` 字段缺失 {bpm['unknown_field_missing']} 个，字段存在但值为 `null` 的有 {bpm['unknown_explicit_null']} 个。已知 BPM 为 {bpm['known_versions']} 个。统计把字段缺失与显式 `null` 都视为未知，不会补默认速度。

## 统计边界

- “已复核片段”表示对指定片段进行逐音视觉音频复核并比对生产解析结果，不表示整首完成或全部版本经人工听校。
- “参考谱·未校准”包括来源时值核对和未复核版本；来源文件或导入成功不代表谱面与原曲一致。
- 作品归并仍可能是暂定判断。复核范围与待处理作品见[校准队列](CALIBRATION-QUEUE.md)。
- 本页只列作品、版本、校准状态和标识等元数据；不包含曲谱正文、音频或来源 URL。

## 历史运行报告

旧的本地运行报告 `runtime-audit-011` 记录了 262 个未知 BPM，但报告中的目录 SHA-256 为 `5814d66871f37a7e2315e67cd9565602731dda0961ae1cc76a99c805d6d34307`，与本页的当前目录哈希不一致。因此它只能说明另一份目录快照，不能作为当前目录的运行验证。本阶段验证见[0.4.2 状态记录](RELEASE-0.4.2-STATUS.md)；只有报告所记目录哈希与目标目录相同，才可称为验证了该目录快照。

运行 `python scripts/catalog_status.py --check` 可检查本页及 JSON 是否与当前目录快照一致。
""".encode("utf-8")


def render_queue_markdown(summary):
    items = summary["calibration_queue"]["items"]
    lines = [
        "# 未复核作品队列",
        "",
        render_stage_markdown(summary, include_exclusions=True).rstrip(),
        "",
        f"当前目录有 **{len(items)} 个作品组**没有已复核片段。队列按作品组列出，并附上可在 `catalog/songs.json` 中定位的版本 ID；同组归并可能仍是暂定判断。此表是整理队列，不意味着条目已校准或可用于演奏。",
        "",
        "暂缓版本会在版本 ID 后标注 `（暂缓）`；这类条目仍保留在队列中，但不代表当前优先复核。未知 BPM 保持未知。",
        "",
        "| # | 作品 | work_id | 版本数 | 版本 ID |",
        "| ---: | --- | --- | ---: | --- |",
    ]
    for index, item in enumerate(items, 1):
        deferred = set(item["deferred_variant_ids"])
        variant_text = ", ".join(
            f"`{variant_id}`" + ("（暂缓）" if variant_id in deferred else "")
            for variant_id in item["variant_ids"]
        )
        lines.append(
            f"| {index} | {_markdown_cell(item['title'])} | `{item['work_id']}` | "
            f"{item['version_count']} | {variant_text} |"
        )
    lines.extend([
        "",
        "逐音复核应绑定具体版本和文件哈希，并明确实际核对的片段起止位置。不要因同组其他版本已复核、来源时值已核对或 BPM 可用而升级标签。",
        "",
        "本文件由 `scripts/catalog_status.py` 生成；运行 `python scripts/catalog_status.py --check` 检查是否陈旧。",
        "",
    ])
    return "\n".join(lines).encode("utf-8")


def build_outputs(catalog_path, output_paths=None, stage_path=None):
    catalog_path = Path(catalog_path)
    raw = catalog_path.read_bytes()
    try:
        catalog = json.loads(raw.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read UTF-8 catalog JSON at {catalog_path}: {error}") from error
    companion = Path(stage_path) if stage_path is not None else catalog_path.parent / STAGE_FILENAME
    stage = None
    if companion.exists():
        try:
            stage = json.loads(companion.read_text(encoding="utf-8-sig"))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError(f"cannot read calibration stage JSON at {companion}: {error}") from error
    elif stage_path is not None:
        raise ValueError(f"calibration stage file does not exist: {companion}")
    summary = summarize_catalog(catalog, sha256_bytes(raw), stage)
    paths = output_paths or DEFAULT_OUTPUTS
    content = {
        "json": render_json(summary),
        "status": render_status_markdown(summary),
        "queue": render_queue_markdown(summary),
    }
    return {Path(path): content[kind] for path, kind in paths.items()}


def write_or_check(outputs, check):
    stale = []
    for path, expected in outputs.items():
        if not path.exists() or path.read_bytes() != expected:
            stale.append(path)
    if check:
        if stale:
            for path in stale:
                print(f"STALE: {path}", file=sys.stderr)
            return False
        print(f"Catalog status is current ({len(outputs)} generated files).")
        return True

    for path, expected in outputs.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(expected)
        print(f"Updated {path.relative_to(ROOT) if path.is_relative_to(ROOT) else path}")
    return True


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", type=Path, default=DEFAULT_CATALOG)
    parser.add_argument("--check", action="store_true", help="fail if a generated file is stale; do not write")
    args = parser.parse_args(argv)
    try:
        outputs = build_outputs(args.catalog)
    except (OSError, ValueError) as error:
        print(f"catalog_status: {error}", file=sys.stderr)
        return 2
    return 0 if write_or_check(outputs, args.check) else 1


if __name__ == "__main__":
    raise SystemExit(main())
