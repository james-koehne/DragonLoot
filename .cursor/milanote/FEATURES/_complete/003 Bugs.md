# Bugs

Category

General bugs

Status

Complete

## Tasks

- [x] Ensure coins always land properly
- [x] Auto stacking coins
- [x] Coyote jump
- [x] Dial light impact on gems
- [x] Instanced coins not showing
- [x] Flipped picking up coin stack, should pick up active from the selection then up goes to your hand bottom up
- [x] Gems should always be selectable from the piles
- [x] Place/throw down coins on hold but also add a small padding for the first auto
- [x] Pick up indicator for artifacts
- [x] Can throw coins at constellation - But they just drop to pile on floor
- [x] Top of pile hard to pickup gems as pile goes down
- [x] Movement penalty off by default
- [x] Glide press jump again to cancel
- [x] Auto sort table/coin stacks on an auto sort table - if placement is invalid it should always find a valid slot
- [x] There is a bug that needs fixing

## Metadata

Milanote ID:
bugs-general-bugs

Last Sync:
2026-08-06

Category Sort Score:
229376

Category Sort Index:
3

Sort Score:
9223372036854775807

Sort Index:
2147483647

## Cursor Implementation

Fixed auto-sort display table placement when the aimed slot is invalid (full stack, occupied gem slot, or mismatched coin column). Preview still snaps to the hovered pile so invalid feedback stays local; placement now runs a second pass with `AutoFindValidSlot` on `TypedDisplayTableInteractable` and `MixedDisplayTableInteractable` targets.

`TrySecondaryPlace` previously returned early when the ghost was red, even though the no-preview path already enabled auto-find. Hold-to-place repeat uses the same secondary path, so it benefits too.

## Files Modified

- Assets/Code/Interaction/PlayerPlacement.cs

## Testing Instructions

1. Enter play mode with a coin display table that has at least one full slot and one empty slot.
2. Pick up a matching coin and aim at the full slot — ghost should stay red on that stack.
3. Right-click (or hold secondary place) — coin should snap into the nearest valid empty/stackable slot, not fail silently.
4. Repeat on a mixed display table: aim at a slot holding a different treasure type with a stackable coin in hand — placement should land on the nearest valid slot for that coin type.
5. Fill every slot on a typed table and aim at any slot — placement should still fail (no valid slot exists).
6. Aim at a typed table with the wrong coin/gem type — placement should still fail (no slot accepts that definition).

## Cursor Notes

Preview intentionally keeps `AutoFindValidSlot` off so the ghost shows invalid feedback on the hovered pile; only the place attempt auto-resolves.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
