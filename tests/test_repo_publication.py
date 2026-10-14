"""Check that stage publication never widens the allowlist to local materials."""
import sys
import json
import re
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
from urllib.parse import unquote, urlsplit
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
import check_repo
import package_desktop_preview


class RepositoryPublicationTests(unittest.TestCase):
    def setUp(self):
        self.required = set(check_repo.PACKAGE_FILES) | check_repo.REPO_ONLY_FILES

    def test_stage_metadata_and_release_documents_are_required(self):
        expected = {
            'catalog/calibration-stage-20261014.json',
            'docs/RELEASE-0.4.2-NOTES.md',
            'docs/RELEASE-0.4.2-STATUS.md',
            'scripts/listening_media.py',
            'tests/test_listening_media.py',
        }
        self.assertTrue(expected <= check_repo.REPO_ONLY_FILES)
        check_repo.validate_repository_index(self.required, set())
        with self.assertRaisesRegex(AssertionError, 'Required files not staged'):
            check_repo.validate_repository_index(self.required - expected, set())

    def test_only_validated_evidence_files_can_enter_the_index(self):
        proof = 'catalog/calibration-evidence/calibrated-0123456789abcdef.json'
        check_repo.validate_repository_index(self.required | {proof}, {proof})
        with self.assertRaisesRegex(AssertionError, 'Unreviewed files in Git index'):
            check_repo.validate_repository_index(self.required | {proof}, set())
        with self.assertRaisesRegex(AssertionError, 'Required files not staged'):
            check_repo.validate_repository_index(self.required, {proof})

    def test_local_scores_media_and_private_test_dependencies_are_not_allowed(self):
        for name in (
            'local/calibration-221/accepted_timeline.json',
            'local/calibration-221/listening_media.py',
            'local/scores/third-party.txt',
            'catalog/calibration-evidence/recording.wav',
            'catalog/calibration-evidence/score.png',
            'catalog/raw-score.txt',
        ):
            with self.subTest(name=name), self.assertRaisesRegex(AssertionError, 'Unreviewed files in Git index'):
                check_repo.validate_repository_index(self.required | {name}, set())


class StageDesktopZipTests(unittest.TestCase):
    def test_stage_documents_are_packaged_with_working_local_links(self):
        # A metadata-only fixture exercises ZIP assembly without a desktop build.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / 'manifest.json'
            manifest.write_text(json.dumps({'version': '0.4.2', 'files': []}), encoding='utf-8')
            output = root / 'release.zip'
            with patch.object(package_desktop_preview, 'validate_payload', return_value=[]):
                package_desktop_preview.package(root, manifest, output)
            with ZipFile(output) as archive:
                names = set(archive.namelist())
                self.assertEqual(names, {
                    '使用说明.md', '验证记录.md', '游戏验证.md', '演奏节奏修复.md',
                    '本版更新说明.md', '发行验证记录.md', '曲库与校准状态.md',
                    '现行队列.md', '校对阶段处置.json', '开始使用.txt', 'publish-manifest.json',
                })
                stage = json.loads(archive.read('校对阶段处置.json'))
                self.assertEqual(stage['accepted_arrangements'], 340)
                for name in ('本版更新说明.md', '发行验证记录.md', '曲库与校准状态.md', '现行队列.md'):
                    for target in re.findall(r'\[[^\]]+\]\(([^)]+)\)', archive.read(name).decode('utf-8')):
                        parts = urlsplit(target)
                        if parts.scheme:
                            self.assertEqual(parts.scheme, 'https')
                        elif parts.path:
                            self.assertIn(unquote(parts.path), names, f'{name} has a broken local link: {target}')


if __name__ == '__main__':
    unittest.main()
