using System.Text;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Bottom-middle look-at prompts for every currently actionable situational control.
/// </summary>
public class InteractionContextUI : MonoBehaviour
{
	[Header( "Layout" )]
	[SerializeField] float bottomOffset = 96f;
	[SerializeField] int fontSize = 18;

	Text _label;
	bool _ready;
	readonly StringBuilder _builder = new StringBuilder( 192 );

	public void Setup()
	{
		EnsureUi();
		_ready = true;
		SetVisible( false );
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
		if ( _label == null )
			return;

		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
		{
			SetVisible( false );
			return;
		}

		PlayerController player = GameMode.Instance.Player;
		PlayerInteraction interaction = player.Interaction;
		PlayerPlacement placement = player.Placement;
		PlayerCarry carry = player.Carry;
		PlayerSorterReposition sorter = player.SorterReposition;
		PlayerMinecartPush minecartPush = player.MinecartPush;
		PlayerWholeStackInteraction wholeStack = player.WholeStack;

		InputController inputController = InputController.Instance;
		GameInput gameInput = inputController != null ? inputController.GameInput : null;
		if ( gameInput == null || interaction == null )
		{
			SetVisible( false );
			return;
		}

		_builder.Length = 0;

		if ( sorter != null && sorter.IsCarrying )
		{
			AppendSorterLines( gameInput, sorter );
			FinishRefresh();
			return;
		}

		if ( minecartPush != null && minecartPush.IsPushing )
		{
			AppendBound( gameInput.Interact, "Pushing (release to stop)" );
			FinishRefresh();
			return;
		}

		IInteractable focus = interaction.Current;
		if ( focus is CoinSortingCrankInteractable crankFocus && crankFocus.CanInteract( player ) )
			AppendBound( gameInput.Interact, "Hold to crank" );
		else if ( focus is CoinSortingStationMoveInteractable )
			AppendBound( gameInput.Interact, "Hold to move sorter" );
		else if ( focus != null && focus.CanInteract( player ) )
		{
			string primary = FormatPrimaryPrompt( focus, carry );
			if ( !string.IsNullOrEmpty( primary ) )
				AppendBound( gameInput.Interact, primary );
		}

		bool offerWholePickup = wholeStack != null && wholeStack.CanOfferWholeStackPickup;
		if ( offerWholePickup )
			AppendBound( gameInput.WholeStackPickup, "Hold to pick up stack" );

		bool carrying = carry != null && carry.Count > 0;
		if ( carrying && placement != null )
		{
			if ( wholeStack != null && wholeStack.CanOfferWholeStackPlace )
				AppendBound( gameInput.WholeStackPlace, "Hold to place stack" );

			AppendSecondaryLines( gameInput, placement );
		}

		bool sorterBusy = sorter != null && sorter.IsBusy;
		if ( carrying && !sorterBusy && carry.Count > 1 )
			AppendBound( gameInput.ScrollWheel, "Cycle held" );

		if ( carrying && !sorterBusy && HasOtherBucketItems( carry ) )
			AppendCategorySwitchLine( gameInput );

		if ( carrying && IsCleanBound( gameInput ) && TryGetDirtyActive( carry, out _ ) )
			AppendBound( gameInput.Clean, "Hold to polish" );

		FinishRefresh();
	}

	void AppendSorterLines( GameInput gameInput, PlayerSorterReposition sorter )
	{
		AppendBound( gameInput.SecondaryInteract, sorter.HasValidPlacement ? "Place sorter" : "Cannot place" );

		string rotateBinding = FormatRotateBinding( gameInput );
		if ( !string.IsNullOrEmpty( rotateBinding ) )
			AppendLine( rotateBinding, "Rotate" );
	}

