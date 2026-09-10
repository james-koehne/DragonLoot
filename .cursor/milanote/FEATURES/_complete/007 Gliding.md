# Gliding

Category

Player

Status

Complete

## Tasks

- [x] Double jump

## Metadata

Milanote ID:
1WM2NI1k4GAQdF

Last Sync:
2026-07-28

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
393216

Sort Index:
4

## Cursor Implementation

- After the first airborne glide (double jump), Space toggles glide off if already gliding, then pressing Space again while still airborne re-enters glide without a second double-jump boost.
- Re-glide uses the same BeginGlide setup as the initial air jump (ignore grounding, clear climb/slide boost). Glide still ends on landing.
- Gliding (and sliding) eases FOV up by `CameraDefinition.speedFovBoost` (default +8°) over `speedFovBlendTime` (0.2s). Cinematics still own FOV while playing.
- Glide/double-jump is gated by passive ability `glide` (`Ability_Glide`, display name Double Jump). `PlayerController.HasGlideAbility()` must be true before air-jump glide or re-glide.
- `tut_gliding` uses trigger `AbilityUnlocked` (`requiredAbilityId: glide`); body recommends getting high (not a height gate).

## Files Modified

- Assets/Code/Player/PlayerController.cs
- Assets/Code/Camera/FirstPersonCameraController.cs
- Assets/Code/Definitions/CameraDefinition.cs
- Assets/Definitions/CameraDefinition.asset
- Assets/Code/Graphics/Cinematic/CinematicPresentationController.cs
- Assets/Code/Definitions/AbilityDefinition.cs
- Assets/Code/Abilities/AbilitySystem.cs
- Assets/Code/Abilities/AbilityUnlockedEvent.cs
- Assets/Definitions/Abilities/Ability_Glide.asset
- Assets/Definitions/AbilityCatalogDefinition.asset
- Assets/Definitions/Tutorials/Tutorial_Gliding.asset
- Assets/Code/Definitions/TutorialDefinition.cs
- Assets/Code/Tutorials/TutorialManager.cs
- .cursor/milanote/FEATURES/_complete/007 Gliding.md

## Testing Instructions

1. Without glide unlocked: jump in air — no double jump / glide.
2. Unlock glide (debug Abilities or constellation reward) — confirm unlock toast “Unlocked: Double Jump”.
3. Jump, then press Space in the air to start gliding.
4. Press Space again to cancel glide. Confirm fall speed returns to normal gravity.
5. Press Space again before landing. Confirm glide resumes (slower fall, glide air control) with no extra upward boost.
6. Repeat cancel/re-glide until you land. After landing, confirm you must jump + air jump again to glide.
7. While gliding, confirm FOV widens slightly and eases back when glide ends.
8. After unlock, glide tutorial can show without being above Y=30; copy still recommends going high. Completing requires actually gliding.

## Cursor Notes

- Tune glide/slide FOV on `CameraDefinition`: `speedFovBoost` (8), `speedFovBlendTime` (0.2).
- Ability id constant: `PlayerController.GlideAbilityId` (`"glide"`).

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
