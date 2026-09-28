"""Build an offline A/B listening worksheet from a fresh production-parser audit.

Requires numpy and soundfile, already present in the local analysis environment.
Never downloads media, modifies a score, or promotes its review status.
"""
from __future__ import annotations

import argparse
import hashlib
import html
import json
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import numpy as np
import soundfile as sf


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("audit", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    audit = json.loads(args.audit.read_text(encoding="utf-8-sig"))
    if audit["catalog_sha256"] != digest(root / "catalog/songs.json"):
        raise ValueError("Audit catalog is stale")
    output = args.audit.resolve().parent / "listening"
    output.mkdir(exist_ok=False)
    ledger = json.loads((root / "local/catalog-0.4.0/coverage-ledger.json").read_text(encoding="utf-8-sig"))
    calibrations = {c["variant_id"]: c for c in ledger["calibrations"] if c.get("status") == "reviewed"}
    catalog = {s["id"]: s for s in json.loads((root / "catalog/songs.json").read_text(encoding="utf-8-sig"))["songs"]}
    reports, cards = [], []
    for score in audit["reviewed_scores"]:
        ident = score["variant_id"]
        calibration = calibrations[ident]
        if not all(e["hash_matches"] for e in score["evidence"]):
            raise ValueError(f"Saved evidence changed: {ident}")
        proposal_path = next(root / e["file"] for e in calibration["evidence"]
                             if e["file"].endswith("/manual-timing-proposal.json"))
        proposal = json.loads(proposal_path.read_text(encoding="utf-8"))
        rows = proposal["rows"]
        notes = [e for e in score["events"] if e["midi"] is not None]
        if len(notes) != len(rows):
            raise ValueError(f"Reviewed note count differs: {ident}")
        start = proposal["reference_start_seconds"]
        errors = []
        for event, row in zip(notes, rows):
            if event["midi"] != row["midi"]:
                raise ValueError(f"Reviewed pitch differs: {ident}, event {event['event_number']}")
            errors.extend([abs(event["start_seconds"] + start - row["start_seconds"]),
                           abs(event["end_seconds"] + start - row["end_seconds"] )])
        if max(errors) > 1e-6:
            raise ValueError(f"Reviewed boundaries differ: {ident}: {max(errors)}")
        # Validate every reference byte before cutting an excerpt for local listening.
        ref_id = parse_qs(urlparse(score["reference"]["url"]).query)["v"][0]
        reference = root / "local/audio-workflow/references" / f"{ref_id}.wav"
        if digest(reference) != score["reference"]["audio_sha256"]:
            raise ValueError(f"Reference changed: {ident}")
        with sf.SoundFile(reference) as audio:
            sr = audio.samplerate
            audio.seek(round(start * sr))
            frames = round(score["duration_seconds"] * sr)
            excerpt = audio.read(frames, dtype="float32", always_2d=True)
            if len(excerpt) != frames:
                raise ValueError("Reference excerpt exceeds the saved recording")
        source_name, preview_name = f"{ident}-reference.wav", f"{ident}-score.wav"
        sf.write(output / source_name, excerpt, sr, subtype="PCM_16")
        # Offline illustration of the score's pitches and its preview gaps.
        # This is not a capture of Windows WaveTone or the game's sound generator.
        preview_sr = 44100
        pcm = np.zeros(round(score["duration_seconds"] * preview_sr), dtype=np.float32)
        for event in notes:
            begin = round(event["start_seconds"] * preview_sr)
            end = round((event["end_seconds"] - score["gap_ms"] / 1000) * preview_sr)
            size = max(0, end - begin)
            phase = 2 * np.pi * 440 * 2 ** ((event["midi"] - 69) / 12) * np.arange(size) / preview_sr
            tone = .22 * (np.sin(phase) + .18 * np.sin(2 * phase) + .07 * np.sin(3 * phase)) / 1.25
            fade = min(132, size // 2)
            if fade:
                tone[:fade] *= np.linspace(0, 1, fade)
                tone[-fade:] *= np.linspace(1, 0, fade)
            pcm[begin:end] = tone
        sf.write(output / preview_name, pcm, preview_sr, subtype="PCM_16")
        item = {"title": score["title"], "variant_id": ident, "score_sha256": score["sha256"],
                "reference_start_seconds": start, "reference_end_seconds": rows[-1]["end_seconds"],
                "notes": len(notes), "maximum_boundary_error_seconds": max(errors),
                "accepted_timeline_regression": "passed", "human_listening": "pending", "real_game": "not-tested",
                "review_note": catalog[ident]["review_note"], "reference_excerpt": source_name, "synthesized_preview": preview_name}
        reports.append(item)
        cards.append(f'''<article data-id="{ident}"><h2>{html.escape(score['title'])}</h2>
<p>原录音 {start:.2f}–{rows[-1]['end_seconds']:.2f} 秒 · {len(notes)} 个音 · 仅此片段</p>
<p>{html.escape(item['review_note'])}</p>
<div class="pair"><label>原版录音片段<audio controls preload="none" src="{source_name}"></audio></label>
<label>曲谱离线合成示意<audio controls preload="none" src="{preview_name}"></audio></label></div>
<p class="hint">重点比较音高、重复音、换气和尾音。合成示意含 3 ms 淡入淡出；不是软件实录或游戏音效。</p>
<label>你的听辨结果 <select><option value="pending">尚未听辨</option><option value="acceptable">片段听感可接受</option><option value="needs-fix">需要修改</option></select></label>
<textarea placeholder="记录不自然的位置、漏音或拖长，例如：第 12 秒的重复音…"></textarea></article>''')
    (output / "verification.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    page = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>四首精选片段 · 听辨工作表</title>
<style>body{font:16px/1.7 system-ui,sans-serif;background:#f4f5f1;color:#23312f;max-width:1050px;margin:40px auto;padding:0 24px}h1{font-size:30px}article{background:white;border:1px solid #d8dfd8;border-radius:16px;padding:24px;margin:24px 0}h2{margin:0}.pair{display:grid;grid-template-columns:1fr 1fr;gap:20px}audio{display:block;width:100%;margin-top:8px}.hint{font-size:13px;color:#5b6965}textarea{display:block;box-sizing:border-box;width:100%;height:90px;margin-top:12px;padding:12px;border:1px solid #bccac2;border-radius:8px;font:inherit}select,button{font:inherit;padding:8px 12px;border:1px solid #bccac2;border-radius:8px}button{background:#215e50;color:white;cursor:pointer}small{overflow-wrap:anywhere}@media(max-width:640px){.pair{grid-template-columns:1fr}}</style>
<h1>四首精选片段 · 听辨工作表</h1><p>谱面和已接受的逐音时间线已重新比对。请分别播放原录音和曲谱示意，填写实际感受。当前四首均未获得本轮人工听辨或真实游戏结论。</p>
<p>文件仅供本机复核；保存结果只会下载一份 JSON，不上传数据，也不会自动更改曲库复核标签。</p>''' + "".join(cards) + '''
<button id="export">保存听辨结果</button><script>
const manifest=MANIFEST;
document.querySelectorAll('audio').forEach(a=>a.addEventListener('play',()=>document.querySelectorAll('audio').forEach(b=>{if(a!==b)b.pause()})));
document.getElementById('export').onclick=()=>{const rows=manifest.map(m=>{const c=document.querySelector(`[data-id="${m.variant_id}"]`);return {...m,human_listening:c.querySelector('select').value,listener_notes:c.querySelector('textarea').value}});const blob=new Blob([JSON.stringify({recorded_at:new Date().toISOString(),items:rows},null,2)],{type:'application/json'});const u=URL.createObjectURL(blob),a=document.createElement('a');a.href=u;a.download='listening-results.json';a.click();setTimeout(()=>URL.revokeObjectURL(u),1000)};
</script></html>'''.replace("MANIFEST", json.dumps(reports, ensure_ascii=False).replace("<", "\\u003c"))
    (output / "index.html").write_text(page, encoding="utf-8")
    print(f"Prepared {len(reports)} local listening pairs. All accepted timeline comparisons passed: {output}")


if __name__ == "__main__":
    main()
