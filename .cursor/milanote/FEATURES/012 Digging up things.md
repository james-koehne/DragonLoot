# Digging up things

Category

Coin piles

Status

In Progress

## Tasks

- [x] Deterministic spawning of all items within the pile
- [x] Picking up artifacts from pile needs specific handling for their collider, probs fine to have a unique physics collider for each
- [x] If any artifact goes below floor it should stay above it
- [x] Ensure that if gold pile below ground it means nothing is required from it
- [x] I saw multiple artifacts/gems spawn on the exact same position/pose, this should never occur
- [x] Related to moving artifacts above the floor, if they are repositioned they should not just be pushed up as that makes them hover off the gold pile, should push them up in the gold pile
- [ ] Don't move too many coins to digging location

## Metadata

Milanote ID:
1WM32U1k4GAQdP

Last Sync:
2026-07-28

Category Sort Score:
163840

Category Sort Index:
4

Sort Score:
65536

Sort Index:
1

## Cursor Implementation

- Volume poses for gems/artifacts only seat inside solid mound columns (`ExistsAtLocal`); empty/below-ground cells are skipped (no origin collapse).
- Outside-fraction tests treat below-ground pile as the floor plane — no dig required from empty cells; carving to ground still reveals buried props geometrically.
- Probe/AABB seats are floor-clamped above `GroundLevel`; real mesh bounds lift on spawn if they would clip the floor.
- Deterministic multi-attempt sampling enforces min spacing so no two volume props share the same position/pose.
- Floor repositioning uses `GetPileColumnBand` / `LiftBoundsIntoPileColumn`: props clear the floor plane but stay capped within the embedded column band at their XZ (surface − probe embed), so skirt props no longer hover above the mound after a vertical-only lift. `ClampAboveFloor` uses the same band for spawned pivot seating.
- Coin densify after each carve uses a shared per-dig move budget (`coinPullToCarveCount`, default 2): reactivating taken slots and pulling distant coins both draw from the same cap. Pull/reseat is skipped entirely when the dig neighborhood already has at least that many active coin visuals.

## Files Modified

- `Assets/Code/Graphics/GoldPile/GoldPileTreasurePlacement.cs`
- `Assets/Code/Graphics/GoldPile/GoldPileArtifactProps.cs`
- `Assets/Code/Graphics/GoldPile/GoldPileLootInstances.cs`
- `Assets/Code/Definitions/TreasurePileDefinition.cs`
- `Assets/Definitions/Treasure/Pile/TreasurePileDefinition.asset`
- `Assets/Definitions/Treasure/Pile/TreasurePileLargeDefinition.asset`
- `Assets/Definitions/Treasure/Pile/TreasurePileGiganticDefinition.asset`

## Testing Instructions

1. Enter Play Mode on a scene with a large treasure pile; note several gem/artifact positions (or enable pile loot spawn logs).
2. Restart Play Mode with the same `CoreDefinition.lootWorldSeed` — latent seats should match (deterministic).
3. Confirm no two gems/artifacts occupy the same world pose (look for overlapping crowns/gems at bind).
4. Dig/carve until a cell goes below ground level — that cell should not be interactable as pile; buried props in that column should still reveal once their bounds sit above the remaining surface/floor.
5. Inspect props near the skirt/floor — none should clip below the pile floor plane.
6. Dig around low-skirt / floor-edge artifacts until they reveal — they should stay embedded on the gold surface, not float visibly above the mound after spawn or deposit.
7. Dig repeatedly in one spot on a coin-heavy pile — at most 2–4 coin visuals should relocate into the dig neighborhood per carve (pile size via definition asset); distant coins should stay put once the carve site is already dense.

## Cursor Notes

- Spacing uses `max(placementMinSpacing, probe*1.5)` in 3D; very dense authored counts may skip a seat after 32 attempts rather than stack.
- GPU gem path in `GoldPileLootInstances` still wires occupancy if gems return to GPU instances; currently large props are `GoldPileArtifactProps` only.
- On degenerate skirt columns where `ceilingY < floorY`, floor clearance wins over embed cap (same rule as volume sampling skip).
- Tune `coinPullToCarveCount` on pile definition assets if digs feel too sparse (raise) or still too swarmy (lower).

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
