"""Keep the shipped library complete, byte-exact and inside its reviewed scope."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import check_desktop
import check_repo
import prepare_bundled_scores as bundled


class BundledScoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.identity = 'calibrated-0123456789abcdef'
        self.content = '测试曲谱\r\n1 2 3\r\n'.encode('utf-8')
        self.song = {
            'id': self.identity, 'title': '测试曲谱', 'work_id': 'work-test',
            'verified_sha256': hashlib.sha256(self.content).hexdigest(),
            'source_url': 'https://example.com/source', 'redistribution': 'permission-unconfirmed',
            'verification': {'rhythm': 'reviewed'},
        }
        self.catalog = {'schema_version': 3, 'songs': [self.song]}
        self.write_json('catalog/songs.json', self.catalog)
        self.source = self.root / 'local/accepted.txt'
        self.source.parent.mkdir(parents=True)
        self.source.write_bytes(self.content)
        self.ledger = {'accepted': [{'catalog_variant_id': self.identity,
                                   'file': 'local/accepted.txt', 'sha256': self.song['verified_sha256']}]}
        self.write_json(bundled.LEDGERS[0][0], self.ledger)

    def write_json(self, name, value):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

    def test_prepare_preserves_original_bytes_and_works_without_local_research(self):
        manifest = bundled.prepare_bundle(self.root)
        self.assertEqual(manifest['total_bytes'], len(self.content))
        target = self.root / bundled.BUNDLE / (self.identity + '.txt')
        self.assertEqual(target.read_bytes(), self.content)
        original_manifest = (self.root / bundled.MANIFEST).read_bytes()
        # CI checks and repeat builds cannot depend on ignored local evidence.
        shutil.rmtree(self.root / 'local')
        self.assertEqual(bundled.validate_bundle(self.root), manifest)
        self.assertEqual(bundled.prepare_bundle(self.root), manifest)
        self.assertEqual((self.root / bundled.MANIFEST).read_bytes(), original_manifest)
        sources = check_desktop.bundled_score_sources(self.root)
        self.assertEqual(set(sources), {'Resources/BundledScores/manifest.json',
                                       f'Resources/BundledScores/{self.identity}.txt'})
        self.assertEqual(manifest['songs'][0]['redistribution'], 'permission-unconfirmed')
        self.assertNotIn('local/', original_manifest.decode('utf-8'))

    def test_wrong_accepted_hash_fails_before_any_bundle_is_created(self):
        self.source.write_bytes(b'changed source')
        with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'):
            bundled.prepare_bundle(self.root)
        self.assertFalse((self.root / bundled.BUNDLE).exists())

    def test_modified_or_missing_bundled_bytes_are_rejected(self):
        bundled.prepare_bundle(self.root)
        target = self.root / bundled.BUNDLE / (self.identity + '.txt')
        target.write_bytes(self.content.replace(b'\r\n', b'\n'))
        with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'):
            bundled.validate_bundle(self.root)
        target.unlink()
        with self.assertRaisesRegex(ValueError, 'score is missing'):
            bundled.validate_bundle(self.root)

    def test_unlisted_files_and_directories_are_rejected(self):
        bundled.prepare_bundle(self.root)
        unexpected = self.root / bundled.BUNDLE / 'personal.txt'
        unexpected.write_text('private', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'Unlisted file'):
            bundled.validate_bundle(self.root)
        with self.assertRaisesRegex(ValueError, 'Unexpected existing bundle file'):
            bundled.prepare_bundle(self.root)
        unexpected.unlink()
        (self.root / bundled.BUNDLE / 'research').mkdir()
        with self.assertRaisesRegex(ValueError, 'Unlisted file'):
            bundled.validate_bundle(self.root)

    def test_catalog_coverage_is_exact_and_unreviewed_entries_cannot_be_bundled(self):
        manifest = bundled.prepare_bundle(self.root)
        manifest['songs'] = []
        self.write_json(bundled.MANIFEST, manifest)
        with self.assertRaisesRegex(ValueError, 'Reviewed scores missing'):
            bundled.validate_bundle(self.root)
        bundled.prepare_bundle(self.root)
        self.catalog['songs'][0]['verification']['rhythm'] = 'not-reviewed'
        self.write_json('catalog/songs.json', self.catalog)
        with self.assertRaisesRegex(ValueError, 'Unknown, unreviewed or duplicate'):
            bundled.validate_bundle(self.root)

    def test_manifest_cannot_change_source_rights_hash_identity_or_path(self):
        pristine = bundled.prepare_bundle(self.root)
        for field, value in (
            ('source_url', 'https://example.com/other'), ('redistribution', 'MIT'),
            ('sha256', '0' * 64), ('file', '../private.txt'),
            ('catalog_id', 'calibrated-0000000000000000'),
        ):
            with self.subTest(field=field):
                manifest = json.loads(json.dumps(pristine))
                manifest['songs'][0][field] = value
                self.write_json(bundled.MANIFEST, manifest)
                with self.assertRaises(ValueError):
                    bundled.validate_bundle(self.root)
        pristine['songs'].append(pristine['songs'][0])
        self.write_json(bundled.MANIFEST, pristine)
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            bundled.validate_bundle(self.root)

    def test_repository_allowlist_requires_each_validated_bundle_file(self):
        bundled.prepare_bundle(self.root)
        files = bundled.bundle_repository_files(self.root)
        required = set(check_repo.PACKAGE_FILES) | check_repo.REPO_ONLY_FILES
        check_repo.validate_repository_index(required | files, set(), files)
        with self.assertRaisesRegex(AssertionError, 'Required files not staged'):
            check_repo.validate_repository_index(required, set(), files)
        with self.assertRaisesRegex(AssertionError, 'Unreviewed files in Git index'):
            check_repo.validate_repository_index(required | files | {'scores/bundled/unreviewed.txt'}, set(), files)

    def test_checked_in_bundle_and_git_attributes_preserve_verified_bytes(self):
        manifest = bundled.validate_bundle()
        self.assertGreater(manifest['total_scores'], 0)
        path = bundled.BUNDLE + '/' + manifest['songs'][0]['file']
        result = subprocess.run(['git', 'check-attr', 'text', '--', path], cwd=bundled.ROOT,
                                capture_output=True, text=True, encoding='utf-8', check=True)
        self.assertIn(': text: unset', result.stdout)


if __name__ == '__main__':
    unittest.main()
