# Inventory & Save Lab source

Self-initiated portfolio demonstration; Unity6000.0.45f1, Built-in renderer, uGUI, Windows x64. Final source revision: polished-20-f1. See PORTFOLIO_CASE.md and VERIFICATION.json for the tested scope and remaining limitations.

Open Project/ in the stated Unity version, then Assets/Scenes/InventoryLab.unity. The scene boots the UI; no imported paid assets are needed. The included package manifest and lock pin the dependencies. The inventory model is in Assets/Lab/Core; runtime UI and persistence in Assets/Lab/Runtime; tests in Assets/Lab/Tests.

From this extracted directory, the PowerShell scripts can invoke an installed editor:

```powershell
powershell -ExecutionPolicy Bypass -File Run-Lab.ps1 -Action EditMode -Revision your-test-run
powershell -ExecutionPolicy Bypass -File Run-Lab.ps1 -Action Build -Revision your-build-run
```

Inspect Run-Lab.ps1 for its editor path and supported parameters before using it on another machine. It writes named Evidence/ and Builds/ folders; use a new revision name to preserve previous output. Opening Unity and running tests/builds requires the normal local Unity installation/licensing environment. The source archive does not contain Unity itself or its Library cache.

The Windows Player is supplied separately. Its default save folder is UserData/ next to the executable. Save is explicit: F5 saves, F9 loads, Tab changes theme, F12 captures the native framebuffer. New Session changes memory until Save. A single application instance should own a save directory.

Screenshots/ contains real native Player images; VERIFICATION.json labels their input method. SourceAssets/provenance.csv records original procedural graphics and the Unity runtime font. MANIFEST.json lists the packaged files and SHA256 digests. No private commercial calculations, account information, raw Unity logs or verification saves are included.
