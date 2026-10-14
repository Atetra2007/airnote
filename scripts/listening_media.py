"""Normalize hash-pinned static score images shared by listening tools."""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any


IMAGE_MEDIA_TYPES = {
    ".svg": "image/svg+xml",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif": "image/gif",
}


def score_image_references(reference: dict[str, Any]) -> list[dict[str, Any]]:
    """Return each static score page once, in page order when page numbers exist.

    ``additional_score_images`` is the canonical complete page list when present.
    Its primary image often also appears as ``score_image_path`` for compatibility;
    the duplicate is merged so a page is copied and shown only once.
    """
    primary_key = next((key for key in ("score_svg_path", "score_image_path") if key in reference), None)
    additional = reference.get("additional_score_images", [])
    if not isinstance(additional, list):
        raise ValueError("additional_score_images must be an array")
    if primary_key is None:
        if additional:
            raise ValueError("additional_score_images requires a primary score image")
        return []

    primary_hash_key = "score_svg_sha256" if primary_key == "score_svg_path" else "score_image_sha256"
    primary_type_key = "score_svg_media_type" if primary_key == "score_svg_path" else "score_image_media_type"
    rows: list[dict[str, Any]] = []
    by_path: dict[str, dict[str, Any]] = {}

    def add(path_value: Any, hash_value: Any, media_value: Any = None, page_value: Any = None, *, primary: bool = False) -> None:
        if not isinstance(path_value, str) or not path_value.strip():
            raise ValueError("Score image file path must be a non-empty string")
        path = Path(path_value).as_posix()
        media = IMAGE_MEDIA_TYPES.get(Path(path).suffix.lower())
        if media is None:
            raise ValueError(f"Unsupported score image extension: {path}")
        if media_value not in (None, media):
            raise ValueError(f"Score image media type does not match its extension: {path}")
        if not isinstance(hash_value, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", hash_value):
            raise ValueError(f"Score image SHA-256 is invalid: {path}")
        if page_value is not None and (type(page_value) is not int or page_value < 1):
            raise ValueError(f"Score image page must be a positive integer: {path}")
        row = by_path.get(path)
        if row is not None:
            if row["sha256"] != hash_value.lower() or row["media_type"] != media:
                raise ValueError(f"Duplicate score image path has conflicting hash or media type: {path}")
            if page_value is not None:
                if row["page"] not in (None, page_value):
                    raise ValueError(f"Duplicate score image path has conflicting page numbers: {path}")
                row["page"] = page_value
            row["is_primary"] = row["is_primary"] or primary
            return
        row = {
            "path": path,
            "sha256": hash_value.lower(),
            "media_type": media,
            "page": page_value,
            "is_primary": primary,
        }
        by_path[path] = row
        rows.append(row)

    add(reference[primary_key], reference.get(primary_hash_key), reference.get(primary_type_key), primary=True)
    if "secondary_score_image_path" in reference:
        add(
            reference["secondary_score_image_path"],
            reference.get("secondary_score_image_sha256"),
            reference.get("secondary_score_image_media_type"),
        )
    for image in additional:
        if not isinstance(image, dict):
            raise ValueError("Each additional_score_images entry must be an object")
        add(image.get("file", image.get("path")), image.get("sha256"), image.get("media_type"), image.get("page"))

    pages: dict[int, str] = {}
    for row in rows:
        page = row["page"]
        if page is not None:
            previous = pages.setdefault(page, row["path"])
            if previous != row["path"]:
                raise ValueError(f"Multiple score image files claim page {page}")

    if rows and all(row["page"] is not None for row in rows):
        rows.sort(key=lambda row: row["page"])
    else:
        rows.sort(key=lambda row: (not row["is_primary"], row["page"] if row["page"] is not None else 0))
    return rows
