using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Pure math for square gem pyramid slot offsets (local space, Y up from contact).
/// Fill order: expand N×N base (stable corner-anchored L-rings), then append full upper
/// layers (N-1)…1. Duplicate poses are skipped. Grows without a max layer / gem cap.
/// </summary>
public static class GemPyramidLattice
{
	public const int MinBaseSide = 2;
	const float VerticalFactor = 0.8164966f; // ≈ √(2/3) sphere packing
	const float UnitSpacing = 1f;

	static readonly List<Vector3> UnitSlots = new List<Vector3>( 64 );
	static readonly HashSet<long> SeenKeys = new HashSet<long>();
	static int _builtThroughBase;

	/// <summary>N×N cell count for a square layer of the given side length.</summary>
	public static int LayerCapacity( int side )
	{
		side = Mathf.Max( 1, side );
		return side * side;
	}

	/// <summary>
	/// Local offset for slot <paramref name="slotIndex"/>. Slot 0 is the first 2×2 corner cell.
	/// Builds additional base sizes on demand — no max layer limit.
	/// </summary>
	public static bool TryGetSlotLocal( int slotIndex, float spacing, out Vector3 local )
	{
		local = Vector3.zero;
		if ( slotIndex < 0 || spacing < 0.0001f )
			return false;

		EnsureSlot( slotIndex );
		if ( slotIndex >= UnitSlots.Count )
			return false;

		local = UnitSlots[ slotIndex ] * spacing;
		return true;
	}

	static void EnsureSlot( int slotIndex )
	{
		while ( UnitSlots.Count <= slotIndex )
		{
			int nextBase = _builtThroughBase < MinBaseSide ? MinBaseSide : _builtThroughBase + 1;
			int before = UnitSlots.Count;
			AppendBaseSide( nextBase );
			_builtThroughBase = nextBase;
			if ( UnitSlots.Count <= before )
				break;
		}
	}

	static void AppendBaseSide( int baseSide )
	{
		float yStep = UnitSpacing * VerticalFactor;

		int bottomNew = CountNewBaseCells( baseSide );
		for ( int i = 0; i < bottomNew; i++ )
		{
			if ( !TryGetNewBaseCellLocal( baseSide, i, UnitSpacing, out Vector2 xz ) )
				continue;
			TryAddUnique( new Vector3( xz.x, 0f, xz.y ) );
		}

		for ( int layer = 1; layer < baseSide; layer++ )
		{
			int layerSide = baseSide - layer;
			int layerCap = LayerCapacity( layerSide );
			for ( int i = 0; i < layerCap; i++ )
			{
				if ( !TryGetUpperLayerCellLocal( baseSide, layerSide, i, UnitSpacing, out Vector2 xz ) )
					continue;
				TryAddUnique( new Vector3( xz.x, layer * yStep, xz.y ) );
			}
		}
	}

	static void TryAddUnique( Vector3 unitLocal )
	{
		long key = QuantizeKey( unitLocal );
		if ( !SeenKeys.Add( key ) )
			return;
		UnitSlots.Add( unitLocal );
	}

	static long QuantizeKey( Vector3 unitLocal )
	{
		int x = Mathf.RoundToInt( unitLocal.x * 1000f );
		int y = Mathf.RoundToInt( unitLocal.y * 1000f );
		int z = Mathf.RoundToInt( unitLocal.z * 1000f );
		return ( ( long )x & 0x1FFFFF ) << 42 | ( ( long )y & 0x1FFFFF ) << 21 | ( ( long )z & 0x1FFFFF );
	}

	static int CountNewBaseCells( int baseSide )
	{
		if ( baseSide <= MinBaseSide )
			return LayerCapacity( MinBaseSide );
		return LayerCapacity( baseSide ) - LayerCapacity( baseSide - 1 );
	}

	static Vector2 BaseCellXZ( int i, int j, float spacing )
	{
		return new Vector2( ( i - 0.5f ) * spacing, ( j - 0.5f ) * spacing );
	}

	static bool TryGetNewBaseCellLocal( int baseSide, int indexInNew, float spacing, out Vector2 xz )
	{
		xz = Vector2.zero;
		if ( indexInNew < 0 )
			return false;

		int cursor = 0;
		if ( baseSide <= MinBaseSide )
		{
			for ( int j = 0; j < MinBaseSide; j++ )
			{
				for ( int i = 0; i < MinBaseSide; i++ )
				{
					if ( cursor == indexInNew )
					{
						xz = BaseCellXZ( i, j, spacing );
						return true;
					}

					cursor++;
				}
			}

			return false;
		}

		int n = baseSide;
		int edge = n - 1;
		for ( int j = 0; j < n; j++ )
		{
			if ( cursor == indexInNew )
			{
				xz = BaseCellXZ( edge, j, spacing );
				return true;
			}

			cursor++;
		}

		for ( int i = 0; i < edge; i++ )
		{
			if ( cursor == indexInNew )
			{
				xz = BaseCellXZ( i, edge, spacing );
				return true;
			}

			cursor++;
		}

		return false;
	}

	static bool TryGetUpperLayerCellLocal(
		int baseSide,
		int layerSide,
		int indexInLayer,
		float spacing,
		out Vector2 xz )
	{
		xz = Vector2.zero;
		int cap = LayerCapacity( layerSide );
		if ( indexInLayer < 0 || indexInLayer >= cap )
			return false;

		float baseCenter = ( ( baseSide - 1 ) * 0.5f - 0.5f ) * spacing;
		float half = ( layerSide - 1 ) * 0.5f;
		int row = indexInLayer / layerSide;
		int col = indexInLayer % layerSide;
		xz = new Vector2(
			baseCenter + ( col - half ) * spacing,
			baseCenter + ( row - half ) * spacing );
		return true;
	}
}
