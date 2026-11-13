# Mario 64 (Unity Remake)

A *Super Mario 64* 3D platformer remake built in **Unity 6 (`6000.6.8f1`)** with URP, Cinemachine, ProBuilder, and the Unity Input System.

## Requirements
- **Unity Editor**: `6000.6.8f1`
- **Git LFS**: Enabled via `.gitattributes` (`*.fbx`, `*.png`, `*.wav`, `*.asset`)
- **Key Packages** (`Packages/manifest.json`):
  - `com.unity.render-pipelines.universal` (`17.6.0`)
  - `com.unity.inputsystem` (`1.20.1`)
  - `com.unity.cinemachine` (`6.6.0`)
  - `com.unity.probuilder` (`6.1.4`)
  - `com.unity.test-framework` (`1.8.0`)

## Main Scenes (`Assets/Scenes/`)
- `Assets/Scenes/Test.unity` — Main gameplay / mechanics & boss test course.
- `Assets/Scenes/New Scene.unity` — Clean sandbox scene for testing plug-and-play prefabs (`Mario.prefab`, `Water.prefab`, enemies, modular kit).

## Getting Started
1. Clone the repository with Git LFS (`git lfs pull`).
2. Open the project in **Unity `6000.6.8f1`**.
3. Open `Assets/Scenes/Test.unity` (or drop `Assets/Prefabs/Characters/Mario.prefab` into any scene) and press **Play**.

## Controls
- **Move**: `W A S D` / Arrow Keys
- **Camera Orbit**: Mouse movement (360° orbit)
- **Jump / Double / Triple / Wall Kick**: `Space`
- **Crouch / Ground Pound / Dive**: `Ctrl` / `Shift` / `Left Click`
- **Grab & Throw** (Bob-ombs, King Bob-omb from behind, Bowser's tail): `F` (or `Left Click` to release/throw)

