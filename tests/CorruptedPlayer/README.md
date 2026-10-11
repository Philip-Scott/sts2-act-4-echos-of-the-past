# History-backed native party inspection

The [native launcher guide](../../TheArchitectCode/CorruptedPlayerCombat/README.md)
covers dependencies, isolation, and shared-visible controls. Rebuild the mod before
launching a history-backed inspection.

```sh
bash scripts/native-demo.sh run "/absolute/path/to/Slay the Spire 2" \
  --party-2 --manual --shared-visible --party-history party-history.json
```

`--party-history` replaces the fixed inspection builds with selected victory
loadouts. It requires `--manual --shared-visible` and one of `--party-1`,
`--party-2`, `--party-3`, or `--party-4`. It does not support Downfall scenarios.
The JSON must contain exactly one member per player:

```json
{
  "Seed": "HISTORY-PARTY-INSPECTION",
  "Members": [
    {
      "PlayerHistoryPath": "/absolute/path/to/player-one.run",
      "OpponentHistoryPath": "/absolute/path/to/opponent-one.run",
      "MaxEnergy": 3,
      "BaseOrbSlotCount": 0
    },
    {
      "PlayerHistoryPath": "/absolute/path/to/player-two.run",
      "OpponentHistoryPath": "/absolute/path/to/opponent-two.run",
      "MaxEnergy": 3,
      "BaseOrbSlotCount": 0
    }
  ]
}
```

Use distinct, completed singleplayer victory histories, not multiplayer histories,
abandoned runs, or saved runs. All history paths must be absolute regular files.
`Seed` must be a nonempty string. `MaxEnergy` must be a positive integer and
`BaseOrbSlotCount` a nonnegative integer. These values are explicit because history
files do not preserve them reliably. The first player's Ascension is shared by
the whole inspection.

This is a disposable local inspection, not live co-op or a save-slot setup.
It creates no gameplay saves and does not modify source histories or live profiles.
The launcher copies the selected histories into the isolated run's `party-history/`
directory and rewrites `party-history.json` to use those copies. It does not mount
the source home or profile. `run.json` records SHA-256 hashes of the source
configuration, rewritten configuration, and history copies.

The setup restores final loadouts without replaying card or relic acquisition
effects. It does not replay the historical run.

Run the launcher regressions without starting the game:

```sh
python3 -m unittest discover -s tests -p native_demo_test.py
```
