# Nights 4 and 5

Night 3 now leads to Night 4, then Night 5. Night 5 finishes with the same 8 AM card and returns the party to the main menu. Both new scenes clone the saved Night 3 map and retain its equipment, geometry, night duration, resource rules and existing encounters. The lunge monster and PA screecher occur only from Night 3 onward.

## Small difficulty steps

Relative to Night 3, existing monster movement speeds are multiplied by 1.05 in Night 4 and 1.1025 in Night 5 (another 5%). Motion AI wander jitter is divided by the same multiplier for slightly more accurate pursuit. Chase memory increases by the square root of the multiplier; lunge windup, Watcher reaction time, and Surveyor neglect threshold decrease by that small factor. Rush and solar-flare event odds increase by one percentage point per night. PA event odds are 36%/37% per eligible check; quiet-night probability, event budget, volume and recovery remain unchanged.

Power-outage odds are 23% in Night 4 and 26% in Night 5, at the existing 55-second check interval. Radar has 12 successful scans before repair from Night 4 onward. Earlier capacities, repair duration, costs, interruptions, and shared team ownership retain their existing rules.

## Whisper (Night 4 onward)

A partially transparent cube with no footstep audio. It roams, spots an exposed living player within 32 metres through unobstructed sight, emits a quiet positional whisper/laugh, and gives a 0.75-second warning before pursuit. Speed is 10.5 m/s in Night 4 and 11.025 in Night 5.

A real enabled player flashlight aimed at it within the beam's range/angle and unobstructed line of sight makes it retreat quickly around the target. After at least two seconds and leaving that player's view it warns again and attacks from its new angle. Illumination delays pursuit rather than ending the encounter. Another living player within six metres of the victim and twelve metres of the monster can receive the pursuit; transfer has a cooldown to prevent rapid target swapping. Returning to the bunker breaks pursuit. Dead, disconnected, or replaced player objects are not valid targets.

## Burrow (Night 5)

A brown cube while roaming. On spotting a living exposed player within 32 metres, it gives a 1.2-second positional warning, hides its cube and collision body, and approaches at 18 m/s along the terrain. A world-space sand/smoke trail and looping burrowing sound identify its approach.

Jump as it reaches your feet: your feet must clear the sand by more than 0.35 metres and your movement state must be airborne. A short 0.18-second host-side strike grace accommodates delivery of the remote jump state. Simply running past its approach does not consume the attack and grant a free cooldown. A successful jump makes it disappear for a full 30 seconds before respawning at its original roaming area. A completed lethal strike also removes it for 30 seconds. Bunker occupants are excluded.

## Networking and validation

SandStalker runs acquisition, movement, flashlight checks, handoffs, strike resolution, death flags and cooldowns only on the server. Clients render replicated position, phase, target identity, warning sequence, and dormancy deadline. Existing player-owned movement/pitch and server-approved flashlight state provide the counters. Death presentation is targeted only to the victim. Client motors are disabled; despawn/shutdown stops audio and particles. Late joiners receive current state without replaying old warning events.

LaterNightChecks installs and validates scenes in the isolated verification project, regenerates canonical scene NetworkObject identities, builds the multiplayer fixture and a development build of every production scene. StalkerNetworkTest exercises the actual gameplay component across standalone NGO/Unity Transport processes. LaterNightTransferTest exercises the actual campaign scene loader and clock completion, including a dead client across Night 3 -> 4 -> 5. These opt-in test actors are excluded from production scenes; the transfer bootstrap is excluded from non-development builds.

Evidence lives in Logs/LaterNightsVerification.json and the referenced logs/results. Localhost tests do not establish real Relay latency or final human audio balance; those remain manual playtest checks. Placeholder renders are Logs/LaterNightsCubes.png and Logs/LaterNightsBurrowSmoke.png.

## Seamless campaign transitions

The persistent NightTransition canvas covers every player, including spectators. Starting from the lobby fades to black; night completion fades for 0.8 seconds, shows centered `8 AM` for 1.25 seconds, fades that text out over 0.35 seconds, and holds plain black for 0.4 seconds. The host waits for an acknowledgement that every connected player's screen is fully black before loading. Reliable repeated begin messages tolerate a client's handler becoming available just after connection; invalid/stale acknowledgements are ignored. A peer that cannot acknowledge within 18 seconds is disconnected before loading.

After NGO reports scene completion and the normal player-spawn pass completes, each peer waits for its spawned local player and scene clock. It shows `NIGHT 1` through `NIGHT 5` on black, then fades into the scene. The curtain persists across scene changes, so the camera, UI and delayed player spawns remain covered. Gameplay and oxygen updates are held during the card; NGO continues processing. The old GameIntro black overlay is suppressed to avoid competing fades. Shutdown, canceled loads and the final menu return release the curtain safely.

The final sweep also covers a new party after completing the campaign, PA takeover/voice cleanup, lunge/rock RPCs and late joining, and shared radar wear/repair races and cancellation. Fixed during the sweep: reset transition tokens between network sessions; reject stale reveal messages after cancel; select Whisper handoff targets from eligible nearby teammates; stop spectator fades safely when their scene-root image unloads; use the replicated server-time deadline for Burrow's full cooldown even under low frame rates.

## Final sweep (2026-11-09)

Passed 130 host-side multiplayer assertions: new encounters 30; existing lunge/PA 36; radar wear/repair 23; real five-night progression, spectator persistence, final menu return and new-party reconnect 41. Additional editor checks passed 25 scene assertions, 28 PA/unlock assertions and 14 lunge core assertions. Canonical NGO scene identity preflight and complete production-scene development build passed with zero errors.

The final campaign fixture uses scene-owned players, exercising real destruction/respawn at each night rather than retaining starter actors at the fixture origin. Regression actors run only with their explicit test argument. Final log prefix: `Logs/FinalSweep-`; manifest: `Logs/LaterNightsVerification.json`. The final successful role logs have no managed exceptions or scene-spawn/synchronization failures. Earlier failed iterations are retained separately; a stale earlier `*-FAIL.txt` is not the outcome of the timestamped final run.
