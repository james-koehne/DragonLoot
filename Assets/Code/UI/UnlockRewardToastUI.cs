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
	struct ToastEntry
	{
		public string Message;
		public Sprite Icon;
		public ToastStackUI.ToastTier Tier;
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
	[SerializeField] Feedbacks milestoneShowFeedback;
	[SerializeField] Feedbacks milestoneHideFeedback;
	[SerializeField] float holdDuration;

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
			Icon = definition.icon,
			Tier = ToastStackUI.ToastTier.Unlock
		} );
	}

	public static void NotifyMessage( string message, Sprite icon = null, ToastStackUI.ToastTier tier = ToastStackUI.ToastTier.Unlock )
	{
		EnqueueStatic( new ToastEntry
		{
			Message = message,
			Icon = icon,
			Tier = tier
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

		ToastStackUI.NotifyUnlock( entry.Message, entry.Icon, 0f, entry.Tier );
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

		ResolveFeedbackRef( ref showFeedback, "ShowFeedbacks" );
		ResolveFeedbackRef( ref hideFeedback, "HideFeedbacks" );
		ResolveFeedbackRef( ref milestoneShowFeedback, "MilestoneShowFeedbacks" );
		ResolveFeedbackRef( ref milestoneHideFeedback, "MilestoneHideFeedbacks" );

		CanvasGroup group = GetComponent<CanvasGroup>();
		RectTransform rect = transform as RectTransform;
		Image accent = FindChildImage( "Accent" );
		Image icon = FindChildImage( "RewardIcon" );
		Image backdrop = GetComponent<Image>();

		stack.RegisterTier( ToastStackUI.ToastTier.Unlock, showFeedback, hideFeedback, group, rect, accent, icon, backdrop );
		stack.RegisterTier( ToastStackUI.ToastTier.Milestone, milestoneShowFeedback, milestoneHideFeedback, group, rect, accent, icon, backdrop );
	}

	void ResolveFeedbackRef( ref Feedbacks field, string childName )
	{
		if ( field != null )
			return;

		Transform existing = transform.Find( childName );
		if ( existing != null )
			field = existing.GetComponent<Feedbacks>();
	}

	Image FindChildImage( string childName )
	{
		Transform existing = transform.Find( childName );
		if ( existing == null )
			return null;
		return existing.GetComponent<Image>();
	}

	void HideChild( string childName )
	{
		Transform existing = transform.Find( childName );
		if ( existing != null )
			existing.gameObject.SetActive( false );
	}
}
