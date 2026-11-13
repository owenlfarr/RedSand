# Four-player crews (2026-11-10)

RedSand supports four total players (host plus three clients). Everyone should use the new build and its complete data folder: `Logs/FourPlayerRelease/Red Sand.exe`.

## Changes

- One shared four-player cap drives connection approval, public advertisements, counters and lobby display limits. A fifth player and joins after match start remain rejected.
- The menu clones its existing third astronaut at runtime for the fourth slot, with a synchronized name and the existing floating/billboard behavior. The four astronauts fit within the previous display bounds. The subtitle now says 1?4 players.
- The existing first spawn position is now registered as a `NetworkStartPosition` in all five campaign maps. No map geometry or network-object identities were changed.
- The stress monitor includes four living outside crew members. Its existing three-level stress scale and 512-byte network snapshot are unchanged; four worst-case 24-character CJK names fit.
- Revive, radar, defense, PA/voice, spectator and progression implementations already enumerate the live roster. They were retained. Equipment prices, resource yields, timings, difficulty and the existing Night 3 playtest starting override were retained.

## Verification

- Fresh development build of all enabled production scenes and normal Windows release build succeeded with zero errors. Canonical scene network identities and four registered markers per campaign map passed.
- Lobby lifecycle: 31 asynchronous checks. Stress thresholds and four-row UTF-8 capacity: 24 checks.
- Real cloud Lobby/Relay run `four-head-225536`: 164 assertions across five processes. Four connected players, fifth rejection, private/public discovery, menu astronauts/names, all four stress displays, fourth-player PA/proximity channel switching, three distinct spectator targets with HUD/compass/voice listener, all five nights with survivor/dead state retained, and a timed wipe returning all peers to the menu.
- Four-peer revive run `fourrevive-223705`: 282 assertions. Third fallen-teammate selection, fourth-player revival and operation, one plasma spend, real 15-second silence, radar/defense state on every peer, host revival across scene transfer, disconnect refund and completion after console removal.
- Four-peer death run `four-224159`: 164 assertions across nine scenarios, targeting the fourth player and host. Movement Detected collision, oxygen death, Surveyor/Rush/shared presentation, Watcher death RPC and duplicate rejection, survivor isolation, spectator/audio restoration.
- Four-astronaut menu preview visually inspected. Evidence and source hashes: `Logs/FourPlayerVerification.json`; screenshot: `Logs/FourPlayerMenuPreview.png`.

## Reproduce

Use the isolated verification project so fixture builders cannot replace an open authored scene. `FourPlayerChecks.Run` validates scenes and builds production/death/revive development players. Launch lobby roles `host`, `client`, `observer`, `fourth`, `reject` with `-lobbyBrowserTest ROLE UNIQUE_RUN`; launch revive roles `host`, `client1`, `client2`, `client3` with `-reviveTest ROLE UNIQUE_RUN`; launch death roles with `-deathPresentationTest ROLE`. All participants in a suite share a working directory for fixture handshakes. Test-only flags and fixtures are excluded from the normal release. The cloud fixture shortens Night 1 without changing saved clocks and restores host starting-night preferences.

Actual microphone mix/listening across four separate computers and long-session balance remain human playtest checks. These automated runs used separate processes on one PC, with real cloud Lobby/Relay for the campaign run.
