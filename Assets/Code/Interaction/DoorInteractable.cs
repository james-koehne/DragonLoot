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

	[SerializeField]
	DoorState state = DoorState.Locked;

	Feedbacks _activeTransitionFeedback;

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
		RefreshInteractionName();
	}

	void OnEnable()
	{
		EventBus.Subscribe<QuestProgressChangedEvent>( OnQuestProgress );
		EventBus.Subscribe<DoorUnlockedEvent>( OnDoorUnlocked );
		RestoreFromSave();
		EvaluateUnlockState();
		RefreshInteractionName();
	}

	void OnDisable()
	{
		EventBus.Unsubscribe<QuestProgressChangedEvent>( OnQuestProgress );
		EventBus.Unsubscribe<DoorUnlockedEvent>( OnDoorUnlocked );
		StopActiveTransitionFeedback();
		StopLockedFeedback();
	}

	void Update()
	{
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
		if ( !base.CanInteract( player ) || player == null || IsTransitioning )
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
		DoorInteractable[] doors = UnityEngine.Object.FindObjectsByType<DoorInteractable>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
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

	void OnQuestProgress( QuestProgressChangedEvent evt )
	{
		EvaluateUnlockState();
	}

	void OnDoorUnlocked( DoorUnlockedEvent evt )
	{
		if ( !MatchesUnlockEvent( evt.DoorId ) )
			return;

		UnlockFromEvent();
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
			RefreshInteractionName();
			return;
		}

		if ( state == DoorState.Locked )
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
		}

		RefreshInteractionName();
	}

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

		if ( !QuestSystem.Enabled )
			return true;

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		if ( save == null || save.completedQuestIds == null )
			return false;

		return save.completedQuestIds.Contains( unlockQuestId );
	}

	void BeginOpen()
	{
		state = DoorState.Opening;
		RefreshInteractionName();
		StopLockedFeedback();
		StopActiveTransitionFeedback();
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
