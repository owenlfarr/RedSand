# Blind Lunge Monster

Installed in Night 3. Reusable prefab: Assets/Prefabs/BlindLungeMonster.prefab. The four-sided labeled cube is the Visual child: replace that child and reassign the visual field when the final model is ready. Keep the root NetworkObject, CharacterController and BlindLungeMonster component to retain gameplay.

The host controls hearing, movement, deaths, searches, inventory and rock impacts. The monster has no sight or silent-player tracking. Walks are audible to 12 m, sprinting to 32 m, landings and in-game speech to 26 m, and rock impacts to the 32 m hearing limit. Solid walls muffle sound. The PA anomaly also attracts it. In-game voice speech detection sends only the speaking player's location, with rate limiting; external voice apps cannot be detected.

It commits to a straight lunge of up to 10 m at 32 m/s, after a 0.35 s windup, then recovers for 1.25 s. Walls stop the lunge. Contact during a lunge kills through the existing death/spectator flow. It remembers a sound for eight seconds and stops when it reaches the sound or the memory expires. A rock impact briefly takes priority over ordinary footsteps. A faint 3D heartbeat fades out by 26 m.

Look at nearby ground and hold E for one second to search. Each completed search has a 10% success chance and can start only once every 2.5 seconds. The result says Found one small rock or Nothing found and fades. Releasing E, looking away from reachable ground, opening a menu, or dying interrupts the search without a roll. Release E before starting another attempt. The host validates held-input heartbeats throughout the search. There are no placed collectible rocks.

Carry one small rock at a time. A found rock appears as an actual rotating 3D model in the bottom-left HUD preview. Left-click throws it along the view direction and consumes it. The host simulates the projectile and detects its first impact; all peers receive the same launch and landing, and only that authoritative impact creates distraction noise for Blind Lunge.

Controls are suppressed while menus are open, dead, or not the local owner. Requests cannot choose another player's inventory or search/throw origin. Searches are limited to ground near the actual feet, independently of where the player model pivot is placed.

Tools > Blind Lunge > Check Core Behaviour runs isolated core checks. Logs/BlindLungeChecksResult.txt contains results. The request-driven BlindLungePlayChecks runs an offline play-mode integration test without saving test changes into production scenes. A live host/client session remains necessary to verify multiplayer input/latency, contact deaths and audio balance together.

## Multiplayer verification

BlindLungeNetworkChecks builds an isolated test scene into Logs/BlindNetworkBuild. Run the executable with -blindTest host, -blindTest client, and, once Logs/Blind-latejoin.ready exists, -blindTest late. The host, joined client and late joiner each write PASS/FAIL results under Logs. The verified run passed 22 assertions for remote sprint noise, replicated movement and phase, per-player search cooldown and replies, server-owned inventory, shared rock launch/impact, speech reports, late-join position, and owner-only deaths for both host and joined players.

Clients apply replicated phase and initial position without enabling their monster motor. Heartbeats use server time for playback alignment. After scene synchronization, the host replays any in-flight rock trajectories to the joining client, using their original server launch time and suppressing duplicate visuals. Disconnect/despawn clears callbacks, player tracking, active flights and flight visuals. Server death clears carried rocks. The three-process test uses real UDP networking on localhost; Internet latency and audio loudness still depend on a normal party playtest.

Held-search correction: 29 fresh host/client/late-join assertions passed, including a successful held search supplying the actual thrown rock, release cancellation without a later award, shared impact distraction and owner-only deaths. Actual Night 3 direct Play also verified the exact 10% success band, Nothing found on failure, interruption on release, and a nonempty rendered 3D model on success. Placed collectible rocks and their pickup API were removed. Evidence: `C:/Users/rhode/RedSand-Verification/Logs/HeldSearchOfflineResult.txt` and `Blind-ROLE-PASS.txt`.
