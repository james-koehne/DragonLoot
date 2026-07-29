# Slide

Category

Player

Status

In Progress

## Tasks

- [x] Any interaction remove slide velocity
- [x] Make ending slide less like ice - Could be 3s full speed and ramp down quickly
- [ ] Exit boost doesn't feel great

## Metadata

Milanote ID:
1WOuDh1gaX2j19

Last Sync:
2026-07-28

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
360448

Sort Index:
5

## Cursor Implementation

- Post-slide exit is an **input-gated boost**, not passive momentum: on slide exit, planar speed is capped to `slideExitBoostMaxSpeed` (24) and stored as a boost target.
- Boost is maintained only while move intent stays aligned with travel direction (`slideExitCoastCancelDot` threshold). Budget drains at `slideExitBoostDuration` (1.5s) while maintaining; mild steering via `slideExitBoostSteer`.
- Releasing input, strafing, or exhausting the budget applies `slideExitBoostDecay` (70) toward walk speed — no unassisted free-coast hold.
- Reverse-brake cancel unchanged (`slideExitCoastCancelDeceleration`).
- `PlayerController.CancelSlideVelocity()` ends an active slide and zeroes planar velocity. Called after successful primary interact and successful secondary place/throw so interaction plants the player (throw still inherits slide speed first).
- Debug overlay shows exit boost speed and remaining budget while active.

## Files Modified

- Assets/Code/Player/PlayerController.cs
- Assets/Code/Definitions/PlayerControllerDefinition.cs
- Assets/Definitions/PlayerControllerDefinition.asset
- Assets/Code/Interaction/PlayerInteraction.cs
- Assets/Code/Debug/Sections/DebugPlayerSection.cs

## Testing Instructions

1. Charge a slide on a steep slope (look down + hold W). Exit onto flatter ground, then **release all input** immediately.
   - Expected: speed drops to walk within ~0.3s — no free glide.
2. Same setup, **hold W** along travel direction for ~1.5s.
   - Expected: capped boost speed (~24 u/s) held for the budget duration, then decays.
3. During boost, **strafe or release W**.
   - Expected: immediate decay via `slideExitBoostDecay`, not continued coast.
4. During boost, **hold move against travel direction**.
   - Expected: residual speed cancels quickly (reverse brake).
5. While actively sliding, look at a pickable treasure and primary-interact.
   - Expected: pickup occurs; planar slide speed drops to zero immediately; slide state ends.
6. While sliding or coasting with something held, secondary-place or throw successfully.
   - Expected: place/throw succeeds; player planar slide speed clears afterward.
7. Walk normally and interact with nothing focused.
   - Expected: no change to movement (cancel only runs on successful interact/place).

## Cursor Notes

- Tune on `PlayerControllerDefinition`: `slideExitBoostMaxSpeed`, `slideExitBoostDuration`, `slideExitBoostDecay`, `slideExitBoostSteer`. Asset defaults: 24 / 1.5 / 70 / 25.
- Forward maintain and reverse cancel share `slideExitCoastCancelDot`.
- Cancel does not run on failed/invalid place aims.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
