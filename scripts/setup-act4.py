#!/usr/bin/env python3
"""Compose Act 4 saves from reusable YAML builds. Linux/Steam local playtests only."""

import argparse
import copy
from contextlib import contextmanager
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import random
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid

import yaml


ROOT = Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "artifacts/act4"
SAVE_KEY = "TheArchitect.Run.v1"
RESOURCES = ("current_hp", "max_hp", "gold", "max_energy", "base_orb_slot_count",
             "max_potion_slot_count")
FIELDS = {"schema", "name", "character", "ascension", "cards", "relics", "potions", *RESOURCES}
NATIVE_SPEC = importlib.util.spec_from_file_location("act4_native_demo", ROOT / "scripts/native-demo.py")
NATIVE = importlib.util.module_from_spec(NATIVE_SPEC)
NATIVE_SPEC.loader.exec_module(NATIVE)
BUNDLE_NAMES = tuple(f"TheArchitect.{suffix}" for suffix in ("dll", "pdb", "pck", "json"))


class PlaytestFailure(ValueError):
    def __init__(self, message, category):
        super().__init__(message)
        self.category = category


@contextmanager
def operation_lock():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    with (ARTIFACTS / "launcher.lock").open("a") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise ValueError("Another Act 4 operation is active in this worktree. Do not queue a second launch.") from None
        yield


class Stages:
    def __init__(self, artifact):
        self.artifact = artifact
        self.entries = []

    @contextmanager
    def run(self, name):
        entry = {"Stage": name, "Status": "running", "StartedAt": time.time()}
        self.entries.append(entry)
        start = time.monotonic()
        write_json(self.artifact / "stages.json", self.entries)
        print(f"[{name}] starting. Artifacts: {self.artifact}", flush=True)
        try:
            yield
            entry["Status"] = "ok"
        except BaseException as error:
            entry.update(Status="failed", Error=str(error), ErrorType=type(error).__name__,
                         Category=getattr(error, "category", "operation-error"))
            raise
        finally:
            entry["Seconds"] = round(time.monotonic() - start, 3)
            write_json(self.artifact / "stages.json", self.entries)
            print(f"[{name}] {entry['Status']} ({entry['Seconds']:.1f}s)", flush=True)


def steam_executable(configured=None):
    if configured is not None and not configured.strip():
        raise ValueError("--steam must name an executable.")
    configured = configured or os.environ.get("ARCHITECT_STEAM")
    if configured:
        candidates = [configured]
    else:
        candidates = [str(Path.home() / ".local/share/Steam/ubuntu12_32/steam"),
                      str(Path.home() / ".steam/steam/ubuntu12_32/steam"), "steam"]
    for candidate in candidates:
        found = shutil.which(os.path.expanduser(candidate))
        if found:
            result = str(Path(found).resolve())
            print(f"Steam executable: {result}", flush=True)
            return result
    raise ValueError("No executable Steam client found. Set --steam or ARCHITECT_STEAM to its executable.")


class DeckLoader(yaml.SafeLoader):
    """Reject ambiguous YAML rather than silently accepting the last duplicate key."""


def unique_mapping(loader, node, deep=False):
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if not isinstance(key, str) or key in result:
            raise ValueError(f"YAML keys must be unique strings: {key!r}")
        result[key] = loader.construct_object(value_node, deep=deep)
    return result


DeckLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, unique_mapping)


def integer(value, name, minimum=0, maximum=None):
    if type(value) is not int or value < minimum or maximum is not None and value > maximum:
        raise ValueError(f"{name} must be an integer >= {minimum}" +
                         (f" and <= {maximum}" if maximum is not None else "") + ".")
    return value


def validate_deck(deck, *, resources=True):
    if (not isinstance(deck, dict) or set(deck) != FIELDS or
            type(deck.get("schema")) is not int or deck["schema"] != 1):
        raise ValueError(f"A deck needs schema 1 and exactly these fields: {', '.join(sorted(FIELDS))}.")
    if not isinstance(deck["name"], str) or not deck["name"].strip():
        raise ValueError("A deck needs a name.")
    if not isinstance(deck["character"], str) or not deck["character"].startswith("CHARACTER."):
        raise ValueError("character must be a native CHARACTER.* model ID.")
    integer(deck["ascension"], "ascension", maximum=10)
    for field, prefix in (("cards", "CARD."), ("relics", "RELIC."), ("potions", "POTION.")):
        items = deck[field]
        if not isinstance(items, list) or field == "cards" and not items:
            raise ValueError(f"{field} must be an array, and cards cannot be empty.")
        for item in items:
            if not isinstance(item, dict) or not isinstance(item.get("id"), str) or not item["id"].startswith(prefix):
                raise ValueError(f"Each {field} entry needs an {prefix}* id.")
            if field == "cards":
                integer(item.get("current_upgrade_level", 0), "current_upgrade_level")
    for field in RESOURCES:
        if not resources and field in ("max_energy", "base_orb_slot_count") and deck[field] is None:
            continue
        integer(deck[field], field, 1 if field in ("current_hp", "max_hp", "max_energy") else 0)
    if deck["current_hp"] > deck["max_hp"]:
        raise ValueError("current_hp cannot exceed max_hp.")
    slots = [integer(potion.get("slot_index"), "potion slot_index") for potion in deck["potions"]]
    if len(slots) != len(set(slots)) or any(slot >= deck["max_potion_slot_count"] for slot in slots):
        raise ValueError("Potion slots must be unique and within max_potion_slot_count.")
    # Native entries must remain JSON data, not YAML dates, sets, aliases with cycles, or tagged objects.
    json.dumps(deck, allow_nan=False)
    return deck


def read_deck(path, *, resources=True):
    return validate_deck(yaml.load(Path(path).read_text(), Loader=DeckLoader), resources=resources)


