using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Bottom-center pouch HUD: icon, binding, and label per pouch, plus the cycle control.
/// Hidden until the player enters <see cref="EventSceneAutoWire.IdVolumeMainCave"/>, then stays visible.
/// Wire children on the Interface prefab; assign pouch icons on each slot Image.
/// Assign <see cref="newItemStarSprite"/> (or each slot's NewItem/Star Image) for the new-item badge.
/// </summary>
public class PouchBarUI : MonoBehaviour
{
	const float UnselectedAlpha = 0.4f;

	[System.Serializable]
	public class Slot
	{
		public CanvasGroup root;
		public Image icon;
		public Text binding;
		public Text label;
		public RectTransform newItemRoot;
		public Image newItemIcon;
		public Text newItemCount;
	}

	[SerializeField] Slot[] slots = new Slot[ PlayerCarry.BucketCount ];
	[SerializeField] Text cycleBinding;
	[SerializeField] Text cycleLabel;
	[Tooltip( "Star shown top-right of each pouch when it holds newly discovered types. Leave empty to assign per-slot Star images on the prefab." )]
	[SerializeField] Sprite newItemStarSprite;

	public static PouchBarUI Instance { get; private set; }

	bool _ready;
	bool _unlocked;
	bool _subscribed;
	CanvasGroup _group;

	static readonly string[] SlotNames = { "SlotCoin", "SlotGem", "SlotArtifact", "SlotGeneral" };
	static readonly string[] SlotLabels = { "Coins", "Gems", "Artifacts", "General" };

	public void Setup()
	{
		Instance = this;
		EnsureUi();
		BindChildren();
		CacheGroup();
		SetBarVisible( false );
		RefreshBindings();
		RefreshState();
		Subscribe();
		_ready = true;
		TryUnlockFromOverlap();
	}

	/// <summary>Pouch HUD sprite for the given carry bucket, or null if unwired.</summary>
	public Sprite GetSlotIcon( CarryBucketKind kind )
	{
		int index = (int)kind;
		if ( slots == null || index < 0 || index >= slots.Length )
			return null;

		Slot slot = slots[ index ];
		if ( slot == null || slot.icon == null )
			return null;

		return slot.icon.sprite;
	}

	void OnEnable()
	{
		Instance = this;
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		Unsubscribe();
		if ( Instance == this )
			Instance = null;
	}

	void Update()
	{
		if ( !_ready )
			return;

		if ( !_unlocked )
			TryUnlockFromOverlap();
		else
			RefreshState();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<VolumeEnteredEvent>( OnVolumeEntered );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		_subscribed = false;
	}

	void OnVolumeEntered( VolumeEnteredEvent evt )
	{
		if ( evt.VolumeId != EventSceneAutoWire.IdVolumeMainCave )
			return;
		Unlock();
	}

	void TryUnlockFromOverlap()
	{
		if ( _unlocked )
			return;
		if ( !EventTargetRegistry.TryGetVolume( EventSceneAutoWire.IdVolumeMainCave, out QuestVolume volume ) )
			return;
		if ( volume == null )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return;

		Collider col = volume.GetComponent<Collider>();
		if ( col == null )
			return;

		if ( !col.bounds.Contains( player.transform.position ) )
			return;

		Unlock();
	}

	void Unlock()
	{
		if ( _unlocked )
			return;

		_unlocked = true;
		SetBarVisible( true );
		RefreshState();
	}

	void CacheGroup()
	{
		if ( _group == null )
			_group = GetComponent<CanvasGroup>();
		if ( _group == null )
			_group = gameObject.AddComponent<CanvasGroup>();
		_group.blocksRaycasts = false;
		_group.interactable = false;
	}

	void SetBarVisible( bool visible )
	{
		CacheGroup();
		if ( _group != null )
			_group.alpha = visible ? 1f : 0f;
	}

	void RefreshState()
	{
		PlayerCarry carry = GameMode.Instance != null && GameMode.Instance.Player != null
			? GameMode.Instance.Player.Carry
			: null;

		CarryBucketKind selected = carry != null ? carry.SelectedBucket : CarryBucketKind.Coin;
		int count = slots != null ? slots.Length : 0;
		if ( count > PlayerCarry.BucketCount )
			count = PlayerCarry.BucketCount;

		for ( int i = 0; i < count; i++ )
			ApplySlot( i, selected, carry );
	}

