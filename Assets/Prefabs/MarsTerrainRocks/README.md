# Mars terrain rocks

Five separate low-poly terrain formations, with bottom-centred pivots, unit scale and convex MeshColliders. Drag prefabs into the level; partially bury the bases for a natural terrain transition. No scripts or networking components are needed.

| Prefab | Approximate width x height x depth (metres) | Triangles |
| --- | --- | --- |
| MarsTerrainBoulder | 9 x 5 x 7 | 110 |
| MarsTerrainCrag | 12 x 8 x 9 | 100 |
| MarsTerrainSlab | 16 x 5 x 13 | 104 |
| MarsTerrainSpire | 18 x 16 x 14 | 106 |
| MarsTerrainMassif | 23 x 13.25 x 17.5 | 102 |

Massif is approximately five times the dimensions of the original RockLarge. Original small rock assets remain available.

Materials use rough red/orange iron-oxide coloration, mineral mottling and grit, with shared 1024px repeating albedo and normal maps. UVs use dominant-face projection with approximately four metres per texture repeat, so vertical faces have texture detail. Standard shader matches this project's built-in rendering setup; GPU instancing enabled.

Meshes and portable OBJ copies: Assets/Art/Models/MarsTerrainRocks. Materials: Assets/Materials/MarsTerrainRocks. Textures: Assets/Art/Textures/MarsTerrainRocks.

Built and rendered in Unity 6000.0.32f1. Checked closed edges, outward winding, positive volume, collider/prefab references, and delivered mesh/material/texture GUIDs. Preview: Logs/MarsTerrainRocksPreview.png. Generation and verification evidence: Logs/RockAssetBuild/BuildMarsRocks.cs (under Assets/Editor), MarsBuild.log, MarsRockValidation.txt and MarsMeshValidation.json.
