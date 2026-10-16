"""Check provenance, honest status, offline data and the publication boundary."""
import hashlib
import json
import math
import re
import subprocess
from html.parser import HTMLParser
from pathlib import Path, PurePosixPath, PureWindowsPath
from urllib.parse import quote, unquote, urlsplit

from build_catalog import render_catalog
from package import PACKAGE_FILES, package_files
from prepare_bundled_scores import bundle_repository_files

ROOT = Path(__file__).resolve().parents[1]
CATALOG_SCHEMAS = {2, 3}
STABLE_ID = re.compile(r'[A-Za-z0-9][A-Za-z0-9-]{0,63}')
SHA256 = re.compile(r'[0-9a-f]{64}')
CALIBRATED_VARIANT_ID = re.compile(r'calibrated-[0-9a-f]{16}')
CALIBRATION_EVIDENCE_FILE = re.compile(r'calibrated-[0-9a-f]{16}\.json')


def is_sha256(value):
    return isinstance(value, str) and SHA256.fullmatch(value) is not None


def require_https(url):
    assert isinstance(url, str) and url and not any(char.isspace() for char in url)
    parts = urlsplit(url)
    assert parts.scheme == 'https' and parts.hostname and parts.username is None and parts.password is None
    try:
        parts.port
    except ValueError as error:
        raise AssertionError(f'Invalid HTTPS URL: {url}') from error


def validate_reference(reference):
    if reference is None:
        return
    assert isinstance(reference, dict)
    if 'url' in reference:
        require_https(reference['url'])
    for key in ('artist', 'edition'):
        if key in reference:
            assert isinstance(reference[key], str) and len(reference[key]) <= 200
    for key in ('segment_start_seconds', 'segment_end_seconds'):
        if key in reference:
            value = reference[key]
            assert isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and value >= 0
    if 'segment_start_seconds' in reference and 'segment_end_seconds' in reference:
        assert reference['segment_end_seconds'] > reference['segment_start_seconds']


def validate_reference_type(reference):
    if reference is None:
        return None
    assert isinstance(reference, dict), 'Reference metadata must be an object.'
    reference_type = reference.get('type')
    assert reference_type in (None, 'recording', 'published-sheet'), 'Reference type must be recording or published-sheet.'
    return reference_type


def validate_published_sheet_reference(reference):
    """Validate score metadata without accepting recording-only timing fields."""
    assert isinstance(reference, dict), 'Published sheet needs a reference object.'
    assert reference.get('type') == 'published-sheet', 'Published sheet reference must declare type published-sheet.'
    validate_reference(reference)
    require_https(reference.get('url'))
    assert isinstance(reference.get('edition'), str) and reference['edition'].strip(), 'Published sheet needs a specific edition.'
    assert is_sha256(reference.get('score_sha256')), 'Published sheet needs a score SHA-256.'
    if 'midi_sha256' in reference:
        assert is_sha256(reference['midi_sha256']), 'Published sheet MIDI SHA-256 is invalid.'
    recording_fields = {'audio_sha256', 'start_seconds', 'segment_start_seconds', 'segment_end_seconds'}
    assert not (recording_fields & reference.keys()), 'Published sheet reference cannot contain recording hashes or timing fields.'


