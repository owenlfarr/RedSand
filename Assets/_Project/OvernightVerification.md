# Overnight verification — 2026-11-08

Verified the current overnight changes in an isolated project copy with Unity 6000.0.32f1.

- All enabled production scenes built successfully, with zero build errors. Network scene identity preflight passed.
- 28 editor assertions passed for PA gating, duration limits, hearing/voice mute restoration, revive coexistence, unlock eligibility and campaign loading regression.
- 13 Blind Lunge core assertions passed.
- Fresh standalone network builds succeeded with zero build errors.
- Eye: 21 host/client assertions passed, including remote observation/freeze, movement, animation, footsteps, and owner-specific contact deaths.
- Radar: 23 host/client/late-join assertions passed, including wear, pricing, atomic metal payment, repair races, cancellation and synchronized inventory.
- Blind Lunge and PA: 26 host/client/late-join assertions passed, including remote noise, rocks, inventory, owner-specific deaths, PA hearing suppression, ongoing-event late joining and restoration on all peers.

Fixed the campaign tracker resetting while the initial menu-to-night network scene load was pending. Disconnect and return-to-menu resets remain covered by regression checks.

Evidence is in `C:/Users/rhode/RedSand-Verification/Logs` and its Unity build logs. Network tests used separate Windows processes with real NGO/UDP on localhost. Internet latency, live Vivox speech/audio balance, and visual feel were not verified by these headless tests. Night 1's existing five-second quick-test setting was preserved.

Rerun fresh builds through `OvernightReleaseChecks.Run` in a separate Unity project copy. The Blind Lunge/PA fixture is saved under `Assets/_Project/Tests/BlindPA/Night 3.unity` so it exercises the real PA night gate and NGO scene synchronization.
