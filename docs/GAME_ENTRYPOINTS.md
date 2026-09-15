# Game integration and upgrade-risk inventory

Snapshot: **2026-09-12**, working tree based on `b8bb892`, **including uncommitted and untracked C# changes**. This describes the source currently present, not necessarily the installed DLL or published release.
Watcher content, enchantment, stance-visual, audio and relic-hook rows updated **2026-09-15**; the patch census retains the original snapshot scope.

Declared compatibility: Slay the Spire 2 **public-beta 0.111.0** (README: build `24724944`, game commit `41cef1ea`), **BaseLib 3.4.5**, .NET 9, Godot.NET SDK 4.5.1. See [project references](../TheArchitect.csproj), [mod manifest](../TheArchitect.json), and [README](../README.md). Game and Harmony assemblies come from the selected local game installation, not version-pinned NuGet packages. The manifest declares minimum versions, not a guarantee of compatibility with later versions.

## Bottom line

**BaseLib supplies the content framework, save extension, and custom-message transport; the fourth-act lifecycle and native Corrupted Player integration are predominantly custom game patches.** Updating BaseLib alone does not insulate those systems from game changes.

| Measurable surface | Current inventory | Interpretation |
| --- | --- | --- |
| Concrete BaseLib-backed content models | **26**: 1 act, 1 Ancient, 1 encounter, 2 monsters, 4 powers, 15 relics, 2 enchantments | Includes three retired relics retained for saved runs. All current custom content uses BaseLib model bases. The abstract card template adds no playable custom card. |
| BaseLib pool/localization/offer integration | 15 `[Pool]` attributes; 1 `ILocalizationProvider`; 3 `AddCustomAncientSpawnCondition` calls | Content registration and Ancient eligibility, not replacements for the lifecycle patches. |
| BaseLib extended-save registration | **1**, `TheArchitect.Run.v1` | Saves mod run state; external successor files and native card serialization remain separate dependencies. |
| BaseLib custom-message implementations | **2**, `ArchitectPartyMessage` and `LobbyDeckPreviewMessage` | Custom protocol logic still belongs to this mod. |
| Production Harmony patch declarations | **69** | Lifecycle 35; native combat 23; UI 9; encounter 1; monster 1. A declaration can select multiple runtime methods. |
| Playtest Harmony patch declarations | **9** | Opt-in behavior, but discovered by the same unconditional `PatchAll`. |
| Additional programmatic patch installer | **1**, `NativeCombatCallSites.Install` | Scans the game assembly and installs call-site transpilers; not included in the 78 declaration count. |

These are **different units**, not an honest "X% BaseLib / Y% custom" denominator. Counting imports, lines of code, or model classes against patch classes would conceal the large dynamic patch surface. Native virtual overrides are a third category: using a BaseLib base class does not make every inherited gameplay callback a BaseLib-specific API.

### Scope and risk notation

This inventory includes loader entry, content callbacks, every declared Harmony patch family, the dynamic installer, native events/commands, private-member dependencies, serialization/network contracts, and scene/resource dependencies. Ordinary calls such as each individual `PowerCmd.Apply` are grouped by contract rather than listed repeatedly. Internal planners, hashing, layout math, and synchronization state machines are not themselves game entrypoints.

**High**: private fields/methods, compiler-generated code, IL patterns, global routing, or native lifecycle assumptions. **Medium**: public native callbacks, serialization, network contracts, scene structures, or BaseLib/game interaction. **Lower**: primarily BaseLib content/metadata APIs. These describe upgrade sensitivity, not known defects or guaranteed failures.

`Pre` = prefix; `Post` = postfix; `IL` = transpiler; `Fin` = finalizer. Prefixes that skip the original implementation are replacements, even where their runtime guard restricts them to Architect content. Guards do **not** prevent Harmony resolving targets and applying transpilers during initialization.

## 1. Loader and BaseLib-facing contracts

