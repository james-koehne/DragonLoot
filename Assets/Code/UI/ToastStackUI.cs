using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared top-center toast list for discovery and unlock messages. Up to 3 visible rows.
/// </summary>
public class ToastStackUI : MonoBehaviour
{
	public const int MaxVisible = 3;
	public const float TopInset = 72f;
	public const float ToastWidth = 720f;
	public const float ToastHeight = 64f;
	public const float Spacing = 8f;
	const float IconSize = 40f;
	const float IconPadding = 12f;
	const float DefaultHoldSeconds = 2.5f;

	public enum Kind
	{
		Discovery,
		Unlock
	}

	public struct Entry
	{
		public Kind Kind;
		public string Message;
		public Sprite Icon;
		public CarryBucketKind Pouch;
		public bool ShowPouchIcon;
		public float HoldDuration;
	}

	sealed class ToastItem
	{
		public RectTransform Slot;
		public RectTransform Content;
		public CanvasGroup Group;
		public Image Backdrop;
		public Image Icon;
		public Text Label;
		public Feedbacks DiscoveryShow;
		public Feedbacks DiscoveryHide;
		public Feedbacks UnlockShow;
		public Feedbacks UnlockHide;
		public bool InUse;
		public Kind Kind;
		public Coroutine Routine;
	}

	static readonly Color DiscoveryBackdrop = new Color( 0f, 0f, 0f, 0.55f );
	static readonly Color UnlockBackdrop = new Color( 0.08f, 0.12f, 0.2f, 0.7f );
	static readonly Queue<Entry> s_pendingBeforeInstance = new Queue<Entry>( 8 );

	public static ToastStackUI Instance { get; private set; }

	readonly Queue<Entry> _queue = new Queue<Entry>( 8 );
	readonly List<ToastItem> _active = new List<ToastItem>( MaxVisible );
	ToastItem[] _items;
	Font _labelFont;
	bool _ready;
	Feedbacks _discoveryShowTemplate;
	Feedbacks _discoveryHideTemplate;
	CanvasGroup _discoverySourceGroup;
	RectTransform _discoverySourceRect;
	Feedbacks _unlockShowTemplate;
	Feedbacks _unlockHideTemplate;
	CanvasGroup _unlockSourceGroup;
	RectTransform _unlockSourceRect;

	public int VisibleCount => _active.Count;
	public int QueuedCount => _queue.Count + s_pendingBeforeInstance.Count;
	public bool IsBusy => _active.Count > 0 || _queue.Count > 0 || s_pendingBeforeInstance.Count > 0;

	public static ToastStackUI EnsureOnCanvas( Transform from )
	{
		if ( Instance != null )
			return Instance;

		Transform canvasRoot = ResolveCanvasRoot( from );
		if ( canvasRoot == null )
			return null;

		Transform existing = canvasRoot.Find( "ToastStack" );
		GameObject go = existing != null ? existing.gameObject : new GameObject( "ToastStack", typeof( RectTransform ) );
		if ( existing == null )
			go.transform.SetParent( canvasRoot, false );

		ToastStackUI stack = go.GetComponent<ToastStackUI>();
		if ( stack == null )
			stack = go.AddComponent<ToastStackUI>();
		stack.Setup();
		return stack;
	}

	public static void NotifyDiscovery( string message, CarryBucketKind pouch, bool showPouchIcon, float holdDuration )
	{
		EnqueueStatic( new Entry
		{
			Kind = Kind.Discovery,
			Message = message,
			Pouch = pouch,
			ShowPouchIcon = showPouchIcon,
			HoldDuration = holdDuration
		} );
	}

	public static void NotifyUnlock( string message, Sprite icon, float holdDuration )
	{
		EnqueueStatic( new Entry
		{
			Kind = Kind.Unlock,
			Message = message,
			Icon = icon,
			HoldDuration = holdDuration
		} );
	}

