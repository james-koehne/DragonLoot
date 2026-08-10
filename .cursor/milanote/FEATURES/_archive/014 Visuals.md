# Visuals

Category

Placement validation

Status

Ready

## Tasks

- [ ] Glowy
- [ ] Easier to see object underneath

## Metadata

Milanote ID:
1WN5bZ1AtJlL8B

Last Sync:
2026-07-28

Category Sort Score:
950272

Category Sort Index:
14

Sort Score:
0

Sort Index:
0

## Cursor Implementation

Hover outlines use a **URP Renderer Feature** (`HoverOutlineRendererFeature`) with Roy Stan depth+normal edge detection:

1. **Mask pass** — registered hover `Renderer`s drawn to an R8 mask RT (depth-tested)
2. **Composite pass** — full-screen Roberts cross on `_CameraDepthTexture` + `_CameraNormalsTexture`, multiplied by dilated mask, HDR alpha-blended **before post-processing**
3. **Bloom** — existing volume Bloom (`SampleSceneProfile`) blooms the HDR outline color

Runtime registration via `HoverOutlineRegistrar`:
- `PlayerInteraction` — pickable gems/artifacts/crank (`PlayerInteractionDefinition.pickableOutline`)
- `PlayerPlacement` — stack/station mesh outlines (`PlayerPlacementDefinition.stackOutline`), green/red by validity

Placement ghost styles:
- **ItemMesh** — fresnel full ghost (floor coins, gems, artifacts, cleaning intake pose)
- **StackOutline** — outline only on target meshes (coin stacks, table coin columns, sorting hopper/station); no cylinder volume ghost
- **Suppressed** — gold pile / similar

Holding any item while aiming a pickable non-coin clears placement preview so the gold pickup outline wins. Wrong item on a coin stack shows red stack outline only (no floor fallback). Held Interact instantly picks up newly focused `CanInteract` targets (bypasses hold initial delay). Sorting station hopper placement outlines the whole station mesh (excludes crank); crank hover uses gold pickable outline. Cleaning station keeps ItemMesh ghost and adds red/green whole-station outline from placement validity.

Editor installer (`HoverOutlineRendererFeatureInstaller`) auto-adds the feature to `PC_Renderer` on import.

### One-time setup (already automated)

- Feature on `Assets/Settings/PC_Renderer.asset` (via editor installer)
- URP depth texture enabled on `PC_RPAsset`
- SSAO depth-normals on `PC_Renderer` (normals buffer)
- Bloom on scene volume profile

## Files Modified

- `Assets/Code/Graphics/HoverOutline/HoverOutlineRegistrar.cs`
- `Assets/Code/Graphics/HoverOutline/HoverOutlineTargetUtility.cs`
- `Assets/Code/Graphics/HoverOutline/HoverOutlineRendererFeature.cs`
- `Assets/Code/Graphics/HoverOutline/HoverOutlineMaskPass.cs`
- `Assets/Code/Graphics/HoverOutline/HoverOutlineCompositePass.cs`
- `Assets/Code/Graphics/HoverOutline/Editor/HoverOutlineRendererFeatureInstaller.cs`
- `Assets/Materials/Shaders/HoverOutline/HoverOutlineMask.shader`
- `Assets/Materials/Shaders/HoverOutline/HoverOutlineComposite.shader`
- `Assets/Code/Definitions/HoverOutlineVisualSettings.cs`
- `Assets/Code/Definitions/PlayerInteractionDefinition.cs`
- `Assets/Code/Definitions/PlayerPlacementDefinition.cs`
- `Assets/Definitions/PlayerInteractionDefinition.asset`
- `Assets/Definitions/PlayerPlacementDefinition.asset`
- `Assets/Code/Interaction/PlayerInteraction.cs`
- `Assets/Code/Interaction/PlayerPlacement.cs`
- `Assets/Code/Interaction/PlacementGhost.cs`
- `Assets/Code/Interaction/PlacementPreview.cs`
- `Assets/Code/Interaction/FloorPlacementTarget.cs`
- `Assets/Code/Interaction/GroundCoinStack.cs`
- `Assets/Code/Interaction/GroundTreasureStackTarget.cs`
- `Assets/Code/Interaction/TypedDisplayTableInteractable.cs`
- `Assets/Code/Interaction/MixedDisplayTableInteractable.cs`
- `Assets/Code/Interaction/CoinSortingHopper.cs`
- `Assets/Code/Interaction/CleaningStationInteractable.cs` (outline driven from `PlayerPlacement`)

Removed: `PickableFocusIndicator.cs`, mesh-overlay `DragonLoot/Pickable Outline` usage; StackVolume cylinder ghost path.

## Testing Instructions

1. Enter play mode — confirm `HoverOutline` feature on `PC_Renderer` (added by installer).
2. Hover gem/artifact (empty hands) — golden HDR outline on silhouette + creases; bloom halo visible.
3. Hold coin, aim empty floor — full fresnel coin ItemMesh ghost (no cylinder).
4. Hold coins, aim at ground stack / table coin column — single green/red outline on stack meshes only; no item-mesh tip ghost.
5. Hold gem/artifact, aim coin stack — red outline on stack; leave to floor — ItemMesh ghost returns.
6. Hold coin, aim ground/physical gem or artifact — no placement ghost; gold pickup outline.
7. Hold Interact and sweep across several pickables — each newly focused pickable grabs immediately (no hold-delay wait).
8. Hold coins, aim sorting hopper — red/green outline on whole station (not crank); hover crank alone — gold crank outline.
9. Hold dirty artifact, aim cleaning station — ItemMesh + red/green whole-station outline by validity; wrong item — red.
10. Tune on definition assets: `scalePixels`, `depthThreshold`, `normalThreshold`, `hdrBoost`, `maskDilatePixels`.

## Cursor Notes

- `PlacementGhostStyle.StackOutline` replaced the invisible StackVolume cylinder; outline comes only from target mesh renderers via the renderer feature.
- `HoverOutlineRegistrar` uses ownership (`Pickable` vs `StackVolume`) so placement `ClearPreview` no longer wipes gem/artifact hover outlines. Cleaning station and stacks share the StackVolume owner tag for placement outlines.
- Display-table outline targets the aimed slot only (`AppendPreviewStackOutlineRenderers`), not every column on the table.
- `SyncFromItem` requires real ghost mesh renderers before early-returning, so leaving outline-only stack aim restores ItemMesh correctly.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
