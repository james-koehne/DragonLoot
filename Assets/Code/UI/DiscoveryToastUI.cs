using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-center discovery / completion toast. Non-blocking; FeedbackSystem show/hide juice.
/// Self-initializes on Awake. Queues messages that arrive before the Interface toast exists.
/// </summary>
public class DiscoveryToastUI : MonoBehaviour
{
	const float HoldSeconds = 2.5f;
	const float TopInset = 72f;
	const float ToastWidth = 720f;
	const float ToastHeight = 64f;
	const float IconSize = 40f;
	const float IconPadding = 12f;

	struct ToastEntry
	{
		public string Message;
		public CarryBucketKind Pouch;
		public bool ShowPouchIcon;
	}

	public static DiscoveryToastUI Instance { get; private set; }

	/// <summary>True while a toast is visible or more are queued.</summary>
	public bool IsBusy => _visible || _routine != null || _queue.Count > 0;

	static readonly Queue<ToastEntry> s_pendingBeforeInstance = new Queue<ToastEntry>( 8 );

	[SerializeField] CanvasGroup group;
	[SerializeField] Text label;
	[SerializeField] Image pouchIcon;
	[SerializeField] Sprite coinPouchSprite;
	[SerializeField] Sprite gemPouchSprite;
	[SerializeField] Sprite artifactPouchSprite;
	[SerializeField] Sprite generalPouchSprite;
	[SerializeField] Feedbacks showFeedback;
	[SerializeField] Feedbacks hideFeedback;
	[SerializeField] float holdDuration = HoldSeconds;

	readonly Queue<ToastEntry> _queue = new Queue<ToastEntry>( 4 );
	bool _ready;
	bool _subscribed;
	bool _visible;
	Coroutine _routine;
	Vector3 _restScale = Vector3.one;
	Vector2 _restAnchored;
	bool _restAnchoredCaptured;

	void Awake()
	{
		Instance = this;
		Setup();
	}

