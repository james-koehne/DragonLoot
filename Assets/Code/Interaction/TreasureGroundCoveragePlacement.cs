using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Deterministic editor/runtime placement math for <see cref="TreasureGroundCoverageZone"/>.
/// Places every unit from the assigned group onto valid treasure-surface cells
/// that are not occupied by gold-pile height.
/// </summary>
public static class TreasureGroundCoveragePlacement
{
	public struct CoinStackPlacement
	{
		public Vector3 worldPosition;
		public TreasureDefinition[] slots;

		public int Count => slots != null ? slots.Length : 0;

		public TreasureDefinition PrimaryDefinition
		{
			get
			{
				if ( slots == null || slots.Length == 0 )
					return null;
				return slots[ 0 ];
			}
		}

		public bool IsHomogeneous
		{
			get
			{
				if ( slots == null || slots.Length == 0 )
					return false;
				TreasureDefinition first = slots[ 0 ];
				for ( int i = 1; i < slots.Length; i++ )
				{
					if ( slots[ i ] != first )
						return false;
				}

				return true;
			}
		}
	}

	public struct PropPlacement
	{
		public Vector3 worldPosition;
		public Quaternion rotation;
		public TreasureDefinition definition;
	}

	struct CandidateCell
	{
		public int cellX;
		public int cellZ;
		public Vector3 worldCenter;
		public float densityNoise;
	}

	public struct Request
	{
		public Bounds worldBounds;
		public TreasureGroupDefinition group;
		public int layoutSeed;
		public float densityFrequency;
		public float densityThreshold;
		public int minCoinsPerStack;
		public int maxCoinsPerStack;
		public float stackHeightFrequency;
		public float minSpacing;
		public float coinMixFraction;
		public float placementJitter;
		public TreasureGroundCategoryRules categoryRules;
		public int surfaceNeighborhoodCells;
	}

	public struct Result
	{
		public bool success;
		public string error;
		public CoinStackPlacement[] coinStacks;
		public PropPlacement[] props;
		public int requestedCoins;
		public int placedCoins;
		public int requestedProps;
		public int placedProps;
	}

