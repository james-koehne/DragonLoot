using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Leaf-based imperfect packing: each 8 coins is a leaf with a stable seed.
/// 16 = two leaves, 32 = four leaves, so 8+8 can merge into a 16 with identical XZ.
/// </summary>
public static class CoinStackImperfectLayout
{
	public const string ImperfectChildPrefix = "Imperfect_";
	public const int MinChunkCoins = 8;
	public const int LeafSize = 8;

	static readonly int[] ChunkSizesDescending = { 32, 16, 8 };

	public struct ChunkPlacement
	{
		public int CoinCount;
		public int SeedIndex;
		public int BaseIndex;
		public float LocalY;
		public CoinStackImperfectChunkEntry Entry;
	}

	public static bool IsImperfectEnabled( CoinStackVisualDefinition def )
	{
		return def != null && def.imperfectEnabled;
	}

	public static int ResolveMaxImperfectCoins( CoinStackVisualDefinition def )
	{
		if ( def == null )
			return 300;
		return def.imperfectMaxCoins > 0 ? def.imperfectMaxCoins : 300;
	}

	public static int ResolveImperfectBudget( CoinStackVisualDefinition def, int coinCount )
	{
		if ( def == null || coinCount <= 0 || !def.imperfectEnabled )
			return 0;

		return Mathf.Min( coinCount, ResolveMaxImperfectCoins( def ) );
	}

	public static void ResolveXzRadiiMesh( CoinStackVisualDefinition def, out float minRadius, out float maxRadius )
	{
		minRadius = 0f;
		maxRadius = 0f;
		if ( def == null )
			return;

		def.GetMeshReferenceSize( out float refDiameter, out _ );
		refDiameter = Mathf.Max( 0.0001f, refDiameter );
		float minFrac = Mathf.Clamp( def.imperfectXzRadiusMinFraction, 0f, 0.5f );
		float maxFrac = Mathf.Clamp( def.imperfectXzRadiusMaxFraction, 0f, 0.5f );
		if ( maxFrac < minFrac )
		{
			float swap = minFrac;
			minFrac = maxFrac;
			maxFrac = swap;
		}

		minRadius = refDiameter * minFrac;
		maxRadius = refDiameter * maxFrac;
	}

	/// <summary>
	/// Per-stack yaw (degrees) so identical leaf patterns still read uniquely when rotated.
	/// </summary>
	public static float GetScatterYawDegrees( float variationSeed )
	{
		float u = Hash01( (int)( variationSeed * 127.1f ) ^ 91711 );
		float v = Hash01( (int)( variationSeed * 311.7f ) ^ 43391 );
		return ( u + v * 0.001f ) * 360f;
	}

	public static Vector2 ApplyScatterYaw( Vector2 xz, float variationSeed )
	{
		if ( xz.sqrMagnitude < 1e-12f )
			return xz;

		float rad = GetScatterYawDegrees( variationSeed ) * Mathf.Deg2Rad;
		float c = Mathf.Cos( rad );
		float s = Mathf.Sin( rad );
		return new Vector2( xz.x * c - xz.y * s, xz.x * s + xz.y * c );
	}

	/// <summary>
	/// Deterministic mesh-local XZ (same as baker leaf sampling).
	/// </summary>
	public static Vector2 SampleMeshLocalXz( int seedIndex, int coinIndexInLeaf, float minRadius, float maxRadius )
	{
		if ( maxRadius < minRadius )
		{
			float swap = minRadius;
			minRadius = maxRadius;
			maxRadius = swap;
		}

		if ( maxRadius <= 0.00001f )
			return Vector2.zero;

		float u = Hash01( seedIndex * 73856093 ^ coinIndexInLeaf * 19349663 ^ 83492791 );
		float v = Hash01( seedIndex * 19349663 ^ coinIndexInLeaf * 83492791 ^ 73856093 );
		float angle = u * Mathf.PI * 2f;
		float radius = Mathf.Lerp( minRadius, maxRadius, Mathf.Sqrt( v ) );
		return new Vector2( Mathf.Cos( angle ) * radius, Mathf.Sin( angle ) * radius );
	}

	/// <summary>
	/// Leaf seeds are consecutive from a stack base so 16/32 chunks can concatenate 8-leaves.
	/// </summary>
	public static int LeafSeedIndex( float variationSeed, int leafOrdinal, int variantCount )
	{
		int count = Mathf.Max( 1, variantCount );
		int baseSeed = HashSeedIndex( variationSeed, 0, count );
		if ( leafOrdinal < 0 )
			leafOrdinal = 0;
		return ( baseSeed + leafOrdinal ) % count;
	}

	/// <summary>
	/// XZ for a hierarchical chunk local index (8 / 16 / 32) starting at leaf seed <paramref name="seedIndex"/>.
	/// 16 = leaves seedIndex + (seedIndex+1); 32 = four consecutive leaves.
	/// </summary>
	public static Vector2 SampleHierarchicalChunkXz(
		int seedIndex,
		int localCoinIndex,
		int chunkCoinCount,
		int variantCount,
		float minRadius,
		float maxRadius )
	{
		int count = Mathf.Max( 1, variantCount );
		int leafOrdinalInChunk = localCoinIndex / LeafSize;
		int indexInLeaf = localCoinIndex % LeafSize;
		int leafSeed = ( seedIndex + leafOrdinalInChunk ) % count;
		return SampleMeshLocalXz( leafSeed, indexInLeaf, minRadius, maxRadius );
	}