	public void Setup()
	{
		Instance = this;
		EnsureUi();
		EnsureFeedbacks();
		CaptureRest();
		if ( !_visible )
			HideImmediate();
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

	void OnDisable()
	{
		if ( _routine != null )
		{
			StopCoroutine( _routine );
			_routine = null;
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

		if ( !isActiveAndEnabled )
			gameObject.SetActive( true );

		if ( !isActiveAndEnabled )
		{
			s_pendingBeforeInstance.Enqueue( entry );
			return;
		}

		_queue.Enqueue( entry );
		if ( _routine == null )
			_routine = StartCoroutine( ToastRoutine() );
	}

	IEnumerator ToastRoutine()
	{
		while ( _queue.Count > 0 )
		{
			ToastEntry entry = _queue.Dequeue();
			Show( entry );

			float hold = holdDuration > 0.1f ? holdDuration : HoldSeconds;
			float elapsed = 0f;
			while ( elapsed < hold )
			{
				elapsed += Time.unscaledDeltaTime;
				yield return null;
			}

			Hide();
			float hideDur = ResolveFeedbackDuration( hideFeedback, 0.25f );
			elapsed = 0f;
			while ( elapsed < hideDur )
			{
				elapsed += Time.unscaledDeltaTime;
				yield return null;
			}

			HideImmediate();
		}

		_routine = null;
	}

	void Show( ToastEntry entry )
	{
		transform.SetAsLastSibling();
		_visible = true;

		StopFeedbacks();
		RestoreRest();
		ApplyPouchIcon( entry );

		if ( label != null )
			label.text = entry.Message ?? string.Empty;

		if ( group != null )
		{
			group.blocksRaycasts = false;
			group.interactable = false;
			group.alpha = 1f;
		}

		if ( showFeedback != null )
			showFeedback.Play();
	}

	void Hide()
	{
		if ( !_visible )
		{
			HideImmediate();
			return;
		}

		_visible = false;
		if ( showFeedback != null )
			showFeedback.Stop();

		RestoreRest();

		if ( hideFeedback != null )
		{
			hideFeedback.Play();
			return;
		}

		HideImmediate();
	}

	void HideImmediate()
	{
		_visible = false;
		StopFeedbacks();
		RestoreRest();

		if ( group != null )
		{
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}

		ApplyPouchIcon( default );
	}

	void StopFeedbacks()
	{
		if ( showFeedback != null )
			showFeedback.Stop();
		if ( hideFeedback != null )
			hideFeedback.Stop();
	}

	void CaptureRest()
	{
		_restScale = transform.localScale;
		if ( _restScale.sqrMagnitude < 0.0001f )
			_restScale = Vector3.one;

		RectTransform rect = transform as RectTransform;
		if ( rect != null )
		{
			_restAnchored = rect.anchoredPosition;
			_restAnchoredCaptured = true;
		}
	}

	void RestoreRest()
	{
		transform.localScale = _restScale;
		transform.localRotation = Quaternion.identity;
		if ( !_restAnchoredCaptured )
			return;
		RectTransform rect = transform as RectTransform;
		if ( rect != null )
			rect.anchoredPosition = _restAnchored;
	}

	void EnsureUi()
	{
		RectTransform root = transform as RectTransform;
		if ( root == null )
			root = gameObject.AddComponent<RectTransform>();

		root.anchorMin = new Vector2( 0.5f, 1f );
		root.anchorMax = new Vector2( 0.5f, 1f );
		root.pivot = new Vector2( 0.5f, 1f );
		root.anchoredPosition = new Vector2( 0f, -TopInset );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, ToastHeight );

		if ( group == null )
			group = GetComponent<CanvasGroup>();
		if ( group == null )
			group = gameObject.AddComponent<CanvasGroup>();
		group.blocksRaycasts = false;
		group.interactable = false;

		Image backdrop = GetComponent<Image>();
		if ( backdrop == null )
			backdrop = gameObject.AddComponent<Image>();
		backdrop.color = new Color( 0f, 0f, 0f, 0.55f );
		backdrop.raycastTarget = false;

		EnsurePouchIcon();

		if ( label == null )
		{
			Transform existing = transform.Find( "Label" );
			if ( existing != null )
				label = existing.GetComponent<Text>();
		}

		if ( label == null )
		{
			GameObject labelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
			labelGo.transform.SetParent( transform, false );
			label = labelGo.GetComponent<Text>();
		}

		label.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( label.font == null )
			label.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		label.fontSize = 28;
		label.fontStyle = FontStyle.Bold;
		label.color = Color.white;
		label.horizontalOverflow = HorizontalWrapMode.Overflow;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.raycastTarget = false;
		label.supportRichText = false;
		ApplyLabelLayout( false );
	}

	void EnsurePouchIcon()
	{
		if ( pouchIcon == null )
		{
			Transform existing = transform.Find( "PouchIcon" );
			if ( existing != null )
				pouchIcon = existing.GetComponent<Image>();
		}

		if ( pouchIcon == null )
		{
			GameObject iconGo = new GameObject( "PouchIcon", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			iconGo.transform.SetParent( transform, false );
			pouchIcon = iconGo.GetComponent<Image>();
		}

		RectTransform iconRect = pouchIcon.transform as RectTransform;
		iconRect.anchorMin = new Vector2( 0f, 0.5f );
		iconRect.anchorMax = new Vector2( 0f, 0.5f );
		iconRect.pivot = new Vector2( 0f, 0.5f );
		iconRect.anchoredPosition = new Vector2( IconPadding, 0f );
		iconRect.sizeDelta = new Vector2( IconSize, IconSize );

		pouchIcon.color = Color.white;
		pouchIcon.raycastTarget = false;
		pouchIcon.preserveAspect = true;
		pouchIcon.enabled = false;
		pouchIcon.gameObject.SetActive( false );
	}

	void ApplyPouchIcon( ToastEntry entry )
	{
		if ( pouchIcon == null )
		{
			ApplyLabelLayout( false );
			return;
		}

		Sprite sprite = entry.ShowPouchIcon ? ResolvePouchSprite( entry.Pouch ) : null;
		bool show = sprite != null;
		pouchIcon.sprite = sprite;
		pouchIcon.enabled = show;
		pouchIcon.gameObject.SetActive( show );
		ApplyLabelLayout( show );
	}

	Sprite ResolvePouchSprite( CarryBucketKind pouch )
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
				return generalPouchSprite;
			default:
				return coinPouchSprite;
		}
	}