	void ApplySlot( int index, CarryBucketKind selected, PlayerCarry carry )
	{
		if ( slots == null || index < 0 || index >= slots.Length )
			return;

		Slot slot = slots[ index ];
		if ( slot == null || slot.root == null )
			return;

		CarryBucketKind kind = (CarryBucketKind)index;
		int held = carry != null ? carry.GetBucketCount( kind ) : 0;
		bool visible = kind != CarryBucketKind.General || held > 0;
		if ( slot.root.gameObject.activeSelf != visible )
			slot.root.gameObject.SetActive( visible );

		if ( !visible )
			return;

		bool isSelected = selected == kind;
		slot.root.alpha = isSelected ? 1f : UnselectedAlpha;
		slot.root.blocksRaycasts = false;
		slot.root.interactable = false;

		int newCount = ( carry != null && !isSelected ) ? carry.GetNewItemTypeCount( kind ) : 0;
		ApplyNewItemBadge( slot, newCount );
	}

	void ApplyNewItemBadge( Slot slot, int newCount )
	{
		bool show = newCount > 0;
		if ( slot.newItemRoot != null && slot.newItemRoot.gameObject.activeSelf != show )
			slot.newItemRoot.gameObject.SetActive( show );

		if ( !show )
			return;

		if ( slot.newItemCount != null )
			slot.newItemCount.text = "x" + newCount;

		if ( slot.newItemIcon != null )
		{
			if ( slot.newItemIcon.sprite == null && newItemStarSprite != null )
				slot.newItemIcon.sprite = newItemStarSprite;
			bool hasSprite = slot.newItemIcon.sprite != null;
			slot.newItemIcon.enabled = hasSprite;
			slot.newItemIcon.gameObject.SetActive( hasSprite );
		}
	}

	void RefreshBindings()
	{
		InputController inputController = InputController.Instance;
		GameInput gameInput = inputController != null ? inputController.GameInput : null;

		if ( slots != null )
		{
			int count = slots.Length;
			if ( count > PlayerCarry.BucketCount )
				count = PlayerCarry.BucketCount;

			for ( int i = 0; i < count; i++ )
			{
				Slot slot = slots[ i ];
				if ( slot == null || slot.binding == null )
					continue;

				string display = null;
				if ( gameInput != null && gameInput.CategorySlots != null && i < gameInput.CategorySlots.Length )
					display = FormatBindingDisplay( gameInput.CategorySlots[ i ] );
				if ( string.IsNullOrEmpty( display ) && i < GameInput.CategorySlotCount )
					display = ( i + 1 ).ToString();

				slot.binding.text = string.IsNullOrEmpty( display ) ? "" : "[" + display + "]";
			}
		}

		if ( cycleBinding != null )
		{
			string cycleDisplay = gameInput != null ? FormatBindingDisplay( gameInput.CyclePouch ) : null;
			if ( string.IsNullOrEmpty( cycleDisplay ) )
				cycleDisplay = "Q";
			cycleBinding.text = "[" + cycleDisplay + "]";
		}

		if ( cycleLabel != null && string.IsNullOrEmpty( cycleLabel.text ) )
			cycleLabel.text = "Cycle";
	}

	void BindChildren()
	{
		if ( slots == null || slots.Length != PlayerCarry.BucketCount )
			slots = new Slot[ PlayerCarry.BucketCount ];

		for ( int i = 0; i < PlayerCarry.BucketCount; i++ )
		{
			if ( slots[ i ] == null )
				slots[ i ] = new Slot();

			Transform slotT = transform.Find( SlotNames[ i ] );
			if ( slotT == null )
				continue;

			Slot slot = slots[ i ];
			if ( slot.root == null )
			{
				slot.root = slotT.GetComponent<CanvasGroup>();
				if ( slot.root == null )
					slot.root = slotT.gameObject.AddComponent<CanvasGroup>();
			}

			Transform selectedT = slotT.Find( "Selected" );
			if ( selectedT != null )
				selectedT.gameObject.SetActive( false );

			if ( slot.icon == null )
				slot.icon = FindChildImage( slotT, "Icon" );
			if ( slot.binding == null )
				slot.binding = FindChildText( slotT, "Binding" );
			if ( slot.label == null )
				slot.label = FindChildText( slotT, "Label" );

			EnsureNewItem( slot );

			if ( slot.label != null && string.IsNullOrEmpty( slot.label.text ) )
				slot.label.text = SlotLabels[ i ];
		}

		Transform cycleT = transform.Find( "Cycle" );
		if ( cycleT != null )
		{
			if ( cycleBinding == null )
				cycleBinding = FindChildText( cycleT, "Binding" );
			if ( cycleLabel == null )
				cycleLabel = FindChildText( cycleT, "Label" );
		}

		OrderSlots();
	}

