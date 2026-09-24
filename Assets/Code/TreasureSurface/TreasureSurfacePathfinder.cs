using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local-grid A* on the treasure surface with soft edge-clearance costs.
/// </summary>
public static class TreasureSurfacePathfinder
{
	const float CollinearDotThreshold = 0.998f;
	const int MaxSearchCells = 131072;
	const int MaxExpandAttempts = 4;
	const float ExpandPaddingScale = 2f;

	static readonly int[] NeighborDx4 = { 1, -1, 0, 0 };
	static readonly int[] NeighborDz4 = { 0, 0, 1, -1 };
	static readonly int[] NeighborDx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
	static readonly int[] NeighborDz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

	public static bool TryFindPath(
		Vector3 from,
		Vector3 to,
		in TreasureSurfacePathSettings settings,
		out TreasureSurfacePath path )
	{
		path = TreasureSurfacePath.Failed;

		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || !world.IsInitialized || world.Definition == null )
			return false;

		TreasureSurfaceDefinition def = world.Definition;
		float cellSize = def.CellSize;
		float maxStep = settings.ResolveMaxStepHeight();

		if ( !TryResolvePathCell( world, def, from, out int startGx, out int startGz, out float startHeight ) )
			return false;
		if ( !TryResolvePathCell( world, def, to, out int endGx, out int endGz, out float endHeight ) )
			return false;

		float pad = Mathf.Max( cellSize * 4f, settings.searchPadding );
		for ( int attempt = 0; attempt < MaxExpandAttempts; attempt++ )
		{
			if ( TryFindPathInBounds( world, def, startGx, startGz, startHeight, endGx, endGz, endHeight, pad, cellSize, maxStep, settings, out path ) )
				return true;

			pad *= ExpandPaddingScale;
		}

