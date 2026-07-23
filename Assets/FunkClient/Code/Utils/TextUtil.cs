using UnityEngine;
using TMPro;
using Tweens;
using System;
using System.Collections.Generic;

public static class TextUtils
{
	private class TweenHandle : MonoBehaviour
	{
		public TweenInstance<Transform, Color> colorTween;
		public TweenInstance<Transform, float> gradientTween;
	}

	private class ActiveTween
	{
		public TweenInstance<Transform, float> tween;
		public TMP_Text tmp;
	}

	private static readonly List<ActiveTween> activeTweens = new List<ActiveTween>();

	public static void AnimateTextShine( TMP_Text tmp, Color highlightColor, float duration = 1f, bool loop = true, float delayBetween = 0.2f )
	{
		if ( tmp == null )
			return;

		// Enable vertex gradient if not active
		if ( !tmp.enableVertexGradient )
			tmp.enableVertexGradient = true;

		CleanupDestroyedTweens();
		CleanupExistingTweens( tmp );

		VertexGradient baseGradient = new VertexGradient( tmp.color );

		FloatTween tween = new FloatTween
		{
			from = -1.0f,
			to = 2.0f,
			duration = duration,
			easeType = EaseType.Linear,
			onUpdate = ( _, t ) =>
			{
				if ( tmp == null || tmp.gameObject == null )
					return;

				Color GetShineColor( Color baseColor, float position )
				{
					float shineCenter = t;
					float distance = Mathf.Abs( position - shineCenter );
					float width = 0.95f;
					float raw = 1f - ( distance / width );
					float alpha = Mathf.Clamp( Mathf.SmoothStep( 0f, 1f, raw ), 0.0f, 0.75f );
					return Color.Lerp( baseColor, highlightColor, alpha );
				}

				var newGradient = new VertexGradient(
					GetShineColor( baseGradient.topLeft, 0f ),
					GetShineColor( baseGradient.topRight, 0.5f ),
					GetShineColor( baseGradient.bottomLeft, 0.5f ),
					GetShineColor( baseGradient.bottomRight, 1f )
				);

				tmp.colorGradient = newGradient;
			},
			onEnd = _ =>
			{
				activeTweens.RemoveAll( x => x.tmp == tmp );

				if ( loop && tmp != null && tmp.gameObject != null )
				{
					FloatTween delayTween = new FloatTween
					{
						from = 0f,
						to = 0f,
						duration = delayBetween,
						onEnd = __ =>
						{
							AnimateTextShine( tmp, highlightColor, duration, loop, delayBetween );
						}
					};
					tmp.gameObject.AddTween( delayTween );
				}
				else
				{
					// Restore base gradient if not looping
					if ( tmp != null )
						tmp.colorGradient = baseGradient;
				}
			}
		};

		TweenInstance<Transform, float> tweenInstance = tmp.gameObject.AddTween( tween );
		activeTweens.Add( new ActiveTween { tween = tweenInstance, tmp = tmp } );
	}

	public static void AnimateTextShineGradient( TMP_Text tmp, Gradient highlightGradient, float duration = 1f, bool loop = true, float delayBetween = 0.2f )
	{
		if ( tmp == null )
			return;

		// Enable vertex gradient if not active
		if ( !tmp.enableVertexGradient )
			tmp.enableVertexGradient = true;

		CleanupDestroyedTweens();
		CleanupExistingTweens( tmp );

		VertexGradient baseGradient = new VertexGradient( tmp.color );

		FloatTween tween = new FloatTween
		{
			from = -1.0f,
			to = 2.0f,
			duration = duration,
			easeType = EaseType.Linear,
			onUpdate = ( _, t ) =>
			{
				if ( tmp == null || tmp.gameObject == null )
					return;

				Color GetShineColor( Color baseColor, float position )
				{
					float shineCenter = t;
					float distance = Mathf.Abs( position - shineCenter );
					float width = 0.95f;
					float raw = 1f - ( distance / width );
					float alpha = Mathf.Clamp( raw, 0.0f, 1.0f );
					return Color.Lerp( baseColor, highlightGradient.Evaluate( alpha ), alpha );
				}

				var newGradient = new VertexGradient(
					GetShineColor( baseGradient.topLeft, 0f ),
					GetShineColor( baseGradient.topRight, 0.5f ),
					GetShineColor( baseGradient.bottomLeft, 0.5f ),
					GetShineColor( baseGradient.bottomRight, 1f )
				);

				tmp.colorGradient = newGradient;
			},
			onEnd = _ =>
			{
				activeTweens.RemoveAll( x => x.tmp == tmp );

				if ( loop && tmp != null && tmp.gameObject != null )
				{
					FloatTween delayTween = new FloatTween
					{
						from = 0f,
						to = 0f,
						duration = delayBetween,
						onEnd = __ =>
						{
							AnimateTextShineGradient( tmp, highlightGradient, duration, loop, delayBetween );
						}
					};
					tmp.gameObject.AddTween( delayTween );
				}
				else
				{
					// Restore base gradient if not looping
					if ( tmp != null )
						tmp.colorGradient = baseGradient;
				}
			}
		};

		TweenInstance<Transform, float> tweenInstance = tmp.gameObject.AddTween( tween );
		activeTweens.Add( new ActiveTween { tween = tweenInstance, tmp = tmp } );
	}

