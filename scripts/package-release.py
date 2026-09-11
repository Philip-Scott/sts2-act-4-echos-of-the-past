#!/usr/bin/env python3
"""Build a fresh release and stage GitHub files and an existing Steam Workshop item update."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parent.parent
RUNTIME_FILES = ("TheArchitect.dll", "TheArchitect.json", "TheArchitect.pck")


def workshop_update(manifest):
    item_id = (ROOT / "release/mod_id.txt").read_text().strip()
    if not item_id.isascii() or not item_id.isdecimal() or not 0 < int(item_id) < 2**64:
        raise ValueError("release/mod_id.txt must contain the existing Workshop item ID.")
    config = json.loads((ROOT / "release/workshop.json").read_text())
    if config.get("title") != manifest["name"]:
        raise ValueError("Workshop title must match the in-game mod name.")
    for field in ("description", "visibility"):
        if config.get(field) is not None:
            raise ValueError(f"Workshop updates must omit {field} to preserve the Steam-edited listing.")
    return item_id, config


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/releases")
    parser.add_argument("--builder", choices=("local", "podman"), default="local")
    parser.add_argument("--game", type=Path, help="Game installation; required for Podman unless STS2_PATH is set.")
    args = parser.parse_args()
    game = args.game or (Path(os.environ["STS2_PATH"]) if os.environ.get("STS2_PATH") else None)
    if game is not None:
        game = game.expanduser().resolve(strict=True)
        if not game.is_dir():
            raise ValueError(f"Game installation must be a directory: {game}")
    if args.builder == "podman" and game is None:
        parser.error("--builder podman requires --game or STS2_PATH.")
    subprocess.run(["python3", str(ROOT / "scripts/validate_manifest.py")], check=True)
    manifest = json.loads((ROOT / "TheArchitect.json").read_text(encoding="utf-8-sig"))
    version = manifest["version"].removeprefix("v")
    project_version = ET.parse(ROOT / "TheArchitect.csproj").findtext("./PropertyGroup/Version")
    if version != project_version:
        raise ValueError("Manifest and assembly project versions must match.")
    item_id, config = workshop_update(manifest)
    media = ROOT / "docs/media"
    thumbnail = media / "workshop-thumbnail.png"
    previews = sorted((media / "screenshots").glob("*.jpg"))
    if not thumbnail.is_file() or not previews:
        raise ValueError("Generate the release thumbnail and capture gameplay screenshots first.")
    for image in [thumbnail, *previews]:
        if not 0 < image.stat().st_size < 1_000_000:
            raise ValueError(f"Workshop image must be nonempty and less than 1 MB: {image}")

    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f"v{version}"
    if destination.exists():
        raise FileExistsError(f"Refusing to replace {destination}; preserve any Workshop mod_id.txt.")
    with tempfile.TemporaryDirectory(prefix=".release-", dir=output) as temporary:
        stage = Path(temporary)
        if args.builder == "podman":
            if not stage.is_relative_to(ROOT):
                raise ValueError("Podman output must be inside the repository's mounted directory.")
            command = [
                "podman", "run", "--rm", "--security-opt", "label=disable", "--userns=keep-id",
                "-v", f"{ROOT}:/mod", "-v", f"{game}:/sts2:ro",
                "-v", "sts2-the-architect-build-nuget:/nuget-cache",
                "-e", "HOME=/tmp", "-e", "STS2_PATH=/sts2",
                "-e", f"MODS_PATH=/mod/{stage.relative_to(ROOT).as_posix()}/build/",
                "localhost/sts2-the-architect-build:latest", "--configuration", "Release",
            ]
        else:
            command = [
                "bash", str(ROOT / "scripts/build.sh"), "--configuration", "Release",
                "--mods-path", str(stage / "build"),
            ]
            if game is not None:
                command.extend(["--sts2-path", str(game)])
        subprocess.run(command, cwd=ROOT, check=True)
        built = stage / "build/TheArchitect"
        for name in RUNTIME_FILES:
            path = built / name
            if not path.is_file() or path.stat().st_size == 0:
                raise ValueError(f"Fresh build did not produce {name}.")
        if json.loads((built / "TheArchitect.json").read_text(encoding="utf-8-sig")) != manifest:
            raise ValueError("The build changed the manifest; review dependency versions and retry.")
        if (built / "TheArchitect.dll").read_bytes()[:2] != b"MZ":
            raise ValueError("Built DLL has an invalid PE header.")
        if (built / "TheArchitect.pck").read_bytes()[:4] != b"GDPC":
            raise ValueError("Built PCK has an invalid Godot pack header.")

        bundle = stage / f"v{version}"
        workspace = bundle / "workshop"
        content = workspace / "content"
        content.mkdir(parents=True)
        for name in RUNTIME_FILES:
            shutil.copyfile(built / name, content / name)
        shutil.copyfile(thumbnail, workspace / "image.png")
        (workspace / "previews").mkdir()
        for image in previews:
            shutil.copyfile(image, workspace / "previews" / image.name)
        (workspace / "workshop.json").write_text(json.dumps(config, indent=2) + "\n")
        (workspace / "mod_id.txt").write_text(item_id + "\n")
        for name in ("INSTALL.md", "RELEASE_NOTES.md"):
            shutil.copyfile(ROOT / "release" / name, bundle / name)
        shutil.copyfile(ROOT / "docs/RELEASING.md", bundle / "PUBLISHING.md")
        shutil.copytree(media, bundle / "media")

        archive = bundle / f"TheArchitect-v{version}.zip"
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
            for name in RUNTIME_FILES:
                zipped.write(content / name, f"TheArchitect/{name}")
            zipped.write(bundle / "INSTALL.md", "INSTALL.md")
        checksums = []
        for path in sorted(bundle.rglob("*")):
            if path.is_file():
                checksums.append(f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.relative_to(bundle)}")
        (bundle / "SHA256SUMS").write_text("\n".join(checksums) + "\n")
        bundle.rename(destination)
    print(f"Release staged at {destination}")
    print(f"Nothing uploaded. Prepared update to Workshop item {item_id}; Steam description and visibility remain unchanged.")


if __name__ == "__main__":
    main()
