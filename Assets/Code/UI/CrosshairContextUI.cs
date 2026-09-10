using System.Text;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Look-at text under the crosshair (display fill, sorter timer; optional stack counts)
/// and a glide/slide icon above it.
/// </summary>
public class CrosshairContextUI : MonoBehaviour
{
	[Header( "Look-at" )]
	[SerializeField] Text _lookLabel;
	[SerializeField] int fontSize = 20;
	[SerializeField] bool showCoinStackCount;

	[Header( "Movement" )]
	[SerializeField] Image _movementIcon;
	[SerializeField] Sprite _glideSprite;
	[SerializeField] Sprite _slideSprite;
	[SerializeField] float iconSize = 40f;
	[SerializeField] float iconOffset = 48f;
	[SerializeField] float lookOffset = 18f;

	readonly StringBuilder _builder = new StringBuilder( 256 );
	readonly DisplayRequirementUI _displayRequirements = new DisplayRequirementUI();
	bool _ready;

	public void Setup()
	{
		EnsureUi();
		ApplyLookLabelStyle();
		_ready = true;
		SetLookVisible( false );
		SetMovementVisible( false, null );
	}

	void Update()
	{
		if ( !_ready )
			return;

		Refresh();
	}

	void Refresh()
	{
		EnsureUi();

		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
		{
			SetLookVisible( false );
			SetMovementVisible( false, null );
			return;
		}

		PlayerController player = GameMode.Instance.Player;
		RefreshLook( player );
		RefreshMovement( player );
	}

	void RefreshLook( PlayerController player )
	{
		if ( _lookLabel == null )
			return;

		_builder.Length = 0;
		if ( showCoinStackCount )
			TryAppendCoinStack( player, _builder );
		_displayRequirements.TryAppendSummary( player, _builder );
		TryAppendSorterTimer( player, _builder );

		if ( _builder.Length == 0 )
		{
			SetLookVisible( false );
			return;
		}

		_lookLabel.text = _builder.ToString();
		SetLookVisible( true );
	}

	void RefreshMovement( PlayerController player )
	{
		if ( player.IsGliding )
		{
			SetMovementVisible( _glideSprite != null, _glideSprite );
			return;
		}

		if ( player.IsSliding )
		{
			SetMovementVisible( _slideSprite != null, _slideSprite );
			return;
		}

		SetMovementVisible( false, null );
	}

	static bool TryAppendCoinStack( PlayerController player, StringBuilder builder )
	{
		if ( !TryResolveCoinStackCount( player, out int count ) || count <= 0 )
			return false;

		AppendLine( builder, count.ToString() );
		return true;
	}

	static bool TryResolveCoinStackCount( PlayerController player, out int count )
	{
		count = 0;
		if ( player == null )
			return false;

		PlayerInteraction interaction = player.Interaction;
		if ( interaction != null )
		{
			if ( TryGetCoinStackCount( interaction.Current, out count ) )
				return true;

			IInteractable focus = interaction.Current;
			TreasureItemInteractable itemFocus = focus as TreasureItemInteractable;
			if ( itemFocus != null && TryGetCoinStackCountFromItem( itemFocus.Item, out count ) )
				return true;

			if ( interaction.TryGetLastHit( out RaycastHit lastHit )
				&& TryGetCoinStackCountFromCollider( lastHit.collider, out count ) )
				return true;

			if ( interaction.TryGetPlacementAimHit( out RaycastHit aimHit )
				&& TryGetCoinStackCountFromCollider( aimHit.collider, out count ) )
				return true;
		}

		return false;
	}

	static bool TryGetCoinStackCount( object target, out int count )
	{
		count = 0;
		if ( target == null )
			return false;

		GroundCoinStack groundStack = target as GroundCoinStack;
		if ( groundStack != null && groundStack.Count > 0 )
		{
			count = groundStack.Count;
			return true;
		}

		CoinStackInteractable coinStack = target as CoinStackInteractable;
		if ( coinStack != null && coinStack.CoinCount > 0 )
		{
			count = coinStack.CoinCount;
			return true;
		}

		TreasureItemInteractable itemFocus = target as TreasureItemInteractable;
		if ( itemFocus != null )
			return TryGetCoinStackCountFromItem( itemFocus.Item, out count );

		return false;
	}

