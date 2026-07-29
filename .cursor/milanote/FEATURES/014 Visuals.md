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
- `PlayerInteraction` — pickable gems/artifacts (`PlayerInteractionDefinition.pickableOutline`)
- `PlayerPlacement` — coin stack volume capsule (`PlayerPlacementDefinition.stackOutline`)

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

Removed: `PickableFocusIndicator.cs`, mesh-overlay `DragonLoot/Pickable Outline` usage.

## Testing Instructions

1. Enter play mode — confirm `HoverOutline` feature on `PC_Renderer` (added by installer).
2. Hover gem/artifact (empty hands) — golden HDR outline on silhouette + creases; bloom halo visible.
3. Hold coins, aim at ground stack — green/red outline on stack volume only; coins readable underneath.
4. No hover — no outline, passes early-out.
5. Tune on definition assets: `scalePixels`, `depthThreshold`, `normalThreshold`, `hdrBoost`, `maskDilatePixels`.
6. If halo weak: lower Bloom threshold on volume or raise `hdrBoost`.
7. Item-mesh placement ghost still fresnel (unchanged).

## Cursor Notes

- Stack volume cylinder `MeshRenderer` is disabled in-game; mask pass still draws it for outline registration.
- Pickable hover deferred when stack volume preview is active (`HasActiveStackVolumePreview`).

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