def sha(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def hashes(path):
    return {str(p.relative_to(path)): sha(p) for p in sorted(path.rglob("*")) if p.is_file()}


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode="w", dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        try:
            json.dump(value, stream, indent=2, allow_nan=False)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        except BaseException:
            temporary.unlink(missing_ok=True)
            raise
    try:
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def artifact_directory(label):
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    return Path(tempfile.mkdtemp(prefix=f"{label}-", dir=ARTIFACTS))


def account_root(data_root, account=None):
    steam = Path(data_root).expanduser().resolve() / "steam"
    candidates = [path for path in steam.iterdir() if path.is_dir() and path.name.isdecimal()]
    if account is not None:
        candidates = [path for path in candidates if path.name == account]
    if len(candidates) != 1:
        raise ValueError("Cannot identify one Steam account. Pass --account with an existing account directory.")
    return candidates[0]


def victory_records(account, source_slot):
    records = {}
    for directory in (account / f"profile{source_slot}/saves/history",
                      account / f"modded/profile{source_slot}/saves/history"):
        for path in sorted(directory.glob("*.run")):
            history = json.loads(path.read_text())
            if history.get("win") is not True or history.get("was_abandoned") is not False:
                continue
            if not isinstance(history.get("players"), list) or len(history["players"]) != 1:
                continue
            key = (history["seed"], history["start_time"])
            records.setdefault(key, (path, history))
    return list(records.values())


def deck_from_history(history, name):
    player = history["players"][0]
    stats = [stat for act in history["map_point_history"] for point in act
             for stat in point["player_stats"]][-1]
    deck = {"schema": 1, "name": name, "character": player["character"],
            "ascension": history["ascension"], "cards": copy.deepcopy(player["deck"]),
            "relics": copy.deepcopy(player["relics"]), "potions": copy.deepcopy(player["potions"]),
            "current_hp": stats["current_hp"], "max_hp": stats["max_hp"], "gold": stats["current_gold"],
            "max_potion_slot_count": player["max_potion_slot_count"],
            "max_energy": None, "base_orb_slot_count": None}
    return validate_deck(deck, resources=False)


def capture(args):
    account = account_root(args.data_root, args.account)
    records = victory_records(account, args.source_slot)
    if args.character:
        records = [(source, history) for source, history in records
                   if history["players"][0]["character"] == args.character]
    if len(records) < args.count:
        character = f" for {args.character}" if args.character else ""
        raise ValueError(f"Only {len(records)} distinct single-player victories{character} are available.")
    output = Path(args.output).resolve()
    destinations = [output / f"deck-{index}.yml" for index in range(1, args.count + 1)]
    if any(path.exists() for path in destinations):
        raise ValueError("Capture will not overwrite existing decks. Choose another --output directory.")
    selected = random.SystemRandom().sample(records, args.count)
    artifact = artifact_directory("capture")
    manifest = {"SourceSlot": args.source_slot, "Account": str(account), "Decks": []}
    output.mkdir(parents=True, exist_ok=True)
    for index, ((source, history), destination) in enumerate(zip(selected, destinations), 1):
        frozen = artifact / f"victory-{index}.run"
        shutil.copyfile(source, frozen)
        if sha(source) != sha(frozen):
            raise ValueError("A victory changed during capture; selections and copies are preserved.")
        deck = deck_from_history(json.loads(frozen.read_text()), f"Victory {index}")
        manifest["Decks"].append({"Source": str(source), "Sha256": sha(frozen),
                                  "Seed": history["seed"], "StartTime": history["start_time"],
                                  "Character": deck["character"], "Deck": str(destination)})
        write_json(artifact / "selection.json", manifest)
        destination.write_text(yaml.safe_dump(deck, sort_keys=False, allow_unicode=False))
        print(f"{destination}: {deck['character']}, {len(deck['cards'])} cards, {len(deck['relics'])} relics")
    print(f"Private selection record: {artifact}")
    print("History omits base resources. Review max_energy and base_orb_slot_count before loading.")


def dotnet_json(value):
    """Match System.Text.Json's default encoder for snapshot content hashes."""
    if isinstance(value, str):
        text = json.dumps(value, ensure_ascii=True)
        text = text.replace('\\"', r"\u0022")
        for char in "<>&'+`":
            text = text.replace(char, f"\\u{ord(char):04X}")
        return re.sub(r"\\u([a-fA-F0-9]{4})", lambda match: "\\u" + match[1].upper(), text)
    if isinstance(value, list):
        return "[" + ",".join(dotnet_json(item) for item in value) + "]"
    if isinstance(value, dict):
        return "{" + ",".join(dotnet_json(key) + ":" + dotnet_json(item) for key, item in value.items()) + "}"
    return json.dumps(value, allow_nan=False, separators=(",", ":"))


def snapshot(deck):
    content = deck["character"] + "|" + str(deck["max_hp"]) + "|" + "|".join(map(dotnet_json, deck["cards"]))
    return {"CharacterId": deck["character"], "MaxHp": deck["max_hp"],
            "Deck": copy.deepcopy(deck["cards"]),
            "ContentHash": hashlib.sha256(content.encode()).hexdigest()}


def state_location(save):
    matches = []

    def visit(value):
        if isinstance(value, dict):
            if SAVE_KEY in value:
                matches.append(value)
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(save)
    if len(matches) != 1 or not isinstance(matches[0][SAVE_KEY], str):
        raise ValueError("Native template must contain exactly one serialized Architect run state.")
    return matches[0]


def validate_models(deck, models, *, corrupted=False):
    def visit(value):
        if isinstance(value, dict):
            for key, child in value.items():
                if key == "id" and isinstance(child, str) and child not in models:
                    raise ValueError(f"Model is not installed: {child}. No save was changed.")
                visit(child)
        elif isinstance(value, list):
            for item in value:
                visit(item)
    if deck["character"] not in models:
        raise ValueError(f"Character is not installed: {deck['character']}.")
    visit(deck["cards"])
    if not corrupted:
        visit(deck["relics"])
        visit(deck["potions"])


def compose_save(template, players, opponents, models, *, local_id=1):
    if not 1 <= len(players) <= 4 or not 1 <= len(opponents) <= 4:
        raise ValueError("Choose one to four player decks and one to four corrupted decks.")
    if (template.get("current_act_index") != 3 or
            [player.get("character_id") for player in template.get("players", [])] !=
            [deck["character"] for deck in players]):
        raise ValueError("Expected a fresh native Act 4 template for the selected player characters.")
    for deck in players:
        validate_deck(deck)
        validate_models(deck, models)
    for deck in opponents:
        validate_deck(deck, resources=False)
        validate_models(deck, models, corrupted=True)
    save = copy.deepcopy(template)
    for index, (player, deck) in enumerate(zip(save["players"], players)):
        player.update({field: deck[field] for field in RESOURCES})
        player.update(character_id=deck["character"], deck=copy.deepcopy(deck["cards"]),
                      relics=copy.deepcopy(deck["relics"]), potions=copy.deepcopy(deck["potions"]),
                      net_id=local_id if index == 0 else index + 1)
    owner = state_location(save)
    state = json.loads(owner[SAVE_KEY])
    state.update(Id=uuid.uuid4().hex, Entered=True, EntrySnapshot=None, EntryParty=None,
                 PendingParty=None, Origin=None, StartingHostNetId=None, OriginalNetworkIds=None,
                 Outcome=None, SnapshotRevision=None,
                 PlaytestOpponents=[snapshot(deck) for deck in opponents])
    owner[SAVE_KEY] = json.dumps(state, separators=(",", ":"))
    return save


def game_path(value):
    path = Path(value).expanduser().resolve()
    if (sys.platform != "linux" or not (path / "SlayTheSpire2").is_file() or
            not os.access(path / "SlayTheSpire2", os.X_OK)):
        raise ValueError("This launcher requires an executable Linux Steam install; pass --game if needed.")
    return path


def game_processes(game):
    binary = (game / "SlayTheSpire2").resolve()
    result = []
    for process in Path("/proc").iterdir():
        if not process.name.isdecimal():
            continue
        try:
            if (process / "exe").resolve(strict=True) == binary:
                result.append(int(process.name))
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
    return result


def require_closed(game, stop=False):
    processes = game_processes(game)
    if processes and not stop:
        raise ValueError(f"Close the game first, or explicitly use --stop-game. Game PIDs: {processes}")
    for pid in processes:
        os.kill(pid, signal.SIGTERM)
    deadline = time.monotonic() + 20
    while game_processes(game):
        if time.monotonic() > deadline:
            raise ValueError("The game did not exit. No saves or installed mods were changed.")
        time.sleep(0.2)


def native_command(game, flag, source, label, visible=False):
    command = [sys.executable, str(ROOT / "scripts/native-demo.py"), "run", str(game),
               flag, str(source), "--label", label]
    if visible:
        command += ["--manual", "--shared-visible"]
    return command


def native_environment(artifact):
    environment = os.environ.copy()
    environment["ARCHITECT_MODS_INPUT"] = str(artifact / "mods")
    return environment


def native_run(game, flag, source, artifact, *, visible=False, expected=None):
    label = "act4-" + uuid.uuid4().hex[:12]
    log_path = artifact / (flag.removeprefix("--") + ".log")
    with log_path.open("w") as log:
        process = subprocess.Popen(native_command(game, flag, source, label, visible),
                                   env=native_environment(artifact), stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 180
            run = None
            while process.poll() is None:
                candidates = list((ROOT / "artifacts/native-demo").glob(f"run-*-{label}-*"))
                if len(candidates) == 1:
                    run = candidates[0]
                if visible and run and (run / "ready.json").exists():
                    if expected is not None:
                        verify_receipt(json.loads((run / "ready.json").read_text()), *expected, saved=False)
                    print(f"Fake multiplayer is ready at the Corrupted Player fight. It is not a profile save.\nArtifacts: {run}", flush=True)
                    print((run / "ready.json").read_text(), flush=True)
                    return process.wait()
                if time.monotonic() > deadline:
                    raise PlaytestFailure(f"Native preparation timed out. Inspect {log_path}" +
                                          (f" and {run}/game.log." if run else "."), "native-timeout")
                time.sleep(0.25)
            candidates = list((ROOT / "artifacts/native-demo").glob(f"run-*-{label}-*"))
            if process.returncode != 0 or len(candidates) != 1:
                raise PlaytestFailure(f"Native preparation failed. Inspect {log_path} and "
                                      "its native-demo artifact directory.", "native-process-exit")
            return candidates[0]
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def verify_receipt(receipt, players, opponents, seed, ascension, *, saved, profile=None):
    expected_players = [{"Character": deck["character"], "Cards": len(deck["cards"]),
                         "Relics": len(deck["relics"]), "Hp": deck["current_hp"],
                         "MaxHp": deck["max_hp"], "Gold": deck["gold"],
                         "MaxEnergy": deck["max_energy"], "BaseOrbSlotCount": deck["base_orb_slot_count"]}
                        for deck in players]
    expected_opponents = [{"Character": deck["character"], "Cards": len(deck["cards"]),
                           "MaxHp": deck["max_hp"], "ContentHash": snapshot(deck)["ContentHash"]}
                          for deck in opponents]
    if (not isinstance(receipt, dict) or receipt.get("Saved") is not saved or receipt.get("Profile") != profile or
            receipt.get("Visible") is not True or
            receipt.get("Location") != ("Watcher" if saved else "CorruptedPlayer") or
            receipt.get("Seed") != seed or receipt.get("Ascension") != ascension or
            receipt.get("Players") != expected_players or receipt.get("Opponents") != expected_opponents or
            not isinstance(receipt.get("Offers"), list) or len(receipt["Offers"]) != 3):
        raise PlaytestFailure("Native receipt does not match the requested starting builds. "
                              "Keep the logs and backup.", "receipt-assertion")


def backup_target(account, slot, artifact):
    target = account / f"modded/profile{slot}"
    backup = artifact / "backup"
    backup.mkdir()
    if target.exists():
        shutil.copytree(target, backup / target.name)
    for path in (account / "modded").glob("profile.save*"):
        if path.is_file():
            shutil.copyfile(path, backup / path.name)
    write_json(backup / "location.json", {"Target": str(target)})
    return target


def install_bundle(game, artifact):
    source = artifact / "mods/TheArchitect"
    target = game / "mods/TheArchitect"
    require_closed(game)
    if target.resolve() != game.resolve() / "mods/TheArchitect":
        raise ValueError("Refusing a linked installed mod directory.")
    if any((target / name).is_symlink() or
           (target / name).exists() and (target / name).stat().st_nlink != 1 for name in BUNDLE_NAMES):
        raise ValueError("Refusing linked installed mod files.")
    expected = runtime_hashes(artifact / "mods")
    installed = runtime_hashes(game / "mods")
    if other_mods(expected) != other_mods(installed):
        raise ValueError("Installed dependencies changed since native verification. No mod was installed.")
    if any(name.startswith("TheArchitect/") and name not in expected for name in installed):
        raise ValueError("Unexpected installed Architect runtime files. Resolve them before preparing.")
    names = BUNDLE_NAMES
    for name in names:
        if not (source / name).is_file():
            raise ValueError("Build first with scripts/build.sh --mods-path artifacts/mods.")
    before = {name: sha(source / name) for name in names}
    for suffix in ("dll", "json", "pck"):
        if not (game / f"mods/BaseLib/BaseLib.{suffix}").is_file():
            raise ValueError("The installed BaseLib runtime is incomplete.")
    if target.exists():
        shutil.copytree(target, artifact / "installed-mod-backup")
    target.mkdir(parents=True, exist_ok=True)
    for name in names:
        shutil.copyfile(source / name, target / name)
    if before != {name: sha(source / name) for name in names} or before != {name: sha(target / name) for name in names}:
        raise ValueError("The staged mod changed during installation. Do not launch; keep the backup and rebuild.")


def target_preflight(account, slot, *, replace=False):
    if slot not in (2, 3):
        raise ValueError("Slot 1 is protected.")
    target = account / f"modded/profile{slot}"
    if target.resolve() != account.resolve() / f"modded/profile{slot}" or (target / "saves").is_symlink():
        raise ValueError("Refusing a linked target profile or saves directory; it could alias a protected slot.")
    if any(path.is_symlink() for path in (target / "saves").glob("*")):
        raise ValueError("Refusing linked save files.")
    for name in ("current_run_mp.save", "current_run_mp.save.backup"):
        if (target / "saves" / name).exists():
            raise ValueError("The target has a multiplayer save. This script never replaces multiplayer saves.")
    if any((target / "saves" / name).exists() for name in ("current_run.save", "current_run.save.backup")) and not replace:
        raise ValueError("The target has a solo run. Use --replace to authorize a backed-up replacement.")
    profile_path = account / "modded/profile.save"
    if profile_path.is_symlink():
        raise ValueError("Refusing a linked profile selection.")
    profile = json.loads(profile_path.read_text())
    if not isinstance(profile, dict) or type(profile.get("last_profile_id")) is not int:
        raise ValueError("Unrecognized native profile selection schema.")
    return target, profile_path, profile


def write_profile(account, slot, artifact, game, prepared, *, replace=False):
    if len(prepared.get("players", [])) != 1:
        raise ValueError("Only solo saves may be written, and Slot 1 is protected.")
    require_closed(game)
    target, profile_path, profile = target_preflight(account, slot, replace=replace)
    before_target, before_selection = hashes(target), sha(profile_path)
    protected = [(path, hashes(path)) for path in (account / "profile1", account / "modded/profile1")]
    write_json(artifact / "protected-profiles.json", {str(path): before for path, before in protected})
    backup_target(account, slot, artifact)
    install_bundle(game, artifact)
    require_closed(game)
    target_preflight(account, slot, replace=replace)
    if hashes(target) != before_target or sha(profile_path) != before_selection:
        raise ValueError("The target profile changed during installation. No save was overwritten.")
    live_save = copy.deepcopy(prepared)
    live_save["platform_type"] = "steam"
    for name in ("current_run.save", "current_run.save.backup"):
        write_json(target / "saves" / name, live_save)
    profile["last_profile_id"] = slot
    write_json(profile_path, profile)
    if any(hashes(path) != before for path, before in protected):
        raise ValueError("Slot 1 changed during preparation. Backups and logs are preserved.")
    return target, protected


def runtime_hashes(directory):
    return {str(path.relative_to(directory)): sha(path) for mod in NATIVE.mod_directories(directory)
            for path in sorted(mod.rglob("*")) if path.is_file() and path.suffix in (".dll", ".json", ".pck")}


def other_mods(entries):
    return {key: value for key, value in entries.items() if not key.startswith("TheArchitect/")}


def game_identity(game):
    files = {game / "SlayTheSpire2", *game.glob("*.pck"), *game.rglob("sts2.dll"), *game.rglob("0Harmony.dll")}
    return {str(path.relative_to(game)): sha(path) for path in sorted(files)}


def bundle_preflight(game, *, live):
    mods, base = NATIVE.mods_input(game)
    if live:
        for name in BUNDLE_NAMES:
            if not (mods / "TheArchitect" / name).is_file():
                raise ValueError(f"Missing staged {name}. Build to a separate bundle first.")
        target = game / "mods/TheArchitect"
        if target.resolve() != game.resolve() / "mods/TheArchitect":
            raise ValueError("Refusing a linked installed mod directory.")
        if any((target / name).is_symlink() or
               (target / name).exists() and (target / name).stat().st_nlink != 1 for name in BUNDLE_NAMES):
            raise ValueError("Refusing linked installed mod files.")
        expected = runtime_hashes(mods)
        for suffix in ("dll", "json", "pck"):
            expected[f"BaseLib/BaseLib.{suffix}"] = sha(base / f"BaseLib.{suffix}")
        installed = runtime_hashes(game / "mods")
        if other_mods(expected) != other_mods(installed):
            raise ValueError("Staged dependencies differ from installed mods. Match BaseLib and other "
                             "mods explicitly; this launcher installs only TheArchitect.")
        if any(name.startswith("TheArchitect/") and name not in expected for name in installed):
            raise ValueError("Unexpected installed Architect runtime files. Resolve them before preparing.")
    return mods, base


def freeze_bundle(game, artifact, *, live):
    mods, base = bundle_preflight(game, live=live)
    before = NATIVE.mod_hashes(mods)
    base_before = {f"BaseLib.{suffix}": sha(base / f"BaseLib.{suffix}")
                   for suffix in ("dll", "json", "pck")}
    NATIVE.copy_mods(mods, artifact / "mods")
    frozen_base = artifact / "mods/BaseLib"
    frozen_base.mkdir(exist_ok=True)
    for suffix in ("dll", "json", "pck"):
        shutil.copyfile(base / f"BaseLib.{suffix}", frozen_base / f"BaseLib.{suffix}")
    expected = dict(before)
    expected.update({f"BaseLib/{key}": value for key, value in base_before.items()})
    if (before != NATIVE.mod_hashes(mods) or any(sha(base / name) != digest for name, digest in base_before.items()) or
            hashes(artifact / "mods") != expected):
        raise ValueError("Mod inputs changed while freezing preparation. No profile was changed.")


def sealed_record(artifact, name, value):
    path = artifact / f"{name}.json"
    write_json(path, value)
    write_json(artifact / f"{name}.sha256", sha(path))


def read_record(artifact, name):
    path = artifact / f"{name}.json"
    seal = artifact / f"{name}.sha256"
    if not path.is_file() or not seal.is_file():
        raise ValueError("No versioned recovery record. Legacy load directories cannot be recovered "
                         "automatically; use normal Continue for an already working run, or authorize "
                         "a fresh full preparation. No save was changed.")
    if sha(path) != json.loads(seal.read_text()):
        raise ValueError(f"Damaged {name} record; refusing recovery.")
    record = json.loads(path.read_text())
    if not isinstance(record, dict):
        raise ValueError(f"Invalid {name} record.")
    return record


def live_files(account, slot):
    return {name: sha(account / name) for name in
            (f"modded/profile{slot}/saves/current_run.save",
             f"modded/profile{slot}/saves/current_run.save.backup", "modded/profile.save")}


def save_identity(save):
    state = json.loads(state_location(save)[SAVE_KEY])
    if (save.get("current_act_index") != 3 or len(save.get("players", [])) != 1 or
            not state.get("Id") or not state.get("PlaytestOpponents")):
        raise ValueError("Not a recognized solo Act 4 playtest. Full preparation is required.")
    return {"Id": state["Id"], "Seed": save["rng"]["seed"], "Ascension": save["ascension"],
            "Opponents": state["PlaytestOpponents"], "Schema": save.get("schema_version")}


def record_preparation(artifact, account, slot, game, players, opponents, seed, ascension, *, parent=None):
    immutable = ("verified.save", "native-receipt.json", "inputs.json")
    receipt = json.loads((artifact / "native-receipt.json").read_text())
    verify_receipt(receipt, players, opponents, seed, ascension, saved=False)
    write_json(artifact / "inputs.json", {"Players": players, "Corrupted": opponents,
                                         "Seed": seed, "Ascension": ascension, "Offers": receipt["Offers"]})
    save = json.loads((account / f"modded/profile{slot}/saves/current_run.save").read_text())
    record = {"Version": 1, "Worktree": str(ROOT), "Account": str(account), "Slot": slot,
              "Game": str(game), "GameFiles": game_identity(game), "Identity": save_identity(save),
              "Files": {name: sha(artifact / name) for name in immutable},
              "Mods": hashes(artifact / "mods"),
              "Verification": "ui-only-consent" if parent else "native",
              "ParentPreparation": str(parent) if parent else None}
    sealed_record(artifact, "preparation", record)
    checkpoint(artifact, record, state="prepared")
    return record


def checkpoint(artifact, record, *, state, receipt=None):
    installed = runtime_hashes(Path(record["Game"]) / "mods")
    if installed != runtime_hashes(artifact / "mods"):
        raise ValueError("Installed runtime does not match the frozen preparation. Refusing to record compatibility.")
    sealed_record(artifact, "checkpoint",
                  {"Version": 1, "Preparation": sha(artifact / "preparation.json"),
                   "State": state, "Receipt": receipt,
                   "LiveFiles": live_files(Path(record["Account"]), record["Slot"]),
                   "InstalledMods": installed})


def validate_preparation(artifact):
    record = read_record(artifact, "preparation")
    fields = {"Version", "Worktree", "Account", "Slot", "Game", "GameFiles", "Identity",
              "Files", "Mods", "Verification", "ParentPreparation"}
    if (set(record) != fields or record.get("Version") != 1 or record.get("Worktree") != str(ROOT) or
            not isinstance(record.get("Files"), dict) or not isinstance(record.get("Mods"), dict)):
        raise ValueError("Unsupported preparation version or different worktree. Full preparation is required.")
    if (record.get("Files", {}).keys() != {"verified.save", "native-receipt.json", "inputs.json"} or
            record.get("Verification") not in ("native", "ui-only-consent") or
            not record.get("Mods") or
            any(sha(artifact / name) != digest for name, digest in record["Files"].items()) or
            hashes(artifact / "mods") != record["Mods"]):
        raise ValueError("Preparation artifacts changed. Refusing stale or incompatible recovery.")
    status = read_record(artifact, "checkpoint")
    if (set(status) != {"Version", "Preparation", "State", "Receipt", "LiveFiles", "InstalledMods"} or
            status.get("Version") != 1 or status.get("Preparation") != sha(artifact / "preparation.json") or
            status.get("State") not in ("prepared", "ready")):
        raise ValueError("Invalid preparation checkpoint.")
    account, game, slot = Path(record["Account"]), game_path(record["Game"]), record["Slot"]
    if account_root(account.parent.parent, account.name) != account:
        raise ValueError("The recorded account moved.")
    target, _, profile = target_preflight(account, slot, replace=True)
    if profile["last_profile_id"] != slot:
        raise ValueError("The selected profile changed. Recovery will not change profile selection.")
    if live_files(account, slot) != status["LiveFiles"]:
        raise ValueError("The current save or profile changed, possibly through gameplay. "
                         "Recovery will not reset progress; full preparation requires separate consent.")
    if (game_identity(game) != record["GameFiles"] or
            runtime_hashes(game / "mods") != status["InstalledMods"] or
            runtime_hashes(artifact / "mods") != status["InstalledMods"]):
        raise ValueError("Game or installed mods changed. Full preparation is required.")
    save = json.loads((target / "saves/current_run.save").read_text())
    if save_identity(save) != record["Identity"]:
        raise ValueError("The save identity or persisted opponents changed.")
    inputs = json.loads((artifact / "inputs.json").read_text())
    native_receipt = json.loads((artifact / "native-receipt.json").read_text())
    verify_receipt(native_receipt, inputs["Players"], inputs["Corrupted"],
                   inputs["Seed"], inputs["Ascension"], saved=False)
    if status["State"] == "ready" or status["Receipt"] is not None:
        verify_receipt(status["Receipt"], inputs["Players"], inputs["Corrupted"],
                       inputs["Seed"], inputs["Ascension"], saved=True, profile=slot)
    return record, status, inputs


def check_pending_attempts(artifact, confirmed):
    for path in artifact.glob("attempt-*/dispatch.json"):
        if json.loads(path.read_text()).get("State") != "ready" and not confirmed:
            raise ValueError("An earlier Steam dispatch is unresolved. Inspect its logs and the Steam "
                             "client first. Only after ruling out a delayed launch, use "
                             "--confirm-no-pending-launch. Never queue duplicate launches.")


def dispatch(game, steam, artifact, slot, *, timeout):
    require_closed(game)
    attempt = Path(tempfile.mkdtemp(prefix="attempt-", dir=artifact))
    request = attempt / "launch.json"
    write_json(request, {"ProfileId": slot, "OutputDirectory": str(attempt)})
    command = [steam, "-applaunch", "2868840", "--architect-save-playtest", str(request),
               "--log-file", str(attempt / "game.log")]
    status = {"State": "dispatching", "Command": command, "StartedAt": time.time()}
    write_json(attempt / "dispatch.json", status)
    dispatched_at = time.monotonic()
    with (attempt / "steam.log").open("w") as log:
        try:
            result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=30, check=False)
            status["ExitCode"] = result.returncode
        except subprocess.TimeoutExpired:
            status["DispatchTimedOut"] = True
        except OSError as error:
            status["DispatchError"] = str(error)
    status.update(State="waiting", DispatchSeconds=round(time.monotonic() - dispatched_at, 3))
    write_json(attempt / "dispatch.json", status)
    print(f"Steam dispatch exit={status.get('ExitCode', 'unknown')}, "
          f"timed_out={status.get('DispatchTimedOut', False)}. Output: {attempt / 'steam.log'}", flush=True)
    start = time.monotonic()
    warned = False
    while True:
        evidence = bool(game_processes(game)) or (attempt / "game.log").exists()
        status["StartupEvidence"] = status.get("StartupEvidence", False) or evidence
        if (attempt / "error.txt").exists():
            status.update(State="native-error", Error=(attempt / "error.txt").read_text())
            break
        if (attempt / "ready.json").exists():
            try:
                receipt = json.loads((attempt / "ready.json").read_text())
            except json.JSONDecodeError as error:
                status["ReceiptReadError"] = str(error)
            else:
                status.update(State="receipt-received", ReceiptWaitSeconds=round(time.monotonic() - start, 3))
                write_json(attempt / "dispatch.json", status)
                return attempt, receipt
        elapsed = time.monotonic() - start
        if elapsed >= 20 and not warned:
            print("Game startup detected; waiting for the validated receipt." if status["StartupEvidence"] else
                  "Steam returned but no game startup evidence yet. Waiting; do not dispatch again.", flush=True)
            warned = True
        if elapsed >= timeout:
            status["State"] = "receipt-timeout" if status["StartupEvidence"] else "no-startup-evidence"
            status["Error"] = ("Startup occurred but no receipt arrived." if status["StartupEvidence"] else
                               "Steam dispatch produced no game process or new log, even if its exit code was zero.")
            break
        time.sleep(0.5)
    status["Seconds"] = round(time.monotonic() - start, 3)
    write_json(attempt / "dispatch.json", status)
    raise PlaytestFailure(f"{status['Error']} Inspect {attempt}. No automatic retry or game termination. "
                          f"After ruling out a delayed launch and closing any game, use: "
                          f"python3 scripts/setup-act4.py launch-only '{artifact}' "
                          "--confirm-no-pending-launch --steam /path/to/working/steam", status["State"])


def launch_prepared(artifact, steam, timeout):
    record, previous, inputs = validate_preparation(artifact)
    game, account, slot = Path(record["Game"]), Path(record["Account"]), record["Slot"]
    protected = [(path, hashes(path)) for path in (account / "profile1", account / "modded/profile1")]
    attempt, receipt = dispatch(game, steam, artifact, slot, timeout=timeout)
    try:
        verify_receipt(receipt, inputs["Players"], inputs["Corrupted"], inputs["Seed"],
                       inputs["Ascension"], saved=True, profile=slot)
        if (receipt["Offers"] != inputs["Offers"] or
                previous["Receipt"] and receipt["Offers"] != previous["Receipt"]["Offers"]):
            raise PlaytestFailure("Natural offers changed while resuming the existing playtest.", "receipt-assertion")
        persisted = json.loads((account / f"modded/profile{slot}/saves/current_run.save").read_text())
        if save_identity(persisted) != record["Identity"]:
            raise PlaytestFailure("The native save did not retain the save identity and selected opponents.",
                                  "persisted-save-assertion")
        backup = json.loads((account / f"modded/profile{slot}/saves/current_run.save.backup").read_text())
        if save_identity(backup) != record["Identity"]:
            raise PlaytestFailure("The native backup did not retain the save identity and selected opponents.",
                                  "persisted-save-assertion")
        if any(hashes(path) != before for path, before in protected):
            raise ValueError("Slot 1 changed while the game was starting.")
        checkpoint(artifact, record, state="ready", receipt=receipt)
    except BaseException as error:
        status = json.loads((attempt / "dispatch.json").read_text())
        write_json(attempt / "dispatch.json", {**status, "State": "receipt-invalid", "Error": str(error)})
        raise
    status = json.loads((attempt / "dispatch.json").read_text())
    write_json(attempt / "dispatch.json", {**status, "State": "ready"})
    print(f"Slot {slot} is ready at The Watcher. Artifacts: {artifact}")
    print(json.dumps(receipt, indent=2))


def recover(args):
    artifact = Path(args.preparation).expanduser().resolve()
    if not artifact.is_dir():
        raise ValueError("Preparation directory does not exist.")
    stages = Stages(artifact / ("recovery-" + uuid.uuid4().hex))
    with stages.run("recovery-preflight"):
        steam = steam_executable(args.steam)
        record, status, inputs = validate_preparation(artifact)
        check_pending_attempts(artifact, args.confirm_no_pending_launch)
        game = Path(record["Game"])
        if args.command == "restart-existing-playtest":
            if not args.stop_game or not args.ui_only or not args.unchanged_playtest:
                raise ValueError("Restart requires --stop-game, --ui-only and --unchanged-playtest. "
                                 "Confirm no gameplay since the last receipt, including unsaved progress, "
                                 "and no gameplay, serialization or playtest-hook changes in the build.")
            if status["State"] != "ready":
                raise ValueError("Restart requires a previously validated live receipt.")
            bundle_preflight(game, live=True)
        else:
            require_closed(game)
    if args.command == "restart-existing-playtest":
        destination = artifact_directory("restart")
        stages = Stages(destination)
        with stages.run("freeze-ui-build"):
            freeze_bundle(game, destination, live=True)
            frozen = hashes(destination / "mods")
            if sha(destination / "mods/TheArchitect/TheArchitect.json") != sha(
                    artifact / "mods/TheArchitect/TheArchitect.json"):
                raise ValueError("The mod manifest changed. Full preparation is required, not a UI restart.")
        with stages.run("stop-and-recheck"):
            require_closed(game, stop=True)
            validate_preparation(artifact)
        with stages.run("backup-and-install-ui"):
            account, slot = Path(record["Account"]), record["Slot"]
            if frozen != hashes(destination / "mods"):
                raise ValueError("Frozen UI inputs changed. No mod was installed.")
            backup_target(account, slot, destination)
            shutil.copyfile(account / f"modded/profile{slot}/saves/current_run.save",
                            destination / "verified.save")
            shutil.copyfile(artifact / "native-receipt.json", destination / "native-receipt.json")
            install_bundle(game, destination)
            # Restart never writes either live save or profile selection.
            if live_files(account, slot) != status["LiveFiles"]:
                raise ValueError("The save changed during restart. Do not launch; backups are preserved.")
            new_record = record_preparation(destination, account, slot, game, inputs["Players"],
                                            inputs["Corrupted"], inputs["Seed"], inputs["Ascension"],
                                            parent=artifact)
            checkpoint(destination, new_record, state="prepared", receipt=status["Receipt"])
        artifact = destination
    with stages.run("steam-launch-and-receipt"):
        launch_prepared(artifact, steam, args.ready_timeout)


def load(args):
    artifact = artifact_directory("load")
    stages = Stages(artifact)
    live = not args.fake_multiplayer and not args.verify
    with stages.run("preflight"):
        players = [read_deck(path) for path in args.player]
        opponents = [read_deck(path, resources=False) for path in args.corrupted]
        if not 1 <= len(players) <= 4 or not 1 <= len(opponents) <= 4:
            raise ValueError("Choose one to four builds for each role.")
        if len(players) > 1 and live:
            raise ValueError("Multiple local players require --fake-multiplayer.")
        if args.fake_multiplayer and args.no_launch:
            raise ValueError("--fake-multiplayer is an unsaved runtime; use --no-launch without it to prepare files.")
        game = game_path(args.game)
        steam = steam_executable(args.steam) if live and not args.no_launch else None
        account = account_root(args.data_root, args.account) if live else None
        if live:
            target_preflight(account, args.slot, replace=args.replace)
        bundle_preflight(game, live=live)
        NATIVE.private_preflight()
        if not args.verify and not args.stop_game:
            require_closed(game)
    with stages.run("freeze-inputs"):
        freeze_bundle(game, artifact, live=live)
        frozen = hashes(artifact / "mods")
        original_game = game_identity(game)
    if not args.verify:
        with stages.run("close-game"):
            require_closed(game, args.stop_game)
            if live:
                target_preflight(account, args.slot, replace=args.replace)
                target_before = hashes(account / f"modded/profile{args.slot}")
                selection_before = sha(account / "modded/profile.save")
    seed = args.seed or uuid.uuid4().hex
    ascension = players[0]["ascension"] if args.ascension is None else args.ascension
    with stages.run("native-template"):
        request = artifact / "template-request.json"
        write_json(request, {"Seed": seed, "Ascension": ascension,
                             "Characters": [deck["character"] for deck in players]})
        native = native_run(game, "--save-template", request, artifact)
        template = json.loads((native / "template.save").read_text())
        models = set(json.loads((native / "models.json").read_text()))
        shutil.copyfile(native / "template.save", artifact / "template.save")
    with stages.run("compose"):
        prepared = compose_save(template, players, opponents, models)
        save_path = artifact / "prepared.save"
        write_json(save_path, prepared)
        write_json(artifact / "request.json", {"Players": [str(Path(path).resolve()) for path in args.player],
                                             "Corrupted": [str(Path(path).resolve()) for path in args.corrupted],
                                             "Seed": seed, "Ascension": ascension})
    if args.fake_multiplayer:
        with stages.run("visible-fixture"):
            result = native_run(game, "--save-input", save_path, artifact, visible=True,
                                expected=(players, opponents, seed, ascension))
            if result:
                raise ValueError(f"Fake multiplayer exited with code {result}. Inspect {artifact}.")
        return
    with stages.run("native-verification"):
        verified = native_run(game, "--save-input", save_path, artifact)
        if not (verified / "verified.json").exists():
            raise PlaytestFailure(f"The native save/encounter check did not finish. Inspect {verified}/game.log.",
                                  "native-receipt-missing")
        receipt = json.loads((verified / "ready.json").read_text())
        verify_receipt(receipt, players, opponents, seed, ascension, saved=False)
        shutil.copyfile(verified / "verified.json", artifact / "verified.save")
        shutil.copyfile(verified / "ready.json", artifact / "native-receipt.json")
        if frozen != hashes(artifact / "mods") or original_game != game_identity(game):
            raise ValueError("Frozen inputs or game changed during verification. No profile was overwritten.")
    if args.verify:
        print(f"Verified in a disposable game: {verified}")
        return
    with stages.run("backup-and-write-profile"):
        bundle_preflight(game, live=True)
        if (target_before != hashes(account / f"modded/profile{args.slot}") or
                selection_before != sha(account / "modded/profile.save")):
            raise ValueError("The target profile changed during preparation. No profile was overwritten.")
        validated_save = json.loads((artifact / "verified.save").read_text())
        write_profile(account, args.slot, artifact, game, validated_save, replace=args.replace)
        record_preparation(artifact, account, args.slot, game, players, opponents, seed, ascension)
    if args.no_launch:
        print(f"Prepared Slot {args.slot}; live game not launched. Backup: {artifact / 'backup'}\n"
              f"Launch without preparing again: python3 scripts/setup-act4.py launch-only '{artifact}'")
        return
    with stages.run("steam-launch-and-receipt"):
        launch_prepared(artifact, steam, args.ready_timeout)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    capture_parser = commands.add_parser("capture", help="Export distinct random victories as reusable deck YAML.")
    capture_parser.add_argument("--count", type=int, default=3)
    capture_parser.add_argument("--source-slot", type=int, choices=(1, 2, 3), default=1)
    capture_parser.add_argument("--output", default=str(ROOT / "decks"))
    capture_parser.add_argument("--character", help="Only sample victories for this native CHARACTER.* model ID.")
    load_parser = commands.add_parser("load", help="Compose, back up, overwrite, and launch a local Act 4 run.")
    load_parser.add_argument("--player", nargs="+", required=True, metavar="DECK.yml")
    load_parser.add_argument("--corrupted", nargs="+", required=True, metavar="DECK.yml")
    load_parser.add_argument("--slot", type=int, choices=(2, 3), default=2,
                             help="Target modded profile. Slot 1 is always protected.")
    load_parser.add_argument("--game", default=os.environ.get("STS2_PATH", str(
        Path.home() / ".local/share/Steam/steamapps/common/Slay the Spire 2")))
    load_parser.add_argument("--replace", action="store_true", help="Authorize replacement of the target solo run.")
    load_parser.add_argument("--stop-game", action="store_true", help="Authorize closing the identified game process.")
    load_parser.add_argument("--fake-multiplayer", action="store_true", help="Use the existing unsaved local fixture.")
    load_parser.add_argument("--verify", action="store_true", help="Check loading and combat privately; no profile writes.")
    load_parser.add_argument("--no-launch", action="store_true", help="Write the solo save without launching the live game.")
    load_parser.add_argument("--seed")
    load_parser.add_argument("--ascension", type=int, choices=range(11))
    recovery_parsers = []
    for name in ("launch-only", "restart-existing-playtest"):
        command = commands.add_parser(name, help="Resume a versioned preparation without resetting its save.")
        command.add_argument("preparation", help="Existing load-* or restart-* artifact directory.")
        command.add_argument("--confirm-no-pending-launch", action="store_true",
                             help="Confirm an earlier failed Steam dispatch cannot still start the game.")
        if name == "restart-existing-playtest":
            command.add_argument("--stop-game", action="store_true", help="Authorize stopping the current game.")
            command.add_argument("--ui-only", action="store_true",
                                 help="Attest no gameplay, save schema or playtest-hook changes in the staged build.")
            command.add_argument("--unchanged-playtest", action="store_true",
                                 help="Attest no gameplay or unsaved progress since the last validated receipt.")
        recovery_parsers.append(command)
    for command in (load_parser, *recovery_parsers):
        command.add_argument("--steam", help="Steam executable. Overrides ARCHITECT_STEAM and automatic discovery.")
        command.add_argument("--ready-timeout", type=int, default=240,
                             help="Seconds to wait for a live receipt after dispatch (minimum 30; never retries).")
    for command in (capture_parser, load_parser):
        command.add_argument("--data-root", default=str(Path.home() / ".local/share/SlayTheSpire2"))
        command.add_argument("--account", help="Required only when multiple Steam accounts exist.")
    args = parser.parse_args()
    if args.command == "capture":
        integer(args.count, "count", 1)
        capture(args)
    elif args.command == "load":
        if args.verify and (args.fake_multiplayer or args.no_launch or args.replace or args.stop_game):
            parser.error("--verify cannot be combined with live-write or visible-run options.")
        if args.seed is not None and not args.seed.strip():
            parser.error("--seed must not be empty.")
        integer(args.ready_timeout, "ready-timeout", 30)
        with operation_lock():
            load(args)
    else:
        integer(args.ready_timeout, "ready-timeout", 30)
        with operation_lock():
            recover(args)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, yaml.YAMLError, subprocess.SubprocessError) as error:
        print(f"setup-act4: {error}", file=sys.stderr)
        sys.exit(1)
