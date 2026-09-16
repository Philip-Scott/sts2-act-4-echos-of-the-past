# Publishing 1.2.0 - Act 4: Echos of the Past

Version 1.2.0 is prepared for release, not published. Steam Workshop and GitHub
publication both require approval. Steam Workshop 1.1.0 remains the published version.
Preparation does not publish, tag, or commit anything.
This updates the existing Workshop item **3799307965**. The internal mod ID and
runtime filenames remain `TheArchitect`; do not create a second Workshop item.

## Publication status

The 1.2.0 candidate includes changes since 1.1.0 through `77219bb`, plus the local
release metadata, documentation, relic text, and build exclusion for `artifacts/`.
Its change note uses the author's edited 1.2.0 changelog bullets. No 1.2.0 upload,
GitHub Release, push, tag, or commit has been made.

## Previous release: 1.1.0

The author confirmed the Steam release was **2026-09-14 at 10:00 a.m. PDT**, with
the then-edited `CHANGELOG.md` as its change note and no changes to the main description or images.
Steam accepted the 1.1.0 upload to item **3799307965**. The published change note
matches the submitted changelog retained in its upload workspace.
The main description, thumbnail and screenshot list
match the listing before the upload.

The upload used `artifacts/releases/v1.1.0-steam-content-only/`, with runtime files
identical to the tested `~/TheArchitect-v1.1.0.zip`. That workspace contains no
`image.png` or `previews/` directory, and its `workshop.json` contains only
`changeNote`. It also retains the submitted changelog, upload log and before/after
listing snapshots. No GitHub Release, push, tag or commit was made.

The author confirmed **1.0.1** was the previous Workshop version. This
1.1.0 release includes changes through `5ba614a`, plus the local 1.1.0 release
metadata and documentation. Release-note comparisons use the previous release
preparation checkpoint `77f873c`; there is no release tag proving the exact
published source commit.

Retain the existing `artifacts/releases/v1.0.1/` and `v1.1.0/` bundles unchanged.
Their publishing handoffs describe the earlier preparation state, not the final
content-only upload. No promotional images were uploaded with 1.1.0.

## Before public release

- Review the Watcher artwork, relic and stance icons, and new music/audio credits.
  Keep the Steam description and images unchanged unless separately approved.
- Confirm the supported game branch/version and BaseLib version still match.
  The release targets public-beta v0.111.0 and BaseLib 3.4.5.
- Finish a normal first visit and repeat visit, including save/reload and both
  Architect outcomes. Exercise a real networked co-op group; simulated-party
  scenarios do not replace this. Record any remaining blockers.
- Exercise the staged candidate with and without Downfall 0.1.16, co-op card
  ownership, saved modded modifiers, solo/co-op Continue previews, and three-
  and four-player formations. Keep unsupported combinations explicit.
- Measure performance on an affected laptop before claiming frame-time or FPS
  improvements; the implemented reductions in idle work are not such measurements.
- Exercise all three Watcher gift categories, Wrath/Calm transitions and audio,
  Nuremberg Egg replays, saved relic compatibility, and the boss music handoff.
- Review source and artwork provenance and choose a repository license.
  No license has been assigned by this preparation. Confirm rights to redistribute
  all shipped assets; this is a release decision, not inferred from files being present.
- Review the changes since 1.1.0 and the local release metadata before choosing
  the release commit. Do not publish a package from unreviewed working-tree changes.
- Review the known limitations in `release/INSTALL.md` and `RELEASE_NOTES.md`.
  Crash-safe result transactions remain tracked in #12; do not describe 1.2.0
  as universally card-compatible or crash-proof.

## Build and stage

Regenerate the promotional artwork with `bash scripts/build-release-media.sh`
(ImageMagick, fontconfig, Noto Serif Bold and Lato Heavy). See
`docs/media/README.md` for screenshot sources and capture commands.

With .NET 9 available and the game installed:

```sh
python3 scripts/package-release.py
```

`STS2_PATH` / `STS2_DATA_DIR` can override game discovery. The packager invokes a
fresh **Release** build in temporary staging, never installs into the live game,
and refuses an existing version directory so it cannot destroy a Workshop item ID.
It uses the project's quick PCK packer, the same asset path used by native captures.
For the current asset formats, a separate full Godot export is not required by the
packaging command. If using `--publish` separately, use Godot/MegaDot 4.5.1 mono;
export failures must stop the build.

With the repository's existing Podman build image on Linux:

```sh
python3 scripts/package-release.py --builder podman \
  --game "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2"
```

