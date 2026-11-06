# Terrain rock navigation

ZombieAI roaming, chasing and stalking, plus PowerStationWatcher roaming, now use a shared scene-local Unity navigation surface and independent per-enemy routes. Multiplayer clients still follow replicated transforms; only existing server/offline movement calls request paths. Surveyor radial apparition behavior is unchanged.

TerrainRockNavigation collects ground/static collision geometry and explicitly adds unwalkable volumes for each painted rock instance using the prefab mesh bounds, child transforms, root scale, terrain position, tree rotation, width and height. Sources come from TerrainData.treeInstances, not visible renderers, camera distance, tree billboards, or distance-activated colliders. Tiny rocks under 20 cm are step-over stones. Navigation uses 60 cm radius, 2 m height, 25 cm climb, and 42 degree slope.

Navigation builds asynchronously once per loaded gameplay scene. Monsters pause horizontal movement until the surface is ready, avoiding blind movement during generation. The current 4 km terrain took about 16.2 seconds for a synchronous isolated editor test; runtime background timing can differ. Scene destruction cancels pending work and removes the registered navigation data. Editing terrain or rock placement during an already running game requires reloading the level.

Each enemy recalculates its route at most every 0.65 seconds, when the target moves over 2 m, or when stalled. Turns follow path corners; wandering is retained only in a clear final corridor. Partial paths stop at the reachable endpoint rather than pushing through an obstruction. Existing AI target selection, memory, attack timing, defense and transform replication remain in place.

Validation:
- Gameplay C# compilation passed against cached Unity 6000.0.32f1 references: Logs/TerrainRockNavigationCheck.dll.
- Twelve real Unity checks passed: real rotated Mars rock prefab, nonwalkable interior, complete detour, independent crossing routes, actual CharacterController traversal around a real collider, instance scale, small step-over stones, short tree render distance and fully disabled tree rendering. Logs/RockAssetBuild/NavigationFinalTest.log and RockNavigationTest.txt. Test source: Logs/RockAssetBuild/Assets/Editor/TestRockNavigation.cs.
- A fresh copy of the saved actual terrain (4,000 x 600 x 4,000 m; 3,217 painted instances at test time) generated 1,899 explicit rock obstacle volumes and 80,648 walkable navigation triangles with tree rendering disabled. Logs/RockAssetBuild/ActualTerrainNavigationTest.txt and ActualTerrainNavigation.log. Test sources isolated the terrain from bunker geometry; production source collection also includes static scene colliders.
- No fresh full-level host/client gameplay playtest or executable build was performed. No scene, terrain, prefab, material or package edits are part of this navigation change.
