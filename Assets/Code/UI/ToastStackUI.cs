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
	public const float Spacing = 8f;

	const float AccentWidthDiscovery = 6f;
	const float AccentWidthUnlock = 8f;
	const float AccentWidthMilestone = 10f;
	const float IconPadding = 12f;
	const float TextRightPad = 16f;
	const float KickerGap = 2f;

	static readonly Color GoldOutline = new Color( 0.92f, 0.78f, 0.32f, 0.85f );
	static readonly Color GoldKicker = new Color( 1f, 0.88f, 0.45f, 1f );
	static readonly Color CoinAccent = new Color( 0.95f, 0.78f, 0.28f, 1f );
	static readonly Color GemAccent = new Color( 0.35f, 0.82f, 0.95f, 1f );
	static readonly Color ArtifactAccent = new Color( 0.82f, 0.55f, 0.32f, 1f );
	static readonly Color GeneralAccent = new Color( 0.7f, 0.72f, 0.78f, 1f );
	static readonly Color UnlockAccent = new Color( 1f, 0.86f, 0.35f, 1f );
	static readonly Color MilestoneAccent = new Color( 1f, 0.92f, 0.45f, 1f );

	static readonly Color DiscoveryBackdrop = new Color( 0.05f, 0.04f, 0.02f, 0.72f );
	static readonly Color CompletionBackdrop = new Color( 0.06f, 0.07f, 0.1f, 0.78f );
	static readonly Color UnlockBackdrop = new Color( 0.1f, 0.08f, 0.03f, 0.82f );
	static readonly Color MilestoneBackdrop = new Color( 0.14f, 0.1f, 0.03f, 0.88f );

	public enum Kind
	{
		Discovery,
		Unlock
	}

	public enum ToastTier
	{
		Discovery,
		Completion,
		Unlock,
		Milestone
	}

	public struct Entry
	{
		public Kind Kind;
		public ToastTier Tier;
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
		public Outline Outline;
		public Image Accent;
		public Image Icon;
		public Text Kicker;
		public Text Label;
		public Feedbacks DiscoveryShow;
		public Feedbacks DiscoveryHide;
		public Feedbacks CompletionShow;
		public Feedbacks CompletionHide;
		public Feedbacks UnlockShow;
		public Feedbacks UnlockHide;
		public Feedbacks MilestoneShow;
		public Feedbacks MilestoneHide;
		public bool InUse;
		public Kind Kind;
		public ToastTier Tier;
		public float Height;
		public Coroutine Routine;
	}

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
	Image _discoverySourceAccent;
	Image _discoverySourceIcon;

	Feedbacks _completionShowTemplate;
	Feedbacks _completionHideTemplate;

	Feedbacks _unlockShowTemplate;
	Feedbacks _unlockHideTemplate;
	CanvasGroup _unlockSourceGroup;
	RectTransform _unlockSourceRect;
	Image _unlockSourceAccent;
	Image _unlockSourceIcon;
	Image _unlockSourceBackdrop;

	Feedbacks _milestoneShowTemplate;
	Feedbacks _milestoneHideTemplate;

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

	public static void NotifyDiscovery( string message, CarryBucketKind pouch, bool showPouchIcon, float holdDuration, ToastTier tier = ToastTier.Discovery )
	{
		EnqueueStatic( new Entry
		{
			Kind = Kind.Discovery,
			Tier = tier,
			Message = message,
			Pouch = pouch,
			ShowPouchIcon = showPouchIcon,
			HoldDuration = holdDuration > 0.1f ? holdDuration : DefaultHoldForTier( tier )
		} );
	}

	public static void NotifyUnlock( string message, Sprite icon, float holdDuration, ToastTier tier = ToastTier.Unlock )
	{
		EnqueueStatic( new Entry
		{
			Kind = Kind.Unlock,
			Tier = tier,
			Message = message,
			Icon = icon,
			HoldDuration = holdDuration > 0.1f ? holdDuration : DefaultHoldForTier( tier )
		} );
	}

	public static float DefaultHoldForTier( ToastTier tier )
	{
		switch ( tier )
		{
			case ToastTier.Completion:
				return 3.2f;
			case ToastTier.Unlock:
				return 3.5f;
			case ToastTier.Milestone:
				return 4f;
			default:
				return 2.5f;
		}
	}

	public static float HeightForTier( ToastTier tier )
	{
		switch ( tier )
		{
			case ToastTier.Completion:
				return 76f;
			case ToastTier.Unlock:
				return 88f;
			case ToastTier.Milestone:
				return 100f;
			default:
				return 68f;
		}
	}

	public static float IconSizeForTier( ToastTier tier )
	{
		switch ( tier )
		{
			case ToastTier.Completion:
				return 56f;
			case ToastTier.Unlock:
			case ToastTier.Milestone:
				return 64f;
			default:
				return 48f;
		}
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

	public void RegisterTier( ToastTier tier, Feedbacks show, Feedbacks hide, CanvasGroup sourceGroup, RectTransform sourceRect, Image sourceAccent = null, Image sourceIcon = null, Image sourceBackdrop = null )
	{
		switch ( tier )
		{
			case ToastTier.Completion:
				_completionShowTemplate = show;
				_completionHideTemplate = hide;
				if ( sourceGroup != null )
					_discoverySourceGroup = sourceGroup;
				if ( sourceRect != null )
					_discoverySourceRect = sourceRect;
				if ( sourceAccent != null )
					_discoverySourceAccent = sourceAccent;
				if ( sourceIcon != null )
					_discoverySourceIcon = sourceIcon;
				break;
			case ToastTier.Unlock:
				_unlockShowTemplate = show;
				_unlockHideTemplate = hide;
				_unlockSourceGroup = sourceGroup;
				_unlockSourceRect = sourceRect;
				_unlockSourceAccent = sourceAccent;
				_unlockSourceIcon = sourceIcon;
				_unlockSourceBackdrop = sourceBackdrop;
				break;
			case ToastTier.Milestone:
				_milestoneShowTemplate = show;
				_milestoneHideTemplate = hide;
				if ( sourceGroup != null )
					_unlockSourceGroup = sourceGroup;
				if ( sourceRect != null )
					_unlockSourceRect = sourceRect;
				if ( sourceAccent != null )
					_unlockSourceAccent = sourceAccent;
				if ( sourceIcon != null )
					_unlockSourceIcon = sourceIcon;
				if ( sourceBackdrop != null )
					_unlockSourceBackdrop = sourceBackdrop;
				break;
			default:
				_discoveryShowTemplate = show;
				_discoveryHideTemplate = hide;
				_discoverySourceGroup = sourceGroup;
				_discoverySourceRect = sourceRect;
				_discoverySourceAccent = sourceAccent;
				_discoverySourceIcon = sourceIcon;
				break;
		}

		if ( _items == null )
			return;

		for ( int i = 0; i < _items.Length; i++ )
			EnsureItemFeedbacks( _items[ i ] );
	}

	/// <summary>Legacy Kind registration — maps Discovery/Unlock templates.</summary>
	public void RegisterKind( Kind kind, Feedbacks show, Feedbacks hide, CanvasGroup sourceGroup, RectTransform sourceRect )
	{
		RegisterTier( kind == Kind.Unlock ? ToastTier.Unlock : ToastTier.Discovery, show, hide, sourceGroup, sourceRect );
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
			if ( item == null )
				continue;
			if ( item.Label != null )
				item.Label.font = font;
			if ( item.Kicker != null )
				item.Kicker.font = font;
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
			item.Tier = entry.Tier;
			item.Height = HeightForTier( entry.Tier );
			_active.Add( item );
			Relayout();
			item.Routine = StartCoroutine( RunToast( item, entry ) );
		}
	}

	IEnumerator RunToast( ToastItem item, Entry entry )
	{
		Show( item, entry );

		float hold = entry.HoldDuration > 0.1f ? entry.HoldDuration : DefaultHoldForTier( entry.Tier );
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
		Relayout();

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

		ApplyIcon( item, null, ToastTier.Discovery );
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
		float y = 0f;
		for ( int i = 0; i < _active.Count; i++ )
		{
			ToastItem item = _active[ i ];
			SetSlotPosition( item, y );
			y -= item.Height + Spacing;
		}
	}

	static void SetSlotPosition( ToastItem item, float y )
	{
		if ( item == null || item.Slot == null )
			return;

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
		float height = HeightForTier( entry.Tier );
		item.Height = height;
		item.Tier = entry.Tier;
		ApplySize( item, height );

		if ( item.Backdrop != null )
			item.Backdrop.color = BackdropForTier( entry.Tier );

		if ( item.Outline != null )
		{
			item.Outline.enabled = true;
			item.Outline.effectColor = GoldOutline;
			item.Outline.effectDistance = entry.Tier == ToastTier.Milestone
				? new Vector2( 3f, 3f )
				: new Vector2( 2f, 2f );
		}

		if ( item.Accent != null )
		{
			item.Accent.color = AccentForEntry( entry );
			float accentW = AccentWidthForTier( entry.Tier );
			RectTransform accentRect = item.Accent.transform as RectTransform;
			if ( accentRect != null )
			{
				accentRect.anchorMin = new Vector2( 0f, 0f );
				accentRect.anchorMax = new Vector2( 0f, 1f );
				accentRect.pivot = new Vector2( 0f, 0.5f );
				accentRect.anchoredPosition = Vector2.zero;
				accentRect.sizeDelta = new Vector2( accentW, 0f );
			}
		}

		string message = entry.Message ?? string.Empty;
		SplitMessage( message, out string kicker, out string body );
		bool hasKicker = !string.IsNullOrEmpty( kicker );

		if ( item.Kicker != null )
		{
			item.Kicker.gameObject.SetActive( hasKicker );
			item.Kicker.text = kicker;
			item.Kicker.color = GoldKicker;
			item.Kicker.fontSize = entry.Tier == ToastTier.Milestone ? 18 : 16;
		}

		if ( item.Label != null )
		{
			item.Label.text = hasKicker ? body : message;
			item.Label.fontSize = BodyFontSize( entry.Tier );
			item.Label.color = Color.white;
		}

		Sprite icon = ResolveIcon( entry );
		ApplyIcon( item, icon, entry.Tier );
		ApplyLabelLayout( item, icon != null, hasKicker, entry.Tier );
	}

	static Color BackdropForTier( ToastTier tier )
	{
		switch ( tier )
		{
			case ToastTier.Completion:
				return CompletionBackdrop;
			case ToastTier.Unlock:
				return UnlockBackdrop;
			case ToastTier.Milestone:
				return MilestoneBackdrop;
			default:
				return DiscoveryBackdrop;
		}
	}

	static Color AccentForEntry( Entry entry )
	{
		if ( entry.Tier == ToastTier.Milestone )
			return MilestoneAccent;
		if ( entry.Tier == ToastTier.Unlock )
			return UnlockAccent;
		if ( entry.Kind != Kind.Discovery || !entry.ShowPouchIcon )
			return CoinAccent;

		switch ( entry.Pouch )
		{
			case CarryBucketKind.Gem:
				return GemAccent;
			case CarryBucketKind.Artifact:
				return ArtifactAccent;
			case CarryBucketKind.General:
			case CarryBucketKind.Junk:
				return GeneralAccent;
			default:
				return CoinAccent;
		}
	}

	static float AccentWidthForTier( ToastTier tier )
	{
		switch ( tier )
		{
			case ToastTier.Unlock:
				return AccentWidthUnlock;
			case ToastTier.Milestone:
				return AccentWidthMilestone;
			default:
				return AccentWidthDiscovery;
		}
	}

	static int BodyFontSize( ToastTier tier )
	{
		switch ( tier )
		{
			case ToastTier.Unlock:
				return 30;
			case ToastTier.Milestone:
				return 32;
			case ToastTier.Completion:
				return 28;
			default:
				return 26;
		}
	}

	static void SplitMessage( string message, out string kicker, out string body )
	{
		kicker = string.Empty;
		body = message;
		if ( string.IsNullOrEmpty( message ) )
			return;

		int colon = message.IndexOf( ": " );
		int emDash = message.IndexOf( " — " );
		int sep = -1;
		int sepLen = 0;

		if ( colon >= 0 && ( emDash < 0 || colon < emDash ) )
		{
			sep = colon;
			sepLen = 2;
		}
		else if ( emDash >= 0 )
		{
			sep = emDash;
			sepLen = 3;
		}

		if ( sep <= 0 )
			return;

		kicker = message.Substring( 0, sep ).Trim();
		body = message.Substring( sep + sepLen ).Trim();
		if ( string.IsNullOrEmpty( body ) )
		{
			kicker = string.Empty;
			body = message;
		}
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

	void ApplyIcon( ToastItem item, Sprite sprite, ToastTier tier )
	{
		if ( item.Icon == null )
			return;

		bool show = sprite != null;
		item.Icon.sprite = sprite;
		item.Icon.enabled = show;
		item.Icon.gameObject.SetActive( show );

		if ( !show )
			return;

		float size = IconSizeForTier( tier );
		RectTransform iconRect = item.Icon.transform as RectTransform;
		if ( iconRect == null )
			return;

		float accentW = AccentWidthForTier( tier );
		iconRect.anchorMin = new Vector2( 0f, 0.5f );
		iconRect.anchorMax = new Vector2( 0f, 0.5f );
		iconRect.pivot = new Vector2( 0f, 0.5f );
		iconRect.anchoredPosition = new Vector2( accentW + IconPadding, 0f );
		iconRect.sizeDelta = new Vector2( size, size );
	}

	static void ApplyLabelLayout( ToastItem item, bool iconVisible, bool hasKicker, ToastTier tier )
	{
		float accentW = AccentWidthForTier( tier );
		float iconSize = IconSizeForTier( tier );
		float left = accentW + IconPadding;
		if ( iconVisible )
			left += iconSize + IconPadding;
		else
			left += 4f;

		if ( item.Kicker != null )
		{
			RectTransform kickerRect = item.Kicker.transform as RectTransform;
			if ( kickerRect != null )
			{
				if ( hasKicker )
				{
					kickerRect.anchorMin = new Vector2( 0f, 0.55f );
					kickerRect.anchorMax = new Vector2( 1f, 1f );
					kickerRect.offsetMin = new Vector2( left, KickerGap );
					kickerRect.offsetMax = new Vector2( -TextRightPad, -6f );
					item.Kicker.alignment = TextAnchor.LowerLeft;
				}
			}
		}

		if ( item.Label == null )
			return;

		RectTransform labelRect = item.Label.transform as RectTransform;
		if ( labelRect == null )
			return;

		if ( hasKicker )
		{
			labelRect.anchorMin = new Vector2( 0f, 0f );
			labelRect.anchorMax = new Vector2( 1f, 0.58f );
			labelRect.offsetMin = new Vector2( left, 8f );
			labelRect.offsetMax = new Vector2( -TextRightPad, -KickerGap );
			item.Label.alignment = TextAnchor.UpperLeft;
		}
		else
		{
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2( left, 8f );
			labelRect.offsetMax = new Vector2( -TextRightPad, -8f );
			item.Label.alignment = iconVisible ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
		}
	}

	static void ApplySize( ToastItem item, float height )
	{
		if ( item.Slot != null )
		{
			item.Slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
			item.Slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
		}

		if ( item.Content != null )
		{
			item.Content.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
			item.Content.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
		}
	}

	void EnsureRoot()
	{
		RectTransform root = transform as RectTransform;
		if ( root == null )
			root = gameObject.AddComponent<RectTransform>();

		float height = MaxVisible * HeightForTier( ToastTier.Milestone ) + ( MaxVisible - 1 ) * Spacing;
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
		float height = HeightForTier( ToastTier.Discovery );

		GameObject slotGo = new GameObject( "ToastItem_" + index, typeof( RectTransform ) );
		slotGo.transform.SetParent( transform, false );

		RectTransform slot = slotGo.GetComponent<RectTransform>();
		slot.anchorMin = new Vector2( 0.5f, 1f );
		slot.anchorMax = new Vector2( 0.5f, 1f );
		slot.pivot = new Vector2( 0.5f, 1f );
		slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
		slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
		slot.anchoredPosition = new Vector2( 0f, -index * ( height + Spacing ) );

		GameObject contentGo = new GameObject( "Content", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( CanvasGroup ), typeof( Outline ) );
		contentGo.transform.SetParent( slot, false );

		RectTransform content = contentGo.GetComponent<RectTransform>();
		content.anchorMin = new Vector2( 0.5f, 1f );
		content.anchorMax = new Vector2( 0.5f, 1f );
		content.pivot = new Vector2( 0.5f, 1f );
		content.anchoredPosition = Vector2.zero;
		content.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastWidth );
		content.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );

		Image backdrop = contentGo.GetComponent<Image>();
		backdrop.color = DiscoveryBackdrop;
		backdrop.raycastTarget = false;

		Outline outline = contentGo.GetComponent<Outline>();
		outline.effectColor = GoldOutline;
		outline.effectDistance = new Vector2( 2f, 2f );
		outline.useGraphicAlpha = true;

		CanvasGroup group = contentGo.GetComponent<CanvasGroup>();
		group.blocksRaycasts = false;
		group.interactable = false;
		group.alpha = 0f;

		Image accent = CreateAccent( content );
		Image icon = CreateIcon( content );
		Text kicker = CreateKicker( content );
		Text label = CreateLabel( content );

		ToastItem item = new ToastItem
		{
			Slot = slot,
			Content = content,
			Group = group,
			Backdrop = backdrop,
			Outline = outline,
			Accent = accent,
			Icon = icon,
			Kicker = kicker,
			Label = label,
			Height = height,
			Tier = ToastTier.Discovery
		};

		EnsureItemFeedbacks( item );
		return item;
	}

	void EnsureItemFeedbacks( ToastItem item )
	{
		if ( item == null || item.Content == null )
			return;

		if ( item.DiscoveryShow == null && _discoveryShowTemplate != null )
			item.DiscoveryShow = CloneAndRetarget( _discoveryShowTemplate, item, _discoverySourceGroup, _discoverySourceRect, _discoverySourceAccent, _discoverySourceIcon, null, "DiscoveryShowFeedbacks" );
		if ( item.DiscoveryHide == null && _discoveryHideTemplate != null )
			item.DiscoveryHide = CloneAndRetarget( _discoveryHideTemplate, item, _discoverySourceGroup, _discoverySourceRect, _discoverySourceAccent, _discoverySourceIcon, null, "DiscoveryHideFeedbacks" );

		if ( item.CompletionShow == null && _completionShowTemplate != null )
			item.CompletionShow = CloneAndRetarget( _completionShowTemplate, item, _discoverySourceGroup, _discoverySourceRect, _discoverySourceAccent, _discoverySourceIcon, null, "CompletionShowFeedbacks" );
		if ( item.CompletionHide == null && _completionHideTemplate != null )
			item.CompletionHide = CloneAndRetarget( _completionHideTemplate, item, _discoverySourceGroup, _discoverySourceRect, _discoverySourceAccent, _discoverySourceIcon, null, "CompletionHideFeedbacks" );

		if ( item.UnlockShow == null && _unlockShowTemplate != null )
			item.UnlockShow = CloneAndRetarget( _unlockShowTemplate, item, _unlockSourceGroup, _unlockSourceRect, _unlockSourceAccent, _unlockSourceIcon, _unlockSourceBackdrop, "UnlockShowFeedbacks" );
		if ( item.UnlockHide == null && _unlockHideTemplate != null )
			item.UnlockHide = CloneAndRetarget( _unlockHideTemplate, item, _unlockSourceGroup, _unlockSourceRect, _unlockSourceAccent, _unlockSourceIcon, _unlockSourceBackdrop, "UnlockHideFeedbacks" );

		if ( item.MilestoneShow == null && _milestoneShowTemplate != null )
			item.MilestoneShow = CloneAndRetarget( _milestoneShowTemplate, item, _unlockSourceGroup, _unlockSourceRect, _unlockSourceAccent, _unlockSourceIcon, _unlockSourceBackdrop, "MilestoneShowFeedbacks" );
		if ( item.MilestoneHide == null && _milestoneHideTemplate != null )
			item.MilestoneHide = CloneAndRetarget( _milestoneHideTemplate, item, _unlockSourceGroup, _unlockSourceRect, _unlockSourceAccent, _unlockSourceIcon, _unlockSourceBackdrop, "MilestoneHideFeedbacks" );
	}

	Feedbacks ResolveShow( ToastItem item )
	{
		if ( item == null )
			return null;

		switch ( item.Tier )
		{
			case ToastTier.Completion:
				return item.CompletionShow != null ? item.CompletionShow : item.DiscoveryShow;
			case ToastTier.Unlock:
				return item.UnlockShow;
			case ToastTier.Milestone:
				return item.MilestoneShow != null ? item.MilestoneShow : item.UnlockShow;
			default:
				return item.DiscoveryShow;
		}
	}

	Feedbacks ResolveHide( ToastItem item )
	{
		if ( item == null )
			return null;

		switch ( item.Tier )
		{
			case ToastTier.Completion:
				return item.CompletionHide != null ? item.CompletionHide : item.DiscoveryHide;
			case ToastTier.Unlock:
				return item.UnlockHide;
			case ToastTier.Milestone:
				return item.MilestoneHide != null ? item.MilestoneHide : item.UnlockHide;
			default:
				return item.DiscoveryHide;
		}
	}

	static Feedbacks CloneAndRetarget( Feedbacks template, ToastItem item, CanvasGroup sourceGroup, RectTransform sourceRect, Image sourceAccent, Image sourceIcon, Image sourceBackdrop, string cloneName )
	{
		if ( template == null || item == null || item.Content == null )
			return null;

		Transform existing = item.Content.Find( cloneName );
		if ( existing != null )
			Destroy( existing.gameObject );

		GameObject cloneGo = Instantiate( template.gameObject, item.Content, false );
		cloneGo.name = cloneName;
		cloneGo.SetActive( true );

		Feedbacks clone = cloneGo.GetComponent<Feedbacks>();
		if ( clone == null )
			return null;

		RetargetFeedbacks( clone.FeedbackList, sourceGroup, sourceRect, sourceAccent, sourceIcon, sourceBackdrop, item );
		return clone;
	}

	static void RetargetFeedbacks( List<Feedback> list, CanvasGroup sourceGroup, RectTransform sourceRect, Image sourceAccent, Image sourceIcon, Image sourceBackdrop, ToastItem item )
	{
		if ( list == null || item == null )
			return;

		for ( int i = 0; i < list.Count; i++ )
		{
			Feedback feedback = list[ i ];
			if ( feedback == null )
				continue;

			CanvasGroupFadeFeedback fade = feedback as CanvasGroupFadeFeedback;
			if ( fade != null && ( sourceGroup == null || fade.Target == sourceGroup ) )
				fade.Target = item.Group;

			UiAnchoredSlideFeedback slide = feedback as UiAnchoredSlideFeedback;
			if ( slide != null && ( sourceRect == null || slide.Target == sourceRect ) )
				slide.Target = item.Content;

			UiPunchScaleFeedback punch = feedback as UiPunchScaleFeedback;
			if ( punch != null )
			{
				if ( sourceIcon != null && punch.Target == sourceIcon.transform )
					punch.Target = item.Icon != null ? item.Icon.transform : item.Content;
				else if ( sourceRect == null || punch.Target == sourceRect )
					punch.Target = item.Content;
			}

			UiPunchRotationFeedback rot = feedback as UiPunchRotationFeedback;
			if ( rot != null && ( sourceRect == null || rot.Target == sourceRect ) )
				rot.Target = item.Content;

			UiGraphicColorPunchFeedback colorPunch = feedback as UiGraphicColorPunchFeedback;
			if ( colorPunch != null )
			{
				if ( sourceAccent != null && colorPunch.Target == sourceAccent )
					colorPunch.Target = item.Accent;
				else if ( sourceBackdrop != null && colorPunch.Target == sourceBackdrop )
					colorPunch.Target = item.Backdrop;
				else if ( colorPunch.Target == null || colorPunch.Target == sourceBackdrop )
					colorPunch.Target = item.Accent != null ? item.Accent : item.Backdrop;
			}

			ParallelFeedback parallel = feedback as ParallelFeedback;
			if ( parallel != null )
				RetargetFeedbacks( parallel.Feedbacks, sourceGroup, sourceRect, sourceAccent, sourceIcon, sourceBackdrop, item );

			SequenceFeedback sequence = feedback as SequenceFeedback;
			if ( sequence != null )
				RetargetFeedbacks( sequence.Feedbacks, sourceGroup, sourceRect, sourceAccent, sourceIcon, sourceBackdrop, item );
		}
	}

	Image CreateAccent( RectTransform parent )
	{
		GameObject accentGo = new GameObject( "Accent", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		accentGo.transform.SetParent( parent, false );

		Image accent = accentGo.GetComponent<Image>();
		RectTransform accentRect = accent.transform as RectTransform;
		accentRect.anchorMin = new Vector2( 0f, 0f );
		accentRect.anchorMax = new Vector2( 0f, 1f );
		accentRect.pivot = new Vector2( 0f, 0.5f );
		accentRect.anchoredPosition = Vector2.zero;
		accentRect.sizeDelta = new Vector2( AccentWidthDiscovery, 0f );

		accent.color = CoinAccent;
		accent.raycastTarget = false;
		return accent;
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
		iconRect.anchoredPosition = new Vector2( AccentWidthDiscovery + IconPadding, 0f );
		iconRect.sizeDelta = new Vector2( IconSizeForTier( ToastTier.Discovery ), IconSizeForTier( ToastTier.Discovery ) );

		icon.color = Color.white;
		icon.raycastTarget = false;
		icon.preserveAspect = true;
		icon.enabled = false;
		iconGo.SetActive( false );
		return icon;
	}

	Text CreateKicker( RectTransform parent )
	{
		GameObject kickerGo = new GameObject( "Kicker", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		kickerGo.transform.SetParent( parent, false );

		Text kicker = kickerGo.GetComponent<Text>();
		kicker.font = ResolveFont();
		kicker.fontSize = 16;
		kicker.fontStyle = FontStyle.Bold;
		kicker.color = GoldKicker;
		kicker.horizontalOverflow = HorizontalWrapMode.Overflow;
		kicker.verticalOverflow = VerticalWrapMode.Overflow;
		kicker.raycastTarget = false;
		kicker.supportRichText = false;
		kicker.alignment = TextAnchor.LowerLeft;
		kickerGo.SetActive( false );
		return kicker;
	}

	Text CreateLabel( RectTransform parent )
	{
		GameObject labelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		labelGo.transform.SetParent( parent, false );

		Text label = labelGo.GetComponent<Text>();
		label.font = ResolveFont();
		label.fontSize = 26;
		label.fontStyle = FontStyle.Bold;
		label.color = Color.white;
		label.horizontalOverflow = HorizontalWrapMode.Overflow;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.raycastTarget = false;
		label.supportRichText = false;

		ToastItem layout = new ToastItem { Label = label };
		ApplyLabelLayout( layout, false, false, ToastTier.Discovery );
		return label;
	}

	Font ResolveFont()
	{
		if ( _labelFont != null )
			return _labelFont;
		Font font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		return font;
	}

	static void StopItemFeedbacks( ToastItem item )
	{
		if ( item == null )
			return;

		StopFeedback( item.DiscoveryShow );
		StopFeedback( item.DiscoveryHide );
		StopFeedback( item.CompletionShow );
		StopFeedback( item.CompletionHide );
		StopFeedback( item.UnlockShow );
		StopFeedback( item.UnlockHide );
		StopFeedback( item.MilestoneShow );
		StopFeedback( item.MilestoneHide );
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
