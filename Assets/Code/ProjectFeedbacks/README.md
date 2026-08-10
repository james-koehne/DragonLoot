# Project Feedbacks

Dragon Loot / Little Hoardkeeper feedbacks that must **not** live in `Assets/Plugins/FeedbackSystem/`.

The portable FeedbackSystem discovers any `Feedback` subclass automatically. Add a file here and it appears in the `Feedbacks` component Add menu.

```csharp
using System;
using FeedbackSystem;
using UnityEngine;

[Serializable]
[FeedbackInfo( "Treasure/Scatter Coins" )]
public class ScatterCoinsFeedback : Feedback
{
	public override void Play()
	{
	}
}
```

Suggested folders as features land:

```text
ProjectFeedbacks/
├── Audio/
├── Gameplay/
├── Treasure/
├── Dragon/
└── UI/
```