	private static void CleanupExistingTweens( TMP_Text tmp )
	{
		for ( int i = activeTweens.Count - 1; i >= 0; i-- )
		{
			if ( activeTweens[ i ].tmp == null || activeTweens[ i ].tmp == tmp )
			{
				activeTweens[ i ].tween?.Cancel();
				activeTweens.RemoveAt( i );
			}
		}
	}

	private static void CleanupDestroyedTweens()
	{
		for ( int i = activeTweens.Count - 1; i >= 0; i-- )
		{
			if ( activeTweens[ i ].tmp == null || activeTweens[ i ].tmp.gameObject == null )
			{
				activeTweens[ i ].tween?.Cancel();
				activeTweens.RemoveAt( i );
			}
		}
	}

	public static TweenInstance<Transform, Color> AnimateTextColor( TextMeshProUGUI tmp, Color fromColor, Color toColor, float duration, EaseType easeType = EaseType.Linear, bool loop = false, bool pingPong = false, int loopCount = -1 )
	{
		if ( tmp == null || tmp.gameObject == null )
			return null;

		TweenHandle handle = tmp.GetComponent<TweenHandle>() ?? tmp.gameObject.AddComponent<TweenHandle>();
		handle.colorTween?.Cancel();

		TweenInstance<Transform, Color> tweenInstance = null;
		int loopsDone = 0;

		Action<Color, Color> startTween = null; // Declare first

		startTween = ( start, end ) =>
		{
			if ( tmp == null || tmp.gameObject == null )
				return;

			var tween = new ColorTween
			{
				from = start,
				to = end,
				duration = duration,
				easeType = easeType,
				onUpdate = ( _, value ) =>
				{
					if ( tmp == null || tmp.gameObject == null )
					{
						tweenInstance?.Cancel();
						return;
					}
					tmp.color = value;
				},
				onEnd = _ =>
				{
					if ( tmp == null || tmp.gameObject == null )
						return;

					if ( !loop && !pingPong )
						return;
					loopsDone++;
					if ( loopCount >= 0 && loopsDone >= loopCount )
						return;

					if ( pingPong )
						startTween( end, start );
					else
						startTween( fromColor, toColor );
				}
			};

			tweenInstance = tmp.gameObject.AddTween( tween );
			handle.colorTween = tweenInstance;
		};

		startTween( fromColor, toColor );
		return tweenInstance;
	}

	public static TweenInstance<Transform, Color> AnimateTextColor( TextMeshProUGUI tmp, Color toColor, float duration, EaseType easeType = EaseType.Linear, bool loop = false, bool pingPong = false, int loopCount = -1 )
	{
		if ( tmp == null )
			return null;

		return AnimateTextColor( tmp, tmp.color, toColor, duration, easeType, loop, pingPong, loopCount );
	}

	public static TweenInstance<Transform, float> AnimateTextGradient( TextMeshProUGUI tmp, VertexGradient fromGradient, VertexGradient toGradient, float duration, EaseType easeType = EaseType.Linear, bool loop = false, bool pingPong = false, int loopCount = -1 )
	{
		if ( tmp == null || tmp.gameObject == null )
			return null;

		TweenHandle handle = tmp.GetComponent<TweenHandle>() ?? tmp.gameObject.AddComponent<TweenHandle>();
		handle.gradientTween?.Cancel();

		TweenInstance<Transform, float> tweenInstance = null;
		int loopsDone = 0;

		Action<VertexGradient, VertexGradient> startTween = null; // Declare first

		startTween = ( start, end ) =>
		{
			if ( tmp == null || tmp.gameObject == null )
				return;

			var tween = new FloatTween
			{
				from = 0f,
				to = 1f,
				duration = duration,
				easeType = easeType,
				onUpdate = ( _, t ) =>
				{
					if ( tmp == null || tmp.gameObject == null )
					{
						tweenInstance?.Cancel();
						return;
					}
					tmp.colorGradient = new VertexGradient(
						Color.Lerp( start.topLeft, end.topLeft, t ),
						Color.Lerp( start.topRight, end.topRight, t ),
						Color.Lerp( start.bottomLeft, end.bottomLeft, t ),
						Color.Lerp( start.bottomRight, end.bottomRight, t )
					);
				},
				onEnd = _ =>
				{
					if ( tmp == null || tmp.gameObject == null )
						return;

					if ( !loop && !pingPong )
						return;
					loopsDone++;
					if ( loopCount >= 0 && loopsDone >= loopCount )
						return;

					if ( pingPong )
						startTween( end, start );
					else
						startTween( fromGradient, toGradient );
				}
			};

			tweenInstance = tmp.gameObject.AddTween( tween );
			handle.gradientTween = tweenInstance;
		};

		startTween( fromGradient, toGradient );
		return tweenInstance;
	}

	public static TweenInstance<Transform, float> AnimateTextGradient( TextMeshProUGUI tmp, VertexGradient toGradient, float duration, EaseType easeType = EaseType.Linear, bool loop = false, bool pingPong = false, int loopCount = -1 )
	{
		if ( tmp == null )
			return null;

		return AnimateTextGradient( tmp, tmp.colorGradient, toGradient, duration, easeType, loop, pingPong, loopCount );
	}
}