	void AppendSecondaryLines( GameInput gameInput, PlayerPlacement placement )
	{
		SecondaryContextAction action = placement.GetSecondaryContextAction();
		switch ( action )
		{
			case SecondaryContextAction.Place:
				AppendBound( gameInput.SecondaryInteract, FormatPlacePrompt( placement ) );
				break;
			case SecondaryContextAction.Throw:
				AppendBound( gameInput.SecondaryInteract, "Throw" );
				break;
			case SecondaryContextAction.CannotPlace:
				AppendBound( gameInput.SecondaryInteract, "Cannot place" );
				break;
		}
	}

	static string FormatPlacePrompt( PlayerPlacement placement )
	{
		ITreasurePlacementTarget target = placement.ActiveTarget;
		if ( target is CleaningStationInteractable )
			return "Place to clean";
		if ( target is GemConstellationInteractable )
			return "Place gem";
		return "Place";
	}

	static string FormatPrimaryPrompt( IInteractable focus, PlayerCarry carry )
	{
		if ( focus == null )
			return null;

		if ( focus is MinecartInteractable )
		{
			bool carrying = carry != null && carry.Count > 0;
			return carrying ? "Load & push" : "Hold to push";
		}

		if ( focus is MinecartUnloadPoint )
			return "Unload minecart";

		if ( focus is CoinSortingCrankInteractable )
			return "Hold to crank";

		if ( focus is CoinSortingStationMoveInteractable )
			return "Hold to move sorter";

		if ( focus is TreasurePileInteractable )
		{
			string pileName = focus.InteractionName;
			return string.IsNullOrEmpty( pileName ) ? "Dig" : "Dig " + pileName;
		}

		if ( focus is GroundCoinStack || focus is CoinStackInteractable )
			return "Take coin";

		if ( focus is TreasureItemInteractable itemFocus )
		{
			TreasureItem item = itemFocus.Item;
			if ( item != null
				&& item.Definition != null
				&& item.Definition.category == TreasureCategory.Coin
				&& item.Owner is ITreasureDisplayStackOwner )
				return "Take coin";
		}

		if ( focus is ChestInteractable || focus is SkeletonKeyDisplayCase )
			return focus.InteractionName;

		if ( focus is TreasureItemInteractable || focus is PickupInteractable )
		{
			string itemName = focus.InteractionName;
			return string.IsNullOrEmpty( itemName ) ? "Pick up" : "Pick up " + itemName;
		}

		return focus.InteractionName;
	}

	void AppendCategorySwitchLine( GameInput gameInput )
	{
		if ( gameInput.CategorySlots == null || gameInput.CategorySlots.Length == 0 )
			return;

		int startLength = _builder.Length;
		if ( startLength > 0 )
			_builder.Append( '\n' );

		bool any = false;
		for ( int i = 0; i < gameInput.CategorySlots.Length; i++ )
		{
			string display = FormatBindingDisplay( gameInput.CategorySlots[ i ] );
			if ( string.IsNullOrEmpty( display ) )
				continue;

			if ( any )
				_builder.Append( ' ' );
			_builder.Append( '[' );
			_builder.Append( display );
			_builder.Append( ']' );
			any = true;
		}

		if ( !any )
		{
			_builder.Length = startLength;
			return;
		}

		_builder.Append( "  Switch pouch" );
	}

	static string FormatRotateBinding( GameInput gameInput )
	{
		string scroll = FormatBindingDisplay( gameInput.ScrollWheel );
		string left = FormatBindingDisplay( gameInput.RotateLeft );
		string right = FormatBindingDisplay( gameInput.RotateRight );

		StringBuilder rotate = new StringBuilder( 48 );
		if ( !string.IsNullOrEmpty( scroll ) )
			rotate.Append( '[' ).Append( scroll ).Append( ']' );

		if ( !string.IsNullOrEmpty( left ) || !string.IsNullOrEmpty( right ) )
		{
			if ( rotate.Length > 0 )
				rotate.Append( " / " );
			if ( !string.IsNullOrEmpty( left ) )
				rotate.Append( '[' ).Append( left ).Append( ']' );
			if ( !string.IsNullOrEmpty( left ) && !string.IsNullOrEmpty( right ) )
				rotate.Append( ' ' );
			if ( !string.IsNullOrEmpty( right ) )
				rotate.Append( '[' ).Append( right ).Append( ']' );
		}

		return rotate.Length > 0 ? rotate.ToString() : null;
	}

