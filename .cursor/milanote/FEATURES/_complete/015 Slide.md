# Slide

Category

Player

Status

Complete

## Tasks

- [x] Any interaction remove slide velocity
- [x] Make ending slide less like ice - Could be 3s full speed and ramp down quickly
- [x] Exit boost doesn't feel great

## Metadata

Milanote ID:
1WOuDh1gaX2j19

Last Sync:
2026-08-06

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
360448

Sort Index:
5

## Cursor Implementation

- Dual slide input modes via `slideInputMode` on `PlayerControllerDefinition` (default **SprintHold**):
  - **Charge** — look down + hold W downhill for `slideEnterHoldTime`; continues until flatten / uphill / reverse brake / jump / interact. Shift is sprint only.
  - **SprintHold** — enter when Shift + look down + move downhill. **Release Shift does not end the slide.** Move input steers and can add downhill speed / soft-brake, but never cancels. Slide ends when ground flattens (`slideExitHysteresis`), idle stall below `slideMinDownhillSpeed`, jump, or interact.
- Shared feel pass: `slideMaxSpeed` terminal velocity (asset 32); exit boost only when exit speed is above walk+0.5 (slow exits keep residual velocity, no ice coast); asset `slideExitHysteresis` raised to 10.
- `PlayerController.CancelSlideVelocity()` still plants after successful primary interact / secondary place/throw.
- Debug overlay: `Slide: Sprint Hold (off = Charge)`; charge % only in Charge mode while charging.
- Sliding (and gliding) eases FOV up by `CameraDefinition.speedFovBoost` (default +8°) over `speedFovBlendTime` (0.2s).
- Contextual tutorial `tut_sliding` shows on a slideable slope while looking down (after sprinting tutorial). Completes when a slide starts.

## Files Modified

- Assets/Code/Player/PlayerController.cs
- Assets/Code/Definitions/PlayerControllerDefinition.cs
- Assets/Definitions/Player/PlayerControllerDefinition.asset
- Assets/Code/Debug/Sections/DebugPlayerSection.cs
- Assets/Code/Interaction/PlayerInteraction.cs
- Assets/Code/Camera/FirstPersonCameraController.cs
- Assets/Code/Definitions/CameraDefinition.cs
- Assets/Definitions/CameraDefinition.asset
- Assets/Code/Graphics/Cinematic/CinematicPresentationController.cs
- Assets/Code/Definitions/TutorialDefinition.cs
- Assets/Code/Tutorials/TutorialManager.cs
- Assets/Definitions/Tutorials/Tutorial_Sliding.asset
- Assets/Definitions/TutorialCatalogDefinition.asset

## Testing Instructions

1. SprintHold: on a steep pile, look down, move downhill, hold Shift — slide starts immediately.
2. Release Shift mid-pile — slide continues until flatten / stall / cancel.
3. Ride onto flatter ground — slide ends; fast exit gets boost, slow exit does not ice-coast.
4. Contour or idle stall until downhill speed &lt; `slideMinDownhillSpeed` — slide ends. Holding move (including reverse/strafe) does not cancel.
5. While sliding in SprintHold, press W downhill — speed builds (capped by `slideMaxSpeed`). Strafe steers. S soft-brakes but does not plant.
6. Charge mode: W + look downhill charges; Shift does not start/stop. Uphill commit / reverse brake still plant.
7. Interact while sliding still plants via `CancelSlideVelocity`.
8. While sliding, confirm FOV widens slightly and eases back when the slide ends.
9. After completing the sprinting tutorial, look down a steep slope. Confirm the Sliding tutorial appears. Start a slide — the Slide task completes and the popup dismisses.

## Cursor Notes

- Tune: `slideMaxSpeed` (32), `slideMinDownhillSpeed` (1.5), `slideExitHysteresis` (10), `slideGravityScale`, exit boost (`slideExitBoostMaxSpeed` / `Decay` / `Steer`).
- SprintHold enter still needs Shift + look down + downhill move; Shift is only for entry, not hold-to-slide.
- Cancel does not run on failed/invalid place aims.
- Tune glide/slide FOV on `CameraDefinition`: `speedFovBoost` (8), `speedFovBlendTime` (0.2).

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