def validate_external_verification(song, schema_version):
    score_type = validate_reference_type(song.get('reference'))
    if score_type == 'published-sheet':
        validate_published_sheet_reference(song.get('reference'))

    verification = song.get('verification', {})
    assert isinstance(verification, dict), 'External import verification metadata must be an object.'
    untested = {'not-tested', 'not-reviewed'}
    for field in ('app_import', 'log_playback', 'game'):
        if field in verification:
            assert verification[field] in untested, f'External import cannot claim {field} verification.'
    rhythm = verification.get('rhythm', 'not-reviewed')
    if rhythm != 'reviewed':
        assert rhythm in untested | {'deferred'}, 'External import rhythm status must be not-tested, not-reviewed, deferred, or reviewed.'
        return

    assert schema_version == 3, 'Only schema 3 external imports may claim reviewed rhythm.'
    assert is_sha256(song.get('verified_sha256')), 'Reviewed external import needs a verified file SHA-256.'
    source_hashes = [song[key] for key in ('source_sha256', 'source_fingerprint_sha256') if key in song]
    assert source_hashes and all(is_sha256(value) for value in source_hashes), 'Reviewed external import needs a source SHA-256 or fingerprint.'

    reference = song.get('reference')
    assert isinstance(reference, dict), 'Reviewed external import needs a concrete reference.'
    if score_type == 'published-sheet':
        validate_published_sheet_reference(reference)
    else:
        validate_reference(reference)
        require_https(reference.get('url'))
        assert isinstance(reference.get('edition'), str) and reference['edition'].strip(), 'Reviewed reference needs a specific edition.'
        assert is_sha256(reference.get('audio_sha256')), 'Reviewed reference needs an audio SHA-256.'
        if 'score_sha256' in reference:
            assert is_sha256(reference['score_sha256']), 'Reference score SHA-256 is invalid.'
        start = reference.get('start_seconds')
        assert isinstance(start, (int, float)) and not isinstance(start, bool) and math.isfinite(start) and start >= 0, 'Reviewed reference needs a valid start time.'

    for field in ('review_note', 'review_method'):
        value = song.get(field)
        assert isinstance(value, str) and value.strip() and len(value) <= 4000, f'Reviewed external import needs a non-empty {field}.'
    variant_id = song.get('variant_id', song.get('id'))
    assert isinstance(variant_id, str) and CALIBRATED_VARIANT_ID.fullmatch(variant_id), 'Reviewed external import needs a calibrated variant ID.'
    evidence = song.get('evidence')
    assert isinstance(evidence, list) and len(evidence) == 1, 'Reviewed external import needs one public evidence manifest.'
    proof = evidence[0]
    assert isinstance(proof, dict) and proof.get('file') == f'catalog/calibration-evidence/{variant_id}.json', 'Evidence must point to the public variant proof manifest.'
    assert is_sha256(proof.get('sha256')), 'Evidence manifest needs a valid SHA-256.'

    validation = song.get('validation')
    assert isinstance(validation, dict), 'Reviewed external import needs validation results.'
    assert validation.get('production_parser') == 'passed', 'Reviewed external import needs a passing production parser check.'
    note_count = validation.get('reviewed_note_count')
    assert type(note_count) is int and note_count > 0, 'Reviewed external import needs a positive reviewed note count.'
    if 'independent_attack_supported_notes' in validation:
        attack_count = validation['independent_attack_supported_notes']
        assert type(attack_count) is int and attack_count > 0, 'Independent attack-supported note count must be positive.'
    parser_error = validation.get('maximum_parser_boundary_error_seconds')
    assert isinstance(parser_error, (int, float)) and not isinstance(parser_error, bool) and math.isfinite(parser_error) and parser_error >= 0, 'Reviewed external import needs a finite parser error.'


def is_link_or_reparse_point(path):
    try:
        metadata = path.lstat()
    except FileNotFoundError:
        return False
    return path.is_symlink() or bool(getattr(metadata, 'st_file_attributes', 0) & 0x400)


def validate_calibration_evidence_directory(root):
    root = Path(root).resolve()
    catalog_dir = root / 'catalog'
    if not catalog_dir.exists() and not is_link_or_reparse_point(catalog_dir):
        return set()
    assert not is_link_or_reparse_point(catalog_dir), 'Catalog directory must not be a link.'
    assert catalog_dir.is_dir(), 'Catalog path must be a directory.'
    assert catalog_dir.resolve().is_relative_to(root), 'Catalog directory escapes the repository root.'

    evidence_dir = catalog_dir / 'calibration-evidence'
    if not evidence_dir.exists() and not is_link_or_reparse_point(evidence_dir):
        return set()
    assert not is_link_or_reparse_point(evidence_dir), 'Calibration evidence directory must not be a link.'
    assert evidence_dir.is_dir(), 'Calibration evidence path must be a directory.'
    assert evidence_dir.resolve().is_relative_to(root), 'Calibration evidence directory escapes the repository root.'

    files = set()
    for path in evidence_dir.iterdir():
        assert CALIBRATION_EVIDENCE_FILE.fullmatch(path.name), f'Unexpected file in calibration evidence directory: {path.name}'
        assert not is_link_or_reparse_point(path) and path.is_file(), f'Calibration evidence must be a regular file: {path.name}'
        assert path.resolve().is_relative_to(root), f'Calibration evidence escapes the repository root: {path.name}'
        files.add(path.relative_to(root).as_posix())
    return files


