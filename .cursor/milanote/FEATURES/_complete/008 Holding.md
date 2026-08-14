# Holding

Category

Player

Status

Complete

## Tasks

- [x] Held stack to left hand
- [x] Right hand for current object
- [x] X/Z slight variation for held stack
- [x] Weight limit vs carry X limit - Slow move speed

## Bugs

- [x] Flickering on shadows

## Metadata

Milanote ID:
1WM2iN1k4GAQd6

Last Sync:
2026-07-28

Category Sort Score:
0

Category Sort Index:
0

Sort Score:
196608

Sort Index:
1

## Cursor Implementation

Reworked player carry into three category buckets (Coins / Gems / Artifacts). Each bucket has its own left-hand HoldRoot collection and right-hand ActiveRoot item. New pickups of a category force-select that pouch and merge into left-hand bottom when Active is occupied; placing Active pulls the next item from held index 0 into the right hand.

Category switching (1/2/3) animates the old and new rigs between active and rest poses, then hides the outgoing category. Coin left-hand stacks use `CoinStackCylinderVisual` with a visual height cap; gems and artifacts remain individual meshes.

Whole-stack interactions: hold E looking at a `GroundCoinStack` or multi-gem pyramid to absorb the entire collection into the left-hand bottom; hold F to dump the selected category as a group (coins merge/create ground stacks or fly into the coin sorter hopper, gems reform pyramids, artifacts scatter onto the floor). Hold duration scales with quantity via `CarryDefinition` tiers (defaults ≤10→2s, ≤30→4s, ≤60→6s, cap 6s). Circular progress ring around the crosshair; interruptible on release / lost target. LMB takes one aimed stack coin only; hold-LMB repeats one-at-a-time. Clean and ability keybinds unbound this pass.

Coin visual pool (`TreasureItemFactory.RentVisualCoin`) instantiates/reuses real Addressable coin prefabs (primitive fallback only if missing). Gold-pile multi-steal starts a staggered pooled prefab flight per coin (first → Active, rest → left defs after flight). Scroll promotes from cylinder bottom or top by direction. Context UI always shows LMB pickup when aiming at pickables, alongside E when whole-stack is offered. Place settle hooks expose optional FeedbackSystem `Feedbacks` on stacks / placement / sorter dump.

## Files Modified

- Assets/Code/Interaction/TreasureItemFactory.cs
- Assets/Code/Interaction/CoinStackFlight.cs
- Assets/Code/Interaction/GroundCoinStack.cs
- Assets/Code/Interaction/TreasurePileInteractable.cs
- Assets/Code/Interaction/CoinSortingStation.cs
- Assets/Code/Interaction/PlayerPlacement.cs
- Assets/Code/Player/PlayerCarry.cs
- Assets/Code/Player/PlayerWholeStackInteraction.cs
- Assets/Code/Definitions/CarryDefinition.cs
- Assets/Code/UI/InteractionContextUI.cs

## Testing Instructions

1. Enter play mode. LMB a coin — right hand shows the real coin prefab (not a primitive). Pick up more — left cylinder grows; right keeps Active.
2. Aim mid-height on a ground coin stack and LMB — only that aimed coin flies to the right hand. Hold LMB — takes one at a time from the aim. Context UI shows `[LMB] Take coin` and `[E] Hold to pick up stack`.
3. Hold E on a large stack — charge time is longer than a small stack (tiers 2/4/6s). Whole stack flies in as a cylinder.
4. Multi-steal from a gold pile — each stolen coin flies as its own prefab (staggered); first becomes Active, later merge into left after landing.
5. Scroll while carrying many coins — promote from bottom vs top of the left cylinder matches scroll direction.
6. With coins selected, pick up a gem — pouch switches to gems and shows the gem Active. Reverse (gems → coins) also switches.
7. Carry coins, aim at the sorter hopper, hold F — stack cylinder flies into the hopper; remaining coins stay if hopper was partial. RMB dump into hopper also animates the stack flight.
8. Place coins/gems/artifacts onto floor or stacks — optional land Feedbacks fire when wired in the Inspector.

## Cursor Notes

- Clean / ability rebinds deferred; `PlayerCleaning` and `PlayerAbilities` still exist but have no keybinds.
- Coin flights use prefab-backed `RentVisualCoin` / pool return via `Despawn` for pooled instances; left hand remains definition + cylinder.
- Feedbacks slots are empty for authoring — punch/SFX content is Inspector work.
- Stack shader variation uses a static `_VariationSeed` per stack/hand (not world position).
- Unity must regenerate `.meta` for new scripts on import.
## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