	void ApplyLabelLayout( bool iconVisible )
	{
		if ( label == null )
			return;

		RectTransform labelRect = label.transform as RectTransform;
		if ( labelRect == null )
			return;

		float left = iconVisible ? IconPadding + IconSize + IconPadding : 16f;
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = new Vector2( left, 8f );
		labelRect.offsetMax = new Vector2( -16f, -8f );
		label.alignment = iconVisible ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
	}

	void EnsureFeedbacks()
	{
		RectTransform rect = transform as RectTransform;

		if ( showFeedback == null )
			showFeedback = FindOrCreateFeedbacks( "ShowFeedbacks" );

		if ( showFeedback != null && ( showFeedback.FeedbackList == null || showFeedback.FeedbackList.Count == 0 ) )
		{
			showFeedback.UseUnscaledTime = true;
			ParallelFeedback parallel = new ParallelFeedback();
			parallel.Feedbacks = new List<Feedback>
			{
				new CanvasGroupFadeFeedback
				{
					Target = group,
					From = 0f,
					To = 1f,
					Duration = 0.22f,
					UseUnscaledTime = true
				},
				new UiAnchoredSlideFeedback
				{
					Target = rect,
					FromOffset = new Vector2( 0f, 28f ),
					ToOffset = Vector2.zero,
					Duration = 0.28f,
					UseUnscaledTime = true
				},
				new UiPunchScaleFeedback
				{
					Target = transform,
					Punch = new Vector3( 0.12f, 0.12f, 0f ),
					Duration = 0.35f,
					UseUnscaledTime = true
				}
			};
			showFeedback.AddFeedback( parallel );
		}

		if ( hideFeedback == null )
			hideFeedback = FindOrCreateFeedbacks( "HideFeedbacks" );

		if ( hideFeedback != null && ( hideFeedback.FeedbackList == null || hideFeedback.FeedbackList.Count == 0 ) )
		{
			hideFeedback.UseUnscaledTime = true;
			ParallelFeedback parallel = new ParallelFeedback();
			parallel.Feedbacks = new List<Feedback>
			{
				new CanvasGroupFadeFeedback
				{
					Target = group,
					From = 1f,
					To = 0f,
					Duration = 0.22f,
					CaptureCurrentAsFrom = true,
					UseUnscaledTime = true
				},
				new UiPunchScaleFeedback
				{
					Target = transform,
					Punch = new Vector3( -0.04f, -0.04f, 0f ),
					Duration = 0.2f,
					UseUnscaledTime = true
				}
			};
			hideFeedback.AddFeedback( parallel );
		}

		if ( showFeedback != null )
			showFeedback.UseUnscaledTime = true;
		if ( hideFeedback != null )
			hideFeedback.UseUnscaledTime = true;
	}

	Feedbacks FindOrCreateFeedbacks( string childName )
	{
		Transform existing = transform.Find( childName );
		if ( existing != null )
		{
			Feedbacks found = existing.GetComponent<Feedbacks>();
			if ( found != null )
				return found;
			return existing.gameObject.AddComponent<Feedbacks>();
		}

		GameObject go = new GameObject( childName, typeof( RectTransform ) );
		go.transform.SetParent( transform, false );
		return go.AddComponent<Feedbacks>();
	}

	static float ResolveFeedbackDuration( Feedbacks feedbacks, float fallback )
	{
		if ( feedbacks == null || feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
			return fallback;

		float max = 0f;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			Feedback feedback = feedbacks.FeedbackList[ i ];
			if ( feedback == null || !feedback.Enabled )
				continue;
			float hold = feedback.GetHoldDuration();
			if ( hold > max )
				max = hold;
		}

		return max > 0.01f ? max : fallback;
	}
}
