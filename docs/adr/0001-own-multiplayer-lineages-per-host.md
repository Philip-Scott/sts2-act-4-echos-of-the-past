# Own multiplayer successor lineages per host

Multiplayer Successor Lineages are local, profile-scoped records owned exclusively by the profile that hosted the recorded run. Although a Successor Group is identified by an order-independent set of persistent profile UUIDs, different hosts keep separate lineages for that same group; this avoids replica reconciliation and remote deletion promises while cloud portability is unavailable, at the cost of lineage continuity when the host changes.

The starting host remains authoritative for the full run and atomically replaces the lineage with every original participant's terminal Corrupted Player Snapshot after either Architect outcome. A domain-separated derivation from the run seed selects one compatible member for the next Corrupted Player encounter. Invalid synchronization, no compatible character snapshot, or no lineage produces First Visit behavior rather than blocking play; unresolved cards within a compatible character use the visible neutral fallback.

Participation implies consent to host-local persistence. There is no in-game reset or host migration: manual deletion of the local profile-scoped data is the only reset mechanism.