	void AppendBound( InputAction action, string prompt )
	{
		string binding = FormatBinding( action );
		if ( binding == null )
			return;

		AppendLine( binding, prompt );
	}

	void AppendLine( string binding, string action )
	{
		if ( _builder.Length > 0 )
			_builder.Append( '\n' );
		_builder.Append( binding );
		_builder.Append( "  " );
		_builder.Append( action );
	}

	void FinishRefresh()
	{
		if ( _builder.Length == 0 )
		{
			SetVisible( false );
			return;
		}

		_label.text = _builder.ToString();
		SetVisible( true );
	}

	static string FormatBinding( InputAction action )
	{
		string display = FormatBindingDisplay( action );
		if ( string.IsNullOrEmpty( display ) )
			return null;

		return "[" + display + "]";
	}

	static string FormatBindingDisplay( InputAction action )
	{
		if ( action == null || !HasBindings( action ) )
			return null;

		string display = action.GetBindingDisplayString();
		if ( string.IsNullOrEmpty( display ) )
			return null;

		// Prefer the first device segment when multi-scheme strings are joined.
		int pipe = display.IndexOf( '|' );
		if ( pipe >= 0 )
			display = display.Substring( 0, pipe ).Trim();

		return string.IsNullOrEmpty( display ) ? null : display;
	}

	static bool HasBindings( InputAction action )
	{
		if ( action == null )
			return false;

		var bindings = action.bindings;
		for ( int i = 0; i < bindings.Count; i++ )
		{
			if ( !bindings[ i ].isComposite && !string.IsNullOrEmpty( bindings[ i ].effectivePath ) )
				return true;
		}

		return false;
	}

	static bool IsCleanBound( GameInput gameInput )
	{
		return gameInput != null && HasBindings( gameInput.Clean );
	}

	static bool HasOtherBucketItems( PlayerCarry carry )
	{
		if ( carry == null )
			return false;

		CarryBucketKind selected = carry.SelectedBucket;
		for ( int i = 0; i < PlayerCarry.BucketCount; i++ )
		{
			CarryBucketKind kind = (CarryBucketKind)i;
			if ( kind == selected )
				continue;
			if ( carry.GetBucketCount( kind ) > 0 )
				return true;
		}

		return false;
	}

	static bool TryGetDirtyActive( PlayerCarry carry, out TreasureItem item )
	{
		item = null;
		if ( carry == null || !carry.TryPeekActive( out item ) || item == null )
			return false;
		return item.IsDirty;
	}

	void SetVisible( bool visible )
	{
		EnsureUi();
		if ( _label != null )
			_label.enabled = visible;
	}

	void EnsureUi()
	{
		if ( _label != null )
			return;

		_label = GetComponentInChildren<Text>( true );
		if ( _label != null )
			return;

		GameObject go = new GameObject( "InteractionContext", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		go.transform.SetParent( transform, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0f );
		rect.anchorMax = new Vector2( 0.5f, 0f );
		rect.pivot = new Vector2( 0.5f, 0f );
		rect.anchoredPosition = new Vector2( 0f, bottomOffset );
		rect.sizeDelta = new Vector2( 640f, 110f );

		_label = go.GetComponent<Text>();
		_label.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( _label.font == null )
			_label.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		_label.fontSize = fontSize;
		_label.fontStyle = FontStyle.Bold;
		_label.alignment = TextAnchor.LowerCenter;
		_label.color = new Color( 1f, 1f, 1f, 0.92f );
		_label.raycastTarget = false;
		_label.horizontalOverflow = HorizontalWrapMode.Overflow;
		_label.verticalOverflow = VerticalWrapMode.Overflow;
		_label.supportRichText = false;
	}
}