	void OrderSlots()
	{
		for ( int i = 0; i < SlotNames.Length; i++ )
		{
			Transform slotT = transform.Find( SlotNames[ i ] );
			if ( slotT != null )
				slotT.SetSiblingIndex( i );
		}

		Transform cycleT = transform.Find( "Cycle" );
		if ( cycleT != null )
			cycleT.SetAsLastSibling();
	}

	void EnsureUi()
	{
		RectTransform rect = GetComponent<RectTransform>();
		if ( rect == null )
			rect = gameObject.AddComponent<RectTransform>();

		EnsureLayout( rect );

		CanvasGroup group = GetComponent<CanvasGroup>();
		if ( group == null )
			group = gameObject.AddComponent<CanvasGroup>();
		group.blocksRaycasts = false;
		group.interactable = false;

		if ( slots == null || slots.Length != PlayerCarry.BucketCount )
			slots = new Slot[ PlayerCarry.BucketCount ];

		for ( int i = 0; i < PlayerCarry.BucketCount; i++ )
		{
			if ( transform.Find( SlotNames[ i ] ) != null )
				continue;

			slots[ i ] = CreateSlot( SlotNames[ i ], SlotLabels[ i ] );
			if ( i == (int)CarryBucketKind.General )
				slots[ i ].root.gameObject.SetActive( false );
		}

		if ( transform.Find( "Cycle" ) == null )
			CreateCycle();

		OrderSlots();
	}

	void EnsureLayout( RectTransform rect )
	{
		if ( rect != null && transform.childCount == 0 )
		{
			rect.anchorMin = new Vector2( 0.5f, 0f );
			rect.anchorMax = new Vector2( 0.5f, 0f );
			rect.pivot = new Vector2( 0.5f, 0f );
			rect.anchoredPosition = new Vector2( 0f, 16f );
			rect.sizeDelta = new Vector2( 720f, 140f );
		}

		HorizontalLayoutGroup layout = GetComponent<HorizontalLayoutGroup>();
		if ( layout == null )
			layout = gameObject.AddComponent<HorizontalLayoutGroup>();
		layout.spacing = 8f;
		layout.childAlignment = TextAnchor.MiddleCenter;
		layout.childControlWidth = false;
		layout.childControlHeight = false;
		layout.childForceExpandWidth = false;
		layout.childForceExpandHeight = false;
		layout.padding = new RectOffset( 12, 12, 8, 8 );
	}

	Slot CreateSlot( string name, string labelText )
	{
		GameObject slotGo = new GameObject( name, typeof( RectTransform ), typeof( CanvasGroup ) );
		slotGo.transform.SetParent( transform, false );
		RectTransform slotRect = slotGo.GetComponent<RectTransform>();
		slotRect.sizeDelta = new Vector2( 120f, 128f );

		CanvasGroup slotGroup = slotGo.GetComponent<CanvasGroup>();
		slotGroup.blocksRaycasts = false;
		slotGroup.interactable = false;

		Image icon = CreateImage( slotGo.transform, "Icon", new Vector2( 0.5f, 1f ), new Vector2( 0.5f, 1f ), new Vector2( 0f, -8f ), new Vector2( 64f, 64f ), Color.white );
		icon.preserveAspect = true;

		Text binding = CreateText( slotGo.transform, "Binding", "", new Vector2( 0.5f, 0.5f ), new Vector2( 0f, -22f ), new Vector2( 120f, 24f ), 18 );
		Text label = CreateText( slotGo.transform, "Label", labelText, new Vector2( 0.5f, 0f ), new Vector2( 0f, 8f ), new Vector2( 120f, 24f ), 16 );

		Slot slot = new Slot
		{
			root = slotGroup,
			icon = icon,
			binding = binding,
			label = label
		};
		EnsureNewItem( slot );
		return slot;
	}

