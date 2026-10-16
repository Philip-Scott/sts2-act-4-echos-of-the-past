"""Launcher regressions without starting a game or accessing a real display."""

import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch


SPEC = importlib.util.spec_from_file_location(
    "native_demo", Path(__file__).resolve().parents[1] / "scripts/native-demo.py")
demo = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(demo)


def party_history_fixture(root, count):
    members = []
    for index in range(count):
        member = {"MaxEnergy": 3, "BaseOrbSlotCount": 0}
        for role in ("Player", "Opponent"):
            history = root / f"{role}-{index}.run"
            history.write_text(json.dumps({"seed": f"{role}-{index}", "start_time": index,
                                           "win": True, "was_abandoned": False, "players": [{}]}))
            member[role + "HistoryPath"] = str(history)
        members.append(member)
    config = root / "selection.json"
    config.write_text(json.dumps({"Seed": "PARTY-SEED", "Members": members}))
    return config


class PartyHistoryTests(unittest.TestCase):
    def test_cli_save_fixture_supports_existing_manual_display(self):
        for flag, scenario, extra in (("--save-template", "save-template", []),
                                      ("--save-input", "save-input", ["--manual", "--shared-visible"])):
            with self.subTest(flag=flag), patch.object(demo.sys, "argv",
                    ["native-demo.py", "run", "/game", flag, "/input.json", *extra]), \
                    patch.object(demo, "launch", return_value=0) as launch:
                self.assertEqual(demo.main(), 0)
                self.assertEqual(launch.call_args.args[0].scenario, scenario)

    def test_save_fixture_rejects_conflicting_scenarios_before_launch(self):
        for template, saved, scenario in (("/input", "/input", "save-template"),
                                          ("/input", None, "party-2"), (None, "/input", "party-2")):
            with self.subTest(template=template, saved=saved), patch.object(demo, "required_file") as required:
                with self.assertRaisesRegex(ValueError, "cannot be combined"):
                    demo.launch(SimpleNamespace(save_template=template, save_input=saved,
                                                scenario=scenario, layout_only=False))
                required.assert_not_called()

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(dir=demo.ROOT)
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.config = party_history_fixture(self.root, 2)

    def test_history_requires_manual_visible_individual_non_downfall_party(self):
        for manual, visible, scenario in (
                (False, True, "party-2"), (True, False, "party-2"),
                (True, True, "party"), (True, True, "downfall-party"),
                (True, True, "downfall-champ")):
            with self.subTest(manual=manual, visible=visible, scenario=scenario), \
                 patch.object(demo, "required_file") as required:
                with self.assertRaisesRegex(ValueError, "--party-history requires"):
                    demo.launch(SimpleNamespace(party_history=str(self.config), manual=manual,
                                                shared_visible=visible, scenario=scenario))
                required.assert_not_called()

    def test_cli_accepts_history_config_with_manual_party(self):
        with patch.object(demo.sys, "argv", ["native-demo.py", "run", "/game", "--party-2",
                                           "--manual", "--shared-visible", "--party-history",
                                           str(self.config)]), \
             patch.object(demo, "launch", return_value=0) as launch:
            self.assertEqual(demo.main(), 0)
        args = launch.call_args.args[0]
        self.assertEqual(args.party_history, str(self.config))
        self.assertEqual(args.scenario, "party-2")
        self.assertTrue(args.manual)
        self.assertTrue(args.shared_visible)

    def test_copy_freezes_histories_rewrites_paths_and_receipts_hashes(self):
        for count in (1, 2, 3, 4):
            with self.subTest(count=count):
                config = party_history_fixture(self.root, count)
                before = demo.hashes(self.root)
                selection = demo.load_party_history(config, count)
                run = self.root / f"run-{count}"
                run.mkdir()
                receipt = demo.copy_party_history(run, selection)
                copied = json.loads((run / "party-history.json").read_text())
                self.assertEqual(copied["Seed"], "PARTY-SEED")
                self.assertEqual(len(copied["Members"]), count)
                self.assertEqual(receipt["party_history_source_sha256"], before["selection.json"])
                self.assertEqual(receipt["party_history_files"], demo.hashes(run))
                for index, member in enumerate(copied["Members"], 1):
                    for role in ("Player", "Opponent"):
                        path = Path(member[role + "HistoryPath"])
                        self.assertEqual(path, run / f"party-history/{role.lower()}-{index}.run")
                        original = self.root / f"{role}-{index - 1}.run"
                        self.assertEqual(path.read_bytes(), original.read_bytes())
                        self.assertEqual(before[original.name], demo.hashlib.sha256(original.read_bytes()).hexdigest())
                original = self.root / "Player-0.run"
                original.write_text("changed")
                self.assertNotEqual((run / "party-history/player-1.run").read_text(), original.read_text())

    def test_config_rejects_invalid_seed_count_stats_and_paths(self):
        valid = json.loads(self.config.read_text())
        cases = [None, [], {}, {**valid, "Seed": ""}, {**valid, "Seed": " \n"},
                 {**valid, "Seed": 3}, {**valid, "Members": {}},
                 {**valid, "Members": valid["Members"][:1]}, {**valid, "Members": [None, None]}]
        for field, values in (("MaxEnergy", (None, 0, -1, True, 3.5, "3")),
                              ("BaseOrbSlotCount", (None, -1, False, 1.5, "0")),
                              ("PlayerHistoryPath", (None, "relative.run", str(self.root))),
                              ("OpponentHistoryPath", (None, "relative.run", str(self.root)))):
            for value in values:
                members = [{**member} for member in valid["Members"]]
                if value is None:
                    del members[0][field]
                else:
                    members[0][field] = value
                cases.append({**valid, "Members": members})
        for invalid in cases:
            with self.subTest(config=invalid):
                self.config.write_text(json.dumps(invalid))
                with self.assertRaises(ValueError):
                    demo.load_party_history(self.config, 2)
        self.config.write_text("{")
        with self.assertRaises(ValueError):
            demo.load_party_history(self.config, 2)

    def test_rejects_nonvictory_multiplayer_missing_and_duplicate_histories(self):
        history = self.root / "Player-0.run"
        valid = json.loads(history.read_text())
        cases = [None, [], {}, {**valid, "win": False}, {**valid, "win": 1},
                 {**valid, "was_abandoned": True}, {**valid, "was_abandoned": 0},
                 {**valid, "players": []}, {**valid, "players": [{}, {}]},
                 {**valid, "players": [None]}, {**valid, "seed": None},
                 {**valid, "start_time": None}, {**valid, "start_time": True}]
        for invalid in cases:
            with self.subTest(history=invalid):
                history.write_text(json.dumps(invalid))
                with self.assertRaises(ValueError):
                    demo.load_party_history(self.config, 2)
        history.write_text("{")
        with self.assertRaises(ValueError):
            demo.load_party_history(self.config, 2)
        history.unlink()
        with self.assertRaises(FileNotFoundError):
            demo.load_party_history(self.config, 2)
        history.write_bytes((self.root / "Opponent-1.run").read_bytes())
        with self.assertRaisesRegex(ValueError, "distinct victories"):
            demo.load_party_history(self.config, 2)

    def test_serve_passes_only_the_runtime_config_to_the_game(self):
        run = self.root / "run-test"
        run.mkdir()
        metadata = {"scenario": "party-2", "manual": True, "party_history": True,
                    "display_mode": "shared-visible", "host_display": ":7", "game": "/game"}
        process = Mock(returncode=0, pid=123)
        process.poll.return_value = 0
        with patch.object(demo, "owned_run", return_value=(run, metadata)), \
             patch.dict(demo.os.environ, {"ARCHITECT_NATIVE_RUNTIME": str(run), "DISPLAY": ":7"}), \
             patch.object(Path, "read_text", return_value="bwrap\n"), \
             patch.object(demo.os, "chdir"), patch.object(demo.os, "chmod"), \
             patch.object(demo.os, "readlink", return_value="namespace"), \
             patch.object(demo.signal, "signal"), patch.object(demo.socket, "socket"), \
             patch.object(demo.subprocess, "run", return_value=Mock(returncode=0)), \
             patch.object(demo.subprocess, "Popen", return_value=process) as popen, \
             patch("builtins.print"):
            self.assertEqual(demo.serve(SimpleNamespace(run=run.name)), 0)
        command = popen.call_args.args[0]
        self.assertEqual(command[command.index("--architect-native-party-history") + 1],
                         str(run / "party-history.json"))
        self.assertNotIn(str(self.config), command)


