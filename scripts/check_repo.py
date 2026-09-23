"""Check provenance, honest status, offline data and the publication boundary."""
import json
import re
import subprocess
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit

from build_catalog import render_catalog
from package import PACKAGE_FILES, package_files

ROOT = Path(__file__).resolve().parents[1]
REPO_ONLY_FILES = {
    '.gitignore', '.gitattributes', 'docs/MAINTAINING.md',
    'scripts/build_catalog.py', 'scripts/check_repo.py', 'scripts/package.py',
    'tests/test_preparation.py', 'tests/test_packaging.py',
    '.github/workflows/check.yml',
    '.github/ISSUE_TEMPLATE/problem.yml', '.github/ISSUE_TEMPLATE/score.yml',
}


class LocalReferences(HTMLParser):
    def handle_starttag(self, tag, attributes):
        for key, value in attributes:
            if key not in {'href', 'src'} or not value:
                continue
            parts = urlsplit(value)
            if parts.scheme:
                assert parts.scheme == 'https' and key == 'href', f'Unexpected network dependency: {value}'
                continue
            if not parts.path:
                continue
            relative = unquote(parts.path)
            assert relative in PACKAGE_FILES, f'Local page reference missing from friends package: {value}'


def check_repo():
    lock = json.loads((ROOT / 'catalog/upstream.lock.json').read_text(encoding='utf-8'))
    catalog = json.loads((ROOT / 'catalog/songs.json').read_text(encoding='utf-8'))
    validation = json.loads((ROOT / 'catalog/validation.json').read_text(encoding='utf-8'))
    assert lock['tag'] == 'v0.2.1'
    assert re.fullmatch(r'[0-9a-f]{40}', lock['commit'])
    assert catalog['source_commit'] == lock['commit']
    assert re.fullmatch(r'[0-9a-f]{64}', lock['asset']['sha256'])
    assert lock['asset']['url'] == f"https://github.com/{lock['repository']}/releases/download/{lock['tag']}/{lock['asset']['name']}"
    songs = catalog['songs']
    assert len(songs) == 10 and len({song['id'] for song in songs}) == 10
    assert {s['id'] for s in songs if s['sample']} == {'father', 'sunny-day', 'rain-love'}
    assert validation['schema_version'] == 1
    assert validation['scope'] == 'original-player-log-only'
    assert validation['player'] == catalog['player']
    assert re.fullmatch(r'[0-9a-f]{64}', validation['exe_sha256'])
    runs = {run['id']: run for run in validation['song_runs']}
    assert len(runs) == len(validation['song_runs'])
    assert set(runs) <= {song['id'] for song in songs}
    for song in songs:
        assert 20 <= song['bpm'] <= 300
        assert 0 < song['size'] <= 1_048_576
        assert Path(song['filename']).name == song['filename'] and '..' not in song['filename']
        assert song['filename'].endswith('.txt')
        assert song['source_commit'] == lock['commit']
        source_path = quote(song['source_path'], safe='/')
        assert song['source_url'] == f"https://github.com/{lock['repository']}/blob/{lock['commit']}/{source_path}"
        assert song['download_url'] == f"https://raw.githubusercontent.com/{lock['repository']}/{lock['commit']}/{source_path}"
        assert song['local_path'] == 'local/scores/' + song['filename']
        assert re.fullmatch(r'[0-9a-f]{40}', song['git_blob_sha1'])
        assert re.fullmatch(r'[0-9a-f]{64}', song['sha256'])
        assert song['redistribution'] == 'permission-unconfirmed'
        assert song['verification']['app_import'] in {'not-tested', 'passed', 'failed'}
        assert song['verification']['log_playback'] in {'not-tested', 'passed', 'failed'}
        if song['verification']['log_playback'] == 'passed':
            run = runs[song['id']]
            assert song['verification']['app_import'] == run['app_import'] == 'passed'
            assert run['log_playback'] == 'passed' and run['completion_observed'] is True
            assert run['bpm'] == song['bpm']
            assert run['events'] > 0 and run['beats'] > 0
            assert abs(run['expected_seconds'] - run['beats'] * 60 / run['bpm']) < 0.001
        assert song['verification']['rhythm'] in {'not-reviewed', 'reviewed', 'issues-found'}
        assert song['verification']['game'] == 'not-tested'
    assert (ROOT / 'assets/catalog.js').read_text(encoding='utf-8') == render_catalog(), 'Run scripts/build_catalog.py'
    for _, path in package_files(ROOT):
        path.read_text(encoding='utf-8')
    LocalReferences().feed((ROOT / 'index.html').read_text(encoding='utf-8'))
    tracked = subprocess.run(['git', 'ls-files', '-z'], cwd=ROOT, capture_output=True)
    if tracked.returncode == 0:
        names = set(tracked.stdout.decode('utf-8').rstrip('\0').split('\0')) - {''}
        unexpected = names - set(PACKAGE_FILES) - REPO_ONLY_FILES
        assert not unexpected, f'Unreviewed files in Git index: {sorted(unexpected)}'
        if names:
            missing = (set(PACKAGE_FILES) | REPO_ONLY_FILES) - names
            assert not missing, f'Required files not staged in Git: {sorted(missing)}'
    # The distributable must only contain original TXT fixtures, never songs.
    assert all(not name.endswith('.txt') or name.startswith('scores/original/') for name in PACKAGE_FILES)
    print(f'PASS: {len(songs)} pinned source records, {len(runs)} recorded log runs, offline data synchronized; public package allowlist checked.')


if __name__ == '__main__':
    check_repo()
