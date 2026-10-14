import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
import check_repo
import check_desktop

spec = importlib.util.spec_from_file_location('kit_package', ROOT / 'scripts/package.py')
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


def reviewed_external_record(base=None):
    song = {
        **(base or {}),
        'id': 'external-fixture',
        'title': 'External Fixture',
        'acquisition': 'external-import',
        'source_url': 'https://example.com/source',
        'work_id': 'work-fixture',
        'source_record_id': 'source-fixture',
        'variant_id': 'calibrated-0123456789abcdef',
        'verified_sha256': 'a' * 64,
        'source_sha256': 'b' * 64,
        'reference': {
            'type': 'published-sheet',
            'url': 'https://example.com/published-score',
            'edition': 'Official published score, 2024 edition',
            'score_sha256': 'c' * 64,
            'midi_sha256': 'e' * 64,
        },
        'review_note': 'All 84 written notes were checked against the published score.',
        'review_method': 'Pitch, notation and production-parser comparison.',
        'validation': {
            'production_parser': 'passed',
            'reviewed_note_count': 84,
            'maximum_parser_boundary_error_seconds': 0.0001,
        },
        'verification': {'rhythm': 'reviewed', 'app_import': 'not-tested', 'log_playback': 'not-tested', 'game': 'not-tested'},
    }
    return song


def write_calibration_proof(root, song, changes=None):
    variant_id = song['variant_id']
    proof = {
        'schema_version': 1,
        'variant_id': variant_id,
        'work_id': song['work_id'],
        'source_id': song['source_record_id'],
        'source_url': song['source_url'],
        'status': 'reviewed',
        'sha256': song['verified_sha256'],
        'source_sha256': song['source_sha256'],
        'reference': {key: value for key, value in song['reference'].items()
                      if key in {'type', 'url', 'edition', 'audio_sha256', 'start_seconds', 'score_sha256', 'midi_sha256'}},
        'review_note': song['review_note'],
        'review_method': song['review_method'],
        'validation': dict(song['validation']),
    }
    proof.update(changes or {})
    path = root / 'catalog/calibration-evidence' / f'{variant_id}.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    raw = json.dumps(proof, ensure_ascii=False, indent=2).encode('utf-8') + b'\n'
    path.write_bytes(raw)
    song['evidence'] = [{'file': path.relative_to(root).as_posix(), 'sha256': hashlib.sha256(raw).hexdigest()}]
    return path


def write_public_catalog(root, songs):
    path = root / 'catalog/songs.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({'schema_version': 3, 'songs': songs}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return path


class PublicationTests(unittest.TestCase):
    def test_private_files_and_unlisted_scores_never_enter_package(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in package.PACKAGE_FILES:
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / name, target)
            for name in ('local/player/original.exe', 'local/scores/song.txt', 'catalog/unlicensed.txt', '.env'):
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text('PRIVATE MUST NOT SHIP', encoding='utf-8')
            archive, digest = package.build_package(root)
            with zipfile.ZipFile(archive) as zipped:
                self.assertEqual({f'{package.NAME}/{name}' for name in package.PACKAGE_FILES}, set(zipped.namelist()))
                self.assertTrue(all(b'PRIVATE MUST NOT SHIP' not in zipped.read(name) for name in zipped.namelist()))
                self.assertIsNone(zipped.testzip())
            self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), digest)
            self.assertEqual(package.build_package(root)[1], digest)


