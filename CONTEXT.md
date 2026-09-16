# Architect

This context defines the recurring final-act challenge and the lineage it carries between completed runs.

## Language

**Architect Act**:
The mandatory fourth act presented to the player as **Act 4 — The Architect**, containing a fixed Ancient, Rest Site, Shop, and Architect encounter.
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
An Architect Act entered when no compatible Corrupted Player Snapshot exists for the active profile or host-owned Successor Lineage, so the Corrupted Player is skipped.
_Avoid_: First attempt, first run, empty lineage

**Repeat Visit**:
An Architect Act entered with a compatible Corrupted Player Snapshot or complete saved Successor Group, producing a continuous Corrupted Player-to-Architect encounter.
_Avoid_: Rematch, second visit

**Corrupted Player Snapshot**:
A terminal record of one participant's character identity, final deck including upgrades, and maximum health. It excludes relics, current health, and defeat state.
_Avoid_: Run save, checkpoint, ghost, build

**Architect Outcome**:
The mod-owned result of the Architect encounter, either Architect Win or Architect Loss, distinct from the base game's victory result for the completed run.
_Avoid_: Run result, Act 3 result

**Successor Lineage**:
The latest atomic set of Corrupted Player Snapshots recorded for one owner and one exact group membership after a terminal Architect encounter.
_Avoid_: History, archive

**Successor Group**:
An order-independent set of participants identified by their persistent profile identities. Changing any participant identity creates a different group.
_Avoid_: Lobby, party slot order

**Lineage Owner**:
The profile that hosted a multiplayer run and exclusively persists and advances that run's Successor Lineage. Different hosts maintain separate lineages for the same Successor Group.
_Avoid_: Party owner, server

**Corrupted Player**:
The corrupted version of a previous player character, restored from a compatible Corrupted Player Snapshot, that confronts the current party before the Architect. This is the enemy, not the human player challenging the Architect.
_Avoid_: Challenger, Corrupted Challenger, player clone, ghost player

**Corrupted Party**:
The complete set of Corrupted Players from the latest saved Successor Lineage, fought together before the Architect. The Architect appears only after every member has been defeated.
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
