# Project Rules & Engineering Guidelines (Mario 64 Remake)

## 1. Input & Player Architecture
- **Always use `UnityEngine.InputSystem`** (`Keyboard.current`, `Mouse.current`, or Input Actions). Do **not** introduce legacy `UnityEngine.Input` calls.
- Preserve the existing character controller and procedural animation architecture in:
  - `Assets/Scripts/Player/MarioRemakeController.cs`
  - `Assets/Scripts/Player/ProceduralCharacterMotion.cs`
  - `Assets/Scripts/Core/GameManager.cs`
- Keep core prefabs (`Mario.prefab`, `Water.prefab`, `BowserBoss.prefab`, `KingBobOmb.prefab`, `BobOmb.prefab`) **plug-and-play** so they work cleanly in any empty or test scene without manual scene wiring.

## 2. Editor Tooling
- Place all Editor scripts, prefab builders, and batch upgraders under `Assets/Scripts/Editor/`.
- Use `Undo`, `SerializedObject`, `PrefabUtility.LoadPrefabContents` / `EditPrefabContentsScope`, and `AssetDatabase.SaveAssets()` safely so prefab edits persist cleanly without corrupting scene overrides.

## 3. Rendering, Shaders & VFX (URP 17)
- Target **URP 17.6.0** (`Universal Render Pipeline/Lit` or SRP-Batcher-compatible HLSL shaders).
- Reuse `VFXPool` for runtime particle bursts and dust/fire effects instead of allocating unpooled objects in hot loops.

## 4. Audio
- `Assets/Scripts/Audio/GameAudio.cs` handles procedural/runtime audio hooks. When adding `AudioMixer` groups or `.wav`/`.ogg` SFX/BGM clips, route all `AudioSource` outputs through the project `AudioMixer` and apply platform-appropriate compression/load types.

## 5. Testing & Version Control
- Write EditMode/PlayMode tests using `com.unity.test-framework` (`1.8.0`).
- Keep binary assets (`*.fbx`, `*.png`, `*.wav`, `*.asset`) tracked via Git LFS (`.gitattributes`).

