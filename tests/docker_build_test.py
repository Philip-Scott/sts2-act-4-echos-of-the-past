"""Container command construction without running a container or a build."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class ContainerBuildTests(unittest.TestCase):
    def run_helper(self, engine, reuse="true", inspect_code=0, arguments=None, extra_env=None):
        directory = tempfile.TemporaryDirectory(dir=ROOT)
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        fake = root / engine
        fake.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "with open(os.environ['CALLS'], 'a') as log:\n"
            "    log.write(json.dumps(sys.argv[1:]) + '\\n')\n"
            "if sys.argv[1:3] == ['image', 'inspect']:\n"
            "    sys.exit(int(os.environ['INSPECT_CODE']))\n")
        fake.chmod(0o755)
        game = root / "game"
        game.mkdir()
        output = root / "isolated-mods"
        log = root / "calls.jsonl"
        env = {**os.environ, "DOCKER": str(fake), "IMAGE": "localhost/build:latest",
               "STS2_PATH": str(game), "OUTPUT_DIR": str(output), "REUSE_IMAGE": reuse,
               "CONTAINER_HOME": str(output) + ".container-home",
               "CALLS": str(log), "INSPECT_CODE": str(inspect_code), **(extra_env or {})}
        result = subprocess.run(["bash", str(ROOT / "scripts/docker-build.sh"),
                                 *(arguments if arguments is not None else ["--", "--verbosity", "quiet"])],
                                env=env, capture_output=True, text=True)
        calls = [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []
        return result, calls, output

    def test_existing_podman_image_is_not_built_pulled_or_run_with_host_uid(self):
        result, calls, output = self.run_helper("podman")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls[0], ["image", "inspect", "localhost/build:latest"])
        self.assertEqual(len(calls), 2)
        command = calls[1]
        self.assertEqual(command[0], "run")
        self.assertIn("--userns=keep-id", command)
        self.assertNotIn("--user", command)
        self.assertIn("label=disable", command)
        self.assertIn("--pull=never", command)
        self.assertIn(f"{output}:/out", command)
        self.assertIn(f"{output}.container-home:/container-home", command)
        self.assertIn("HOME=/container-home", command)
        self.assertIn("DOTNET_CLI_HOME=/container-home", command)
        self.assertIn("NUGET_PACKAGES=/container-home/.nuget/packages", command)
        self.assertEqual(list(output.iterdir()), [])
        self.assertFalse(any("/nuget-cache" in value for value in command))
        self.assertEqual(command[-4:], ["localhost/build:latest", "--", "--verbosity", "quiet"])

    def test_docker_build_default_keeps_numeric_user(self):
        result, calls, _ = self.run_helper("docker", reuse="false")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls[0][0], "build")
        self.assertIn("--user", calls[1])
        self.assertNotIn("--userns=keep-id", calls[1])
        self.assertIn("localhost_build_latest-nuget:/nuget-cache", calls[1])

    def test_missing_reused_image_never_builds_runs_or_creates_output(self):
        result, calls, output = self.run_helper("podman", inspect_code=1)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(len(calls), 1)
        self.assertFalse(output.exists())

    def test_invalid_reuse_value_is_rejected(self):
        result, calls, output = self.run_helper("podman", reuse="yes")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(calls, [])
        self.assertFalse(output.exists())

    def test_container_home_inside_bundle_is_rejected(self):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            result, calls, _ = self.run_helper("podman", extra_env={
                "OUTPUT_DIR": directory, "CONTAINER_HOME": str(Path(directory) / ".home")})
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("must be outside OUTPUT_DIR", result.stderr)
            self.assertEqual(len(calls), 1)

    def test_test_mode_passes_explicit_paths_and_arguments_to_both_projects(self):
        result, calls, output = self.run_helper("podman", arguments=["--test", "--verbosity", "quiet"])
        self.assertEqual(result.returncode, 0, result.stderr)
        command = calls[-1]
        self.assertIn("--entrypoint", command)
        body = command[command.index("-c") + 1]
        data = output.parent / "assemblies"
        data.mkdir()
        for assembly in ("sts2.dll", "0Harmony.dll", "GodotSharp.dll"):
            (data / assembly).touch()
        dotnet = output.parent / "dotnet"
        dotnet.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "with open(os.environ['DOTNET_CALLS'], 'a') as log:\n"
            "    log.write(json.dumps(sys.argv[1:]) + '\\n')\n"
            "sys.exit(int(os.environ.get('DOTNET_EXIT', '0')))\n")
        dotnet.chmod(0o755)
        log = output.parent / "dotnet.jsonl"
        env = {**os.environ, "PATH": f"{output.parent}:{os.environ['PATH']}",
               "STS2_DATA_DIR": str(data), "DOTNET_CALLS": str(log), "DOTNET_EXIT": "0"}
        args = ["bash", "-c", body, "--", "--verbosity", "quiet"]
        run = subprocess.run(args, env=env, capture_output=True, text=True)
        self.assertEqual(run.returncode, 0, run.stderr)
        invocations = [json.loads(line) for line in log.read_text().splitlines()]
        self.assertEqual([call[2] for call in invocations], [
            "tests/CorruptedPlayer/CorruptedPlayer.Tests.csproj", "tests/Architect/Architect.Tests.csproj"])
        for call in invocations:
            self.assertIn("-p:Sts2Path=/sts2", call)
            self.assertIn(f"-p:Sts2DataDir={data}", call)
            self.assertEqual(call[-2:], ["--verbosity", "quiet"])
        log.unlink()
        failed = subprocess.run(args, env={**env, "DOTNET_EXIT": "7"}, capture_output=True, text=True)
        self.assertEqual(failed.returncode, 7)
        self.assertEqual(len(log.read_text().splitlines()), 1)
        log.unlink()
        (data / "GodotSharp.dll").unlink()
        missing = subprocess.run(args, env=env, capture_output=True, text=True)
        self.assertNotEqual(missing.returncode, 0)
        self.assertIn("Missing game assembly:", missing.stderr)
        self.assertFalse(log.exists())
