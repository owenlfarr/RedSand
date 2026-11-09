# Night 3 additions

PA Screecher installs automatically through NightTimeManager on scenes named Night 3, Night 4, and higher. Each night has a 25% chance of no screechers at all. Otherwise the server allows at most two or three events, first checking after 120 seconds, then every 90 seconds at 35% chance while power is on. Each event lasts 5?15 seconds, with at least 180 seconds of recovery. Fewer events can occur if checks fail or power is out. Its replicated server-time deadline keeps everyone, including late joiners, on the same event. Earlier nights never install it.

The generated PA feedback masks normal game audio to 4% and interrupts Vivox input/output and radio. Both restore at expiry or scene cleanup, preserving existing revive/device mute state. The feedback respects the listener volume the player had before the event. An optional custom clip can replace it on PAScreecher. External voice apps are unaffected.

A host who survives Night 1 -> Night 2 -> Night 3 without restarting or disconnecting earns a persistent local Night 2 starting option. Launching Night 3 directly or starting Night 2 does not earn the original unlock. Host death invalidates that run. The unlock is saved in PlayerPrefs on that computer and does not transfer to joining players. A banner announces the unlock; future host lobbies offer a Starting night button. Night 1 remains available and is the default until changed.

Tools > Night Testing > Check PA and Host Unlock runs isolated editor checks without granting a real unlock or changing save preferences. Logs/NightThreeFeaturesResult.txt records the result. Live multiplayer voice/audio balance still needs a connected-party playtest.

The host start selector is strictly Night 1 / Night 2. Completing any later night never unlocks starts on Nights 3, 4, 5, or 6.

Local persistence is automatic: the earned Night 2 unlock and selected starting night are flushed with PlayerPrefs.Save, as are sensitivity, volume, VSync, FPS limit/counter, player name and confirmed display settings. Earning the unlock once keeps it on that computer across game restarts and new sessions; selecting Night 1 does not revoke it. Joining another host does not replace these local preferences. Display changes still require Apply/Keep Changes before they are saved.

Restart verification: separate write and read standalone processes passed the actual unlock/selection and settings UI save/load checks under an isolated test company/product, without changing real player preferences. `LocalPersistenceCheckBuild.Build` creates that fixture; run with `-localSaveTest write`, then `-localSaveTest read`. Evidence: `C:/Users/rhode/RedSand-Verification/Logs/LocalSave-write-PASS.txt` and `LocalSave-read-PASS.txt`.
