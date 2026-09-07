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

	public static DiscoveryToastUI Instance { get; private set; }

	static readonly Queue<string> s_pendingBeforeInstance = new Queue<string>( 8 );

	[SerializeField] CanvasGroup group;
	[SerializeField] Text label;
	[SerializeField] Feedbacks showFeedback;
	[SerializeField] Feedbacks hideFeedback;
	[SerializeField] float holdDuration = HoldSeconds;

	readonly Queue<string> _queue = new Queue<string>( 4 );
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

		EnqueueStatic( "New Discovery: " + ResolveName( treasure ) );
	}

	public static void NotifyMessage( string message )
	{
		EnqueueStatic( message );
	}

	static void EnqueueStatic( string message )
	{
		if ( string.IsNullOrEmpty( message ) )
			return;

		DiscoveryToastUI ui = Instance;
		if ( ui != null && ui._ready )
		{
			ui.Enqueue( message );
			return;
		}

		s_pendingBeforeInstance.Enqueue( message );
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
		Enqueue( "Display Complete: " + name );
	}

	void OnArtifactDisplayCompleted( ArtifactPresentationTableCompletedEvent evt )
	{
		Enqueue( "Display Complete: Artifacts" );
	}

	void OnConstellationCompleted( GemConstellationCompletedEvent evt )
	{
		string name = "Constellation";
		if ( evt.Constellation != null )
		{
			TreasureDefinition gem = evt.Constellation.DefaultAcceptedGem;
			if ( gem != null && !string.IsNullOrEmpty( gem.displayName ) )
				name = gem.displayName;
			else if ( !string.IsNullOrEmpty( evt.Constellation.InteractionName ) )
				name = evt.Constellation.InteractionName;
		}

		Enqueue( "Constellation Complete: " + name );
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

	void Enqueue( string message )
	{
		if ( !_ready )
			Setup();

		if ( string.IsNullOrEmpty( message ) )
			return;

		if ( !isActiveAndEnabled )
			gameObject.SetActive( true );

		if ( !isActiveAndEnabled )
		{
			s_pendingBeforeInstance.Enqueue( message );
			return;
		}

		_queue.Enqueue( message );
		if ( _routine == null )
			_routine = StartCoroutine( ToastRoutine() );
	}

	IEnumerator ToastRoutine()
	{
		while ( _queue.Count > 0 )
		{
			string message = _queue.Dequeue();
			Show( message );

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

	void Show( string message )
	{
		transform.SetAsLastSibling();
		_visible = true;

		StopFeedbacks();
		RestoreRest();

		if ( label != null )
			label.text = message ?? string.Empty;

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
			RectTransform labelRect = labelGo.transform as RectTransform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2( 16f, 8f );
			labelRect.offsetMax = new Vector2( -16f, -8f );
			label = labelGo.GetComponent<Text>();
		}

		label.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( label.font == null )
			label.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		label.fontSize = 28;
		label.fontStyle = FontStyle.Bold;
		label.alignment = TextAnchor.MiddleCenter;
		label.color = Color.white;
		label.horizontalOverflow = HorizontalWrapMode.Overflow;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.raycastTarget = false;
		label.supportRichText = false;
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
