using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Scale punch for coins and gems: a pop on pickup (into the hand) and a squash on place.
/// Prefer configuring this on a <see cref="Feedbacks"/> chain; use <see cref="PlayPickup"/> /
/// <see cref="PlayPlace"/> from code.
/// </summary>
[Serializable]
[FeedbackInfo( "Treasure/Coin Gem Interact" )]
public class CoinGemInteractFeedback : Feedback, IFeedbackTick
{
	public enum InteractKind
	{
		Pickup,
		Place
	}

	public Transform Target;
	public InteractKind Kind = InteractKind.Place;

	[Tooltip( "Uniform scale pop when the item settles into the hand." )]
	public Vector3 PickupPunch = new Vector3( 0.1f, 0.1f, 0.1f );

	[Min( 0.05f )]
	public float PickupDuration = 0.16f;

	[Tooltip( "Peak scale offset at place impact (positive XZ / negative Y reads as a squat)." )]
	public Vector3 PlaceSquash = new Vector3( 0.14f, -0.2f, 0.14f );

	[Tooltip( "Brief stretch overshoot after the squash before returning to base scale." )]
	public Vector3 PlaceStretch = new Vector3( -0.04f, 0.08f, -0.04f );

	[Min( 0.05f )]
	public float PlaceDuration = 0.2f;

	[Range( 0.1f, 0.7f )]
	public float PlaceSquashFraction = 0.38f;

	Vector3 _baseScale;
	float _elapsed;
	bool _running;
	InteractKind _playingKind;

	public static bool IsCoinOrGem( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		TreasureCategory category = item.Definition.category;
		return category == TreasureCategory.Coin || category == TreasureCategory.Gem;
	}

	public static void PlayPickup( TreasureItem item )
	{
		Play( item, InteractKind.Pickup );
	}

	public static void PlayPlace( TreasureItem item )
	{
		Play( item, InteractKind.Place );
	}

	static void Play( TreasureItem item, InteractKind kind )
	{
		if ( !IsCoinOrGem( item ) )
			return;

		Feedbacks feedbacks = EnsureFeedbacks( item );
		if ( feedbacks == null )
			return;

		if ( feedbacks.FeedbackList != null )
		{
			for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
			{
				CoinGemInteractFeedback coinGem = feedbacks.FeedbackList[ i ] as CoinGemInteractFeedback;
				if ( coinGem == null )
					continue;

				coinGem.Kind = kind;
				if ( coinGem.Target == null )
					coinGem.Target = item.transform;
			}
		}

		FeedbackContext context = new FeedbackContext();
		context.Source = item.gameObject;
		context.Target = item.gameObject;
		context.Position = item.transform.position;
		feedbacks.Play( context );
	}

	static Feedbacks EnsureFeedbacks( TreasureItem item )
	{
		Feedbacks feedbacks = item.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = item.gameObject.AddComponent<Feedbacks>();

		feedbacks.Initialize();

		if ( feedbacks.FeedbackList != null && feedbacks.FeedbackList.Count > 0 )
			return feedbacks;

		feedbacks.AddFeedback( new CoinGemInteractFeedback
		{
			Target = item.transform
		} );
		return feedbacks;
	}

	public override void Play()
	{
		Transform target = ResolveTarget();
		if ( target == null )
			return;

		Cancel();
		_baseScale = target.localScale;
		_elapsed = 0f;
		_playingKind = Kind;
		_running = true;
		RegisterTick( this );
	}

	public override void Stop()
	{
		Cancel();
	}

	public override void Reset()
	{
		Cancel();
	}

	public bool Tick( float deltaTime )
	{
		Transform target = ResolveTarget();
		if ( !_running || target == null )
		{
			_running = false;
			return false;
		}

		if ( _playingKind == InteractKind.Pickup )
			return TickPickup( target, deltaTime );

		return TickPlace( target, deltaTime );
	}

	bool TickPickup( Transform target, float deltaTime )
	{
		if ( PickupDuration <= 0.0001f )
		{
			target.localScale = _baseScale;
			_running = false;
			return false;
		}

		_elapsed += deltaTime;
		float t = Mathf.Clamp01( _elapsed / PickupDuration );
		if ( t >= 1f )
		{
			target.localScale = _baseScale;
			_running = false;
			return false;
		}

		float curve = Mathf.Sin( t * Mathf.PI );
		target.localScale = _baseScale + PickupPunch * curve;
		return true;
	}

	bool TickPlace( Transform target, float deltaTime )
	{
		if ( PlaceDuration <= 0.0001f )
		{
			target.localScale = _baseScale;
			_running = false;
			return false;
		}

		_elapsed += deltaTime;
		float t = Mathf.Clamp01( _elapsed / PlaceDuration );
		if ( t >= 1f )
		{
			target.localScale = _baseScale;
			_running = false;
			return false;
		}

		float squashEnd = Mathf.Clamp( PlaceSquashFraction, 0.1f, 0.7f );
		Vector3 offset;
		if ( t <= squashEnd )
		{
			float u = squashEnd > 0.0001f ? t / squashEnd : 1f;
			float ease = Mathf.Sin( u * Mathf.PI * 0.5f );
			offset = PlaceSquash * ease;
		}
		else
		{
			float u = ( t - squashEnd ) / Mathf.Max( 0.0001f, 1f - squashEnd );
			float stretchWeight = Mathf.Sin( Mathf.Clamp01( u ) * Mathf.PI );
			float recover = u * u * ( 3f - 2f * u );
			offset = Vector3.Lerp( PlaceSquash, Vector3.zero, recover ) + PlaceStretch * stretchWeight * ( 1f - recover );
		}

		target.localScale = _baseScale + offset;
		return true;
	}

	Transform ResolveTarget()
	{
		if ( Target != null )
			return Target;

		if ( Context != null && Context.Target != null )
			return Context.Target.transform;

		if ( Owner != null )
			return Owner.transform;

		return null;
	}

	public void Cancel()
	{
		if ( _running )
		{
			Transform target = ResolveTarget();
			if ( target != null )
				target.localScale = _baseScale;
		}

		_running = false;
		UnregisterTick( this );
	}
}