		return false;
	}

	static bool TryFindPathInBounds(
		TreasureSurfaceWorld world,
		TreasureSurfaceDefinition def,
		int startGx,
		int startGz,
		float startHeight,
		int endGx,
		int endGz,
		float endHeight,
		float pad,
		float cellSize,
		float maxStep,
		in TreasureSurfacePathSettings settings,
		out TreasureSurfacePath path )
	{
		path = TreasureSurfacePath.Failed;

		Vector3 startWorld = GlobalCellCenter( def, startGx, startGz, startHeight );
		Vector3 endWorld = GlobalCellCenter( def, endGx, endGz, endHeight );

		float minWx = Mathf.Min( startWorld.x, endWorld.x ) - pad;
		float maxWx = Mathf.Max( startWorld.x, endWorld.x ) + pad;
		float minWz = Mathf.Min( startWorld.z, endWorld.z ) - pad;
		float maxWz = Mathf.Max( startWorld.z, endWorld.z ) + pad;

		TryWorldToGlobalCellClamped( def, new Vector3( minWx, 0f, minWz ), out int minGx, out int minGz );
		TryWorldToGlobalCellClamped( def, new Vector3( maxWx, 0f, maxWz ), out int maxGx, out int maxGz );

		minGx = Mathf.Min( minGx, Mathf.Min( startGx, endGx ) );
		maxGx = Mathf.Max( maxGx, Mathf.Max( startGx, endGx ) );
		minGz = Mathf.Min( minGz, Mathf.Min( startGz, endGz ) );
		maxGz = Mathf.Max( maxGz, Mathf.Max( startGz, endGz ) );

		int width = maxGx - minGx + 1;
		int height = maxGz - minGz + 1;
		if ( width <= 0 || height <= 0 )
			return false;

		long cellCountLong = ( long )width * height;
		if ( cellCountLong > MaxSearchCells )
		{
			if ( !ShrinkBoundsToBudget( def, startGx, startGz, endGx, endGz, ref minGx, ref maxGx, ref minGz, ref maxGz, out width, out height ) )
				return false;
			cellCountLong = ( long )width * height;
			if ( cellCountLong > MaxSearchCells )
				return false;
		}

		int cellCount = width * height;
		EnsureChunksLoaded( world, def, minGx, maxGx, minGz, maxGz );

		bool[] walkable = new bool[ cellCount ];
		float[] heights = new float[ cellCount ];
		float[] clearance = new float[ cellCount ];

		FillWalkableAndHeights( world, def, minGx, minGz, width, height, walkable, heights );

		int startIdx = ToIndex( startGx, startGz, minGx, minGz, width );
		int endIdx = ToIndex( endGx, endGz, minGx, minGz, width );
		if ( startIdx < 0 || startIdx >= cellCount || endIdx < 0 || endIdx >= cellCount )
			return false;

		if ( !walkable[ startIdx ] )
		{
			if ( !TrySnapToNearestWalkable( walkable, width, height, startIdx, out startIdx ) )
				return false;
			FromIndex( startIdx, minGx, minGz, width, out startGx, out startGz );
		}

		if ( !walkable[ endIdx ] )
		{
			if ( !TrySnapToNearestWalkable( walkable, width, height, endIdx, out endIdx ) )
				return false;
			FromIndex( endIdx, minGx, minGz, width, out endGx, out endGz );
		}

		if ( startIdx == endIdx )
		{
			Vector3 single = GlobalCellCenter( def, startGx, startGz, heights[ startIdx ] );
			path = TreasureSurfacePath.FromWaypoints( new List<Vector3> { single } );
			return true;
		}

		BuildClearanceField( walkable, width, height, cellSize, clearance );

		int[] cameFrom = new int[ cellCount ];
		for ( int i = 0; i < cellCount; i++ )
			cameFrom[ i ] = -1;

		float[] gScore = new float[ cellCount ];
		for ( int i = 0; i < cellCount; i++ )
			gScore[ i ] = float.PositiveInfinity;
		gScore[ startIdx ] = 0f;

		MinHeap open = new MinHeap( Mathf.Min( 256, cellCount ) );
		open.Push( startIdx, Heuristic( startGx, startGz, endGx, endGz, cellSize ) );

		bool[] closed = new bool[ cellCount ];
		int[] dx = settings.allowDiagonal ? NeighborDx8 : NeighborDx4;
		int[] dz = settings.allowDiagonal ? NeighborDz8 : NeighborDz4;
		int neighborCount = dx.Length;
		float edgeMargin = Mathf.Max( 0f, settings.edgeMargin );
		float edgePenalty = Mathf.Max( 0f, settings.edgePenalty );
		bool useHeightBlock = maxStep < float.MaxValue * 0.5f;

		while ( open.Count > 0 )
		{
			int current = open.Pop();
			if ( closed[ current ] )
				continue;
			closed[ current ] = true;

			if ( current == endIdx )
			{
				path = BuildPath( def, cameFrom, heights, minGx, minGz, width, startIdx, endIdx );
				return path.Success;
			}

			FromIndex( current, minGx, minGz, width, out int cx, out int cz );
			float currentHeight = heights[ current ];

			for ( int n = 0; n < neighborCount; n++ )
			{
				int nx = cx + dx[ n ];
				int nz = cz + dz[ n ];
				if ( nx < minGx || nx > maxGx || nz < minGz || nz > maxGz )
					continue;

				int neighbor = ToIndex( nx, nz, minGx, minGz, width );
				if ( closed[ neighbor ] || !walkable[ neighbor ] )
					continue;

				if ( dx[ n ] != 0 && dz[ n ] != 0 )
				{
					int orthA = ToIndex( cx + dx[ n ], cz, minGx, minGz, width );
					int orthB = ToIndex( cx, cz + dz[ n ], minGx, minGz, width );
					if ( orthA < 0 || orthA >= cellCount || !walkable[ orthA ] )
						continue;
					if ( orthB < 0 || orthB >= cellCount || !walkable[ orthB ] )
						continue;
				}

				if ( useHeightBlock )
				{
					float heightDelta = Mathf.Abs( heights[ neighbor ] - currentHeight );
					if ( heightDelta > maxStep )
						continue;
				}

				float stepDist = ( dx[ n ] != 0 && dz[ n ] != 0 ) ? cellSize * 1.41421356f : cellSize;
				float clearanceShortfall = Mathf.Max( 0f, edgeMargin - clearance[ neighbor ] );
				float stepCost = stepDist + edgePenalty * clearanceShortfall;
				float tentative = gScore[ current ] + stepCost;
				if ( tentative >= gScore[ neighbor ] )
					continue;

				cameFrom[ neighbor ] = current;
				gScore[ neighbor ] = tentative;
				float f = tentative + Heuristic( nx, nz, endGx, endGz, cellSize );
				open.Push( neighbor, f );
			}
		}

		return false;
	}

	/// <summary>
	/// Lenient paint cell under the point (loads chunk). Falls back to a local walkable snap.
	/// </summary>
	static bool TryResolvePathCell(
		TreasureSurfaceWorld world,
		TreasureSurfaceDefinition def,
		Vector3 worldPos,
		out int cellX,
		out int cellZ,
		out float height )
	{
		cellX = 0;
		cellZ = 0;
		height = def.baseHeight;

		if ( world.TryGetChunkCoord( worldPos, out TreasureChunkCoord coord ) )
			world.EnsureChunkLoaded( coord );

		if ( !TryWorldToGlobalCell( def, worldPos, out cellX, out cellZ ) )
		{
			if ( !TryWorldToGlobalCellClamped( def, worldPos, out cellX, out cellZ ) )
				return false;
		}

		if ( TryGetCellPaint( world, def, cellX, cellZ, out bool trav, out height ) && trav )
			return true;

		// Local spiral snap on loaded paint (does not depend on frozen/streamer nearest search).
		int res = Mathf.Max( 1, def.cellsPerChunk );
		int maxRadius = Mathf.Max( 8, res );
		for ( int r = 1; r <= maxRadius; r++ )
		{
			for ( int dz = -r; dz <= r; dz++ )
			{
				for ( int dx = -r; dx <= r; dx++ )
				{
					if ( Mathf.Max( Mathf.Abs( dx ), Mathf.Abs( dz ) ) != r )
						continue;

					int gx = cellX + dx;
					int gz = cellZ + dz;
					if ( !IsGlobalCellInWorld( def, gx, gz ) )
						continue;

					int chunkX = gx / res;
					int chunkZ = gz / res;
					world.EnsureChunkLoaded( new TreasureChunkCoord( chunkX, chunkZ ) );

					if ( !TryGetCellPaint( world, def, gx, gz, out bool nearTrav, out float nearHeight ) || !nearTrav )
						continue;

					cellX = gx;
					cellZ = gz;
					height = nearHeight;
					return true;
				}
			}
		}

		return false;
	}

	static bool TryGetCellPaint(
		TreasureSurfaceWorld world,
		TreasureSurfaceDefinition def,
		int globalX,
		int globalZ,
		out bool traversable,
		out float height )
	{
		traversable = false;
		height = def.baseHeight;
		int res = Mathf.Max( 1, def.cellsPerChunk );
		if ( !IsGlobalCellInWorld( def, globalX, globalZ ) )
			return false;

		int chunkX = globalX / res;
		int chunkZ = globalZ / res;
		int localX = globalX - chunkX * res;
		int localZ = globalZ - chunkZ * res;
		TreasureChunk chunk = world.GetLoadedChunk( new TreasureChunkCoord( chunkX, chunkZ ) );
		if ( chunk == null || !chunk.Loaded || chunk.PaintTraversable == null )
			return false;

		int idx = chunk.Index( localX, localZ );
		traversable = chunk.PaintTraversable[ idx ] != 0;
		height = chunk.SmoothedHeight != null ? chunk.SmoothedHeight[ idx ] : def.baseHeight;
		return true;
	}

	static bool IsGlobalCellInWorld( TreasureSurfaceDefinition def, int gx, int gz )
	{
		int totalX = def.ChunkCountX * def.cellsPerChunk;
		int totalZ = def.ChunkCountZ * def.cellsPerChunk;
		return gx >= 0 && gz >= 0 && gx < totalX && gz < totalZ;
	}

	static bool ShrinkBoundsToBudget(
		TreasureSurfaceDefinition def,
		int startGx,
		int startGz,
		int endGx,
		int endGz,
		ref int minGx,
		ref int maxGx,
		ref int minGz,
		ref int maxGz,
		out int width,
		out int height )
	{
		// Keep a corridor around the start–end axis instead of a fat AABB when over budget.
		int axisMinX = Mathf.Min( startGx, endGx );
		int axisMaxX = Mathf.Max( startGx, endGx );
		int axisMinZ = Mathf.Min( startGz, endGz );
		int axisMaxZ = Mathf.Max( startGz, endGz );

		int padCells = 8;
		while ( padCells >= 2 )
		{
			minGx = Mathf.Max( 0, axisMinX - padCells );
			maxGx = Mathf.Min( def.ChunkCountX * def.cellsPerChunk - 1, axisMaxX + padCells );
			minGz = Mathf.Max( 0, axisMinZ - padCells );
			maxGz = Mathf.Min( def.ChunkCountZ * def.cellsPerChunk - 1, axisMaxZ + padCells );
			width = maxGx - minGx + 1;
			height = maxGz - minGz + 1;
			if ( ( long )width * height <= MaxSearchCells )
				return true;
			padCells /= 2;
		}

		minGx = axisMinX;
		maxGx = axisMaxX;
		minGz = axisMinZ;
		maxGz = axisMaxZ;
		width = maxGx - minGx + 1;
		height = maxGz - minGz + 1;
		return width > 0 && height > 0 && ( long )width * height <= MaxSearchCells;
	}

	static TreasureSurfacePath BuildPath(
		TreasureSurfaceDefinition def,
		int[] cameFrom,
		float[] heights,
		int minGx,
		int minGz,
		int width,
		int startIdx,
		int endIdx )
	{
		List<int> reverse = new List<int>( 64 );
		int cursor = endIdx;
		while ( cursor >= 0 )
		{
			reverse.Add( cursor );
			if ( cursor == startIdx )
				break;
			cursor = cameFrom[ cursor ];
			if ( reverse.Count > cameFrom.Length + 2 )
				return TreasureSurfacePath.Failed;
		}

		if ( reverse.Count == 0 || reverse[ reverse.Count - 1 ] != startIdx )
			return TreasureSurfacePath.Failed;

		List<Vector3> points = new List<Vector3>( reverse.Count );
		for ( int i = reverse.Count - 1; i >= 0; i-- )
		{
			int idx = reverse[ i ];
			FromIndex( idx, minGx, minGz, width, out int gx, out int gz );
			points.Add( GlobalCellCenter( def, gx, gz, heights[ idx ] ) );
		}

		DropCollinear( points );
		return TreasureSurfacePath.FromWaypoints( points );
	}

	static void DropCollinear( List<Vector3> points )
	{
		if ( points == null || points.Count < 3 )
			return;

		int write = 1;
		for ( int i = 1; i < points.Count - 1; i++ )
		{
			Vector3 prev = points[ write - 1 ];
			Vector3 cur = points[ i ];
			Vector3 next = points[ i + 1 ];
			Vector3 a = new Vector3( cur.x - prev.x, 0f, cur.z - prev.z );
			Vector3 b = new Vector3( next.x - cur.x, 0f, next.z - cur.z );
			if ( a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f )
				continue;

			a.Normalize();
			b.Normalize();
			if ( Vector3.Dot( a, b ) >= CollinearDotThreshold )
				continue;

			points[ write ] = cur;
			write++;
		}

		points[ write ] = points[ points.Count - 1 ];
		write++;
		if ( write < points.Count )
			points.RemoveRange( write, points.Count - write );
	}

	static void BuildClearanceField( bool[] walkable, int width, int height, float cellSize, float[] clearance )
	{
		int count = width * height;
		int[] distCells = new int[ count ];
		Queue<int> queue = new Queue<int>( Mathf.Min( 256, count ) );

		for ( int i = 0; i < count; i++ )
		{
			if ( !walkable[ i ] )
			{
				distCells[ i ] = 0;
				queue.Enqueue( i );
			}
			else
			{
				distCells[ i ] = int.MaxValue;
			}
		}

		if ( queue.Count == 0 )
		{
			float openClearance = Mathf.Max( width, height ) * cellSize;
			for ( int i = 0; i < count; i++ )
				clearance[ i ] = walkable[ i ] ? openClearance : 0f;
			return;
		}

		while ( queue.Count > 0 )
		{
			int current = queue.Dequeue();
			int cd = distCells[ current ];
			int cx = current % width;
			int cz = current / width;

			for ( int n = 0; n < NeighborDx8.Length; n++ )
			{
				int nx = cx + NeighborDx8[ n ];
				int nz = cz + NeighborDz8[ n ];
				if ( nx < 0 || nx >= width || nz < 0 || nz >= height )
					continue;

				int neighbor = nz * width + nx;
				int nd = cd + 1;
				if ( nd >= distCells[ neighbor ] )
					continue;

				distCells[ neighbor ] = nd;
				queue.Enqueue( neighbor );
			}
		}

		float fallback = Mathf.Max( width, height ) * cellSize;
		for ( int i = 0; i < count; i++ )
		{
			int d = distCells[ i ];
			if ( d == int.MaxValue )
				clearance[ i ] = fallback;
			else
				clearance[ i ] = d * cellSize;
		}
	}

	static void FillWalkableAndHeights(
		TreasureSurfaceWorld world,
		TreasureSurfaceDefinition def,
		int minGx,
		int minGz,
		int width,
		int height,
		bool[] walkable,
		float[] heights )
	{
		int res = Mathf.Max( 1, def.cellsPerChunk );
		float baseHeight = def.baseHeight;

		for ( int lz = 0; lz < height; lz++ )
		{
			int gz = minGz + lz;
			for ( int lx = 0; lx < width; lx++ )
			{
				int gx = minGx + lx;
				int idx = lz * width + lx;
				int chunkX = gx / res;
				int chunkZ = gz / res;
				int localX = gx - chunkX * res;
				int localZ = gz - chunkZ * res;

				TreasureChunk chunk = world.GetLoadedChunk( new TreasureChunkCoord( chunkX, chunkZ ) );
				if ( chunk == null || !chunk.Loaded || chunk.PaintTraversable == null )
				{
					walkable[ idx ] = false;
					heights[ idx ] = baseHeight;
					continue;
				}

				int cell = chunk.Index( localX, localZ );
				walkable[ idx ] = chunk.PaintTraversable[ cell ] != 0;
				heights[ idx ] = chunk.SmoothedHeight != null ? chunk.SmoothedHeight[ cell ] : baseHeight;
			}
		}
	}

	static void EnsureChunksLoaded(
		TreasureSurfaceWorld world,
		TreasureSurfaceDefinition def,
		int minGx,
		int maxGx,
		int minGz,
		int maxGz )
	{
		int res = Mathf.Max( 1, def.cellsPerChunk );
		int minChunkX = Mathf.Clamp( minGx / res, 0, def.ChunkCountX - 1 );
		int maxChunkX = Mathf.Clamp( maxGx / res, 0, def.ChunkCountX - 1 );
		int minChunkZ = Mathf.Clamp( minGz / res, 0, def.ChunkCountZ - 1 );
		int maxChunkZ = Mathf.Clamp( maxGz / res, 0, def.ChunkCountZ - 1 );

		for ( int cz = minChunkZ; cz <= maxChunkZ; cz++ )
		{
			for ( int cx = minChunkX; cx <= maxChunkX; cx++ )
				world.EnsureChunkLoaded( new TreasureChunkCoord( cx, cz ) );
		}
	}

	static bool TrySnapToNearestWalkable( bool[] walkable, int width, int height, int fromIdx, out int foundIdx )
	{
		foundIdx = fromIdx;
		int fx = fromIdx % width;
		int fz = fromIdx / width;
		int maxR = Mathf.Max( width, height );
		for ( int r = 1; r <= maxR; r++ )
		{
			for ( int dz = -r; dz <= r; dz++ )
			{
				for ( int dx = -r; dx <= r; dx++ )
				{
					if ( Mathf.Max( Mathf.Abs( dx ), Mathf.Abs( dz ) ) != r )
						continue;

					int x = fx + dx;
					int z = fz + dz;
					if ( x < 0 || x >= width || z < 0 || z >= height )
						continue;

					int idx = z * width + x;
					if ( !walkable[ idx ] )
						continue;

					foundIdx = idx;
					return true;
				}
			}
		}

		return false;
	}

	static float Heuristic( int ax, int az, int bx, int bz, float cellSize )
	{
		float dx = ( ax - bx ) * cellSize;
		float dz = ( az - bz ) * cellSize;
		return Mathf.Sqrt( dx * dx + dz * dz );
	}

	static int ToIndex( int gx, int gz, int minGx, int minGz, int width )
	{
		return ( gz - minGz ) * width + ( gx - minGx );
	}

	static void FromIndex( int index, int minGx, int minGz, int width, out int gx, out int gz )
	{
		int lx = index % width;
		int lz = index / width;
		gx = minGx + lx;
		gz = minGz + lz;
	}

	static bool TryWorldToGlobalCell( TreasureSurfaceDefinition def, Vector3 world, out int cellX, out int cellZ )
	{
		cellX = 0;
		cellZ = 0;
		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float localX = world.x - def.worldOrigin.x + halfX;
		float localZ = world.z - def.worldOrigin.z + halfZ;
		if ( localX < 0f || localZ < 0f || localX >= def.worldSizeX || localZ >= def.worldSizeZ )
			return false;

		float cell = def.CellSize;
		int totalX = def.ChunkCountX * def.cellsPerChunk;
		int totalZ = def.ChunkCountZ * def.cellsPerChunk;
		cellX = Mathf.Clamp( Mathf.FloorToInt( localX / cell ), 0, totalX - 1 );
		cellZ = Mathf.Clamp( Mathf.FloorToInt( localZ / cell ), 0, totalZ - 1 );
		return true;
	}

	static bool TryWorldToGlobalCellClamped( TreasureSurfaceDefinition def, Vector3 world, out int cellX, out int cellZ )
	{
		cellX = 0;
		cellZ = 0;
		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float localX = Mathf.Clamp( world.x - def.worldOrigin.x + halfX, 0f, def.worldSizeX - 0.0001f );
		float localZ = Mathf.Clamp( world.z - def.worldOrigin.z + halfZ, 0f, def.worldSizeZ - 0.0001f );
		float cell = def.CellSize;
		int totalX = def.ChunkCountX * def.cellsPerChunk;
		int totalZ = def.ChunkCountZ * def.cellsPerChunk;
		cellX = Mathf.Clamp( Mathf.FloorToInt( localX / cell ), 0, totalX - 1 );
		cellZ = Mathf.Clamp( Mathf.FloorToInt( localZ / cell ), 0, totalZ - 1 );
		return true;
	}

	static Vector3 GlobalCellCenter( TreasureSurfaceDefinition def, int cellX, int cellZ, float height )
	{
		float halfX = def.worldSizeX * 0.5f;
		float halfZ = def.worldSizeZ * 0.5f;
		float cell = def.CellSize;
		float wx = def.worldOrigin.x - halfX + ( cellX + 0.5f ) * cell;
		float wz = def.worldOrigin.z - halfZ + ( cellZ + 0.5f ) * cell;
		return new Vector3( wx, height, wz );
	}

	sealed class MinHeap
	{
		struct Node
		{
			public int Index;
			public float Priority;
		}

		Node[] _items;
		int _count;

		public int Count => _count;

		public MinHeap( int capacity )
		{
			_items = new Node[ Mathf.Max( 16, capacity ) ];
			_count = 0;
		}

		public void Push( int index, float priority )
		{
			if ( _count >= _items.Length )
			{
				Node[] grown = new Node[ _items.Length * 2 ];
				for ( int i = 0; i < _items.Length; i++ )
					grown[ i ] = _items[ i ];
				_items = grown;
			}

			_items[ _count ] = new Node { Index = index, Priority = priority };
			SiftUp( _count );
			_count++;
		}

		public int Pop()
		{
			int result = _items[ 0 ].Index;
			_count--;
			if ( _count > 0 )
			{
				_items[ 0 ] = _items[ _count ];
				SiftDown( 0 );
			}

			return result;
		}

		void SiftUp( int i )
		{
			while ( i > 0 )
			{
				int parent = ( i - 1 ) >> 1;
				if ( _items[ i ].Priority >= _items[ parent ].Priority )
					break;

				Node tmp = _items[ i ];
				_items[ i ] = _items[ parent ];
				_items[ parent ] = tmp;
				i = parent;
			}
		}

		void SiftDown( int i )
		{
			while ( true )
			{
				int left = ( i << 1 ) + 1;
				if ( left >= _count )
					break;

				int right = left + 1;
				int smallest = left;
				if ( right < _count && _items[ right ].Priority < _items[ left ].Priority )
					smallest = right;

				if ( _items[ i ].Priority <= _items[ smallest ].Priority )
					break;

				Node tmp = _items[ i ];
				_items[ i ] = _items[ smallest ];
				_items[ smallest ] = tmp;
				i = smallest;
			}
		}
	}
}
