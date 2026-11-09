# RedSand project context

## Nights 4/5 and seamless campaign transfers (2026-11-09)

- Authored Night 4 and Night 5 from the saved Night 3 map; preserved geometry, night duration and equipment/resource mechanics. Night 3 -> 4 -> 5 -> MainMenu. Lunge and PA stay restricted to Night 3 onward. Existing speeds increase 5% per night, with small pursuit/reaction adjustments. Outages are 23%/26% per existing 55-second check; radar uses stay at 12 from Night 4 onward.
- Whisper (Night 4+): translucent cube, positional whisper/laugh, silent chase, host-validated flashlight retreat and alternate-angle retries, nearby teammate handoff, bunker escape. Burrow (Night 5): cube above ground, warning, underground sand/smoke trail and burrowing audio, host-resolved timed-jump counter, brief remote-jump grace and a real server-clock 30-second respawn window. Host owns all movement/targeting/hits; clients render replicated state; deaths target the victim only.
- Persistent NightTransition covers all peers before scene loading, with reliable acknowledgements. Lobby Start fades all screens to black. Night completion shows centered 8 AM, removes the text, holds black, then loads. New scenes wait for local spawned players/clocks, show their NIGHT number and fade in. Final Night 5 returns the party to menu. Survivor/spectator rosters and existing spawn passes are preserved.
- Sweep fixes include fresh-session transition tokens, stale-reveal rejection, eligible teammate selection, destroyed spectator-overlay fade guards, and cooldown timing under low frame rates. Replaced competing intro black overlays while retaining blur. Gameplay/oxygen updates pause during transition cards; networking continues.
- Final standalone localhost NGO/Unity Transport sweep passed 130 host assertions (encounters 30, lunge/PA 36, radar 23, complete five-night campaign/reconnect 41). Editor checks passed 67 assertions plus canonical scene identity preflight. Full production-scene development build: zero errors. Final role logs contain no managed exceptions or scene-spawn failures. Cube/smoke and transition renders visually inspected; blank transition frame verified entirely black.
- Evidence: Logs/LaterNightsVerification.json, Logs/FinalSweep-*.log, Logs/LaterNights*Result.txt and transition PNGs. Repro tooling: LaterNightChecks; StalkerNetworkTest; LaterNightTransferTest. Details and remaining manual Relay/audio checks: Assets/_Project/LaterNights.md. Current new gameplay inputs match the verified checkout byte-for-byte.

## Fresh pre-playtest audit (2026-11-03)

- Current-source development audit build and normal Windows release build both succeeded with zero errors. Release output: Logs/PlaytestRelease/Red Sand.exe. The release scene list excludes generated test scenes, and the development-only audit bootstrap is excluded.
- Real cloud/Relay run preplayfinal passed 78 assertions over four processes, covering private/public lobbies, cancellation, capacity/late-join rejection, both real night scenes, stress display data, two spectator HUD targets, settings/sensitivity, five-second wipe and all peers returning to menu. Vivox initialized/logged in/joined channels successfully; actual microphone mix/listening and native mouse capture remain manual.
- Fresh local multiplayer: revive run preplay2346 passed 195 assertions; nine-case death run Audit2-Death passed 144; Watcher passed 67 including real 45-second cooldown; drill run preplay2350 passed its three-peer/two-scene suite. Additional editor checks: lobby 31, stress 24, fixed scare layout 11. Fresh successful log scans had no managed exceptions or NGO spawn/config errors. Headless unsupported-shader diagnostics are expected from -nographics.
- Fixed competing disconnect/menu loaders, Surveyor pre-spawn initialization/update and blocked retreat/offline-radar notification/client sound, pre-spawn update guards for major systems, spectator ambience targeting, saved game-volume changes made during death/revival mute, and Vivox listener positioning while spectating.
- Full results, evidence paths, scope limits and a tomorrow checklist: Assets/_Project/PlaytestReadiness-2026-11-04.md; source hashes/build results: Logs/PlaytestVerification.json. Earlier failing test iterations are preserved; fresh evidence uses the run IDs above.

