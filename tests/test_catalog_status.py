import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from io import StringIO


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("catalog_status", ROOT / "scripts/catalog_status.py")
catalog_status = importlib.util.module_from_spec(spec)
spec.loader.exec_module(catalog_status)


class CatalogStatusTests(unittest.TestCase):
    def test_counts_null_and_missing_bpm_and_builds_work_level_queue(self):
        catalog = {
            "schema_version": 3,
            "songs": [
                {"id": "reviewed-a", "variant_id": "reviewed-a", "work_id": "work-a", "title": "Reviewed", "bpm": 80,
                 "verification": {"rhythm": "reviewed"}, "score_text": "private score body"},
                {"id": "reviewed-b", "variant_id": "reviewed-b", "work_id": "work-a", "title": "Reviewed alt", "bpm": 82,
                 "verification": {"rhythm": "not-reviewed"}},
                {"id": "ref", "variant_id": "ref", "work_id": "work-b", "title": "Reference",
                 "verification": {"rhythm": "source-timing-verified"}},
                {"id": "null", "variant_id": "null", "work_id": "work-c", "title": "Deferred", "bpm": None,
                 "verification": {"rhythm": "deferred"}},
                {"id": "missing", "variant_id": "missing", "work_id": "work-c", "title": "Deferred alt",
                 "verification": {"rhythm": "not-reviewed"}},
            ],
        }
        summary = catalog_status.summarize_catalog(catalog, "a" * 64)

        self.assertEqual(summary["counts"], {
            "versions": 5,
            "works": 3,
            "reviewed_excerpt_versions": 1,
            "works_with_reviewed_excerpt": 1,
            "reference_versions": 3,
            "not_reviewed_versions": 2,
            "source_timing_verified_versions": 1,
            "deferred_versions": 1,
            "works_without_reviewed_excerpt": 2,
        })
        self.assertEqual(summary["bpm"], {
            "known_versions": 2,
            "unknown_versions": 3,
            "unknown_field_missing": 2,
            "unknown_explicit_null": 1,
        })
        queue = summary["calibration_queue"]["items"]
        self.assertEqual({item["work_id"] for item in queue}, {"work-b", "work-c"})
        deferred = next(item for item in queue if item["work_id"] == "work-c")
        self.assertEqual(deferred["deferred_variant_ids"], ["null"])
        serialized = catalog_status.render_json(summary)
        self.assertNotIn(b"private score body", serialized)
        self.assertNotIn(b"score_text", serialized)

    def test_unknown_rhythm_state_fails_instead_of_silently_downgrading(self):
        catalog = {
            "schema_version": 3,
            "songs": [{"id": "x", "work_id": "w", "verification": {"rhythm": "guessed"}}],
        }
        with self.assertRaisesRegex(ValueError, "unknown rhythm status"):
            catalog_status.summarize_catalog(catalog, "b" * 64)

    def test_invalid_known_bpm_values_fail_closed(self):
        for bpm in (0, 19, 301, 92.5, "92", True, float("nan")):
            with self.subTest(bpm=bpm):
                catalog = {
                    "schema_version": 3,
                    "songs": [{"id": "x", "work_id": "w", "bpm": bpm,
                               "verification": {"rhythm": "not-reviewed"}}],
                }
                with self.assertRaisesRegex(ValueError, "invalid BPM"):
                    catalog_status.summarize_catalog(catalog, "c" * 64)

    def test_generated_outputs_are_deterministic_and_staleness_is_detected(self):
        catalog = {
            "schema_version": 3,
            "updated_at": "2026-09-28",
            "songs": [{"id": "x", "variant_id": "x", "work_id": "w", "title": "Song", "bpm": 90,
                       "verification": {"rhythm": "not-reviewed"}}],
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "songs.json"
            source.write_text(json.dumps(catalog), encoding="utf-8")
            outputs = {root / "status.json": "json", root / "status.md": "status", root / "queue.md": "queue"}
            first = catalog_status.build_outputs(source, outputs)
            second = catalog_status.build_outputs(source, outputs)
            self.assertEqual(first, second)
            with redirect_stdout(StringIO()):
                self.assertTrue(catalog_status.write_or_check(first, check=False))
                self.assertTrue(catalog_status.write_or_check(first, check=True))
            (root / "queue.md").write_text("stale", encoding="utf-8")
            with redirect_stderr(StringIO()):
                self.assertFalse(catalog_status.write_or_check(first, check=True))


if __name__ == "__main__":
    unittest.main()
