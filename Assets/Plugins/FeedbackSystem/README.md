# FeedbackSystem

Lightweight, inspector-driven feedback chaining for Unity. Inspired by FEEL, without the weight.

Portable core lives in this folder. Copy `Assets/Plugins/FeedbackSystem/` into another project, or later wrap it as a UPM package. Game-specific feedbacks stay outside this folder.

## Folders

```text
FeedbackSystem/
├── Runtime/          Portable runtime (asmdef: FeedbackSystem.Runtime)
│   ├── Core/
│   └── Feedbacks/    Built-in audio, particles, transforms, etc.
├── Editor/           Inspector UX (asmdef: FeedbackSystem.Editor)
└── Samples/          Demo bootstrap
```

Project-specific feedbacks for Dragon Loot live in `Assets/Code/ProjectFeedbacks/` and must never be referenced by this package.

## Quick start

1. Add the `Feedbacks` component to a GameObject (`Add Component > FeedbackSystem > Feedbacks`).
2. Click **+ Add Feedback** and configure entries in the Inspector.
3. From gameplay code:

```csharp
using FeedbackSystem;

public class Example
{
	public Feedbacks sortingCompleteFeedback;

	public void OnSorted()
	{
		sortingCompleteFeedback.Play();
	}
}
```

Optional context (not required):

```csharp
FeedbackContext context = new FeedbackContext();
context.Source = gameObject;
context.Target = other;
context.Position = transform.position;
sortingCompleteFeedback.Play( context );
```

## Public API

| Method | Behaviour |
|--------|-----------|
| `Play()` | Stops any current run, then plays the list sequentially |
| `Play(FeedbackContext)` | Same, with optional context available to feedbacks via `Context` |
| `Stop()` | Stops the sequence and any timed ticker ops |
| `Reset()` / `ResetFeedbacks()` | Stop plus per-feedback `Reset()` |

Zero-hold feedbacks run in the same frame. `DelayFeedback` and nested `SequenceFeedback` hold the sequence. Move / punch / shake start on the shared ticker and do **not** block later entries.

## Adding a new feedback

Create one class anywhere in the project (core or `ProjectFeedbacks`):

```csharp
using System;
using FeedbackSystem;
using UnityEngine;

[Serializable]
[FeedbackInfo( "Treasure/Scatter Coins" )]
public class ScatterCoinsFeedback : Feedback
{
	public int Count = 8;

	public override void Play()
	{
		// Project-specific behaviour
	}
}
```

It appears in **Add Feedback** automatically. No core registration list.

Lifecycle you may override: `Initialize()`, `Play()`, `Stop()`, `Reset()`, `GetHoldDuration()`.

## Built-in feedbacks

- **Audio:** Play SFX, Play Random SFX (optional `AudioSource`, otherwise pooled one-shots)
- **Particles:** Play Particles, Particle Burst (`Emit`)
- **GameObjects:** Set Active, Instantiate, Destroy
- **Transforms:** Set Position / Rotation / Scale, Move, Punch Scale, Shake
- **Animation:** Play Animation, Set Animator Trigger
- **Visual:** Set Renderer Enabled, Set Color (`MaterialPropertyBlock`, configurable property name)
- **Utility:** Delay, Unity Event
- **Composition:** Parallel, Sequence

## Performance notes

- Update-driven player + host ticker. No per-feedback coroutines.
- Instant entries run same-frame.
- Host `Update` early-outs when idle.
- Default SFX uses a small growing `AudioSource` pool (not `PlayClipAtPoint`).
- `SetColorFeedback` never touches `renderer.material`.
- Instantiate / Destroy are intentionally simple and relatively expensive.

## Copying to another project

1. Copy `Assets/Plugins/FeedbackSystem/`.
2. Let Unity generate `.meta` files.
3. Add `using FeedbackSystem;` from gameplay assemblies (`FeedbackSystem.Runtime` is auto-referenced).

Do not copy `Assets/Code/ProjectFeedbacks/`.

## Demo

Scene: `Samples/FeedbackSystemDemo.unity`

1. Open **FeedbackSystem > Open Demo Scene** (adds `FeedbackDemoController` if missing), **or** open the scene and add `FeedbackDemoController` to the **Feedback Demo** object.
2. Press Play.

The controller builds cubes, particles, generated beeps, and UI buttons at runtime so the demo has no clip/prefab dependencies.

Animator feedbacks are not auto-wired in the demo (they need an Animator Controller). See testing steps below.

## Testing instructions

### Inspector

1. Create an empty GameObject, add `Feedbacks`.
2. Click **+ Add Feedback**. Confirm categories (Audio, Particles, GameObjects, Transforms, Animation, Visual, Utility, Composition).
3. Add several entries, reorder with ▲ / ▼, duplicate with **D**, remove with **X**, collapse foldouts.
4. Add `Composition/Parallel` and `Composition/Sequence`. Expand them and add nested feedbacks without editing core code.

### Play API

1. Open **FeedbackSystem > Open Demo Scene** and press Play.
2. Click each left-side button. Expected results:

| Button | Expected |
|--------|----------|
| Punch Scale | Orange cube pops scale 1 → larger → 1 |
| Shake Transform | Cyan cube shakes position/rotation, then restores |
| Move + Delay | Green cube moves up, pauses, moves back down |
| Set Color | White cube flashes pink, then returns to white |
| Set Active | Purple cube disappears briefly, then returns |
| Play Particles | Particle system plays |
| Particle Burst | Extra particles emit immediately |
| Play SFX | Short generated beep |
| Play Random SFX | One of three beep pitches |
| Sequence Combo | Random beep + particles + punch + shake together, then directional light tint toggles (UnityEvent) |
| Nested Sequence | Blue cube scales up, waits, rotates, waits, restores |
| Instantiate | Small sphere appears near the right side (template lives under the floor) |
| Destroy | Red cube punches then is destroyed (button will no-op after) |
| Renderer Enabled | Grey cube renderer disables then re-enables |
| Set Position | Teal cube hops up then returns |

3. While a move/punch/shake is running, click **Stop** on that `Feedbacks` inspector (play-mode buttons). Motion should halt / restore as designed.
4. Click a demo button twice quickly. The sequence should restart rather than stack.

### Audio source override

1. Add an `AudioSource` to a GameObject with `PlaySFXFeedback`.
2. Assign that source on the feedback.
3. Play — clip plays through the assigned source, not the pool.

### Animation (manual)

1. Create a GameObject with an `Animator` and a controller that has a state (e.g. `Bounce`) and a trigger (e.g. `Play`).
2. Add `Play Animation` with that state name, and `Set Animator Trigger` with that trigger.
3. Press Play on the `Feedbacks` component. The animator state / trigger should fire.

### Project feedback discovery

1. Create `Assets/Code/ProjectFeedbacks/Example/LogMessageFeedback.cs` as a `Feedback` subclass with `[FeedbackInfo("Project/Log Message")]`.
2. Select any `Feedbacks` component. **Add Feedback** should list **Project/Log Message** without changing package code.

### Portability smoke check

1. Confirm `Assets/Plugins/FeedbackSystem/Runtime` has no `using` of DragonLoot / game types.
2. Confirm `FeedbackSystem.Runtime.asmdef` references no game assemblies.
