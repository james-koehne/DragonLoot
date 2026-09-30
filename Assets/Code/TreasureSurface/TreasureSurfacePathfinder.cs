using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sparse-grid A* on the treasure surface with edge-margin erosion.
/// Searches loaded chunks only (no forced streaming of the AABB).
/// </summary>
public static class TreasureSurfacePathfinder
{
	const float CollinearDotThreshold = 0.998f;
	const float Sqrt2 = 1.41421356f;

	static readonly int[] NeighborDx4 = { 1, -1, 0, 0 };
	static readonly int[] NeighborDz4 = { 0, 0, 1, -1 };
	static readonly int[] NeighborDx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
	static readonly int[] NeighborDz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

	static bool[] s_PaintWalkable;
	static bool[] s_SearchWalkable;
	static float[] s_Heights;
	static float[] s_GScore;
	static int[] s_CameFrom;
	static bool[] s_Closed;
	static int[] s_ErodeQueue;
	static int[] s_ErodeDist;
	static readonly MinHeap s_Open = new MinHeap( 256 );
	static readonly List<Vector3> s_WaypointScratch = new List<Vector3>( 128 );
	static readonly List<int> s_PathScratch = new List<int>( 128 );

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
		int stride = settings.ResolveNavStride();
		int maxCells = settings.ResolveMaxSearchCells();
		float fineCell = def.CellSize;
		float navCell = fineCell * stride;
		float maxStep = settings.ResolveMaxStepHeight();

		if ( !TryResolvePathCell( world, def, from, out int startFineX, out int startFineZ, out _ ) )
			return false;
		if ( !TryResolvePathCell( world, def, to, out int endFineX, out int endFineZ, out _ ) )
			return false;

		int startNavX = startFineX / stride;
		int startNavZ = startFineZ / stride;
		int endNavX = endFineX / stride;
		int endNavZ = endFineZ / stride;

		float pad = Mathf.Max( navCell * 2f, settings.searchPadding );
		if ( TryFindPathInBounds( world, def, startNavX, startNavZ, endNavX, endNavZ, pad, stride, navCell, maxStep, maxCells, settings, out path ) )
			return true;

