# November 4 playtest readiness

Fresh development regression build and non-development Windows build both succeeded with zero build errors on November 3. All following test results were generated from the current gameplay source during this audit; they are not the older November 1 results.

## Passed

- **Real Unity Relay / cloud lobby:** 78 assertions across four processes. Private/public creation, browser discovery/cancellation, counts, three-player limit, fourth-player rejection, late-join rejection, MainMenu → Night 1 → Night 2 with owned players, actual PA stress display data, remote death, spectator oxygen and compass changes for both survivors, settings/sensitivity, five-second wipe, network shutdown, and every peer returning to MainMenu. Vivox initialization/login/channel joins succeeded in the process logs.
- **Revival:** 195 assertions across host and two clients, including persistent paid countdown, currency, power/equipment/operator interruptions, real scene transfer, replacement ownership/spawn/oxygen/control, victim-only audio mute, mute release, and disconnect refund. Run `preplay2346`.
- **Death/jumpscares:** 144 assertions across nine scenarios and three peers, including both roaming monsters, Watcher host/client kills, victim-only images/sound assignments, continuous image timing/expiry, spectator handoff, disabled dead controls, and one active listener. Logs `Audit2-Death-*`.
- **Watcher:** 67 assertions with a real host/client, including real 45-second absence, randomized stillness, victim/bystander separation, activity-report checks, bunker/wall exclusion and patrol/respawn. Logs `Audit-Watcher-*`.
- **Drilling:** fresh three-peer regression passed, including shared resources, action validation, cooldown/equipment interactions, late join, scene transfer and shutdown reset. Host result records 72 assertions across its two scene phases; client logs contain their own additional checks. Run `preplay2350`.
- **Editor checks:** 31 lobby lifecycle checks, 24 stress threshold/UTF-8/scene checks, 11 stationary image/text layout checks. Saved PA screen renders visually inspected. MainMenu and both current nights have no missing script or script-to-object references in the static scan. Unity's canonical scene NetworkObject ID preflight passed during both builds.
- **Audio files:** 12 WAVs checked; none empty or digitally silent. This is a data check, not a listening/mix review.

## Fixed during this audit

- Spectator proximity-voice listening follows the watched camera while the speaking position remains at the local player; verified for both spectator targets over real Relay.

- Disconnect handling now uses one party-owned cleanup/menu-return path rather than two competing scene loads.
- Surveyor initializes its distance before network spawn, waits for spawn before updating, can actually retreat after radar scans, responds to offline radar, synchronizes client movement audio and plays/stops death audio on each peer. Its singleton clears on scene teardown.
- Power/flare/Rush/radar/defense/oxygen update loops wait for their network spawn before running networked gameplay.
- Ambience follows the currently watched player while spectating.
- Game-volume changes made while death/revival audio is muted remain saved and are respected when audio restores; the settings volume readout no longer reports the temporary mute as the user's selected volume.

The first expanded tests caught two obsolete test assumptions: Watcher image lookup still expected a child of the moving player, and a headless process was expected to physically capture a mouse. Those fixtures were corrected and rerun. No failure was waived as a passing gameplay result.

## Test tomorrow with people

1. Give everyone the same complete ZIP. Extract it; keep the executable with its Data folder and DLLs. Start through MainMenu. The party limit is three players total.
2. Check proximity voice at near/far distances and hold E at the PA. Confirm everyone can hear broadcasts, releasing E returns to proximity, and microphone/output levels are comfortable.
3. Have two players leave the bunker at different distances from monsters; compare PA stress names/levels and confirm entry/death removes their row.
4. Trigger a death: check fixed image/text, sound level, spectating both survivors, correct oxygen/compass and no doubled audio.
5. Collect plasma and revive a teammate. Check the silent 15-second reconstruction and restored audio/control, including after changing volume in settings.
6. Use radar/defense, repair power/flare, hide in separate locker slots, use the tablet and surveillance view. Check simultaneous interactions and notifications.
7. Open Escape settings while alive and spectating. Check actual mouse release/capture, sensitivity, resolution confirmation/revert and Disconnect.
8. Complete Night 1 with one dead teammate. Confirm all peers reach Night 2 with survivors playable and the dead teammate spectating.
9. Wipe the whole party. Confirm the five-second delay, all peers at MainMenu, and that the host can create another party afterward.
10. Include a host leaving the match, a client leaving, and at least one tester on another internet connection.

The automated runs were on one Windows machine. They cannot prove microphone loudness, GPU rendering on other systems, internet latency/packet loss, or every possible sequence. Native mouse capture and real listening remain manual checks. Night 2 is currently the last authored night; its end has no next-night destination.

Detailed evidence: `Logs/PlaytestVerification.json`, `Logs/PlaytestAudioAudit.json`, the fresh run logs named above, and the build result files. Older `*-FAIL.txt` files may remain from earlier iterations; use the run names and current verification manifest.
