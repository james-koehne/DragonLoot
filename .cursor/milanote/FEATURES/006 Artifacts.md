# Artifacts

Category

Sorted Treasure

Status

In Progress

## Tasks

- [x] Need editor tooling to see what they would look like in the slot pre-play mode
- [x] Remove books and add scrolls
- [x] If hovering table show valid placement if correct artifact
- [ ] Need to hide the base artifact ghost and show the invalid/valid ghost when hovering over an artifact slot

## Bugs

- [x] Clicking on table should set to the correct slot
- [x] Placement validation improved visual
- [x] Not displaying on the table correctly

## Metadata

Milanote ID:
1WKwSM10ct0TcV

Last Sync:
2026-07-28

Category Sort Score:
65536

Category Sort Index:
1

Sort Score:
32768

Sort Index:
1

## Cursor Implementation

When aiming at an artifact presentation table while holding treasure, the held-item placement ghost stays suppressed (`PlacementGhostStyle.Suppressed`) and slot indicators provide valid/invalid feedback instead.

**Fixes applied:**
1. **Hover vs placement slot split** — Ray-resolved slot index (the slot under the crosshair) is tracked separately from the placement slot (which may fall back to another empty matching slot). Indicator feedback always reflects the hovered slot; placement preview/acceptance still uses the nearest valid slot when appropriate.
2. **Same-frame indicator refresh** — `RefreshAimFeedback()` is called immediately from `TryGetPlacementPreview` so slot tints update in the same frame as aim feedback, regardless of `LateUpdate` script execution order.

When hovering a slot, its cyan hologram tints green (correct artifact) or red (wrong/occupied). Other empty slots dim. The held-item placement ghost remains hidden.

## Files Modified

- `Assets/Code/Interaction/ArtifactPresentationTableInteractable.cs`
- `Assets/Code/Interaction/ArtifactPresentationSlotIndicators.cs`

## Testing Instructions

1. Enter play mode with an artifact presentation table that has multiple configured slots (at least one empty).
2. Pick up an artifact that matches one empty slot.
3. Aim at that slot's volume — the cyan slot hologram should turn **green**; no separate held-item ghost should appear at the slot.
4. Aim at a slot that requires a **different** artifact — that slot's hologram should turn **red**, even if another slot on the table would accept the held item.
5. Aim at the table surface away from slots — nearest slot feedback should still apply; placement should succeed on the matching empty slot when clicking.
6. Place the artifact — slot indicator hides, artifact snaps in, other slots remain cyan.

## Cursor Notes

Slot indicator feedback uses `PlacementFeedbackColors.ValidIndicator` / `InvalidIndicator` tints on the existing hologram mesh — not the `PlacementGhost` fresnel shader. This matches the prior "placement validation improved visual" bug fix approach.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