def validate_calibration_evidence(song, root, available_files=None):
    root = Path(root).resolve()
    if available_files is None:
        available_files = validate_calibration_evidence_directory(root)
    variant_id = song.get('variant_id', song.get('id'))
    assert isinstance(variant_id, str) and CALIBRATED_VARIANT_ID.fullmatch(variant_id), 'Reviewed external import needs a calibrated variant ID.'
    expected_path = f'catalog/calibration-evidence/{variant_id}.json'
    evidence = song.get('evidence')
    assert isinstance(evidence, list) and len(evidence) == 1 and isinstance(evidence[0], dict), 'Reviewed external import needs one evidence manifest.'
    entry = evidence[0]
    assert entry.get('file') == expected_path, 'Evidence must point to the matching calibrated variant manifest.'
    assert expected_path in available_files, f'Calibration evidence file is missing: {expected_path}'
    proof_path = root / expected_path
    assert not is_link_or_reparse_point(proof_path) and proof_path.is_file(), f'Calibration evidence must be a regular file: {expected_path}'
    assert proof_path.resolve().is_relative_to(root), f'Calibration evidence escapes the repository root: {expected_path}'

    proof_bytes = proof_path.read_bytes()
    assert hashlib.sha256(proof_bytes).hexdigest() == entry.get('sha256'), f'Calibration evidence SHA-256 mismatch: {expected_path}'
    proof = json.loads(proof_bytes.decode('utf-8'))
    assert isinstance(proof, dict), f'Calibration evidence must be a JSON object: {expected_path}'

    source_id = song.get('source_record_id', song.get('id'))
    expected_identity = {
        'variant_id': variant_id,
        'work_id': song.get('work_id', song.get('id')),
        'source_id': source_id,
        'sha256': song.get('verified_sha256'),
    }
    for field, value in expected_identity.items():
        assert isinstance(value, str) and proof.get(field) == value, f'Calibration evidence {field} does not match catalog record {song.get("id")}.'
    assert isinstance(source_id, str) and STABLE_ID.fullmatch(source_id), f'Catalog record {song.get("id")} needs a stable source record ID.'
    source_hashes = [song[field] for field in ('source_sha256', 'source_fingerprint_sha256') if field in song]
    assert source_hashes and all(is_sha256(value) for value in source_hashes) and proof.get('source_sha256') in source_hashes, f'Calibration evidence source_sha256 does not match catalog record {song.get("id")}.'
    assert proof.get('source_url') == song.get('source_url'), f'Calibration evidence source_url does not match catalog record {song.get("id")}.'
    assert type(proof.get('schema_version')) is int and proof.get('schema_version') == 1 and proof.get('status') == 'reviewed', f'Calibration evidence is not a reviewed schema 1 manifest: {expected_path}'

    reference = song.get('reference')
    proof_reference = proof.get('reference')
    assert isinstance(reference, dict) and isinstance(proof_reference, dict), f'Calibration evidence reference is missing: {expected_path}'
    score_type = validate_reference_type(reference)
    proof_type = validate_reference_type(proof_reference)
    assert proof_type == score_type, f'Calibration evidence reference.type does not match catalog record {song.get("id")}.'
    if score_type == 'published-sheet':
        validate_published_sheet_reference(reference)
        validate_published_sheet_reference(proof_reference)
        for field in ('type', 'url', 'edition', 'score_sha256', 'midi_sha256'):
            assert (field in proof_reference) == (field in reference) and proof_reference.get(field) == reference.get(field), f'Calibration evidence reference.{field} does not match catalog record {song.get("id")}.'
    else:
        for field in ('url', 'edition', 'audio_sha256', 'start_seconds'):
            assert proof_reference.get(field) == reference.get(field), f'Calibration evidence reference.{field} does not match catalog record {song.get("id")}.'
        if 'score_sha256' in reference:
            assert proof_reference.get('score_sha256') == reference['score_sha256'], f'Calibration evidence reference.score_sha256 does not match catalog record {song.get("id")}.'
        proof_start = proof_reference.get('start_seconds')
        assert isinstance(proof_start, (int, float)) and not isinstance(proof_start, bool) and math.isfinite(proof_start) and proof_start >= 0, f'Calibration evidence start time is invalid: {expected_path}'
    for field in ('review_note', 'review_method'):
        assert proof.get(field) == song.get(field), f'Calibration evidence {field} does not match catalog record {song.get("id")}.'
    proof_validation = proof.get('validation')
    catalog_validation = song.get('validation')
    assert isinstance(proof_validation, dict) and isinstance(catalog_validation, dict), f'Calibration evidence validation is missing: {expected_path}'
    validation_fields = ('production_parser', 'reviewed_note_count', 'maximum_parser_boundary_error_seconds',
                         'independent_attack_supported_notes')
    for field in validation_fields:
        if field not in catalog_validation:
            continue
        assert proof_validation.get(field) == catalog_validation.get(field), f'Calibration evidence validation.{field} does not match catalog record {song.get("id")}.'


