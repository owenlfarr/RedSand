# Plasma revive station

Installed in Night1_MultiplayerTest and Night 2 using the converted console and adjacent wall monitor. Their placed geometry/materials were preserved. Both stations use their scene's `NetworkStartPosition_1` as the respawn point; the reference can be reassigned in the ReviveStation inspector.

## Player controls

Look at the revive monitor within four metres. **Q** cycles fallen teammates; **E** spends **one shared plasma** to revive the displayed teammate. Selection, target name, active job and countdown are shared across the party. With no fallen teammates or insufficient plasma, activation consumes nothing. A stale selection request is rejected rather than silently reviving a different player.

Activation is a single press, not a hold. Once paid, the station runs an uninterrupted **15-second server-clock countdown**. Power outages, solar/system lockouts, radar, defense, the operator leaving/dying, and scaled-time pauses do not cancel/reset it. The paid job is owned by the persistent NetworkManager's ReviveSession and carries across night loading. If scene loading overlaps its deadline, replacement waits for the progression transfer to finish.

Only the target's game audio and proximity voice input/output are muted during reconstruction. Their countdown remains visible while spectating. Audio settings are restored after their replacement player arrives; party shutdown/disconnect also releases local silence. The station removes the old dead player and spawns a fresh NetworkRealPlayer with the same client ownership at the assigned point, restoring oxygen, movement, camera, animation and living state. The campaign survivor roster is updated so the revived teammate remains playable in subsequent nights.

One job runs at a time. The server validates the activating player, distance, obstruction, living/movement state, selected dead target and available plasma, then atomically charges the shared stockpile. Repeated/concurrent requests cannot double-charge. Other resources are untouched. A target who disconnects before completion cannot be reconstructed; that job is cleared and its plasma returned once.

## Radio removal and networking

RadioBroadcastStation.cs and its metadata were removed. VoiceChatManager no longer attaches broadcast components, joins a `_RADIO` group, or exposes broadcast actions. Existing proximity chat remains. The revive mute uses Vivox's local input/output mute APIs and preserves their prior mute settings. Physical meshes from the converted radio are retained as console artwork.

ReviveStation replicates one ReviveSnapshot (selected client/name, active flag, old player object ID, server completion timestamp). Each screen computes remaining seconds from the synchronized server clock. ReviveAudioGate lives on the persistent local manager and applies only to the selected client. DrillSessionStockpile now notifies the drill of spending/refunds so its wall totals remain synchronized immediately.

Existing Night 1/Night 2 scene NetworkObject identities were preserved. A new station object was added in each scene and the reusable ReviveStation prefab is registered with NGO. Rebuild matching players together. The baseline scenes and affected scripts, including removed radio source, are preserved in Logs/ReviveBackup.

## Verification

Evidence for the final passing run is in Logs/ReviveVerification.json and its referenced host/client logs. The opt-in regression uses three real NGO/Unity Transport processes on localhost with the real console/player/drill prefabs and actual power/radar/defense components. It covers target cycling, missing/stale targets, insufficient plasma, distance/wall rejection, simultaneous charging, sustained victim silence, host/client revival, full oxygen/control/audio restoration, power/equipment/operator interruptions, scaled-time pause, roster restoration, scene transfer, configured spawn points and disconnect refunds.

Both actual night scenes were separately checked for valid references, screen aiming colliders, rendered display appearance and network identity preflight (Logs/RevivePlacementResult.txt, ReviveNight1.png, ReviveNight2.png). The regression is headless and isolated; a manual full-level Relay playthrough and live Vivox listening test were not performed. Vivox mute intent/cleanup was checked without initializing the external voice service, and the SDK calls were verified against the installed IVivoxService implementation.

Tools > Revive contains explicit installation, scene inspection and regression-build commands. Generated test scenes remain excluded from normal Build Settings. The development-only test starts only with `-reviveTest`; normal games never receive its resource seeding or input-freezing helper.

Final run 234331: 172 assertions passed (host 95, client1 43, client2 34). Development build zero errors and release-style C# compilation passed. The target clients were observed silent over hundreds of consecutive frames; controls/audio restored at respawn. Early fixture failures involved missing test-scene replacement actors and input-sensitive post-respawn position assertions, corrected before the final passing run.

## PA clarification (2026-11-02)
The PA broadcast function is restored per the user's correction. Hold E while looking at the existing wall PA to speak to everyone. Original radio channel/transmission/attachment paths and the RadioBroadcastStation component/GUID are restored. Proximity chat and the revive-only silence remain. This supersedes the earlier radio-removal description; revive console geometry and mechanics are unchanged.
