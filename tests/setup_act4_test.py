"""Deck composition and save safety without touching game profiles."""

import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import yaml


SPEC = importlib.util.spec_from_file_location(
    "setup_act4", Path(__file__).resolve().parents[1] / "scripts/setup-act4.py")
act4 = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(act4)


def deck():
    return {"schema": 1, "name": "Test", "character": "CHARACTER.IRONCLAD", "ascension": 5,
            "cards": [{"id": "CARD.BASH", "current_upgrade_level": 1,
                       "enchantment": {"id": "ENCHANTMENT.SOWN", "amount": 2}}],
            "relics": [{"id": "RELIC.PEN_NIB", "props": {"ints": [{"name": "Counter", "value": 4}]}}],
            "potions": [{"id": "POTION.BLOCK_POTION", "slot_index": 1}], "current_hp": 31,
            "max_hp": 80, "gold": 123, "max_energy": 3, "base_orb_slot_count": 0,
            "max_potion_slot_count": 2}


def template(count=1):
    return {"current_act_index": 3, "ascension": 5, "rng": {"seed": "fixed"},
            "players": [{"net_id": index + 1, "character_id": "CHARACTER.IRONCLAD",
                         "rng": {"native": 42}, "extra_fields": {}} for index in range(count)],
            "save_dict_String": {act4.SAVE_KEY: json.dumps({"Id": "old", "Entered": True,
                                                          "EntrySnapshot": {}, "KeepFutureField": 7})}}


MODELS = {"CHARACTER.IRONCLAD", "CARD.BASH", "ENCHANTMENT.SOWN", "RELIC.PEN_NIB", "POTION.BLOCK_POTION"}


