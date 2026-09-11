import json
from pathlib import Path
import runpy
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
WORKSHOP_UPDATE = runpy.run_path(str(ROOT / "scripts/package-release.py"))["workshop_update"]


class WorkshopUpdateTests(unittest.TestCase):
    def setUp(self):
        self.manifest = json.loads((ROOT / "TheArchitect.json").read_text(encoding="utf-8-sig"))
        self.config = json.loads((ROOT / "release/workshop.json").read_text())

    def load_config(self, config, item_id="3799307965"):
        with patch.object(Path, "read_text", side_effect=[item_id, json.dumps(config)]):
            return WORKSHOP_UPDATE(self.manifest)

    def test_release_targets_existing_item_without_steam_managed_fields(self):
        item_id, config = WORKSHOP_UPDATE(self.manifest)
        self.assertEqual(item_id, "3799307965")
        self.assertEqual(config["title"], self.manifest["name"])
        self.assertNotIn("description", config)
        self.assertNotIn("visibility", config)

    def test_rejects_description_overwrites_including_empty_strings(self):
        for description in ("Old local description", ""):
            with self.subTest(description=description), self.assertRaisesRegex(ValueError, "description"):
                self.load_config({**self.config, "description": description})

    def test_rejects_visibility_overwrites(self):
        for visibility in ("private", "public", "friends_only", ""):
            with self.subTest(visibility=visibility), self.assertRaisesRegex(ValueError, "visibility"):
                self.load_config({**self.config, "visibility": visibility})

    def test_null_fields_retain_official_uploader_skip_semantics(self):
        item_id, config = self.load_config({**self.config, "description": None, "visibility": None})
        self.assertEqual(item_id, "3799307965")
        self.assertIsNone(config["description"])
        self.assertIsNone(config["visibility"])

    def test_rejects_missing_or_invalid_item_ids(self):
        for item_id in ("", "0", "-1", "not-an-id", str(2**64)):
            with self.subTest(item_id=item_id), self.assertRaisesRegex(ValueError, "existing Workshop item ID"):
                self.load_config(self.config, item_id)
        with patch.object(Path, "read_text", side_effect=FileNotFoundError), self.assertRaises(FileNotFoundError):
            WORKSHOP_UPDATE(self.manifest)

    def test_rejects_mismatched_titles(self):
        with self.assertRaisesRegex(ValueError, "title"):
            self.load_config({**self.config, "title": "The Architect"})


if __name__ == "__main__":
    unittest.main()