	public static Result Compute( Request request )
	{
		Result result = default;
		if ( request.group == null )
		{
			result.error = "Treasure group is not assigned.";
			return result;
		}

		TreasureSurfaceAuthoring surface = TreasureSurfaceAuthoring.Instance;
		if ( surface == null )
		{
			result.error = "No TreasureSurfaceAuthoring in the scene.";
			return result;
		}

		surface.EnsurePaintBuffers();
		if ( surface.PaintAsset == null || !surface.PaintAsset.HasBuffers )
		{
			result.error =
				"Treasure surface paint is not loaded. "
				+ "Player builds copy the .paintbin sidecar into StreamingAssets; rebuild after painting.";
			return result;
		}

		int seed = ResolveLayoutSeed( request.layoutSeed );
		float seedOffsetX = seed * 0.0137f;
		float seedOffsetZ = seed * 0.091f;
		int neighborhood = Mathf.Max( 1, request.surfaceNeighborhoodCells );
		TreasurePileVisual[] piles = CollectPiles();

		List<CandidateCell> valid = CollectValidCells(
			surface,
			request.worldBounds,
			neighborhood,
			piles );

		if ( valid.Count == 0 )
		{
			result.error = "No valid treasure-surface cells inside the zone (excluding gold-pile height).";
			return result;
		}

		ComputeDensityNoise( valid, request.densityFrequency, seedOffsetX, seedOffsetZ );

		TreasureGroupEntry[] coinEntries = request.group.GetCoinEntries();
		TreasureGroupEntry[] propEntries = request.group.GetNonCoinEntries();
		int totalCoins = SumEntryCounts( coinEntries );
		int totalProps = SumEntryCounts( propEntries );
		result.requestedCoins = totalCoins;
		result.requestedProps = totalProps;

		int minStack = Mathf.Max( 1, request.minCoinsPerStack );
		int maxStack = Mathf.Max( minStack, request.maxCoinsPerStack );
		float coinSpacing = ResolveCoinSpacing( request );
		int stackSlots = Mathf.Max( 0, valid.Count - totalProps );
		int stackCount = ResolvePreferredStackCount( totalCoins, minStack, maxStack, stackSlots );
		if ( stackCount + totalProps > valid.Count )
		{
			result.error =
				$"Need {stackCount + totalProps} placement sites ({stackCount} coin stacks + {totalProps} props), "
				+ $"but only {valid.Count} valid surface cells are free of gold-pile height.";
			return result;
		}

		List<Vector3> reserved = new List<Vector3>( stackCount + totalProps );
		List<CandidateCell> stackSites = SelectSitesFast(
			request,
			surface,
			valid,
			reserved,
			stackCount,
			coinSpacing,
			seed,
			11,
			densityBias: 0f );

		if ( stackSites.Count < stackCount )
		{
			result.error =
				$"Need {stackCount} coin stack sites, found {stackSites.Count} valid cells after spacing.";
			return result;
		}

		List<CoinStackPlacement> stacks = BuildCoinStacks(
			request,
			coinEntries,
			stackSites,
			totalCoins,
			minStack,
			maxStack,
			seed,
			seedOffsetX,
			seedOffsetZ );

		int placedCoins = 0;
		for ( int i = 0; i < stacks.Count; i++ )
			placedCoins += stacks[ i ].Count;

		List<PropPlacement> props = BuildPropPlacements(
			request,
			surface,
			piles,
			propEntries,
			valid,
			reserved,
			seed );

		int placedProps = props.Count;
		result.placedCoins = placedCoins;
		result.placedProps = placedProps;
		result.coinStacks = stacks.ToArray();
		result.props = props.ToArray();

		if ( placedCoins != totalCoins || placedProps != totalProps )
		{
			result.error =
				$"Could not place every treasure. Coins {placedCoins}/{totalCoins}, props {placedProps}/{totalProps}.";
			return result;
		}

		result.success = true;
		return result;
	}

	static TreasurePileVisual[] CollectPiles()
	{
		return UnityEngine.Object.FindObjectsByType<TreasurePileVisual>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
	}

	static int ResolveLayoutSeed( int layoutSeed )
	{
		if ( layoutSeed != 0 )
			return layoutSeed;
		return 1337;
	}

	static int SumEntryCounts( TreasureGroupEntry[] entries )
	{
		if ( entries == null )
			return 0;

		int total = 0;
		for ( int i = 0; i < entries.Length; i++ )
		{
			if ( entries[ i ].treasure != null )
				total += Mathf.Max( 0, entries[ i ].count );
		}

		return total;
	}

	static List<CandidateCell> CollectValidCells(
		TreasureSurfaceAuthoring surface,
		Bounds bounds,
		int neighborhood,
		TreasurePileVisual[] piles )
	{
		List<CandidateCell> cells = new List<CandidateCell>( 256 );
		if ( !TryWorldBoundsToCellRange( surface, bounds, out int minX, out int minZ, out int maxX, out int maxZ ) )
			return cells;

		Vector3 min = bounds.min;
		Vector3 max = bounds.max;
		for ( int z = minZ; z <= maxZ; z++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				if ( !surface.TryGetPaint( x, z, out bool traversable, out _ ) || !traversable )
					continue;
				if ( !surface.HasTraversableNeighborhood( x, z, neighborhood ) )
					continue;

				Vector3 center = surface.CellCenterWorld( x, z );
				if ( center.x < min.x || center.x > max.x || center.z < min.z || center.z > max.z )
					continue;
				if ( !HasRuntimeSurfaceIfAvailable( center ) )
					continue;
				if ( HasAnyPileHeight( piles, center ) )
					continue;

				cells.Add( new CandidateCell
				{
					cellX = x,
					cellZ = z,
					worldCenter = center
				} );
			}
		}

