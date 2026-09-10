using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Unlock / reward toast below DiscoveryToast. Waits until discovery toast is idle before showing.
/// </summary>
public class UnlockRewardToastUI : MonoBehaviour
{
	const float HoldSeconds = 2.5f;
	const float TopInset = 160f;
	const float ToastWidth = 720f;
	const float ToastHeight = 64f;
	const float IconSize = 40f;
	const float IconPadding = 12f;

	struct ToastEntry
	{
		public string Message;
		public Sprite Icon;
	}

	public static UnlockRewardToastUI Instance { get; private set; }

	public bool IsBusy => _visible || _routine != null || _queue.Count > 0;

	static readonly Queue<ToastEntry> s_pendingBeforeInstance = new Queue<ToastEntry>( 8 );

	[SerializeField] CanvasGroup group;
	[SerializeField] Text label;
	[SerializeField] Image rewardIcon;
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
			// Same-frame constellation complete may enqueue DiscoveryToast after AbilityUnlocked.
			yield return null;
			while ( IsDiscoveryBusy() )
				yield return null;

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

	static bool IsDiscoveryBusy()
	{
		DiscoveryToastUI discovery = DiscoveryToastUI.Instance;
		return discovery != null && discovery.IsBusy;
	}

	void Show( ToastEntry entry )
	{
		transform.SetAsLastSibling();
		_visible = true;

		StopFeedbacks();
		RestoreRest();
		ApplyRewardIcon( entry );

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

		ApplyRewardIcon( default );
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
		backdrop.color = new Color( 0.08f, 0.12f, 0.2f, 0.7f );
		backdrop.raycastTarget = false;

		EnsureRewardIcon();

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

	void EnsureRewardIcon()
	{
		if ( rewardIcon == null )
		{
			Transform existing = transform.Find( "RewardIcon" );
			if ( existing != null )
				rewardIcon = existing.GetComponent<Image>();
		}

		if ( rewardIcon == null )
		{
			GameObject iconGo = new GameObject( "RewardIcon", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			iconGo.transform.SetParent( transform, false );
			rewardIcon = iconGo.GetComponent<Image>();
		}

		RectTransform iconRect = rewardIcon.transform as RectTransform;
		iconRect.anchorMin = new Vector2( 0f, 0.5f );
		iconRect.anchorMax = new Vector2( 0f, 0.5f );
		iconRect.pivot = new Vector2( 0f, 0.5f );
		iconRect.anchoredPosition = new Vector2( IconPadding, 0f );
		iconRect.sizeDelta = new Vector2( IconSize, IconSize );

		rewardIcon.color = Color.white;
		rewardIcon.raycastTarget = false;
		rewardIcon.preserveAspect = true;
		rewardIcon.enabled = false;
		rewardIcon.gameObject.SetActive( false );
	}

	void ApplyRewardIcon( ToastEntry entry )
	{
		if ( rewardIcon == null )
		{
			ApplyLabelLayout( false );
			return;
		}

		bool show = entry.Icon != null;
		rewardIcon.sprite = entry.Icon;
		rewardIcon.enabled = show;
		rewardIcon.gameObject.SetActive( show );
		ApplyLabelLayout( show );
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
