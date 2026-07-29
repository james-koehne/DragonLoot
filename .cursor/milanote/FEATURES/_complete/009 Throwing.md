# Throwing

Category

Player

Status

Complete

## Tasks

- [x] Control throw distance (Default more, different per item type)
- [x] Throwing needs to apply your character's velocity to the throw too
- [x] Setup throwing arc to go fully to floor no matter the angle of throw

## Bugs

- [x] Not throwing towards aim direction, throw arc should be based on direction of your throw

## Review

- [x] Overall

## Metadata

Milanote ID:
1WM2s61k4GAQdh

Last Sync:
2026-07-28

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
262144

Sort Index:
2

## Cursor Implementation

Throw arcs now use the true ballistic time-to-floor instead of a hard 1.25s cap, so steep lobs finish their path and steep drops stop at seat height instead of tunneling under the surface.

- `MaxFlightTime` matches the prediction horizon (`MaxBallisticSteps * dt` ≈ 4s).
- Impact hits store the simulated elapsed time (not clamped up to a long minimum that overshoots short drops).
- Flight tween follows pure ballistic motion, clamps Y to the land height once descending, and only late-eases into the seated end for stack corrections.

## Files Modified

- `Assets/Code/TreasureSurface/TreasureSurfaceThrow.cs`
- `.cursor/milanote/FEATURES/009 Throwing.md`

## Testing Instructions

1. Pick up a coin. Aim roughly horizontal and throw into empty space — arc should follow aim and land on the treasure surface.
2. Aim steeply upward and throw — coin should rise through the full lob and come down to the floor (no mid-air snap / cut-short settle).
3. Aim steeply downward at nearby floor and throw — coin should travel down along aim and stop on the floor (no underground pass-through, then pop-up).
4. Sprint and throw while looking slightly up — landing should still complete a full arc; inherited velocity should still carry the throw forward.
5. Repeat steps 2–3 with a gem/artifact (heavy throw) — same full-to-floor behavior at lower speed.

## Cursor Notes

- Soft drops reuse the same flight path; if they feel too long on steep aim, soft velocity scales still apply before prediction.
- If very extreme debug throw forces (>~30) still clip the 4s horizon, raise `MaxBallisticSteps` rather than reintroducing a short animation cap.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
