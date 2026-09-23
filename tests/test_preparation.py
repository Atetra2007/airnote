"""Exercise the real downloader with local fixtures; no network or player launch."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
POWERSHELL = shutil.which('powershell.exe')


@unittest.skipUnless(POWERSHELL, 'Windows PowerShell is required for the preparation script')
class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / '中文 path'
        (self.root / 'scripts').mkdir(parents=True)
        shutil.copyfile(ROOT / 'scripts/Prepare.ps1', self.root / 'scripts/Prepare.ps1')
        (self.root / 'catalog').mkdir()
        (self.root / 'local/scores').mkdir(parents=True)
        (self.root / 'local/downloads').mkdir()
        self.score = self.root / 'local/scores/test.txt'
        self.score.write_bytes(b'1 0 2_ 3_\n')
        self.songs = {'songs': [{'id': 'sample', 'filename': 'test.txt', 'size': self.score.stat().st_size,
                                'sha256': hashlib.sha256(self.score.read_bytes()).hexdigest(),
                                'git_blob_sha1': '', 'download_url': 'https://raw.githubusercontent.com/example/example/fixed/test.txt'}]}
        self.archive = self.root / 'local/downloads/player.zip'
        self.write_archive('dummy.txt', b'NOT AN EXECUTABLE')
        self.write_metadata()

    def write_archive(self, name, content):
        with zipfile.ZipFile(self.archive, 'w') as zipped:
            zipped.writestr(name, content)
        self.lock = {'tag': 'v-test', 'asset': {'name': 'player.zip', 'size': self.archive.stat().st_size,
                    'sha256': hashlib.sha256(self.archive.read_bytes()).hexdigest(),
                    'url': 'https://github.com/example/example/releases/download/v-test/player.zip'}}

    def write_metadata(self):
        (self.root / 'catalog/upstream.lock.json').write_text(json.dumps(self.lock), encoding='utf-8')
        (self.root / 'catalog/songs.json').write_text(json.dumps(self.songs), encoding='utf-8')

    def run_script(self, *args):
        return subprocess.run([POWERSHELL, '-NoLogo', '-NoProfile', '-File', str(self.root / 'scripts/Prepare.ps1'), *args],
                              capture_output=True, timeout=30, cwd=self.root)

    def test_cached_files_extract_in_chinese_path_and_repeat_verifies(self):
        first = self.run_script()
        self.assertEqual(first.returncode, 0, first.stdout.decode(errors='replace'))
        self.assertEqual((self.root / 'local/player/v-test/dummy.txt').read_bytes(), b'NOT AN EXECUTABLE')
        repeat = self.run_script('-VerifyOnly')
        self.assertEqual(repeat.returncode, 0, repeat.stdout.decode(errors='replace'))

    def test_corrupt_existing_score_is_not_overwritten(self):
        modified = b'X' * self.score.stat().st_size
        self.score.write_bytes(modified)
        result = self.run_script('-Only', 'Scores', '-VerifyOnly')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(b'SHA256 mismatch', result.stdout)
        self.assertEqual(self.score.read_bytes(), modified)

    def test_invalid_selection_fails_before_any_download(self):
        result = self.run_script('-SongId', 'missing')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(b'Unknown song id', result.stdout)
        self.assertFalse((self.root / 'local/player').exists())

    def test_traversing_archive_is_rejected_before_extraction(self):
        self.write_archive('../escape.txt', b'UNSAFE')
        self.write_metadata()
        result = self.run_script('-Only', 'Player')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(b'Unsafe archive entry', result.stdout)
        self.assertFalse((self.root / 'local/player/escape.txt').exists())

    def test_modified_extracted_file_is_not_replaced(self):
        self.assertEqual(self.run_script().returncode, 0)
        target = self.root / 'local/player/v-test/dummy.txt'
        target.write_bytes(b'CHANGED')
        result = self.run_script('-Only', 'Player', '-VerifyOnly')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(target.read_bytes(), b'CHANGED')

    def test_verify_only_never_downloads_a_missing_score(self):
        self.score.unlink()
        result = self.run_script('-Only', 'Scores', '-VerifyOnly')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(b'Missing file', result.stdout)
        self.assertFalse(self.score.exists())


if __name__ == '__main__':
    unittest.main()
