# Constellations

Category

Sorted Treasure

Status

Complete

## Bugs

- [x] Line not visible

## Metadata

Milanote ID:
1WKwPE10ct0TcT

Last Sync:
2026-07-28

Category Sort Score:
65536

Category Sort Index:
1

Sort Score:
0

Sort Index:
0

## Cursor Implementation

- Place settle is a two-phase motion: arc to a point in front of the slot (local −Z / `-transform.forward * approachDistance`), brief hold, then screw-spin into the socket around constellation Z.
- On settle: place SFX, `CoinGemInteractFeedback.PlayPlace`, and optional `placeFeedbacks`.
- Connected gems drive `_ConnectedGlow` on `DragonLoot/Gem` through `TreasureItem.SetConnectedGlow` (MaterialPropertyBlock), gem-colored rim only; lerped when any lit edge touches the slot.
- **Constellation Ignition** on player complete: sequential gem rim flare (`cascadeStagger`), line width/glow/scroll surge (`PlayCompleteSurge`), `completeFeedbacks`, then idle breathe on gems + lines while complete. Start-fill complete enables breathe only (no cascade).
- `connectionFeedbacks` fire when an edge first becomes lit. Assign Feedbacks chains on the GemConstellation prefab in the Inspector.

## Files Modified

- Assets/Code/Interaction/GemConstellationInteractable.cs
- Assets/Code/Interaction/GemConstellationLineVisual.cs
- Assets/Code/Interaction/TreasureItem.cs
- Assets/Materials/Shaders/Gem/DragonLoot_Gem.shader
- Assets/Materials/Shaders/Gem/GemInput.hlsl
- Assets/Materials/Shaders/Gem/GemLighting.hlsl
- .cursor/milanote/FEATURES/_complete/005 Constellations.md

## Testing Instructions

1. Place a gem on an empty constellation slot — gem flies to local −Z in front of the slot, holds, then screw-spins into the socket; place SFX and scale punch play.
2. Place a second gem that completes a line — line lights and both endpoint gems show gem-colored rim glow.
3. Remove one endpoint gem — that gem’s glow clears; remaining gems stay glowing only if still on a lit edge.
4. Fill all slots (player) — gems flare in slot order, lines surge brighter/wider, complete feedback plays if assigned, then soft breathe continues.
5. Remove a gem after complete — breathe/surge stop; glow returns to per-edge connected state.
6. Start-fill already-complete constellations — breathe only at play start (no ignition cascade spam).

## Cursor Notes

- Feedbacks fields (`placeFeedbacks`, `connectionFeedbacks`, `completeFeedbacks`) are null until assigned on `Assets/Addressables/Tables/GemConstellation.prefab`.
- Place tunables: `approachDistance`, `approachDuration`, `approachHold`, `placeSpinDuration`, `placeSpins`, `connectedGlowLerpSpeed`.
- Ignition tunables: `cascadeStagger`, `ignitionPeakGlow`, `completedGlowBaseline`, `breatheAmplitude`, `breatheSpeed`, `completeSurgeDuration`; line surge muls on `GemConstellationLineVisual`.
- Follow-ups: connection spark particles, gem-tinted line colors.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