| Source | Entrypoint/API | Upgrade exposure |
| --- | --- | --- |
| [MainFile.cs](../TheArchitectCode/MainFile.cs), `Initialize` | Native `[ModInitializer]`; initialize demo safety, register save and run-start listener, `Harmony.PatchAll(assembly)`, register stance texture aliases, then `NativeCombatCallSites.Install(harmony)` | **High** blast radius: initialization does not isolate a failed patch family and continue. A broken optional-playtest target can also block initialization. This is native loader + Harmony, not BaseLib initialization. |
| [Acts/ArchitectAct.cs](../TheArchitectCode/Acts/ArchitectAct.cs) | `CustomActModel(4, autoAdd: false)`; `ILocalizationProvider`, `ActLoc`; `CustomCreateMap`, background/map/rest asset overrides | BaseLib-backed content, but manual append, `_rooms` assignment, and fixed fourth-act assumptions remain **High** native coupling. |
| [Ancients/TheUnwritten.cs](../TheArchitectCode/Ancients/TheUnwritten.cs) | `CustomAncientModel`; `MakeOptionPools`, `MakePool`, `IsValidForAct`, asset/icon overrides | **Lower/Medium**. Native Ancient layout, entry healing, choice synchronization and rewards are reused. The background is supplied through a separate Harmony patch. |
| [Encounters/ArchitectEncounter.cs](../TheArchitectCode/Encounters/ArchitectEncounter.cs) | `CustomEncounterModel(RoomType.Boss, autoAdd: false)`; `GenerateMonsters`, `AllPossibleMonsters`, validity/reward/scene/slot/icon overrides | **Medium**. Needs a separate prefix to obtain run context before generation; empty slots require custom spawn positioning. |
| [Monsters/ArchitectBoss.cs](../TheArchitectCode/Monsters/ArchitectBoss.cs), [CorruptedPlayer.cs](../TheArchitectCode/Monsters/CorruptedPlayer.cs) | `CustomMonsterModel`; HP/SFX/assets; `CreateCustomVisuals`, `SetupCustomAnimationStates`; native room/death/move-state callbacks | **Medium**, becoming **High** for native Corrupted Player ownership and borrowed native visuals. |
| [Powers/ArchitectInvinciblePower.cs](../TheArchitectCode/Powers/ArchitectInvinciblePower.cs), [ArchitectBeatOfDeathPower.cs](../TheArchitectCode/Powers/ArchitectBeatOfDeathPower.cs) | `CustomPowerModel`; icon/display metadata; native damage, turn, card-play and application callbacks | **Medium**. Damage-hook order and actual HP-change notifications are gameplay contracts, not shielded by custom icon support. |
| [Relics/UnwrittenRelic.cs](../TheArchitectCode/Relics/UnwrittenRelic.cs) and its 15 concrete relics | `CustomRelicModel`; `[Pool(typeof(SharedRelicPool))]`; Ancient rarity; icon overrides; `RemovePrefix()` | **Medium**. Twelve Watcher offers and three retired, still-loadable relics. Relic effects use inherited native hooks and commands; saved IDs stay unchanged. |
| [Relics/HandheldMirror.cs](../TheArchitectCode/Relics/HandheldMirror.cs) | BaseLib `AddCustomAncientSpawnCondition` | **Medium**. Requires Ancient owner to exist when eligibility is evaluated; duplication itself is native/mod-owned logic. |
| `Relics/DeusExMachina.cs`, `Relics/VioletLotus.cs`, `Enchantments/Wrath.cs`, `Enchantments/Calm.cs` | BaseLib Ancient spawn conditions and `CustomEnchantmentModel`; native enchantment serialization | **Medium**. Offers require enough eligible unenchanted owner cards. Enchantment IDs must remain resolvable in saved decks and Corrupted Player snapshots. |
| [Persistence/ArchitectRun.cs](../TheArchitectCode/Persistence/ArchitectRun.cs), `Register` | `ExtendedSaveTypes.RegisterSavedValue<IRunState, string>` with JSON and packet string readers/writers | **Medium/High**. Depends on BaseLib invoking save/load extensions at the right time, native player IDs being available, and the saved key/schema remaining compatible. |
| [Multiplayer/ArchitectMultiplayer.cs](../TheArchitectCode/Multiplayer/ArchitectMultiplayer.cs), [LobbyDeckPreviewMessage.cs](../TheArchitectCode/Multiplayer/LobbyDeckPreviewMessage.cs) | BaseLib `ICustomMessage` (2 implementations), `CustomMessageWrapper` | **Medium/High**. Mod-owned serialization order, routing, host identity, membership and handshake semantics must match on every peer. |
| [ArchitectModels.cs](../TheArchitectCode/ArchitectModels.cs), [ArchitectModels.Watcher.cs](../TheArchitectCode/ArchitectModels.Watcher.cs), `TheUnwritten.Relic` | Native `ModelDb.GetById` with explicit `THEARCHITECT-...` IDs | **Medium/High** interaction with BaseLib ID prefix/registration. Normal-startup caches can bypass generic custom-model lookups. Watcher pickups and stance transitions pass these canonical models to concrete `CardCmd.Enchant`/`PowerCmd.Apply` overloads rather than resolving again through generic commands. IDs are persisted contracts; renaming them is not cosmetic. |
| [Cards/TheArchitectCard.cs](../TheArchitectCode/Cards/TheArchitectCard.cs), `Powers/TheArchitectPower.cs`, `Relics/TheArchitectRelic.cs` | Abstract scaffold bases over `CustomCardModel`, `CustomPowerModel`, `CustomRelicModel`; `RemovePrefix()` | Not additional concrete content. The `*ImagePath()` helpers are **local** [StringExtensions](../TheArchitectCode/Extensions/StringExtensions.cs), not BaseLib APIs. |

