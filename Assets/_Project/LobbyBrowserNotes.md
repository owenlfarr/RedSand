# Public/private lobby browser

Baseline before this feature: commit `1ca68fceeb1f47f2f7d297d79de3ef522d3a95db` on main and the local backup branch `backup/pre-lobby-browser-20261101-111858`. That baseline remains on GitHub until a separate push is requested.

## Player flow

- Create Lobby opens a Private/Public chooser. Private is the default and creates the same invite-code Relay lobby as before.
- Public creates the Relay host first, then advertises it through the already-installed Unity Lobby API. Invite codes work for public lobbies too.
- Browse Public Lobbies opens a small list with owner name, actual connected players out of three, and Join/Full controls. It refreshes every five seconds while open; Refresh is also available. Join Lobby with an empty invite code opens this same browser; entering a code preserves the original join behavior.
- A row is checked against the service again before joining. Full, started, private, missing, malformed or incompatible listings cannot be joined through cached rows. Closing the browser cancels a pending row join before it reaches Relay.
- Starting locks and hides the public advertisement before the existing network scene load begins. If that service call fails, the host can retry Start without losing its current party. If the existing scene-load step fails, public advertising is restored.
- Leaving or losing the host stops heartbeats and removes the listing. A crashed process naturally ages out under Unity Lobby's active-lifespan rules.

## Networking boundaries

`RelayPartyManager.cs` has small hooks plus a single-flight wrapper around its existing Unity initialization/authentication routine. The original bodies of JoinParty, connection approval, scene loading, manual spawning and match-start core are unchanged compared with the baseline. No new NetworkBehaviour, NetworkObject, scene edits, prefab edits, packages or scene/prefab network identity changes are required for this feature.

`LobbyAdvertisement.cs` is a discovery-only service with a serialized async lifecycle. The service lobby represents the advertisement; it is not a second player roster. It stores the actual NGO connected count in indexed public data. This keeps manual invite-code joins accurate without forcing them through Unity Lobby membership. The existing NGO approval still enforces the three-player cap and blocks late joins.

`RelayLobbyBrowser.cs` builds its own menu UI at runtime and passes row joins into the original Relay JoinParty method. The public advertisement contains the Relay invite code because anyone is allowed to join a public lobby; private Relay codes are never advertised. Names use plain text rather than TMP markup. Listings filter the directory schema and application/protocol version.

Public hosts heartbeat every twelve seconds. Roster/name updates are throttled to two-second intervals. Queries are throttled to five seconds and return at most twenty listings, with six displayed. Service failures do not shut down an existing Relay connection; a failed public publish is reported as invite-code-only hosting. Semaphore/generation checks prevent leave/start from being undone by in-flight create/update operations; deletion failures are retried.

## Tests

Tools > Lobby Browser contains lifecycle tests and a development build for opt-in real service integration testing. The integration fixture runs only with the `-lobbyBrowserTest` command-line option, uses separate anonymous authentication profiles, and leaves normal launches unaffected. It tests real private Relay joins, public service queries/joins/counts, full and started-game rejection, and the existing Night 1 to Night 2 transfer with three players. Test logs/results are under Logs and are excluded from source control.

A full-game build is needed on every participating machine before testing this feature with other people. Do not mix it with an older player build that lacks this menu feature.

## Reverting this feature

Compare against the baseline before restoring. Restore the RelayPartyManager source from that commit and remove the new browser/service/test files and their metadata; retain unrelated later edits. Existing game scenes/prefabs and transport/gameplay scripts were preserved. The independent raw project archive and hash manifest are recorded in PROJECT_CONTEXT.md.

Final validation: 31 async lifecycle checks plus 53 live cloud/Relay assertions across four actual game processes passed (84 total). The final live run includes closing the browser while a row join is pending, private leave/rehost, public 1/3 -> 2/3 -> 3/3 counts, fourth-player rejection, listing removal at start, late-join rejection and all three players reaching Night 2. Evidence: Logs/LobbyBrowserVerification.json and Logs/LobbyBrowserRun-114702. UI chooser and empty-state browser were visually checked in Editor Play Mode. Development and release-style C# compilation passed; the opt-in command-line integration launcher is compiled only for the editor/development builds. Generated font-cache edits from Play Mode were reverted to the exact baseline source assets.

## Menu styling (2026-11-01)
Presentation-only refresh: menu-aligned Server Browser button, existing SpaceMono menu font, charcoal panels, red accents, column headings, row spacing, and matching host chooser. No service, query, join, advertisement or transport behavior changes. C# compile and whitespace checks passed. Editor Play Mode visually checked the menu, empty-state browser, refresh/close controls and Private/Public selection.