class NativeDemoTests(unittest.TestCase):
    def test_xvfb_resolution_uses_override_then_path_then_repo_tool(self):
        with tempfile.TemporaryDirectory(dir=demo.ROOT) as directory:
            root = Path(directory)
            candidate = root / "artifacts/tools/xvfb/usr/bin/Xvfb"
            candidate.parent.mkdir(parents=True)
            candidate.touch()
            with patch.object(demo, "ROOT", root), \
                 patch.object(demo, "tool", side_effect=lambda name: name) as tool, \
                 patch.object(demo.shutil, "which", return_value=None), \
                 patch.dict(demo.os.environ, {"ARCHITECT_XVFB": ""}):
                self.assertEqual(demo.resolve_xvfb(), str(candidate))
                with patch.dict(demo.os.environ, {"ARCHITECT_XVFB": "/chosen/Xvfb"}):
                    self.assertEqual(demo.resolve_xvfb(), "/chosen/Xvfb")
                with patch.object(demo.shutil, "which", return_value="/usr/bin/Xvfb"):
                    self.assertEqual(demo.resolve_xvfb(), "Xvfb")
                tool.assert_called_with("Xvfb")

    def test_override_requires_complete_baselib_before_creating_run(self):
        with tempfile.TemporaryDirectory(dir=demo.ROOT) as directory:
            root = Path(directory)
            mods = root / "mods"
            game = root / "game"
            for folder, name in ((mods / "TheArchitect", "TheArchitect"),
                                 (game / "mods/BaseLib", "BaseLib")):
                folder.mkdir(parents=True)
                for suffix in ("dll", "json", "pck"):
                    (folder / f"{name}.{suffix}").touch()
            with patch.dict(demo.os.environ, {"ARCHITECT_MODS_INPUT": str(mods)}), \
                 patch.object(demo.tempfile, "mkdtemp") as create:
                with self.assertRaisesRegex(ValueError, "override must include BaseLib"):
                    demo.mods_input(game)
                create.assert_not_called()
            with patch.dict(demo.os.environ):
                demo.os.environ.pop("ARCHITECT_MODS_INPUT", None)
                with patch.object(demo, "ROOT", root):
                    (root / "artifacts").mkdir()
                    (root / "artifacts/mods").symlink_to(mods, target_is_directory=True)
                    self.assertEqual(demo.mods_input(game), (mods, game / "mods/BaseLib"))

    def test_downfall_matchup_requires_visible_manual_mode(self):
        with patch.object(demo, "required_file") as required:
            with self.assertRaisesRegex(ValueError, "--downfall-matchup requires"):
                demo.launch(SimpleNamespace(manual=False, scenario="downfall-matchup"))
            with self.assertRaisesRegex(ValueError, "--manual requires"):
                demo.launch(SimpleNamespace(manual=True, shared_visible=False, scenario="downfall-matchup"))
            required.assert_not_called()

    def test_cli_accepts_manual_downfall_matchup(self):
        with patch.object(demo.sys, "argv", ["native-demo.py", "run", "/game",
                                           "--downfall-matchup", "--manual", "--shared-visible"]), \
             patch.object(demo, "launch", return_value=0) as launch:
            self.assertEqual(demo.main(), 0)
        args = launch.call_args.args[0]
        self.assertEqual(args.scenario, "downfall-matchup")
        self.assertTrue(args.manual)
        self.assertTrue(args.shared_visible)

    def test_watcher_probes_reject_shared_desktop_before_accessing_files(self):
        for scenario in ("enchantment-art", "stance-tooltips", "stance-vfx", "watcher-audio", "watcher-background"):
            with self.subTest(scenario=scenario), patch.object(demo, "required_file") as required:
                with self.assertRaisesRegex(ValueError, "private display"):
                    demo.launch(SimpleNamespace(scenario=scenario, shared_visible=True))
                required.assert_not_called()

    def test_watcher_audio_server_has_only_a_private_socket_and_null_sink(self):
        with patch.object(demo, "tool", side_effect=lambda name: "/usr/bin/" + name):
            command = demo.private_audio_command()
        self.assertEqual(command, ["/usr/bin/pipewire", "-c", str(demo.ROOT / "scripts/native-demo-audio.conf")])
        config = (demo.ROOT / "scripts/native-demo-audio.conf").read_text()
        self.assertIn("support.null-audio-sink", config)
        self.assertIn("unix:/tmp/watcher-audio.sock", config)
        self.assertNotIn("api.alsa", config)
        self.assertNotIn("udev", config)

    def test_watcher_audio_rejects_shared_desktop_before_accessing_files(self):
        with patch.object(demo, "required_file") as required:
            with self.assertRaisesRegex(ValueError, "private display"):
                demo.launch(SimpleNamespace(scenario="watcher-audio", shared_visible=True))
            required.assert_not_called()

    def test_recording_rejects_shared_desktop_before_accessing_files(self):
        with patch.object(demo, "required_file") as required:
            with self.assertRaisesRegex(ValueError, "private display"):
                demo.launch(SimpleNamespace(manual=False, layout_only=False, scenario="downfall-champ",
                                            shared_visible=True, record=True))
            required.assert_not_called()

    def test_keyword_mode_requires_downfall_character(self):
        with patch.object(demo, "required_file") as required:
            with self.assertRaisesRegex(ValueError, "--downfall-keywords requires"):
                demo.launch(SimpleNamespace(manual=False, layout_only=False, scenario="party",
                                            downfall_keywords=True))
            required.assert_not_called()

    def test_recording_uses_only_private_display_and_run_output(self):
        with patch.object(demo, "tool", side_effect=lambda name: "/usr/bin/" + name):
            command = demo.recording_command(Path("/run-test"), "1280x720")
        self.assertEqual(command[command.index("-i") + 1], ":0")
        self.assertEqual(command[-1], "/run-test/playtest.mp4")
        self.assertIn("-n", command)
        self.assertEqual(command[command.index("-video_size") + 1], "1280x720")

    def test_recording_finalizes_and_requires_real_frames(self):
        with tempfile.TemporaryDirectory() as temporary:
            run = Path(temporary)
            recorder = Mock(returncode=0)
            recorder.poll.side_effect = [None, 0]
            result = Mock(stdout=json.dumps({"streams": [{"width": 1280, "height": 720, "nb_frames": "90"}],
                                            "format": {"duration": "3.0"}}))
            with patch.object(demo, "tool", return_value="/usr/bin/ffprobe"), \
                 patch.object(demo.subprocess, "run", return_value=result):
                demo.finish_recording(recorder, run)
            recorder.communicate.assert_called_once_with(input=b"q\n", timeout=30)
            self.assertEqual(json.loads((run / "recording.json").read_text())["format"]["duration"], "3.0")
            recorder.terminate.assert_not_called()
            for output in ({"streams": [], "format": {"duration": "0"}},
                           {"streams": [{"nb_frames": "0"}], "format": {"duration": "1"}}):
                recorder = Mock(returncode=0)
                recorder.poll.return_value = 0
                with patch.object(demo, "tool", return_value="/usr/bin/ffprobe"), \
                     patch.object(demo.subprocess, "run", return_value=Mock(stdout=json.dumps(output))):
                    with self.assertRaisesRegex(ValueError, "no video frames"):
                        demo.finish_recording(recorder, run)

    def test_recording_failure_is_not_reported_as_success(self):
        recorder = Mock(returncode=1)
        recorder.poll.return_value = 1
        with patch.object(demo.subprocess, "run") as probe:
            with self.assertRaisesRegex(ValueError, "Video recording failed"):
                demo.finish_recording(recorder, Path("/run-test"))
            probe.assert_not_called()

    def test_manual_requires_visible_individual_party_before_accessing_files(self):
        for visible, scenario in ((False, "party-2"), (True, "party"), (True, "ancient")):
            with self.subTest(visible=visible, scenario=scenario), \
                 patch.object(demo, "required_file") as required:
                with self.assertRaisesRegex(ValueError, "--manual requires"):
                    demo.launch(SimpleNamespace(manual=True, shared_visible=visible, scenario=scenario))
                required.assert_not_called()

    def test_layout_only_requires_individual_automated_party(self):
        for manual, scenario in ((True, "party-2"), (False, "party"), (False, "ancient")):
            with self.subTest(manual=manual, scenario=scenario), \
                 patch.object(demo, "required_file") as required:
                with self.assertRaisesRegex(ValueError, "--layout-only requires"):
                    demo.launch(SimpleNamespace(manual=manual, layout_only=True,
                        shared_visible=True, scenario=scenario))
                required.assert_not_called()

    def test_single_player_inspection_requires_manual_mode(self):
        with patch.object(demo, "required_file") as required:
            with self.assertRaisesRegex(ValueError, "--party-1 requires --manual"):
                demo.launch(SimpleNamespace(manual=False, scenario="party-1"))
            required.assert_not_called()

    def test_downfall_party_requires_visible_manual_mode(self):
        with patch.object(demo, "required_file") as required:
            with self.assertRaisesRegex(ValueError, "--downfall-party requires"):
                demo.launch(SimpleNamespace(manual=False, scenario="downfall-party"))
            with self.assertRaisesRegex(ValueError, "--manual requires"):
                demo.launch(SimpleNamespace(manual=True, shared_visible=False, scenario="downfall-party"))
            required.assert_not_called()

    def test_sandbox_has_private_devices_and_namespaces_and_no_host_display(self):
        with patch.object(demo, "tool", side_effect=lambda name: "/usr/bin/" + name):
            command = demo.sandbox_command(Path("/game"), demo.RUNS / "run-test", "/usr/bin/Xvfb")
        for flag in ("--unshare-pid", "--unshare-net", "--unshare-ipc", "--clearenv"):
            self.assertIn(flag, command)
        self.assertNotIn("--dev-bind", command)
        self.assertNotIn("/tmp/.X11-unix", command)
        self.assertIn("--dev", command)
        self.assertIn("LIBGL_ALWAYS_SOFTWARE", command)
        self.assertIn("__EGL_VENDOR_LIBRARY_FILENAMES", command)
        self.assertIn(str(demo.RUNS), command)
        self.assertNotIn("DISPLAY", command)

    def test_run_lookup_rejects_paths_and_other_worktrees(self):
        with tempfile.TemporaryDirectory() as temporary:
            runs = Path(temporary)
            run = runs / "run-test"
            run.mkdir()
            (run / "run.json").write_text(json.dumps({"worktree": "/different"}))
            with patch.object(demo, "RUNS", runs):
                for name in ("../run-test", str(run), "run-test"):
                    with self.assertRaises(ValueError):
                        demo.owned_run(name)
                (run / "run.json").write_text(json.dumps({"worktree": str(demo.ROOT), "state": "exited"}))
                self.assertEqual(demo.owned_run("run-test")[0], run)
                (runs / "run-link").symlink_to(run)
                with self.assertRaises(ValueError):
                    demo.owned_run("run-link")

    def test_mod_hashes_detect_changes(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            file = directory / "TheArchitect.dll"
            file.write_bytes(b"build-one")
            before = demo.hashes(directory)
            file.write_bytes(b"build-two")
            self.assertNotEqual(before, demo.hashes(directory))

    def test_launch_freezes_mods_and_copies_snapshot_without_installing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            game, mods, snapshot = root / "game", root / "input-mods", root / "snapshot.json"
            game.mkdir()
            (game / "SlayTheSpire2").touch()
            snapshot.write_text('{"captured":true}')
            run_save = root / "current_run.save"
            run_save.write_text('{"saved_run":true}')
            (root / "scripts").mkdir()
            (root / "scripts/native-demo-settings.json").write_text("{}")
            for mod in ("TheArchitect", "BaseLib", "Downfall"):
                (mods / mod).mkdir(parents=True)
                for suffix in ("dll", "json", "pck"):
                    (mods / mod / f"{mod}.{suffix}").write_text("original")
            process = Mock(returncode=0)
            process.wait.return_value = 0
            process.poll.return_value = 0
            required = demo.required_file
            with patch.object(demo, "ROOT", root), patch.object(demo, "RUNS", root / "runs"), \
                 patch.dict(demo.os.environ, {"ARCHITECT_SNAPSHOT_INPUT": str(snapshot),
                                             "ARCHITECT_RUN_INPUT": str(run_save),
                                             "ARCHITECT_MODS_INPUT": str(mods)}), \
                 patch.object(demo, "tool", return_value="/usr/bin/unused"), \
                 patch.object(demo, "required_file", side_effect=lambda p: Path(p) if str(p).endswith("50_mesa.json") else required(p)), \
                 patch.object(demo, "sandbox_command", return_value=["unused"]), \
                 patch.object(demo.subprocess, "Popen", return_value=process), \
                 patch.object(demo.signal, "signal"), patch("builtins.print"):
                result = demo.launch(SimpleNamespace(game=str(game), label="test", scenario="default",
                                                     resolution="1280x720", manual=False, layout_only=False,
                                                     cache_from=None, cold=True, render_threads=4,
                                                     shared_visible=False, render_device="/dev/dri/renderD128"))
                with patch.dict(demo.os.environ, {"ARCHITECT_SNAPSHOT_INPUT": ""}):
                    ancient_result = demo.launch(SimpleNamespace(game=str(game), label="ancient", scenario="ancient",
                                                                 resolution="1280x720", manual=False, layout_only=False,
                                                                 cache_from=None, cold=True, render_threads=4,
                                                                 shared_visible=False, render_device="/dev/dri/renderD128"))
                    self.assertEqual(demo.launch(SimpleNamespace(game=str(game), label="enchantment-art",
                        scenario="enchantment-art", resolution="1920x1080", manual=False, layout_only=False,
                        cache_from=None, cold=True, render_threads=2,
                        shared_visible=False, render_device="/dev/dri/renderD128")), 0)
                    art_run = next((root / "runs").glob("run-*-enchantment-art-*"))
                    self.assertFalse((art_run / "snapshot-input.json").exists())
                    party_result = demo.launch(SimpleNamespace(game=str(game), label="party", scenario="party",
                                                               resolution="1280x720", manual=False, layout_only=False,
                                                               cache_from=None, cold=True, render_threads=4,
                                                               shared_visible=False, render_device="/dev/dri/renderD128"))
                    saved_result = demo.launch(SimpleNamespace(game=str(game), label="saved", scenario="saved-run",
                                                               resolution="1280x720", manual=False, layout_only=False,
                                                               cache_from=None, cold=True, render_threads=4,
                                                               shared_visible=False, render_device="/dev/dri/renderD128"))
                    vfx_result = demo.launch(SimpleNamespace(game=str(game), label="vfx", scenario="attack-vfx",
                                                             resolution="1280x720", manual=False, layout_only=False,
                                                             cache_from=None, cold=True, render_threads=4,
                                                             shared_visible=False, render_device="/dev/dri/renderD128"))
                    corruption_result = demo.launch(SimpleNamespace(game=str(game), label="corruption",
                        scenario="corruption", resolution="1280x720", cache_from=None, cold=True, manual=False, layout_only=False,
                        render_threads=4, shared_visible=False, render_device="/dev/dri/renderD128"))
                    for scenario in demo.DOWNFALL_SCENARIOS:
                        self.assertEqual(demo.launch(SimpleNamespace(game=str(game), label=scenario,
                            scenario=scenario, resolution="1280x720", cache_from=None, cold=True, manual=False,
                            layout_only=False, render_threads=4, shared_visible=False,
                            render_device="/dev/dri/renderD128", record=True, downfall_keywords=True)), 0)
                        downfall_run = next((root / "runs").glob(f"run-*-{scenario}-*"))
                        self.assertFalse((downfall_run / "snapshot-input.json").exists())
                        receipt = json.loads((downfall_run / "run.json").read_text())
                        self.assertIsNone(receipt["snapshot_sha256"])
                        self.assertIn("Downfall/Downfall.dll", receipt["mods"])
                        self.assertTrue(receipt["record"])
                        self.assertTrue(receipt["downfall_keywords"])
                    (mods / "Downfall/Downfall.dll").unlink()
                    run_count = len(list((root / "runs").iterdir()))
                    with self.assertRaises(FileNotFoundError):
                        demo.launch(SimpleNamespace(game=str(game), label="missing-downfall",
                            scenario="downfall-champ", resolution="1280x720", cache_from=None, cold=True,
                            manual=False, layout_only=False, render_threads=4, shared_visible=False,
                            render_device="/dev/dri/renderD128"))
                    self.assertEqual(len(list((root / "runs").iterdir())), run_count)
                    for size in (2, 3, 4):
                        scenario = f"party-{size}"
                        self.assertEqual(demo.launch(SimpleNamespace(game=str(game), label=scenario,
                            scenario=scenario, resolution="1280x720", cache_from=None, cold=True, manual=False, layout_only=False,
                            render_threads=4, shared_visible=False, render_device="/dev/dri/renderD128")), 0)
                        party_run = next((root / "runs").glob(f"run-*-{scenario}-*"))
                        self.assertFalse((party_run / "snapshot-input.json").exists())
                    history_config = party_history_fixture(root, 2)
                    with patch.dict(demo.os.environ, {"DISPLAY": ":7", "XAUTHORITY": str(snapshot)}), \
                         patch.object(Path, "is_socket", return_value=True), \
                         patch.object(Path, "is_char_device", return_value=True):
                        self.assertEqual(demo.launch(SimpleNamespace(game=str(game), label="history",
                            scenario="party-2", resolution="1280x720", cache_from=None, cold=True,
                            manual=True, layout_only=False, render_threads=4, shared_visible=True,
                            render_device="/dev/dri/renderD128", party_history=str(history_config))), 0)
                    history_run = next((root / "runs").glob("run-*-history-*"))
                    history_receipt = json.loads((history_run / "run.json").read_text())
                    self.assertTrue(history_receipt["party_history"])
                    self.assertEqual(len(history_receipt["party_history_files"]), 5)
                    self.assertTrue((history_run / "party-history.json").is_file())
                    self.assertFalse((history_run / "run-input.json").exists())
            self.assertEqual(result, 0)
            self.assertEqual(ancient_result, 0)
            self.assertEqual(party_result, 0)
            self.assertEqual(saved_result, 0)
            self.assertEqual(vfx_result, 0)
            self.assertEqual(corruption_result, 0)
            corruption = next((root / "runs").glob("run-*-corruption-*"))
            self.assertFalse((corruption / "snapshot-input.json").exists())
            self.assertIsNone(json.loads((corruption / "run.json").read_text())["snapshot_sha256"])
            vfx = next((root / "runs").glob("run-*-vfx-*"))
            self.assertFalse((vfx / "snapshot-input.json").exists())
            self.assertIsNone(json.loads((vfx / "run.json").read_text())["snapshot_sha256"])
            run = next((root / "runs").glob("run-*-test-*"))
            ancient = next((root / "runs").glob("run-*-ancient-*"))
            self.assertFalse((ancient / "snapshot-input.json").exists())
            party = next((root / "runs").glob("run-*-party-*"))
            self.assertFalse((party / "snapshot-input.json").exists())
            self.assertIsNone(json.loads((ancient / "run.json").read_text())["snapshot_sha256"])
            saved = next((root / "runs").glob("run-*-saved-*"))
            self.assertEqual((saved / "run-input.json").read_text(), run_save.read_text())
            self.assertFalse((saved / "snapshot-input.json").exists())
            self.assertEqual(json.loads((saved / "run.json").read_text())["run_save_sha256"],
                             demo.hashlib.sha256(run_save.read_bytes()).hexdigest())
            run_save.write_text('{"saved_run":"changed"}')
            self.assertEqual((saved / "run-input.json").read_text(), '{"saved_run":true}')
            (mods / "TheArchitect/TheArchitect.dll").write_text("rebuilt")
            self.assertEqual((run / "mods/TheArchitect/TheArchitect.dll").read_text(), "original")
            self.assertEqual((run / "snapshot-input.json").read_text(), snapshot.read_text())
            self.assertFalse((game / "mods").exists())
            self.assertEqual(json.loads((run / "run.json").read_text())["state"], "exited")

    def test_controls_are_bounded_not_arbitrary_commands(self):
        with patch.object(demo.subprocess, "run") as execute:
            for request in ([], {"action": "exec", "command": "anything"},
                            {"action": "pointer", "x": -1, "y": 0},
                            {"action": "pointer", "x": 0, "y": 720},
                            {"action": "pointer", "x": True, "y": 0},
                            {"action": "key", "key": "--window"},
                            {"action": "click", "button": 4}):
                with self.assertRaises(ValueError):
                    demo.perform(request, Path("/run-test"), {})
            execute.assert_not_called()
            demo.perform({"action": "pointer", "x": 10, "y": 20}, Path("/run-test"), {})
            self.assertEqual(execute.call_args.args[0],
                             ["xdotool", "mousemove", "10", "20"])

    def test_shared_display_mounts_only_selected_gpu_and_socket(self):
        run = demo.RUNS / "run-visible"
        with patch.object(demo, "tool", side_effect=lambda name: "/usr/bin/" + name):
            command = demo.sandbox_command(Path("/game"), run, None, shared_display=":2",
                                           render_device="/dev/dri/renderD128")
        self.assertIn("/tmp/.X11-unix/X2", command)
        self.assertIn("/dev/dri/renderD128", command)
        self.assertIn("DISPLAY", command)
        self.assertIn("--unshare-pid", command)
        self.assertIn("--unshare-net", command)
        self.assertNotIn("LIBGL_ALWAYS_SOFTWARE", command)
        self.assertNotIn(str(run / "Xvfb"), command)
        self.assertNotIn("/dev/input", command)
        self.assertNotIn("/dev/dri/card0", command)
        for display in ("host:0", "localhost:10", "", ":0;command"):
            with self.assertRaises(ValueError):
                demo.local_display_socket(display)

    def test_shared_display_rejects_desktop_input_and_uses_native_capture(self):
        metadata = {"display_mode": "shared-visible"}
        with tempfile.TemporaryDirectory() as temporary, patch.object(demo.subprocess, "run") as execute:
            run = Path(temporary)
            (run / "captures").mkdir()
            for action in ("pointer", "key", "click"):
                with self.assertRaisesRegex(ValueError, "Desktop input controls are disabled"):
                    demo.perform({"action": action}, run, metadata)
            with patch.object(demo.time, "time_ns", return_value=123):
                def complete(_delay):
                    self.assertEqual((run / "capture-request").read_text(), "capture-123")
                    (run / "captures/capture-123.png").write_bytes(b"png")
                    (run / "captures/capture-123.ready").write_text("ok")
                with patch.object(demo.time, "sleep", side_effect=complete):
                    response = demo.perform({"action": "capture"}, run, metadata)
            self.assertEqual(response, {"capture": str(run / "captures/capture-123.png")})
            execute.assert_not_called()

    def test_stop_does_not_signal_a_pid_from_metadata(self):
        with patch.object(demo.subprocess, "run") as execute:
            response = demo.perform({"action": "stop"}, Path("/run-test"),
                                    {"state": "running", "game_pid": 123})
            self.assertEqual(response["state"], "stopping")
            execute.assert_not_called()

    def test_cleanup_waits_and_escalates_only_its_child(self):
        child = Mock()
        child.poll.return_value = None
        child.wait.side_effect = [subprocess.TimeoutExpired("owned", 5), 0]
        demo.stop_child(child)
        child.terminate.assert_called_once_with()
        child.kill.assert_called_once_with()
        self.assertEqual(child.wait.call_count, 2)
        exited = Mock()
        exited.poll.return_value = 0
        demo.stop_child(exited)
        exited.terminate.assert_not_called()

    def test_control_message_size_and_format(self):
        connection = Mock()
        connection.recv.side_effect = [b'{"action":', b'"status"}\n']
        self.assertEqual(demo.receive(connection), {"action": "status"})
        connection.recv.side_effect = [b"x" * 8193]
        with self.assertRaisesRegex(ValueError, "too large"):
            demo.receive(connection)
        connection.recv.side_effect = [b""]
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            demo.receive(connection)

    def test_cache_seed_copies_only_shaders_from_completed_owned_run(self):
        with tempfile.TemporaryDirectory() as temporary:
            runs = Path(temporary)
            source, destination = runs / "run-source", runs / "run-new"
            source.mkdir()
            destination.mkdir()
            metadata = {"worktree": str(demo.ROOT), "game": "/game", "state": "exited",
                        "exit_code": 0, "display": ":0 (private namespace)"}
            (source / "run.json").write_text(json.dumps(metadata))
            for directory in demo.CACHE_PATHS:
                path = source / directory
                path.mkdir(parents=True)
                (path / "shader").write_text("compiled")
            profile = source / "xdg/SlayTheSpire2/default"
            profile.mkdir()
            (profile / "real-save").write_text("do not copy")
            with patch.object(demo, "RUNS", runs):
                self.assertIsNone(demo.seed_shader_cache(destination, Path("/game"), cold=True))
                self.assertEqual(demo.seed_shader_cache(destination, Path("/game")), source.name)
                for directory in demo.CACHE_PATHS:
                    self.assertEqual((destination / directory / "shader").read_text(), "compiled")
                self.assertFalse((destination / "xdg/SlayTheSpire2/default").exists())
                with self.assertRaisesRegex(ValueError, "completed virtual run"):
                    demo.seed_shader_cache(destination, Path("/other-game"), source.name)
                metadata["state"] = "running"
                (source / "run.json").write_text(json.dumps(metadata))
                with self.assertRaisesRegex(ValueError, "completed virtual run"):
                    demo.seed_shader_cache(destination, Path("/game"), source.name)


if __name__ == "__main__":
    unittest.main()