def validate_catalog_calibration_evidence(songs, root):
    """Validate every public reviewed proof and reject unreferenced evidence files."""
    root = Path(root).resolve()
    available_files = validate_calibration_evidence_directory(root)
    expected_files = set()
    for song in songs:
        verification = song.get('verification', {})
        if song.get('acquisition', 'direct-download') != 'external-import' or verification.get('rhythm') != 'reviewed':
            continue
        validate_calibration_evidence(song, root, available_files)
        variant_id = song.get('variant_id', song.get('id'))
        expected_files.add(f'catalog/calibration-evidence/{variant_id}.json')
    unexpected = available_files - expected_files
    missing = expected_files - available_files
    assert not unexpected, f'Unreferenced calibration evidence files: {sorted(unexpected)}'
    assert not missing, f'Missing calibration evidence files: {sorted(missing)}'
    return available_files


def validate_acquisition(song, schema_version):
    acquisition = song.get('acquisition', 'direct-download')
    assert acquisition in {'direct-download', 'external-import'}
    assert schema_version == 3 or acquisition == 'direct-download'
    score_type = validate_reference_type(song.get('reference'))
    validate_reference(song.get('reference'))
    if score_type == 'published-sheet':
        validate_published_sheet_reference(song.get('reference'))
    if 'verified_sha256' in song:
        assert is_sha256(song['verified_sha256'])

    if acquisition == 'external-import':
        require_https(song['source_url'])
        assert 'download_url' not in song, 'External imports must not advertise a download URL.'
        assert 'local_path' not in song, 'External import metadata must not expose a local path.'
        if 'filename' in song:
            filename = song['filename']
            assert isinstance(filename, str) and PurePosixPath(filename).name == filename and PureWindowsPath(filename).name == filename and '..' not in filename
        if 'size' in song:
            assert type(song['size']) is int and 0 < song['size'] <= 1_048_576
        if 'sha256' in song:
            assert is_sha256(song['sha256'])
        if 'git_blob_sha1' in song:
            assert re.fullmatch(r'[0-9a-f]{40}', song['git_blob_sha1'])
        if 'source_commit' in song:
            assert re.fullmatch(r'[0-9a-f]{40}', song['source_commit'])
        if 'source_repository' in song:
            assert re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', song['source_repository'])
        if 'source_path' in song:
            source_path = song['source_path']
            assert isinstance(source_path, str) and source_path
            path = PurePosixPath(source_path)
            assert not path.is_absolute() and not PureWindowsPath(source_path).drive
            assert '\\' not in source_path and ':' not in source_path and all(part not in {'.', '..'} for part in path.parts)
        if 'source_sha256' in song:
            assert is_sha256(song['source_sha256'])
        if 'source_fingerprint_sha256' in song:
            assert is_sha256(song['source_fingerprint_sha256'])
        validate_external_verification(song, schema_version)
        return acquisition

    assert type(song['size']) is int and 0 < song['size'] <= 1_048_576
    filename = song['filename']
    assert isinstance(filename, str) and PurePosixPath(filename).name == filename and PureWindowsPath(filename).name == filename and '..' not in filename
    assert song['filename'].endswith('.txt')
    assert re.fullmatch(r'[0-9a-f]{40}', song['source_commit'])
    repository = song['source_repository']
    commit = song['source_commit']
    assert re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository)
    source_path = quote(song['source_path'], safe='/')
    assert song['source_url'] == f"https://github.com/{repository}/blob/{commit}/{source_path}"
    assert song['download_url'] == f"https://raw.githubusercontent.com/{repository}/{commit}/{source_path}"
    assert song['local_path'] == 'local/scores/' + song['filename']
    assert re.fullmatch(r'[0-9a-f]{40}', song['git_blob_sha1'])
    assert is_sha256(song['sha256'])
    return acquisition


