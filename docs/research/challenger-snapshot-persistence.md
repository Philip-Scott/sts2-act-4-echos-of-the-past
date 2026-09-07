# Challenger Snapshot persistence seams

Research date: 2026-09-07  
Ticket: [#4](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/4)  
Versions examined: The Architect manifest minimum `0.107.0`; BaseLib `3.4.5`
(`22757933ba10adc4322a628519a233a567507d87`); game API snapshot
`d3db818409984371aa5582b94c55877ee2f72e3e`.

## Decision

Persist the single-player Challenger Snapshot as an Architect-owned, versioned JSON file under the
active game's **modded profile directory**. Resolve the directory from
`SaveManager.Instance.GetProfileScopedPath(...)`, listen to `SaveManager.ProfileIdChanged`, and use
the game's atomic write pattern (`.tmp`, rename, optional `.backup`). Treat `CurrentProfileId` only
as a local storage selector, not as a player identity.

Do **not** put the successor snapshot in BaseLib `ModConfig`, and do not attach it only to
`IRunState`:

- `ModConfig` writes to the account-global `OS.GetUserDataDir()/mod_configs` directory, so it is not
  profile-scoped.
- BaseLib's extended `IRunState` save data is appropriate for state needed to resume the *current*
  run, but a Challenger Snapshot is progression between runs and must survive deletion of the run
  save.

The game does not expose a public API for adding arbitrary files to its Steam Cloud synchronization
set. A file written through the profile path is local and profile-correct, but **cloud persistence is
not established**. Ship local persistence first, document this limitation, and do not patch private
cloud-save internals until there is a supported extension point.

For future multiplayer, add an Architect-generated random 128-bit `profileUuid` to each profile's
snapshot envelope. Participants exchange these IDs, and the host computes an order-independent group
key from the sorted distinct UUID byte strings. The host is the only authority that selects and
advances the shared snapshot; clients receive the authoritative envelope over a reliable BaseLib
custom message. This multiplayer layer should remain unimplemented until a lobby/pre-run message
hook is proven, because BaseLib currently registers custom handlers only when `RunManager` initializes
the shared run.

## Single-player storage

### Profile scope and lifecycle

`SaveManager` implements `IProfileIdProvider`; `CurrentProfileId` is valid after `InitProfileId`,
and `ProfileIdChanged` fires from `SwitchProfileId`. Its `GetProfileScopedPath(userData)` resolves a
path beneath the current profile directory. Profile deletion recursively deletes that directory.
([`SaveManager.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Saves/SaveManager.cs))

`UserDataPathProvider` lays data out by platform account and profile:

```text
user://{steam|default|editor}/{platformUserId}/modded/profile{profileId}/{dataType}
```

The `modded/` segment is selected when `IsRunningModded` is true. This keeps modded and unmodded
profiles separate.
([`UserDataPathProvider.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Saves/UserDataPathProvider.cs))

Recommended relative path:

```text
TheArchitect/challenger_snapshot.json
```

Recommended lifecycle:

1. Load after `SaveManager` has initialized the profile.
2. Reload on `ProfileIdChanged`.
3. Save only when a completed run produces a new challenger.
4. Never derive the path manually from `profile{n}`; call `GetProfileScopedPath`.
5. Serialize first, write `challenger_snapshot.json.tmp`, then atomically rename it over the target;
   retain or refresh `.backup` before replacement.

The game's `GodotFileIo.WriteFile` follows this backup/temp/rename sequence and creates missing
directories. Godot documents `user://` as the writable persistent-data location in exported games.
([`GodotFileIo.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Saves/GodotFileIo.cs),
[`user://` documentation](https://docs.godotengine.org/en/stable/tutorials/io/data_paths.html#accessing-persistent-user-data-user))

### Versioned envelope

Use an explicit envelope rather than serializing the current in-memory class directly:

```json
{
  "schemaVersion": 1,
  "revision": 7,
  "profileUuid": "8e35f4d0-40fd-4b26-817c-c21e42a5811d",
  "writtenByModVersion": "v0.1.0",
  "gameVersion": "0.107.1",
  "contentFingerprint": "...",
  "snapshot": {}
}
```

- `schemaVersion` selects an Architect-owned migration.
- `revision` is monotonic within this profile (or multiplayer group).
- `profileUuid` is generated once with a cryptographically strong UUID source and retained across
  display-name, character, and slot changes.
- `writtenByModVersion` and `gameVersion` support diagnostics; they are not migration keys.
- `contentFingerprint` covers all stable model IDs referenced by `snapshot`.
- Unknown newer schema versions must be rejected without overwriting the file.
- Missing/corrupt primary files may fall back to `.backup`; failure must be surfaced and must not
  silently replace the snapshot with an empty one.

Use stable `ModelId` values for cards, relics, powers, characters, and other game content, never
runtime object references or numeric network-cache indices. BaseLib provides save-type registration
for `ModelId` and custom types where the game serializer is used.
([`SavePatchUtils.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Utils/SavePatchUtils.cs),
[`ExtendedSaveTypes.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Patches/Saves/ExtendedSaveTypes.cs))

### Why the built-in and BaseLib seams are insufficient

The public `ISaveStore` surface has exactly the operations needed, but `SaveManager` keeps its store
private and exposes no arbitrary read/write method. `GetProfileScopedPath` therefore gives a correct
local path but not access to the game's cloud-aware store.
([`ISaveStore.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Saves/ISaveStore.cs),
[`SaveManager.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Saves/SaveManager.cs))

`CloudSaveStore` mirrors writes that pass through it, but startup synchronization is explicitly
enumerated in `SaveManager.SyncCloudToLocal` and `TryFirstTimeCloudSync` for profile, progress,
preferences, current run, multiplayer current run, and run-history files. An arbitrary Architect file
is not on those lists. Writing it directly with Godot or `System.IO` bypasses cloud mirroring.
([`CloudSaveStore.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Saves/CloudSaveStore.cs),
[`SaveManager.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Saves/SaveManager.cs))

BaseLib `ExtendedSaveTypes.RegisterSavedValue<IRunState, T>` patches `SerializableRun` and packet
serialization. It is useful if the current run must remember which challenger it loaded, but is not
the authoritative between-run store. Registration must happen before BaseLib freezes the generated
serializer properties.
([`ExtendedSaveHandlers.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Patches/Saves/ExtendedSaveHandlers.cs),
[`ExtendedSaveTypes.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Patches/Saves/ExtendedSaveTypes.cs))

BaseLib `ModConfig` stores static settings in `OS.GetUserDataDir()/mod_configs` and is account-global.
Its temp-file replacement is useful prior art, but the facility itself is the wrong ownership and
scope for gameplay progression.
([`ModConfig.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Config/ModConfig.cs))

## Multiplayer identity and group keys

### What game IDs mean

`LobbyPlayer.id`, `INetGameService.NetId`, message `senderId`, and
`INetHostGameService.ConnectedPeers[].peerId` are the same session participant identifier family.
The host builds lobby membership from message `senderId`, and `LobbyPlayer` serializes the `ulong`
ID to every peer.
([`LobbyPlayer.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Entities/Multiplayer/LobbyPlayer.cs),
[`StartRunLobby.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Game/Lobby/StartRunLobby.cs),
[`INetGameService.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Game/INetGameService.cs))

Their durability depends on transport:

- Steam transport uses the account's SteamID64 for host and client `NetId`.
- ENet reserves `1` for the host and accepts a caller-supplied client ID in its handshake.

Consequently, a raw `NetId` is suitable for routing and validating the current session, but not as
the persistence key. It would also couple an Architect save to one platform account and make ENet
semantics ambiguous.
([`SteamHost.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Transport/Steam/SteamHost.cs),
[`SteamClient.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Transport/Steam/SteamClient.cs),
[`ENetHost.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Transport/ENet/ENetHost.cs),
[`ENetClient.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Transport/ENet/ENetClient.cs))

Use the session ID only to bind a received `profileUuid` declaration to its sender for that lobby.
The persisted `profileUuid` is the participant identity used by the group key.

### Canonical group key

Given each participant's 16 raw UUID bytes:

1. Reject duplicates and unsupported identity/schema versions.
2. Sort byte strings lexicographically (unsigned byte order).
3. Encode a domain tag, encoding version, participant count, then the fixed-width UUID bytes.
4. SHA-256 the encoding and store the lowercase hexadecimal digest.

```text
groupKey = hex(SHA-256(
  utf8("TheArchitect/ChallengerGroup/v1") ||
  uint32_be(participantCount) ||
  concat(sort_lexicographically(distinct(profileUuidBytes)))
))
```

The count and fixed-width entries make the encoding unambiguous. Sorting makes the key independent
of join order, lobby slot, current host, character choice, display name, and transport ID. Domain
separation leaves room for unrelated future hashes.

## Authority and synchronization

The base game is host-relayed: clients send to the host; `NetHostGameService` handles the message
and, when `ShouldBroadcast` is true, forwards it to other ready peers while preserving the original
sender ID. Host-only lobby operations reject client execution, and the host constructs the canonical
lobby roster and begin-run message.
([`NetHostGameService.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/NetHostGameService.cs),
[`StartRunLobby.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Game/Lobby/StartRunLobby.cs))

Follow that model:

1. Every participant reliably submits `{profileUuid, schemaVersion, contentFingerprint}`.
2. The host checks one declaration per connected sender and verifies the declared participant set
   against the lobby roster.
3. The host computes `groupKey`, loads candidate state, chooses the authoritative snapshot, and
   increments `revision` when committing a successor.
4. The host broadcasts `{participantUuids, groupKey, revision, envelope}`.
5. Clients recompute the group key, reject unexpected membership and non-increasing revisions, and
   never independently merge snapshots.
6. Each peer may persist the host-authored envelope locally under its own profile, keyed by
   `groupKey`; writes are idempotent by revision.

Use reliable transfer. BaseLib `ICustomMessage` defaults to reliable transfer and exposes
`ShouldBroadcast`; its handler receives `senderId`.
([`CustomMessage.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomMessage.cs))

BaseLib registers custom message handlers after `RunManager.InitializeShared` and unregisters them
at cleanup. That verifies an **in-run** synchronization seam, not a character-select/lobby seam. A
snapshot needed to construct the run cannot safely depend on this API without runtime proof of
ordering or a separate lobby patch.
([`CustomMessagePatches.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Patches/Networking/CustomMessagePatches.cs))

No host-migration contract was found. Treat host disconnect as session termination; do not let a
new peer elect itself and write a successor without a separately designed consensus/recovery
protocol.

## Mod compatibility

The base-game join handshake carries game version, model-ID database hash, game mode/session state,
and gameplay-relevant mods. `JoinFlow` rejects game-version mismatch, differing gameplay mod lists,
and model-ID hash mismatch. The mod list entries are `manifest id + "-" + version`, and mods default
to gameplay-relevant unless their manifest says otherwise.
([`InitialGameInfoMessage.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Messages/Lobby/InitialGameInfoMessage.cs),
[`JoinFlow.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Multiplayer/Game/JoinFlow.cs),
[`ModManager.cs`](https://github.com/hongyipan152/STS2SourceCode/blob/d3db818409984371aa5582b94c55877ee2f72e3e/src/Core/Modding/ModManager.cs))

The Architect already declares `"affects_gameplay": true`, so peers must run the same Architect
manifest version. Still include an independent snapshot schema/wire version: equal mod versions do
not make malformed or future persisted data safe to consume.

BaseLib discovers custom message implementations, assigns keys from their fully qualified type
names, and constructs them with parameterless activation. Keep message type names stable, give every
payload an explicit wire version, and add new message types rather than silently changing old
layouts.
([`CustomMessage.cs`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/Abstracts/CustomMessage.cs))

There is one baseline mismatch to resolve before implementation: this repository permits game
`0.107.0`, while BaseLib `3.4.5` declares minimum game version `0.107.1`. The project also references
BaseLib with `Version="*"`, even though the manifest is rewritten at build time. Pinning the tested
package version would make persistence and wire compatibility reproducible.
([The Architect manifest](../../TheArchitect.json),
[`BaseLib.json`](https://github.com/Alchyr/BaseLib-StS2/blob/22757933ba10adc4322a628519a233a567507d87/BaseLib.json),
[`TheArchitect.csproj`](../../TheArchitect.csproj))

## Implementation gates

Before implementing persistence:

- Verify the chosen load/save hooks against the installed game build; the inspected game source is
  a public API mirror, not an official source repository.
- Test profile switch and profile deletion with all three slots.
- Test crash recovery from primary, `.tmp`, and `.backup` combinations.
- Decide explicitly whether local-only storage is acceptable; do not claim Steam Cloud support.
- Prototype the multiplayer identity exchange at the earliest point the challenger is needed and
  prove the custom handler exists there.
- Confirm that reconnect preserves the same session sender binding.
- Reconcile the game minimum and pin the BaseLib package used for the wire/storage contract.

No persistence implementation is part of this research result.
