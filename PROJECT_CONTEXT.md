# RedSand project context

## Two-player transfer failure and fix (2026-10-30)

- Live client Player.log confirmed 13 in-scene NetworkObject soft-sync hash failures followed by a NullReferenceException in SceneEventData.DeserializeScenePlacedObjects. This prevented the scene-load completion handshake and destination player spawning; the client clock remained at its default and neither side had a working player camera.
- The raw scene clone incorrectly retained level 1's GlobalObjectIdHash values. Corrected all 14 Night 2 NetworkObjects using NGO's actual XXHash32(GlobalObjectId_V1-2-sceneGuid-componentFileId-0) algorithm. The calculation reproduces all 14 existing level 1 IDs exactly; Night 2 now has distinct canonical IDs.
- Added read-only NetworkSceneIdentityBuildCheck (IPreprocessBuildWithReport) to reject stale or duplicate serialized scene IDs before building. It does not enter Play Mode, open/save scenes, or run setup.
- Clock initializes its display before network spawn, ignores unspawned network state, clears its singleton on despawn/destruction, and pauses while progression waits for load completion. Cross-object initialization avoids writing to unspawned game-state and zombie variables.
- Added transfer/load-completion/player-spawn diagnostics. The 20-second level 1 test timer remains enabled.
- Validation: gameplay plus build-check C# compiled successfully. All enabled build scenes passed canonical ID checks; both gameplay maps match except their IDs, timer, and destination. No new live test or game launch was performed. Rebuild both host/client from the corrected project before retesting.

## Level progression update (2026-10-29)

This update supersedes the ending flow described in the original review below.

- Level 1 remains `Night1_MultiplayerTest`; at 8 AM, NightTimeManager asks the host's persistent NetworkLevelProgression component to load `Night 2` through NGO scene management.
- `Assets/Scenes/Night 2.unity` is a copy of the current working level 1 scene, including its uncommitted art changes. The only difference between the two scene files is the clock's next-level destination: level 1 targets Night 2; Night 2 has no destination yet. Both are registered in Build Settings.
- BunkerEndingSequence and WinScreen components are disabled in both scenes. No cutscene, ManThing reveal, ending cards, or old victory screen runs at night completion. Old scripts/assets remain available but are not part of this flow.
- Host snapshots living player IDs from NetworkOxygenState.IsDead before unloading. After the network scene load completes it respawns party player objects: survivors are playable with fresh level oxygen, eliminated clients retain dead objects for the existing spectator system. Death does not grant a revival on transfer. If nobody survives, the next level does not start.
- RelayPartyManager now disables legacy non-network scene players in any scene containing NightTimeManager, including Night 2.
- NetworkGameState's count now excludes dead player objects when refreshed; it still does not subscribe to every death-state change.
- Validation: compiled gameplay C# with Unity's bundled Roslyn compiler and cached project references (passed). Verified clone equality except destination, disabled retired components, 8 AM setting, unique serialized IDs, and Night 2 build GUID registration. This is not Unity's full asset-import/NGO postprocessing pipeline or a live multiplayer playtest.
- Playtest next: host plus clients, one dead before 8 AM, simultaneous survival, no survivors, dead host with living client, destination spawn/camera/UI/oxygen, and continuing proximity/radio voice. Night 2's completion currently stops its clock with no further scene transfer until level 3 is authored.

Reviewed 2026-10-29 against local commit `c3910b9` plus existing uncommitted assets.
This document reconstructs history from Git and describes inspected source and serialized scenes. It is not a record of unavailable chat conversations or a multiplayer playtest.

## Project and scene flow

- Unity 6000.0.32f1, URP 17.0.3.
- Main path: `Assets/Scenes/MainMenu.unity` -> `Assets/Scenes/Night1_MultiplayerTest.unity` -> ending cards -> MainMenu.
- Build settings also include `Night 1` and `MultiplayerTest`; these coexist with the main multiplayer scene.
- Core scripts: `Assets/Scripts`; network infrastructure: `Assets/Scripts/Networking`; player prefab: `Assets/Prefabs/NetworkRealPlayer.prefab`.
- Project conventions live in `Assets/_Project/README_Conventions.md`. Keep vendor edits out of ThirdParty and check duplicate assets before committing.

## Reconstructed earlier work

The repository records these changes, oldest to newest:

- Project cleanup, multiplayer/menu stabilization, then Vivox proximity voice integration.
- Join-code binding fixes; multiplayer surface teleport; local-player binding, lockers, Rush hiding, and voice transform fixes.
- Menu astronaut visuals and AI improvements; computer cooldown input and power compass fixes.
- Solar-flare repair compass, oxygen/flashlight/cooldown synchronization, and zombie retargeting.
- Networked ending, longer proximity voice range, radio broadcasting restricted to the Radio interaction object.
- Placed scene assets/spaceship, Rush limited to bunker players, death ragdolls and spectating.
- Per-slot networked locker occupancy; later scene/gameplay changes; spectator timing and event lighting fixes.
- Latest commits: `0f62c2a` fixes multiplayer lobby controls; `c3910b9` keeps the main-menu cursor unlocked (2026-08-03).

These are repository facts; Git alone does not establish who authored each decision in prior conversations.

## Gameplay systems

| System | Main components | Behavior |
|---|---|---|
| Player | PlayerController, PlayerBodyAnimator | CharacterController movement, sprint energy, jump, mouse look, camera effects, replicated animation input/pitch |
| Oxygen | OxygenSystem, NetworkOxygenState | Surface depletion, bunker recharge, death flag, synchronized flashlight |
| Night | NightTimeManager, ClockUI | Host clock and increasing zombie difficulty; ending trigger |
| Power | PowerSystem | Random outages, emergency lighting, repair-box interaction, console cooldown multiplier |
| Solar flare | SolarFlareSystem | Disables equipment, visual/audio effects, held repair interaction, repair compass target |
| Radar | RadarSystem | Shared scan/cooldown results and Surveyor radar-check notification |
| Defense | DefenseSystem | Shared activation/cooldown and temporary nearby threat neutralization |
| Surface enemies | ZombieAI | Roam/stalk/chase, field-of-view targeting, living-player targeting, targeted death presentation |
| Surveyor | SurveyorMonster | Advances when radar is neglected, retreats after scans, lethal close approach |
| Rush | RushMonsterEvent, RushMonsterUI | Warning/flicker, hiding window, bunker-player and locker checks, blackout/restoration |
| Lockers | LockerInteractionNew | Server tracks occupancy per slot; targeted enter/exit view and movement changes |
| Death | PlayerDeathSpectator | Added by OxygenSystem at runtime; death visuals/runtime ragdoll, delayed local spectating, target switching |
| Travel | TeleportOnTouch | Surface/bunker transitions; each network owner moves their local character; flashlight update |
| Camera console | SCameraNightVisionInteraction | Temporary night-vision illumination scoped to the surveillance camera render |
| Radio | RadioBroadcastStation, VoiceChatManager | Look at the Radio and hold E to transmit to the party-wide radio channel |
| Ending | BunkerEndingSequence | Network start, local-player bunker placement, darkness, eyes reveal, fade, ending cards |
| Presentation/setup | GameIntro, AmbianceSystem, CompassPointer, setup helpers | Intro, ambience, navigation, runtime UI/reference setup |

`IgnoreMonster` is currently a static ending prop compatibility component: it reports no active threat. `MultiplayerMenu` is an empty compatibility component. Old menu, player, locker, and setup scripts remain; do not assume every similarly named script is the active implementation.

### Actual serialized tuning in the main multiplayer scene

Use scene/prefab values before C# defaults when assessing balance:

- Night: 22:00 to 08:00, 90 real seconds per game hour (15 minutes total); custom ending timer disabled. Ending targets MainMenu.
- Network player oxygen: 180 seconds from full to empty on surface, 60 seconds recharge in bunker.
- Radar cooldown: 30 seconds; defense cooldown: 90 seconds.
- Rush: 20% check chance every 45 seconds.
- Solar flare: 20% check chance every 120 seconds.

## Multiplayer architecture

