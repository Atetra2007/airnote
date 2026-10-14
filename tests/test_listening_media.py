from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
from listening_media import score_image_references


def five_page_reference() -> dict[str, object]:
    hashes = {page: f"{page:x}" * 64 for page in range(1, 6)}
    pages = [
        {"file": f"score-{page}.png", "sha256": hashes[page], "media_type": "image/png", "page": page}
        for page in (5, 2, 1, 4, 3)
    ]
    return {
        "score_image_path": "score-2.png",
        "score_image_sha256": hashes[2],
        "score_image_media_type": "image/png",
        "additional_score_images": pages,
    }


class ScoreImageReferenceTests(unittest.TestCase):
    def test_additional_pages_are_ordered_and_primary_duplicate_is_merged(self) -> None:
        images = score_image_references(five_page_reference())

        self.assertEqual([image["page"] for image in images], [1, 2, 3, 4, 5])
        self.assertEqual(len(images), 5)
        self.assertTrue(images[1]["is_primary"])
        self.assertEqual(sum(image["is_primary"] for image in images), 1)

    def test_conflicting_duplicate_path_is_rejected(self) -> None:
        reference = five_page_reference()
        reference["additional_score_images"].append({
            "file": "score-2.png", "sha256": "f" * 64, "media_type": "image/png", "page": 2,
        })

        with self.assertRaisesRegex(ValueError, "conflicting hash or media type"):
            score_image_references(reference)

    def test_duplicate_page_claims_are_rejected(self) -> None:
        reference = five_page_reference()
        reference["additional_score_images"].append({
            "file": "different-page-2.png", "sha256": "a" * 64, "media_type": "image/png", "page": 2,
        })

        with self.assertRaisesRegex(ValueError, "Multiple score image files claim page 2"):
            score_image_references(reference)

    def test_declared_mime_type_must_match_extension(self) -> None:
        reference = five_page_reference()
        reference["additional_score_images"][0]["media_type"] = "image/jpeg"

        with self.assertRaisesRegex(ValueError, "does not match its extension"):
            score_image_references(reference)

    def test_legacy_path_entries_infer_mime_and_preserve_reference_order(self) -> None:
        reference = {
            "score_image_path": "primary.png",
            "score_image_sha256": "a" * 64,
            "additional_score_images": [
                {"path": "detail-1.png", "sha256": "b" * 64},
                {"path": "detail-2.png", "sha256": "c" * 64},
            ],
        }

        images = score_image_references(reference)

        self.assertEqual([image["path"] for image in images], ["primary.png", "detail-1.png", "detail-2.png"])
        self.assertEqual([image["media_type"] for image in images], ["image/png"] * 3)
        self.assertTrue(images[0]["is_primary"])


if __name__ == "__main__":
    unittest.main()