def validate_bpm(song, schema_version):
    acquisition = song.get('acquisition', 'direct-download')
    value = song.get('bpm')
    if value is None:
        assert schema_version == 3 and acquisition == 'external-import', 'Only schema 3 external imports may omit BPM.'
        return False
    assert type(value) is int and 20 <= value <= 300, 'BPM must be an integer from 20 to 300.'
    return True


def catalog_identity(song, schema_version):
    if schema_version == 2:
        return song['title'].casefold(), song['variant']
    for field in ('work_id', 'variant_id'):
        if field in song:
            assert isinstance(song[field], str) and STABLE_ID.fullmatch(song[field])
    if 'is_default_variant' in song:
        assert isinstance(song['is_default_variant'], bool)
    return song.get('work_id', song['id']), song.get('variant_id', song['id'])


def validate_catalog_variants(songs, schema_version):
    assert songs and len({song['id'] for song in songs}) == len(songs)
    identities = [catalog_identity(song, schema_version) for song in songs]
    assert len(set(identities)) == len(songs)
    if schema_version == 3:
        defaults = {}
        for song in songs:
            if song.get('is_default_variant') is True:
                work_id = song.get('work_id', song['id'])
                defaults[work_id] = defaults.get(work_id, 0) + 1
        assert all(count <= 1 for count in defaults.values()), 'A work may have at most one default variant.'
    return identities
