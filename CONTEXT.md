# Architect

This context defines the recurring final-act challenge and the lineage it carries between completed runs.

## Language

**Architect Act**:
The mandatory fourth act presented to the player as **Act 4 — The Architect**, containing an Ancient, Rest Site, Shop, an optional Corrupted Player Elite, and the Architect boss.
_Avoid_: Bonus act, secret act

**The Watcher**:
The Act 4 Ancient, a keeper of forgotten lessons who equips challengers with one chosen Ancient Bonus drawn from relics of a previous climb.
_Avoid_: The Unwritten, replacement merchant

**Ancient Bonus**:
A custom relic offered by The Watcher as a Preparation, Discipline, or Transcendence gift.
_Avoid_: Permanent inheritance, extra reward entitlement

**Preparation**:
The Watcher's offer category for resources, foresight, and protection.
_Avoid_: Recovery slot

**Discipline**:
The Watcher's offer category for drawing and enchanting cards.
_Avoid_: Build slot

**Transcendence**:
The Watcher's offer category for repetition, scaling, and relic duplication.
_Avoid_: Bargain slot, guaranteed drawback

**First Visit**:
An Architect Act entered when no compatible Corrupted Player Snapshot exists for the active profile or Successor Group, so the Corrupted Player is skipped.
_Avoid_: First attempt, first run, empty lineage

**Repeat Visit**:
An Architect Act entered with a compatible Corrupted Player Snapshot or complete saved Successor Group, adding a separate Corrupted Player Elite before the Architect boss.
_Avoid_: Rematch, second visit

**Corrupted Player Snapshot**:
A terminal record of one participant's character identity, final deck including upgrades, and maximum health. It excludes relics, current health, and defeat state.
_Avoid_: Run save, checkpoint, ghost, build

**Architect Outcome**:
The mod-owned result of the Act 4 fights, either Architect Win for defeating the Architect or Architect Loss for falling to the Corrupted Player Elite or Architect, distinct from the base game's victory result.
_Avoid_: Run result, Act 3 result

**Successor Lineage**:
The latest complete set of Corrupted Player Snapshots recorded for one exact group membership after a terminal Architect encounter. Every participant retains a copy, so changing the host between runs preserves the group's lineage.
_Avoid_: History, archive

**Successor Group**:
An order-independent set of participants identified by their persistent profile identities. Changing any participant identity creates a different group.
_Avoid_: Lobby, party slot order

**Lineage Owner**:
A participant profile that retains its own copy of a Successor Group's lineage. The host selects its copy for the next run, and every participant advances their copy from that shared encounter.
_Avoid_: Party owner, server

**Corrupted Player**:
The corrupted version of a previous player character, restored from a compatible Corrupted Player Snapshot, that confronts the current party before the Architect. This is the enemy, not the human player challenging the Architect.
_Avoid_: Challenger, Corrupted Challenger, player clone, ghost player

**Corrupted Party**:
The complete set of Corrupted Players from the latest saved Successor Lineage, fought together in an Elite encounter before the separate Architect boss encounter.
_Avoid_: Random member, representative character

**Corrupted Player Simulation**:
The Corrupted Player's private, deterministic model of its snapshot deck, card piles, and energy. It resembles player combat enough to preserve deck identity but is not a live player or a complete emulation of one.
_Avoid_: Player simulation, hidden player

**Corrupted Player Plan**:
The immutable, player-visible sequence of card outcomes selected and resolved in monotonic left-to-right order for one Corrupted Player turn. Both telegraphing and execution derive from it; this order supersedes the prototype's historical right-to-left presentation behavior.
_Avoid_: Intent preview, move script

**Unplayed Card**:
A card that the Corrupted Player Plan does not select because it is unaffordable, intrinsically unplayable, or fails an adapter-proven eligibility condition.
_Avoid_: Unsupported card, skipped effect

**Unsupported Card**:
An otherwise eligible card whose complete effect cannot be translated safely; it remains visible and resolves as an explicit neutral no-op.
_Avoid_: Unplayed card, ignored card