class CatalogAcquisitionTests(unittest.TestCase):
    def setUp(self):
        self.external = {
            'id': 'external-song',
            'title': 'External Song',
            'bpm': 92,
            'acquisition': 'external-import',
            'source_url': 'https://www.bilibili.com/video/BV1234567890',
        }

    def test_external_import_needs_source_but_not_download_bytes(self):
        self.assertEqual(check_repo.validate_acquisition(self.external, 3), 'external-import')
        self.assertNotIn('download_url', self.external)
        self.assertNotIn('size', self.external)
        self.assertNotIn('sha256', self.external)

    def test_unknown_bpm_is_limited_to_schema3_external_imports(self):
        self.assertTrue(check_repo.validate_bpm(self.external, 3))
        for record in ({**self.external, 'bpm': None}, {key: value for key, value in self.external.items() if key != 'bpm'}):
            self.assertFalse(check_repo.validate_bpm(record, 3))
        with self.assertRaises(AssertionError):
            check_repo.validate_bpm({**self.external, 'bpm': None}, 2)
        for bpm in (19, 301, 92.5, '92', True):
            with self.subTest(bpm=bpm), self.assertRaises(AssertionError):
                check_repo.validate_bpm({**self.external, 'bpm': bpm}, 3)
        for schema_version in (2, 3):
            for record in (
                {'acquisition': 'direct-download'},
                {'acquisition': 'direct-download', 'bpm': None},
            ):
                with self.subTest(schema_version=schema_version, record=record), self.assertRaises(AssertionError):
                    check_repo.validate_bpm(record, schema_version)

    def test_external_import_rejects_download_url_and_non_https_source(self):
        with self.assertRaises(AssertionError):
            check_repo.validate_acquisition({**self.external, 'download_url': 'https://example.com/song.txt'}, 3)
        with self.assertRaises(AssertionError):
            check_repo.validate_acquisition({**self.external, 'local_path': 'C:/Users/name/private.txt'}, 3)
        with self.assertRaises(AssertionError):
            check_repo.validate_acquisition({**self.external, 'source_url': 'http://example.com/song'}, 3)
        with self.assertRaises(AssertionError):
            check_repo.validate_acquisition({**self.external, 'source_url': 'https://user:secret@example.com/song'}, 3)

    def test_external_import_cannot_claim_legacy_schema_or_verified_hash_without_hash_shape(self):
        with self.assertRaises(AssertionError):
            check_repo.validate_acquisition(self.external, 2)
        with self.assertRaises(AssertionError):
            check_repo.validate_acquisition({**self.external, 'verified_sha256': 'not-a-hash'}, 3)

    def test_external_reviewed_rhythm_requires_the_public_evidence_contract(self):
        reviewed = {
            **self.external,
            'variant_id': 'calibrated-0123456789abcdef',
            'verified_sha256': 'a' * 64,
            'source_fingerprint_sha256': 'b' * 64,
            'reference': {
                'type': 'published-sheet',
                'url': 'https://example.com/published-score',
                'edition': 'Official published score, 2024 edition',
                'score_sha256': 'c' * 64,
                'midi_sha256': 'e' * 64,
            },
            'review_note': 'All 84 written notes were checked against the published score.',
            'review_method': 'Pitch, notation and production-parser comparison.',
            'evidence': [{'file': 'catalog/calibration-evidence/calibrated-0123456789abcdef.json', 'sha256': 'd' * 64}],
            'validation': {
                'production_parser': 'passed',
                'reviewed_note_count': 84,
                'maximum_parser_boundary_error_seconds': 0.0001,
            },
            'verification': {'rhythm': 'reviewed', 'app_import': 'not-tested', 'log_playback': 'not-tested', 'game': 'not-tested'},
        }
        self.assertEqual(check_repo.validate_acquisition(reviewed, 3), 'external-import')
        without_midi = json.loads(json.dumps(reviewed))
        without_midi['reference'].pop('midi_sha256')
        self.assertEqual(check_repo.validate_acquisition(without_midi, 3), 'external-import')

        invalid = []
        row = json.loads(json.dumps(reviewed)); row.pop('verified_sha256'); invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row.pop('source_fingerprint_sha256'); invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['reference'].pop('score_sha256'); invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['reference']['score_sha256'] = 'invalid'; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['reference']['midi_sha256'] = 'invalid'; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['reference']['audio_sha256'] = 'c' * 64; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['reference']['start_seconds'] = 7.5; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['reference']['segment_start_seconds'] = 7.5; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['reference']['type'] = 'audio'; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row.pop('review_note'); invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row.pop('review_method'); invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['evidence'][0]['file'] = 'local/private-review.json'; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['evidence'][0]['sha256'] = 'invalid'; invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['validation'].pop('reviewed_note_count'); invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['validation'].pop('maximum_parser_boundary_error_seconds'); invalid.append(row)
        row = json.loads(json.dumps(reviewed)); row['verification']['game'] = 'passed'; invalid.append(row)
        for row in invalid:
            with self.subTest(row=row), self.assertRaises(AssertionError):
                check_repo.validate_acquisition(row, 3)

    def test_legacy_recording_reference_contract_remains_supported(self):
        reviewed = {
            **self.external,
            'variant_id': 'calibrated-0123456789abcdef',
            'verified_sha256': 'a' * 64,
            'source_fingerprint_sha256': 'b' * 64,
            'reference': {
                'type': 'recording',
                'url': 'https://example.com/official-recording',
                'edition': 'Official performance, 2024',
                'audio_sha256': 'c' * 64,
                'start_seconds': 7.5,
            },
            'review_note': 'All 84 written notes were checked against the referenced recording.',
            'review_method': 'Pitch, onset and production-parser comparison.',
            'evidence': [{'file': 'catalog/calibration-evidence/calibrated-0123456789abcdef.json', 'sha256': 'd' * 64}],
            'validation': {
                'production_parser': 'passed',
                'reviewed_note_count': 84,
                'maximum_parser_boundary_error_seconds': 0.0001,
            },
            'verification': {'rhythm': 'reviewed', 'app_import': 'not-tested', 'log_playback': 'not-tested', 'game': 'not-tested'},
        }
        self.assertEqual(check_repo.validate_acquisition(reviewed, 3), 'external-import')
        legacy_without_type = json.loads(json.dumps(reviewed))
        legacy_without_type['reference'].pop('type')
        self.assertEqual(check_repo.validate_acquisition(legacy_without_type, 3), 'external-import')

    def test_desktop_package_check_accepts_external_catalog_metadata_without_score_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            unknown_bpm = {key: value for key, value in self.external.items() if key != 'bpm'}
            write_public_catalog(root, [unknown_bpm])
            check_desktop.validate_public_catalog(root)
            reviewed = reviewed_external_record(unknown_bpm)
            write_calibration_proof(root, reviewed)
            write_public_catalog(root, [reviewed])
            check_desktop.validate_public_catalog(root)
            publish = root / 'publish'
            publish.mkdir()
            orphan = root / 'catalog/calibration-evidence/calibrated-fedcba9876543210.json'
            orphan.write_text('{}\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'calibration evidence'):
                check_desktop.validate_payload(publish, root)
            orphan.unlink()
            reviewed['verification'] = {'log_playback': 'passed'}
            write_public_catalog(root, [reviewed])
            with self.assertRaises(ValueError):
                check_desktop.validate_public_catalog(root)

    def test_published_sheet_calibration_proof_accepts_optional_midi_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            reviewed = reviewed_external_record()
            reviewed['reference'].pop('midi_sha256')
            write_calibration_proof(root, reviewed)
            write_public_catalog(root, [reviewed])
            check_desktop.validate_public_catalog(root)

    def test_desktop_publish_entry_rejects_missing_changed_and_mismatched_proofs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            publish = root / 'publish'
            publish.mkdir()
            reviewed = reviewed_external_record()
            proof_path = write_calibration_proof(root, reviewed)
            write_public_catalog(root, [reviewed])

            proof_path.unlink()
            with self.assertRaisesRegex(ValueError, 'calibration evidence'):
                check_desktop.validate_payload(publish, root)

            proof_path = write_calibration_proof(root, reviewed)
            write_public_catalog(root, [reviewed])
            proof_path.write_bytes(proof_path.read_bytes() + b' ')
            with self.assertRaisesRegex(ValueError, 'calibration evidence'):
                check_desktop.validate_payload(publish, root)

            proof_path = write_calibration_proof(root, reviewed, {'variant_id': 'calibrated-fedcba9876543210'})
            write_public_catalog(root, [reviewed])
            with self.assertRaisesRegex(ValueError, 'calibration evidence'):
                check_desktop.validate_payload(publish, root)

            proof_path = write_calibration_proof(root, reviewed, {
                'reference': {**reviewed['reference'], 'score_sha256': 'f' * 64},
            })
            write_public_catalog(root, [reviewed])
            with self.assertRaisesRegex(ValueError, 'calibration evidence'):
                check_desktop.validate_payload(publish, root)

    def test_reference_requires_https_and_ordered_finite_segment(self):
        row = {**self.external, 'reference': {'url': 'https://example.com/reference',
                                             'segment_start_seconds': 10, 'segment_end_seconds': 20}}
        self.assertEqual(check_repo.validate_acquisition(row, 3), 'external-import')
        for reference in (
            {'url': 'http://example.com/reference'},
            {'url': 'https://user@example.com/reference'},
            {'segment_start_seconds': 20, 'segment_end_seconds': 10},
        ):
            with self.subTest(reference=reference), self.assertRaises(AssertionError):
                check_repo.validate_acquisition({**self.external, 'reference': reference}, 3)

    def test_direct_download_keeps_required_sha_and_pinned_github_urls(self):
        direct = {
            'id': 'direct-song', 'title': 'Direct Song', 'variant': 'Original',
            'filename': 'song.txt', 'size': 42, 'source_commit': 'a' * 40,
            'source_repository': 'example/scores', 'source_path': 'scores/song.txt',
            'source_url': 'https://github.com/example/scores/blob/' + 'a' * 40 + '/scores/song.txt',
            'download_url': 'https://raw.githubusercontent.com/example/scores/' + 'a' * 40 + '/scores/song.txt',
            'local_path': 'local/scores/song.txt', 'git_blob_sha1': 'b' * 40, 'sha256': 'c' * 64,
        }
        self.assertEqual(check_repo.validate_acquisition(direct, 2), 'direct-download')
        missing_hash = dict(direct)
        del missing_hash['sha256']
        with self.assertRaises((AssertionError, KeyError)):
            check_repo.validate_acquisition(missing_hash, 3)

    def test_schema3_variants_group_by_stable_work_and_variant_ids(self):
        songs = [
            {'id': 'old', 'title': 'Same Work', 'work_id': 'same-work', 'variant_id': 'old'},
            {'id': 'new', 'title': 'Same Work', 'work_id': 'same-work', 'variant_id': 'new', 'is_default_variant': True},
        ]
        self.assertEqual(check_repo.validate_catalog_variants(songs, 3), [('same-work', 'old'), ('same-work', 'new')])
        duplicate = [*songs, {'id': 'copy', 'title': 'Another label', 'work_id': 'same-work', 'variant_id': 'new'}]
        with self.assertRaises(AssertionError):
            check_repo.validate_catalog_variants(duplicate, 3)
        two_defaults = [songs[0], {**songs[1], 'is_default_variant': True},
                        {'id': 'third', 'title': 'Same Work', 'work_id': 'same-work', 'variant_id': 'third', 'is_default_variant': True}]
        with self.assertRaises(AssertionError):
            check_repo.validate_catalog_variants(two_defaults, 3)

    def test_missing_required_file_stops_packaging(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                package.build_package(Path(directory))
            self.assertFalse((Path(directory) / 'artifacts').exists())


if __name__ == '__main__':
    unittest.main()
