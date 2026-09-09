---
name: setup-act4
description: Set up or reset an Architect Act 4 playtest using two random victory runs from another save slot. Use when asked to prepare Act 4, replay historical victory builds, or reset a playtest slot. Defaults to reading Slot 1 and preparing modded Slot 2, with naturally rolled Ancient offers.
---

# Set up Act 4 from victory history

Prepare a real, playable run using the existing native history launcher. Do not
implement a second save importer or manually fabricate `current_run.save`.

## Defaults and overrides

| Setting | Default |
| --- | --- |
| Source slot | 1 |
| Target slot | 2, in the modded profile tree |
| Histories | Two distinct, randomly selected single-player victories |
| Player | Character, deck, relics and resources from one victory |
| Corrupted Player | Character, deck and maximum HP from the other victory |
| Arrival | The Unwritten, then Rest Site -> Shop -> Corrupted Player -> Architect |
| Ancient offers | Normal randomness; explicitly set `ForceMirror: false` |
| Final action | Launch the game and leave the Ancient choice to the user |

Honor explicitly requested source/target slots, characters, histories or seed.
Slots must be 1, 2 or 3, and source and target must differ. Never silently use the
source as the target. Ask only about real ambiguity, such as multiple Steam
accounts, unavailable resources, or permission to discard an active run.

An explicit request to prepare files without launching overrides the final
action. Since the importer executes in the native game, explain this dependency
and get approval before launching a preparation process.

## Read the current implementation

Before acting, read:

- `CONTEXT.md` for First Visit, Repeat Visit and Corrupted Player terminology.
- `TheArchitectCode/Playtest/ArchitectHistoryPlaytest.cs` and
  `TheArchitectCode/Playtest/ArchitectHistorySetup.cs` for the launch/configuration
  contract and safety guards.
- `TheArchitectCode/Persistence/CorruptedPlayerStore.cs` for snapshot boundaries.
- `README.md`, `Sts2PathDiscovery.props` and the relevant build script.

Keep the installed game's version and BaseLib compatibility in mind. Do not
claim that unavailable card, relic or enchantment models can be restored.

## 1. Locate profiles without guessing an account

Discover the installed game, its mod directory, and its actual user-data root.
Use existing configuration and game logs when available. Linux commonly uses
`~/.local/share/SlayTheSpire2`; resolve the platform-appropriate location on
Windows/macOS instead of copying a Linux path.

For a Steam account, histories are normally under:

```text
<user-data>/steam/<account-id>/profile<source>/saves/history/
<user-data>/steam/<account-id>/modded/profile<source>/saves/history/
```

The target is:

```text
<user-data>/steam/<account-id>/modded/profile<target>/
```

Account-level modded slot selection lives in
`<user-data>/steam/<account-id>/modded/profile.save`, not the unmodded
account's `profile.save`. Use the corresponding account root for non-Steam
profiles; do not change platform/account mode as a shortcut.

Treat both source-slot trees as read-only. Never upload save data or copy an
entire source profile over the target. If the active account is ambiguous, ask.

## 2. Select and freeze two victories

Read source history `.run` files from both vanilla and modded source trees.
Eligible records have `win: true`, `was_abandoned: false` and exactly one entry
in `players`. Do not pick another participant from a multiplayer history.

Deduplicate copies using `(seed, start_time)` before random selection. Select
two different runs without replacement, assigning one to the opponent and one
to the player. Different runs may use the same character.

For "another reset" or "two other runs", exclude the most recent pair recorded
in this session's selection manifests when enough eligible victories remain.
Do not silently reuse a run if the user explicitly requested different ones.
If fewer than two eligible records remain, ask how to broaden the selection.

Create a unique, persistent artifact directory outside tracked repository
content, preferably the session's `files/` directory. Record:

- Source/target slots and the resolved account/profile paths.
- Each selected source filename, seed, start time, character and role.
- Original source-file hashes.
- Copies named `player.run` and `opponent.run`.

Keep the selected records fixed while preparing the game. Do not reroll a
selection merely because it is weak, unusual, or a difficult opponent.

## 3. Preserve the loadout faithfully

The launcher restores the player's saved deck, upgrades, enchantments, relic
properties/counters, potions, potion capacity, terminal HP, maximum HP and Gold.
It also uses that victory's Ascension. Do not obtain historical relics through
pickup commands: doing so would repeat rewards, card choices and costs.

The opponent inherits only character, deck and maximum HP. Do not copy its
relics, potions, Gold, current HP or victory state into the Corrupted Player.

History does not store every base resource field. Determine `MaxEnergy` and
`BaseOrbSlotCount` from the character and any permanent changes represented by
the selected build. Distinguish permanent base changes from combat-time relic
bonuses; do not add the latter twice. Character defaults are appropriate only
after establishing that no permanent adjustments need restoring.