	static bool TryGetCoinStackCountFromItem( TreasureItem item, out int count )
	{
		count = 0;
		if ( item == null )
			return false;

		if ( TryGetCoinStackCount( item.Owner, out count ) )
			return true;

		if ( item.Definition == null || item.Definition.category != TreasureCategory.Coin )
			return false;

		ITreasureDisplayStackOwner display = item.Owner as ITreasureDisplayStackOwner;
		if ( display == null || !display.TryGetSlotIndex( item, out int slotIndex ) )
			return false;

		int slotCount = display.GetSlotCount( slotIndex );
		if ( slotCount <= 0 )
			return false;

		count = slotCount;
		return true;
	}

	static bool TryGetCoinStackCountFromCollider( Collider collider, out int count )
	{
		count = 0;
		if ( collider == null )
			return false;

		GroundCoinStack groundStack = collider.GetComponentInParent<GroundCoinStack>();
		if ( groundStack != null && groundStack.Count > 0 )
		{
			count = groundStack.Count;
			return true;
		}

		CoinStackInteractable coinStack = collider.GetComponentInParent<CoinStackInteractable>();
		if ( coinStack != null && coinStack.CoinCount > 0 )
		{
			count = coinStack.CoinCount;
			return true;
		}

		TreasureItemInteractable itemFocus = collider.GetComponentInParent<TreasureItemInteractable>();
		if ( itemFocus != null )
			return TryGetCoinStackCountFromItem( itemFocus.Item, out count );

		TreasureItem item = collider.GetComponentInParent<TreasureItem>();
		if ( item != null )
			return TryGetCoinStackCountFromItem( item, out count );

		return false;
	}

	static bool TryAppendSorterTimer( PlayerController player, StringBuilder builder )
	{
		CoinSortingStation station = ResolveSorter( player );
		if ( station == null )
			return false;

		AppendLine( builder, station.ReserveSeconds.ToString( "0.0" ) + "s" );
		return true;
	}

	static CoinSortingStation ResolveSorter( PlayerController player )
	{
		if ( player == null )
			return null;

		PlayerInteraction interaction = player.Interaction;
		if ( interaction != null )
		{
			CoinSortingStation fromFocus = ResolveStationFromObject( interaction.Current );
			if ( fromFocus != null )
				return fromFocus;

			IInteractable focus = interaction.Current;
			TreasureItemInteractable itemFocus = focus as TreasureItemInteractable;
			if ( itemFocus != null )
			{
				CoinSortingStation fromItem = ResolveStationFromObject( itemFocus.Item );
				if ( fromItem == null && itemFocus.Item != null )
					fromItem = ResolveStationFromObject( itemFocus.Item.Owner );
				if ( fromItem != null )
					return fromItem;
			}

			if ( interaction.TryGetLastHit( out RaycastHit lastHit ) )
			{
				CoinSortingStation fromHit = ResolveStationFromCollider( lastHit.collider );
				if ( fromHit != null )
					return fromHit;
			}

			if ( interaction.TryGetPlacementAimHit( out RaycastHit aimHit ) )
			{
				CoinSortingStation fromAim = ResolveStationFromCollider( aimHit.collider );
				if ( fromAim != null )
					return fromAim;
			}
		}

		PlayerPlacement placement = player.Placement;
		if ( placement != null )
		{
			CoinSortingStation fromTarget = ResolveStationFromObject( placement.ActiveTarget );
			if ( fromTarget != null )
				return fromTarget;
		}

		return null;
	}

	static CoinSortingStation ResolveStationFromObject( object target )
	{
		if ( target == null )
			return null;

		CoinSortingStation station = target as CoinSortingStation;
		if ( station != null )
			return station;

		Component component = target as Component;
		if ( component != null )
		{
			station = component.GetComponentInParent<CoinSortingStation>();
			if ( station != null )
				return station;
		}

		GroundCoinStack stack = target as GroundCoinStack;
		if ( stack != null && stack.MachineStation != null )
			return stack.MachineStation;

		CoinSortingHopper hopper = target as CoinSortingHopper;
		if ( hopper != null && hopper.Station != null )
			return hopper.Station;

		TreasureItem item = target as TreasureItem;
		if ( item != null )
			return ResolveStationFromObject( item.Owner );

		return null;
	}

