import hashlib
import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('kit_package', ROOT / 'scripts/package.py')
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


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

    def test_missing_required_file_stops_packaging(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                package.build_package(Path(directory))
            self.assertFalse((Path(directory) / 'artifacts').exists())


if __name__ == '__main__':
    unittest.main()
