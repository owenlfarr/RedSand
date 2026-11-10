# Yeller

Installed in **Night 5** beside eight painted rocks. Each is a small orange cube placeholder (0.3 m), with no solid collider to obstruct movement. A reusable prefab is provided.

An exposed living player within **1.524 m (five feet)** and unobstructed line of sight triggers attachment. The cube follows that specific player's shoulder and emits a looping positional scream for **15 seconds**, timed against the host's network clock. A late join receives the existing deadline and current attachment, rather than starting another 15 seconds. Another bug cannot attach to the same player while one is already screaming.

The scream draws the active motion monsters, Eye, blind lunge, Watcher, Surveyor, Whisper and Burrow directly toward the attached player, regardless of their ordinary detection distance. The host alone selects targets, moves enemies and resolves contacts. Movement routes use the existing terrain/rock navigation where the monster has a CharacterController. Burrow retains its underground approach/jump mechanic. Dormant or neutralized enemies retain their cooldowns; the PA and Rush are scripted audio/raid events and are not moving creatures.

When the timer ends, forced attraction stops and each monster resumes its normal perception/patrol/chase or encounter behavior. Existing bunker exclusions remain in effect. Death, disconnect and scene despawn also clear attraction and stop the scream. Each Yeller is spent and disappears after its scream; it cannot immediately reattach and create an endless loop. Independent simultaneous screams select the nearest active victim for each monster.

Networking uses one atomic YellerState snapshot containing phase, victim ID, deadline and attachment position. Attachment requests are never accepted from clients. Clients resolve remote player objects through the spawn manager, follow the attachment locally, and stop audio at the shared deadline. Existing monster NetworkBehaviour ordering is unchanged; Surveyor has explicit replicated lure position/yaw so clients follow its temporary free movement.

Authoring/build tooling: `YellerChecks.InstallAndBuild`, run in the isolated verification project. It preserves the existing Night 5 layout and validates canonical network identities, then builds the encounter fixture and every enabled production scene. Test entry point: `YellerNetworkTest`, `-yellerTest host|client|late`. Evidence: `Logs/YellerVerification.json` and its referenced logs. Localhost NGO/Unity Transport tests do not establish real Relay latency or final human audio balance.

Validation: 39 assertions passed across real host/client/late-join processes; successful role logs have no managed exceptions or NGO spawn failures. All enabled production scenes and the fixture built with zero errors; canonical scene network identity preflight passed.
