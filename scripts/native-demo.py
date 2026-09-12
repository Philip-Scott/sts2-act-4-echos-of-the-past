#!/usr/bin/env python3
"""Worktree-local native playtests with private or explicitly shared-visible X11."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time


ROOT = Path(__file__).resolve().parent.parent
RUNS = ROOT / "artifacts/native-demo"
CACHE_PATHS = ("cache/mesa_shader_cache", "xdg/SlayTheSpire2/shader_cache")


def required_file(path):
    path = Path(path).expanduser().resolve(strict=True)
    if not path.is_file():
        raise ValueError(f"Not a file: {path}")
    return path


def tool(name):
    found = shutil.which(name)
    if not found:
        raise ValueError(f"Missing {name}; see native Corrupted Player README dependency setup.")
    return str(Path(found).resolve())


def hashes(directory):
    return {str(p.relative_to(directory)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(directory.rglob("*")) if p.is_file()}


def write_json(path, value):
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.replace(path)


def owned_run(name):
    if not re.fullmatch(r"run-[a-zA-Z0-9_-]+", name):
        raise ValueError("Use the exact run-* ID printed by this worktree's launcher, not a path.")
    run = RUNS / name
    if run.is_symlink() or run.resolve().parent != RUNS.resolve():
        raise ValueError("Run must belong to this worktree.")
    metadata = json.loads((run / "run.json").read_text())
    if not isinstance(metadata, dict):
        raise ValueError(f"Invalid run receipt: {run}/run.json")
    if metadata.get("worktree") != str(ROOT):
        raise ValueError("Run belongs to a different worktree.")
    if metadata.get("state") not in ("preparing", "starting", "running", "stopping", "exited"):
        raise ValueError(f"Invalid run state: {run}/run.json")
    return run, metadata


def stop_child(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()


def seed_shader_cache(run, game, requested=None, cold=False):
    if cold:
        return None
    if requested:
        source, metadata = owned_run(requested)
        candidates = [(source, metadata)]
    else:
        candidates = []
        for receipt in sorted(RUNS.glob("run-*/run.json"), key=lambda p: p.stat().st_mtime, reverse=True):
            source, metadata = owned_run(receipt.parent.name)
            candidates.append((source, metadata))
    for source, metadata in candidates:
        compatible = (metadata["state"] == "exited" and metadata.get("exit_code") == 0
                      and metadata["game"] == str(game)
                      and metadata.get("display") == ":0 (private namespace)")
        if not compatible:
            if requested:
                raise ValueError("--cache-from requires a completed virtual run of this game in this worktree.")
            continue
        copied = False
        for relative in CACHE_PATHS:
            directory = source / relative
            if directory.is_dir():
                shutil.copytree(directory, run / relative)
                copied = True
        if copied:
            return source.name
        if requested:
            raise ValueError(f"{source.name} has no shader caches to copy.")
    return None


def local_display_socket(display):
    match = re.fullmatch(r":([0-9]+)(?:\.[0-9]+)?", display)
    if not match:
        raise ValueError("--shared-visible requires a local DISPLAY such as :0 (no TCP).")
    return Path("/tmp/.X11-unix") / ("X" + match.group(1))


def sandbox_command(game, run, xvfb, render_threads=4, shared_display=None, render_device=None):
    # Hide host homes, Steam/desktop sockets, and sibling run control sockets.
    command = [tool("bwrap"), "--die-with-parent", "--unshare-pid", "--unshare-net",
               "--unshare-ipc", "--unshare-uts", "--ro-bind", "/", "/"]
    homes = {str(Path(path).resolve()) for path in ("/home", "/var/home", "/root", Path.home())
             if Path(path).exists()}
    for home in sorted(homes):
        command += ["--tmpfs", home]
    command += ["--tmpfs", "/run", "--bind", str(run / "tmp"), "/tmp",
               "--ro-bind", str(ROOT), str(ROOT), "--tmpfs", str(RUNS),
               "--ro-bind", str(game), str(game),
               "--bind", str(run), str(run),
               "--ro-bind", str(run / "mods"), str(run / "mods"),
               "--ro-bind", str(run / "mods"), str(game / "mods"),
               "--dev", "/dev", "--proc", "/proc", "--clearenv"]
    if shared_display:
        display_socket = str(local_display_socket(shared_display))
        command += ["--ro-bind", display_socket, display_socket,
                    "--ro-bind", str(run / "Xauthority"), str(run / "Xauthority"),
                    "--dev-bind", render_device, render_device]
    else:
        command += ["--ro-bind", xvfb, str(run / "Xvfb")]
    environment = {
        "PATH": "/usr/bin:/bin", "LANG": "C.UTF-8", "HOME": str(run / "home"),
        "XDG_DATA_HOME": str(run / "xdg"), "XDG_CONFIG_HOME": str(run / "config"),
        "XDG_CACHE_HOME": str(run / "cache"), "XDG_RUNTIME_DIR": str(run / "tmp"),
        "TMPDIR": str(run / "tmp"),
        "__GLX_VENDOR_LIBRARY_NAME": "mesa",
        "__EGL_VENDOR_LIBRARY_FILENAMES": "/usr/share/glvnd/egl_vendor.d/50_mesa.json",
        "ALSA_CONFIG_PATH": str(ROOT / "scripts/native-demo-alsa.conf"),
        "ARCHITECT_NATIVE_RUNTIME": str(run),
    }
    if shared_display:
        environment.update(DISPLAY=shared_display, XAUTHORITY=str(run / "Xauthority"))
    else:
        environment.update(LIBGL_ALWAYS_SOFTWARE="1", LP_NUM_THREADS=str(render_threads))
    for key, value in environment.items():
        command += ["--setenv", key, value]
    return command + ["--chdir", str(run), tool("python3"), str(Path(__file__).resolve()),
                      "_serve", run.name]


def launch(args):
    if args.manual and (not args.shared_visible or args.scenario not in ("party-1", "party-2", "party-3", "party-4")):
        raise ValueError("--manual requires --shared-visible and --party-1, --party-2, --party-3 or --party-4.")
    if args.scenario == "party-1" and not args.manual:
        raise ValueError("--party-1 requires --manual.")
    if args.layout_only and (args.manual or args.scenario not in ("party-2", "party-3", "party-4")):
        raise ValueError("--layout-only requires --party-2, --party-3 or --party-4 without --manual.")
    game = required_file(Path(args.game) / "SlayTheSpire2").parent
    requires_snapshot = args.scenario not in ("ancient", "saved-run", "relic-art", "party", "party-1", "party-2", "party-3", "party-4", "party-layout", "party-layout-prototype", "attack-vfx", "deck-preview", "ending", "corruption")
    if requires_snapshot and not os.environ.get("ARCHITECT_SNAPSHOT_INPUT"):
        raise ValueError("Set ARCHITECT_SNAPSHOT_INPUT to the captured snapshot to copy (never modified).")
    snapshot = required_file(os.environ["ARCHITECT_SNAPSHOT_INPUT"]) if (
        requires_snapshot or args.scenario == "deck-preview" and os.environ.get("ARCHITECT_SNAPSHOT_INPUT")
    ) else None
    run_save = None
    if args.scenario == "saved-run":
        if not os.environ.get("ARCHITECT_RUN_INPUT"):
            raise ValueError("Set ARCHITECT_RUN_INPUT to the saved run to copy (never modified).")
        run_save = required_file(os.environ["ARCHITECT_RUN_INPUT"])
    shared_display = os.environ.get("DISPLAY", "") if args.shared_visible else None
    if args.shared_visible:
        if not local_display_socket(shared_display).is_socket():
            raise ValueError("Host X11 socket is unavailable; set DISPLAY to a running local X11 server.")
        if not os.environ.get("XAUTHORITY"):
            raise ValueError("Set XAUTHORITY to the host X11 authorization file for --shared-visible.")
        authority = required_file(os.environ["XAUTHORITY"])
        if not re.fullmatch(r"/dev/dri/renderD[0-9]+", args.render_device) or not Path(args.render_device).is_char_device():
            raise ValueError("--render-device must name an available /dev/dri/renderD* GPU render node.")
        if args.cache_from:
            raise ValueError("--cache-from currently supports private software displays only; omit it for shared-visible.")
    xvfb = None if args.shared_visible else tool(os.environ.get("ARCHITECT_XVFB", "Xvfb"))
    for name in (("bwrap", "xdpyinfo") if args.shared_visible else
                 ("bwrap", "xauth", "xdpyinfo", "xdotool", "magick")):
        tool(name)
    required_file("/usr/share/glvnd/egl_vendor.d/50_mesa.json")
    mods = Path(os.environ.get("ARCHITECT_MODS_INPUT", ROOT / "artifacts/mods")).resolve()
    required_file(mods / "TheArchitect/TheArchitect.dll")
    RUNS.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix=f"run-{time.strftime('%Y%m%d-%H%M%S')}-{args.label}-",
                               dir=RUNS))
    metadata = {"id": run.name, "worktree": str(ROOT), "game": str(game),
                "label": args.label, "scenario": args.scenario, "state": "preparing",
                "manual": args.manual,
                "layout_only": args.layout_only,
                "render_threads": None if args.shared_visible else args.render_threads,
                "display_mode": "shared-visible" if args.shared_visible else "virtual",
                "host_display": shared_display, "resolution": args.resolution,
                "render_device": args.render_device if args.shared_visible else None}
    write_json(run / "run.json", metadata)
    process = None
    try:
        before = hashes(mods)
        shutil.copytree(mods, run / "mods")
        if hashes(mods) != before or hashes(run / "mods") != before:
            raise ValueError("Mods changed while copying; finish the build before launching.")
        if "ARCHITECT_MODS_INPUT" not in os.environ:
            base = run / "mods/BaseLib"
            base.mkdir(exist_ok=True)
            for suffix in ("dll", "json", "pck"):
                shutil.copyfile(required_file(game / f"mods/BaseLib/BaseLib.{suffix}"),
                                base / f"BaseLib.{suffix}")
        for mod in ("BaseLib", "TheArchitect"):
            for suffix in ("dll", "json", "pck"):
                required_file(run / f"mods/{mod}/{mod}.{suffix}")
        for directory in ("home", "xdg", "tmp", "config", "cache", "captures"):
            (run / directory).mkdir(mode=0o700)
        (run / "Xvfb").touch()
        (run / "tmp/.X11-unix").mkdir(mode=0o700)
        if args.shared_visible:
            shutil.copyfile(authority, run / "Xauthority")
            os.chmod(run / "Xauthority", 0o600)
        metadata["cache_source"] = seed_shader_cache(run, game, args.cache_from,
                                                    args.cold or args.shared_visible)
        settings = run / "xdg/SlayTheSpire2/default/1"
        settings.mkdir(parents=True)
        capture_settings = json.loads((ROOT / "scripts/native-demo-settings.json").read_text())
        width, height = map(int, args.resolution.split("x"))
        capture_settings["window_size"] = {"x": width, "y": height}
        write_json(settings / "settings.save", capture_settings)
        if snapshot is not None:
            shutil.copyfile(snapshot, run / "snapshot-input.json")
        if run_save is not None:
            shutil.copyfile(run_save, run / "run-input.json")
            metadata["run_save_sha256"] = hashlib.sha256((run / "run-input.json").read_bytes()).hexdigest()
        metadata.update(mods=hashes(run / "mods"),
                        snapshot_sha256=hashlib.sha256((run / "snapshot-input.json").read_bytes()).hexdigest()
                        if snapshot is not None else None,
                        state="starting", started_at=time.time())
        write_json(run / "run.json", metadata)
        print(f"Isolated native demo: {run.name}\nArtifacts: {run}", flush=True)
        if args.shared_visible:
            print("Shared desktop: windows may affect focus at startup. External input controls are disabled.", flush=True)
        with (run / "launcher.log").open("w") as log:
            process = subprocess.Popen(sandbox_command(game, run, xvfb, args.render_threads,
                                                      shared_display, args.render_device), stdout=log,
                                       stderr=subprocess.STDOUT)
            def interrupt(signum, _frame):
                stop_child(process)
                raise SystemExit(128 + signum)
            signal.signal(signal.SIGTERM, interrupt)
            signal.signal(signal.SIGINT, interrupt)
            result = process.wait()
        if result:
            print(f"Native demo exited {result}; inspect {run}/launcher.log and game.log", file=sys.stderr)
        return result if result >= 0 else 128 - result
    finally:
        stop_child(process)
        metadata = json.loads((run / "run.json").read_text())
        metadata.update(state="exited", finished_at=time.time(),
                        exit_code=process.returncode if process else None)
        write_json(run / "run.json", metadata)
        (run / "control.sock").unlink(missing_ok=True)


def receive(connection):
    data = b""
    while not data.endswith(b"\n"):
        part = connection.recv(4096)
        if not part:
            raise ValueError("Incomplete control message.")
        data += part
        if len(data) > 8192:
            raise ValueError("Control message too large.")
    return json.loads(data)


def focus_game(game, resolution):
    width, height = map(int, resolution.split("x"))
    deadline = time.monotonic() + 30
    while game.poll() is None and time.monotonic() < deadline:
        windows = subprocess.run(["xdotool", "search", "--onlyvisible", "--pid", str(game.pid)],
                                 capture_output=True, text=True, timeout=5)
        if windows.returncode == 0 and windows.stdout.strip():
            window = windows.stdout.splitlines()[-1]
            subprocess.run(["xdotool", "windowfocus", "--sync", window],
                           check=True, timeout=5)
            subprocess.run(["xdotool", "mousemove", str(width - 10), str(height - 10)],
                           check=True, timeout=5)
            return window
        if windows.returncode not in (0, 1):
            raise ValueError(f"Cannot find private game window: {windows.stderr}")
        time.sleep(0.1)
    raise ValueError("Game did not open a private X11 window within 30 seconds; see game.log.")


def perform(request, run, metadata):
    if not isinstance(request, dict):
        raise ValueError("Control message must be an object.")
    action = request.get("action")
    shared_visible = metadata.get("display_mode") == "shared-visible"
    if shared_visible and action in ("pointer", "key", "click"):
        raise ValueError("Desktop input controls are disabled in shared-visible mode; use in-process native tests.")
    if action == "status" or action == "stop":
        result = {**metadata, "state": "stopping" if action == "stop" else "running"}
        if action == "status" and shared_visible:
            receipt = run / "telemetry.json"
            result["telemetry"] = json.loads(receipt.read_text()) if receipt.exists() else None
        elif action == "status":
            result["pointer"] = subprocess.run(["xdotool", "getmouselocation", "--shell"],
                                              check=True, capture_output=True, text=True,
                                              timeout=5).stdout.strip()
        return result
    if action == "capture":
        name = f"capture-{time.time_ns()}"
        if shared_visible:
            temporary = run / "capture-request.tmp"
            temporary.write_text(name)
            temporary.replace(run / "capture-request")
            deadline = time.monotonic() + 15
            while not (run / "captures" / (name + ".ready")).exists():
                if time.monotonic() > deadline:
                    raise ValueError("Native viewport capture timed out; rebuild the mod and inspect game.log.")
                time.sleep(0.05)
            return {"capture": str(run / "captures" / (name + ".png"))}
        name += ".png"
        subprocess.run(["magick", "import", "-window", "root", str(run / "captures" / name)],
                       check=True, timeout=15, stdout=subprocess.DEVNULL)
        return {"capture": str(run / "captures" / name)}
    if action == "pointer":
        x, y = request.get("x"), request.get("y")
        resolution = metadata.get("resolution", "1280x720")
        width, height = map(int, resolution.split("x"))
        if type(x) is not int or type(y) is not int or not (0 <= x < width and 0 <= y < height):
            raise ValueError(f"Pointer coordinates must be inside {resolution}.")
        command = ["xdotool", "mousemove", str(x), str(y)]
    elif action == "key":
        key = request.get("key", "")
        if not isinstance(key, str) or not re.fullmatch(r"[A-Za-z0-9_+]+", key):
            raise ValueError("Key must be a keysym or combination such as Escape or ctrl+a.")
        command = ["xdotool", "key", "--clearmodifiers", key]
    elif action == "click":
        button = request.get("button")
        if type(button) is not int or not 1 <= button <= 3:
            raise ValueError("Mouse button must be 1, 2, or 3.")
        command = ["xdotool", "click", str(button)]
    else:
        raise ValueError(f"Unknown control action: {action}")
    subprocess.run(command, check=True, timeout=5, stdout=subprocess.DEVNULL)
    return {"ok": True}


def serve(args):
    run, metadata = owned_run(args.run)
    # A host display is permitted only by the explicit shared-visible launch mode.
    if os.environ.get("ARCHITECT_NATIVE_RUNTIME") != str(run) or Path("/proc/1/comm").read_text().strip() != "bwrap":
        raise ValueError("_serve is internal; use run to enter the private PID/display sandbox.")
    os.chdir(run)
    shared_visible = metadata.get("display_mode") == "shared-visible"
    resolution = metadata.get("resolution", "1280x720")
    if not shared_visible:
        os.environ["DISPLAY"] = ":0"
        os.environ["XAUTHORITY"] = str(run / "Xauthority")
        subprocess.run(["xauth", "-f", os.environ["XAUTHORITY"], "add", ":0", ".",
                        os.urandom(16).hex()], check=True)
    elif os.environ.get("DISPLAY") != metadata["host_display"]:
        raise ValueError("Shared-visible display does not match its launch receipt.")
    xvfb = game = None
    def interrupt(signum, _frame):
        raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGINT, interrupt)
    try:
        if not shared_visible:
            with (run / "display.log").open("w") as display_log:
                xvfb = subprocess.Popen([str(run / "Xvfb"), ":0", "-screen", "0", resolution + "x24",
                                         "-nolisten", "tcp", "-auth", os.environ["XAUTHORITY"],
                                         "-noreset"], stdout=display_log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 15
        while subprocess.run(["xdpyinfo"], stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL, timeout=2).returncode:
            if (xvfb is not None and xvfb.poll() is not None) or time.monotonic() > deadline:
                raise ValueError(f"X11 display is unavailable; inspect {run}/launcher.log and display.log.")
            time.sleep(0.1)
        command = [str(Path(metadata["game"]) / "SlayTheSpire2"),
                   "--display-driver", "x11", "--rendering-method", "gl_compatibility",
                   "--windowed", "--resolution", resolution, "--audio-driver", "Dummy",
                   "--max-fps", "30",
                   "--log-file", str(run / "game.log"), "--force-steam", "off",
                   "--architect-native-test"]
        if metadata["scenario"] == "corruption":
            command.append("--architect-corruption-visuals")
        elif metadata["scenario"] != "default":
            command.append("--architect-native-" + metadata["scenario"])
        if resolution == "1920x1080":
            command.append("--architect-native-1080p")
        if shared_visible:
            command.append("--architect-shared-visible")
        if metadata.get("manual"):
            command.append("--architect-native-party-manual")
        if metadata.get("layout_only"):
            command.append("--architect-native-party-layout")
        game = subprocess.Popen(command, cwd=metadata["game"])
        metadata.update(state="running", display=metadata["host_display"] if shared_visible else ":0 (private namespace)",
                        game_pid=game.pid, display_pid=xvfb.pid if xvfb else None,
                        window=None if shared_visible else focus_game(game, resolution),
                        namespaces={name: os.readlink(f"/proc/self/ns/{name}")
                                    for name in ("pid", "net", "ipc", "mnt")})
        write_json(run / "run.json", metadata)
        print(f"NATIVE INSTANCE {json.dumps(metadata)}", flush=True)
        with socket.socket(socket.AF_UNIX) as server:
            # Relative addresses avoid AF_UNIX's 108-byte limit in long worktree paths.
            server.bind("control.sock")
            os.chmod("control.sock", 0o600)
            server.listen(4)
            server.settimeout(0.25)
            while game.poll() is None:
                if xvfb is not None and xvfb.poll() is not None:
                    raise ValueError("Private display exited; see display.log.")
                try:
                    connection, _ = server.accept()
                except socket.timeout:
                    continue
                with connection:
                    connection.settimeout(20)
                    try:
                        request = receive(connection)
                        response = perform(request, run, metadata)
                    except (ValueError, OSError, subprocess.SubprocessError) as error:
                        response = {"error": str(error)}
                        request = {}
                        print(f"Control error: {error}", file=sys.stderr, flush=True)
                    try:
                        connection.sendall(json.dumps(response).encode() + b"\n")
                    except (BrokenPipeError, ConnectionResetError):
                        print("Control client disconnected before response.", file=sys.stderr, flush=True)
                    if request.get("action") == "stop":
                        metadata.update(stop_requested=True, state="stopping")
                        write_json(run / "run.json", metadata)
                        return 0
        return game.returncode
    finally:
        stop_child(game)
        stop_child(xvfb)
        (run / "control.sock").unlink(missing_ok=True)


def control(args):
    run, metadata = owned_run(args.run)
    if args.action == "status" and metadata["state"] in ("exited", "stopping"):
        print(json.dumps(metadata, indent=2))
        return 0
    os.chdir(run)
    with socket.socket(socket.AF_UNIX) as client:
        client.settimeout(25)
        try:
            client.connect("control.sock")
        except (FileNotFoundError, ConnectionRefusedError) as error:
            raise ValueError(f"{run.name} is not controllable ({metadata['state']}); inspect launcher.log.") from error
        client.sendall(json.dumps(vars(args)).encode() + b"\n")
        response = receive(client)
    if "error" in response:
        raise ValueError(response["error"])
    if args.action == "stop":
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            response = json.loads((run / "run.json").read_text())
            if response["state"] == "exited":
                break
            time.sleep(0.1)
        else:
            raise ValueError(f"{run.name}: stop acknowledged but teardown did not finish; inspect launcher.log.")
    print(json.dumps(response, indent=2))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    run = commands.add_parser("run", help="Run in foreground; Ctrl-C stops only this instance.")
    run.add_argument("game")
    run.add_argument("--label", default="native")
    run.add_argument("--resolution", choices=("1280x720", "1920x1080"), default="1280x720",
                     help="Game and private-display size; use 1920x1080 for release screenshots.")
    run.add_argument("--shared-visible", action="store_true",
                     help="Opt in to GPU windows on the shared desktop; no external input automation.")
    run.add_argument("--manual", action="store_true",
                     help="Leave a shared-visible --party-1/2/3/4 fight open with GUI input enabled.")
    run.add_argument("--layout-only", action="store_true",
                     help="Check a --party-2/3/4 inspection layout and card hover, then exit without playing turns.")
    run.add_argument("--render-device", default="/dev/dri/renderD128",
                     help="Mesa GPU render node for --shared-visible (default: /dev/dri/renderD128).")
    run.add_argument("--render-threads", type=int, choices=range(1, 17), default=4,
                     help="Private-display software-renderer threads per instance (default: 4).")
    cache = run.add_mutually_exclusive_group()
    cache.add_argument("--cache-from", help="Copy shader caches from this completed run ID.")
    cache.add_argument("--cold", action="store_true", help="Do not seed shader caches from a completed run.")
    scenarios = run.add_mutually_exclusive_group()
    for scenario in ("loss", "nondefect", "poison", "ancient", "saved-run", "relic-art", "previews", "media", "party", "party-1", "party-2", "party-3", "party-4", "party-layout", "party-layout-prototype", "attack-vfx", "deck-preview", "ending", "corruption"):
        scenarios.add_argument("--" + scenario, dest="scenario", action="store_const", const=scenario)
    run.set_defaults(scenario="default")
    for action in ("status", "capture", "stop", "pointer", "click", "key", "_serve"):
        command = commands.add_parser(action, help="Internal sandbox entry point." if action == "_serve" else action)
        command.add_argument("run", help="Exact run-* ID from this worktree.")
        if action == "pointer":
            command.add_argument("x", type=int)
            command.add_argument("y", type=int)
        elif action == "click":
            command.add_argument("button", type=int, choices=(1, 2, 3))
        elif action == "key":
            command.add_argument("key")
    args = parser.parse_args()
    if args.action == "run":
        if not re.fullmatch(r"[a-zA-Z0-9_-]{1,40}", args.label):
            parser.error("--label must be 1-40 letters, digits, underscores or hyphens.")
        return launch(args)
    return serve(args) if args.action == "_serve" else control(args)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError, subprocess.SubprocessError) as error:
        print(f"native-demo: {error}", file=sys.stderr)
        sys.exit(1)