REPO_ONLY_FILES = {
    '.gitignore', '.gitattributes', 'docs/MAINTAINING.md',
    'scripts/build_catalog.py', 'scripts/check_repo.py', 'scripts/package.py',
    'tests/test_preparation.py', 'tests/test_packaging.py',
    '.github/workflows/check.yml',
    '.github/ISSUE_TEMPLATE/problem.yml', '.github/ISSUE_TEMPLATE/score.yml',
    '.github/ISSUE_TEMPLATE/feature.yml',
    'docs/PROJECT-PRESENTATION.md', 'docs/REPOSITORY-STATUS.md', 'docs/images/airnote.png',
    'global.json', 'scripts/create_icon.py', 'scripts/Build-Desktop.ps1',
    'scripts/check_desktop.py', 'installer/HarmonicaDesk.iss',
    'scripts/prepare_bundled_scores.py', 'tests/test_bundled_scores.py',
    '.github/workflows/desktop.yml', 'docs/DESKTOP-BUILD.md',
    'docs/QUICKSTART.md', 'docs/DESKTOP-VALIDATION.md', 'catalog/desktop-validation.json',
    'src/Harmonica.Core/Harmonica.Core.csproj',
    'src/Harmonica.Core/ScoreCodec.cs', 'src/Harmonica.Core/ScoreModels.cs',
    'src/Harmonica.Windows/Harmonica.Windows.csproj',
    'src/Harmonica.Windows/HotkeyManager.cs', 'src/Harmonica.Windows/NativeInput.cs',
    'src/Harmonica.Windows/PlaybackEngine.cs', 'src/Harmonica.Windows/WaveTone.cs',
    'src/Harmonica.Windows/WindowsPlaybackOutput.cs', 'src/Harmonica.Windows/README.md',
    'src/Harmonica.Windows/Properties/AssemblyInfo.cs',
    'src/Harmonica.InputProbe/Harmonica.InputProbe.csproj',
    'src/Harmonica.InputProbe/Program.cs',
    'src/Harmonica.Desktop/Harmonica.Desktop.csproj',
    'src/Harmonica.Desktop/packages.lock.json',
    'src/Harmonica.Desktop/App.xaml', 'src/Harmonica.Desktop/App.xaml.cs',
    'src/Harmonica.Desktop/app.manifest', 'src/Harmonica.Desktop/Assets/App.ico',
    'src/Harmonica.Desktop/MainWindow.xaml', 'src/Harmonica.Desktop/MainWindow.xaml.cs',
    'src/Harmonica.Desktop/MainWindow.Workspace.cs',
    'src/Harmonica.Desktop/PasteDialog.xaml', 'src/Harmonica.Desktop/PasteDialog.xaml.cs',
    'src/Harmonica.Desktop/SettingsDialog.xaml', 'src/Harmonica.Desktop/SettingsDialog.xaml.cs',
    'src/Harmonica.Desktop/Models/LibraryItem.cs',
    'src/Harmonica.Desktop/Models/LibrarySearch.cs', 'src/Harmonica.Desktop/MainWindow.Library.cs',
    'src/Harmonica.Desktop/Services/LibraryStore.cs',
    'src/Harmonica.Desktop/Services/ThemeService.cs',
    'tests/Harmonica.Core.Tests/Harmonica.Core.Tests.csproj',
    'tests/Harmonica.Core.Tests/ScoreCodecTests.cs', 'tests/Harmonica.Core.Tests/LocalSamples.cs',
    'tests/Harmonica.Core.Tests/packages.lock.json',
    'tests/Harmonica.Windows.Tests/Harmonica.Windows.Tests.csproj',
    'tests/Harmonica.Windows.Tests/Program.cs', 'tests/Harmonica.Windows.Tests/packages.lock.json',
    'tests/Harmonica.Desktop.Tests/Harmonica.Desktop.Tests.csproj',
    'tests/Harmonica.Desktop.Tests/Program.cs', 'tests/Harmonica.Desktop.Tests/packages.lock.json',
    'tests/Harmonica.Desktop.Tests/LibrarySearchChecks.cs',
    'tests/Harmonica.Desktop.Tests/BundledLibraryChecks.cs',
    'tests/Harmonica.Desktop.Tests/RealBundledLibraryAudit.cs',
    'catalog/catalog-status.json', 'docs/CATALOG-STATUS.md', 'docs/CALIBRATION-QUEUE.md',
    'docs/RELEASE-0.4.1-NOTES.md', 'docs/RELEASE-0.4.1-STATUS.md',
    'catalog/calibration-stage-20261014.json',
    'docs/RELEASE-0.4.2-NOTES.md', 'docs/RELEASE-0.4.2-STATUS.md',
    'docs/RELEASE-0.4.3-NOTES.md', 'docs/RELEASE-0.4.3-STATUS.md',
    'tests/test_repo_publication.py',
    'scripts/catalog_status.py', 'tests/test_catalog_status.py',
    'scripts/Audit-SelectedScores.cs', 'scripts/prepare_listening_review.py',
    'scripts/Audit-CatalogBatch.cs', 'scripts/Test-Audit-CatalogBatch.ps1',
    'scripts/Normalize-InventoryTextSources.ps1', 'scripts/Repair-TurkishMarch.cs',
    'scripts/listening_media.py', 'tests/test_listening_media.py',
    'docs/TURKISH-MARCH-REPAIR.md',
}
OPTIONAL_REPO_FILES = {
    'src/Harmonica.Core/packages.lock.json',
    'src/Harmonica.Windows/packages.lock.json',
    'src/Harmonica.InputProbe/packages.lock.json',
    'catalog/release-0.4.0-inventory.json',
    'catalog/social-research-2026-09-24.json',
    'docs/RELEASE-0.4.0-SONG-LIST.md',
    'docs/RELEASE-0.4.0-STATUS.md',
    'docs/RELEASE-0.4.0-NOTES.md',
    'docs/SOCIAL-CATALOG-2026-09-24.md',
}
REPO_ONLY_FILES.update({
    'src/Harmonica.Core/CommunityScoreConverter.cs',
    'tests/Harmonica.Core.Tests/CommunityScoreTests.cs',
    'tests/Harmonica.Desktop.Tests/CatalogAudit.cs',
    'scripts/audit_community.py', 'docs/CATALOG-EXPANSION.md',
    'catalog/catalog-audit.json', 'catalog/research.json',
    'scripts/package_desktop_preview.py',
    'docs/GAME-VALIDATION.md', 'docs/GUIDE-VALIDATION.md', 'docs/GUIDE.md',
    'docs/RHYTHM-VALIDATION.md',
    'src/Harmonica.Core/ScoreTimeline.cs',
    'src/Harmonica.Desktop/GuideNoteView.cs', 'src/Harmonica.Desktop/GuideOverlayWindow.xaml',
    'src/Harmonica.Desktop/GuideOverlayWindow.xaml.cs', 'src/Harmonica.Desktop/MainWindow.Guide.cs',
    'src/Harmonica.InputProbe/GuideProbeWindow.cs',
    'src/Harmonica.Windows/GuideEngine.cs', 'src/Harmonica.Windows/GuideInput.cs',
    'tests/Harmonica.Core.Tests/ScoreTimelineTests.cs',
    'tests/Harmonica.LayoutChecks/Harmonica.LayoutChecks.csproj',
    'tests/Harmonica.LayoutChecks/NativeOverlayChecks.cs', 'tests/Harmonica.LayoutChecks/Program.cs',
    'tests/Harmonica.LayoutChecks/SearchLayoutChecks.cs',
    'tests/Harmonica.Windows.Tests/GuideIsolation.cs',
    'tests/Harmonica.Windows.Tests/GuideRawPacketTests.cs', 'tests/Harmonica.Windows.Tests/GuideTests.cs',
})


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


