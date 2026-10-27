using System;

using FeedbackSystem;

using UnityEngine;

public enum DoorLeafLayout
{
	Single = 0,
	Double = 1
}

public enum DoorState
{
	Locked = 0,
	UnlockedClosed = 1,
	Opening = 2,
	Open = 3,
	Closing = 4
}

[Serializable]
public struct DoorLeaf
{
	public Transform hingePivot;
	public Vector3 closedLocalEuler;
	public Vector3 openLocalEuler;
}

/// <summary>
/// Quest/event-gated door with single or double leaf hinged rotation via FeedbackSystem chains.
/// Setup: DoorInteractable on the assembly root (no collider required). Put a collider on each leaf mesh
/// so raycasts resolve via GetComponentInParent. Child hinge pivot(s) + OnOpenFeedbacks/OnCloseFeedbacks/OnLockedFeedbacks
/// with RotateTransformFeedback per leaf (use ParallelFeedback for double doors).
/// </summary>
[DisallowMultipleComponent]
public class DoorInteractable : InteractableBase
{
	[Header( "Identity" )]
	[SerializeField]
	string doorId;

	[Header( "Unlock" )]
	[SerializeField]
	bool startsUnlocked;

	[SerializeField]
	[Tooltip( "Objective id that must be completed before this door unlocks (e.g. obj_workshop_access). While locked, interact still plays locked shake/SFX. Do not use ObjectiveUnlockGate on doors — that blocks interaction entirely." )]
	string unlockQuestId;

	[SerializeField]
	string unlockEventId;

	[Header( "Leaves" )]
	[SerializeField]
	DoorLeafLayout layout = DoorLeafLayout.Single;

	[SerializeField]
	DoorLeaf primaryLeaf;

	[SerializeField]
	DoorLeaf secondaryLeaf;

	[Header( "Feedbacks" )]
	[SerializeField]
	Feedbacks onOpenFeedback;

	[SerializeField]
	Feedbacks onCloseFeedback;

	[SerializeField]
	Feedbacks onLockedFeedback;

	[Header( "Unlock FX" )]
	[Tooltip( "Optional CompleteGlow / DisplayCompleteEffect roots (e.g. one per double-door leaf). On fresh unlock: activates (bright flash), then hides. Not shown when restoring an already-unlocked door." )]
	[SerializeField]
	GameObject[] unlockGlowRoots;

	[SerializeField]
	DoorState state = DoorState.Locked;

	Feedbacks _activeTransitionFeedback;
	bool _unlockGlowPlaying;
	float _unlockGlowHideAt;

	public string DoorId => doorId;
	public DoorState State => state;
	public DoorLeafLayout Layout => layout;
	public bool IsTransitioning => state == DoorState.Opening || state == DoorState.Closing;
	public bool IsLocked => state == DoorState.Locked;
	public bool IsOpen => state == DoorState.Open;
	public bool ShowsLockedPrompt => state == DoorState.Locked;

	void Awake()
	{
		EnsureLockedFeedback();
		SetUnlockGlowRootsActive( false );
		RefreshInteractionName();
	}

	void OnEnable()
	{
		EventBus.Subscribe<DoorUnlockedEvent>( OnDoorUnlocked );
		EventBus.Subscribe<ObjectiveCompletedEvent>( OnObjectiveCompleted );
		RestoreFromSave();
		EvaluateUnlockState();
		RefreshInteractionName();
	}

	void OnDisable()
	{
		EventBus.Unsubscribe<DoorUnlockedEvent>( OnDoorUnlocked );
		EventBus.Unsubscribe<ObjectiveCompletedEvent>( OnObjectiveCompleted );
		StopActiveTransitionFeedback();
		StopLockedFeedback();
		StopUnlockGlow( true );
	}

	void Update()
	{
		if ( _unlockGlowPlaying )
			TickUnlockGlowHide();

		if ( !IsTransitioning )
			return;

		if ( IsTransitionFeedbackPlaying( _activeTransitionFeedback ) )
			return;

		FinishTransition();
	}

	public bool CanOutlineFocus( PlayerController player )
	{
		if ( !IsAvailable || player == null || IsTransitioning )
			return false;

		return state == DoorState.Locked
			|| state == DoorState.UnlockedClosed
			|| state == DoorState.Open;
	}

