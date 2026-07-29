# Interaction

Category

Player

Status

Complete

## Tasks

- [x] Placement should not show on gold piles
- [x] Hold to throw/place/pickup
- [x] Fix coin stacks - The coins you pick up from a stack when selecting midway down the stack is picking up 1 coin below where I am expecting so something is off

## Bugs

- [x] Picking up items not always working
- [x] Throwing a gem or artifact back onto pile should always put it visible on the pile

## Metadata

Milanote ID:
1WJNyb1GzXe35l

Last Sync:
2026-07-28

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
131072

Sort Index:
2

## Cursor Implementation

Mid-stack coin pickup was resolving one slot too low when looking down at a stack. Aim height used closest-approach of the aim ray to the stack axis in XZ; with a downward look that point sits below the crosshair hit on the capsule/cylinder surface.

- `GroundCoinStack.ResolvePickupStartIndex` now prefers `TryGetLastHit().point.y` when the hit belongs to that stack, with axis projection only as fallback.
- `CoinColumnPickup` treats coin transforms as bottom-of-slot (matching seating + cylinder bands), uses half-open bands, and accepts an optional hit Y for the same reason.
- Loose columns and display-table stack pickup pass the interact hit Y through `ITreasureDisplayStackOwner.TryCollectPickupColumn`.

## Files Modified

- `Assets/Code/Interaction/CoinColumnPickup.cs`
- `Assets/Code/Interaction/GroundCoinStack.cs`
- `Assets/Code/Interaction/TreasureItemInteractable.cs`
- `Assets/Code/Interaction/ITreasureDisplayStackOwner.cs`
- `Assets/Code/Interaction/MixedDisplayTableInteractable.cs`
- `Assets/Code/Interaction/TypedDisplayTableInteractable.cs`
- `Assets/Code/Interaction/ArtifactPresentationTableInteractable.cs`
- `Assets/Code/Interaction/GemConstellationInteractable.cs`

## Testing Instructions

1. Spawn or place a tall ground coin stack (10+ coins), stand close and look slightly down at the middle of the stack.
2. Aim the crosshair at a clear band (not a groove) about halfway up; pick up.
3. Expected: the Active held coin matches the band under the crosshair; coins below that band stay on the ground; coins from that band upward enter the hand (bottom-up, aimed = Active).
4. Repeat aiming one band higher and one band lower; each pickup should shift by exactly one coin relative to the previous.
5. On a mixed display-table coin column, aim mid-stack and pick up; expected: same crosshair-aligned start index (not one below).

## Cursor Notes

Axis fallback remains for cases with no stack hit (should be rare during pickup). Inclusive top band still clamps aim Y to the stack extents so aiming past the top takes the top coin.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