## 2. Native content callbacks (not custom Harmony patches)

| Source/model | Native hook or contract | What an upgrade could change |
| --- | --- | --- |
| `Acts/ArchitectMap.cs` | `ActMap.Grid`, `StartingMapPoint`, `BossMapPoint`, `startMapPoints`, `MapPoint.AddChildPoint` | Grid/coordinate/save assumptions; current route is Ancient -> Rest -> Shop -> boss, while older saves retain their three-stop route. |
| `ArchitectAct.InitializeRooms` | Mutable model `_rooms = new RoomSet { Ancient, Boss }` | Protected native representation and room initialization requirements. BaseLib's `autoAdd` is deliberately disabled. |
| `ArchitectEncounter.GenerateMonsters` | `ToMutable`, frozen snapshots, `CorruptedPartyPhase`, native `CreatureCmd.Add` later in combat | Room initialization order, mutable model ownership, multiplayer counterpart identity, mid-combat spawn behavior. |
| `ArchitectBoss` | `AfterAddedToRoom`, `GenerateMoveStateMachine`; custom `MonsterState.GetNextState/RegisterStates`; intent `GetIntentDescription`; `AttackCommand`, `PowerCmd`, `CardPileCmd` | Turn/move/intent contracts, native status semantics, Ascension and multiplayer scaling. No bespoke boss-card engine is used. |
| `CorruptedPlayer` | `AfterAddedToRoom`, `GenerateMoveStateMachine`, `AfterDeath`; native character `CreateVisuals`/`GenerateAnimator`; `SetMaxHpInternal`/`SetCurrentHpInternal` | Native engine initialization and creature ownership; death/removal/revival order before spawning Architect and cleaning up enemy pets. |
| `ArchitectInvinciblePower` | `ModifyHpLostAfterOstyLate`, `AfterApplied`, `BeforeSideTurnStart`; subscribes to `Creature.CurrentHpChanged` and power `Removed`; `HpDisplay`, `InvokeDisplayAmountChanged` | Damage-stage ordering, synchronous HP event timing, event signatures, turn participant lists, display refresh and cleanup. |
| `ArchitectBeatOfDeathPower` | `AfterCardPlayed` | Card-play notification timing and creature side/death semantics. |
| `LooseThread` | `ModifyHandDraw`, `AfterModifyingHandDraw` | Opening draw and `PlayerCombatState.TurnNumber`, including extra turns. |
| `CrookedNeedle`, `OrangePearl` | `BeforeCombatStart` | Startup hook ordering and native power application. |
| `BorrowedTomorrow` | `BeforeCombatStart`, `AfterEnergyReset` | Energy reset/extra-turn ordering and three-turn gating. |
| `DiamondHand` | `AfterPlayerTurnStart` | Callback must occur after opening draw; native `Glam.CanEnchant`, `CardCmd.Enchant`, ownership and reward RNG contracts. |
| `UnspentPossibility`, `LastMeal` | `AfterObtained` | Native Gold/max-HP/healing behavior and nested `RewardsSet`, `PotionReward`, `CardReward` flows. |
| `HandheldMirror`, `MirrorDuplication` | `AfterObtained`; `RelicCmd.Obtain`, `ToMutable`; explicit `DustyTome.AncientCard`, `SeaGlass.CharacterId`, `Girya.TimesLifted` preservation | Native relic acquisition side effects, fields and duplicate-type semantics can change without any Harmony target breaking. Blocklist is tied to current relic behavior. |
| `TheLastWish`, `RitualDagger` | `AfterObtained`, `BeforeCombatStart`; native Gold, Plating, Strength, Ritual and Vulnerable commands | Native power timing, Artifact prevention and ownership. |
| `GoldenEye` | `BeforeHandDraw`, `CardSelectCmd.FromSimpleGrid`, `CardPileCmd.Add` | Scry must precede the opening draw, preserve top-to-bottom order and synchronize optional selection. Moving draw-pile cards must not trigger hand-discard effects. |
| `NurembergEgg` | `ModifyCardPlayResultLocation`, `ModifyCardPlayCount`, `BeforeCardPlayed`, `TryModifyKeywordsInCombat` | Native result-pile resolution precedes replay-count hooks. Count original play series, add two replays without replacing other bonuses, and keep Exhaust combat-only. |
| `DevaForm` | `ModifyShuffleOrder`, `AfterEnergyReset` | Initial shuffle must be excluded; the first mid-combat reshuffle enables a nonstacking bonus on subsequent owner turns, including extra turns. |
| `Wrath`, `Calm`, `WatcherStances`, stance powers | Enchantment `OnPlay`, native power application/removal and `ModifyDamageMultiplicative` | Enchantments enter stances after their card effect. Same-stance entry must not stack or pay Calm Energy; damage ownership must also work for Corrupted Players. |
| `CalmStancePower`, `WrathStancePower`, `WatcherStanceVfx` | `AfterApplied`, `AfterCombatEnd`, power `Removed`; cached creature/power state and Godot `_ExitTree` | Stance visuals attach only to the exact owner, including remote and Corrupted Players. Model-only `TestMode` skips visuals; stale removal callbacks must not detach a replacement stance. |