	static CoinSortingStation ResolveStationFromCollider( Collider collider )
	{
		if ( collider == null )
			return null;

		CoinSortingStation station = collider.GetComponentInParent<CoinSortingStation>();
		if ( station != null )
			return station;

		GroundCoinStack stack = collider.GetComponentInParent<GroundCoinStack>();
		if ( stack != null && stack.MachineStation != null )
			return stack.MachineStation;

		CoinSortingHopper hopper = collider.GetComponentInParent<CoinSortingHopper>();
		if ( hopper != null && hopper.Station != null )
			return hopper.Station;

		return null;
	}

	static void AppendLine( StringBuilder builder, string text )
	{
		if ( builder == null || string.IsNullOrEmpty( text ) )
			return;

		if ( builder.Length > 0 )
			builder.Append( '\n' );
		builder.Append( text );
	}

	void SetLookVisible( bool visible )
	{
		if ( _lookLabel != null )
			_lookLabel.enabled = visible;
	}

	void SetMovementVisible( bool visible, Sprite sprite )
	{
		if ( _movementIcon == null )
			return;

		_movementIcon.sprite = sprite;
		_movementIcon.enabled = visible && sprite != null;
	}

	void EnsureUi()
	{
		EnsureLookLabel();
		EnsureMovementIcon();
	}

	void EnsureLookLabel()
	{
		if ( _lookLabel != null )
			return;

		Transform existing = transform.Find( "DisplayRequirements" );
		if ( existing != null )
		{
			Transform labelTransform = existing.Find( "Label" );
			if ( labelTransform != null )
				_lookLabel = labelTransform.GetComponent<Text>();
			if ( _lookLabel == null )
				_lookLabel = existing.GetComponentInChildren<Text>( true );
		}

		if ( _lookLabel != null )
		{
			ApplyLookLabelStyle();
			return;
		}

		GameObject rootGo = new GameObject( "DisplayRequirements", typeof( RectTransform ) );
		rootGo.transform.SetParent( transform, false );
		RectTransform root = rootGo.GetComponent<RectTransform>();
		root.anchorMin = new Vector2( 0.5f, 0.5f );
		root.anchorMax = new Vector2( 0.5f, 0.5f );
		root.pivot = new Vector2( 0.5f, 1f );
		root.anchoredPosition = new Vector2( 0f, -lookOffset );
		root.sizeDelta = new Vector2( 400f, 80f );

		GameObject labelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		labelGo.transform.SetParent( root, false );
		RectTransform labelRect = labelGo.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;

		_lookLabel = labelGo.GetComponent<Text>();
		ApplyLookLabelStyle();
	}

	void EnsureMovementIcon()
	{
		if ( _movementIcon != null )
			return;

		Transform existing = transform.Find( "MovementIcon" );
		if ( existing != null )
			_movementIcon = existing.GetComponent<Image>();

		if ( _movementIcon != null )
		{
			_movementIcon.raycastTarget = false;
			_movementIcon.preserveAspect = true;
			return;
		}

		GameObject go = new GameObject( "MovementIcon", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		go.transform.SetParent( transform, false );
		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = new Vector2( 0f, iconOffset );
		rect.sizeDelta = new Vector2( iconSize, iconSize );

		_movementIcon = go.GetComponent<Image>();
		_movementIcon.raycastTarget = false;
		_movementIcon.preserveAspect = true;
		_movementIcon.enabled = false;
	}

	void ApplyLookLabelStyle()
	{
		if ( _lookLabel == null )
			return;

		if ( _lookLabel.font == null )
		{
			_lookLabel.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
			if ( _lookLabel.font == null )
				_lookLabel.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		}

		_lookLabel.fontSize = fontSize;
		_lookLabel.fontStyle = FontStyle.Bold;
		_lookLabel.alignment = TextAnchor.UpperCenter;
		_lookLabel.color = new Color( 1f, 1f, 1f, 0.92f );
		_lookLabel.raycastTarget = false;
		_lookLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
		_lookLabel.verticalOverflow = VerticalWrapMode.Overflow;
		_lookLabel.supportRichText = false;
	}
}
