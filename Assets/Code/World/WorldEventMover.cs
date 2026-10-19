using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Moves a transform by a local offset when a catalog world event fires.
/// Snaps open if the event already fired (save / session restore).
/// </summary>
[DisallowMultipleComponent]
public class WorldEventMover : MonoBehaviour
{
	[Tooltip( "Catalog world event id that activates this mover." )]
	[SerializeField]
	string _triggerEventId;

	[Tooltip( "Transform to move. Empty = this transform." )]
	[SerializeField]
	Transform _target;

	[Tooltip( "Local-space offset applied when activated (e.g. 0,3,0 to slide up)." )]
	[SerializeField]
	Vector3 _localOffset = new Vector3( 0f, 3f, 0f );

	[SerializeField]
	[Min( 0.01f )]
	float _duration = 1.2f;

	[SerializeField]
	AnimationCurve _curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );

	[SerializeField]
	Feedbacks _onActivate;

	Vector3 _closedLocalPosition;
	bool _hasClosedPose;
	bool _activated;
	bool _animating;
	float _animT;
	bool _subscribed;

	public string TriggerEventId => _triggerEventId;
	public bool IsActivated => _activated;

	void OnEnable()
	{
		CaptureClosedPoseIfNeeded();
		Subscribe();

		if ( ShouldRestoreActivated() )
		{
			SnapOpen();
			return;
		}

		ApplyClosed();
	}

	void OnDisable()
	{
		Unsubscribe();
		_animating = false;
	}

	void Update()
	{
		if ( !_animating )
			return;

		TickAnimation();
	}

	void CaptureClosedPoseIfNeeded()
	{
		if ( _hasClosedPose )
			return;

		Transform target = ResolveTarget();
		if ( target == null )
			return;

		_closedLocalPosition = target.localPosition;
		_hasClosedPose = true;
	}

	Transform ResolveTarget()
	{
		if ( _target != null )
			return _target;
		return transform;
	}

	bool ShouldRestoreActivated()
	{
		if ( string.IsNullOrEmpty( _triggerEventId ) )
			return false;

		WorldEventSystem worldEvents = WorldEventSystem.Instance;
		if ( worldEvents != null )
			return worldEvents.HasEventFired( _triggerEventId );

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		if ( save == null )
			return false;

		save.EnsureWorldEventProgress();
		return save.firedWorldEventIds != null && save.firedWorldEventIds.Contains( _triggerEventId );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<WorldEventFiredEvent>( OnWorldEventFired );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<WorldEventFiredEvent>( OnWorldEventFired );
		_subscribed = false;
	}

	void OnWorldEventFired( WorldEventFiredEvent evt )
	{
		if ( _activated )
			return;
		if ( string.IsNullOrEmpty( _triggerEventId ) || evt.Id != _triggerEventId )
			return;

		Activate();
	}

	/// <summary>Begin the open animation. No-op if already activated.</summary>
	public void Activate()
	{
		if ( _activated )
			return;

		CaptureClosedPoseIfNeeded();
		_activated = true;
		_animating = true;
		_animT = 0f;

		if ( _onActivate != null )
			_onActivate.Play();

		if ( _duration <= 0.01f )
			SnapOpen();
	}

	void TickAnimation()
	{
		Transform target = ResolveTarget();
		if ( target == null )
		{
			_animating = false;
			return;
		}

		_animT += Time.deltaTime;
		float duration = Mathf.Max( 0.01f, _duration );
		float normalized = Mathf.Clamp01( _animT / duration );
		float eased = _curve != null ? _curve.Evaluate( normalized ) : normalized;
		target.localPosition = Vector3.LerpUnclamped( _closedLocalPosition, _closedLocalPosition + _localOffset, eased );

		if ( normalized < 1f )
			return;

		target.localPosition = _closedLocalPosition + _localOffset;
		_animating = false;
	}

	void SnapOpen()
	{
		CaptureClosedPoseIfNeeded();
		_activated = true;
		_animating = false;

		Transform target = ResolveTarget();
		if ( target != null )
			target.localPosition = _closedLocalPosition + _localOffset;
	}

	void ApplyClosed()
	{
		_activated = false;
		_animating = false;

		Transform target = ResolveTarget();
		if ( target != null && _hasClosedPose )
			target.localPosition = _closedLocalPosition;
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		_duration = Mathf.Max( 0.01f, _duration );
		if ( _curve == null || _curve.length == 0 )
			_curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
	}

	void OnDrawGizmosSelected()
	{
		Transform target = ResolveTarget();
		if ( target == null )
			return;

		Vector3 closedWorld = target.parent != null
			? target.parent.TransformPoint( _hasClosedPose ? _closedLocalPosition : target.localPosition )
			: ( _hasClosedPose ? _closedLocalPosition : target.localPosition );
		Vector3 openLocal = ( _hasClosedPose ? _closedLocalPosition : target.localPosition ) + _localOffset;
		Vector3 openWorld = target.parent != null ? target.parent.TransformPoint( openLocal ) : openLocal;

		Gizmos.color = new Color( 0.4f, 0.9f, 1f, 0.9f );
		Gizmos.DrawWireSphere( closedWorld, 0.15f );
		Gizmos.color = new Color( 0.3f, 1f, 0.5f, 0.9f );
		Gizmos.DrawWireSphere( openWorld, 0.15f );
		Gizmos.color = new Color( 1f, 0.85f, 0.3f, 0.85f );
		Gizmos.DrawLine( closedWorld, openWorld );
	}
#endif
}
