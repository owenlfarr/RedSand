# Astronaut action animations

The six downloaded generic-rig FBXs are preserved under `Assets/Art/Animations/Astronaut`. Their original GUIDs are preserved. Extracted, in-place clips live in `Assets/Animations/Player`.

The existing astronaut controller keeps its original GUID and avatar. Walking blends idle, forward/backward and the new walking strafes. Running blends idle, forward sprint, backward running and running strafes. Diagonals blend adjacent directions. Existing speeds, jump physics and stamina settings are unchanged.

`PlayerController` publishes an owner-written `PlayerAnimationSnapshot` after movement. Direction, sprint, airborne state and jump/landing timestamps travel together through NGO. Each `PlayerBodyAnimator` samples the same snapshot using the synchronized server clock, including players joining during a jump. No per-frame animation RPC or competing NetworkAnimator is required.

The jump starts at normalized time 0.33, plays to 0.48, and holds that pose throughout the remaining airtime. Actual CharacterController ground contact starts the landing section at 0.60, then returns to directional locomotion at 0.80. Walking off a ledge starts directly in the airborne pose. The clip's baked horizontal motion and upward root translation are removed; the CharacterController remains responsible for world movement. Death/ragdoll and movement suppression retain control over presentation.

Editor menu: Tools > Player Animations > Install Directional and Jump Animations. This regenerates the clips/controller without replacing their GUIDs. `AstronautAnimationSetup.InstallAndBuild` also validates all ten original/new clips against the astronaut skeleton and builds production scenes plus an isolated multiplayer fixture. The fixture uses the actual player components and separate host/client/late observer processes over Unity Transport. It checks directional clips on both peers, owner-only writes, held bone pose, actual ground-contact recovery, ledge falls, repeated jumps and late synchronization.

All multiplayer participants must use the updated build because the network movement schema now contains the animation snapshot.

The four strafe clips also have their source heading offsets corrected (+90 degrees for left, -90 for right), so the astronaut retains its forward-facing direction while moving sideways.

Verification: full enabled-scene Windows development build and skeleton binding validation passed with zero errors. Separate host/client/late-joining client transport tests passed 30 assertions; their logs contained no network errors or exceptions. The rendered pose sheet is in `Logs/Astronaut-contact-sheet.png`.