- Packages: Netcode for GameObjects 2.6.0, Transport 2.4.0, Multiplayer Services 1.1.2, Authentication 3.6.1, Vivox 16.10.0.
- Host/client architecture over Unity Relay with DTLS; anonymous Unity Services authentication. No host-migration implementation was found in the reviewed networking code.
- `RelayPartyManager` creates the Relay allocation and starts the host immediately, so the join code has a live host endpoint.
- The explicit approval limit is three total connected players. Three astronaut/name slots reflect this limit.
- Lobby names use custom named NGO messages. UI is largely discovered/bound at runtime, including settings, sensitivity, loading indication, and lobby controls.
- Connection approval suppresses automatic player creation. Host loads the gameplay scene through NGO, then manually creates missing player objects after the scene-load completion event.
- `PersistentNetworkManager` retains the network manager across scenes, enables scene management, and includes client disconnect/menu-return handling.
- `OwnerNetworkTransform` explicitly makes character transforms owner-authoritative. Movement input, pitch, sprint and grounded state are owner-written. This is mixed authority, not fully server-authoritative movement.
- Host owns oxygen/death state, shared night time, equipment states/cooldowns, enemy decisions, and world events. RPCs carry interaction requests and effects/results.
- `NetworkGameState` provides shared power/flare/equipment/restart/compass/count/seed fields; individual systems also maintain their own replicated state.
- Vivox is separate from gameplay replication: a positional channel named from the Relay code plus a group channel suffixed `_RADIO`. Defaults are 72-unit audible range, 24-unit conversational range, position updates every 0.1 seconds. Voice failure is handled as nonfatal.
- The player prefab directly contains PlayerController, OxygenSystem, PlayerBodyAnimator, OwnerNetworkTransform, NetworkPlayerMovementState, and NetworkOxygenState. PlayerDeathSpectator is attached at runtime.

## Findings to investigate before relying on multiplayer behavior

These findings were not changed during this review:

1. **Alive count is not a death count.** `NetworkGameState.RefreshPlayerCounts()` counts every non-null PlayerObject as alive and only refreshes on network spawn/connect/disconnect. Dead spectator objects remain, so this field cannot reliably drive all-dead logic.
2. **Movement RPC lacks sender validation.** `NetworkPlayerMovementState.SetMovementEnabledServerRpc` permits non-owner calls and checks target ID against the object owner, but does not check the requesting sender. A client can potentially toggle another player's movement by targeting the correct object/ID.
3. **Bunker state is client-reported.** NetworkOxygenState validates that the sender owns the player, but trusts the supplied bunker boolean. Host oxygen and Rush logic should be assessed with that trust boundary in mind.
4. **Late joining is incomplete in the inspected party flow.** Approval checks capacity, not whether the match started; automatic player creation is always disabled. The manual spawn pass runs after the initial game scene load. A later client can be approved without a clear corresponding spawn path.
5. **Scene infrastructure needs runtime verification.** Serialized gameplay scene contains two network-manager/transport script occurrences, four ServerSpawnPointAssigner occurrences, and a legacy scene player. Occurrence counts alone do not establish which objects are active; check duplicates and spawn ordering in Play mode.
6. **Runtime setup relies on broad searches.** GameSystemFixer and MegaUISetup use first-found/tag/name lookups, including OxygenSystem. With several players these may bind to a remote or legacy scene player; inspect actual runtime references before changing UI behavior.
7. **Portable package restoration issue.** manifest.json references Bezi Sidekick through an absolute path under this user's Downloads folder. A different checkout/machine may not resolve it.
8. **Latest compile log is not a successful check.** August `Logs/CodexCompileCheck.log` shows Unity's `HandleProjectAlreadyOpenInAnotherInstance`. June `CodexReleaseCompile.log` reports successful batch exit; neither validates today's modified assets.

## Existing working-tree changes at review start

- Modified: SpaceMono-Bold SDF font asset, NetworkRealPlayer prefab, MainMenu scene, Night1_MultiplayerTest scene.
- Player prefab diff changes its NetworkObject GlobalObjectIdHash; compare host/client build versions when diagnosing prefab mismatch.
- MainMenu diff adds serialized optional settings bindings.
- Main gameplay scene has substantial uncommitted serialized changes (roughly 5,000 added lines).
- Untracked radio FBX, radio/plate/glass textures, AO textures, and materials, including duplicate names with ` 1` suffixes.
- Existing gameplay/source/assets were preserved during this review. This context document is the only intentional addition.

## Next verification session

Start through MainMenu with a host and two clients. Check roster and names, rejected fourth player, scene transition/spawn uniqueness, local camera/UI bindings, surface/bunker oxygen and flashlight replication, simultaneous console use and shared cooldowns, flare repair and compass, Rush hiding with occupied slots, zombie retargeting after death, ragdoll/spectator timing, all-dead recovery, radio/proximity transitions, ending, host departure, and join attempts after the match begins.

No new compile, build, visual inspection, or live multi-client verification was performed for this source/scene review.