## Stationary jumpscare overlays (2026-11-03)

- LocalJumpscarePresentation now creates its screen-space canvas as a scene root instead of parenting it to the player. Death/ragdoll translation, rotation, and nonuniform scale can no longer move or rotate the image/message. The owning presentation still destroys the canvas on player destruction and hides it on disable/expiry.
- Message anchors are explicitly centered and the scaler uses Expand. Gameplay compilation passed; Tools > Death Presentation > Check Stable Scare Layout checks image and text corners across five moving/rotating/scaling owner poses.

## Broadcast screen crew stress (2026-11-03)

- Blank-screen correction: the original bunker-center direction put the canvas on the wall-facing side of Radio. CrewStressMonitor is now serialized directly onto Radio in both current night scenes and mounts canvases beyond the actual mesh bounds on both faces. Saved-scene previews confirmed the PA-facing screen is readable. Tools > Crew Stress > Render Saved Screens provides isolated visual verification.

- NightTimeManager installs CrewStressMonitor on the existing Radio wall screen at runtime. A bunker-facing world canvas displays up to three living players outside the bunker, names, and color-coded stress. No scene NetworkObject IDs or PA interactions are changed.
- Host calculates the highest stress over active, spawned, non-neutralized ZombieAI instances at 5Hz. Chase range uses sightDistance for FOV AI and detectionRadius for radius AI. Distance up to chase range +30m is 1/3; up to +20m is 2/3; actual chase of that specific player is 3/3; farther is CALM 0/3. Merely standing inside range without an active chase stays 2/3.
- NetworkGameState replicates one bounded UTF-8 screen snapshot to all clients. Inside/dead/disconnected players drop out. Tools > Crew Stress > Check Thresholds runs boundary, encoding-capacity, and night-screen presence checks.

## In-game Escape menu (2026-11-03)

- Persistent PlayerSettingsUI creates a scene-local Escape overlay in scenes with NightTimeManager or names starting with Night, covering future nights without scene wiring. Escape/Resume restores captured mouse; opening releases it. The game continues running.
- Settings reuses the existing display/audio/FPS builder, including display confirmation/revert, and saves/applies mouse sensitivity immediately with the same MouseSensitivity preference. Disconnect uses RelayPartyManager's shutdown/menu path, with a standalone-scene fallback.
- Local movement/look/animation and console/tool input are suppressed while open. Watcher ignores UI-only input while retaining actual movement checks. Spectator switching is suppressed while navigating the menu.
- Validation: gameplay C# compilation and diff whitespace checks passed. Visual and multiplayer menu/disconnect playtests remain outstanding.

## Party wipe and spectator HUD (2026-11-03)

- NetworkGameState checks spawned player death flags on the host during nights. Five real-time seconds with everyone dead returns the party to MainMenu and shuts down networking. Missing players during loading do not count as dead; a living player resets the timer. A paid revival does not extend the five-second wipe window.
- RelayPartyManager handles session/voice/lobby cleanup and waits for shutdown before loading the menu. Gameplay clients also return if their host disconnects.
- Spectator oxygen fill/text now reads the watched player's replicated oxygen and capacity. Power and bunker compass references follow the watched player, including bunker visibility, target switching, and returning to the local player after revival.
- Validation: current gameplay C# compilation passed. Full multiplayer wipe/return and spectator visual playtests remain outstanding.

## HUD, local settings and visibility (2026-10-31)