		// One retry with larger pad if the first pass failed (detour may need more room).
		float retryPad = pad * 2f;
		return TryFindPathInBounds( world, def, startNavX, startNavZ, endNavX, endNavZ, retryPad, stride, navCell, maxStep, maxCells, settings, out path );
	}

	static bool TryFindPathInBounds(
		TreasureSurfaceWorld world,
		TreasureSurfaceDefinition def,
		int startNavX,
		int startNavZ,
		int endNavX,
		int endNavZ,
		float pad,
		int stride,
		float navCell,
		float maxStep,
		int maxCells,
		in TreasureSurfacePathSettings settings,
		out TreasureSurfacePath path )
	{
		path = TreasureSurfacePath.Failed;

		int totalFineX = def.ChunkCountX * def.cellsPerChunk;
		int totalFineZ = def.ChunkCountZ * def.cellsPerChunk;
		int totalNavX = ( totalFineX + stride - 1 ) / stride;
		int totalNavZ = ( totalFineZ + stride - 1 ) / stride;

		int startFineX = NavCenterFine( startNavX, stride, totalFineX );
		int startFineZ = NavCenterFine( startNavZ, stride, totalFineZ );
		int endFineX = NavCenterFine( endNavX, stride, totalFineX );
		int endFineZ = NavCenterFine( endNavZ, stride, totalFineZ );

		Vector3 startWorld = GlobalCellCenter( def, startFineX, startFineZ, def.baseHeight );
		Vector3 endWorld = GlobalCellCenter( def, endFineX, endFineZ, def.baseHeight );

		float minWx = Mathf.Min( startWorld.x, endWorld.x ) - pad;
		float maxWx = Mathf.Max( startWorld.x, endWorld.x ) + pad;
		float minWz = Mathf.Min( startWorld.z, endWorld.z ) - pad;
		float maxWz = Mathf.Max( startWorld.z, endWorld.z ) + pad;

		TryWorldToGlobalCellClamped( def, new Vector3( minWx, 0f, minWz ), out int minFineX, out int minFineZ );
		TryWorldToGlobalCellClamped( def, new Vector3( maxWx, 0f, maxWz ), out int maxFineX, out int maxFineZ );

		int minNavX = Mathf.Min( minFineX / stride, Mathf.Min( startNavX, endNavX ) );
		int maxNavX = Mathf.Max( maxFineX / stride, Mathf.Max( startNavX, endNavX ) );
		int minNavZ = Mathf.Min( minFineZ / stride, Mathf.Min( startNavZ, endNavZ ) );
		int maxNavZ = Mathf.Max( maxFineZ / stride, Mathf.Max( startNavZ, endNavZ ) );

		minNavX = Mathf.Clamp( minNavX, 0, totalNavX - 1 );
		maxNavX = Mathf.Clamp( maxNavX, 0, totalNavX - 1 );
		minNavZ = Mathf.Clamp( minNavZ, 0, totalNavZ - 1 );
		maxNavZ = Mathf.Clamp( maxNavZ, 0, totalNavZ - 1 );

		int width = maxNavX - minNavX + 1;
		int height = maxNavZ - minNavZ + 1;
		if ( width <= 0 || height <= 0 )
			return false;

		long cellCountLong = ( long )width * height;
		if ( cellCountLong > maxCells )
		{
			if ( !ShrinkNavBoundsToBudget( totalNavX, totalNavZ, startNavX, startNavZ, endNavX, endNavZ, maxCells, ref minNavX, ref maxNavX, ref minNavZ, ref maxNavZ, out width, out height ) )
				return false;
		}

		int cellCount = width * height;
		EnsureScratch( cellCount );

		FillNavWalkableAndHeights( world, def, minNavX, minNavZ, width, height, stride, totalFineX, totalFineZ, s_PaintWalkable, s_Heights );

		int startIdx = ToIndex( startNavX, startNavZ, minNavX, minNavZ, width );
		int endIdx = ToIndex( endNavX, endNavZ, minNavX, minNavZ, width );
		if ( startIdx < 0 || startIdx >= cellCount || endIdx < 0 || endIdx >= cellCount )
			return false;

		if ( !s_PaintWalkable[ startIdx ] )
		{
			if ( !TrySnapToNearestWalkable( s_PaintWalkable, width, height, startIdx, out startIdx ) )
				return false;
			FromIndex( startIdx, minNavX, minNavZ, width, out startNavX, out startNavZ );
		}

		if ( !s_PaintWalkable[ endIdx ] )
		{
			if ( !TrySnapToNearestWalkable( s_PaintWalkable, width, height, endIdx, out endIdx ) )
				return false;
			FromIndex( endIdx, minNavX, minNavZ, width, out endNavX, out endNavZ );
		}

		if ( startIdx == endIdx )
		{
			int fx = NavCenterFine( startNavX, stride, totalFineX );
			int fz = NavCenterFine( startNavZ, stride, totalFineZ );
			path = TreasureSurfacePath.FromWaypoints( new List<Vector3>
			{
				GlobalCellCenter( def, fx, fz, s_Heights[ startIdx ] )
			} );
			return true;
		}

		CopyBool( s_PaintWalkable, s_SearchWalkable, cellCount );

		int erodeRings = 0;
		if ( settings.edgeMargin > 0f )
			erodeRings = Mathf.Max( 1, Mathf.CeilToInt( settings.edgeMargin / navCell ) );

		if ( erodeRings > 0 )
			ErodeWalkable( s_SearchWalkable, width, height, cellCount, erodeRings );

		// Keep endpoints searchable even if they sit in the eroded band.
		s_SearchWalkable[ startIdx ] = true;
		s_SearchWalkable[ endIdx ] = true;

		return RunAStar(
			def,
			minNavX,
			minNavZ,
			width,
			height,
			cellCount,
			startIdx,
			endIdx,
			startNavX,
			startNavZ,
			endNavX,
			endNavZ,
			stride,
			navCell,
			totalFineX,
			totalFineZ,
			maxStep,
			Mathf.Max( 0f, settings.uphillWeight ),
			maxCells,
			settings.allowDiagonal,
			out path );
	}

	static bool RunAStar(
		TreasureSurfaceDefinition def,
		int minNavX,
		int minNavZ,
		int width,
		int height,
		int cellCount,
		int startIdx,
		int endIdx,
		int startNavX,
		int startNavZ,
		int endNavX,
		int endNavZ,
		int stride,
		float navCell,
		int totalFineX,
		int totalFineZ,
		float maxStep,
		float uphillWeight,
		int expansionCap,
		bool allowDiagonal,
		out TreasureSurfacePath path )
	{
		path = TreasureSurfacePath.Failed;

		for ( int i = 0; i < cellCount; i++ )
		{
			s_CameFrom[ i ] = -1;
			s_GScore[ i ] = float.PositiveInfinity;
			s_Closed[ i ] = false;
		}

		s_GScore[ startIdx ] = 0f;
		s_Open.Clear();
		s_Open.Push( startIdx, OctileHeuristic( startNavX, startNavZ, endNavX, endNavZ, navCell ) );

		int[] dx = allowDiagonal ? NeighborDx8 : NeighborDx4;
		int[] dz = allowDiagonal ? NeighborDz8 : NeighborDz4;
		int neighborCount = dx.Length;
		bool useHeightBlock = maxStep < float.MaxValue * 0.5f;
		int closedCount = 0;

		while ( s_Open.Count > 0 )
		{
			int current = s_Open.Pop();
			if ( s_Closed[ current ] )
				continue;

			s_Closed[ current ] = true;
			closedCount++;
			if ( closedCount > expansionCap )
				return false;

			if ( current == endIdx )
			{
				path = BuildPath( def, s_CameFrom, s_Heights, minNavX, minNavZ, width, startIdx, endIdx, stride, totalFineX, totalFineZ );
				return path.Success;
			}

			FromIndex( current, minNavX, minNavZ, width, out int cx, out int cz );
			float currentHeight = s_Heights[ current ];

			for ( int n = 0; n < neighborCount; n++ )
			{
				int nx = cx + dx[ n ];
				int nz = cz + dz[ n ];
				if ( nx < minNavX || nx > minNavX + width - 1 || nz < minNavZ || nz > minNavZ + height - 1 )
					continue;

				int neighbor = ToIndex( nx, nz, minNavX, minNavZ, width );
				if ( s_Closed[ neighbor ] || !s_SearchWalkable[ neighbor ] )
					continue;

				if ( dx[ n ] != 0 && dz[ n ] != 0 )
				{
					int orthA = ToIndex( cx + dx[ n ], cz, minNavX, minNavZ, width );
					int orthB = ToIndex( cx, cz + dz[ n ], minNavX, minNavZ, width );
					if ( orthA < 0 || orthA >= cellCount || !s_SearchWalkable[ orthA ] )
						continue;
					if ( orthB < 0 || orthB >= cellCount || !s_SearchWalkable[ orthB ] )
						continue;
				}

				float neighborHeight = s_Heights[ neighbor ];
				float heightDelta = neighborHeight - currentHeight;
				if ( useHeightBlock && Mathf.Abs( heightDelta ) > maxStep )
					continue;

				float stepDist = ( dx[ n ] != 0 && dz[ n ] != 0 ) ? navCell * Sqrt2 : navCell;
				if ( uphillWeight > 0f && heightDelta > 0f )
					stepDist += heightDelta * uphillWeight;

				float tentative = s_GScore[ current ] + stepDist;
				if ( tentative >= s_GScore[ neighbor ] )
					continue;

				s_CameFrom[ neighbor ] = current;
				s_GScore[ neighbor ] = tentative;
				float f = tentative + OctileHeuristic( nx, nz, endNavX, endNavZ, navCell );
				s_Open.Push( neighbor, f );
			}
		}

		return false;
	}

	static void ErodeWalkable( bool[] walkable, int width, int height, int cellCount, int rings )
	{
		EnsureErodeScratch( cellCount );
		int queueHead = 0;
		int queueTail = 0;

		for ( int i = 0; i < cellCount; i++ )
		{
			s_ErodeDist[ i ] = -1;
			if ( walkable[ i ] )
				continue;

			s_ErodeDist[ i ] = 0;
			s_ErodeQueue[ queueTail++ ] = i;
		}

		while ( queueHead < queueTail )
		{
			int current = s_ErodeQueue[ queueHead++ ];
			int cd = s_ErodeDist[ current ];
			if ( cd >= rings )
				continue;

			int cx = current % width;
			int cz = current / width;
			for ( int n = 0; n < NeighborDx8.Length; n++ )
			{
				int nx = cx + NeighborDx8[ n ];
				int nz = cz + NeighborDz8[ n ];
				if ( nx < 0 || nx >= width || nz < 0 || nz >= height )
					continue;

				int neighbor = nz * width + nx;
				if ( s_ErodeDist[ neighbor ] >= 0 )
					continue;

				s_ErodeDist[ neighbor ] = cd + 1;
				s_ErodeQueue[ queueTail++ ] = neighbor;
			}
		}

		for ( int i = 0; i < cellCount; i++ )
		{
			int d = s_ErodeDist[ i ];
			if ( d > 0 && d <= rings )
				walkable[ i ] = false;
		}
	}

	static void FillNavWalkableAndHeights(
		TreasureSurfaceWorld world,
		TreasureSurfaceDefinition def,
		int minNavX,
		int minNavZ,
		int width,
		int height,
		int stride,
		int totalFineX,
		int totalFineZ,
		bool[] walkable,
		float[] heights )
	{
		int res = Mathf.Max( 1, def.cellsPerChunk );
		float baseHeight = def.baseHeight;
		int half = stride / 2;

		TreasureChunk lastChunk = null;
		int lastChunkX = int.MinValue;
		int lastChunkZ = int.MinValue;

		for ( int lz = 0; lz < height; lz++ )
		{
			int navZ = minNavZ + lz;
			int fineZ = navZ * stride + half;
			if ( fineZ >= totalFineZ )
				fineZ = totalFineZ - 1;

			for ( int lx = 0; lx < width; lx++ )
			{
				int navX = minNavX + lx;
				int fineX = navX * stride + half;
				if ( fineX >= totalFineX )
					fineX = totalFineX - 1;

				int idx = lz * width + lx;
				int chunkX = fineX / res;
				int chunkZ = fineZ / res;
				int localX = fineX - chunkX * res;
				int localZ = fineZ - chunkZ * res;

				if ( chunkX != lastChunkX || chunkZ != lastChunkZ )
				{
					lastChunkX = chunkX;
					lastChunkZ = chunkZ;
					lastChunk = world.GetLoadedChunk( new TreasureChunkCoord( chunkX, chunkZ ) );
				}

				if ( lastChunk == null || !lastChunk.Loaded || lastChunk.PaintTraversable == null )
				{
					walkable[ idx ] = false;
					heights[ idx ] = baseHeight;
					continue;
				}

				int cell = lastChunk.Index( localX, localZ );
				walkable[ idx ] = lastChunk.PaintTraversable[ cell ] != 0;
				heights[ idx ] = lastChunk.SmoothedHeight != null ? lastChunk.SmoothedHeight[ cell ] : baseHeight;
			}
		}
	}

	static bool ShrinkNavBoundsToBudget(
		int totalNavX,
		int totalNavZ,
		int startNavX,
		int startNavZ,
		int endNavX,
		int endNavZ,
		int maxCells,
		ref int minNavX,
		ref int maxNavX,
		ref int minNavZ,
		ref int maxNavZ,
		out int width,
		out int height )
	{
		int axisMinX = Mathf.Min( startNavX, endNavX );
		int axisMaxX = Mathf.Max( startNavX, endNavX );
		int axisMinZ = Mathf.Min( startNavZ, endNavZ );
		int axisMaxZ = Mathf.Max( startNavZ, endNavZ );

		int padCells = Mathf.Max( axisMaxX - axisMinX, axisMaxZ - axisMinZ ) + 8;
		while ( padCells >= 1 )
		{
			minNavX = Mathf.Max( 0, axisMinX - padCells );
			maxNavX = Mathf.Min( totalNavX - 1, axisMaxX + padCells );
			minNavZ = Mathf.Max( 0, axisMinZ - padCells );
			maxNavZ = Mathf.Min( totalNavZ - 1, axisMaxZ + padCells );
			width = maxNavX - minNavX + 1;
			height = maxNavZ - minNavZ + 1;
			if ( ( long )width * height <= maxCells )
				return true;
			padCells /= 2;
		}

		minNavX = axisMinX;
		maxNavX = axisMaxX;
		minNavZ = axisMinZ;
		maxNavZ = axisMaxZ;
		width = maxNavX - minNavX + 1;
		height = maxNavZ - minNavZ + 1;
		return width > 0 && height > 0 && ( long )width * height <= maxCells;
	}

	static TreasureSurfacePath BuildPath(
		TreasureSurfaceDefinition def,
		int[] cameFrom,
		float[] heights,
		int minNavX,
		int minNavZ,
		int width,
		int startIdx,
		int endIdx,
		int stride,
		int totalFineX,
		int totalFineZ )
	{
		s_PathScratch.Clear();
		int cursor = endIdx;
		while ( cursor >= 0 )
		{
			s_PathScratch.Add( cursor );
			if ( cursor == startIdx )
				break;
			cursor = cameFrom[ cursor ];
			if ( s_PathScratch.Count > cameFrom.Length + 2 )
				return TreasureSurfacePath.Failed;
		}

		if ( s_PathScratch.Count == 0 || s_PathScratch[ s_PathScratch.Count - 1 ] != startIdx )
			return TreasureSurfacePath.Failed;

		s_WaypointScratch.Clear();
		for ( int i = s_PathScratch.Count - 1; i >= 0; i-- )
		{
			int idx = s_PathScratch[ i ];
			FromIndex( idx, minNavX, minNavZ, width, out int navX, out int navZ );
			int fx = NavCenterFine( navX, stride, totalFineX );
			int fz = NavCenterFine( navZ, stride, totalFineZ );
			s_WaypointScratch.Add( GlobalCellCenter( def, fx, fz, heights[ idx ] ) );
		}

		DropCollinear( s_WaypointScratch );
		return TreasureSurfacePath.FromWaypoints( s_WaypointScratch );
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

	/// <summary>
	/// Lenient paint cell under the point. Only ensures the chunk under the query (not the full AABB).
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

	static float OctileHeuristic( int ax, int az, int bx, int bz, float navCell )
	{
		int dx = Mathf.Abs( ax - bx );
		int dz = Mathf.Abs( az - bz );
		int min = dx < dz ? dx : dz;
		int max = dx > dz ? dx : dz;
		return ( max + ( Sqrt2 - 1f ) * min ) * navCell;
	}

	static int NavCenterFine( int nav, int stride, int totalFine )
	{
		int fine = nav * stride + stride / 2;
		if ( fine >= totalFine )
			fine = totalFine - 1;
		if ( fine < 0 )
			fine = 0;
		return fine;
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

	static void EnsureScratch( int cellCount )
	{
		if ( s_PaintWalkable == null || s_PaintWalkable.Length < cellCount )
		{
			s_PaintWalkable = new bool[ cellCount ];
			s_SearchWalkable = new bool[ cellCount ];
			s_Heights = new float[ cellCount ];
			s_GScore = new float[ cellCount ];
			s_CameFrom = new int[ cellCount ];
			s_Closed = new bool[ cellCount ];
		}
	}

	static void EnsureErodeScratch( int cellCount )
	{
		if ( s_ErodeQueue == null || s_ErodeQueue.Length < cellCount )
		{
			s_ErodeQueue = new int[ cellCount ];
			s_ErodeDist = new int[ cellCount ];
		}
	}

	static void CopyBool( bool[] src, bool[] dst, int count )
	{
		for ( int i = 0; i < count; i++ )
			dst[ i ] = src[ i ];
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

		public void Clear()
		{
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