## 3. Fourth-act lifecycle and presentation patches

Paths below are relative to `TheArchitectCode/`. Each row is one `[HarmonyPatch]` declaration unless noted.

### Lifecycle/ArchitectLifecycle.cs (13)

| Patch class | Native target | Kind | Purpose / upgrade risk |
| --- | --- | --- | --- |
| `ArchitectRoomsPatch` | `ActModel.GenerateRooms` | Pre | Replace room generation for custom act. **High** initialization-order coupling. |
| `EnterArchitectActPatch` | `RunManager.EnterNextAct` | Pre | Append fourth act after index 2. **High**; requires exactly three original acts and reflective `RunState.Acts` setter. |
| `ResumeLegacyArchitectEntryPatch` | `RunManager.WinRun` | Pre | Divert legacy Act 3 ending-event saves into Act 4 using `ActChangeSynchronizer.SetLocalPlayerReady`. **High** ending/act-transition contract. |
| `SynchronizeArchitectEntryPatch` | `RunManager.EnterAct` | Pre | Await host/peer validation, then re-enter native method. **High** async entry/re-entry semantics. |
| `SynchronizeArchitectReloadPatch` | `RunManager.LoadIntoLatestMapCoord` | Pre | Same gate on reload. **High** saved-room and entry ordering. |
| `CaptureCorruptedPlayerAtEntryPatch` | `RunManager.SetActInternal` | Pre | Freeze solo snapshot or assert multiplayer readiness. **High** ordering before room setup. |
| `FinishArchitectWithoutRewardsPatch` | `NCombatUi.OnCombatWon` | Pre | Skip normal rewards and defer terminal handling. **High** UI/combat completion order. |
| `ResumeFinishedArchitectPatch` | `CombatRoom.StartPreFinishedCombat` | Pre | Complete a reloaded finished encounter. **High** reload internals. |
| `ArchitectOutcomePatch` | `RunManager.OnEnded` | Pre | Record Architect win/loss and successor before setting native victory to true. **High**, progression/save side effects. |
| `ArchitectVictoryRoomPatch` | `AbstractRoom.IsVictoryRoom` getter | Post | Expose terminal Architect room as victory. **Medium**, native ending predicates. |
| `ArchitectCharacterEpochPatch` | `ProgressSaveManager.ObtainCharUnlockEpoch` | Pre | Suppress nonexistent Act 4 character epoch. **High**, native progression table assumptions. |
| `ArchitectGameOverPatch` | `RunState.IsGameOver` getter | Post | Mark finished Architect encounter terminal. **Medium**, native completion predicates. |
| `ArchitectConsoleCommand.ArchitectEmptyRoomSetPatch` | `RoomSet.FromSave` | Pre | Fill omitted empty collections for this encounter. **Medium/High**, native serialized room schema. Production save fix despite nesting inside console-command class. |

