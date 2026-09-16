using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Unlock / reward toast source. Visuals live on the shared <see cref="ToastStackUI"/> list
/// (same origin as discovery toasts).
/// </summary>
public class UnlockRewardToastUI : MonoBehaviour
{
	const float HoldSeconds = 2.5f;

	struct ToastEntry
	{
		public string Message;
		public Sprite Icon;
	}

	public static UnlockRewardToastUI Instance { get; private set; }

	public bool IsBusy
	{
		get
		{
			if ( s_pendingBeforeInstance.Count > 0 )
				return true;
			ToastStackUI stack = ToastStackUI.Instance;
			return stack != null && stack.HasToast( ToastStackUI.Kind.Unlock );
		}
	}

	static readonly Queue<ToastEntry> s_pendingBeforeInstance = new Queue<ToastEntry>( 8 );

	[SerializeField] Text label;
	[SerializeField] Feedbacks showFeedback;
	[SerializeField] Feedbacks hideFeedback;
	[SerializeField] float holdDuration = HoldSeconds;

	bool _ready;
	bool _subscribed;

	void Awake()
	{
		Instance = this;
		Setup();
	}

	public void Setup()
	{
		Instance = this;
		if ( label == null )
		{
			Transform existingLabel = transform.Find( "Label" );
			if ( existingLabel != null )
				label = existingLabel.GetComponent<Text>();
		}

		ToastStackUI stack = ToastStackUI.EnsureOnCanvas( transform );
		if ( stack != null && label != null && label.font != null )
			stack.SetLabelFont( label.font );
		BindStackFeedbacks( stack );
		HideLegacyVisual();
		Subscribe();
		_ready = true;
		FlushPending();
	}

	void OnEnable()
	{
		Instance = this;
		if ( !_ready )
			Setup();
		else
		{
			Subscribe();
			FlushPending();
		}
	}

	void OnDestroy()
	{
		Unsubscribe();
		if ( Instance == this )
			Instance = null;
	}

	public static void NotifyUnlock( AbilityDefinition definition )
	{
		if ( definition == null )
			return;

		string name = definition.ResolveDisplayName();
		EnqueueStatic( new ToastEntry
		{
			Message = "Unlocked: " + name,
			Icon = definition.icon
		} );
	}

	public static void NotifyMessage( string message, Sprite icon = null )
	{
		EnqueueStatic( new ToastEntry
		{
			Message = message,
			Icon = icon
		} );
	}

	static void EnqueueStatic( ToastEntry entry )
	{
		if ( string.IsNullOrEmpty( entry.Message ) )
			return;

		UnlockRewardToastUI ui = Instance;
		if ( ui != null && ui._ready )
		{
			ui.Enqueue( entry );
			return;
		}

		s_pendingBeforeInstance.Enqueue( entry );
	}

	void FlushPending()
	{
		while ( s_pendingBeforeInstance.Count > 0 )
			Enqueue( s_pendingBeforeInstance.Dequeue() );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<AbilityUnlockedEvent>( OnAbilityUnlocked );
		_subscribed = false;
	}

	void OnAbilityUnlocked( AbilityUnlockedEvent evt )
	{
		if ( evt.Definition != null )
			NotifyUnlock( evt.Definition );
		else if ( !string.IsNullOrEmpty( evt.AbilityId ) )
			NotifyMessage( "Unlocked: " + evt.AbilityId );
	}

	void Enqueue( ToastEntry entry )
	{
		if ( !_ready )
			Setup();

		if ( string.IsNullOrEmpty( entry.Message ) )
			return;

		float hold = holdDuration > 0.1f ? holdDuration : HoldSeconds;
		ToastStackUI.NotifyUnlock( entry.Message, entry.Icon, hold );
	}

	void HideLegacyVisual()
	{
		Image backdrop = GetComponent<Image>();
		if ( backdrop != null )
			backdrop.enabled = false;

		CanvasGroup group = GetComponent<CanvasGroup>();
		if ( group != null )
		{
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}

		HideChild( "Label" );
		HideChild( "RewardIcon" );
	}

	void BindStackFeedbacks( ToastStackUI stack )
	{
		if ( stack == null )
			return;

		if ( showFeedback == null )
		{
			Transform existing = transform.Find( "ShowFeedbacks" );
			if ( existing != null )
				showFeedback = existing.GetComponent<Feedbacks>();
		}

		if ( hideFeedback == null )
		{
			Transform existing = transform.Find( "HideFeedbacks" );
			if ( existing != null )
				hideFeedback = existing.GetComponent<Feedbacks>();
		}

		stack.RegisterKind( ToastStackUI.Kind.Unlock, showFeedback, hideFeedback, GetComponent<CanvasGroup>(), transform as RectTransform );
	}

	void HideChild( string childName )
	{
		Transform existing = transform.Find( childName );
		if ( existing != null )
			existing.gameObject.SetActive( false );
	}
}
