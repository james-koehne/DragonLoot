using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Movable treasure container: grid cargo bed, weight limit, hold-Interact push, unload-point dump.
/// Scene setup: Rigidbody (kinematic) + colliders on this object; optional <see cref="cargoRoot"/>;
/// assign <see cref="MinecartDefinition"/>. Create via Dragon Loot → Create Minecart Setup.
/// </summary>
[RequireComponent( typeof( Collider ) )]
[RequireComponent( typeof( Rigidbody ) )]
public class MinecartInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget
{
	sealed class CargoStack
	{
		public int OriginX;
		public int OriginY;
		public Vector2Int Footprint;
		public TreasureDefinition Definition;
		public readonly List<TreasureItem> Items = new List<TreasureItem>();

		public int Count => Items.Count;
		public TreasureItem Top => Count > 0 ? Items[ Count - 1 ] : null;
	}

	static readonly List<TreasureItem> DumpBuffer = new List<TreasureItem>( 32 );
	static readonly List<CargoStack> UnloadBuffer = new List<CargoStack>( 32 );

	[SerializeField]
	MinecartDefinition definition;

	[Tooltip( "Local parent for cargo poses. Defaults to this transform." )]
	[SerializeField]
	Transform cargoRoot;

	Rigidbody _body;
	CargoStack[ , ] _cellStacks;
	readonly List<CargoStack> _stacks = new List<CargoStack>();
	readonly List<TreasureItem> _allItems = new List<TreasureItem>();
	int _currentWeight;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.Minecart;

	public MinecartDefinition Definition => definition;

	public int CurrentWeight => _currentWeight;

	public int MaxWeight => definition != null ? Mathf.Max( 1, definition.maxWeight ) : 40;

	public int ItemCount => _allItems.Count;

	public IReadOnlyList<TreasureItem> StoredItems => _allItems;

	public float PushAttachRadius => definition != null ? definition.pushAttachRadius : 2.5f;

	public float EmptyPushSpeed => definition != null ? definition.emptyPushSpeed : 3.5f;

	public float FullPushSpeed => definition != null ? definition.fullPushSpeed : 1.25f;

	public float UnloadScatterRadius => definition != null ? definition.unloadScatterRadius : 0.35f;

	int GridColumns => definition != null ? Mathf.Max( 1, definition.gridColumns ) : 4;

	int GridRows => definition != null ? Mathf.Max( 1, definition.gridRows ) : 3;

	float CellSpacing => definition != null ? Mathf.Max( 0.05f, definition.cellSpacing ) : 0.22f;

	int MaxStackPerCell => definition != null ? Mathf.Max( 0, definition.maxStackPerCell ) : 0;

	void Reset()
	{
		SetInteractionName( "Minecart" );
	}

	void Awake()
	{
		_body = GetComponent<Rigidbody>();
		if ( _body != null )
		{
			_body.isKinematic = true;
			_body.useGravity = false;
			_body.interpolation = RigidbodyInterpolation.Interpolate;
		}

		EnsureCargoRoot();
		RebuildGrid();
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Minecart" );
	}

	void OnValidate()
	{
		EnsureCargoRoot();
	}

	void EnsureCargoRoot()
	{
		if ( cargoRoot == null )
			cargoRoot = transform;
	}

	void RebuildGrid()
	{
		int cols = GridColumns;
		int rows = GridRows;
		_cellStacks = new CargoStack[ cols, rows ];
		_stacks.Clear();
		_allItems.Clear();
		_currentWeight = 0;
	}

	public override bool CanInteract( PlayerController player )
	{
		return IsAvailable && player != null;
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null )
			return;

		PlayerCarry carry = player.Carry;
		if ( carry != null && carry.Count > 0 )
			TryDumpCarry( carry );