### Lifecycle/ArchitectEnding.cs (8)

| Patch class | Native target | Kind | Purpose / upgrade risk |
| --- | --- | --- | --- |
| `DeferArchitectResultPatch` | `NRun.ShowGameOverScreen` | Pre | Freeze result and enter native ending presentation first. **High**, terminal room transitions. |
| `ArchitectEndingDeadPlayerOptionsPatch` | `EventModel.BeginEvent` async state machine `MoveNext` | IL | Replace `Creature.IsDead` entry gate so defeated party members participate. **High**, compiler-generated method and exactly **one** expected call; throws if count changes. |
| `ArchitectEndingDialoguePatch` | Native event `TheArchitect.LoadDialogue` | Pre | Select using pre-result wins; inject private `_dialogue`. **High**, private storage and dialogue selection. |
| `ArchitectEndingFinalAttackPatch` | Native event `TheArchitect.WinRun` | Pre | Track start of final presentation attack. **High**, internal event sequencing. |
| `ArchitectEndingAttackPatch` | Native event `TheArchitect.AnimArchitectAttackIfNecessary` | Pre | Force Architect attacker. **High**, argument/enum and animation flow. |
| `BindArchitectSuccessorsPatch` | `RunManager.WinRun` | Pre | Replace final completion with binding effect then frozen game-over screen. **High**; shares target with legacy-entry patch. |
| `ArchitectEndingPlayerVisualsPatch` | `NCreature._Ready` | Post | Idle ending participants, including dead ones. **Medium**, visual initialization. |
| `ArchitectEndingDeadPlayerAnimationPatch` | `CreatureCmd.TriggerAnim` | Pre | Animate dead ending participants without reviving them. **High**, bypasses native death gate and supplies replacement Task. |

### Remaining lifecycle/presentation files (14)

| File / patch class | Native target | Kind | Purpose / upgrade risk |
| --- | --- | --- | --- |
| `NativeCorruptedPlayerPreflight.cs` / `NativeCorruptedPlayerPreflight` | `RunManager.EnterMapCoord` | Pre | Block boss entry on incompatible saved content; restore map travel and show error. **Medium/High**, room-entry and native popup contracts. |
| `ArchitectMapLayout.cs` / `ArchitectMapLayout` | `NMapScreen.SetMap` | IL | Rewrite exactly 5 float constants: `2325`, `-1980`, `740`, `720`, `800`. **High**; count change throws during patch application. |
| Same / `ArchitectMapScroll` | `NMapScreen.UpdateScrollPosition` | Pre | Clamp private `_targetDragPos` using `_map`. **High**, private fields. |
| Same / `ArchitectMapOpeningPosition` | `NMapScreen.Open` | Post | Reset `_mapContainer` and `_targetDragPos`. **High**, private fields/layout. |
| Same / `ArchitectMapIntro` | `NMapScreen.StartOfActAnim` | Pre | Replace intro Task; manipulate `_actAnimTween` and call private `InitMapPrompt`. **High**. |
| `ArchitectMapIcons.cs` / `ArchitectMapIcons` | `NNormalMapPoint._Ready` | Post | Private `_runState`, `%Icon` and material replacement. **Medium/High**, field/node structure. |
| `ArchitectHistoryIcons.cs` / `ArchitectHistoryIconPatch` | `ImageHelper.GetRoomIconPath` | Pre | Explicit custom-ID icon before BaseLib/native fallback, `Priority.First`. **Medium/High**, patch ordering and ID contract. |
| Same / `ArchitectHistoryIconOutlinePatch` | `ImageHelper.GetRoomIconOutlinePath` | Pre | Same for outlines. **Medium/High**. |
| `ArchitectRestVisuals.cs` / `ArchitectRestVisuals` | `NRestSiteCharacter._Ready` | IL | Replace `IRunState.CurrentActIndex` getter calls with Act-3 animation index in Act 4. **High**; requires at least one match, otherwise throws. |
| `ArchitectRoomBackgrounds.cs` / `ArchitectRestBackgroundPatch` | `NRestSiteRoom._Ready` | Post | Inject background, hide named native scenery; private `_runState`. **Medium/High**, scene hierarchy. |
| Same / `ArchitectMerchantBackgroundPatch` | `NMerchantRoom._Ready` | Post | Inject shop scenery, hide native background nodes. **Medium**, scene hierarchy. |
| `UnwrittenPresentation.cs` / `UnwrittenPresentation` | `EventModel.CreateBackgroundScene` | Pre | Supply runtime-packed Ancient background. **Medium**, native Ancient layout dimensions/packing assumptions. |
| `ArchitectSpawnPosition.cs` / `ArchitectSpawnPosition` | `NCombatRoom.AddCreature` | Post | Position mid-combat Architect via private `PositionEnemies` and `UpdateCreatureNavigation`. **High**, private delegate signatures. |
| Same / `CorruptedPartySpawnPosition` | `NCombatRoom.PositionEnemies` | Pre | Replace layout for 2+ Corrupted Players. **High**, native layout/camera scaling and list semantics. |

