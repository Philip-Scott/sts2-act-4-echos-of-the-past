# Publishing 1.4.0 - Act 4: Echos of the Past

## Published 1.4.0

Version 1.4.0 is published on Steam Workshop. The author approved publication on
October 19, 2026, and Steam accepted the update to item **3799307965** at
**10:45 p.m. PDT**. No push, tag, or GitHub Release was made. The author
reported a successful playtest of the release changes on October 19, 2026; the
scope was a solo Slot 2 playtest, so real networked co-op remains unverified.

The build uses committed source `d99fe5e` with only `TheArchitect.json`,
`TheArchitect.csproj`, `CHANGELOG.md`, `release/INSTALL.md`,
`release/RELEASE_NOTES.md`, `release/workshop.json`, and this handoff overlaid.
Uncommitted playtest tooling in the main worktree is excluded.

The bundle is `artifacts/releases/steam-v1.4.0-d99fe5e/v1.4.0/`. The
content-only upload workspace is
`artifacts/releases/steam-v1.4.0-d99fe5e/v1.4.0-steam-content-only/`. It contains
only the three runtime files in `content/`, `mod_id.txt`, and `workshop.json`
with `changeNote`, plus the upload log, before/after listing and changelog
snapshots, and `publication-receipt.json`. The snapshots confirm that the title,
description, thumbnail, and screenshots are unchanged. Do not rerun the uploader
without approval for another update.

The C# suites passed on the release source, with a test-only project fix recorded
in `test-only-overlay.patch`; it is not part of the shipped build. The native preview playtest passed
the new empty-hand checks but then failed an existing stale hover check that
looks for the card preview under `HoverTipsContainer`; the preview has been
attached to the combat UI since 1.2.0.

## Published 1.3.0

Version 1.3.0 is published on Steam Workshop. The author confirmed a successful
final playtest and approved publication on October 12, 2026. Steam accepted the
update at **2:19:21 p.m. PDT**. No commit, push, tag, or GitHub Release was made;
GitHub publication still requires separate approval.
This updated the existing Workshop item **3799307965**. The internal mod ID and
runtime filenames remain `TheArchitect`; do not create a second Workshop item.

The published build uses committed source `d160a3c` plus the staged 1.3.0 release
metadata. It includes the separate Corrupted Player Elite, boss-only successor
capture, increased Corrupted Player HP, map artwork fixes, preview text changes,
and the new fight soundtrack and final-strike music handoff. Uncommitted playtest
code and tooling are excluded.

The bundle is `artifacts/releases/steam-v1.3.0-d160a3c/v1.3.0/`. The upload used
`artifacts/releases/steam-v1.3.0-d160a3c/v1.3.0-steam-content-only/`.
Its upload inputs contain only the three runtime files in `content/`, `mod_id.txt`,
and `workshop.json` with `changeNote`. It also retains the upload log, before/after
listing snapshots, published changelog, and `publication-receipt.json`.
Do not rerun the uploader without approval for another update.
Do not upload the image-bearing review workspace.
Preparation records in the bundle retain the source commit, metadata patch,
and checksums. Preserve earlier release bundles unchanged.
The uploaded runtime files exactly match the author's tested
`~/TheArchitect-v1.3.0-d160a3c.zip`. The published change note matches the
submitted note. Before/after snapshots confirm that the title, description,
thumbnail, and all five screenshots are unchanged.
The earlier `artifacts/releases/v1.3.0/` and `v1.3.0-steam-content-only/`
candidates use `33c7025` and do not include the new soundtrack.

The build used an isolated export of `d160a3c` with only `TheArchitect.json`,
`TheArchitect.csproj`, `CHANGELOG.md`, `release/INSTALL.md`,
`release/RELEASE_NOTES.md`, `release/workshop.json`, and this handoff overlaid.
The content-only uploader changed no listing metadata, dependencies, or images.

On October 12, 2026, the mod author confirmed receiving permission from RYS to
use "The Heart OST but it's Metal" in this mod. The author will add credits to
the Steam description. The confirmation is retained in
`artifacts/releases/steam-v1.3.0-d160a3c/music-permission.json`.
The original preparation records predate this confirmation and publication;
they remain unchanged as build evidence. Use the publication receipt for the
final release status.

The gameplay source passed isolated native Elite-loss scenarios for solo and
simulated parties of 2-4, plus solo/party Architect wins and losses. These checks
predate the 1.3.0 Release package and do not constitute real networked co-op testing.
HP regression suites passed after the multiplier change. The author reported
the final candidate playtest successful before approving publication. The test
scope was not specified, so real networked co-op remains unverified.
The reported window-resize crash was not established as a mod fault or fixed;
do not advertise a resize-crash fix.

## Published 1.2.1

The author approved publication on **2026-10-07**. Steam accepted the update at
**10:35:08 p.m. PDT**. The published change note is retained in the 1.2.1
content-only workspace. Before/after listing snapshots confirm that the title,
description, thumbnail, and screenshot gallery are unchanged.