	static void EnqueueStatic( Entry entry )
	{
		if ( string.IsNullOrEmpty( entry.Message ) )
			return;

		ToastStackUI stack = Instance;
		if ( stack != null && stack._ready )
		{
			stack.Enqueue( entry );
			return;
		}

		s_pendingBeforeInstance.Enqueue( entry );
	}

	void Awake()
	{
		Instance = this;
		Setup();
	}

	void OnEnable()
	{
		Instance = this;
		if ( !_ready )
			Setup();
		else
		{
			FlushPending();
			TryShowQueued();
		}
	}

	void OnDisable()
	{
		StopItemRoutines();
		_active.Clear();
		if ( _items == null )
			return;

		for ( int i = 0; i < _items.Length; i++ )
		{
			ToastItem item = _items[ i ];
			if ( item == null )
				continue;
			item.InUse = false;
			item.Routine = null;
			HideImmediate( item );
		}
	}

	void OnDestroy()
	{
		if ( Instance == this )
			Instance = null;
	}

	public void Setup()
	{
		Instance = this;
		EnsureRoot();
		EnsureItems();
		_ready = true;
		FlushPending();
	}

	public void RegisterKind( Kind kind, Feedbacks show, Feedbacks hide, CanvasGroup sourceGroup, RectTransform sourceRect )
	{
		if ( kind == Kind.Unlock )
		{
			_unlockShowTemplate = show;
			_unlockHideTemplate = hide;
			_unlockSourceGroup = sourceGroup;
			_unlockSourceRect = sourceRect;
		}
		else
		{
			_discoveryShowTemplate = show;
			_discoveryHideTemplate = hide;
			_discoverySourceGroup = sourceGroup;
			_discoverySourceRect = sourceRect;
		}

		if ( _items == null )
			return;

		for ( int i = 0; i < _items.Length; i++ )
			EnsureItemFeedbacks( _items[ i ] );
	}

	public void SetLabelFont( Font font )
	{
		if ( font == null )
			return;

		_labelFont = font;
		if ( _items == null )
			return;

		for ( int i = 0; i < _items.Length; i++ )
		{
			ToastItem item = _items[ i ];
			if ( item != null && item.Label != null )
				item.Label.font = font;
		}
	}

	public bool HasToast( Kind kind )
	{
		for ( int i = 0; i < _active.Count; i++ )
		{
			if ( _active[ i ].Kind == kind )
				return true;
		}

		foreach ( Entry entry in _queue )
		{
			if ( entry.Kind == kind )
				return true;
		}

		foreach ( Entry entry in s_pendingBeforeInstance )
		{
			if ( entry.Kind == kind )
				return true;
		}

		return false;
	}

