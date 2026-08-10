using System.Collections.Generic;

using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Chunked MeshColliders for a gold pile. Digs only rebake tiles overlapping the dirty heightfield rect.
/// </summary>
public sealed class GoldPileColliderTiles
{
	const string RootName = "GoldPileColliders";
	const string TileNamePrefix = "ColliderTile_";
	const HideFlags RuntimeHideFlags = HideFlags.HideAndDontSave;
	const MeshColliderCookingOptions CookingOptions = MeshColliderCookingOptions.UseFastMidphase;

	struct BakeJob : IJob
	{
		public EntityId MeshId;
		public MeshColliderCookingOptions Options;

		public void Execute()
		{
			Physics.BakeMesh( MeshId, false, Options );
		}
	}

	public struct Tile
	{
		public GameObject Go;
		public MeshCollider Collider;
		public Mesh Mesh;
		public Vector3[] BaseVerts;
		public Vector3[] DisplacedVerts;
		public int[] Tris;
		public int GridMinX;
		public int GridMaxX;
		public int GridMinZ;
		public int GridMaxZ;
		public int VertCountX;
		public int VertCountZ;
		public bool Dirty;
		public bool VertsPendingUpload;
		public bool CookPending;
		public JobHandle BakeHandle;
		public bool BakeJobPending;
		public int BakeSerial;
		public int ScheduledBakeSerial;
		public bool RebakeAfterCurrent;
	}

	readonly List<int> _dirtyIndices = new List<int>( 32 );
	readonly List<int> _cookQueue = new List<int>( 32 );

	Transform _parent;
	GameObject _root;
	Tile[] _tiles;
	int _tileCountX;
	int _tileCountZ;
	int _colliderResolution;
	float _worldSize;
	float _chunkSize;
	bool _enabled = true;

	public int TileCount => _tiles != null ? _tiles.Length : 0;
	public int TileCountX => _tileCountX;
	public int TileCountZ => _tileCountZ;
	public int ColliderResolution => _colliderResolution;
	public float ChunkSize => _chunkSize;
	public MeshCollider FirstCollider =>
		_tiles != null && _tiles.Length > 0 ? _tiles[ 0 ].Collider : null;

	public void SetEnabled( bool enabled )
	{
		_enabled = enabled;
		if ( _tiles == null )
			return;

		for ( int i = 0; i < _tiles.Length; i++ )
		{
			MeshCollider col = _tiles[ i ].Collider;
			if ( col != null )
				col.enabled = enabled;
		}
	}

	public void Release()
	{
		CancelAllBakes();
		DestroyTiles();
		_tiles = null;
		_tileCountX = 0;
		_tileCountZ = 0;
		_colliderResolution = 0;
		_dirtyIndices.Clear();
		_cookQueue.Clear();
	}

	public void Build(
		Transform parent,
		int colliderResolution,
		float worldSize,
		float chunkSize,
		int layer )
	{
		Release();

		_parent = parent;
		_colliderResolution = Mathf.Max( 2, colliderResolution );
		_worldSize = Mathf.Max( 0.1f, worldSize );
		_chunkSize = Mathf.Max( 1f, chunkSize );

		_tileCountX = Mathf.Max( 1, Mathf.CeilToInt( _worldSize / _chunkSize ) );
		_tileCountZ = Mathf.Max( 1, Mathf.CeilToInt( _worldSize / _chunkSize ) );

		EnsureRoot( layer );

		int cells = _colliderResolution - 1;
		int baseCellsX = cells / _tileCountX;
		int remX = cells % _tileCountX;
		int baseCellsZ = cells / _tileCountZ;
		int remZ = cells % _tileCountZ;

		int[] xEdges = new int[ _tileCountX + 1 ];
		int[] zEdges = new int[ _tileCountZ + 1 ];
		xEdges[ 0 ] = 0;
		zEdges[ 0 ] = 0;
		for ( int tx = 0; tx < _tileCountX; tx++ )
			xEdges[ tx + 1 ] = xEdges[ tx ] + baseCellsX + ( tx < remX ? 1 : 0 );
		for ( int tz = 0; tz < _tileCountZ; tz++ )
			zEdges[ tz + 1 ] = zEdges[ tz ] + baseCellsZ + ( tz < remZ ? 1 : 0 );

		_tiles = new Tile[ _tileCountX * _tileCountZ ];
		for ( int tz = 0; tz < _tileCountZ; tz++ )
		{
			for ( int tx = 0; tx < _tileCountX; tx++ )
			{
				int index = tz * _tileCountX + tx;
				_tiles[ index ] = CreateTile(
					tx,
					tz,
					xEdges[ tx ],
					xEdges[ tx + 1 ],
					zEdges[ tz ],
					zEdges[ tz + 1 ],
					layer );
			}
		}
	}