class DeckTests(unittest.TestCase):
    def test_compose_independent_player_and_opponent_counts_without_mutating_input(self):
        for humans in range(1, 5):
            for enemies in range(1, 5):
                with self.subTest(humans=humans, enemies=enemies):
                    original, build = template(humans), deck()
                    before = copy.deepcopy((original, build))
                    result = act4.compose_save(original, [build] * humans, [build] * enemies, MODELS)
                    self.assertEqual((original, build), before)
                    self.assertEqual([p["net_id"] for p in result["players"]], list(range(1, humans + 1)))
                    for player in result["players"]:
                        self.assertEqual(player["deck"], build["cards"])
                        self.assertEqual(player["relics"], build["relics"])
                        self.assertEqual(player["potions"], build["potions"])
                        self.assertEqual(player["rng"], {"native": 42})
                        for resource in act4.RESOURCES:
                            self.assertEqual(player[resource], build[resource])
                    state = json.loads(act4.state_location(result)[act4.SAVE_KEY])
                    self.assertEqual(len(state["PlaytestOpponents"]), enemies)
                    self.assertEqual(state["KeepFutureField"], 7)
                    self.assertIsNone(state["EntrySnapshot"])
                    self.assertEqual(set(state["PlaytestOpponents"][0]),
                                     {"CharacterId", "MaxHp", "Deck", "ContentHash"})
                    self.assertEqual(result["rng"], original["rng"])

    def test_corrupted_build_ignores_uninstalled_relics_and_unknown_base_resources(self):
        build = deck()
        build["relics"][0]["id"] = "RELIC.UNINSTALLED"
        build["max_energy"] = build["base_orb_slot_count"] = None
        act4.compose_save(template(), [deck()], [build], MODELS)
        with self.assertRaises(ValueError):
            act4.compose_save(template(), [build], [deck()], MODELS)

    def test_reject_missing_cards_enchantments_relics_and_characters(self):
        for model in MODELS:
            with self.subTest(model=model), self.assertRaisesRegex(ValueError, "not installed"):
                act4.compose_save(template(), [deck()], [deck()], MODELS - {model})

    def test_invalid_decks_fail_before_any_file_changes(self):
        for field, value in (("schema", 9), ("cards", []), ("cards", ["CARD.BASH"]),
                             ("max_energy", None), ("max_energy", True), ("current_hp", 81),
                             ("base_orb_slot_count", -1), ("ascension", 11)):
            build = deck()
            build[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                act4.validate_deck(build)
        build = deck()
        build["potions"] *= 2
        with self.assertRaisesRegex(ValueError, "Potion slots"):
            act4.validate_deck(build)

    def test_yaml_rejects_duplicate_keys_and_unsafe_tags(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "deck.yml"
            for content in ("name: first\nname: second\n", "!!python/object:object {}\n"):
                path.write_text(content)
                with self.assertRaises((ValueError, yaml.YAMLError)):
                    act4.read_deck(path)

    def test_snapshot_hash_matches_native_template_with_baselib_property_name(self):
        cards = [{"floor_added_to_deck": 1, "id": name,
                  "save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]": {"BaseLibCardModifiers": []}}
                 for name in ["CARD.STRIKE_IRONCLAD"] * 5 + ["CARD.DEFEND_IRONCLAD"] * 4 +
                 ["CARD.BASH", "CARD.ASCENDERS_BANE"]]
        build = {**deck(), "cards": cards}
        self.assertEqual(act4.snapshot(build)["ContentHash"],
                         "7b79f8ddea91a777cf20c1bb4c7f28163289d1233d369d243d21991e51027731")

    def test_snapshot_hash_excludes_relics_hp_gold_and_base_resources(self):
        before = act4.snapshot(deck())
        build = deck()
        build.update(relics=[], potions=[], current_hp=1, gold=0, max_energy=99)
        self.assertEqual(before, act4.snapshot(build))
        build["cards"][0]["current_upgrade_level"] += 1
        self.assertNotEqual(before["ContentHash"], act4.snapshot(build)["ContentHash"])

    def test_template_requires_one_unambiguous_native_extension(self):
        for original in ({}, {**template(), "current_act_index": 2},
                         {**template(), "another": {act4.SAVE_KEY: "{}"}}):
            with self.assertRaises(ValueError):
                act4.compose_save(original, [deck()], [deck()], MODELS)

    def test_checked_in_decks_have_no_history_or_account_metadata(self):
        characters = set()
        for path in (act4.ROOT / "decks").glob("*.yml"):
            build = act4.read_deck(path)
            characters.add(build["character"])
            self.assertEqual(set(build), act4.FIELDS)
            self.assertNotIn("seed", build)
            self.assertNotIn("id", build)
        self.assertTrue({"CHARACTER.IRONCLAD", "CHARACTER.SILENT", "CHARACTER.REGENT",
                         "CHARACTER.NECROBINDER", "CHARACTER.DEFECT"} <= characters)

    def test_capture_filters_character_before_sampling_and_preserves_native_entries(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            build = deck()
            player = {"character": build["character"], "deck": build["cards"],
                      "relics": build["relics"], "potions": build["potions"],
                      "max_potion_slot_count": build["max_potion_slot_count"]}
            stats = {"current_hp": 31, "max_hp": 80, "current_gold": 123}
            history = {"seed": "private", "start_time": 123, "ascension": 5,
                       "players": [player], "map_point_history": [[{"player_stats": [stats]}]]}
            source = root / "victory.run"
            source.write_text(json.dumps(history))
            other = copy.deepcopy(history)
            other["players"][0]["character"] = "CHARACTER.SILENT"
            args = SimpleNamespace(data_root=root, account=None, source_slot=1, count=1,
                                   character="CHARACTER.IRONCLAD", output=root / "decks")
            artifact = root / "capture"
            artifact.mkdir()
            with patch.object(act4, "account_root", return_value=root), \
                    patch.object(act4, "victory_records", return_value=[(source, history), (source, other)]), \
                    patch.object(act4, "artifact_directory", return_value=artifact), \
                    patch.object(act4.random, "SystemRandom") as random:
                random.return_value.sample.return_value = [(source, history)]
                act4.capture(args)
                random.return_value.sample.assert_called_once_with([(source, history)], 1)
                captured = act4.read_deck(args.output / "deck-1.yml", resources=False)
                self.assertEqual(captured["character"], args.character)
                for field in ("cards", "relics", "potions"):
                    self.assertEqual(captured[field], build[field])
                self.assertIsNone(captured["max_energy"])
                self.assertIsNone(captured["base_orb_slot_count"])
                self.assertEqual(source.read_text(), (artifact / "victory-1.run").read_text())
                args.character = "CHARACTER.DEFECT"
                with self.assertRaisesRegex(ValueError, "0 distinct single-player victories for CHARACTER.DEFECT"):
                    act4.capture(args)
                random.return_value.sample.assert_called_once()


class ProfileTests(unittest.TestCase):
    def test_live_writer_backs_up_before_replacement_and_preserves_every_other_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, artifact = root / "account", root / "artifacts"
            artifact.mkdir()
            paths = ("profile1/saves/source", "modded/profile1/saves/source",
                     "modded/profile2/saves/history/win.run", "modded/profile2/prefs.save",
                     "modded/profile2/saves/current_run.save", "modded/profile2/saves/current_run.save.backup",
                     "modded/profile.save", "modded/profile.save.backup")
            for name in paths:
                path = account / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(json.dumps({"last_profile_id": 1, "schema_version": 2, "future": "keep"}))
            before = act4.hashes(account)
            prepared = act4.compose_save(template(), [deck()], [deck()] * 2, MODELS)
            with patch.object(act4, "require_closed"), patch.object(act4, "install_bundle") as install:
                with self.assertRaisesRegex(ValueError, "--replace"):
                    act4.write_profile(account, 2, artifact, root, prepared)
                self.assertEqual(act4.hashes(account), before)
                install.assert_not_called()
                target, _ = act4.write_profile(account, 2, artifact, root, prepared, replace=True)
            saved = json.loads((target / "saves/current_run.save").read_text())
            self.assertEqual(saved["platform_type"], "steam")
            self.assertEqual(saved["players"][0]["net_id"], 1)
            self.assertEqual(saved, json.loads((target / "saves/current_run.save.backup").read_text()))
            self.assertEqual(json.loads((account / "modded/profile.save").read_text()),
                             {"last_profile_id": 2, "schema_version": 2, "future": "keep"})
            for name in paths:
                if name not in ("modded/profile.save", "modded/profile2/saves/current_run.save",
                                "modded/profile2/saves/current_run.save.backup"):
                    self.assertEqual(act4.sha(account / name), before[name])
            self.assertEqual(act4.sha(artifact / "backup/profile2/saves/current_run.save"),
                             before["modded/profile2/saves/current_run.save"])

    def test_multiplayer_and_slot_one_are_never_replaced(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "modded/profile2/saves/current_run_mp.save.backup"
            path.parent.mkdir(parents=True)
            path.write_text("multiplayer")
            before = act4.hashes(root)
            with patch.object(act4, "require_closed"):
                with self.assertRaisesRegex(ValueError, "multiplayer save"):
                    act4.write_profile(root, 2, root, root, {"players": [{}]}, replace=True)
                with self.assertRaisesRegex(ValueError, "Slot 1"):
                    act4.write_profile(root, 1, root, root, {"players": [{}]}, replace=True)
            self.assertEqual(act4.hashes(root), before)

    def test_deduplicate_victories_and_exclude_abandoned_losses_and_multiplayer(self):
        with tempfile.TemporaryDirectory() as directory:
            account = Path(directory)
            source = account / "profile1/saves/history"
            modded = account / "modded/profile1/saves/history"
            source.mkdir(parents=True)
            modded.mkdir(parents=True)
            good = {"seed": "seed", "start_time": 123, "win": True, "was_abandoned": False, "players": [{}]}
            (source / "good.run").write_text(json.dumps(good))
            (modded / "copy.run").write_text(json.dumps(good))
            for index, change in enumerate(({"win": False}, {"was_abandoned": True}, {"players": [{}, {}]})):
                (source / f"bad-{index}.run").write_text(json.dumps({**good, **change}))
            before = act4.hashes(account)
            self.assertEqual(len(act4.victory_records(account, 1)), 1)
            self.assertEqual(act4.hashes(account), before)

    def test_account_discovery_does_not_guess(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ("1", "2"):
                (root / "steam" / name).mkdir(parents=True)
            with self.assertRaisesRegex(ValueError, "one Steam account"):
                act4.account_root(root)
            self.assertEqual(act4.account_root(root, "2"), root / "steam/2")

    def test_complete_target_and_selection_backups(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, artifact = root / "account", root / "artifacts"
            artifact.mkdir()
            for name in ("profile2/saves/history/victory.run", "profile2/saves/current_run.save",
                         "profile2/prefs.save", "profile.save", "profile.save.backup"):
                path = account / "modded" / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(name)
            before = act4.hashes(account)
            act4.backup_target(account, 2, artifact)
            for name, digest in before.items():
                self.assertEqual(act4.sha(artifact / "backup" / Path(name).relative_to("modded")), digest)
            self.assertEqual(act4.hashes(account), before)

    def test_running_game_requires_explicit_permission(self):
        with patch.object(act4, "game_processes", return_value=[123]), patch.object(act4.os, "kill") as kill:
            with self.assertRaisesRegex(ValueError, "Close the game first"):
                act4.require_closed(Path("/game"))
            kill.assert_not_called()

    def test_receipt_checks_full_requested_shape_and_not_just_startup(self):
        build = deck()
        receipt = {"Saved": False, "Profile": None, "Seed": "seed", "Ascension": 5,
                   "Visible": True, "Location": "CorruptedPlayer",
                   "Players": [{"Character": build["character"], "Cards": 1, "Relics": 1, "Hp": 31,
                                "MaxHp": 80, "Gold": 123, "MaxEnergy": 3, "BaseOrbSlotCount": 0}],
                   "Opponents": [{"Character": build["character"], "Cards": 1, "MaxHp": 80,
                                  "ContentHash": act4.snapshot(build)["ContentHash"]}],
                   "Offers": ["a", "b", "c"]}
        act4.verify_receipt(receipt, [build], [build], "seed", 5, saved=False)
        for change in ({"Saved": True}, {"Seed": "rerolled"}, {"Players": []}, {"Offers": []},
                       {"Visible": False}, {"Location": "MainMenu"}):
            with self.assertRaisesRegex(ValueError, "does not match"):
                act4.verify_receipt({**receipt, **change}, [build], [build], "seed", 5, saved=False)

    def test_linked_target_cannot_alias_source_slot(self):
        with tempfile.TemporaryDirectory() as directory:
            account = Path(directory)
            source = account / "modded/profile1"
            source.mkdir(parents=True)
            (account / "modded/profile2").symlink_to(source, target_is_directory=True)
            with patch.object(act4, "require_closed"), self.assertRaisesRegex(ValueError, "linked target"):
                act4.write_profile(account, 2, account, account, {"players": [{}]}, replace=True)


class StartupTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(dir=act4.ROOT)
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.game = self.root / "game"
        self.mods = self.root / "staged"
        self.account = self.root / "data/steam/123"
        self.artifact = self.root / "load-test"
        self.artifact.mkdir()
        self.game.mkdir()
        (self.game / "SlayTheSpire2").write_text("game")
        (self.game / "SlayTheSpire2").chmod(0o755)
        for base in (self.mods, self.game / "mods"):
            for name in ("BaseLib", "TheArchitect"):
                folder = base / name
                folder.mkdir(parents=True)
                for suffix in ("dll", "pdb", "json", "pck"):
                    (folder / f"{name}.{suffix}").write_text(f"{name}-{suffix}")
        act4.write_json(self.account / "modded/profile.save", {"last_profile_id": 2})
        self.env = patch.dict(act4.os.environ, {"ARCHITECT_MODS_INPUT": str(self.mods)})
        self.env.start()
        self.addCleanup(self.env.stop)
        self.processes = patch.object(act4, "game_processes", return_value=[])
        self.processes.start()
        self.addCleanup(self.processes.stop)
        self.prepared = act4.compose_save(template(), [deck()], [deck()], MODELS)

    def test_freezing_live_bundle_omits_caches_and_keeps_mod_assets(self):
        cache = self.mods / ".home/.nuget"
        cache.mkdir(parents=True)
        (cache / "package.json").write_text("SDK cache")
        assets = self.mods / "TheArchitect/images"
        assets.mkdir()
        (assets / "card.png").write_text("mod asset")
        act4.freeze_bundle(self.game, self.artifact, live=True)
        frozen = self.artifact / "mods"
        self.assertEqual({path.name for path in frozen.iterdir()}, {"TheArchitect", "BaseLib"})
        self.assertEqual((frozen / "TheArchitect/images/card.png").read_text(), "mod asset")
        self.assertFalse(any(name.startswith(".home/") for name in act4.hashes(frozen)))

    def prepare(self, ready=False):
        act4.freeze_bundle(self.game, self.artifact, live=True)
        act4.write_json(self.artifact / "verified.save", self.prepared)
        act4.write_json(self.artifact / "native-receipt.json",
                        {**self.receipt(), "Saved": False, "Profile": None, "Location": "CorruptedPlayer"})
        act4.write_profile(self.account, 2, self.artifact, self.game, self.prepared)
        record = act4.record_preparation(self.artifact, self.account, 2, self.game,
                                        [deck()], [deck()], "fixed", 5)
        if ready:
            act4.checkpoint(self.artifact, record, state="ready", receipt=self.receipt())
        return record

    def receipt(self):
        build = deck()
        return {"Saved": True, "Profile": 2, "Visible": True, "Location": "Watcher",
                "Seed": "fixed", "Ascension": 5, "Offers": ["a", "b", "c"],
                "Players": [{"Character": build["character"], "Cards": 1, "Relics": 1, "Hp": 31,
                             "MaxHp": 80, "Gold": 123, "MaxEnergy": 3, "BaseOrbSlotCount": 0}],
                "Opponents": [{"Character": build["character"], "Cards": 1, "MaxHp": 80,
                               "ContentHash": act4.snapshot(build)["ContentHash"]}]}

    def args(self, command="launch-only", **changes):
        if command == "restart-existing-playtest":
            changes.setdefault("unchanged_playtest", True)
        return SimpleNamespace(command=command, preparation=str(self.artifact), steam="/fake/steam",
                               ready_timeout=30, confirm_no_pending_launch=False, **changes)

    def emit_receipt(self, command, **kwargs):
        request = json.loads(Path(command[command.index("--architect-save-playtest") + 1]).read_text())
        act4.write_json(Path(request["OutputDirectory"]) / "ready.json", self.receipt())
        return SimpleNamespace(returncode=0)

    def test_steam_resolution_flag_env_native_then_path_without_silent_explicit_fallback(self):
        with patch.object(act4.Path, "home", return_value=self.root), \
             patch.object(act4.shutil, "which", side_effect=lambda name: name), \
             patch.dict(act4.os.environ, {"ARCHITECT_STEAM": "/env/steam"}):
            self.assertEqual(act4.steam_executable("/flag/steam"), "/flag/steam")
            self.assertEqual(act4.steam_executable(), "/env/steam")
        native = self.root / ".local/share/Steam/ubuntu12_32/steam"
        with patch.object(act4.Path, "home", return_value=self.root), \
             patch.dict(act4.os.environ, {"ARCHITECT_STEAM": ""}), \
             patch.object(act4.shutil, "which", side_effect=lambda name: name if name == str(native) else None):
            self.assertEqual(act4.steam_executable(), str(native))
        with patch.dict(act4.os.environ, {"ARCHITECT_STEAM": ""}), \
             patch.object(act4.shutil, "which", side_effect=lambda name: "/usr/bin/steam" if name == "steam" else None):
            self.assertEqual(act4.steam_executable(), str(Path("/usr/bin/steam").resolve()))
            with self.assertRaisesRegex(ValueError, "No executable"):
                act4.steam_executable("/missing/explicit")

    def test_zero_exit_without_startup_is_not_success_and_stale_receipts_are_ignored(self):
        old = self.artifact / "attempt-old"
        old.mkdir()
        act4.write_json(old / "ready.json", self.receipt())
        with patch.object(act4.subprocess, "run", return_value=SimpleNamespace(returncode=0)) as run, \
             patch.object(act4.time, "monotonic", side_effect=[0, 0, 0, 0, 31, 32]):
            with self.assertRaisesRegex(ValueError, "no game process"):
                act4.dispatch(self.game, "/steam", self.artifact, 2, timeout=30)
        run.assert_called_once()
        attempt = next(path for path in self.artifact.glob("attempt-*") if path != old)
        status = json.loads((attempt / "dispatch.json").read_text())
        self.assertEqual(status["State"], "no-startup-evidence")
        self.assertEqual(status["ExitCode"], 0)
        self.assertFalse(status["StartupEvidence"])
        self.assertTrue((attempt / "steam.log").exists())
        with self.assertRaisesRegex(ValueError, "unresolved"):
            act4.check_pending_attempts(self.artifact, False)
        act4.check_pending_attempts(self.artifact, True)

    def test_slow_loading_and_dispatch_timeout_do_not_trigger_a_second_launch(self):
        def slow(command, **kwargs):
            request = json.loads(Path(command[command.index("--architect-save-playtest") + 1]).read_text())
            (Path(request["OutputDirectory"]) / "game.log").write_text("Preloading assets\nresource warning")
            raise subprocess.TimeoutExpired(command, 30)
        with patch.object(act4.subprocess, "run", side_effect=slow) as run, \
             patch.object(act4.time, "monotonic", side_effect=[0, 0, 0, 0, 31, 32]):
            with self.assertRaisesRegex(ValueError, "Startup occurred"):
                act4.dispatch(self.game, "/steam", self.artifact, 2, timeout=30)
        run.assert_called_once()
        status = json.loads(next(self.artifact.glob("attempt-*/dispatch.json")).read_text())
        self.assertEqual(status["State"], "receipt-timeout")
        self.assertTrue(status["DispatchTimedOut"])

    def test_launch_only_never_rebuilds_reinstalls_regenerates_or_writes_profile(self):
        self.prepare()
        before = act4.hashes(self.account), act4.hashes(self.game)
        with patch.object(act4, "steam_executable", return_value="/steam"), \
             patch.object(act4.subprocess, "run", side_effect=self.emit_receipt), \
             patch.object(act4, "native_run") as native, patch.object(act4, "install_bundle") as install, \
             patch.object(act4, "compose_save") as compose, patch.object(act4, "write_profile") as writer:
            act4.recover(self.args())
        for operation in (native, install, compose, writer):
            operation.assert_not_called()
        self.assertEqual(before, (act4.hashes(self.account), act4.hashes(self.game)))
        self.assertEqual(act4.read_record(self.artifact, "checkpoint")["State"], "ready")
        self.assertEqual(json.loads(next(self.artifact.glob("attempt-*/dispatch.json")).read_text())["State"], "ready")

    def test_recovery_rejects_changed_save_selection_dependencies_game_and_frozen_artifacts(self):
        self.prepare()
        paths = [self.account / "modded/profile2/saves/current_run.save",
                 self.account / "modded/profile2/saves/current_run.save.backup",
                 self.account / "modded/profile.save", self.game / "SlayTheSpire2",
                 self.game / "mods/BaseLib/BaseLib.dll", self.game / "mods/TheArchitect/TheArchitect.dll",
                 self.artifact / "mods/TheArchitect/TheArchitect.pck", self.artifact / "verified.save",
                 self.artifact / "inputs.json", self.artifact / "preparation.json",
                 self.artifact / "checkpoint.json"]
        for path in paths:
            before = path.read_bytes()
            path.write_bytes(before + b" ")
            with self.subTest(path=path), self.assertRaises(ValueError):
                act4.validate_preparation(self.artifact)
            path.write_bytes(before)
        act4.validate_preparation(self.artifact)

    def test_legacy_and_unknown_versions_are_deliberately_rejected(self):
        with self.assertRaisesRegex(ValueError, "Legacy"):
            act4.validate_preparation(self.artifact)
        record = self.prepare()
        act4.sealed_record(self.artifact, "preparation", {**record, "Version": 99})
        with self.assertRaisesRegex(ValueError, "Unsupported"):
            act4.validate_preparation(self.artifact)

    def test_invalid_receipt_does_not_replace_checkpoint(self):
        self.prepare()
        previous = act4.read_record(self.artifact, "checkpoint")
        original = self.receipt()
        with patch.object(self, "receipt", return_value={**original, "Seed": "wrong"}), \
             patch.object(act4.subprocess, "run", side_effect=self.emit_receipt):
            with self.assertRaisesRegex(ValueError, "does not match"):
                act4.launch_prepared(self.artifact, "/steam", 30)
        self.assertEqual(previous, act4.read_record(self.artifact, "checkpoint"))
        self.assertEqual(json.loads(next(self.artifact.glob("attempt-*/dispatch.json")).read_text())["State"],
                         "receipt-invalid")

    def test_preflight_fails_before_native_processes_or_stopping_game(self):
        load_args = SimpleNamespace(player=["deck"], corrupted=["deck"], fake_multiplayer=False,
                                    verify=False, no_launch=False, game=str(self.game), steam="/steam",
                                    data_root=str(self.root / "data"), account=None, slot=2,
                                    replace=False, stop_game=True)
        (self.account.parent / "456").mkdir()
        with patch.object(act4, "ARTIFACTS", self.root / "artifacts"), \
             patch.object(act4, "read_deck", return_value=deck()), \
             patch.object(act4, "steam_executable", return_value="/steam"), \
             patch.object(act4, "native_run") as native, patch.object(act4.os, "kill") as kill:
            with self.assertRaisesRegex(ValueError, "one Steam account"):
                act4.load(load_args)
        native.assert_not_called()
        kill.assert_not_called()
        stage = json.loads(next((self.root / "artifacts").glob("load-*/stages.json")).read_text())[0]
        self.assertEqual(stage["Stage"], "preflight")
        self.assertEqual(stage["Status"], "failed")
        self.assertEqual(stage["ErrorType"], "ValueError")
        self.assertGreaterEqual(stage["Seconds"], 0)

    def test_restart_requires_all_consents_and_known_success_before_stopping(self):
        self.prepare(ready=True)
        for stop, ui in ((False, False), (False, True), (True, False)):
            with patch.object(act4, "steam_executable", return_value="/steam"), \
                 patch.object(act4.os, "kill") as kill, \
                 self.assertRaisesRegex(ValueError, "requires --stop-game"):
                act4.recover(self.args("restart-existing-playtest", stop_game=stop, ui_only=ui))
            kill.assert_not_called()
        with patch.object(act4, "steam_executable", return_value="/steam"), \
             patch.object(act4.os, "kill") as kill, self.assertRaisesRegex(ValueError, "unsaved progress"):
            act4.recover(self.args("restart-existing-playtest", stop_game=True, ui_only=True,
                                   unchanged_playtest=False))
        kill.assert_not_called()
        record = act4.read_record(self.artifact, "preparation")
        act4.checkpoint(self.artifact, record, state="prepared")
        with patch.object(act4, "steam_executable", return_value="/steam"), \
             self.assertRaisesRegex(ValueError, "previously validated"):
            act4.recover(self.args("restart-existing-playtest", stop_game=True, ui_only=True))

    def test_restart_backs_up_and_installs_ui_without_resetting_profile_or_offers(self):
        self.prepare(ready=True)
        before, old_mod = act4.hashes(self.account), act4.hashes(self.game / "mods/TheArchitect")
        (self.mods / "TheArchitect/TheArchitect.dll").write_text("UI build")
        with patch.object(act4, "ARTIFACTS", self.root / "artifacts"), \
             patch.object(act4, "steam_executable", return_value="/steam"), \
             patch.object(act4.subprocess, "run", side_effect=self.emit_receipt), \
             patch.object(act4, "native_run") as native, patch.object(act4, "write_profile") as writer:
            act4.recover(self.args("restart-existing-playtest", stop_game=True, ui_only=True))
        native.assert_not_called()
        writer.assert_not_called()
        self.assertEqual(before, act4.hashes(self.account))
        destination = next((self.root / "artifacts").glob("restart-*"))
        self.assertEqual(old_mod, act4.hashes(destination / "installed-mod-backup"))
        self.assertEqual((self.game / "mods/TheArchitect/TheArchitect.dll").read_text(), "UI build")
        self.assertEqual(act4.read_record(destination, "checkpoint")["Receipt"]["Offers"], ["a", "b", "c"])
        for name, digest in before.items():
            self.assertEqual(act4.sha(destination / "backup" / Path(name).relative_to("modded")), digest)

    def test_restart_rejects_progress_and_manifest_changes_without_stopping(self):
        self.prepare(ready=True)
        for path in (self.account / "modded/profile2/saves/current_run.save",
                     self.mods / "TheArchitect/TheArchitect.json"):
            content = path.read_bytes()
            path.write_bytes(content + b" ")
            with patch.object(act4, "ARTIFACTS", self.root / "artifacts"), \
                 patch.object(act4, "steam_executable", return_value="/steam"), \
                 patch.object(act4, "require_closed") as closed, self.assertRaises(ValueError):
                act4.recover(self.args("restart-existing-playtest", stop_game=True, ui_only=True))
            closed.assert_not_called()
            path.write_bytes(content)

    def test_changed_natural_offers_are_a_failure(self):
        self.prepare(ready=True)
        receipt = self.receipt()
        with patch.object(self, "receipt", return_value={**receipt, "Offers": ["x", "b", "c"]}), \
             patch.object(act4.subprocess, "run", side_effect=self.emit_receipt):
            with self.assertRaisesRegex(ValueError, "Natural offers changed"):
                act4.launch_prepared(self.artifact, "/steam", 30)

    def test_full_no_launch_flow_keeps_both_native_checks_and_records_recoverable_state(self):
        def native(game, flag, source, artifact, **kwargs):
            output = artifact / flag.removeprefix("--")
            output.mkdir()
            if flag == "--save-template":
                act4.write_json(output / "template.save", template())
                act4.write_json(output / "models.json", sorted(MODELS))
            else:
                act4.write_json(output / "verified.json", json.loads(source.read_text()))
                act4.write_json(output / "ready.json",
                                {**self.receipt(), "Saved": False, "Profile": None, "Location": "CorruptedPlayer"})
            return output
        args = SimpleNamespace(player=["player"], corrupted=["opponent"], fake_multiplayer=False,
                               verify=False, no_launch=True, game=str(self.game), steam=None,
                               data_root=str(self.root / "data"), account=None, slot=2, seed="fixed",
                               ascension=None, replace=False, stop_game=False, ready_timeout=30)
        with patch.object(act4, "ARTIFACTS", self.root / "artifacts"), \
             patch.object(act4, "read_deck", return_value=deck()), \
             patch.object(act4.NATIVE, "private_preflight"), \
             patch.object(act4, "native_run", side_effect=native) as run, \
             patch.object(act4, "dispatch") as dispatch:
            act4.load(args)
        self.assertEqual([call.args[1] for call in run.call_args_list], ["--save-template", "--save-input"])
        dispatch.assert_not_called()
        artifact = next((self.root / "artifacts").glob("load-*"))
        record, status, _ = act4.validate_preparation(artifact)
        self.assertEqual(record["Verification"], "native")
        self.assertEqual(status["State"], "prepared")
        stages = json.loads((artifact / "stages.json").read_text())
        self.assertTrue(all(stage["Status"] == "ok" for stage in stages))
        self.assertEqual(stages[0]["Stage"], "preflight")
        self.assertEqual(stages[-1]["Stage"], "backup-and-write-profile")

    def test_restart_rechecks_save_after_stop_before_installing(self):
        self.prepare(ready=True)
        before = act4.hashes(self.game)
        def close(game, stop=False):
            if stop:
                path = self.account / "modded/profile2/saves/current_run.save"
                path.write_text(path.read_text() + " ")
        with patch.object(act4, "ARTIFACTS", self.root / "artifacts"), \
             patch.object(act4, "steam_executable", return_value="/steam"), \
             patch.object(act4, "require_closed", side_effect=close), \
             patch.object(act4, "install_bundle") as install, self.assertRaisesRegex(ValueError, "reset progress"):
            act4.recover(self.args("restart-existing-playtest", stop_game=True, ui_only=True))
        install.assert_not_called()
        self.assertEqual(act4.hashes(self.game), before)

    def test_partial_receipt_is_waited_for_and_nonfatal_logs_do_not_fail_success(self):
        self.prepare()
        def partial(command, **kwargs):
            request = json.loads(Path(command[command.index("--architect-save-playtest") + 1]).read_text())
            output = Path(request["OutputDirectory"])
            (output / "game.log").write_text("Missing encounter image\nResource leak warning")
            (output / "ready.json").write_text("{")
            return SimpleNamespace(returncode=0)
        def finish(_):
            act4.write_json(next(self.artifact.glob("attempt-*")) / "ready.json", self.receipt())
        with patch.object(act4.subprocess, "run", side_effect=partial) as run, \
             patch.object(act4.time, "sleep", side_effect=finish):
            act4.launch_prepared(self.artifact, "/steam", 30)
        run.assert_called_once()
        self.assertEqual(act4.read_record(self.artifact, "checkpoint")["State"], "ready")

    def test_native_error_is_classified_separately_from_no_startup(self):
        def fail(command, **kwargs):
            request = json.loads(Path(command[command.index("--architect-save-playtest") + 1]).read_text())
            (Path(request["OutputDirectory"]) / "error.txt").write_text("Assertion failed")
            return SimpleNamespace(returncode=1)
        with patch.object(act4.subprocess, "run", side_effect=fail):
            with self.assertRaisesRegex(act4.PlaytestFailure, "Assertion failed") as error:
                act4.dispatch(self.game, "/steam", self.artifact, 2, timeout=30)
        self.assertEqual(error.exception.category, "native-error")
        status = json.loads(next(self.artifact.glob("attempt-*/dispatch.json")).read_text())
        self.assertEqual(status["ExitCode"], 1)

    def test_linked_installed_files_are_refused_before_any_copy(self):
        destination = self.game / "mods/TheArchitect/TheArchitect.dll"
        destination.unlink()
        protected = self.root / "protected"
        protected.write_text("must not change")
        destination.symlink_to(protected)
        with self.assertRaisesRegex(ValueError, "linked installed mod files"):
            act4.bundle_preflight(self.game, live=True)
        self.assertEqual(protected.read_text(), "must not change")

    def test_operation_lock_rejects_concurrent_dispatch_and_releases_after_failure(self):
        with patch.object(act4, "ARTIFACTS", self.root / "artifacts"):
            with self.assertRaisesRegex(ValueError, "Another Act 4 operation"):
                with act4.operation_lock(), act4.operation_lock():
                    self.fail("A concurrent operation must not acquire the lock.")
            with act4.operation_lock():
                pass

    def test_failed_ui_dispatch_remains_prepared_not_ready_for_another_restart(self):
        self.prepare(ready=True)
        before = act4.hashes(self.account)
        with patch.object(act4, "ARTIFACTS", self.root / "artifacts"), \
             patch.object(act4, "steam_executable", return_value="/steam"), \
             patch.object(act4, "dispatch", side_effect=ValueError("dispatch failed")), \
             self.assertRaisesRegex(ValueError, "dispatch failed"):
            act4.recover(self.args("restart-existing-playtest", stop_game=True, ui_only=True))
        destination = next((self.root / "artifacts").glob("restart-*"))
        self.assertEqual(act4.read_record(destination, "checkpoint")["State"], "prepared")
        self.assertEqual(act4.hashes(self.account), before)
        act4.validate_preparation(destination)

    def test_profile_changed_during_install_is_not_overwritten(self):
        act4.freeze_bundle(self.game, self.artifact, live=True)
        def changed(game, artifact):
            act4.write_json(self.account / "modded/profile.save", {"last_profile_id": 3})
        with patch.object(act4, "install_bundle", side_effect=changed), \
             self.assertRaisesRegex(ValueError, "changed during installation"):
            act4.write_profile(self.account, 2, self.artifact, self.game, self.prepared)
        self.assertFalse((self.account / "modded/profile2/saves/current_run.save").exists())
        self.assertEqual(json.loads((self.account / "modded/profile.save").read_text())["last_profile_id"], 3)

    def test_changed_persisted_opponents_are_a_failure(self):
        self.prepare()
        def changed_save(command, **kwargs):
            result = self.emit_receipt(command, **kwargs)
            path = self.account / "modded/profile2/saves/current_run.save"
            save = json.loads(path.read_text())
            state = json.loads(act4.state_location(save)[act4.SAVE_KEY])
            state["PlaytestOpponents"][0]["MaxHp"] += 1
            act4.state_location(save)[act4.SAVE_KEY] = json.dumps(state)
            act4.write_json(path, save)
            return result
        with patch.object(act4.subprocess, "run", side_effect=changed_save):
            with self.assertRaisesRegex(ValueError, "selected opponents"):
                act4.launch_prepared(self.artifact, "/steam", 30)