	public override bool CanInteract( PlayerController player )
	{
		// Skip ObjectiveUnlockGate: doors stay interactable while Locked so locked shake/SFX can play.
		if ( !IsAvailable || player == null || IsTransitioning )
			return false;

		return state == DoorState.Locked || state == DoorState.UnlockedClosed || state == DoorState.Open;
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || !CanInteract( player ) )
			return;

		if ( state == DoorState.Locked )
			PlayLockedAttempt();
		else if ( state == DoorState.UnlockedClosed )
			BeginOpen();
		else if ( state == DoorState.Open )
			BeginClose();
	}

	public void UnlockFromEvent()
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return;

		DoorProgress.MarkEventUnlocked( doorId );
		EvaluateUnlockState();
	}

	public void RefreshUnlockState()
	{
		EvaluateUnlockState();
	}

	public static void RefreshAllUnlockState()
	{
		DoorInteractable[] doors = UnityEngine.Object.FindObjectsByType<DoorInteractable>( FindObjectsInactive.Exclude );
		if ( doors == null )
			return;

		for ( int i = 0; i < doors.Length; i++ )
		{
			DoorInteractable door = doors[ i ];
			if ( door == null )
				continue;
			door.RefreshUnlockState();
		}
	}

	void OnDoorUnlocked( DoorUnlockedEvent evt )
	{
		if ( !MatchesUnlockEvent( evt.DoorId ) )
			return;

		UnlockFromEvent();
	}

	void OnObjectiveCompleted( ObjectiveCompletedEvent evt )
	{
		if ( string.IsNullOrEmpty( unlockQuestId ) )
			return;
		if ( evt.ObjectiveId != unlockQuestId )
			return;

		EvaluateUnlockState();
	}

	bool MatchesUnlockEvent( string eventDoorId )
	{
		if ( string.IsNullOrEmpty( eventDoorId ) )
			return false;

		string listenId = ResolveUnlockEventId();
		return eventDoorId == listenId || eventDoorId == doorId;
	}

	string ResolveUnlockEventId()
	{
		if ( !string.IsNullOrEmpty( unlockEventId ) )
			return unlockEventId;
		return doorId;
	}

	void RestoreFromSave()
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return;

		bool unlocked = IsPermanentlyUnlocked();
		if ( !unlocked )
		{
			state = DoorState.Locked;
			ApplyClosedPose();
			return;
		}

		if ( DoorProgress.IsOpen( doorId ) )
		{
			state = DoorState.Open;
			ApplyOpenPose();
			return;
		}

		state = DoorState.UnlockedClosed;
		ApplyClosedPose();
	}

	void EvaluateUnlockState()
	{
		if ( IsTransitioning )
			return;

		if ( !IsPermanentlyUnlocked() )
		{
			if ( state != DoorState.Locked )
			{
				state = DoorState.Locked;
				ApplyClosedPose();
			}
			StopUnlockGlow( true );
			RefreshInteractionName();
			return;
		}

		bool freshlyUnlocked = state == DoorState.Locked;
		if ( freshlyUnlocked )
		{
			if ( !string.IsNullOrEmpty( doorId ) && DoorProgress.IsOpen( doorId ) )
			{
				state = DoorState.Open;
				ApplyOpenPose();
			}
			else
			{
				state = DoorState.UnlockedClosed;
				ApplyClosedPose();
			}

			PlayUnlockGlowBurst();
		}

		RefreshInteractionName();
	}

	void PlayUnlockGlowBurst()
	{
		if ( !Application.isPlaying || !HasUnlockGlowRoots() )
			return;

		StopUnlockGlow( true );
		SetUnlockGlowRootsActive( true );

		_unlockGlowPlaying = true;
		// Safety timeout if a volume never reports fade complete.
		_unlockGlowHideAt = Time.time + ResolveUnlockGlowDuration() + 0.35f;
	}

	void TickUnlockGlowHide()
	{
		if ( AnyUnlockGlowStillFading() && Time.time < _unlockGlowHideAt )
			return;

		StopUnlockGlow( false );
	}

	bool AnyUnlockGlowStillFading()
	{
		if ( unlockGlowRoots == null )
			return false;

		for ( int i = 0; i < unlockGlowRoots.Length; i++ )
		{
			GameObject root = unlockGlowRoots[ i ];
			if ( root == null || !root.activeInHierarchy )
				continue;

			CompleteGlowVolume volume = root.GetComponentInChildren<CompleteGlowVolume>( true );
			if ( volume != null && volume.IsRevealFadePlaying )
				return true;
		}

		return false;
	}

	float ResolveUnlockGlowDuration()
	{
		const float FallbackDuration = 1.75f;
		float longest = 0f;
		if ( unlockGlowRoots == null )
			return FallbackDuration;

		for ( int i = 0; i < unlockGlowRoots.Length; i++ )
		{
			GameObject root = unlockGlowRoots[ i ];
			if ( root == null )
				continue;

			DisplayCompleteEffect effect = root.GetComponent<DisplayCompleteEffect>();
			if ( effect != null )
			{
				longest = Mathf.Max( longest, effect.ResolveShowDuration() );
				continue;
			}

			CompleteGlowVolume volume = root.GetComponentInChildren<CompleteGlowVolume>( true );
			if ( volume == null || volume.Settings == null )
				continue;

			longest = Mathf.Max( longest, Mathf.Max( 0.05f, volume.Settings.FlashDuration ) + 0.15f );
		}

		if ( longest <= 0f )
			return FallbackDuration;
		return longest;
	}

	void StopUnlockGlow( bool cancelFlash )
	{
		_unlockGlowPlaying = false;
		_unlockGlowHideAt = 0f;

		if ( unlockGlowRoots == null )
			return;

		if ( cancelFlash )
		{
			for ( int i = 0; i < unlockGlowRoots.Length; i++ )
			{
				GameObject root = unlockGlowRoots[ i ];
				if ( root == null )
					continue;

				CompleteGlowVolume volume = root.GetComponentInChildren<CompleteGlowVolume>( true );
				if ( volume == null )
					continue;

				volume.CancelExposureFlash();
				volume.CancelRevealFadeOut( false );
			}
		}

		SetUnlockGlowRootsActive( false );
	}

	bool HasUnlockGlowRoots()
	{
		if ( unlockGlowRoots == null )
			return false;

		for ( int i = 0; i < unlockGlowRoots.Length; i++ )
		{
			if ( unlockGlowRoots[ i ] != null )
				return true;
		}

		return false;
	}

	void SetUnlockGlowRootsActive( bool active )
	{
		if ( unlockGlowRoots == null )
			return;

		for ( int i = 0; i < unlockGlowRoots.Length; i++ )
		{
			GameObject root = unlockGlowRoots[ i ];
			if ( root == null || root.activeSelf == active )
				continue;
			root.SetActive( active );
		}
	}

