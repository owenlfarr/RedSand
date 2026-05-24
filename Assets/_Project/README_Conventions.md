RedSand Asset Conventions

Top-level folders under Assets:
- _Project: project docs, reports, tooling
- Art/Textures, Art/Models, Art/Animations
- Audio
- Materials
- Prefabs
- Scenes
- Scripts
- Settings (URP/global profiles, render textures, terrain layers)
- ThirdParty (imported external packs)
- Docs

Naming:
- Avoid files ending in "(1)", " 1", "New Material", "New Project".
- For gameplay scripts use PascalCase (e.g., PlayerMovement.cs).
- Prefix experimental assets with "WIP_" and move to a dedicated subfolder.

Workflow:
- Keep third-party assets inside Assets/ThirdParty and do not edit vendor files directly.
- Put project overrides/material instances in project folders outside ThirdParty.
- Before commit, review duplicate report in Assets/_Project/DuplicateReport.txt.