- Both night scenes use compact amber suit/clock/compass visuals. SuitOxygenHudVisibility fades the suit panel out only when the existing oxygen fill reaches full.
- ScreenUiLayout fits scene and runtime screen-space canvases within the display, preserving world-space UI and existing reference sizes. Canvas component references were corrected and bidirectionally validated.
- PlayerSettingsUI extends the existing menu settings with saved resolution/window mode, 15-second display confirmation/revert, VSync, FPS cap, FPS counter and game volume. PlayerController now loads the existing saved mouse sensitivity.
- Player camera near clipping reduced from 0.3 to 0.03 to prevent sprint wall-edge clipping.
- Built-in rendering is active (GraphicsSettings and quality pipeline overrides are null). OpaqueDistanceFog uses a Resources shader on player/spectator cameras. Revised after feedback about the visible gray haze: pure black radial fade starts at 35m and saturates completely at 70m, including the sky so distant monsters/mountains cannot silhouette against it. AI/network gameplay is unchanged.
- Validation: gameplay C# compiled against cached Unity references; scene component links and network identity uniqueness passed. No new build, shader rendering verification or multiplayer playtest was performed.

## Night 2 second motion monster (2026-10-31)

- User-added `Movement Detected 2` in Night 2 already has Enemy tagging, ZombieAI, CharacterController, a separate NetworkObject, bunker target, and a child animated model. Its existing serialized configuration was preserved.
- Removed ZombieAI's singleton destruction rule, which otherwise destroys the second monster. Each instance now runs its own host-authoritative AI, movement replication, kills, defense neutralization, and respawn state. Multiplayer monsters wait for network spawn before simulating or attacking; destination writes require spawn.
- Radar already enumerates tagged monsters and displays their individual scene names and neutralized states. Defense now skips neutralized/disabled/unspawned enemies and sends the same named neutralization results to host and clients.
- Validation: gameplay C# compiled with Unity's cached references. Night 1's 14 and Night 2's 15 serialized NetworkObject identities matched canonical XXHash32 IDs with no duplicates; both Night 2 AI bunker references resolve. No live host/client gameplay or animation test was performed.
- Night 2 difficulty tuning: Rush chance 25% per 45-second check; outage chance 20% per 60-second check. Night 1 remains 20% and 16%, respectively.

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

## Bunker tablet (2026-11-01)
- PlayerController adds BunkerTablet to every player at runtime. Owner-only square first-person UI slides with 1; left click shows a rotating bunker bearing for 3 seconds then a 25-second per-player cooldown. Stowing does not reset timers. Input requires locked cursor and an alive, movement-enabled player.
- Target resolves Bunker Arrow Waypoint (spaces/underscores ignored), with BunkerZone fallback. Public target field supports manual assignment.
- OxygenSystem suppresses the replicated flashlight while equipped, retaining requested state across teleports and restoring on stow. Death/control loss hides the view.
- Gameplay C# compiled with cached Unity references; visual and multiplayer Play Mode verification remains outstanding.

## Night 2 power station watcher (2026-11-01)

- Saved the user's unsaved Night 2 placement before setup. The existing `ManThing (1)` is now the visual child of `Power Station Watcher`; its world pose, materials, and scale were preserved. The older inactive ending ManThing was not repurposed. Night 1 has no watcher component or instance.
- Host-authoritative `PowerStationWatcher` patrols a configurable rectangle centered on Powerbox (default half extents 55 x 35m, speed 1.6m/s), with 28m, 100-degree vision and solid-geometry occlusion. Dead and bunker players are excluded. The collider and eye height were fitted to the user's approximately 11.3m-tall placed model; motor minimum movement distance is zero to avoid high-frame-rate movement loss.
- One surface player at a time receives a quiet generated signal cue and increasing local screen static. After a 1.5s reaction grace, moving, looking, jumping, sprinting or using tools fails the encounter and kills only that victim through NetworkOxygenState and the existing spectator flow. The host chooses a random 5-10s stillness period after grace. Controls remain responsive, making stillness a challenge rather than an automatic freeze.
- Success, failure or target invalidation hides the monster and disables its collision for 45s, then it returns on patrol ground. The scene NetworkObject remains registered during this dormant period. Static fades over 2s. Clients snap to the new replicated position on reappearance. Owner/encounter/time-validated activity reports run at 10Hz; server pose/pitch checks and a 2.5s heartbeat timeout supplement them.
- Reusable prefab: Assets/Prefabs/PowerStationWatcher.prefab. Later nights can instantiate it and assign their power station. Setup/validation and isolated test build commands are under Tools > Watcher; notes are in Assets/_Project/WatcherNotes.md. Test scene is excluded from normal Build Settings.
- Validation: Unity development build succeeded with zero errors. Two standalone processes using real NGO/Unity Transport over localhost passed 67 automated assertions: host/client acquisition and stillness survival, victim-only static with increasing exposure and 2s fade, real 45s absence, repeated respawns, position/look/tool/jump/sprint violations, unaffected bystanders, stale/wrong-owner report rejection, bunker exclusion, wall occlusion, initial roaming and roaming after respawn within bounds. First run exposed discarded tiny movement steps; fixed and full suite rerun passed. All enabled scene NetworkObject identities and Night 2-only wiring passed editor validation.
- Evidence: Logs/WatcherNetworkTestResult.txt; Logs/WatcherHost2.log; Logs/WatcherClient2.log; Logs/WatcherTestBuildResult.txt. Tests use the real monster prefab with minimal actors containing actual gameplay movement/death/network transform components. They are headless local tests, not a full Relay/MainMenu session or a visual/audio playtest in Night 2. Unsupported shader messages in those process logs result from the explicit -nographics test mode.

