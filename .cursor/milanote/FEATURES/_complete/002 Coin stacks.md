# Coin stacks

Category

Sorted Treasure

Status

Complete

## Tasks

- [x] Add proper area for stacked coins -> Stone raised floor + Pillars
- [x] Combined coin stack
- [x] Darker line for more 3d ish
- [x] Single coin line
- [x] Multi coin combined stack
- [x] make them prettier with more shiny

## Bugs

- [x] Offset wrong when combined
- [x] Individual coins left around, need to unify the stacks

## Metadata

Milanote ID:
1WJNzM1GzXe35q

Last Sync:
2026-08-06

Category Sort Score:
65536

Category Sort Index:
1

Sort Score:
65536

Sort Index:
1

## Cursor Implementation

Treasure sparkles on coin stacks (ground / table / held cylinders):

1. **DepthNormals** — `DragonLoot/Coin Stack` and `Coin Stack Multi` were missing a DepthNormals pass (loose coins / piles already had one). Sparkle glints sample `_CameraNormalsTexture` for metallic response, so stacks got empty/wrong normals and failed alignment.
2. **Mask registration** — `TreasureSparkleMaskContributor` now keeps renderers registered even when disabled; `CollectEntries` still filters by `enabled`. `CoinStackCylinderVisual.SetStack*` refreshes the contributor after toggling mesh visibility.
3. **Close fade** — `TreasureSparkleDefinition` close-fade was 8–16 m (killed glints at interaction range). Restored to 0.35–1.5 m defaults.

## Files Modified

- `Assets/Materials/Shaders/Coin/DragonLoot_CoinStack.shader`
- `Assets/Materials/Shaders/Coin/DragonLoot_CoinStackMulti.shader`
- `Assets/Code/Graphics/TreasureSparkle/TreasureSparkleMaskContributor.cs`
- `Assets/Code/Interaction/CoinStackCylinderVisual.cs`
- `Assets/Definitions/TreasureSparkleDefinition.asset`

## Testing Instructions

1. Enter play mode with treasure sparkles enabled (`TreasureSparkleDefinition.enableSparkles`).
2. Optional: set Debug Mode → **Mask** — ground/table coin stack silhouettes should lighten like loose coins.
3. Set Debug Mode → **Off**. Build a ground coin stack (2+ coins so it becomes a cylinder). Walk around it in sunlight — metallic glints should appear on the stack body/cap similar to loose coins.
4. Aim at a display-table coin column — same sparkle response.
5. Stand within ~2 m of a stack — sparkles should still be visible (not faded out by close-fade).
6. Confirm gold piles / loose gems still sparkle as before.

## Cursor Notes

- Stack shaders already wrote stencil Ref 64 and registered via `CoinStackCylinderVisual.EnsureSparkleMaskContributor`; the missing DepthNormals pass was the main visual gap.
- If stacks still look sparse vs piles: `cellSize` 0.5 m is large relative to coin diameter — consider a future coin/stack density multiplier.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