### Encounter and monster patches (2)

| File / patch class | Native target | Kind | Purpose / upgrade risk |
| --- | --- | --- | --- |
| `Encounters/ArchitectEncounter.cs` / `PrepareArchitectEncounterPatch` | `EncounterModel.GenerateMonstersWithSlots` | Pre | Supply run context before virtual monster generation. **Medium/High**, callback order. |
| `Monsters/CorruptedPlayer.cs` / `CorruptedPlayerPartyHealthPatch` | `Creature.ScaleMonsterHpForMultiplayer` | Pre | Skip double-scaling Corrupted Players; map Act 4 to native final-act tier for Architect/ending encounter. **High**, native scaling table and assumptions. |

### Audio/WatcherAudioLifecycle.cs (6; added after the original patch census)

| Patch class | Native target | Kind | Purpose / upgrade risk |
| --- | --- | --- | --- |
| `WatcherRunAudio` | `NRun._Ready` | Post | Attach one run-owned audio controller using the exact local creature from the native network identity. **Medium** run/view initialization order. |
| `WatcherAncientAudio` | `NEventRoom._Ready` | Post | Start Mantra and Divinity only for the current mutable Watcher event, not previews. **Medium** event/view identity. |
| `WatcherCombatAudioReset` | `CombatManager.Reset` | Pre | Cancel stance subscriptions, loops and pending cues before reset. **Medium** reset ordering. |
| `WatcherAncientAudioNextFloor` | `RunManager.EnterMapPointInternal` | Pre | Stop Ancient audio when leaving for the next floor, not on gift selection or map display. **Medium** navigation contract. |
| `WatcherAncientAudioNextAct` | `RunManager.EnterAct` | Pre | Stop Ancient ambience on act transition. **Medium** transition ordering. |
| `WatcherRunAudioCleanup` | `RunManager.CleanUp` | Pre | Stop all owned voices before the run is discarded. **Medium** cleanup ordering. |

## 4. Save, multiplayer and non-patch boundaries

| Boundary / source | Native API/data involved | Upgrade failure mode |
| --- | --- | --- |
| Run-start subscription, `Multiplayer/ArchitectMultiplayer.cs` | `RunManager.Instance.RunStarted` | Start handshake at wrong time, miss initialization, or bind before run/network state is usable. This is a native event, not a BaseLib lifecycle hook. |
| Run networking, same file | `NetService.Type`, `NetGameType.Host/Client`, `NetClientGameService.HostNetId`, `NetId`, `IsConnected`, `SendMessage` | Host/membership identity or message timing changes; desync or explicit entry rejection. `PartySynchronizer`, `PartyEntryConsensus` implement mod logic rather than patching game methods. |
| Message payloads, both custom-message implementations | Ordered native `PacketWriter`/`PacketReader` integers, strings, ulong IDs; mod chunk/digest/epoch/request fields | BaseLib message dispatch changes, native packet encoding changes, or mismatched peer versions. No promise of wire compatibility across versions. |
| `Persistence/CorruptedPlayerStore.cs` | `CardModel.ToSerializable`, `SerializableCard`, `JsonSerializationUtility.GetTypeInfo`, `ModelId.Deserialize`, `ModelDb.GetByIdOrNull` | Native saved-card schema, model IDs, upgrades, enchantments and properties can break restore even if mod envelope schema is still `1`. |
| Same; `Persistence/SuccessorStores.cs` | `SaveManager.GetProfileScopedPath`, Godot `ProjectSettings.GlobalizePath`; mod-owned JSON identity, snapshot, host/group lineage and backup files | Profile layout changes can appear as missing lineage; native content changes can invalidate old builds. These files are not managed by BaseLib's extended-save API. |
| `Persistence/ArchitectRunState.cs`, `CorruptedPartySnapshot.cs` | Frozen entry party, original network IDs, host identity, mod schema/hash/revision | Reload must retain the same host and participants; host migration is explicitly unsupported. JSON/digest changes need deliberate migration/protocol handling. |
| `ArchitectModels.cs`, Ancient relic lookup, snapshot restore | Canonical prefixed IDs and native model database | Registered type and saved ID must still resolve. Generic ID-prefix workaround and history-icon precedence should be revisited together on BaseLib updates. |
| `Lifecycle/ArchitectLifecycle.cs`, `ArchitectConsoleCommand` | Native `AbstractConsoleCmd`, `CmdName = "architect"`, `Process`; `EnterAct(3)` | Native command discovery/dispatch and transition contracts. Explicit developer action, not a production startup transition. |