	public void MarkAllDirty()
	{
		if ( _tiles == null )
			return;

		_dirtyIndices.Clear();
		for ( int i = 0; i < _tiles.Length; i++ )
		{
			_tiles[ i ].Dirty = true;
			_dirtyIndices.Add( i );
		}
	}

	/// <summary>
	/// Marks tiles overlapping a heightfield dirty rect (heightfield cell indices).
	/// </summary>
	public void MarkDirtyFromHeightfieldRect(
		int heightRes,
		int minX,
		int maxX,
		int minZ,
		int maxZ,
		bool full )
	{
		if ( _tiles == null || _tiles.Length == 0 )
			return;

		if ( full || heightRes <= 1 )
		{
			MarkAllDirty();
			return;
		}

		int res = _colliderResolution;
		float inv = 1f / ( heightRes - 1 );
		float u0 = minX * inv;
		float u1 = maxX * inv;
		float v0 = minZ * inv;
		float v1 = maxZ * inv;
		int cMinX = Mathf.Clamp( Mathf.FloorToInt( u0 * ( res - 1 ) ) - 1, 0, res - 1 );
		int cMaxX = Mathf.Clamp( Mathf.CeilToInt( u1 * ( res - 1 ) ) + 1, 0, res - 1 );
		int cMinZ = Mathf.Clamp( Mathf.FloorToInt( v0 * ( res - 1 ) ) - 1, 0, res - 1 );
		int cMaxZ = Mathf.Clamp( Mathf.CeilToInt( v1 * ( res - 1 ) ) + 1, 0, res - 1 );

		for ( int i = 0; i < _tiles.Length; i++ )
		{
			ref Tile tile = ref _tiles[ i ];
			if ( tile.GridMaxX < cMinX || tile.GridMinX > cMaxX
				|| tile.GridMaxZ < cMinZ || tile.GridMinZ > cMaxZ )
				continue;

			if ( !tile.Dirty )
			{
				tile.Dirty = true;
				_dirtyIndices.Add( i );
			}
		}
	}

