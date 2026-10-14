import copy
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
    def stage_fixture(self):
        catalog = {
            "schema_version": 3,
            "songs": [
                {"id": "approved-a", "work_id": "same-work", "title": "Retained",
                 "version": "stage-version", "source_record_id": "accepted-source",
                 "verification": {"rhythm": "reviewed"}},
                {"id": "approved-b", "work_id": "same-work", "title": "Retained alternative",
                 "version": "stage-version", "source_record_id": "accepted-source",
                 "verification": {"rhythm": "reviewed"}},
                {"id": "outside-scope", "work_id": "outside-work", "title": "Other reference",
                 "verification": {"rhythm": "not-reviewed"}},
            ],
        }
        stage = {
            "schema_version": 1,
            "status": "completed-with-user-exclusions",
            "original_scope_works": 2,
            "original_scope_source_versions": 2,
            "accepted_source_versions": 1,
            "accepted_arrangements": 2,
            "excluded_source_versions": 1,
            "remaining_source_versions": 0,
            "accepted_catalog_version": "stage-version",
            "all_original_sources_musically_accepted": False,
            "excluded": [{"source_variant_id": "excluded-source", "work_id": "same-work",
                          "title": "Removed same-work reference", "reason": "user-excluded"}],
        }
        return catalog, stage

    def test_completed_stage_retains_other_reference_queue_and_separates_exclusions(self):
        catalog, stage = self.stage_fixture()
        summary = catalog_status.summarize_catalog(catalog, "a" * 64, stage)
        self.assertEqual(summary["counts"]["versions"], 3)
        self.assertEqual(summary["counts"]["reviewed_excerpt_versions"], 2)
        self.assertEqual(summary["calibration_queue"]["items"][0]["variant_ids"], ["outside-scope"])
        self.assertFalse(summary["calibration_stage"]["all_original_sources_musically_accepted"])
        markdown = catalog_status.render_queue_markdown(summary).decode("utf-8")
        self.assertIn("`excluded-source`", markdown)
        self.assertIn("`user-excluded`", markdown)
        self.assertIn("阶段结束不等于原范围全部来源音乐通过", markdown)
        self.assertIn("`outside-scope`", markdown)

    def test_stage_fails_if_excluded_source_is_restored_or_approved_by_mistake(self):
        catalog, stage = self.stage_fixture()
        resurrected = copy.deepcopy(catalog)
        resurrected["songs"].append({"id": "excluded-source", "work_id": "same-work",
                                    "verification": {"rhythm": "not-reviewed"}})
        with self.assertRaisesRegex(ValueError, "excluded source is still"):
            catalog_status.summarize_catalog(resurrected, "a" * 64, stage)
        catalog["songs"][0]["source_record_id"] = "excluded-source"
        with self.assertRaisesRegex(ValueError, "cannot have an accepted"):
            catalog_status.summarize_catalog(catalog, "a" * 64, stage)

    def test_stage_fails_on_incorrect_counts_duplicate_exclusions_or_unreviewed_arrangements(self):
        catalog, stage = self.stage_fixture()
        for field, value, message in (
            ("accepted_arrangements", 3, "arrangement count"),
            ("accepted_source_versions", 2, "source counts"),
            ("original_scope_source_versions", 3, "source counts"),
            ("remaining_source_versions", 1, "remaining sources"),
            ("all_original_sources_musically_accepted", True, "must not claim"),
        ):
            with self.subTest(field=field):
                changed = copy.deepcopy(stage)
                changed[field] = value
                with self.assertRaisesRegex(ValueError, message):
                    catalog_status.summarize_catalog(catalog, "a" * 64, changed)
        changed = copy.deepcopy(stage)
        changed["excluded"].append(copy.deepcopy(changed["excluded"][0]))
        changed["excluded_source_versions"] = 2
        changed["original_scope_source_versions"] = 3
        with self.assertRaisesRegex(ValueError, "duplicate calibration stage exclusion"):
            catalog_status.summarize_catalog(catalog, "a" * 64, changed)
        changed_catalog = copy.deepcopy(catalog)
        changed_catalog["songs"][1]["source_record_id"] = "unexpected-extra-source"
        with self.assertRaisesRegex(ValueError, "accepted source count"):
            catalog_status.summarize_catalog(changed_catalog, "a" * 64, stage)
        catalog["songs"][0]["verification"]["rhythm"] = "not-reviewed"
        with self.assertRaisesRegex(ValueError, "must remain reviewed"):
            catalog_status.summarize_catalog(catalog, "a" * 64, stage)

    def test_build_outputs_loads_stage_companion_and_detects_changed_stage(self):
        catalog, stage = self.stage_fixture()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "songs.json"
            companion = root / catalog_status.STAGE_FILENAME
            source.write_text(json.dumps(catalog), encoding="utf-8")
            companion.write_text(json.dumps(stage), encoding="utf-8")
            outputs = {root / "status.json": "json", root / "queue.md": "queue"}
            first = catalog_status.build_outputs(source, outputs)
            with redirect_stdout(StringIO()):
                self.assertTrue(catalog_status.write_or_check(first, check=False))
            stage["excluded"][0]["title"] = "Updated exclusion title"
            companion.write_text(json.dumps(stage), encoding="utf-8")
            changed = catalog_status.build_outputs(source, outputs)
            with redirect_stderr(StringIO()):
                self.assertFalse(catalog_status.write_or_check(changed, check=True))

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
