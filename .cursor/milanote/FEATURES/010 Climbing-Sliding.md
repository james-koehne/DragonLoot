# Climbing/Sliding

Category

Player

Status

In Progress

## Tasks

- [x] Should only slide if you press w and looking down and holding down for a decent time
- [x] Cancel sliding somehow
- [x] Ensure slighting feels intentional
- [ ] Climbing up a steep slope causes jittering

## Bugs

- [x] Can collide with small artifacts

## Review

- [x] Climbing feel improved

## Metadata

Milanote ID:
1WM2tA1k4GAQdl

Last Sync:
2026-07-28

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
327680

Sort Index:
3

## Cursor Implementation

Fixed steep-slope climb jitter and simplified slide exit boost:

1. **Stable steep climb** — Ground probe now uses current-frame uphill intent (same tick as movement). Steep uphill skips per-frame normal re-projection and ground-down stick; idle on slopes still sticks.
2. **Simple slide exit boost** — On slide exit: `exitSpeed = slideSpeed * slideExitBoostMultiplier` (capped), direction = horizontal exit / last slide travel direction. Speed decays toward walk/sprint at `slideExitBoostDecay`; light steering toward input. Removed input-gated budget, maintain/cancel coast logic.

## Files Modified

- `Assets/Code/Player/PlayerController.cs`
- `Assets/Code/Definitions/PlayerControllerDefinition.cs`
- `Assets/Definitions/PlayerControllerDefinition.asset`
- `Assets/Code/Debug/Sections/DebugPlayerSection.cs`

## Testing Instructions

1. Stand still on a steep slope (≥45°) with no input. Expected: no uphill drift / auto-walk.
2. Find or create a steep slope (≥45°, e.g. gold pile edge or test ramp). Walk uphill holding W (and sprint if desired). Expected: smooth ascent without camera/body jitter or micro-bounces.
2. Walk downhill on the same slope, then turn and climb back up. Expected: no hitch when reversing direction; uphill still smooth.
3. Walk across a steep slope sideways (A/D along contour). Expected: stable footing, no excessive sliding or vibration.
4. Descend a steep uneven pile (gold pile mesh). Expected: ground snap still pulls you onto the surface when going downhill — no floating micro-gaps.
5. Slide down a gold pile onto flatter ground (release input at exit). Expected: no instant stop at the pile edge; exit boost/coast carries speed forward.
6. Charge and perform a slide on ≥45° slope, then exit by holding uphill. Expected: slide entry/exit unchanged; no new jitter when transitioning back to walking uphill.
7. Jump on flat ground and land on a steep slope while holding forward. Expected: clean landing and continued uphill movement without jitter spike.

## Cursor Notes

- Tune `slideExitBoostMultiplier` (try 1.0–1.2), `slideExitBoostMaxSpeed`, `slideExitBoostDecay` (asset default 35), `slideExitBoostSteer` on `PlayerControllerDefinition`.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