	void EnsureNewItem( Slot slot )
	{
		if ( slot == null || slot.icon == null )
			return;

		Transform iconT = slot.icon.transform;
		Transform rootT = iconT.Find( "NewItem" );
		if ( rootT == null && slot.newItemRoot != null )
			rootT = slot.newItemRoot;

		if ( rootT == null )
		{
			GameObject rootGo = new GameObject( "NewItem", typeof( RectTransform ) );
			rootGo.transform.SetParent( iconT, false );
			rootT = rootGo.transform;
			RectTransform rootRect = rootGo.GetComponent<RectTransform>();
			rootRect.anchorMin = new Vector2( 1f, 1f );
			rootRect.anchorMax = new Vector2( 1f, 1f );
			rootRect.pivot = new Vector2( 0f, 1f );
			rootRect.anchoredPosition = new Vector2( 2f, 8f );
			rootRect.sizeDelta = new Vector2( 56f, 20f );
			rootGo.SetActive( false );
		}

		slot.newItemRoot = rootT as RectTransform;
		if ( slot.newItemIcon == null )
			slot.newItemIcon = FindChildImage( rootT, "Star" );
		if ( slot.newItemCount == null )
			slot.newItemCount = FindChildText( rootT, "Count" );

		if ( slot.newItemIcon == null )
		{
			slot.newItemIcon = CreateImage( rootT, "Star", new Vector2( 0f, 0.5f ), new Vector2( 0f, 0.5f ), new Vector2( 9f, 0f ), new Vector2( 18f, 18f ), Color.white );
			slot.newItemIcon.preserveAspect = true;
			RectTransform starRect = slot.newItemIcon.transform as RectTransform;
			if ( starRect != null )
				starRect.pivot = new Vector2( 0.5f, 0.5f );
		}

		if ( slot.newItemCount == null )
		{
			slot.newItemCount = CreateText( rootT, "Count", "x1", new Vector2( 0f, 0.5f ), new Vector2( 38f, 0f ), new Vector2( 36f, 20f ), 14 );
			slot.newItemCount.alignment = TextAnchor.MiddleLeft;
		}

		if ( slot.newItemIcon.sprite == null && newItemStarSprite != null )
			slot.newItemIcon.sprite = newItemStarSprite;
	}

	void CreateCycle()
	{
		GameObject cycleGo = new GameObject( "Cycle", typeof( RectTransform ) );
		cycleGo.transform.SetParent( transform, false );
		RectTransform cycleRect = cycleGo.GetComponent<RectTransform>();
		cycleRect.sizeDelta = new Vector2( 110f, 128f );

		cycleBinding = CreateText( cycleGo.transform, "Binding", "[Q]", new Vector2( 0.5f, 0.5f ), new Vector2( 0f, 12f ), new Vector2( 110f, 28f ), 20 );
		cycleLabel = CreateText( cycleGo.transform, "Label", "Cycle", new Vector2( 0.5f, 0.5f ), new Vector2( 0f, -16f ), new Vector2( 110f, 24f ), 16 );
	}

	static Image CreateImage( Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Color color )
	{
		GameObject go = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		go.transform.SetParent( parent, false );
		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = anchorMin;
		rect.anchorMax = anchorMax;
		rect.pivot = new Vector2( 0.5f, 0.5f );
		if ( anchorMin == Vector2.zero && anchorMax == Vector2.one )
		{
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			rect.pivot = new Vector2( 0.5f, 0.5f );
		}
		else
		{
			rect.pivot = anchorMin.y >= 0.99f ? new Vector2( 0.5f, 1f ) : new Vector2( 0.5f, 0.5f );
			rect.anchoredPosition = pos;
			rect.sizeDelta = size;
		}

		Image image = go.GetComponent<Image>();
		image.color = color;
		image.raycastTarget = false;
		image.sprite = null;
		return image;
	}

	static Text CreateText( Transform parent, string name, string value, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize )
	{
		GameObject go = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		go.transform.SetParent( parent, false );
		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = anchor;
		rect.anchorMax = anchor;
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = pos;
		rect.sizeDelta = size;

		Text text = go.GetComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( text.font == null )
			text.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		text.fontSize = fontSize;
		text.fontStyle = FontStyle.Bold;
		text.alignment = TextAnchor.MiddleCenter;
		text.color = new Color( 1f, 1f, 1f, 0.95f );
		text.raycastTarget = false;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.supportRichText = false;
		text.text = value;
		return text;
	}

	static Image FindChildImage( Transform parent, string childName )
	{
		Transform child = parent.Find( childName );
		return child != null ? child.GetComponent<Image>() : null;
	}

	static Text FindChildText( Transform parent, string childName )
	{
		Transform child = parent.Find( childName );
		return child != null ? child.GetComponent<Text>() : null;
	}

	static string FormatBindingDisplay( InputAction action )
	{
		if ( action == null )
			return null;

		var bindings = action.bindings;
		bool has = false;
		for ( int i = 0; i < bindings.Count; i++ )
		{
			if ( !bindings[ i ].isComposite && !string.IsNullOrEmpty( bindings[ i ].effectivePath ) )
			{
				has = true;
				break;
			}
		}

		if ( !has )
			return null;

		string display = action.GetBindingDisplayString();
		if ( string.IsNullOrEmpty( display ) )
			return null;

		int pipe = display.IndexOf( '|' );
		if ( pipe >= 0 )
			display = display.Substring( 0, pipe ).Trim();

		return string.IsNullOrEmpty( display ) ? null : display;
	}
}
