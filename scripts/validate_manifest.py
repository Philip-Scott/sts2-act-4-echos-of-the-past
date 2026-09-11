#!/usr/bin/env python3
"""Validates TheArchitect.json, the mod manifest read by Slay the Spire 2.

Run with `python3 scripts/validate_manifest.py`; used by the build workflow so a broken
manifest is caught even when the game assemblies are unavailable for compilation.
"""

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

MANIFEST = Path(__file__).resolve().parent.parent / "TheArchitect.json"

REQUIRED_STRINGS = ("id", "name", "author", "description", "version", "min_game_version")
REQUIRED_BOOLS = ("has_pck", "has_dll", "affects_gameplay")
VERSION_PATTERN = re.compile(r"^v?\d+\.\d+\.\d+$")


def main() -> int:
    errors = []

    try:
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8-sig"))
    except FileNotFoundError:
        print(f"{MANIFEST} not found", file=sys.stderr)
        return 1
    except json.JSONDecodeError as exc:
        print(f"{MANIFEST} is not valid JSON: {exc}", file=sys.stderr)
        return 1
    if not isinstance(manifest, dict):
        print(f"{MANIFEST.name}: manifest must be an object", file=sys.stderr)
        return 1

    for key in REQUIRED_STRINGS:
        value = manifest.get(key)
        if not isinstance(value, str) or not value.strip():
            errors.append(f"'{key}' must be a non-empty string")

    for key in REQUIRED_BOOLS:
        if not isinstance(manifest.get(key), bool):
            errors.append(f"'{key}' must be a boolean")

    for key in ("version", "min_game_version"):
        value = manifest.get(key)
        if isinstance(value, str) and not VERSION_PATTERN.match(value):
            errors.append(f"'{key}' must look like '1.2.3' or 'v1.2.3', got '{value}'")

    project = MANIFEST.with_suffix(".csproj")
    try:
        project_version = ET.parse(project).findtext("./PropertyGroup/Version")
    except (OSError, ET.ParseError) as exc:
        errors.append(f"cannot read project version: {exc}")
    else:
        version = manifest.get("version")
        if isinstance(version, str) and version.removeprefix("v") != project_version:
            errors.append(f"manifest version must match {project.name} Version ({project_version})")

    dependencies = manifest.get("dependencies", [])
    if not isinstance(dependencies, list):
        errors.append("'dependencies' must be a list")
    else:
        for index, dependency in enumerate(dependencies):
            if not isinstance(dependency, dict):
                errors.append(f"dependency #{index} must be an object")
                continue
            if not dependency.get("id"):
                errors.append(f"dependency #{index} is missing 'id'")
            version = dependency.get("min_version")
            if not isinstance(version, str) or not VERSION_PATTERN.match(version):
                errors.append(
                    f"dependency '{dependency.get('id', index)}' has an invalid 'min_version'"
                )

    if errors:
        for error in errors:
            print(f"{MANIFEST.name}: {error}", file=sys.stderr)
        return 1

    print(f"{MANIFEST.name} is valid")
    return 0


if __name__ == "__main__":
    sys.exit(main())