	/// <summary>
	/// Greedy largest complete chunks for the current settled imperfect count (8+8→16→32).
	/// </summary>
	public static int BuildVisiblePlacements(
		CoinStackVisualDefinition def,
		float variationSeed,
		int settledCoinCount,
		IList<TreasureDefinition> slots,
		List<ChunkPlacement> visiblePlacements )
	{
		if ( visiblePlacements != null )
			visiblePlacements.Clear();

		if ( !IsImperfectEnabled( def ) || visiblePlacements == null )
			return 0;

		if ( def.imperfectChunks == null || def.imperfectChunks.Count == 0 )
			return 0;

		int budget = Mathf.Min( settledCoinCount, ResolveMaxImperfectCoins( def ) );
		if ( budget < MinChunkCoins )
			return 0;

		int remaining = budget;
		int baseIndex = 0;
		int variantCount = Mathf.Max( 1, def.imperfectVariantCount );

		while ( remaining >= MinChunkCoins )
		{
			int size = 0;
			for ( int s = 0; s < ChunkSizesDescending.Length; s++ )
			{
				int candidate = ChunkSizesDescending[ s ];
				if ( remaining >= candidate && TryFindEntry( def, candidate, 0, out _ ) )
				{
					size = candidate;
					break;
				}
			}

			if ( size <= 0 )
				break;

			int leafOrdinal = baseIndex / LeafSize;
			int seedIndex = LeafSeedIndex( variationSeed, leafOrdinal, variantCount );
			if ( !TryFindEntry( def, size, seedIndex, out CoinStackImperfectChunkEntry entry ) )
			{
				if ( !TryFindEntry( def, size, 0, out entry ) )
					break;
				seedIndex = entry.seedIndex;
			}

			visiblePlacements.Add( new ChunkPlacement
			{
				CoinCount = size,
				SeedIndex = seedIndex,
				BaseIndex = baseIndex,
				LocalY = MeasureSlotHeight( slots, 0, baseIndex ),
				Entry = entry
			} );

			baseIndex += size;
			remaining -= size;
		}

		return baseIndex;
	}

	/// <summary>
	/// Mesh-local XZ from the leaf plan — identical for chunk meshes and individual coins.
	/// </summary>
	public static bool TryGetLocalXz(
		CoinStackVisualDefinition def,
		float variationSeed,
		int layoutCoinCount,
		int slotIndex,
		IList<TreasureDefinition> slots,
		out Vector2 xz )
	{
		xz = Vector2.zero;
		_ = layoutCoinCount;
		_ = slots;

		if ( def == null || slotIndex < 0 || !IsImperfectEnabled( def ) )
			return false;

		int maxCoins = ResolveMaxImperfectCoins( def );
		if ( slotIndex >= maxCoins )
			return false;

		ResolveXzRadiiMesh( def, out float minRadius, out float maxRadius );
		int variantCount = Mathf.Max( 1, def.imperfectVariantCount );
		int leafOrdinal = slotIndex / LeafSize;
		int indexInLeaf = slotIndex % LeafSize;
		int leafSeed = LeafSeedIndex( variationSeed, leafOrdinal, variantCount );
		xz = SampleMeshLocalXz( leafSeed, indexInLeaf, minRadius, maxRadius );
		xz = ApplyScatterYaw( xz, variationSeed );
		return true;
	}

	public static bool TryFindEntry(
		CoinStackVisualDefinition def,
		int coinCount,
		int preferredSeedIndex,
		out CoinStackImperfectChunkEntry entry )
	{
		entry = null;
		if ( def == null || def.imperfectChunks == null )
			return false;

		CoinStackImperfectChunkEntry fallback = null;
		for ( int i = 0; i < def.imperfectChunks.Count; i++ )
		{
			CoinStackImperfectChunkEntry e = def.imperfectChunks[ i ];
			if ( e == null || e.mesh == null || e.coinCount != coinCount )
				continue;

			if ( e.seedIndex == preferredSeedIndex )
			{
				entry = e;
				return true;
			}

			if ( fallback == null )
				fallback = e;
		}

		entry = fallback;
		return entry != null;
	}

	public static int HashSeedIndex( float variationSeed, int chunkOrdinal, int variantCount )
	{
		if ( variantCount <= 1 )
			return 0;

		unchecked
		{
			int h = (int)( variationSeed * 73856093f );
			h ^= chunkOrdinal * 19349663;
			h ^= h >> 16;
			if ( h < 0 )
				h = -h;
			return h % variantCount;
		}
	}

	public static float MeasureSlotHeight( IList<TreasureDefinition> slots, int start, int count )
	{
		float height = 0f;
		if ( slots == null )
			return height;

		int end = Mathf.Min( slots.Count, start + count );
		for ( int i = start; i < end; i++ )
			height += TreasureStackSpacing.GetStep( slots[ i ] );
		return height;
	}

	static float Hash01( int x )
	{
		unchecked
		{
			uint n = (uint)x;
			n = ( n ^ 61u ) ^ ( n >> 16 );
			n *= 9u;
			n = n ^ ( n >> 4 );
			n *= 0x27d4eb2du;
			n = n ^ ( n >> 15 );
			return ( n & 0x00ffffffu ) / 16777215f;
		}
	}
}
