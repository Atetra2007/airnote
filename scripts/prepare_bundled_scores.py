"""Prepare or verify the exact catalog-reviewed TXT library shipped with the app.

Preparation uses accepted local review ledgers when available. Verification and
desktop builds use only committed scores/bundled files, never local research.
The project source license does not relicense the included song arrangements.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
BUNDLE = 'scores/bundled'
MANIFEST = f'{BUNDLE}/manifest.json'
SCORE_ID = re.compile(r'calibrated-[0-9a-f]{16}')
SHA256 = re.compile(r'[0-9a-f]{64}')
LEDGERS = (
    ('local/calibration-221/accepted-review-ledger.json', 'accepted', 'catalog_variant_id'),
    ('local/catalog-0.4.0/coverage-ledger.json', 'calibrations', 'variant_id'),
)
METADATA_FIELDS = ('title', 'work_id', 'source_url', 'redistribution')
ENTRY_FIELDS = {'catalog_id', 'file', 'sha256', *METADATA_FIELDS}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def safe_path(root: Path, path: Path) -> None:
    """Reject linked files/directories as well as paths outside the repository."""
    require(path.resolve().is_relative_to(root), f'Path escapes repository: {path}')
    for part in (path, *path.parents):
        if part == root:
            break
        linked = part.is_symlink()
        if part.exists():
            linked |= bool(getattr(part.lstat(), 'st_file_attributes', 0) & 0x400)
        require(not linked, f'Linked bundle/source path: {part}')


def reviewed_catalog(root: Path, catalog: dict | None = None) -> dict[str, dict]:
    if catalog is None:
        catalog = json.loads((root / 'catalog/songs.json').read_text(encoding='utf-8'))
    result = {}
    for song in catalog['songs']:
        if song.get('verification', {}).get('rhythm') != 'reviewed':
            continue
        identity = song.get('id')
        require(isinstance(identity, str) and SCORE_ID.fullmatch(identity),
                f'Bundled score needs a calibrated catalog ID: {identity}')
        require(identity not in result, f'Duplicate reviewed catalog ID: {identity}')
        digest = song.get('verified_sha256')
        require(isinstance(digest, str) and SHA256.fullmatch(digest),
                f'Bundled score needs a verified SHA-256: {identity}')
        for field in METADATA_FIELDS:
            require(isinstance(song.get(field), str) and bool(song[field]),
                    f'Bundled score lacks catalog {field}: {identity}')
        result[identity] = song
    return result


def bundle_entry(song: dict) -> dict:
    return {'catalog_id': song['id'], 'file': song['id'] + '.txt',
            'sha256': song['verified_sha256'],
            **{field: song[field] for field in METADATA_FIELDS}}


def validate_bundle(root: Path = ROOT, catalog: dict | None = None) -> dict:
    """Verify all committed bytes against both the manifest and live catalog."""
    root = Path(root).resolve()
    expected = reviewed_catalog(root, catalog)
    directory = root / BUNDLE
    safe_path(root, directory)
    require(directory.is_dir(), 'Bundled score directory is missing.')
    manifest_path = root / MANIFEST
    safe_path(root, manifest_path)
    require(manifest_path.is_file(), 'Bundled score manifest is missing.')
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    require(type(manifest.get('schema_version')) is int and manifest['schema_version'] == 1,
            'Unsupported bundled score manifest schema.')
    rows = manifest.get('songs')
    require(isinstance(rows, list), 'Bundled score manifest needs a songs list.')
    seen = set()
    files = {'manifest.json'}
    byte_count = 0
    for row in rows:
        require(isinstance(row, dict) and set(row) == ENTRY_FIELDS,
                'Unexpected bundled score manifest fields.')
        identity = row.get('catalog_id')
        require(isinstance(identity, str) and identity in expected and identity not in seen,
                f'Unknown, unreviewed or duplicate bundled score: {identity}')
        require(row == bundle_entry(expected[identity]),
                f'Bundled score metadata differs from reviewed catalog: {identity}')
        filename = row['file']
        path = directory / filename
        safe_path(root, path)
        require(path.is_file(), f'Bundled score is missing: {filename}')
        content = path.read_bytes()
        require(hashlib.sha256(content).hexdigest() == row['sha256'],
                f'Bundled score SHA-256 mismatch: {filename}')
        # Readability is checked without rewriting line endings or encoding.
        content.decode('utf-8-sig')
        byte_count += len(content)
        seen.add(identity)
        files.add(filename)
    require(seen == set(expected), f'Reviewed scores missing from bundle: {sorted(set(expected) - seen)}')
    for path in directory.iterdir():
        safe_path(root, path)
        require(path.is_file() and path.name in files, f'Unlisted file in bundled scores: {path.name}')
    require(manifest.get('total_scores') == len(rows), 'Bundled score count mismatch.')
    require(manifest.get('total_works') == len({song['work_id'] for song in expected.values()}),
            'Bundled work count mismatch.')
    require(manifest.get('total_bytes') == byte_count, 'Bundled byte count mismatch.')
    return manifest


def bundle_repository_files(root: Path = ROOT, catalog: dict | None = None) -> set[str]:
    manifest = validate_bundle(root, catalog)
    return {MANIFEST, *(f'{BUNDLE}/{row["file"]}' for row in manifest['songs'])}


def prepare_bundle(root: Path = ROOT) -> dict:
    root = Path(root).resolve()
    expected = reviewed_catalog(root)
    sources = {}
    for filename, key, identity_key in LEDGERS:
        ledger = root / filename
        if not ledger.exists():
            continue
        safe_path(root, ledger)
        for row in json.loads(ledger.read_text(encoding='utf-8-sig'))[key]:
            identity = row.get(identity_key)
            if identity not in expected:
                continue
            require(identity not in sources, f'Duplicate accepted source: {identity}')
            require(row.get('sha256') == expected[identity]['verified_sha256'],
                    f'Accepted ledger hash differs from catalog: {identity}')
            source = root / row['file']
            safe_path(root, source)
            require(source.resolve().is_relative_to(root / 'local') and source.suffix == '.txt',
                    f'Accepted source is outside local TXT research: {identity}')
            sources[identity] = source

    directory = root / BUNDLE
    safe_path(root, directory)
    expected_files = {'manifest.json', *(identity + '.txt' for identity in expected)}
    if directory.exists():
        for path in directory.iterdir():
            safe_path(root, path)
            require(path.is_file() and path.name in expected_files,
                    f'Unexpected existing bundle file; review before removing: {path.name}')
    content_by_id = {}
    for identity, song in sorted(expected.items()):
        # Existing verified bytes make repeat preparation work in a clean clone.
        source = sources.get(identity, directory / (identity + '.txt'))
        safe_path(root, source)
        require(source.is_file(), f'Accepted TXT is unavailable: {identity}; update an accepted review ledger.')
        content = source.read_bytes()
        require(hashlib.sha256(content).hexdigest() == song['verified_sha256'],
                f'Accepted TXT SHA-256 mismatch: {identity}')
        content.decode('utf-8-sig')
        content_by_id[identity] = content

    # Resolve and validate the entire input before writing any packaged bytes.
    manifest = {'schema_version': 1, 'total_scores': len(expected),
                'total_works': len({song['work_id'] for song in expected.values()}),
                'total_bytes': sum(map(len, content_by_id.values())),
                'songs': [bundle_entry(expected[identity]) for identity in sorted(expected)]}
    directory.mkdir(parents=True, exist_ok=True)
    for identity, content in content_by_id.items():
        destination = directory / (identity + '.txt')
        if not destination.exists() or destination.read_bytes() != content:
            destination.write_bytes(content)
    manifest_content = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    if not (root / MANIFEST).exists() or (root / MANIFEST).read_bytes() != manifest_content:
        (root / MANIFEST).write_bytes(manifest_content)
    return validate_bundle(root)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Verify committed bundle without reading local ledgers.')
    args = parser.parse_args()
    manifest = validate_bundle() if args.check else prepare_bundle()
    print(f'PASS: {manifest["total_scores"]} bundled scores, {manifest["total_works"]} works, '
          f'{manifest["total_bytes"]} original TXT bytes; all verified hashes match.')


if __name__ == '__main__':
    main()
