# Night 3 startup synchronization and audio

The camera failure reported after Start Game came from a Netcode scene synchronization exception, before the player and its camera could spawn. The drill prefab instance copied from Night 2 retained the saved hash 61371443 in Nights 3, 4 and 5. Unity Editor regenerates the identity from each scene GUID, while standalone builds consume the serialized override, so Editor/build peers disagreed. Night 3's expected 2537135025 matched the missing hash in the user's log. The three scene overrides now contain their own canonical IDs.

The build preflight now validates NetworkObject hashes stored as prefab instance overrides as well as ordinary MonoBehaviour blocks. A regression deliberately substitutes the stale override, requires rejection, restores the exact original bytes and verifies the valid scenes pass. Tools > Networking > Repair Scene Network IDs refreshes and saves these IDs through NGO's validation routine; it requires Edit Mode and saved open scenes.

The persistent night curtain now has a black camera with no listener or scene culling. It covers the interval between outgoing scene cameras and incoming player cameras, preventing Unity's no-camera banner during the handoff. It disables again when the curtain is clear.

The blind monster heartbeat starts only when a living local player has an active player camera, the transition has ended, the player is outside the physical bunker zone and within the existing 26 metre sound range. It stops when these conditions no longer hold. Fallback/loading cameras no longer cause premature audio. Lunge sounds, AI and heartbeat volume/range are unchanged.

`NightStartupNetworkTest` is opt-in in development builds (`-nightStartupTest host` or `client`). It launches the actual production MainMenu, uses the real lobby approval and Start Match scene loading path over local Unity Transport, and verifies Night 3 spawning, both owning cameras, the curtain handoff, bunker silence and the exterior heartbeat cue. It does not allocate a Relay lobby or modify saved unlocks.

Validation: all production scenes build successfully; saved IDs pass validation and a deliberately stale override is rejected. A graphics-enabled host/client production build passed 17 startup assertions, including active owning cameras and the heartbeat starting outside and stopping upon bunker return. No scene synchronization, network spawn, shader or managed exception errors appeared in either test log.