#if UNITY_EDITOR
	public void EditorAssignUnlockGlowRoots( GameObject[] roots )
	{
		unlockGlowRoots = roots;
	}
#endif

	bool IsPermanentlyUnlocked()
	{
		if ( DoorProgress.UnlockAllDoors )
			return true;

		if ( startsUnlocked )
			return true;

		if ( IsQuestUnlocked() )
			return true;

		if ( !string.IsNullOrEmpty( doorId ) && DoorProgress.IsEventUnlocked( doorId ) )
			return true;

		return false;
	}

	bool IsQuestUnlocked()
	{
		if ( string.IsNullOrEmpty( unlockQuestId ) )
			return false;

		ObjectiveSystem objectives = ObjectiveSystem.Instance;
		return objectives != null && objectives.IsCompleted( unlockQuestId );
	}

	void BeginOpen()
	{
		state = DoorState.Opening;
		RefreshInteractionName();
		StopLockedFeedback();
		StopActiveTransitionFeedback();
		if ( !string.IsNullOrEmpty( doorId ) )
			EventBus.Publish( new DoorOpenedEvent { DoorId = doorId } );
		PlayTransitionFeedback( onOpenFeedback, FinishOpenInstant );
	}

	void BeginClose()
	{
		state = DoorState.Closing;
		RefreshInteractionName();
		StopLockedFeedback();
		StopActiveTransitionFeedback();
		PlayTransitionFeedback( onCloseFeedback, FinishCloseInstant );
	}

	void PlayLockedAttempt()
	{
		EnsureLockedFeedback();
		if ( onLockedFeedback == null )
			return;

		if ( IsTransitionFeedbackPlaying( onLockedFeedback ) )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = transform.position;
		onLockedFeedback.Play( context );
	}

	void EnsureLockedFeedback()
	{
		if ( onLockedFeedback != null )
		{
			onLockedFeedback.Initialize();
			if ( onLockedFeedback.FeedbackList != null && onLockedFeedback.FeedbackList.Count > 0 )
				return;
		}

		Transform existing = transform.Find( "LockedFailFeedback" );
		GameObject host = existing != null ? existing.gameObject : new GameObject( "LockedFailFeedback" );
		if ( existing == null )
			host.transform.SetParent( transform, false );

		onLockedFeedback = host.GetComponent<Feedbacks>();
		if ( onLockedFeedback == null )
			onLockedFeedback = host.AddComponent<Feedbacks>();

		onLockedFeedback.Initialize();
		if ( onLockedFeedback.FeedbackList != null && onLockedFeedback.FeedbackList.Count > 0 )
			return;

		ParallelFeedback parallel = new ParallelFeedback();
		if ( primaryLeaf.hingePivot != null )
		{
			parallel.Feedbacks.Add( new ShakeTransformFeedback
			{
				Target = primaryLeaf.hingePivot,
				Duration = 0.22f,
				Strength = 0.035f
			} );
		}

		if ( layout == DoorLeafLayout.Double && secondaryLeaf.hingePivot != null )
		{
			parallel.Feedbacks.Add( new ShakeTransformFeedback
			{
				Target = secondaryLeaf.hingePivot,
				Duration = 0.22f,
				Strength = 0.035f
			} );
		}

		onLockedFeedback.AddFeedback( parallel );
	}

	void StopLockedFeedback()
	{
		if ( onLockedFeedback == null )
			return;

		onLockedFeedback.Stop();
	}

	void PlayTransitionFeedback( Feedbacks feedback, Action instantFallback )
	{
		if ( feedback == null )
		{
			instantFallback();
			return;
		}

		_activeTransitionFeedback = feedback;
		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = transform.position;
		feedback.Play( context );

		if ( !IsTransitionFeedbackPlaying( feedback ) )
			instantFallback();
	}

	static bool IsTransitionFeedbackPlaying( Feedbacks feedback )
	{
		if ( feedback == null )
			return false;

		if ( feedback.IsPlaying )
			return true;

		FeedbackTicker ticker = feedback.Ticker;
		return ticker != null && ticker.HasActive;
	}

	void FinishTransition()
	{
		if ( state == DoorState.Opening )
			FinishOpenInstant();
		else if ( state == DoorState.Closing )
			FinishCloseInstant();

		_activeTransitionFeedback = null;
	}

	void FinishOpenInstant()
	{
		state = DoorState.Open;
		ApplyOpenPose();

		if ( !string.IsNullOrEmpty( doorId ) )
			DoorProgress.MarkOpen( doorId );

		RefreshInteractionName();
	}

	void FinishCloseInstant()
	{
		state = DoorState.UnlockedClosed;
		ApplyClosedPose();

		if ( !string.IsNullOrEmpty( doorId ) )
			DoorProgress.MarkClosed( doorId );

		RefreshInteractionName();
	}

	void StopActiveTransitionFeedback()
	{
		if ( _activeTransitionFeedback == null )
			return;

		_activeTransitionFeedback.Stop();
		_activeTransitionFeedback = null;
	}

	void ApplyOpenPose()
	{
		ApplyLeafPose( primaryLeaf, open: true );
		if ( layout == DoorLeafLayout.Double )
			ApplyLeafPose( secondaryLeaf, open: true );
	}

	void ApplyClosedPose()
	{
		ApplyLeafPose( primaryLeaf, open: false );
		if ( layout == DoorLeafLayout.Double )
			ApplyLeafPose( secondaryLeaf, open: false );
	}

	static void ApplyLeafPose( DoorLeaf leaf, bool open )
	{
		if ( leaf.hingePivot == null )
			return;

		Vector3 euler = open ? leaf.openLocalEuler : leaf.closedLocalEuler;
		leaf.hingePivot.localRotation = Quaternion.Euler( euler );
	}

	void RefreshInteractionName()
	{
		switch ( state )
		{
			case DoorState.Locked:
				SetInteractionName( "Locked" );
				break;
			case DoorState.UnlockedClosed:
				SetInteractionName( "Open Door" );
				break;
			case DoorState.Open:
				SetInteractionName( "Close Door" );
				break;
			default:
				break;
		}
	}
}