def validate_repository_index(names, calibration_evidence_files, bundled_score_files=()):
    """Allow exact repository paths, validated proofs and catalog-pinned scores."""
    names = set(names)
    reviewed_files = set(calibration_evidence_files) | set(bundled_score_files)
    unexpected = names - set(PACKAGE_FILES) - REPO_ONLY_FILES - OPTIONAL_REPO_FILES - reviewed_files
    assert not unexpected, f'Unreviewed files in Git index: {sorted(unexpected)}'
    if names:
        missing = (set(PACKAGE_FILES) | REPO_ONLY_FILES | reviewed_files) - names
        assert not missing, f'Required files not staged in Git: {sorted(missing)}'


def check_repo():
    lock = json.loads((ROOT / 'catalog/upstream.lock.json').read_text(encoding='utf-8'))
    catalog = json.loads((ROOT / 'catalog/songs.json').read_text(encoding='utf-8'))
    validation = json.loads((ROOT / 'catalog/validation.json').read_text(encoding='utf-8'))
    assert lock['tag'] == 'v0.2.1'
    assert re.fullmatch(r'[0-9a-f]{40}', lock['commit'])
    assert catalog['source_commit'] == lock['commit']
    assert re.fullmatch(r'[0-9a-f]{64}', lock['asset']['sha256'])
    assert lock['asset']['url'] == f"https://github.com/{lock['repository']}/releases/download/{lock['tag']}/{lock['asset']['name']}"
    schema_version = catalog['schema_version']
    assert schema_version in CATALOG_SCHEMAS
    songs = catalog['songs']
    validate_catalog_variants(songs, schema_version)
    assert {s['id'] for s in songs if s.get('sample', False)} == {'father', 'sunny-day', 'rain-love'}
    assert validation['schema_version'] == 1
    assert validation['scope'] == 'original-player-log-only'
    assert validation['player'] == catalog['player']
    assert re.fullmatch(r'[0-9a-f]{64}', validation['exe_sha256'])
    runs = {run['id']: run for run in validation['song_runs']}
    assert len(runs) == len(validation['song_runs'])
    direct_download_ids = set()
    for song in songs:
        bpm_known = validate_bpm(song, schema_version)
        acquisition = validate_acquisition(song, schema_version)
        if acquisition == 'direct-download':
            direct_download_ids.add(song['id'])
            assert song['redistribution'] in {'permission-unconfirmed', 'source-link-only'}
        elif 'redistribution' in song:
            assert isinstance(song['redistribution'], str) and 0 < len(song['redistribution']) <= 100
        assert len(song.get('aliases', [])) == len(set(song.get('aliases', [])))
        if acquisition == 'direct-download' and song.get('download_format') == 'delta-music/1':
            assert song['redistribution'] == 'source-link-only' and song['legacy_compatible'] is False
            assert song['source_path'].endswith('.deltamusic')
            assert re.fullmatch(r'[0-9a-f]{64}', song['converted_sha256'])
            assert song['verification']['conversion'] == 'passed'
            assert song['verification']['rhythm'] == 'source-timing-verified'
            assert song['events'] > 0 and song['beats'] > 0
            assert bpm_known and abs(song['duration_seconds'] - song['beats'] * 60 / song['bpm']) < .001
        elif acquisition == 'direct-download':
            assert song.get('download_format', 'harmonica-txt') == 'harmonica-txt'
        verification = song.get('verification', {})
        assert isinstance(verification, dict)
        if acquisition != 'external-import':
            assert verification['app_import'] in {'not-tested', 'passed', 'failed'}
            assert verification['log_playback'] in {'not-tested', 'passed', 'failed'}
            if verification['log_playback'] == 'passed':
                run = runs[song['id']]
                assert verification['app_import'] == run['app_import'] == 'passed'
                assert run['log_playback'] == 'passed' and run['completion_observed'] is True
                assert run['bpm'] == song['bpm']
                assert run['events'] > 0 and run['beats'] > 0
                assert abs(run['expected_seconds'] - run['beats'] * 60 / run['bpm']) < 0.001
            assert verification['rhythm'] in {'not-reviewed', 'reviewed', 'issues-found', 'source-timing-verified', 'deferred'}
            assert verification['game'] == 'not-tested'
    assert set(runs) <= direct_download_ids, 'External imports cannot inherit original-player log runs.'
    calibration_evidence_files = validate_catalog_calibration_evidence(songs, ROOT)
    bundled_score_files = bundle_repository_files(ROOT, catalog)
    assert (ROOT / 'assets/catalog.js').read_text(encoding='utf-8') == render_catalog(), 'Run scripts/build_catalog.py'
    audit = json.loads((ROOT / 'catalog/catalog-audit.json').read_text(encoding='utf-8'))
    results = {row['id']: row for row in audit['songs']}
    assert set(results) == direct_download_ids
    for song in songs:
        if song.get('acquisition', 'direct-download') == 'external-import':
            continue
        assert results[song['id']]['status'] == 'passed'
        assert results[song['id']]['source_sha256'] == song['sha256']
        assert results[song['id']]['duration_seconds'] == song['duration_seconds']
    for _, path in package_files(ROOT):
        path.read_text(encoding='utf-8')
    LocalReferences().feed((ROOT / 'index.html').read_text(encoding='utf-8'))
    tracked = subprocess.run(['git', 'ls-files', '-z'], cwd=ROOT, capture_output=True)
    if tracked.returncode == 0:
        names = set(tracked.stdout.decode('utf-8').rstrip('\0').split('\0')) - {''}
        validate_repository_index(names, calibration_evidence_files, bundled_score_files)
    # The distributable must only contain original TXT fixtures, never songs.
    assert all(not name.endswith('.txt') or name.startswith('scores/original/') for name in PACKAGE_FILES)
    assert not any(name.startswith(('src/', 'installer/', 'tests/', '.github/')) for name in PACKAGE_FILES), 'Desktop/development files must stay out of the legacy friends ZIP.'
    print(f'PASS: {len(songs)} pinned source records, {len(runs)} recorded log runs, offline data synchronized; public package allowlist checked.')


if __name__ == '__main__':
    check_repo()
