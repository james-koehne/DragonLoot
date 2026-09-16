using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Discovery / completion toast source. Visuals live on the shared <see cref="ToastStackUI"/> list.
/// Self-initializes on Awake. Queues messages that arrive before the Interface toast exists.
/// </summary>
public class DiscoveryToastUI : MonoBehaviour
{
	const float HoldSeconds = 2.5f;

	struct ToastEntry
	{
		public string Message;
		public CarryBucketKind Pouch;
		public bool ShowPouchIcon;
	}

	public static DiscoveryToastUI Instance { get; private set; }

	/// <summary>True while a discovery toast is visible or more are queued.</summary>
	public bool IsBusy
	{
		get
		{
			if ( s_pendingBeforeInstance.Count > 0 )
				return true;
			ToastStackUI stack = ToastStackUI.Instance;
			return stack != null && stack.HasToast( ToastStackUI.Kind.Discovery );
		}
	}

	static readonly Queue<ToastEntry> s_pendingBeforeInstance = new Queue<ToastEntry>( 8 );

	[SerializeField] Text label;
	[SerializeField] Sprite coinPouchSprite;
	[SerializeField] Sprite gemPouchSprite;
	[SerializeField] Sprite artifactPouchSprite;
	[Tooltip( "Pouch icon used for general and junk discoveries when no dedicated sprite is set on the pouch bar." )]
	[SerializeField] Sprite generalPouchSprite;
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

	/// <summary>Direct entry from carry — works even if this UI has not spawned yet (queued).</summary>
	public static void NotifyNewTreasure( TreasureDefinition treasure )
	{
		if ( treasure == null )
			return;

		EnqueueStatic( new ToastEntry
		{
			Message = "New Discovery: " + ResolveName( treasure ),
			Pouch = PlayerCarry.ResolveBucket( treasure ),
			ShowPouchIcon = true
		} );
	}

	public static void NotifyMessage( string message )
	{
		EnqueueStatic( new ToastEntry
		{
			Message = message,
			ShowPouchIcon = false
		} );
	}

	static void EnqueueStatic( ToastEntry entry )
	{
		if ( string.IsNullOrEmpty( entry.Message ) )
			return;

		DiscoveryToastUI ui = Instance;
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

		EventBus.Subscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Subscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactDisplayCompleted );
		EventBus.Subscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<CoinDisplayTableCompletedEvent>( OnCoinDisplayCompleted );
		EventBus.Unsubscribe<ArtifactPresentationTableCompletedEvent>( OnArtifactDisplayCompleted );
		EventBus.Unsubscribe<GemConstellationCompletedEvent>( OnConstellationCompleted );
		_subscribed = false;
	}

	void OnCoinDisplayCompleted( CoinDisplayTableCompletedEvent evt )
	{
		string name = ResolveName( evt.AcceptedCoin );
		if ( string.IsNullOrEmpty( name ) && evt.Table != null )
			name = evt.Table.InteractionName;
		if ( string.IsNullOrEmpty( name ) )
			name = "Coins";
		Enqueue( MessageOnly( "Display Complete: " + name ) );
	}

	void OnArtifactDisplayCompleted( ArtifactPresentationTableCompletedEvent evt )
	{
		Enqueue( MessageOnly( "Display Complete: Artifacts" ) );
	}

	void OnConstellationCompleted( GemConstellationCompletedEvent evt )
	{
		if ( !evt.FromPlayer )
			return;

		string name = "Constellation";
		if ( evt.Constellation != null )
		{
			TreasureDefinition gem = evt.Constellation.DefaultAcceptedGem;
			if ( gem != null && !string.IsNullOrEmpty( gem.displayName ) )
				name = gem.displayName;
			else if ( !string.IsNullOrEmpty( evt.Constellation.InteractionName ) )
				name = evt.Constellation.InteractionName;
		}

		Enqueue( MessageOnly( "Constellation Complete: " + name ) );
	}

	static ToastEntry MessageOnly( string message )
	{
		return new ToastEntry
		{
			Message = message,
			ShowPouchIcon = false
		};
	}

	static string ResolveName( TreasureDefinition definition )
	{
		if ( definition == null )
			return string.Empty;
		if ( !string.IsNullOrEmpty( definition.displayName ) )
			return definition.displayName;
		if ( !string.IsNullOrEmpty( definition.id ) )
			return definition.id;
		return definition.name;
	}

	void Enqueue( ToastEntry entry )
	{
		if ( !_ready )
			Setup();

		if ( string.IsNullOrEmpty( entry.Message ) )
			return;

		float hold = holdDuration > 0.1f ? holdDuration : HoldSeconds;
		ToastStackUI.NotifyDiscovery( entry.Message, entry.Pouch, entry.ShowPouchIcon, hold );
	}

	public Sprite ResolvePouchSprite( CarryBucketKind pouch )
	{
		Sprite assigned = SpriteForPouch( pouch );
		if ( assigned != null )
			return assigned;

		PouchBarUI bar = PouchBarUI.Instance;
		if ( bar != null )
			return bar.GetSlotIcon( pouch );

		return null;
	}

	Sprite SpriteForPouch( CarryBucketKind pouch )
	{
		switch ( pouch )
		{
			case CarryBucketKind.Gem:
				return gemPouchSprite;
			case CarryBucketKind.Artifact:
				return artifactPouchSprite;
			case CarryBucketKind.General:
			case CarryBucketKind.Junk:
				return generalPouchSprite;
			default:
				return coinPouchSprite;
		}
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
		HideChild( "PouchIcon" );
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

		stack.RegisterKind( ToastStackUI.Kind.Discovery, showFeedback, hideFeedback, GetComponent<CanvasGroup>(), transform as RectTransform );
	}

	void HideChild( string childName )
	{
		Transform existing = transform.Find( childName );
		if ( existing != null )
			existing.gameObject.SetActive( false );
	}
}