Python runs on the host; only compilation runs in the existing
`localhost/sts2-the-architect-build:latest` image. This does not require Python
inside the build image. Podman output must remain under the repository so it is
visible in the container mount.

Output under `artifacts/releases/v1.2.0/`:

| File/directory | Use |
| --- | --- |
| `TheArchitect-v1.2.0.zip` | Manual install; only this mod's three runtime files plus install instructions |
| `SHA256SUMS` | SHA-256 hashes of the staged files |
| `workshop/` | Existing-item update workspace; preserves Steam description and visibility |
| `media/` | Thumbnail, banner and gameplay screenshots |
| `INSTALL.md`, `RELEASE_NOTES.md`, `PUBLISHING.md` | Installation, announcement and publishing handoff |

No game assemblies, BaseLib binaries, saves, logs, credentials or debug symbols
are included. Before distributing, install the ZIP in a clean disposable setup
and compare its runtime files with the Workshop `content` directory.

## Steam Workshop

Use Mega Crit's official [Slay the Spire 2 Mod Uploader](https://github.com/megacrit/sts2-mod-uploader),
not the first game's ModTheSpire uploader. Its
[workspace documentation](https://github.com/megacrit/sts2-mod-uploader/blob/main/template/README.md)
defines the fields used here.

The generated workspace has `workshop.json`, `mod_id.txt`, `image.png`, `previews/`
and `content/`. The item ID is copied from the tracked `release/mod_id.txt`;
packaging fails if that ID is missing or invalid.
Preview images are below Steam's 1 MB limit. The dependency is
[Alchyr's BaseLib, item 3737335127](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127);
confirm it still supplies a compatible version before publishing.
The update omits tags and mature-content descriptors, leaving their Steam values
unchanged. The uploader's branch
fields are documented as unreliable, so set compatibility on the Workshop
website and retain the explicit branch/version requirement in the description.

**The official v0.2.0 uploader always replaces `image.png`. Do not use it for a
content-only update.** Omitting `previews/` preserves additional screenshots but
does not prevent the thumbnail upload.

The staged 1.2.0 workspace includes promotional images for review, not permission
to upload them. Before an approved content-only publication, prepare a separate
`v1.2.0-steam-content-only/` workspace with the same runtime files, only
`changeNote` in `workshop.json`, and no `image.png` or `previews/`.
Do not reuse or overwrite the published 1.1.0 workspace.

For 1.1.0, a local variant of official source commit
`4c4db1262cdce6a1c4550a494f2e1088dbeda346` was built under
`artifacts/tools/mod-uploader-v0.2.0-content-only/`. Its upload path only sets item
content and submits the change note. It skips all listing metadata, image and
dependency updates, rejects image-bearing workspaces, and requires explicit item
ID `3799307965`. A failed content setter stops submission.

The approved upload used this command. Do not rerun it without approval for
another update:

```sh
cd artifacts/tools/mod-uploader-v0.2.0-content-only/content-only-bin
./ModUploader upload -w \
  "/absolute/path/to/repository/artifacts/releases/v1.1.0-steam-content-only" \
  --id 3799307965
```

Steam must be running under the publishing account. The prepared workspace updates
[the existing item](https://steamcommunity.com/sharedfiles/filedetails/?id=3799307965);
it does not create a new one. No upload is performed by the build or packaging scripts.

**Do not add `description` to `release/workshop.json` or the generated workspace.**
The author edits the description directly on Steam. The official uploader skips
`SetItemDescription` when the field is absent or null, so no local copy or fetch of
the current description is needed. The packager rejects a non-null description
or visibility field to prevent accidental overwrites. Visibility is likewise
left unchanged, whether the author has kept the item private or made it public.
Do not upload the old 1.0.0 workspace: it still contains the initial description.

**Keep `release/mod_id.txt` and each generated `workshop/mod_id.txt`.**
The official uploader uses the workspace ID to update the same item. Do not check
uploader logs or account data into the repository; the Workshop item ID itself is
a listing identifier, not a credential.

## GitHub Release

After approval, commit the reviewed release changes and tag that exact commit
`v1.2.0`. Create a GitHub Release from the tag, use `release/RELEASE_NOTES.md`
as the description, and attach the ZIP, `SHA256SUMS`, and install instructions.
The archive is a working-tree build: rebuild from the approved commit if anything
has changed since staging. Link the Workshop item in the announcement. Keep a
backup of the previous release and modded lineage saves for rollback.