The published build uses committed source `89bb0d2` plus the staged 1.2.1 release
metadata. It excludes the unrelated uncommitted playtest changes in the main worktree.
The build used an isolated export of that commit with only these release files
overlaid: `TheArchitect.json`, `TheArchitect.csproj`, `CHANGELOG.md`,
`release/INSTALL.md`, `release/RELEASE_NOTES.md`, `release/workshop.json`,
and this publishing handoff.

The bundle is `artifacts/releases/v1.2.1/`. The separate content-only Workshop
workspace is `artifacts/releases/v1.2.1-steam-content-only/`; it contains only
the three runtime files in `content/`, `mod_id.txt`, and a `workshop.json` with
`changeNote`, plus the upload log, listing snapshots, and publication receipt.
It contains no images or listing metadata for upload. Do not rerun the uploader
without approval for another update to existing item ID `3799307965`.

The original preparation bundle remains unchanged as evidence of the submitted
build. Its `PREPARATION.json`, `release-metadata.patch`, and `SHA256SUMS` record
the exact source overlay and runtime hashes. The content-only workspace's
`publication-receipt.json` records Steam's publication time and verification.
No commit, push, release tag, or GitHub Release was made during publication.
The change note covers the multiplayer resume cleanup fix, saved-party previews
before peers reconnect, and the Corrupted Deck reset shortcut. Existing frozen
opponents and other parties' histories remain unchanged unless explicitly reset.
Real multi-process co-op playtesting was not run for this publication. Protocol
tests and host-only resume smoke probes passed but do not replace it.

## Published 1.2.0

The author approved the Steam update on **2026-09-16**. Steam accepted 1.2.0 at
**2:23 p.m. PDT**, built from committed and pushed source `7f740f8`. This includes
the confirmed Corrupted Player overlay-layering and Wrath/Calm tooltip-icon fixes.
The published change note is retained in its content-only workspace; the main description,
title, thumbnail, and screenshot list are unchanged.

The fresh bundle is `artifacts/releases/steam-v1.2.0-7f740f8/v1.2.0/`. Its runtime
files exactly match the content-only upload in
`artifacts/releases/v1.2.0-steam-content-only/content/` and the refreshed
`~/TheArchitect-v1.2.0.zip`. The content-only workspace retains the submitted
changelog, upload log, before/after listing snapshots, and
`publication-receipt.json` with source identity, publication time, and hashes.

The earlier `artifacts/releases/v1.2.0/` candidate predates both fixes; retain it
as an earlier preparation artifact, not the published build. No GitHub Release
or release tag was created by this Steam upload.

The author separately approved updating the Workshop thumbnail on **2026-09-16**.
The thumbnail-only update uploaded `docs/media/workshop-thumbnail.png`; the
published original matches that file byte-for-byte. It did not change mod content,
the description, visibility, dependencies, or the screenshot gallery.
`artifacts/releases/v1.2.0-steam-thumbnail-only/` retains the image, upload log,
listing snapshots, and receipt. Its separate local uploader variant at
`artifacts/tools/mod-uploader-v0.2.0-thumbnail-only/` calls only `SetItemPreview`
before submission, requires item ID `3799307965`, and rejects content or preview
directories and nonempty listing configuration.

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
- Review the changes since 1.3.0 and the local release metadata before choosing
  the release commit. Do not publish a package from unreviewed working-tree changes.
- Review the known limitations in `release/INSTALL.md` and `RELEASE_NOTES.md`.
  Crash-safe result transactions remain tracked in #12; do not describe 1.4.0
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

The default command outputs to `artifacts/releases/v1.4.0/` and will refuse the
existing candidate. For a refresh, build from an isolated source export, then
retain the resulting bundle under a new parent directory. The current refreshed
bundle is under `artifacts/releases/steam-v1.4.0-d99fe5e/v1.4.0/`:

| File/directory | Use |
| --- | --- |
| `TheArchitect-v1.4.0.zip` | Manual install; only this mod's three runtime files plus install instructions |
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

The staged bundle includes promotional images for review, not permission to upload
them. The approved 1.2.0 upload used the separate `v1.2.0-steam-content-only/`
workspace with only `changeNote` in `workshop.json` and no `image.png` or
`previews/`. It used the same content-only uploader described below, with that
workspace path and explicit item ID `3799307965`. Do not rerun an upload or
overwrite either published workspace without approval for another update.

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
`v1.4.0`. Create a GitHub Release from the tag, use `release/RELEASE_NOTES.md`
as the description, and attach the ZIP, `SHA256SUMS`, and install instructions.
The archive uses the source commit and release overlay recorded in
`PREPARATION.json`: rebuild from the approved commit if anything has changed
since staging. Link the Workshop item in the announcement. Keep a
backup of the previous release and modded lineage saves for rollback.