		PlayerMinecartPush push = player.MinecartPush;
		if ( push != null )
			push.BeginPush( this );
	}

	/// <summary>Best-effort dump of carried items into free grid/weight capacity.</summary>
	public int TryDumpCarry( PlayerCarry carry )
	{
		if ( carry == null || carry.Count <= 0 )
			return 0;

		carry.CopyCarriedItemsInOrder( DumpBuffer );
		int placed = 0;
		for ( int i = 0; i < DumpBuffer.Count; i++ )
		{
			TreasureItem item = DumpBuffer[ i ];
			if ( item == null )
				continue;

			if ( !TryPlaceDetached( item, carry ) )
				continue;

			placed++;
		}

		DumpBuffer.Clear();
		return placed;
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || !IsAvailable || item.Definition == null )
			return false;

		PlayerCarry carry = query.Player != null ? query.Player.Carry : null;
		if ( carry == null || carry.Count <= 0 )
			return false;

		return CanAcceptDefinition( item.Definition, out _, out _, out _ );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null || item.Definition == null )
			return false;

		bool valid = CanAcceptDefinition( item.Definition, out int ox, out int oy, out int stackIndex );
		if ( !valid && !TryFindAnyOrigin( item.Definition, out ox, out oy, out stackIndex ) )
		{
			GetCellWorldPose( 0, 0, 0, item.Definition, out Vector3 fallbackPos, out Quaternion fallbackRot );
			preview.SetItemMesh( fallbackPos, fallbackRot, item.GetWorldScale(), false );
			return true;
		}

		GetCellWorldPose( ox, oy, stackIndex, item.Definition, out Vector3 pos, out Quaternion rot );
		preview.SetItemMesh( pos, rot, item.GetWorldScale(), valid );
		return true;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanPlace( item, in query ) )
			return false;

		PlayerController player = query.Player;
		if ( player == null )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return false;

		return TryPlaceDetached( item, carry );
	}

	bool TryPlaceDetached( TreasureItem item, PlayerCarry carry )
	{
		if ( item == null || item.Definition == null )
			return false;

		if ( !CanAcceptDefinition( item.Definition, out int ox, out int oy, out _ ) )
			return false;

		if ( carry != null && carry.ContainsItem( item ) )
		{
			if ( !carry.TryDetachItem( item ) )
				return false;
		}

		return CommitItem( item, ox, oy );
	}

	/// <summary>Accept an already-detached world item into the cart (tests / future loaders).</summary>
	public bool TryAcceptWorldItem( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		if ( !CanAcceptDefinition( item.Definition, out int ox, out int oy, out _ ) )
			return false;

		return CommitItem( item, ox, oy );
	}

	bool CommitItem( TreasureItem item, int ox, int oy )
	{
		TreasureDefinition def = item.Definition;
		Vector2Int footprint = def.GetCartGridSize();
		CargoStack stack = _cellStacks[ ox, oy ];
		if ( stack == null )
		{
			stack = new CargoStack
			{
				OriginX = ox,
				OriginY = oy,
				Footprint = footprint,
				Definition = def
			};
			_stacks.Add( stack );
			MarkFootprint( ox, oy, footprint, stack );
		}

		stack.Items.Add( item );
		if ( !_allItems.Contains( item ) )
			_allItems.Add( item );

		_currentWeight += GetItemWeight( def );
		GetCellWorldPose( ox, oy, stack.Items.Count - 1, def, out Vector3 pos, out Quaternion rot );
		item.EnterDisplayed( this, cargoRoot, pos, rot );
		return true;
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null )
			return;

		for ( int s = 0; s < _stacks.Count; s++ )
		{
			CargoStack stack = _stacks[ s ];
			int index = stack.Items.IndexOf( item );
			if ( index < 0 )
				continue;

			stack.Items.RemoveAt( index );
			_allItems.Remove( item );
			_currentWeight = Mathf.Max( 0, _currentWeight - GetItemWeight( item.Definition ) );

			if ( stack.Items.Count == 0 )
			{
				ClearFootprint( stack );
				_stacks.RemoveAt( s );
			}
			else
			{
				RestackVisuals( stack );
			}

			return;
		}
	}

	/// <summary>
	/// Clears cargo occupancy and returns items. Items still own this cart until
	/// <see cref="TreasureItem.EnterSurface"/> / physics transfer calls <see cref="ReleaseTreasure"/>.
	/// </summary>
	public void ExtractAllCargo( List<TreasureItem> destination )
	{
		if ( destination == null )
			return;

		destination.Clear();
		UnloadBuffer.Clear();
		UnloadBuffer.AddRange( _stacks );

		for ( int s = 0; s < UnloadBuffer.Count; s++ )
		{
			CargoStack stack = UnloadBuffer[ s ];
			for ( int i = 0; i < stack.Items.Count; i++ )
			{
				TreasureItem item = stack.Items[ i ];
				if ( item != null )
					destination.Add( item );
			}

			ClearFootprint( stack );
		}

		_stacks.Clear();
		_allItems.Clear();
		_currentWeight = 0;
		UnloadBuffer.Clear();
	}

	public float GetPushSpeed()
	{
		int max = MaxWeight;
		float t = max > 0 ? Mathf.Clamp01( (float)_currentWeight / max ) : 0f;
		return Mathf.Lerp( EmptyPushSpeed, FullPushSpeed, t );
	}

	public void MovePlanar( Vector3 worldDelta )
	{
		if ( _body == null )
			_body = GetComponent<Rigidbody>();

		Vector3 next = transform.position + worldDelta;
		if ( _body != null )
			_body.MovePosition( next );
		else
			transform.position = next;
	}

	bool CanAcceptDefinition( TreasureDefinition def, out int originX, out int originY, out int stackIndex )
	{
		originX = 0;
		originY = 0;
		stackIndex = 0;
		if ( def == null )
			return false;

		if ( _currentWeight + GetItemWeight( def ) > MaxWeight )
			return false;

		return TryFindPlacement( def, out originX, out originY, out stackIndex );
	}

	bool TryFindAnyOrigin( TreasureDefinition def, out int originX, out int originY, out int stackIndex )
	{
		originX = 0;
		originY = 0;
		stackIndex = 0;
		if ( def == null )
			return false;

		return TryFindPlacement( def, out originX, out originY, out stackIndex, ignoreWeight: true );
	}

	bool TryFindPlacement( TreasureDefinition def, out int originX, out int originY, out int stackIndex, bool ignoreWeight = false )
	{
		originX = 0;
		originY = 0;
		stackIndex = 0;
		if ( def == null || _cellStacks == null )
			return false;

		if ( !ignoreWeight && _currentWeight + GetItemWeight( def ) > MaxWeight )
			return false;

		Vector2Int footprint = def.GetCartGridSize();
		int cols = GridColumns;
		int rows = GridRows;

		// Prefer stacking onto matching footprints first.
		if ( def.canStack )
		{
			for ( int y = 0; y <= rows - footprint.y; y++ )
			{
				for ( int x = 0; x <= cols - footprint.x; x++ )
				{
					CargoStack existing = _cellStacks[ x, y ];
					if ( existing == null )
						continue;

					if ( existing.OriginX != x || existing.OriginY != y )
						continue;

					if ( existing.Definition != def )
						continue;

					if ( existing.Footprint != footprint )
						continue;

					if ( !CanAddToStack( existing ) )
						continue;

					originX = x;
					originY = y;
					stackIndex = existing.Count;
					return true;
				}
			}
		}

		for ( int y = 0; y <= rows - footprint.y; y++ )
		{
			for ( int x = 0; x <= cols - footprint.x; x++ )
			{
				if ( !IsRectangleFree( x, y, footprint ) )
					continue;

				originX = x;
				originY = y;
				stackIndex = 0;
				return true;
			}
		}

		return false;
	}

	bool CanAddToStack( CargoStack stack )
	{
		if ( stack == null || stack.Definition == null || !stack.Definition.canStack )
			return false;

		int max = MaxStackPerCell;
		if ( max > 0 && stack.Count >= max )
			return false;

		return true;
	}

	bool IsRectangleFree( int ox, int oy, Vector2Int footprint )
	{
		int cols = GridColumns;
		int rows = GridRows;
		if ( ox < 0 || oy < 0 || ox + footprint.x > cols || oy + footprint.y > rows )
			return false;

		for ( int y = 0; y < footprint.y; y++ )
		{
			for ( int x = 0; x < footprint.x; x++ )
			{
				if ( _cellStacks[ ox + x, oy + y ] != null )
					return false;
			}
		}

		return true;
	}

	void MarkFootprint( int ox, int oy, Vector2Int footprint, CargoStack stack )
	{
		for ( int y = 0; y < footprint.y; y++ )
		{
			for ( int x = 0; x < footprint.x; x++ )
				_cellStacks[ ox + x, oy + y ] = stack;
		}
	}

	void ClearFootprint( CargoStack stack )
	{
		if ( stack == null || _cellStacks == null )
			return;

		for ( int y = 0; y < stack.Footprint.y; y++ )
		{
			for ( int x = 0; x < stack.Footprint.x; x++ )
			{
				int cx = stack.OriginX + x;
				int cy = stack.OriginY + y;
				if ( cx < 0 || cy < 0 || cx >= GridColumns || cy >= GridRows )
					continue;

				if ( _cellStacks[ cx, cy ] == stack )
					_cellStacks[ cx, cy ] = null;
			}
		}
	}

	void RestackVisuals( CargoStack stack )
	{
		if ( stack == null )
			return;

		for ( int i = 0; i < stack.Items.Count; i++ )
		{
			TreasureItem item = stack.Items[ i ];
			if ( item == null )
				continue;

			GetCellWorldPose( stack.OriginX, stack.OriginY, i, stack.Definition, out Vector3 pos, out Quaternion rot );
			item.EnterDisplayed( this, cargoRoot, pos, rot );
		}
	}

	void GetCellWorldPose( int ox, int oy, int stackIndex, TreasureDefinition def, out Vector3 worldPos, out Quaternion worldRot )
	{
		EnsureCargoRoot();
		float spacing = CellSpacing;
		float width = ( GridColumns - 1 ) * spacing;
		float depth = ( GridRows - 1 ) * spacing;
		Vector2Int footprint = def != null ? def.GetCartGridSize() : Vector2Int.one;

		float localX = -width * 0.5f + ( ox + ( footprint.x - 1 ) * 0.5f ) * spacing;
		float localZ = -depth * 0.5f + ( oy + ( footprint.y - 1 ) * 0.5f ) * spacing;
		float thickness = def != null ? def.GetStackThickness() : 0.04f;
		float localY = stackIndex * thickness;

		Vector3 local = new Vector3( localX, localY, localZ );
		worldPos = cargoRoot.TransformPoint( local );
		worldRot = cargoRoot.rotation;
	}

	static int GetItemWeight( TreasureDefinition def )
	{
		if ( def == null )
			return 1;

		return Mathf.Max( 1, def.weight );
	}
}