### Direct Night 2 Play fix (2026-11-01)
- Fixed the reported stationary monster when pressing Play directly in Night 2: the original Update returned until IsSpawned, but direct scene Play does not start a network session.
- With no listening NetworkManager, the watcher now runs a separate local patrol/encounter state, with the same vision, input sampling, grace/hold rules, victim effect, death, cooldown and respawn behavior. This local state does not write NetworkVariables or send RPCs. Network sessions still wait for scene spawn and only the host simulates AI. Network spawn resets local preview state and assigns motor authority; disconnect/despawn cannot restart offline AI.
- Verified in the actual Night 2 scene using editor Play Mode without starting a host/client: moved 7.539 metres over 5 seconds with the CharacterController enabled and IsSpawned false. Test returned Unity to Edit Mode. Evidence: Logs/WatcherDirectPlayResult.txt. The test can be rerun explicitly under Tools > Watcher > Test Direct Night 2 Play.

- Temporary transition test (2026-11-01): Night1_MultiplayerTest's NightTimeManager now uses custom completion after 5 seconds, targeting Night 2. Restore normal night length by disabling Use Custom Ending Time on that scene's clock. Night 2 timing is unchanged.

- Watcher tuning (2026-11-01): increased roam speed from 1.6 to 3.5 m/s, sight range from 28 to 45m, and FOV from 100 to 180 degrees in both the Night 2 scene and reusable prefab, plus script defaults. Replaced the 192x108 point-filtered CPU static with a Resources UI shader using approximately 1.5-screen-pixel grain at 24Hz. GPU noise avoids larger per-frame texture uploads; unsupported shader devices use 640x360 bilinear noise. Grace, hold, cooldown, victim-only effects and wall occlusion are unchanged.

### Movement Detected visibility correction (2026-11-01)
- The two Night 2 ZombieAI monsters were disabling their renderers whenever the local player was farther than maxVisibleDistance=45m. This also made them vanish in Scene view when starting Play, regardless of the Scene view camera's distance.
- Disabled that additional renderer cutoff (default and explicit scene fields now 0). Existing camera black fog still controls distant visibility; defense neutralization still hides its target temporarily. No AI, attack, spawn or network identity changes were made.
- Verified scene wiring: Night1_MultiplayerTest and legacy Night 1 each contain one ZombieAI/Movement Detected; Night 2 contains two (Movement Detected plus Movement Detected 2). The second monster was already absent from Night 1; no removal was needed. Gameplay C# compilation passed.


