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
- **Constellation Ignition** on player complete: sequential gem rim flare (`cascadeStagger`), line width/glow/scroll surge (`PlayCompleteSurge`), `completeFeedbacks`. After the surge has fully eased back to base width/intensity, gems return to connected-level glow (`completedGlowBaseline` = 1) and idle breathe starts on gems + lines. Start-fill complete enables breathe only (no cascade).
- `connectionFeedbacks` fire when an edge first becomes lit. Assign Feedbacks chains on the GemConstellation prefab in the Inspector.
- `GemConstellationCompletedEvent.FromPlayer` is true only for player settle-complete (false for start-fill). Discovery toast and ability rewards ignore start-fill.
- Scene hook `ConstellationAbilityReward`: assign `Ability_Glide` (Double Jump) on the constellation instance to unlock on player complete. Unlock toast follows discovery toast via `UnlockRewardToastUI`.

## Files Modified

- Assets/Code/Interaction/GemConstellationInteractable.cs
- Assets/Code/Interaction/GemConstellationLineVisual.cs
- Assets/Code/Interaction/TreasureItem.cs
- Assets/Code/Interaction/GemConstellationEvents.cs
- Assets/Code/Interaction/ConstellationAbilityReward.cs
- Assets/Code/UI/DiscoveryToastUI.cs
- Assets/Code/UI/UnlockRewardToastUI.cs
- Assets/Code/Core/GameMode.cs
- Assets/Materials/Shaders/Gem/DragonLoot_Gem.shader
- Assets/Materials/Shaders/Gem/GemInput.hlsl
- Assets/Materials/Shaders/Gem/GemLighting.hlsl
- .cursor/milanote/FEATURES/_complete/005 Constellations.md

## Testing Instructions

1. Place a gem on an empty constellation slot — gem flies to local −Z in front of the slot, holds, then screw-spins into the socket; place SFX and scale punch play.
2. Place a second gem that completes a line — line lights and both endpoint gems show gem-colored rim glow.
3. Remove one endpoint gem — that gem’s glow clears; remaining gems stay glowing only if still on a lit edge.
4. Fill all slots (player) — gems flare in slot order, lines surge brighter/wider, complete feedback plays if assigned. When the flare finishes, gem/line glow drop back to the normal connected look and a clear breathe pulse continues (not stuck at flare brightness).
5. With `ConstellationAbilityReward` + `Ability_Glide` wired: player complete shows “Constellation Complete” discovery toast, then “Unlocked: Double Jump” unlock toast (lower, non-overlapping). Glide works afterward; persists in profile.
6. Remove a gem after complete — breathe/surge stop; glow returns to per-edge connected state.
7. Start-fill already-complete constellations — breathe only at play start (no ignition cascade spam, no complete/unlock toasts).

## Cursor Notes

- Feedbacks fields (`placeFeedbacks`, `connectionFeedbacks`, `completeFeedbacks`) are null until assigned on `Assets/Addressables/Tables/GemConstellation.prefab`.
- Place tunables: `approachDistance`, `approachDuration`, `approachHold`, `placeSpinDuration`, `placeSpins`, `connectedGlowLerpSpeed`.
- Ignition tunables: `cascadeStagger`, `ignitionBoostDuration`, `ignitionPeakGlow`, `completedGlowBaseline` (1 = connected idle), `breatheAmplitude`, `breatheSpeed`, `completeSurgeDuration`; line surge muls + `breatheGlowAmplitude` on `GemConstellationLineVisual`.
- Line glow base is cached from the source material, not the runtime instance, so the flare cannot bake into idle intensity.
- Scene wiring: add `ConstellationAbilityReward` on the constellation, assign Reward Ability → `Ability_Glide`.
- Follow-ups: connection spark particles, gem-tinted line colors.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