Current error policies are not uniform: missing/invalid solo lineage can log and use First Visit; unavailable solo character also has a First Visit fallback. Missing required card models block boss entry; invalid multiplayer membership/content blocks synchronization. Unsupported native-runtime effects remain visible and unplayed rather than silently translated. Atomic files/backups protect lineage writes but do not make native schema changes compatible.

## 5. Resource and scene contracts outside BaseLib

| Source | Hard dependency | Sensitivity |
| --- | --- | --- |
| `ArchitectAct`, `ArchitectBoss` | Native Architect workshop background path, native Architect monster assets, `idle_loop`/`hurt`/`attack` animation names; Glory map/rest assets | **Medium/High**. Resource renames or animation/skeleton changes need not produce compiler errors. |
| `TheUnwritten`, `UnwrittenPresentation` | `res://scenes/events/ancient_event_layout.tscn`, native title/offer geometry | **Medium**. Layout changes can overlap or hide content even when event logic works. |
| `WatcherStanceVfx`, `WatcherStanceShader` | `NCreature._Ready` postfix; exact owner `Visuals/%Bounds`; two cached procedural shader quads | **Medium/High** scene coupling. Restore pre-view powers without duplicate effects; preserve character materials/hierarchy, Bound Echo and unrelated pets. Geometry updates only when cached bounds change. |
| `WatcherStanceIcons` | `Resource.TakeOverPath`, strongly retained `AtlasTexture` aliases for the two `power_atlas.sprites/thearchitect-*_stance_power.tres` paths | **Medium** native path contract. Both native fallback and BaseLib custom paths must resolve the same original pixels, even when nonvirtual native getters are inlined. No vanilla resource aliases are replaced. |
| `WatcherAudio`, `WatcherStanceAudio` | Native `SFX`/`Master` buses, `NMuteInBackgroundHandler`, pausable `AudioStreamPlayer`, Ogg import/raw loading; `RoomEntered`, `CombatEnded`, power removal, owner HP/death and view exit | **Medium** audio/lifecycle contracts. Entry cues follow each true owner transition; only the exact local creature drives background stance loops. Pending cue completion must not revive a departed stance. Supplied Oggs are packaged unchanged, and unrelated music/ambience is not replaced. |
| `ArchitectRoomBackgrounds` | Rest `BgContainer` child 0; `RestSiteBG`, foreground/dither/lighting paths; shop `SceneContainer`, `BgContainer`, lights and `stars` | **High** scene-tree coupling. `GetNode` assumes these paths; native fire, logs, lighting, merchant and character containers must remain functional. |
| `ArchitectMapIcons`, map layout patches | `%Icon`, private map containers/tweens and layout constants | **High**. Scene and IL assumptions coexist. |
| `CorruptedPlayer.CreateCustomVisuals` | Every restored native character's visual scene, `%Visuals`, animator/SFX | **Medium/High**. One character can break while others still render. Additional UI/pet dependencies are catalogued with their integration hooks below. |
| Localized model IDs and asset packaging | `TheArchitect/localization/eng/*`, `res://TheArchitect/...`, `.pck` loader and custom IDs | **Medium**. BaseLib registration does not protect against missing packaged assets, changed localization keys, or native tooltip formatting changes. |
