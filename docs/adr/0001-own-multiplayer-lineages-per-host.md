# Own multiplayer successor lineages per host

Multiplayer Successor Lineages are local, profile-scoped records owned exclusively by the profile that hosted the recorded run. Although a Successor Group is identified by an order-independent set of persistent profile UUIDs, different hosts keep separate lineages for that same group; this avoids replica reconciliation and remote deletion promises while cloud portability is unavailable, at the cost of lineage continuity when the host changes.

The starting host remains authoritative for the full run and atomically replaces the lineage with every original participant's terminal Corrupted Player Snapshot after either Architect outcome. The next matching group fights that complete Corrupted Party together, rather than selecting one representative: party size scales the number of enemies, not their individual 2x/2.5x snapshot HP. The Architect enters only after every Corrupted Player has a confirmed death, preserving one continuous encounter.

The host freezes and synchronizes the whole entry party rather than allowing peers to choose from their own local snapshots. No lineage produces First Visit behavior; incomplete or incompatible party data must never silently create a partial party or different fights on different peers. Missing card content blocks entry visibly, and unsupported effects remain preserved but unplayed.

Participation implies consent to host-local persistence. There is no in-game reset or host migration: manual deletion of the local profile-scoped data is the only reset mechanism.
