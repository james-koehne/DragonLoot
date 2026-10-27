using System;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Squash-then-settle scale punch when an artifact plants on the floor after place/throw.
/// Prefer configuring this on a <see cref="Feedbacks"/> chain; use <see cref="PlayOn"/> from code.
/// </summary>
[Serializable]
[FeedbackInfo( "Treasure/Artifact Plant" )]
public class ArtifactPlantFeedback : Feedback, IFeedbackTick
{
	static readonly Vector3 DefaultSquash = new Vector3( 0.22f, -0.32f, 0.22f );
	static readonly Vector3 DefaultStretch = new Vector3( -0.06f, 0.12f, -0.06f );

	public Transform Target;

	[Tooltip( "Peak scale offset at impact (positive XZ / negative Y reads as a squat plant)." )]
	public Vector3 Squash = DefaultSquash;

	[Tooltip( "Brief stretch overshoot after the squash before returning to base scale." )]
	public Vector3 Stretch = DefaultStretch;

	[Min( 0.05f )]
	public float Duration = 0.28f;

	[Range( 0.1f, 0.7f )]
	public float SquashFraction = 0.38f;

	Vector3 _baseScale;
	float _elapsed;
	bool _running;

	public static bool IsArtifact( TreasureItem item )
	{
		return item != null
			&& item.Definition != null
			&& item.Definition.category == TreasureCategory.Artifact;
	}

	/// <summary>
	/// Ensures a Feedbacks chain on the item and plays an Artifact Plant scale punch.
	/// Large items use a reduced squash so statues do not over-compress on plant.
	/// </summary>
	public static void PlayOn( TreasureItem item )
	{
		if ( item == null )
			return;

		Feedbacks feedbacks = EnsureFeedbacks( item );
		if ( feedbacks == null )
			return;

		float extent = -1f;
		if ( item.Definition != null )
			extent = TreasureDefinition.MaxExtentMeters( item.GetRendererWorldBounds() );
		float squashScale = item.Definition != null
			? item.Definition.ResolvePlantSquashScale( extent )
			: 1f;
		ApplySquashScale( feedbacks, squashScale );

		FeedbackContext context = new FeedbackContext();
		context.Source = item.gameObject;
		context.Target = item.gameObject;
		context.Position = item.transform.position;
		feedbacks.Play( context );
	}

	static void ApplySquashScale( Feedbacks feedbacks, float squashScale )
	{
		if ( feedbacks == null || feedbacks.FeedbackList == null )
			return;

		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			ArtifactPlantFeedback plant = feedbacks.FeedbackList[ i ] as ArtifactPlantFeedback;
			if ( plant == null )
				continue;

			plant.Squash = DefaultSquash * squashScale;
			plant.Stretch = DefaultStretch * squashScale;
		}
	}

	static Feedbacks EnsureFeedbacks( TreasureItem item )
	{
		Feedbacks feedbacks = item.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = item.gameObject.AddComponent<Feedbacks>();

		feedbacks.Initialize();

		if ( feedbacks.FeedbackList != null && feedbacks.FeedbackList.Count > 0 )
			return feedbacks;

		feedbacks.AddFeedback( new ArtifactPlantFeedback
		{
			Target = item.transform,
			Squash = DefaultSquash,
			Stretch = DefaultStretch
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

		if ( Duration <= 0.0001f )
		{
			target.localScale = _baseScale;
			_running = false;
			return false;
		}

		_elapsed += deltaTime;
		float t = Mathf.Clamp01( _elapsed / Duration );
		if ( t >= 1f )
		{
			target.localScale = _baseScale;
			_running = false;
			return false;
		}

		float squashEnd = Mathf.Clamp( SquashFraction, 0.1f, 0.7f );
		Vector3 offset;
		if ( t <= squashEnd )
		{
			float u = squashEnd > 0.0001f ? t / squashEnd : 1f;
			float ease = Mathf.Sin( u * Mathf.PI * 0.5f );
			offset = Squash * ease;
		}
		else
		{
			float u = ( t - squashEnd ) / Mathf.Max( 0.0001f, 1f - squashEnd );
			// Stretch peaks early in the recover phase, then eases to zero.
			float stretchWeight = Mathf.Sin( Mathf.Clamp01( u ) * Mathf.PI );
			float recover = u * u * ( 3f - 2f * u );
			offset = Vector3.Lerp( Squash, Vector3.zero, recover ) + Stretch * stretchWeight * ( 1f - recover );
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
