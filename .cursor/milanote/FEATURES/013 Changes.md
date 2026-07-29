# Changes

Category

Physics

Status

In Progress

## Tasks

- [x] Sometimes placing coin on ground makes it not interactable
- [x] Each object sliperyness it NQR
- [x] Auto merging coins to piles
- [x] Reduce sliding of artifacts
- [x] Sliding up against flow should be very difficult
- [x] Gems roll should slow down as the velocity does
- [ ] When throwing a coin/gem straight up it should still bounce like it would if you threw forward
- [ ] Gems roll amount should be relative to the velocity
- [ ] When coins are thrown and they snap together to form a coin stack, animate the movement like the coin toss onto a coin stack
- [ ] All items look like they phase into the ground, the ground is solid so never show items below it
- [ ] Artifacts should bounce too when thrown, but heavily

## Metadata

Milanote ID:
1WMvbm1eg5uy4g

Last Sync:
2026-07-28

Category Sort Score:
196608

Category Sort Index:
3

Sort Score:
-65536

Sort Index:
0

## Cursor Implementation

Three treasure-surface physics tweaks in `TreasureSurfaceSimulator`:

1. **Artifacts slide less** — Crowns/artifacts no longer use a fixed `MoveTowards` settle rate. They now use material friction × `artifactFrictionScale` (2.5) with a lower `artifactSpeedScale` cap (0.35× max slide speed). Slope still reduces friction slightly but much less than coins/gems (35% vs 92%), so heavy props stop quickly on flat gold.

2. **Against-flow uphill is hard** — When a coin/gem’s velocity opposes the painted flow vector, extra deceleration is applied: `flowUphillResistance` (5×) × base friction × gravity. This stacks on top of normal slope friction and the existing downhill push, so climbing back up a pile against flow bleeds speed fast.

3. **Gem roll tracks linear speed** — Grounded gem angular velocity is set directly to `speed × gemRollAngularScale` along the smoothed roll axis (no laggy lerp). Visual spin now drops immediately as linear velocity drops.

4. **Straight-up throw bounce** — Bounce eligibility and hop impulse now use full 3D impact speed (`velocity.magnitude`), not horizontal speed only. Throw flight passes downward landing velocity into `EnterSurface` so vertical lobs still register bounces.

5. **Gem roll scales with velocity** — Initial gem spin on register also uses full impact speed; grounded roll spin was already speed-proportional.

6. **Auto-stack merge animation** — When two loose coins auto-merge, or a coin settles onto an existing ground stack, the incoming coin plays the append arc/flip tween (`BeginAppendFlight`) instead of snapping.

7. **No ground clipping** — Sim Y uses stable seat lift and never goes below surface height. Rotation-dependent mesh clamps were removed (they floated artifacts). Settle snaps to upright contact Y.

8. **Artifact throw bounce** — Crowns/artifacts share the hop path with heavy damping (`artifactBounceRestitution` 0.08, hop capped ~0.45). No post-bounce tumble spin.

9. **Artifact throw flip** — Flight tween spins artifacts exactly once (`ResolveFlightSpins` → 1) before landing upright.

Tuning lives on `TreasureSurfaceDefinition` / asset: `artifactFrictionScale`, `artifactSpeedScale`, `flowUphillResistance`, `artifactMaxBounces`, `artifactBounceRestitution`.

## Files Modified

- `Assets/Code/Definitions/TreasureSurfaceDefinition.cs`
- `Assets/Definitions/TreasureSurface/TreasureSurfaceDefinition.asset`
- `Assets/Code/TreasureSurface/TreasureSurfaceSimulator.cs`
- `Assets/Code/TreasureSurface/TreasureSurfaceThrow.cs`

## Testing Instructions

1. **Artifact slide** — Throw or drop a crown/artifact onto a gold pile slope. Expected: short slide distance, stops within ~1 m on moderate slopes; much less travel than coins.
2. **Against-flow uphill** — On a sloped pile with visible flow, throw a coin/gem downhill so it picks up speed, then observe if it can coast back uphill against the flow. Expected: speed drops sharply when moving opposite flow; item should stall or reverse slowly rather than climb easily.
3. **Gem roll damping** — Roll a gem down a slope and watch it decelerate on flat ground. Expected: visual spin rate decreases in sync with horizontal speed (no “spinning in place” while nearly stopped).
4. **Straight-up throw bounce** — Throw a coin or gem nearly straight up onto a pile. Expected: it hops/bounces on landing like a forward throw, not a dead stop.
5. **Auto-stack animation** — Throw a coin near an existing ground stack, or throw two coins close together. Expected: incoming coin arcs/flips onto the stack instead of snapping.
6. **Ground clipping** — Throw gems/crowns onto slopes and watch them settle. Expected: mesh bottoms never sink below the gold surface; artifacts do not hang in mid-air.
7. **Artifact bounce + flip** — Throw a crown/artifact onto the pile. Expected: exactly one flip in flight, then a short heavy thud-bounce, settling on the surface (not floating).
8. **Regression** — Coins still slide downhill with flow; auto-stack on settle still works; gems still hop/bounce on impact.

## Cursor Notes

- `artifactSettleSpeed` kept on the definition for backward compatibility but artifacts now use friction-based decel. Consider removing in a future cleanup if unused elsewhere.
- Player slide/climb is unchanged; flow uphill resistance applies only to loose treasure surface simulation.
- If against-flow still feels too easy on very steep piles, raise `flowUphillResistance` (try 8–10) in the asset.
- Artifact hop height is capped ~0.45 m/s vertical; raise `artifactBounceRestitution` slightly if crowns feel too dead.

## Developer Verification

Status

☐ Not Tested

☐ Testing

☐ Verified

Date


Comments
