# Shared bunker drill

Installed in `Night1_MultiplayerTest` and `Night 2`. Reusable station: `Assets/Prefabs/DrillStation.prefab`.

## Controls and production

Look at the red drill switch within 3 metres and press **E** to enable/disable. The drill starts disabled. The wall displays `DRILL: ENABLED`/`DRILL: DISABLED`, plus the team's metal, gold and plasma totals.

Each complete active second rolls independently for one metal (1/30), one gold (1/80), and one plasma (1/120). More than one material can succeed in a second. There is no guaranteed payout; the drill runs unattended until interrupted. Incomplete seconds are discarded when stopped. Adding players does not multiply production.

A power outage, solar flare, successful radar use or successful defense activation cancels drilling immediately. Restoring power or waiting through an equipment cooldown does not restart it; use the switch again. Already collected materials remain. Rejected equipment requests (for example during cooldown) do not interrupt the drill.

## Shared state

`DrillSystem.Team` is one server-written `DrillSnapshot`, containing enabled state and all material totals. Only the host performs random rolls. Every peer renders its own world screen from that same replicated snapshot, including players who connect later. Client switch requests validate the sender's actual player, distance, line of sight, alive/movement state and power on the host.

`DrillSessionStockpile` lives on the session's persistent NetworkManager. Team materials carry into the next night; its drill starts off. Ending the host session resets materials. These are session materials, not a disk save. Current values are available through `DrillSystem.Current.Metal`, `.Gold`, and `.Plasma` for future crafting work. No recipes or spending interface were added.

Pressing Play directly without a listening NetworkManager also supports local drill use. Offline preview uses local state and does not write NetworkVariables or send RPCs.

## Installation and preservation

The user's saved Night 1 drill, screen, red switch, and adjacent switch casing/stand were grouped under `Drill Station` while preserving their world transforms/materials. A world-space UI was added on the screen's bunker-facing surface. The tested station was copied into the corresponding Night 2 bunker position. Both scenes contain exactly one station with valid references and scene-specific NGO identity.

Existing power, radar, defense and solar-flare scripts each have one added cancellation call. Their approval, cooldown, effect and replication paths were retained. All 14 pre-existing Night 1 scene NetworkObject identities were preserved. A pre-install snapshot of both scenes and affected existing scripts is in `Logs/DrillBackup`.

## Verification

Development build succeeded with zero errors; release-style gameplay C# compilation passed. Three real standalone NGO/Unity Transport processes on localhost passed **128 assertions** (host 74; clients 27 each), including:

- Exact one-second production, independent rolls, no per-player duplication, duplicate enable requests, manual stopping and no inactive/partial-second production.
- Deterministic success/miss/overflow boundaries and seeded 600,000-tick checks of the requested probabilities.
- Real client switch RPCs, real power outage/restore, radar/defense request/approval/cooldown paths, and rejection of distant, obstructed, movement-disabled and dead-player requests.
- Enabled/disabled wall state and all three counters on every peer, late-join inventory, inventory retention across an NGO scene transfer, and reset on host shutdown.

The regression uses the real station/player prefabs and power/radar/defense components in isolated network scenes. It is not a full Relay session or a manual multiplayer playthrough of the actual levels. Both actual night scenes were separately checked for usable switch reach with their surrounding colliders, enable/disable screen updates, world-screen rendering, and NGO identity correctness.

Evidence: `Logs/DrillVerification.json`, run `222242` role logs, `Logs/DrillNetwork-*-PASS.txt`, `Logs/DrillSceneValidation.txt`, `Logs/DrillPlacementInspection.txt`, and `Logs/DrillNight1.png`/`DrillNight2.png`. Earlier failed run logs document test-fixture placement corrections; the verification JSON identifies the final passing run.

Tools > Drill contains explicit setup, copy, validation, inspection and network regression build commands. Test fixtures compile only in the editor/development builds, exist only in the excluded test scenes, and run only with `-drillTest`. Normal Build Settings were not changed. Rebuild all participating game players together to include the new scene objects and scripts.