	void Enqueue( Entry entry )
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
		TryShowQueued();
	}

	void FlushPending()
	{
		while ( s_pendingBeforeInstance.Count > 0 )
			Enqueue( s_pendingBeforeInstance.Dequeue() );
	}

	void TryShowQueued()
	{
		while ( _queue.Count > 0 && _active.Count < MaxVisible )
		{
			ToastItem item = AcquireIdle();
			if ( item == null )
				return;

			Entry entry = _queue.Dequeue();
			item.InUse = true;
			item.Kind = entry.Kind;
			_active.Add( item );
			Relayout();
			item.Routine = StartCoroutine( RunToast( item, entry ) );
		}
	}

	IEnumerator RunToast( ToastItem item, Entry entry )
	{
		Show( item, entry );

		float hold = entry.HoldDuration > 0.1f ? entry.HoldDuration : DefaultHoldSeconds;
		float elapsed = 0f;
		while ( elapsed < hold )
		{
			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		Hide( item );
		float hideDur = ResolveFeedbackDuration( ResolveHide( item ), 0.25f );
		elapsed = 0f;
		while ( elapsed < hideDur )
		{
			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		HideImmediate( item );
		Release( item );
		TryShowQueued();
	}

	void Show( ToastItem item, Entry entry )
	{
		transform.SetAsLastSibling();
		StopItemFeedbacks( item );
		RestoreItemRest( item );
		ApplyVisual( item, entry );

		if ( item.Group != null )
		{
			item.Group.blocksRaycasts = false;
			item.Group.interactable = false;
			item.Group.alpha = 1f;
		}

		Feedbacks show = ResolveShow( item );
		if ( show != null )
			show.Play();
	}

	void Hide( ToastItem item )
	{
		Feedbacks show = ResolveShow( item );
		if ( show != null )
			show.Stop();

		RestoreItemRest( item );

		Feedbacks hide = ResolveHide( item );
		if ( hide != null )
		{
			hide.Play();
			return;
		}

		HideImmediate( item );
	}

	void HideImmediate( ToastItem item )
	{
		StopItemFeedbacks( item );
		RestoreItemRest( item );

		if ( item.Group != null )
		{
			item.Group.alpha = 0f;
			item.Group.blocksRaycasts = false;
			item.Group.interactable = false;
		}

		ApplyIcon( item, null );
	}

	void Release( ToastItem item )
	{
		if ( item.Routine != null )
			item.Routine = null;

		_active.Remove( item );
		item.InUse = false;
		HideImmediate( item );
		Relayout();
	}

	void Relayout()
	{
		for ( int i = 0; i < _active.Count; i++ )
			SetSlotIndex( _active[ i ], i );
	}

	static void SetSlotIndex( ToastItem item, int index )
	{
		if ( item == null || item.Slot == null )
			return;

		float y = -index * ( ToastHeight + Spacing );
		item.Slot.anchoredPosition = new Vector2( 0f, y );
	}

	ToastItem AcquireIdle()
	{
		if ( _items == null )
			return null;

		for ( int i = 0; i < _items.Length; i++ )
		{
			ToastItem item = _items[ i ];
			if ( item != null && !item.InUse )
				return item;
		}

		return null;
	}

	void ApplyVisual( ToastItem item, Entry entry )
	{
		if ( item.Backdrop != null )
			item.Backdrop.color = entry.Kind == Kind.Unlock ? UnlockBackdrop : DiscoveryBackdrop;

		if ( item.Label != null )
			item.Label.text = entry.Message ?? string.Empty;

		Sprite icon = ResolveIcon( entry );
		ApplyIcon( item, icon );
	}

	static Sprite ResolveIcon( Entry entry )
	{
		if ( entry.Icon != null )
			return entry.Icon;

		if ( entry.Kind != Kind.Discovery || !entry.ShowPouchIcon )
			return null;

		DiscoveryToastUI discovery = DiscoveryToastUI.Instance;
		if ( discovery == null )
			return null;

		return discovery.ResolvePouchSprite( entry.Pouch );
	}

	void ApplyIcon( ToastItem item, Sprite sprite )
	{
		if ( item.Icon == null )
		{
			ApplyLabelLayout( item, false );
			return;
		}

		bool show = sprite != null;
		item.Icon.sprite = sprite;
		item.Icon.enabled = show;
		item.Icon.gameObject.SetActive( show );
		ApplyLabelLayout( item, show );
	}

	static void ApplyLabelLayout( ToastItem item, bool iconVisible )
	{
		if ( item.Label == null )
			return;

		RectTransform labelRect = item.Label.transform as RectTransform;
		if ( labelRect == null )
			return;

		float left = iconVisible ? IconPadding + IconSize + IconPadding : 16f;
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = new Vector2( left, 8f );
		labelRect.offsetMax = new Vector2( -16f, -8f );
		item.Label.alignment = iconVisible ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
	}

	void EnsureRoot()
	{
		RectTransform root = transform as RectTransform;
		if ( root == null )
			root = gameObject.AddComponent<RectTransform>();

		float height = MaxVisible * ToastHeight + ( MaxVisible - 1 ) * Spacing;
		root.anchorMin = new Vector2( 0.5f, 1f );
		root.anchorMax = new Vector2( 0.5f, 1f );
		root.pivot = new Vector2( 0.5f, 1f );
		root.anchoredPosition = new Vector2( 0f, -TopInset );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
	}

	void EnsureItems()
	{
		if ( _items != null && _items.Length == MaxVisible )
			return;

		_items = new ToastItem[ MaxVisible ];
		for ( int i = 0; i < MaxVisible; i++ )
		{
			_items[ i ] = CreateItem( i );
			HideImmediate( _items[ i ] );
		}
	}

	ToastItem CreateItem( int index )
	{
		GameObject slotGo = new GameObject( "ToastItem_" + index, typeof( RectTransform ) );
		slotGo.transform.SetParent( transform, false );

		RectTransform slot = slotGo.GetComponent<RectTransform>();
		slot.anchorMin = new Vector2( 0.5f, 1f );
		slot.anchorMax = new Vector2( 0.5f, 1f );
		slot.pivot = new Vector2( 0.5f, 1f );
		slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
		slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, ToastHeight );
		SetSlotIndex( new ToastItem { Slot = slot }, index );

		GameObject contentGo = new GameObject( "Content", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( CanvasGroup ) );
		contentGo.transform.SetParent( slot, false );

		RectTransform content = contentGo.GetComponent<RectTransform>();
		content.anchorMin = new Vector2( 0.5f, 1f );
		content.anchorMax = new Vector2( 0.5f, 1f );
		content.pivot = new Vector2( 0.5f, 1f );
		content.anchoredPosition = Vector2.zero;
		content.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
		content.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, ToastHeight );

		Image backdrop = contentGo.GetComponent<Image>();
		backdrop.color = DiscoveryBackdrop;
		backdrop.raycastTarget = false;

		CanvasGroup group = contentGo.GetComponent<CanvasGroup>();
		group.blocksRaycasts = false;
		group.interactable = false;
		group.alpha = 0f;

		Image icon = CreateIcon( content );
		Text label = CreateLabel( content );

		ToastItem item = new ToastItem
		{
			Slot = slot,
			Content = content,
			Group = group,
			Backdrop = backdrop,
			Icon = icon,
			Label = label
		};

		EnsureItemFeedbacks( item );
		return item;
	}

	void EnsureItemFeedbacks( ToastItem item )
	{
		if ( item == null || item.Content == null )
			return;

		if ( item.DiscoveryShow == null && _discoveryShowTemplate != null )
			item.DiscoveryShow = CloneAndRetarget( _discoveryShowTemplate, item, _discoverySourceGroup, _discoverySourceRect, "DiscoveryShowFeedbacks" );
		if ( item.DiscoveryHide == null && _discoveryHideTemplate != null )
			item.DiscoveryHide = CloneAndRetarget( _discoveryHideTemplate, item, _discoverySourceGroup, _discoverySourceRect, "DiscoveryHideFeedbacks" );
		if ( item.UnlockShow == null && _unlockShowTemplate != null )
			item.UnlockShow = CloneAndRetarget( _unlockShowTemplate, item, _unlockSourceGroup, _unlockSourceRect, "UnlockShowFeedbacks" );
		if ( item.UnlockHide == null && _unlockHideTemplate != null )
			item.UnlockHide = CloneAndRetarget( _unlockHideTemplate, item, _unlockSourceGroup, _unlockSourceRect, "UnlockHideFeedbacks" );
	}

	static Feedbacks ResolveShow( ToastItem item )
	{
		if ( item == null )
			return null;
		return item.Kind == Kind.Unlock ? item.UnlockShow : item.DiscoveryShow;
	}

	static Feedbacks ResolveHide( ToastItem item )
	{
		if ( item == null )
			return null;
		return item.Kind == Kind.Unlock ? item.UnlockHide : item.DiscoveryHide;
	}

	static Feedbacks CloneAndRetarget( Feedbacks template, ToastItem item, CanvasGroup sourceGroup, RectTransform sourceRect, string cloneName )
	{
		if ( template == null || item == null || item.Content == null )
			return null;

		GameObject cloneGo = Instantiate( template.gameObject, item.Content, false );
		cloneGo.name = cloneName;
		cloneGo.SetActive( true );

		Feedbacks clone = cloneGo.GetComponent<Feedbacks>();
		if ( clone == null )
			return null;

		RetargetFeedbacks( clone.FeedbackList, sourceGroup, sourceRect, item.Group, item.Content );
		return clone;
	}

	static void RetargetFeedbacks( List<Feedback> list, CanvasGroup sourceGroup, RectTransform sourceRect, CanvasGroup destGroup, RectTransform destRect )
	{
		if ( list == null )
			return;

		for ( int i = 0; i < list.Count; i++ )
		{
			Feedback feedback = list[ i ];
			if ( feedback == null )
				continue;

			CanvasGroupFadeFeedback fade = feedback as CanvasGroupFadeFeedback;
			if ( fade != null && ( sourceGroup == null || fade.Target == sourceGroup ) )
				fade.Target = destGroup;

			UiAnchoredSlideFeedback slide = feedback as UiAnchoredSlideFeedback;
			if ( slide != null && ( sourceRect == null || slide.Target == sourceRect ) )
				slide.Target = destRect;

			UiPunchScaleFeedback punch = feedback as UiPunchScaleFeedback;
			if ( punch != null && ( sourceRect == null || punch.Target == sourceRect ) )
				punch.Target = destRect;

			ParallelFeedback parallel = feedback as ParallelFeedback;
			if ( parallel != null )
				RetargetFeedbacks( parallel.Feedbacks, sourceGroup, sourceRect, destGroup, destRect );
		}
	}

	Image CreateIcon( RectTransform parent )
	{
		GameObject iconGo = new GameObject( "Icon", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		iconGo.transform.SetParent( parent, false );

		Image icon = iconGo.GetComponent<Image>();
		RectTransform iconRect = icon.transform as RectTransform;
		iconRect.anchorMin = new Vector2( 0f, 0.5f );
		iconRect.anchorMax = new Vector2( 0f, 0.5f );
		iconRect.pivot = new Vector2( 0f, 0.5f );
		iconRect.anchoredPosition = new Vector2( IconPadding, 0f );
		iconRect.sizeDelta = new Vector2( IconSize, IconSize );

		icon.color = Color.white;
		icon.raycastTarget = false;
		icon.preserveAspect = true;
		icon.enabled = false;
		iconGo.SetActive( false );
		return icon;
	}

	Text CreateLabel( RectTransform parent )
	{
		GameObject labelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		labelGo.transform.SetParent( parent, false );

		Text label = labelGo.GetComponent<Text>();
		label.font = _labelFont != null ? _labelFont : Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( label.font == null )
			label.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		label.fontSize = 28;
		label.fontStyle = FontStyle.Bold;
		label.color = Color.white;
		label.horizontalOverflow = HorizontalWrapMode.Overflow;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.raycastTarget = false;
		label.supportRichText = false;

		ToastItem layout = new ToastItem { Label = label };
		ApplyLabelLayout( layout, false );
		return label;
	}

	static void StopItemFeedbacks( ToastItem item )
	{
		if ( item == null )
			return;

		StopFeedback( item.DiscoveryShow );
		StopFeedback( item.DiscoveryHide );
		StopFeedback( item.UnlockShow );
		StopFeedback( item.UnlockHide );
	}

	static void StopFeedback( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.Stop();
	}

	static void RestoreItemRest( ToastItem item )
	{
		if ( item.Content == null )
			return;

		item.Content.localScale = Vector3.one;
		item.Content.localRotation = Quaternion.identity;
		item.Content.anchoredPosition = Vector2.zero;
	}

	void StopItemRoutines()
	{
		if ( _items == null )
			return;

		for ( int i = 0; i < _items.Length; i++ )
		{
			ToastItem item = _items[ i ];
			if ( item == null || item.Routine == null )
				continue;
			StopCoroutine( item.Routine );
			item.Routine = null;
		}
	}

	static Transform ResolveCanvasRoot( Transform from )
	{
		if ( from == null )
			return null;

		Canvas canvas = from.GetComponentInParent<Canvas>();
		if ( canvas != null )
			return canvas.transform;

		if ( from.parent != null )
			return from.parent;

		return from;
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
