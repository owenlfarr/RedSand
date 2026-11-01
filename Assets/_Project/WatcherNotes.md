# Power station watcher

Night 2 contains `Power Station Watcher`, using the existing placed `ManThing (1)` visual. Night 1 contains no watcher. Reuse `Assets/Prefabs/PowerStationWatcher.prefab` in future nights, then assign that scene's power station as Patrol Center.

Select the watcher in Unity to see the cyan rectangular patrol bounds. Patrol Half Extents currently spans 55 metres on X and 35 metres on Z from the Powerbox. Patrol speed is 3.5 m/s; vision is 45 metres within a 180-degree field of view, blocked by solid geometry. Bunker players and dead players are excluded.

The host selects one visible surface player at a time and chooses a random 5–10 second stillness period. That player has 1.5 seconds to stop walking, sprinting, jumping, turning, or operating tools. Controls remain responsive: violating the rule after the grace period kills that player through the existing network death/spectator system. Existing held tools do not fail the encounter unless the player operates them.

Only the victim receives the faint static and quiet signal cue. Static increases throughout the encounter and fades over 2 seconds after it ends. The monster becomes invisible/non-colliding for 45 seconds, then reappears on valid patrol ground and resumes roaming. Its scene NetworkObject stays registered during this absence so late joiners receive the correct dormant state.

Host-owned state synchronizes pose, target, encounter ID, timings and visibility. Activity reports validate the sender, encounter and sample time. The host also checks actual movement and replicated camera pitch. An absent activity heartbeat times out after 2.5 seconds. Disconnects, death, entering a bunker and scene unload clean up the encounter.

Tools > Watcher provides explicit install/validation and an isolated local multiplayer test build. The test scene is not added to normal Build Settings. Test results go to Logs/WatcherNetworkTestResult.txt, with separate host/client logs.

Validation on 2026-11-01: the Unity development build succeeded with zero errors; the real localhost host/client test passed 67 assertions. Evidence is in Logs/WatcherNetworkTestResult.txt, WatcherHost2.log and WatcherClient2.log. The first test run found a high-frame-rate motor issue; setting minimum movement distance to zero fixed it and the full suite passed on rerun. The tests use the real monster prefab with minimal actors and run headlessly, so a full Night 2 Relay session and subjective visual/audio tuning remain separate checks.

Direct scene Play is also supported: opening Night 2 and pressing Play without a host runs local roaming and encounters. Active network sessions use host authority. Direct Play was verified in the actual Night 2 scene: 7.539m movement in 5s without network spawn; see Logs/WatcherDirectPlayResult.txt. This local path never writes NetworkVariables or sends RPCs and does not restart after multiplayer teardown.

