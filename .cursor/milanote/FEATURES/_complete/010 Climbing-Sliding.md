# Climbing/Sliding

Category

Player

Status

Complete

## Tasks

- [x] Should only slide if you press w and looking down and holding down for a decent time
- [x] Cancel sliding somehow
- [x] Ensure slighting feels intentional
- [x] Climbing up a steep slope causes jittering

## Bugs

- [x] Can collide with small artifacts

## Review

- [x] Climbing feel improved

## Metadata

Milanote ID:
1WM2tA1k4GAQdl

Last Sync:
2026-08-06

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
327680

Sort Index:
3

## Cursor Implementation

Added BotW-style `Climbing` movement state for steep hills:

1. **Climbing state** — Enter on steep (≥`slideAngle`) + uphill commit. Stay while steep (with `climbExitHysteresis`) including downhill/lateral surface move. Exit on flatten, idle release (`climbReleaseHoldTime`), slide enter, leave ground, or climb jump-off.
2. **`ApplyClimbMovement`** — Surface-axis WASD (climb-up / climb-right), deliberate `climbSpeed` / `climbSprintSpeed`, snappy accel, into-surface `climbSurfacePull`. Jump while climbing = push-off along normal+up (`climbJumpForce`) into Airborne.
3. **Anti-jitter** — Climb path owns adhesion (no walk stick/snap fight). Idle/sideways on steep stays Walking with normal stick. Intentional slide and fall unchanged when not climbing.
4. **`climbingEnabled`** — Definition bool (default on). When off, climb never enters and an active climb clears. Debug overlay Player section has a "Climbing Enabled" toggle (`SetClimbingEnabled`).

## Files Modified

- `Assets/Code/Player/PlayerController.cs`
- `Assets/Code/Definitions/PlayerControllerDefinition.cs`
- `Assets/Definitions/PlayerControllerDefinition.asset`
- `Assets/Code/Debug/Sections/DebugPlayerSection.cs`

## Testing Instructions

1. Stand still on a steep slope (≥45°) with no input. Expected: Walking (not Climbing); no uphill drift.
2. Walk uphill on a steep slope (≥45°). Expected: enter Climbing; slower deliberate pace; State shows Climbing; smooth ascent without jitter.
3. While climbing, move A/D laterally and S downhill. Expected: stay Climbing; controlled surface move; no auto-slide from downhill alone.
4. While climbing, hold sprint. Expected: faster climb (`climbSprintSpeed`), still Climbing.
5. While climbing, press jump. Expected: push off the slope into Airborne (not a normal ground jump).
6. While climbing, release all move input for ~0.25s. Expected: exit Climbing to Walking and plant with stick.
7. Charge intentional slide (look down + hold W downhill). Expected: leave Climbing, enter Sliding; can still fall/airborne exit as before.
8. Exit slide by holding uphill on steep ground. Expected: can re-enter Climbing on the next uphill commit.
9. Walk across a steep contour without uphill commit. Expected: Walking with stick; no Climbing until uphill.
10. Debug overlay → Player → uncheck "Climbing Enabled", then walk uphill on steep ground. Expected: stay Walking (no Climbing). Re-enable; next uphill commit enters Climbing again.

## Cursor Notes

- Tune on `PlayerControllerDefinition`: `climbingEnabled`, `climbSpeed` (asset 7), `climbSprintSpeed` (10), `climbAcceleration` / `climbDeceleration`, `climbSurfacePull` (12), `climbJumpForce` (9), `climbExitHysteresis` (5), `climbReleaseHoldTime` (0.25).
- No stamina system — sprint only raises climb speed.
- Toggle climbing off via definition or debug overlay when comparing walk-vs-climb jitter on steep slopes.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
