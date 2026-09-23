"""Package only reviewed, original kit files. Never traverse local/ or the repo."""
from hashlib import sha256
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

ROOT = Path(__file__).resolve().parents[1]
NAME = 'harmonica-friends-kit-v0.1.1-logtest'
PACKAGE_FILES = (
    'README.md', 'LICENSE', 'THIRD_PARTY.md', 'CHANGELOG.md',
    'index.html', 'Prepare.cmd',
    'assets/style.css', 'assets/library.js', 'assets/catalog.js',
    'catalog/songs.json', 'catalog/upstream.lock.json', 'catalog/validation.json',
    'scripts/Prepare.ps1',
    'docs/ADDING_SCORES.md', 'docs/VALIDATION.md', 'docs/KNOWN_GAPS.md',
    'docs/FRIEND_TRIAL.md', 'docs/MAINTAINING.md',
    'templates/score-metadata.example.json', 'templates/codex-transcription-prompt.md',
    'scores/original/01-timing-check.txt', 'scores/original/02-stop-check.txt',
    'scores/original/03-import-save-check.txt',
)


def package_files(root=ROOT):
    root = Path(root).resolve()
    result = []
    for name in PACKAGE_FILES:
        path = root / name
        if path.is_symlink() or not path.resolve().is_relative_to(root):
            raise ValueError(f'Unsafe package file: {name}')
        if not path.is_file():
            raise FileNotFoundError(name)
        result.append((name, path))
    return result


def build_package(root=ROOT, output_dir=None):
    root = Path(root)
    files = package_files(root)
    output_dir = Path(output_dir) if output_dir else root / 'artifacts'
    output_dir.mkdir(parents=True, exist_ok=True)
    destination = output_dir / (NAME + '.zip')
    # A fixed timestamp and sorted allowlist make rebuilds deterministic.
    with ZipFile(destination, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
        for name, path in sorted(files):
            entry = ZipInfo(NAME + '/' + name, date_time=(2026, 9, 24, 0, 0, 0))
            entry.compress_type = ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, path.read_bytes())
    checksum = sha256(destination.read_bytes()).hexdigest()
    destination.with_suffix('.sha256').write_text(f'{checksum}  {destination.name}\n', encoding='ascii')
    return destination, checksum


if __name__ == '__main__':
    path, digest = build_package()
    print(f'{path.name}: {path.stat().st_size} bytes; SHA256={digest}')