## Restore point before lobby browser (2026-11-01)
- Saved current source, scenes, prefabs, packages and project settings, including uncommitted work, to local Git branch `backup/pre-lobby-browser-20261101-111858`.
- Independent byte-for-byte archive, SHA-256 manifest and architecture notes: `C:\Users\rhode\Documents\RedSand-RestorePoints\pre-lobby-browser-20261101-111858`.
- Baseline: Relay/NGO existing create-host, join-by-code, three-player cap, owner names, match-start join rejection, Vivox proximity/radio, and host scene transfer remain in place. No public lobby discovery/browser has been implemented.
- Main path: MainMenu -> Night1_MultiplayerTest -> Night 2. The temporary five-second Night 1 transition is enabled.
- Night 1 has one Movement Detected; Night 2 has two plus the power station watcher. Watcher roaming is 3.5m/s with 45m/180-degree vision, direct scene Play support, fine-grain static, 1.5s grace, 5-10s stillness, 2s fade and 45s absence. Extra ZombieAI renderer-distance cutoff is disabled; black fog remains. Tablet and first-person head hiding are included.

## Public/private lobby browser (2026-11-01)
- Added separate LobbyAdvertisement (discovery-only Unity Lobby service) and RelayLobbyBrowser (runtime menu UI). Default hosting remains private; public hosting advertises owner, actual NGO player count and Relay code. Browser displays up to six matching waiting lobbies, with manual/5s refresh, stale/full/private/version checks, and Join routed into existing Relay JoinParty.
- Added small hooks to RelayPartyManager. Preserved original bodies of Relay joining, capacity/start approval, scene loading, manual spawning and match-start core. Unity service initialization now shares an in-flight task so the browser/menu cannot duplicate authentication. No package, gameplay scene/prefab or network identity edits are part of this feature.
- Public listings lock/hide before game start, update counts after joins/leaves and stop/delete on leave. In-flight operations are serialized and invalidated by generation; cleanup retries do not touch Relay. Service outage leaves existing networking intact. Private codes are not advertised.
- Baseline remains commit 1ca68fc on GitHub/main and the independent local raw backup. Browser work is local until a separate push is requested. Implementation/rollback details: Assets/_Project/LobbyBrowserNotes.md.
- Initial validation: Unity development build succeeded with zero errors; 31 async lifecycle/stale-row tests passed. Four separate game processes using real Unity Lobby/Relay and distinct anonymous profiles passed 52 checks, including private invite joining, public discovery/joins/counts, third/fourth player limits, listing removal on start, late-join rejection and all three players transferring through the five-second Night 1 into Night 2. Final regression passed 53 live checks (84 total including lifecycle checks), including cancellation of an in-flight browser join; evidence: Logs/LobbyBrowserVerification.json and Logs/LobbyBrowserRun-114702/ role logs. Development and release-style C# compilation passed; the opt-in integration launcher is excluded from release builds. Visual hosting chooser and browser empty-state were inspected in editor Play Mode.

## Jumpscare and spectator completion (2026-11-01)
- Resumed the interrupted jumpscare fix. Saved source was present; the prepared multiplayer regression had not been built or run.
- Victim-local LocalJumpscarePresentation provides one continuous timed image above the death fade, with expiry and destruction cleanup. ZombieAI, JumpscareSystem, Rush and Surveyor use it. Shared scares clear their active flag and never restore dead-player controls.
- PlayerDeathSpectator waits for replicated death fields to finish applying and for the scare to expire before fading into a survivor camera; it maintains one local audio listener and cleans camera/overlay resources on destruction.
- Unity development build succeeded with zero errors. Three standalone processes using real NGO/Unity Transport over localhost passed seven scenarios and 100 assertions: host 45, client1 14, client2 41. Covered actual collisions with both Movement Detected instances, shared scare, Rush/Surveyor presentation routines, oxygen death and a 5.2-second scare exceeding the normal spectator delay. Verified continuous image state, timed clearing/no recurrence, victim-only effects, survivor view, disabled dead-player controls, cleared fade, survivor spectating and exactly one active audio listener.
- Evidence: Logs/DeathPresentationBuildResult.txt, Logs/DeathPresentation-host-PASS.txt, Logs/DeathPresentation-client1-PASS.txt, Logs/DeathPresentation-client2-PASS.txt and corresponding logs. Regression is opt-in/development-only; its generated scene is excluded from normal Build Settings. Headless local multiplayer testing does not replace manual visual/audio playtesting or a Relay session. Shared scripts apply to both nights. Lobby-browser/Relay code and gameplay scenes were unchanged during completion.

