# Eye Monster

Installed in `Assets/Scenes/Night 2.unity`. Night 1 has no Eye Monster. Use `Assets/Prefabs/EyeMonster.prefab` in later night scenes and assign its **Bunker Entrance** anchor.

- Patrol radius: 65 m around **Eye Patrol - Bunker Entrance**.
- Detection: nearest living player within 40 m, in any direction.
- Chase: 15 m/s (player sprint is 10 m/s), directly toward the nearest player.
- Any living player's game camera seeing any sampled visible part of the monster freezes movement and animation for everyone, including while roaming. Solid walls block observation.
- The host owns movement, target selection, contact deaths, and replicated position/state. Each client reports its own camera at 20 Hz. A missing/stalled living player's report freezes conservatively until reporting resumes or the player disconnects.
- Dead players cannot hold the monster frozen. Late joiners receive current network state.
- Footsteps are fully 3D at the monster's feet with zero spread/Doppler, moderate nearby (2 m) and fading to silence by 35 m; solid walls muffle them to 8%. Host-issued step events follow real travel distance (about five steps/second during a full-speed chase); frozen or blocked movement produces no new steps. Assign a custom Footstep clip to replace the generated thump.
- Contact remains lethal while watched/frozen. Contact range uses the character body, with an obstruction check to prevent kills through solid walls. The host marks only the victim dead and sends that owner the existing monster death/spectator presentation.
- The host snaps the motor to terrain/floors at spawn and follows descending ground while moving. All peers align the actual animated skinned mesh soles to the motor floor, including while frozen.
- The placed visual keeps its original scale. Its duplicate child Animator is disabled so only the root drives the skeleton. Dedicated looping humanoid walk/run clips use an Eye-only avatar, preserving its model scale. Visibility samples actual animated skin points instead of empty bounding-box corners.

Select **Eye Monster** to adjust range, speeds, kill distance, and patrol radius. The selected-object gizmos show patrol and chase radii.

`Tools > Eye Monster > Build Network Checks` generates an isolated test build. Run it once with `-eyeTest host` and once with `-eyeTest client` from the project directory. Results are written to `Logs/Eye-host-PASS.txt` and `Logs/Eye-client-PASS.txt`. Production scenes and startup are not altered by the test coordinator.