	public bool HasDirtyOrPending()
	{
		if ( _tiles == null )
			return false;

		for ( int i = 0; i < _tiles.Length; i++ )
		{
			ref Tile tile = ref _tiles[ i ];
			if ( tile.Dirty || tile.CookPending || tile.BakeJobPending || tile.RebakeAfterCurrent )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Displaces dirty tile verts from the heightfield. Optionally cooks immediately (bind / edit mode).
	/// </summary>
	public void SyncDirty( GoldPileHeightfield heightfield, bool forceCook )
	{
		if ( _tiles == null || heightfield == null )
			return;

		GoldPileEditTiming.Begin( "GoldPile.SyncColliderTiles" );
		System.Diagnostics.Stopwatch sw = GoldPileEditTiming.StartWatchIfEnabled();

		int dirtyCount = 0;
		int vertCount = 0;
		for ( int i = 0; i < _tiles.Length; i++ )
		{
			ref Tile tile = ref _tiles[ i ];
			if ( !tile.Dirty )
				continue;

			DisplaceTile( ref tile, heightfield );
			dirtyCount++;
			vertCount += tile.DisplacedVerts != null ? tile.DisplacedVerts.Length : 0;

			if ( IsBakeRunning( ref tile ) )
			{
				tile.VertsPendingUpload = true;
				tile.RebakeAfterCurrent = true;
				tile.Dirty = false;
				tile.CookPending = true;
				continue;
			}

			UploadTileVerts( ref tile );
			tile.Dirty = false;

			if ( forceCook )
			{
				CookTileSync( ref tile );
				tile.CookPending = false;
			}
			else
				tile.CookPending = true;
		}

		_dirtyIndices.Clear();

		if ( sw != null )
		{
			sw.Stop();
			GoldPileEditTiming.Record(
				"collider.displaceTiles",
				sw.Elapsed.TotalMilliseconds,
				$"tiles={dirtyCount} verts={vertCount} res={_colliderResolution}" );
		}

		GoldPileEditTiming.End();
	}

	/// <summary>Schedules async PhysX bakes for tiles with CookPending. Returns tiles scheduled.</summary>
	public int RequestAsyncBakes( int maxTiles )
	{
		if ( _tiles == null || maxTiles <= 0 )
			return 0;

		_cookQueue.Clear();
		for ( int i = 0; i < _tiles.Length; i++ )
		{
			ref Tile tile = ref _tiles[ i ];
			if ( !tile.CookPending )
				continue;
			if ( IsBakeRunning( ref tile ) )
			{
				tile.RebakeAfterCurrent = true;
				continue;
			}

			_cookQueue.Add( i );
		}

		int scheduled = 0;
		int limit = Mathf.Min( maxTiles, _cookQueue.Count );
		for ( int q = 0; q < limit; q++ )
		{
			int i = _cookQueue[ q ];
			ref Tile tile = ref _tiles[ i ];
			StartBake( ref tile );
			tile.CookPending = false;
			scheduled++;
		}

		return scheduled;
	}

	public void ApplyCompletedBakes()
	{
		if ( _tiles == null )
			return;

		for ( int i = 0; i < _tiles.Length; i++ )
		{
			ref Tile tile = ref _tiles[ i ];
			if ( !tile.BakeJobPending || !tile.BakeHandle.IsCompleted )
				continue;

			tile.BakeHandle.Complete();
			tile.BakeJobPending = false;

			if ( tile.ScheduledBakeSerial != tile.BakeSerial )
			{
				if ( tile.VertsPendingUpload )
				{
					UploadTileVerts( ref tile );
					tile.RebakeAfterCurrent = true;
				}

				if ( tile.RebakeAfterCurrent )
				{
					tile.RebakeAfterCurrent = false;
					StartBake( ref tile );
				}

				continue;
			}

			GoldPileEditTiming.Begin( "GoldPile.ApplyBakedColliderTile" );
			ApplyBakedSharedMesh( ref tile );
			GoldPileEditTiming.End();

			if ( tile.VertsPendingUpload )
			{
				UploadTileVerts( ref tile );
				tile.RebakeAfterCurrent = true;
			}

			if ( tile.RebakeAfterCurrent )
			{
				tile.RebakeAfterCurrent = false;
				StartBake( ref tile );
			}
		}
	}

	public void CancelAllBakes()
	{
		if ( _tiles == null )
			return;

		for ( int i = 0; i < _tiles.Length; i++ )
		{
			ref Tile tile = ref _tiles[ i ];
			if ( tile.BakeJobPending )
			{
				tile.BakeHandle.Complete();
				tile.BakeJobPending = false;
			}

			tile.BakeSerial++;
			tile.RebakeAfterCurrent = false;
		}
	}

	void EnsureRoot( int layer )
	{
		if ( _parent == null )
			return;

		// Purge deferred-destroy leftovers from a prior rebuild.
		for ( int i = _parent.childCount - 1; i >= 0; i-- )
		{
			Transform child = _parent.GetChild( i );
			if ( child == null )
				continue;
			if ( child.name != RootName && child.name != "~" + RootName )
				continue;

			if ( Application.isPlaying )
				Object.Destroy( child.gameObject );
			else
				Object.DestroyImmediate( child.gameObject );
		}

		_root = new GameObject( RootName );
		_root.hideFlags = RuntimeHideFlags;
		_root.transform.SetParent( _parent, false );
		_root.layer = layer;
	}

	Tile CreateTile(
		int tx,
		int tz,
		int gridMinX,
		int gridMaxX,
		int gridMinZ,
		int gridMaxZ,
		int layer )
	{
		string name = TileNamePrefix + tx + "_" + tz;
		GameObject go = new GameObject( name );
		go.hideFlags = RuntimeHideFlags;
		go.transform.SetParent( _root.transform, false );
		go.layer = layer;

		MeshCollider col = go.GetComponent<MeshCollider>();
		if ( col == null )
			col = go.AddComponent<MeshCollider>();
		col.cookingOptions = CookingOptions;
		col.convex = false;
		col.enabled = _enabled;

		int vertCountX = gridMaxX - gridMinX + 1;
		int vertCountZ = gridMaxZ - gridMinZ + 1;
		int vertCount = vertCountX * vertCountZ;
		Vector3[] baseVerts = new Vector3[ vertCount ];
		Vector3[] displaced = new Vector3[ vertCount ];
		float half = _worldSize * 0.5f;
		float step = _worldSize / Mathf.Max( 1, _colliderResolution - 1 );

		for ( int z = 0; z < vertCountZ; z++ )
		{
			int gz = gridMinZ + z;
			float lz = -half + gz * step;
			for ( int x = 0; x < vertCountX; x++ )
			{
				int gx = gridMinX + x;
				int i = z * vertCountX + x;
				float lx = -half + gx * step;
				baseVerts[ i ] = new Vector3( lx, 0f, lz );
				displaced[ i ] = baseVerts[ i ];
			}
		}

		int quadCount = Mathf.Max( 0, ( vertCountX - 1 ) * ( vertCountZ - 1 ) );
		int[] tris = new int[ quadCount * 6 ];
		int t = 0;
		for ( int z = 0; z < vertCountZ - 1; z++ )
		{
			for ( int x = 0; x < vertCountX - 1; x++ )
			{
				int i = z * vertCountX + x;
				tris[ t++ ] = i;
				tris[ t++ ] = i + vertCountX;
				tris[ t++ ] = i + 1;
				tris[ t++ ] = i + 1;
				tris[ t++ ] = i + vertCountX;
				tris[ t++ ] = i + vertCountX + 1;
			}
		}

		Mesh mesh = new Mesh { name = "GoldPileCollider_" + tx + "_" + tz };
		mesh.hideFlags = RuntimeHideFlags;
		mesh.indexFormat = vertCount > 65000
			? UnityEngine.Rendering.IndexFormat.UInt32
			: UnityEngine.Rendering.IndexFormat.UInt16;
		mesh.MarkDynamic();
		mesh.vertices = displaced;
		mesh.triangles = tris;
		mesh.RecalculateBounds();

		return new Tile
		{
			Go = go,
			Collider = col,
			Mesh = mesh,
			BaseVerts = baseVerts,
			DisplacedVerts = displaced,
			Tris = tris,
			GridMinX = gridMinX,
			GridMaxX = gridMaxX,
			GridMinZ = gridMinZ,
			GridMaxZ = gridMaxZ,
			VertCountX = vertCountX,
			VertCountZ = vertCountZ,
			Dirty = true,
			CookPending = false
		};
	}

	void DisplaceTile( ref Tile tile, GoldPileHeightfield heightfield )
	{
		int res = _colliderResolution;
		float maxH = heightfield.MaxHeight;
		float ground = heightfield.GroundLevel;
		int vertCountX = tile.VertCountX;

		for ( int z = 0; z < tile.VertCountZ; z++ )
		{
			int gz = tile.GridMinZ + z;
			float v = res <= 1 ? 0.5f : ( float )gz / ( res - 1 );
			for ( int x = 0; x < tile.VertCountX; x++ )
			{
				int gx = tile.GridMinX + x;
				float u = res <= 1 ? 0.5f : ( float )gx / ( res - 1 );
				int i = z * vertCountX + x;
				float h = heightfield.SampleNormalizedUV( u, v ) * maxH;
				if ( h < ground )
					h = -1f;
				Vector3 b = tile.BaseVerts[ i ];
				tile.DisplacedVerts[ i ] = new Vector3( b.x, h, b.z );
			}
		}
	}

	void UploadTileVerts( ref Tile tile )
	{
		if ( tile.Mesh == null || tile.DisplacedVerts == null )
			return;

		tile.Mesh.vertices = tile.DisplacedVerts;
		tile.Mesh.RecalculateBounds();
		tile.VertsPendingUpload = false;
	}

	void StartBake( ref Tile tile )
	{
		if ( tile.Mesh == null )
			return;

		if ( tile.BakeJobPending )
		{
			tile.BakeHandle.Complete();
			tile.BakeJobPending = false;
		}

		if ( tile.VertsPendingUpload )
			UploadTileVerts( ref tile );

		if ( tile.Collider != null )
			tile.Collider.cookingOptions = CookingOptions;

		tile.ScheduledBakeSerial = ++tile.BakeSerial;
		tile.RebakeAfterCurrent = false;
		tile.BakeHandle = new BakeJob
		{
			MeshId = tile.Mesh.GetEntityId(),
			Options = CookingOptions
		}.Schedule();
		tile.BakeJobPending = true;
	}

	void CookTileSync( ref Tile tile )
	{
		if ( tile.Collider == null || tile.Mesh == null )
			return;

		if ( tile.BakeJobPending )
		{
			tile.BakeHandle.Complete();
			tile.BakeJobPending = false;
		}

		if ( tile.VertsPendingUpload )
			UploadTileVerts( ref tile );

		GoldPileEditTiming.Begin( "GoldPile.CookColliderTile" );
		System.Diagnostics.Stopwatch sw = GoldPileEditTiming.StartWatchIfEnabled();

		tile.Collider.cookingOptions = CookingOptions;
		Physics.BakeMesh( tile.Mesh.GetEntityId(), false, CookingOptions );
		ApplyBakedSharedMesh( ref tile );

		if ( sw != null )
		{
			sw.Stop();
			GoldPileEditTiming.Record( "collider.cookSyncTile", sw.Elapsed.TotalMilliseconds );
		}

		GoldPileEditTiming.End();
	}

	void ApplyBakedSharedMesh( ref Tile tile )
	{
		if ( tile.Collider == null || tile.Mesh == null )
			return;

		tile.Collider.cookingOptions = CookingOptions;
		if ( tile.Collider.sharedMesh != tile.Mesh )
			tile.Collider.sharedMesh = tile.Mesh;
		else
		{
			bool wasEnabled = tile.Collider.enabled;
			tile.Collider.enabled = false;
			tile.Collider.sharedMesh = tile.Mesh;
			tile.Collider.enabled = wasEnabled && _enabled;
		}
	}

	static bool IsBakeRunning( ref Tile tile )
	{
		return tile.BakeJobPending && !tile.BakeHandle.IsCompleted;
	}

	void DestroyTiles()
	{
		if ( _tiles != null )
		{
			for ( int i = 0; i < _tiles.Length; i++ )
			{
				ref Tile tile = ref _tiles[ i ];
				if ( tile.Mesh != null )
				{
#if UNITY_EDITOR
					if ( !Application.isPlaying )
						Object.DestroyImmediate( tile.Mesh );
					else
#endif
						Object.Destroy( tile.Mesh );
					tile.Mesh = null;
				}

				// Clear sharedMesh before destroying the GO so PhysX drops the cooked mesh.
				if ( tile.Collider != null )
					tile.Collider.sharedMesh = null;

				tile.Go = null;
				tile.Collider = null;
			}
		}

		if ( _root != null )
		{
			// Rename + disable so a deferred Destroy cannot collide with a fresh root
			// or keep empty MeshColliders in the physics world until end of frame.
			_root.name = "~" + RootName;
			_root.SetActive( false );
			if ( Application.isPlaying )
				Object.Destroy( _root );
			else
				Object.DestroyImmediate( _root );
			_root = null;
		}
	}
}