## Shared bunker drill (2026-11-01)
- Saved the user's unsaved Night1_MultiplayerTest drill placement, then installed a Drill Station using the labeled drill, drill screen and drill button plus the adjacent switch housing/stand. Preserved world poses/materials; added a world-space status/resource display. Copied the tested station to Night 2's corresponding bunker position. Reusable prefab: Assets/Prefabs/DrillStation.prefab. Both nights have exactly one wired station and validated scene-specific NGO identity; all 14 existing Night 1 network identities were preserved.
- Look at the switch within 3m and press E to toggle; starts disabled. One independent host roll per active second for metal 1/30, gold 1/80, plasma 1/120; multiple successes are possible. Power/solar outage and successful radar/defense use synchronously cancel production. Partial seconds are discarded; manual restart required. Existing materials remain. Existing system scripts each received only one cancellation call.
- DrillSnapshot atomically replicates enabled state and all shared team totals; no per-player payouts or client random rolls. Server switch RPC validates sender/player, alive/movement state, proximity, line of sight and power. DrillSessionStockpile on the persistent session NetworkManager preserves materials across nights and clears on host shutdown. Direct offline Play uses separate local state. Crafting recipes/spending are not implemented.
- Development build succeeded with zero errors; release-style C# compilation passed. Final run 222242 passed 128 assertions across three real NGO/Unity Transport standalone processes (host 74, clients 27 each): production timing and odds, interruption/restore behavior, real equipment requests/cooldowns, invalid-request rejection, replicated enabled/disabled screen and totals, late join, network scene transfer and session reset. These were isolated scenes using real station/player prefabs and game system scripts, not a full Relay/full-level multiplayer playthrough. Both actual levels separately passed switch reach/collider checks, screen state checks, rendered visual inspection and network identity preflight. Evidence: Logs/DrillVerification.json and run role logs; Logs/DrillSceneValidation.txt; Logs/DrillPlacementInspection.txt; Logs/DrillNight1.png and DrillNight2.png. Initial failures were fixture placement mistakes, corrected before the final passing run.
- Controls/setup/testing notes: Assets/_Project/DrillNotes.md. Backup of pre-install scenes and affected original scripts: Logs/DrillBackup. Test scenes remain excluded from normal Build Settings; opt-in development fixture does not run in normal games. Work is local and not pushed.

## Watcher death presentation (2026-11-01)
- Added a 2-second victim-only Watcher death close-up with an original 1.15-second distorted sound, using the actual ManThing model rendered in Unity. Assets are Assets/Art/Watcher/WatcherDeath.png and Assets/Audio/Watcher/WatcherDeath.wav, assigned in Night 2 and reusable PowerStationWatcher prefab. No Night 1 Watcher added.
- Validated server encounter failure targets only the killed client with a death ClientRpc; encounter deduplication prevents replay. Offline failure also shows the timed local image. Existing LocalJumpscarePresentation/PlayerDeathSpectator handle clearing and spectator handoff; patrol/encounter tuning and dormancy are preserved. Scene network identity preflight passed and pre-existing Night 2 IDs were unchanged.
- Development build zero errors; release-style C# compile passed. Nine death scenarios passed 142 assertions (host 66, clients 56/20), including Watcher kills through the real activity-report RPC for client and host, correct asset/audio, continuous timer, victim-only effects, cleared overlays, audio listener/control state, spectating and duplicate-event suppression. Evidence: Logs/WatcherDeathVerification.json, referenced role logs and Logs/DeathPresentation-*-PASS.txt. Headless localhost test with real gameplay components; manual full Night 2/Relay audiovisual playtest not performed. Native art inspected, audio metadata/peak checked. Notes: Assets/_Project/WatcherNotes.md; backup: Logs/WatcherDeathBackup. Saved locally, not pushed.

