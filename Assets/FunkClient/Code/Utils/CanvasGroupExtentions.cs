using System.Collections.Generic;
using System.Threading.Tasks;

using Tweens;
using Tweens.Core;

using UnityEngine;

public static class CanvasGroupExtensions
{
	private static readonly Dictionary<CanvasGroup, TweenInstance> activeTweens = new Dictionary<CanvasGroup, TweenInstance>();

	private static void CancelActiveTween( CanvasGroup canvasGroup )
	{
		if ( activeTweens.TryGetValue( canvasGroup, out var tween ) )
		{
			tween.Cancel();
			activeTweens.Remove( canvasGroup );
		}
	}

	private static void RegisterTween( CanvasGroup canvasGroup, TweenInstance tween )
	{
		CancelActiveTween( canvasGroup );
		activeTweens.Add( canvasGroup, tween );
	}

	public static void FadeIn( this CanvasGroup canvasGroup, float duration = 0.25f, EaseType ease = EaseType.SineInOut, GameObject tweenHost = null )
	{
		if ( canvasGroup == null )
			return;

		if ( tweenHost == null )
			tweenHost = canvasGroup.gameObject;

		CancelActiveTween( canvasGroup );

		canvasGroup.alpha = 0f;

		FloatTween tween = new FloatTween
		{
			from = 0.0f,
			to = 1.0f,
			duration = duration,
			easeType = ease,
			onUpdate = ( _, value ) =>
			{
				canvasGroup.alpha = value;
			},
			onEnd = ( _ ) =>
			{
				canvasGroup.alpha = 1.0f;
				canvasGroup.interactable = true;
				canvasGroup.blocksRaycasts = true;
			}
		};

		TweenInstance<Transform, float> tweenInstance = tweenHost.AddTween( tween );
		RegisterTween( canvasGroup, tweenInstance );
	}

	public static void FadeOut( this CanvasGroup canvasGroup, float duration = 0.25f, EaseType ease = EaseType.SineInOut, GameObject tweenHost = null )
	{
		if ( canvasGroup == null )
			return;

		if ( tweenHost == null )
			tweenHost = canvasGroup.gameObject;

		CancelActiveTween( canvasGroup );

		FloatTween tween = new FloatTween
		{
			from = canvasGroup.alpha,
			to = 0.0f,
			duration = duration,
			easeType = ease,
			onUpdate = ( _, value ) =>
			{
				canvasGroup.alpha = value;
			},
			onEnd = ( _ ) =>
			{
				canvasGroup.alpha = 0.0f;
				canvasGroup.interactable = false;
				canvasGroup.blocksRaycasts = false;
			}
		};

		TweenInstance<Transform, float> tweenInstance = tweenHost.AddTween( tween );
		RegisterTween( canvasGroup, tweenInstance );
	}

	public static void ToggleVisibility( this CanvasGroup canvasGroup, float duration = 0.25f, EaseType ease = EaseType.SineInOut, GameObject tweenHost = null )
	{
		if ( canvasGroup.alpha > 0.5f )
			canvasGroup.FadeOut( duration, ease, tweenHost );
		else
			canvasGroup.FadeIn( duration, ease, tweenHost );
	}

	public static Task FadeInAsync( this CanvasGroup canvasGroup, float duration = 0.25f, EaseType ease = EaseType.SineInOut, GameObject tweenHost = null )
	{
		if ( canvasGroup == null )
			return Task.CompletedTask;

		if ( tweenHost == null )
			tweenHost = canvasGroup.gameObject;

		CancelActiveTween( canvasGroup );

		canvasGroup.alpha = 0f;

		var tcs = new TaskCompletionSource<bool>();

		FloatTween tween = new FloatTween
		{
			from = 0.0f,
			to = 1.0f,
			duration = duration,
			easeType = ease,
			onUpdate = ( _, value ) =>
			{
				canvasGroup.alpha = value;
			},
			onEnd = ( _ ) =>
			{
				canvasGroup.alpha = 1f;
				canvasGroup.interactable = true;
				canvasGroup.blocksRaycasts = true;
				tcs.TrySetResult( true );
			},
			onCancel = ( _ ) =>
			{
				tcs.TrySetCanceled();
			}
		};

		TweenInstance<Transform, float> tweenInstance = tweenHost.AddTween( tween );
		RegisterTween( canvasGroup, tweenInstance );

		return tcs.Task;
	}

	public static Task FadeOutAsync( this CanvasGroup canvasGroup, float duration = 0.25f, EaseType ease = EaseType.SineInOut, GameObject tweenHost = null )
	{
		if ( canvasGroup == null )
			return Task.CompletedTask;

		if ( tweenHost == null )
			tweenHost = canvasGroup.gameObject;

		CancelActiveTween( canvasGroup );

		var tcs = new TaskCompletionSource<bool>();

		FloatTween tween = new FloatTween
		{
			from = canvasGroup.alpha,
			to = 0.0f,
			duration = duration,
			easeType = ease,
			onUpdate = ( _, value ) =>
			{
				canvasGroup.alpha = value;
			},
			onEnd = ( _ ) =>
			{
				canvasGroup.alpha = 0f;
				canvasGroup.interactable = false;
				canvasGroup.blocksRaycasts = false;
				tcs.TrySetResult( true );
			},
			onCancel = ( _ ) =>
			{
				tcs.TrySetCanceled();
			}
		};

		TweenInstance<Transform, float> tweenInstance = tweenHost.AddTween( tween );
		RegisterTween( canvasGroup, tweenInstance );

		return tcs.Task;
	}
}