		return cells;
	}

	static bool TryWorldBoundsToCellRange(
		TreasureSurfaceAuthoring surface,
		Bounds bounds,
		out int minX,
		out int minZ,
		out int maxX,
		out int maxZ )
	{
		minX = 0;
		minZ = 0;
		maxX = 0;
		maxZ = 0;
		float halfX = surface.WorldSizeX * 0.5f;
		float halfZ = surface.WorldSizeZ * 0.5f;
		Vector3 origin = surface.WorldOrigin;
		float minWX = Mathf.Max( bounds.min.x, origin.x - halfX );
		float maxWX = Mathf.Min( bounds.max.x, origin.x + halfX - 0.0001f );
		float minWZ = Mathf.Max( bounds.min.z, origin.z - halfZ );
		float maxWZ = Mathf.Min( bounds.max.z, origin.z + halfZ - 0.0001f );
		if ( minWX > maxWX || minWZ > maxWZ )
			return false;

		if ( !surface.TryWorldToCell( new Vector3( minWX, 0f, minWZ ), out minX, out minZ ) )
			return false;
		if ( !surface.TryWorldToCell( new Vector3( maxWX, 0f, maxWZ ), out maxX, out maxZ ) )
			return false;

		if ( minX > maxX )
		{
			int swap = minX;
			minX = maxX;
			maxX = swap;
		}

		if ( minZ > maxZ )
		{
			int swap = minZ;
			minZ = maxZ;
			maxZ = swap;
		}

		return true;
	}

	static bool HasRuntimeSurfaceIfAvailable( Vector3 world )
	{
		TreasureSurfaceWorld worldSys = TreasureSurfaceWorld.Instance;
		if ( worldSys == null || !worldSys.IsInitialized || worldSys.Sampler == null )
			return true;

		if ( !worldSys.Sampler.TrySample( world, out TreasureSurfaceSample sample ) )
			return true;

		return sample.Valid && sample.Traversable;
	}

	static bool HasAnyPileHeight( TreasurePileVisual[] piles, Vector3 world )
	{
		if ( piles == null )
			return false;

		for ( int i = 0; i < piles.Length; i++ )
		{
			TreasurePileVisual pile = piles[ i ];
			if ( pile != null && pile.HasAnyHeightAtWorld( world ) )
				return true;
		}

		return false;
	}

	static void ComputeDensityNoise(
		List<CandidateCell> cells,
		float frequency,
		float seedOffsetX,
		float seedOffsetZ )
	{
		float freq = Mathf.Max( 0.01f, frequency );
		for ( int i = 0; i < cells.Count; i++ )
		{
			CandidateCell cell = cells[ i ];
			cell.densityNoise = Mathf.PerlinNoise(
				cell.cellX * freq + seedOffsetX,
				cell.cellZ * freq + seedOffsetZ );
			cells[ i ] = cell;
		}
	}

	static float ResolveCoinSpacing( Request request )
	{
		float baseSpacing = request.minSpacing;
		if ( baseSpacing <= 0.001f )
			baseSpacing = GroundCoinStack.DefaultJoinRadius * 2.2f;
		return baseSpacing * Mathf.Max( 0.25f, request.categoryRules.coinSpacingScale );
	}

	static int ResolvePreferredStackCount(
		int totalCoins,
		int minStack,
		int maxStack,
		int availableCells )
	{
		if ( totalCoins <= 0 )
			return 0;
		if ( totalCoins < minStack )
			return 1;

		int minStacks = Mathf.Max( 1, Mathf.CeilToInt( totalCoins / ( float )maxStack ) );
		int maxStacks = Mathf.Max( minStacks, totalCoins / minStack );
		if ( availableCells <= 0 )
			return 0;
		if ( availableCells < minStacks )
			return availableCells;

		// Target a mid-range average height so stacks can vary between min and max.
		float targetAvg = Mathf.Lerp( minStack, maxStack, 0.48f );
		int desired = Mathf.Clamp( Mathf.RoundToInt( totalCoins / Mathf.Max( 1f, targetAvg ) ), minStacks, maxStacks );
		return Mathf.Clamp( desired, minStacks, Mathf.Min( maxStacks, availableCells ) );
	}

	static List<CandidateCell> SelectSitesFast(
		Request request,
		TreasureSurfaceAuthoring surface,
		List<CandidateCell> cells,
		List<Vector3> reserved,
		int needed,
		float minSpacing,
		int seed,
		int salt,
		float densityBias )
	{
		List<CandidateCell> selected = new List<CandidateCell>( needed );
		if ( needed <= 0 || cells == null || cells.Count == 0 )
			return selected;

		Bounds bounds = request.worldBounds;
		Vector3 bmin = bounds.min;
		Vector3 bsize = bounds.size;
		float spacing = Mathf.Max( 0.02f, minSpacing );
		int binsX = Mathf.Max( 1, Mathf.CeilToInt( bsize.x / spacing ) );
		int binsZ = Mathf.Max( 1, Mathf.CeilToInt( bsize.z / spacing ) );
		const int maxBinCount = 4096;
		int binCount = binsX * binsZ;
		if ( binCount > maxBinCount )
		{
			float scale = Mathf.Sqrt( binCount / ( float )maxBinCount );
			binsX = Mathf.Max( 1, Mathf.CeilToInt( binsX / scale ) );
			binsZ = Mathf.Max( 1, Mathf.CeilToInt( binsZ / scale ) );
			binCount = binsX * binsZ;
		}

		int[] bestCell = new int[ binCount ];
		float[] bestScore = new float[ binCount ];
		for ( int b = 0; b < binCount; b++ )
			bestCell[ b ] = -1;

		float invBinX = binsX / Mathf.Max( 0.0001f, bsize.x );
		float invBinZ = binsZ / Mathf.Max( 0.0001f, bsize.z );
		for ( int i = 0; i < cells.Count; i++ )
		{
			CandidateCell cell = cells[ i ];
			Vector3 p = cell.worldCenter;
			int bx = Mathf.Clamp( Mathf.FloorToInt( ( p.x - bmin.x ) * invBinX ), 0, binsX - 1 );
			int bz = Mathf.Clamp( Mathf.FloorToInt( ( p.z - bmin.z ) * invBinZ ), 0, binsZ - 1 );
			int bin = bz * binsX + bx;

			float binCx = bmin.x + ( bx + 0.5f ) / binsX * bsize.x;
			float binCz = bmin.z + ( bz + 0.5f ) / binsZ * bsize.z;
			float score = DistSqXZ( p, new Vector3( binCx, p.y, binCz ) ) - cell.densityNoise * densityBias;
			if ( bestCell[ bin ] < 0 || score < bestScore[ bin ] )
			{
				bestCell[ bin ] = i;
				bestScore[ bin ] = score;
			}
		}

		List<int> filledBins = new List<int>( binCount );
		for ( int b = 0; b < binCount; b++ )
		{
			if ( bestCell[ b ] >= 0 )
				filledBins.Add( b );
		}

		ShuffleIntList( filledBins, seed, salt );
		PlacementSpatialHash hash = new PlacementSpatialHash( spacing );
		hash.AddExisting( reserved );
		bool[] usedCells = new bool[ cells.Count ];
		float jitterRadius = ResolveJitterRadius( request, surface, minSpacing );

		for ( int b = 0; b < filledBins.Count && selected.Count < needed; b++ )
		{
			int cellIndex = bestCell[ filledBins[ b ] ];
			if ( usedCells[ cellIndex ] )
				continue;

			CandidateCell cell = cells[ cellIndex ];
			Vector3 pos = cell.worldCenter;
			ApplyPlacementJitter( request, ref pos, cell, jitterRadius, seed, salt + b );
			if ( hash.IsTooClose( pos ) )
				continue;

			usedCells[ cellIndex ] = true;
			cell.worldCenter = pos;
			selected.Add( cell );
			reserved.Add( pos );
			hash.Add( pos );
		}

		if ( selected.Count < needed )
		{
			float relaxedSq = spacing * 0.35f;
			relaxedSq *= relaxedSq;
			for ( int i = 0; i < cells.Count && selected.Count < needed; i++ )
			{
				if ( usedCells[ i ] )
					continue;

				CandidateCell cell = cells[ i ];
				Vector3 pos = cell.worldCenter;
				ApplyPlacementJitter( request, ref pos, cell, jitterRadius, seed, salt + i + 911 );
				if ( hash.IsTooClose( pos, relaxedSq ) )
					continue;

				usedCells[ i ] = true;
				cell.worldCenter = pos;
				selected.Add( cell );
				reserved.Add( pos );
				hash.Add( pos );
			}
		}

		return selected;
	}

	struct PlacementSpatialHash
	{
		readonly float _cellSize;
		readonly float _spacingSq;
		readonly Dictionary<long, List<Vector3>> _grid;

		public PlacementSpatialHash( float spacing )
		{
			_cellSize = Mathf.Max( 0.05f, spacing );
			_spacingSq = spacing * spacing;
			_grid = new Dictionary<long, List<Vector3>>( 64 );
		}

		public void AddExisting( List<Vector3> points )
		{
			if ( points == null )
				return;
			for ( int i = 0; i < points.Count; i++ )
				Add( points[ i ] );
		}

		public void Add( Vector3 pos )
		{
			long key = Key( pos );
			if ( !_grid.TryGetValue( key, out List<Vector3> bucket ) )
			{
				bucket = new List<Vector3>( 4 );
				_grid[ key ] = bucket;
			}

			bucket.Add( pos );
		}

		public bool IsTooClose( Vector3 pos, float spacingSqOverride = -1f )
		{
			float spacingSq = spacingSqOverride > 0f ? spacingSqOverride : _spacingSq;
			int gx = Mathf.FloorToInt( pos.x / _cellSize );
			int gz = Mathf.FloorToInt( pos.z / _cellSize );
			for ( int dz = -2; dz <= 2; dz++ )
			{
				for ( int dx = -2; dx <= 2; dx++ )
				{
					long key = Pack( gx + dx, gz + dz );
					if ( !_grid.TryGetValue( key, out List<Vector3> bucket ) )
						continue;
					for ( int i = 0; i < bucket.Count; i++ )
					{
						if ( DistSqXZ( pos, bucket[ i ] ) < spacingSq )
							return true;
					}
				}
			}

			return false;
		}

		static long Pack( int gx, int gz )
		{
			return ( ( long )gx << 32 ) ^ ( uint )gz;
		}

		long Key( Vector3 pos )
		{
			return Pack( Mathf.FloorToInt( pos.x / _cellSize ), Mathf.FloorToInt( pos.z / _cellSize ) );
		}
	}

	static void ApplyPlacementJitter(
		Request request,
		ref Vector3 pos,
		CandidateCell cell,
		float jitterRadius,
		int seed,
		int salt )
	{
		if ( jitterRadius <= 0.0001f )
			return;

		float angle = Hash01( seed, salt, cell.cellX, cell.cellZ ) * Mathf.PI * 2f;
		float radius = Hash01( seed, salt, cell.cellZ, cell.cellX ) * jitterRadius;
		pos.x += Mathf.Cos( angle ) * radius;
		pos.z += Mathf.Sin( angle ) * radius;

		Bounds bounds = request.worldBounds;
		pos.x = Mathf.Clamp( pos.x, bounds.min.x, bounds.max.x );
		pos.z = Mathf.Clamp( pos.z, bounds.min.z, bounds.max.z );
	}

	static void ShuffleIntList( List<int> list, int seed, int salt )
	{
		if ( list == null || list.Count <= 1 )
			return;

		for ( int i = list.Count - 1; i > 0; i-- )
		{
			int j = Mathf.FloorToInt( Hash01( seed, salt, i, 0 ) * ( i + 1 ) );
			j = Mathf.Clamp( j, 0, i );
			int tmp = list[ i ];
			list[ i ] = list[ j ];
			list[ j ] = tmp;
		}
	}

	static float DistSqXZ( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return dx * dx + dz * dz;
	}

	static float ResolveJitterRadius( Request request, TreasureSurfaceAuthoring surface, float minSpacing )
	{
		float cell = surface != null ? surface.CellSize : 0.1f;
		float jitter = Mathf.Clamp01( request.placementJitter );
		if ( jitter <= 0.0001f )
			return 0f;
		return Mathf.Max( cell, minSpacing ) * jitter;
	}

	static List<CoinStackPlacement> BuildCoinStacks(
		Request request,
		TreasureGroupEntry[] coinEntries,
		List<CandidateCell> sites,
		int totalCoins,
		int minStack,
		int maxStack,
		int seed,
		float seedOffsetX,
		float seedOffsetZ )
	{
		List<CoinStackPlacement> output = new List<CoinStackPlacement>( sites.Count );
		if ( totalCoins <= 0 || sites.Count == 0 || coinEntries == null || coinEntries.Length == 0 )
			return output;

		int stackCount = sites.Count;
		int[] heights = BuildStackHeights(
			stackCount,
			totalCoins,
			minStack,
			maxStack,
			sites,
			request.stackHeightFrequency,
			seed,
			seedOffsetX,
			seedOffsetZ );

		List<TreasureDefinition> pool = ExpandCoinPool( coinEntries );
		ShuffleRange( pool, 0, pool.Count, seed, 97 );

		int cursor = 0;
		for ( int i = 0; i < stackCount; i++ )
		{
			int height = heights[ i ];
			if ( height <= 0 )
				continue;
			if ( cursor + height > pool.Count )
				break;

			TreasureDefinition[] slots = new TreasureDefinition[ height ];
			for ( int s = 0; s < height; s++ )
				slots[ s ] = pool[ cursor++ ];

			output.Add( new CoinStackPlacement
			{
				worldPosition = sites[ i ].worldCenter,
				slots = slots
			} );
		}

		return output;
	}

	static int[] BuildStackHeights(
		int stackCount,
		int totalCoins,
		int minStack,
		int maxStack,
		List<CandidateCell> sites,
		float stackHeightFrequency,
		int seed,
		float seedOffsetX,
		float seedOffsetZ )
	{
		int[] heights = new int[ stackCount ];
		if ( stackCount <= 0 )
			return heights;

		int minH = minStack;
		int maxH = maxStack;
		if ( totalCoins < minStack )
		{
			heights[ 0 ] = totalCoins;
			return heights;
		}

		if ( minH * stackCount > totalCoins )
			minH = Mathf.Max( 1, totalCoins / stackCount );

		int extraCap = Mathf.Max( 0, maxH - minH );
		for ( int i = 0; i < stackCount; i++ )
			heights[ i ] = minH;

		int extras = totalCoins - ( minH * stackCount );
		if ( extras <= 0 || extraCap <= 0 )
			return heights;

		int maxExtras = stackCount * extraCap;
		extras = Mathf.Min( extras, maxExtras );

		float freq = Mathf.Max( 0.01f, stackHeightFrequency );
		float[] weight = new float[ stackCount ];
		int[] order = new int[ stackCount ];
		float totalWeight = 0f;
		for ( int i = 0; i < stackCount; i++ )
		{
			Vector3 pos = sites[ i ].worldCenter;
			float spatial = Mathf.PerlinNoise(
				pos.x * freq + seedOffsetX * 2.17f,
				pos.z * freq + seedOffsetZ * 1.73f );
			float unique = Hash01( seed, i, sites[ i ].cellX, sites[ i ].cellZ );
			float t = Mathf.Clamp01( spatial * 0.62f + unique * 0.38f );
			weight[ i ] = 0.12f + t;
			totalWeight += weight[ i ];
			order[ i ] = i;
		}

		int[] extra = new int[ stackCount ];
		float[] remainders = new float[ stackCount ];
		int distributed = 0;
		for ( int i = 0; i < stackCount; i++ )
		{
			float exact = extras * ( weight[ i ] / totalWeight );
			extra[ i ] = Mathf.FloorToInt( exact );
			remainders[ i ] = exact - extra[ i ];
			distributed += extra[ i ];
		}

		int leftover = extras - distributed;
		SortIndicesByRemainder( order, remainders );
		for ( int i = 0; i < leftover && i < stackCount; i++ )
			extra[ order[ i ] ]++;

		for ( int i = 0; i < stackCount; i++ )
			heights[ i ] = minH + Mathf.Min( extra[ i ], extraCap );

		BalanceStackHeights( heights, totalCoins, minH, maxH, weight );
		return heights;
	}

	static void SortIndicesByRemainder( int[] indices, float[] remainders )
	{
		if ( indices == null || remainders == null || indices.Length <= 1 )
			return;

		Array.Sort( indices, ( a, b ) =>
		{
			int cmp = remainders[ b ].CompareTo( remainders[ a ] );
			return cmp != 0 ? cmp : a.CompareTo( b );
		} );
	}

	static void BalanceStackHeights( int[] heights, int totalCoins, int minH, int maxH, float[] weight )
	{
		if ( heights == null || heights.Length == 0 )
			return;

		int[] order = new int[ heights.Length ];
		for ( int i = 0; i < order.Length; i++ )
			order[ i ] = i;

		int guard = 0;
		while ( guard++ < 100000 )
		{
			int sum = 0;
			for ( int i = 0; i < heights.Length; i++ )
				sum += heights[ i ];

			int diff = totalCoins - sum;
			if ( diff == 0 )
				return;

			SortIndicesByRank( order, weight, descending: diff > 0 );
			bool moved = false;
			for ( int pass = 0; pass < order.Length; pass++ )
			{
				int index = order[ pass ];
				if ( diff > 0 && heights[ index ] < maxH )
				{
					heights[ index ]++;
					diff--;
					moved = true;
					if ( diff == 0 )
						return;
				}
				else if ( diff < 0 && heights[ index ] > minH )
				{
					heights[ index ]--;
					diff++;
					moved = true;
					if ( diff == 0 )
						return;
				}
			}

			if ( !moved )
				return;
		}
	}

	static List<TreasureDefinition> ExpandCoinPool( TreasureGroupEntry[] coinEntries )
	{
		int total = SumEntryCounts( coinEntries );
		List<TreasureDefinition> pool = new List<TreasureDefinition>( total );
		for ( int i = 0; i < coinEntries.Length; i++ )
		{
			TreasureGroupEntry entry = coinEntries[ i ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;
			for ( int n = 0; n < entry.count; n++ )
				pool.Add( entry.treasure );
		}

		return pool;
	}

	static void ShuffleRange( List<TreasureDefinition> list, int start, int count, int seed, int salt )
	{
		if ( list == null || count <= 1 )
			return;

		int end = Mathf.Min( start + count, list.Count );
		for ( int i = end - 1; i > start; i-- )
		{
			int j = start + Mathf.FloorToInt( Hash01( seed, salt, i, start ) * ( i - start + 1 ) );
			j = Mathf.Clamp( j, start, i );
			TreasureDefinition tmp = list[ i ];
			list[ i ] = list[ j ];
			list[ j ] = tmp;
		}
	}

	static List<PropPlacement> BuildPropPlacements(
		Request request,
		TreasureSurfaceAuthoring surface,
		TreasurePileVisual[] piles,
		TreasureGroupEntry[] propEntries,
		List<CandidateCell> valid,
		List<Vector3> reserved,
		int seed )
	{
		List<PropPlacement> output = new List<PropPlacement>( 32 );
		if ( propEntries == null || propEntries.Length == 0 )
			return output;

		int totalProps = SumEntryCounts( propEntries );
		if ( totalProps <= 0 )
			return output;

		float coinSpacing = ResolveCoinSpacing( request );
		float maxSpacing = coinSpacing;
		float densityBias = 0f;
		for ( int entryIndex = 0; entryIndex < propEntries.Length; entryIndex++ )
		{
			TreasureGroupEntry entry = propEntries[ entryIndex ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			float densityScale = request.categoryRules.ResolveDensityScale( entry.treasure.category );
			float spacingScale = request.categoryRules.ResolveSpacingScale( entry.treasure.category );
			float spacing = coinSpacing * Mathf.Max( 1f, spacingScale );
			if ( spacing > maxSpacing )
				maxSpacing = spacing;
			float entryBias = request.densityThreshold * spacing * spacing * 0.35f * densityScale;
			if ( entryBias > densityBias )
				densityBias = entryBias;
		}

		List<CandidateCell> sites = SelectSitesFast(
			request,
			surface,
			valid,
			reserved,
			totalProps,
			maxSpacing,
			seed,
			100,
			densityBias );

		int siteIndex = 0;
		int unitIndex = 0;
		for ( int entryIndex = 0; entryIndex < propEntries.Length; entryIndex++ )
		{
			TreasureGroupEntry entry = propEntries[ entryIndex ];
			if ( entry.treasure == null || entry.count <= 0 )
				continue;

			for ( int n = 0; n < entry.count; n++ )
			{
				if ( siteIndex >= sites.Count )
					return output;

				Vector3 pos = sites[ siteIndex ].worldCenter;
				float yaw = Hash01( seed, entryIndex, unitIndex, n ) * 360f;
				output.Add( new PropPlacement
				{
					worldPosition = pos,
					rotation = Quaternion.Euler( 0f, yaw, 0f ),
					definition = entry.treasure
				} );
				siteIndex++;
				unitIndex++;
			}
		}

		return output;
	}

	static void SortIndicesByRank( int[] indices, float[] rank, bool descending )
	{
		if ( indices == null || rank == null || indices.Length <= 1 )
			return;

		Array.Sort( indices, ( a, b ) =>
		{
			int cmp = rank[ a ].CompareTo( rank[ b ] );
			if ( cmp == 0 )
				return a.CompareTo( b );
			return descending ? -cmp : cmp;
		} );
	}

	static float Hash01( int seed, int a, int b, int c )
	{
		unchecked
		{
			uint h = ( uint )seed;
			h = h * 374761393u + ( uint )a;
			h = h * 668265263u + ( uint )b;
			h = h * 2246822519u + ( uint )c;
			h ^= h >> 13;
			h *= 1274126177u;
			h ^= h >> 16;
			return ( h & 0x00FFFFFFu ) / 16777215f;
		}
	}
}

[Serializable]
public struct TreasureGroundCategoryRules
{
	[Tooltip( "Multiplier on base min spacing for coin stack anchors." )]
	[Min( 0.25f )]
	public float coinSpacingScale;

	[Tooltip( "Higher = sparser gem placement (raises density threshold)." )]
	[Min( 0.25f )]
	public float gemDensityScale;

	[Tooltip( "Multiplier on base spacing for gems." )]
	[Min( 0.5f )]
	public float gemSpacingScale;

	[Tooltip( "Higher = sparser artifact / large prop placement." )]
	[Min( 0.25f )]
	public float artifactDensityScale;

	[Tooltip( "Multiplier on base spacing for artifacts and other large props." )]
	[Min( 0.5f )]
	public float artifactSpacingScale;

	public static TreasureGroundCategoryRules Default()
	{
		return new TreasureGroundCategoryRules
		{
			coinSpacingScale = 1f,
			gemDensityScale = 1.4f,
			gemSpacingScale = 1.5f,
			artifactDensityScale = 2f,
			artifactSpacingScale = 2f
		};
	}

	public float ResolveDensityScale( TreasureCategory category )
	{
		if ( category == TreasureCategory.Gem )
			return gemDensityScale;
		if ( category == TreasureCategory.Coin )
			return 1f;
		return artifactDensityScale;
	}

	public float ResolveSpacingScale( TreasureCategory category )
	{
		if ( category == TreasureCategory.Gem )
			return gemSpacingScale;
		if ( category == TreasureCategory.Coin )
			return coinSpacingScale;
		return artifactSpacingScale;
	}
}
