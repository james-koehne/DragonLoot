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

When aiming at an artifact presentation slot while holding treasure:

1. The cyan **base hologram** for that slot is hidden.
2. The held-item **placement ghost** is shown at the slot pose in green (valid) or red (invalid).
3. Other empty slots stay visible but dimmed cyan.

Feedback follows the slot under the crosshair. Placement may still fall back to another matching empty slot on click. Aim feedback is refreshed the same frame via `RefreshAimFeedback()` so script order does not leave the cyan hologram stuck on.

## Files Modified

- `Assets/Code/Interaction/ArtifactPresentationTableInteractable.cs`
- `Assets/Code/Interaction/ArtifactPresentationSlotIndicators.cs`

## Testing Instructions

1. Enter play mode with an artifact presentation table that has multiple configured empty slots.
2. Pick up an artifact that matches one empty slot.
3. Aim at that slot — cyan hologram **disappears**; green held-item ghost appears at the slot. No cyan overlay on top of it.
4. Aim at a slot that requires a **different** artifact — cyan hologram **disappears**; red held-item ghost appears at that slot.
5. Look away from the table — placement ghost hides; cyan holograms return on empty slots.
6. Place a matching artifact — ghost clears, artifact snaps in, that slot stays empty of hologram.

## Cursor Notes

Earlier approach only recolored the slot indicator via `_TintColor`. The shader still adds a fixed cyan `_RimColor`, so the base hologram always looked present. Hiding the indicator and using `PlacementGhostStyle.ItemMesh` avoids that.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
