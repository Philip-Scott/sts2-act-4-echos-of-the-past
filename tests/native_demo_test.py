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


class NativeDemoTests(unittest.TestCase):
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
            for mod in ("TheArchitect", "BaseLib"):
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
                                                     resolution="1280x720",
                                                     cache_from=None, cold=True, render_threads=4,
                                                     shared_visible=False, render_device="/dev/dri/renderD128"))
                with patch.dict(demo.os.environ, {"ARCHITECT_SNAPSHOT_INPUT": ""}):
                    ancient_result = demo.launch(SimpleNamespace(game=str(game), label="ancient", scenario="ancient",
                                                                 resolution="1280x720",
                                                                 cache_from=None, cold=True, render_threads=4,
                                                                 shared_visible=False, render_device="/dev/dri/renderD128"))
                    party_result = demo.launch(SimpleNamespace(game=str(game), label="party", scenario="party",
                                                               resolution="1280x720",
                                                               cache_from=None, cold=True, render_threads=4,
                                                               shared_visible=False, render_device="/dev/dri/renderD128"))
                    saved_result = demo.launch(SimpleNamespace(game=str(game), label="saved", scenario="saved-run",
                                                               resolution="1280x720",
                                                               cache_from=None, cold=True, render_threads=4,
                                                               shared_visible=False, render_device="/dev/dri/renderD128"))
                    vfx_result = demo.launch(SimpleNamespace(game=str(game), label="vfx", scenario="attack-vfx",
                                                             resolution="1280x720",
                                                             cache_from=None, cold=True, render_threads=4,
                                                             shared_visible=False, render_device="/dev/dri/renderD128"))
                    corruption_result = demo.launch(SimpleNamespace(game=str(game), label="corruption",
                        scenario="corruption", resolution="1280x720", cache_from=None, cold=True,
                        render_threads=4, shared_visible=False, render_device="/dev/dri/renderD128"))
                    for size in (2, 3, 4):
                        scenario = f"party-{size}"
                        self.assertEqual(demo.launch(SimpleNamespace(game=str(game), label=scenario,
                            scenario=scenario, resolution="1280x720", cache_from=None, cold=True,
                            render_threads=4, shared_visible=False, render_device="/dev/dri/renderD128")), 0)
                        party_run = next((root / "runs").glob(f"run-*-{scenario}-*"))
                        self.assertFalse((party_run / "snapshot-input.json").exists())
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