If a required value cannot be recovered confidently, ask the user rather than
silently substituting a default. Likewise, stop on missing models instead of
dropping cards, upgrades or relics.

## 4. Back up and prepare the target

Check whether the game is running before touching installed mod files, slot
selection, or target saves. Ask before interrupting it unless the user's reset
request already authorizes that interruption. Stop only the specific identified
game process, never processes by name.

Once the game is closed:

1. Back up the complete target profile and the account-level modded
   `profile.save` (including existing backup files) into the artifact directory.
2. Record source-slot hashes so source preservation can be checked afterward.
3. Inspect the target for active single-player or multiplayer saves.
4. If an active run exists and replacement was not explicitly authorized, ask.

The native setup launcher deliberately refuses active runs. Even after reset
permission, do not bypass this guard or delete only a local current-run file:
Steam Cloud can restore it. Use the game's native abandon/delete flow for the
target run, preserving the backup. If that cannot be performed safely, ask the
user to abandon the target run in-game. Never discard a multiplayer run without
specific authorization.

Preserve completed target history, preferences, progress and unlocks. Replacing
the target's Corrupted Player lineage is intentional for this playtest; the
backup retains the previous lineage.

Ensure the target slot is selected using the native profile UI. With the game
closed, changing only `last_profile_id` in the backed-up modded account
`profile.save` is also acceptable. Preserve its schema and other fields.
Do not force `SaveManager.InitProfileId`'s optional argument: that bypasses
normal profile loading and can leave profile-management state uninitialized.

## 5. Build the configuration and install the mod

Generate one fresh random seed, independently of the Ancient offer results.
Use it unchanged. Set `ForceMirror: true` only if the user explicitly requests
a guaranteed Mirror offer; the launcher otherwise has a legacy true default.
A naturally rolled Mirror is not a reason to reroll.

Write `setup.json` in the artifact directory, with absolute paths:

```json
{
  "ProfileId": 2,
  "PlayerHistoryPath": "/absolute/artifact-directory/player.run",
  "OpponentHistoryPath": "/absolute/artifact-directory/opponent.run",
  "OutputDirectory": "/absolute/artifact-directory/ready",
  "Seed": "ONE_NEW_RANDOM_SEED",
  "MaxEnergy": 3,
  "BaseOrbSlotCount": 0,
  "ForceMirror": false
}
```

The slots and resource values above are examples; substitute the selected
target and the established resource values.

Build using the repository's existing build script with
`--mods-path artifacts/mods` (or the PowerShell equivalent). Never overwrite
loaded DLL/PCK files. With the game closed, back up the installed Architect
bundle and copy the matching DLL, PDB, PCK and manifest into its mod directory.
Keep the compatible BaseLib runtime present. Do not include private histories,
configuration files or backups in a commit.

## 6. Launch and verify the prepared run

Start the actual game through its normal platform launcher with:

```text
--architect-history-setup /absolute/artifact-directory/setup.json
--log-file /absolute/artifact-directory/ready/game.log
```

Do not use `--architect-native-test`, `--force-steam off`, an in-memory save
store, or a changed `XDG_DATA_HOME` for this live-profile task. Those are separate
isolated-test mechanisms, not a way to prepare the user's selected slot.

The launcher creates a native run, restores the selected loadout without
replaying acquisition effects, writes the opponent snapshot, enters Act 4,
saves the run, and produces `OutputDirectory/ready.json`.

Wait for that receipt or an explicit error; process startup alone is not success.
Verify that:

- The receipt names the requested target profile and selected characters.
- Starting deck/relic counts and opponent deck/max HP match the chosen histories.
- `ForceMirror` and `Seed` match the request; normal offers were not seed-searched.
- The target's `saves/current_run.save` and
  `TheArchitect/corrupted_player_snapshot.json` exist.
- The game window is visible at The Unwritten and has no setup failure.
- Source-slot files are unchanged.

Leave the Ancient offer unselected. Do not play cards, spend Gold, or finish
rewards on the user's behalf. If they start interacting during verification,
do not undo their choices; report the starting loadout rather than claiming
the live inventory is still unchanged.

On failure, preserve logs, selections and backups. Do not repeatedly rerun
history setup over a newly created active save. Diagnose it and obtain any
needed recovery decision first. Use normal Continue to resume a prepared run;
`--architect-resume-slot2` is an existing convenience only for target Slot 2.

Finish with a concise handoff: target slot, player character/Ascension and
starting card/relic counts, opponent character/card count/max HP, naturally
rolled offers, and confirmation that the source was preserved and target backed
up. Include a backup location when useful. Do not claim completion until the
prepared run is persistent and ready.