## Plasma revive stations and radio removal (2026-11-01)
- Converted the user's revised Night 1 console and adjacent monitor, plus the corresponding Night 2 setup, into one networked Revive Station per scene. Preserved placed geometry/materials, added shared name/status/countdown UI, and assigned each scene's NetworkStartPosition_1 (exposed respawnPoint field). Q cycles dead teammates globally; E pays 1 shared plasma for the displayed teammate. Stale/invalid selections, insufficient plasma, distant/obstructed/dead/disabled requesters, and duplicate/concurrent requests cannot charge/start an invalid job.
- ReviveSession on the persistent NetworkManager owns the immutable 15-second server-clock job, independent of station power, radar/defense, operator presence/death and scaled time; it survives night transfers. ReviveAudioGate silences only the target's Unity audio and Vivox input/output, retaining their previous settings and a visible countdown. Fresh owned player prefab replaces the dead object at the respawn point, restoring oxygen/body/control/camera and progression survivor membership. A disconnected target clears the impossible job with a one-plasma refund. Party shutdown cleans local silence.
- Shared DrillSessionStockpile has atomic server-only plasma spend/refund and change notifications so drill screens mirror currency immediately. Added one public display-name getter to RelayPartyManager and MarkRevived to NetworkLevelProgression. PlayerDeathSpectator coordinates mute ownership with revive audio; ordinary death behavior remains in place.
- Deleted RadioBroadcastStation source/meta. VoiceChatManager no longer auto-attaches radio interactions, creates the _RADIO group channel or exposes broadcast controls. Existing positional proximity voice remains. All pre-existing Night 1 (15) and Night 2 (16) scene NetworkObject IDs preserved; new station IDs/preflight passed. Baseline scenes/scripts and removed radio source: Logs/ReviveBackup.
- Final development build succeeded with zero errors; release-style C# compile and whitespace checks passed. Run 234331 passed 172 assertions in three real localhost NGO/Unity Transport processes (host 95, client1 43, client2 34), including selection/UI/currency replication, missing/stale target checks, no currency duplication, full silent countdown, actual outage/radar/defense/operator interruptions, scaled-time pause, on-time host/client respawns, fresh oxygen/control/audio/one listener, roster restoration, persistent job across a network scene load, and disconnect refund/mute cleanup. Isolated scenes used real prefabs/system scripts. The fixture mirrors progression's scene-complete missing-player spawn pass and isolates external movement input for spawn-position assertions; earlier expanded fixture runs exposed those test setup issues, corrected before the final pass.
- Both actual night scenes separately passed reference/aiming/identity checks and rendered display inspection. Evidence: Logs/ReviveVerification.json (source hashes and run role logs), Logs/RevivePlacementResult.txt, ReviveNight1.png, ReviveNight2.png. This was not a full-level Relay playthrough or live Vivox listening test; local mute intent/cleanup was tested and SDK mute APIs verified against installed IVivoxService. Controls/implementation/limits: Assets/_Project/ReviveNotes.md. Reusable prefab Assets/Prefabs/ReviveStation.prefab is registered. Test scenes remain excluded from normal Build Settings; opt-in development tests/input-freezing never run during normal games. Saved locally, not pushed.

## PA correction (2026-11-02)
- User clarified that the PA speak-to-everyone system must remain. Restored the exact RadioBroadcastStation script/meta from Logs/ReviveBackup, plus original VoiceChatManager radio group channel, begin/end transmission and automatic PA attachment. Preserved all revive mute handling, revive stations, shared plasma, countdown and respawn code. Existing wall PA object named Radio remains the activation point in both nights. C# compilation and source restoration checks passed; no scene geometry changed during this correction.
