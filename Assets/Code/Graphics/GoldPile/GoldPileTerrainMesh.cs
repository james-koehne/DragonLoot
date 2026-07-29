using UnityEngine;

/// <summary>
/// Fixed-topology pile grid mesh. Visual displace via deform texture; collider verts synced on CPU.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileTerrainMesh : MonoBehaviour
{
	const string DeformEnabledProp = "_DeformEnabled";
	const string DeformMapProp = "_DeformMap";
	const string DeformScaleProp = "_DeformScale";
	const string DeformWorldSizeProp = "_DeformWorldSize";
	const string DeformResolutionProp = "_DeformResolution";
	const string GroundLevelProp = "_GroundLevelHeight";
	const int MinColliderResolution = 16;
	/// <summary>Minimum seconds between MeshCollider cooks (verts still update every dirty LateUpdate).</summary>
	const float ColliderCookMinInterval = 0.05f;

	[SerializeField]
	[Min( 8 )]
	int resolution = 64;

	[SerializeField]
	Material pileMaterial;

	[SerializeField]
	bool createChild = true;

	MeshFilter _filter;
	MeshRenderer _renderer;
	MeshCollider _collider;
	Mesh _visualMesh;
	Mesh _colliderMesh;
	Vector3[] _baseVerts;
	Vector3[] _colliderBaseVerts;
	Vector3[] _displacedVerts;
	Vector2[] _uvs;
	int[] _tris;
	int[] _colliderTris;
	MaterialPropertyBlock _mpb;
	GoldPileHeightfield _heightfield;
	bool _built;
	bool _colliderDirty;
	bool _colliderDirtyFull;
	int _colliderDirtyMinX;
	int _colliderDirtyMaxX;
	int _colliderDirtyMinZ;
	int _colliderDirtyMaxZ;
	int _colliderResolution;
	int _visualResolution = 64;
	bool _uploadPending;
	bool _mpbPending;
	bool _colliderCookPending;
	float _lastColliderCookTime = -1000f;

	public MeshRenderer PileRenderer => _renderer;
	public MeshCollider PileCollider => _collider;
	public Transform MeshTransform => _filter != null ? _filter.transform : transform;
	public int VisualResolution => _visualResolution;

	/// <summary>
	/// Configures material and optional visual mesh resolution.
	/// meshRes 0 or negative matches heightRes; collider stays derived from heightfield res on Bind.
	/// </summary>
	public void Configure( Material material, int heightRes, int meshRes = 0 )
	{
		if ( material != null )
			pileMaterial = material;

		int height = Mathf.Max( 8, heightRes );
		_visualResolution = meshRes > 0 ? Mathf.Max( 8, meshRes ) : height;
		resolution = _visualResolution;
	}

	public void Bind( GoldPileHeightfield heightfield )
	{
		Bind( heightfield, syncCollider: true );
	}

	public void Bind( GoldPileHeightfield heightfield, bool syncCollider )
	{
		_heightfield = heightfield;
		EnsureComponents();
		BuildTopology();
		_uploadPending = false;
		_mpbPending = false;
		if ( _heightfield != null && _heightfield.IsDirty )
			_heightfield.UploadIfDirty();
		ApplyMaterialAndDeform();
		if ( syncCollider )
		{
			if ( _collider != null )
				_collider.enabled = true;
			SyncColliderImmediate();
		}
		else if ( _collider != null )
			_collider.enabled = false;
	}

	/// <summary>
	/// Queues deform upload + deferred low-res collider sync (coalesced in LateUpdate).
	/// In edit mode, flushes immediately so sculpt preview stays responsive.
	/// </summary>
	public void RefreshFromHeightfield()
	{
		if ( _heightfield == null || !_built )
			return;

		if ( _heightfield.TryGetDirtyRect(
			out int minX, out int maxX, out int minZ, out int maxZ, out bool full ) )
		{
			QueueColliderDirty( minX, maxX, minZ, maxZ, full );
		}

		// Coalesce multiple carves in one frame into a single Apply + MPB write.
		_uploadPending = true;
		_mpbPending = true;

		if ( !Application.isPlaying )
		{
			FlushPendingUpload();
			if ( _colliderDirty )
			{
				SyncCollider( forceCook: true );
				_colliderDirty = false;
				_colliderDirtyFull = false;
				_colliderCookPending = false;
			}
		}
	}

	/// <summary>Forces collider sync this frame (bind / init).</summary>
	public void SyncColliderImmediate()
	{
		FlushPendingUpload();
		_colliderDirtyFull = true;
		_colliderDirty = true;
		_colliderCookPending = true;
		SyncCollider( forceCook: true );
		_colliderDirty = false;
		_colliderDirtyFull = false;
		_colliderCookPending = false;
		_lastColliderCookTime = Time.unscaledTime;
	}

	public void SetVisible( bool visible )
	{
		if ( _renderer != null )
			_renderer.enabled = visible;
		if ( _collider != null )
			_collider.enabled = visible;
	}

	void LateUpdate()
	{
		FlushPendingUpload();

		if ( !_colliderDirty && !_colliderCookPending )
			return;

		System.Diagnostics.Stopwatch sw = null;
		if ( GoldPileEditTiming.Enabled )
			sw = System.Diagnostics.Stopwatch.StartNew();

		bool forceCook = _colliderCookPending
			&& ( Time.unscaledTime - _lastColliderCookTime ) >= ColliderCookMinInterval;

		if ( _colliderDirty )
		{
			// New height edits: refresh verts. Defer PhysX cook unless interval allows.
			bool cookNow = forceCook
				|| ( Time.unscaledTime - _lastColliderCookTime ) >= ColliderCookMinInterval;
			SyncCollider( cookNow );
			_colliderDirty = false;
			_colliderDirtyFull = false;
			if ( cookNow )
			{
				_colliderCookPending = false;
				_lastColliderCookTime = Time.unscaledTime;
			}
			else
				_colliderCookPending = true;
		}
		else if ( forceCook )
		{
			// Verts already current; only reassign sharedMesh for PhysX.
			CookColliderMesh( force: false );
			_colliderCookPending = false;
			_lastColliderCookTime = Time.unscaledTime;
		}

		if ( sw != null )
		{
			sw.Stop();
			GoldPileEditTiming.LogIfEnabled(
				$"[GoldPileEdit] collider sync={sw.Elapsed.TotalMilliseconds:F2}ms cookPending={_colliderCookPending} res={_colliderResolution}",
				this );
		}
	}

	void FlushPendingUpload()
	{
		if ( !_uploadPending && !_mpbPending )
			return;

		GoldPileEditTiming.Begin( "GoldPile.FlushDeform" );
		if ( _uploadPending && _heightfield != null )
			_heightfield.UploadIfDirty();
		_uploadPending = false;

		if ( _mpbPending )
			ApplyMaterialAndDeform();
		_mpbPending = false;
		GoldPileEditTiming.End();
	}

	void OnDestroy()
	{
		if ( _visualMesh != null )
		{
#if UNITY_EDITOR
			if ( !Application.isPlaying )
				DestroyImmediate( _visualMesh );
			else
#endif
				Destroy( _visualMesh );
			_visualMesh = null;
		}

		if ( _colliderMesh != null )
		{
#if UNITY_EDITOR
			if ( !Application.isPlaying )
				DestroyImmediate( _colliderMesh );
			else
#endif
				Destroy( _colliderMesh );
			_colliderMesh = null;
		}
	}

	void EnsureComponents()
	{
		Transform root = transform;
		if ( createChild )
		{
			Transform child = transform.Find( "GoldPileTerrain" );
			GameObject go;
			if ( child == null )
			{
				go = new GameObject( "GoldPileTerrain" );
				go.transform.SetParent( transform, false );
				go.layer = gameObject.layer;
			}
			else
				go = child.gameObject;

			root = go.transform;
		}

		_filter = root.GetComponent<MeshFilter>();
		if ( _filter == null )
			_filter = root.gameObject.AddComponent<MeshFilter>();

		_renderer = root.GetComponent<MeshRenderer>();
		if ( _renderer == null )
			_renderer = root.gameObject.AddComponent<MeshRenderer>();

		_collider = root.GetComponent<MeshCollider>();
		if ( _collider == null )
			_collider = root.gameObject.AddComponent<MeshCollider>();

		// Avoid Weld/Clean on dynamic cooks — they dominate SyncCollider cost.
		_collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation;

		if ( root.GetComponent<GoldPileQualityBinder>() == null )
			root.gameObject.AddComponent<GoldPileQualityBinder>();
	}

	void BuildTopology()
	{
		int heightRes = _heightfield != null ? _heightfield.Resolution : Mathf.Max( 8, resolution );
		int res = _visualResolution > 0 ? _visualResolution : heightRes;
		res = Mathf.Max( 8, res );
		_visualResolution = res;
		resolution = res;

		float size = _heightfield != null ? _heightfield.WorldSize : 4f;
		_colliderResolution = Mathf.Max( MinColliderResolution, heightRes / 4 );

		int vertCount = res * res;
		_baseVerts = new Vector3[ vertCount ];
		_uvs = new Vector2[ vertCount ];
		float half = size * 0.5f;
		float step = size / Mathf.Max( 1, res - 1 );

		for ( int z = 0; z < res; z++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				int i = z * res + x;
				float lx = -half + x * step;
				float lz = -half + z * step;
				_baseVerts[ i ] = new Vector3( lx, 0f, lz );
				_uvs[ i ] = new Vector2(
					res <= 1 ? 0.5f : ( float )x / ( res - 1 ),
					res <= 1 ? 0.5f : ( float )z / ( res - 1 ) );
			}
		}

		int quadCount = ( res - 1 ) * ( res - 1 );
		_tris = new int[ quadCount * 6 ];
		int t = 0;
		for ( int z = 0; z < res - 1; z++ )
		{
			for ( int x = 0; x < res - 1; x++ )
			{
				int i = z * res + x;
				_tris[ t++ ] = i;
				_tris[ t++ ] = i + res;
				_tris[ t++ ] = i + 1;
				_tris[ t++ ] = i + 1;
				_tris[ t++ ] = i + res;
				_tris[ t++ ] = i + res + 1;
			}
		}

		if ( _visualMesh == null )
			_visualMesh = new Mesh { name = "GoldPileVisual" };
		else
			_visualMesh.Clear();

		_visualMesh.indexFormat = vertCount > 65000
			? UnityEngine.Rendering.IndexFormat.UInt32
			: UnityEngine.Rendering.IndexFormat.UInt16;
		_visualMesh.vertices = _baseVerts;
		_visualMesh.uv = _uvs;
		_visualMesh.triangles = _tris;
		_visualMesh.RecalculateNormals();
		_visualMesh.RecalculateBounds();
		ExpandVisualBounds( size, _heightfield != null ? _heightfield.MaxHeight : 2f );

		_filter.sharedMesh = _visualMesh;

		BuildColliderTopology( size );
		_built = true;
	}

	void BuildColliderTopology( float size )
	{
		int res = _colliderResolution;
		int vertCount = res * res;
		_colliderBaseVerts = new Vector3[ vertCount ];
		_displacedVerts = new Vector3[ vertCount ];
		float half = size * 0.5f;
		float step = size / Mathf.Max( 1, res - 1 );

		for ( int z = 0; z < res; z++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				int i = z * res + x;
				float lx = -half + x * step;
				float lz = -half + z * step;
				_colliderBaseVerts[ i ] = new Vector3( lx, 0f, lz );
				_displacedVerts[ i ] = _colliderBaseVerts[ i ];
			}
		}

		int quadCount = ( res - 1 ) * ( res - 1 );
		_colliderTris = new int[ quadCount * 6 ];
		int t = 0;
		for ( int z = 0; z < res - 1; z++ )
		{
			for ( int x = 0; x < res - 1; x++ )
			{
				int i = z * res + x;
				_colliderTris[ t++ ] = i;
				_colliderTris[ t++ ] = i + res;
				_colliderTris[ t++ ] = i + 1;
				_colliderTris[ t++ ] = i + 1;
				_colliderTris[ t++ ] = i + res;
				_colliderTris[ t++ ] = i + res + 1;
			}
		}

		if ( _colliderMesh == null )
			_colliderMesh = new Mesh { name = "GoldPileCollider" };
		else
			_colliderMesh.Clear();

		_colliderMesh.indexFormat = vertCount > 65000
			? UnityEngine.Rendering.IndexFormat.UInt32
			: UnityEngine.Rendering.IndexFormat.UInt16;
		_colliderMesh.MarkDynamic();
		_colliderMesh.vertices = _displacedVerts;
		_colliderMesh.triangles = _colliderTris;
		_colliderMesh.RecalculateBounds();

		CookColliderMesh( force: true );
	}

	void ApplyMaterialAndDeform()
	{
		if ( _renderer == null )
			return;

		if ( pileMaterial != null )
			_renderer.sharedMaterial = pileMaterial;

		if ( _heightfield == null || _heightfield.Texture == null )
			return;

		if ( _mpb == null )
			_mpb = new MaterialPropertyBlock();

		_renderer.GetPropertyBlock( _mpb );
		_mpb.SetFloat( DeformEnabledProp, 1f );
		_mpb.SetTexture( DeformMapProp, _heightfield.Texture );
		_mpb.SetFloat( DeformScaleProp, _heightfield.MaxHeight );
		_mpb.SetFloat( DeformWorldSizeProp, _heightfield.WorldSize );
		_mpb.SetFloat( DeformResolutionProp, _heightfield.Resolution );
		_mpb.SetFloat( GroundLevelProp, _heightfield.GroundLevel );
		_renderer.SetPropertyBlock( _mpb );

		// Shader displacement is not reflected in vertex buffers; keep culling bounds tall enough.
		ExpandVisualBounds( _heightfield.WorldSize, _heightfield.MaxHeight );
	}

	/// <summary>
	/// Flat topology + GPU Y displace: Unity culls against undisplaced bounds, so expand them.
	/// </summary>
	void ExpandVisualBounds( float worldSize, float maxHeight )
	{
		if ( _visualMesh == null )
			return;

		float size = Mathf.Max( 0.1f, worldSize );
		float height = Mathf.Max( 0.01f, maxHeight );
		// Small pad so silhouette / normal-extrusion edge cases stay inside the AABB.
		const float Pad = 1.05f;
		_visualMesh.bounds = new Bounds(
			new Vector3( 0f, height * 0.5f, 0f ),
			new Vector3( size * Pad, height * Pad, size * Pad ) );
	}

	void QueueColliderDirty( int minX, int maxX, int minZ, int maxZ, bool full )
	{
		if ( !_colliderDirty )
		{
			_colliderDirty = true;
			_colliderDirtyFull = full;
			_colliderDirtyMinX = minX;
			_colliderDirtyMaxX = maxX;
			_colliderDirtyMinZ = minZ;
			_colliderDirtyMaxZ = maxZ;
			return;
		}

		if ( full || _colliderDirtyFull )
		{
			_colliderDirtyFull = true;
			return;
		}

		if ( minX < _colliderDirtyMinX )
			_colliderDirtyMinX = minX;
		if ( maxX > _colliderDirtyMaxX )
			_colliderDirtyMaxX = maxX;
		if ( minZ < _colliderDirtyMinZ )
			_colliderDirtyMinZ = minZ;
		if ( maxZ > _colliderDirtyMaxZ )
			_colliderDirtyMaxZ = maxZ;
	}

	void SyncCollider( bool forceCook )
	{
		GoldPileEditTiming.Begin( "GoldPile.SyncCollider" );
		if ( !_built || _heightfield == null || _displacedVerts == null || _colliderMesh == null )
		{
			GoldPileEditTiming.End();
			return;
		}

		int res = _colliderResolution;
		float maxH = _heightfield.MaxHeight;
		int minX = 0;
		int maxX = res - 1;
		int minZ = 0;
		int maxZ = res - 1;

		if ( !_colliderDirtyFull && _heightfield.Resolution > 1 )
		{
			float inv = 1f / ( _heightfield.Resolution - 1 );
			float u0 = _colliderDirtyMinX * inv;
			float u1 = _colliderDirtyMaxX * inv;
			float v0 = _colliderDirtyMinZ * inv;
			float v1 = _colliderDirtyMaxZ * inv;
			minX = Mathf.Clamp( Mathf.FloorToInt( u0 * ( res - 1 ) ) - 1, 0, res - 1 );
			maxX = Mathf.Clamp( Mathf.CeilToInt( u1 * ( res - 1 ) ) + 1, 0, res - 1 );
			minZ = Mathf.Clamp( Mathf.FloorToInt( v0 * ( res - 1 ) ) - 1, 0, res - 1 );
			maxZ = Mathf.Clamp( Mathf.CeilToInt( v1 * ( res - 1 ) ) + 1, 0, res - 1 );
		}

		for ( int z = minZ; z <= maxZ; z++ )
		{
			float v = res <= 1 ? 0.5f : ( float )z / ( res - 1 );
			for ( int x = minX; x <= maxX; x++ )
			{
				int i = z * res + x;
				float u = res <= 1 ? 0.5f : ( float )x / ( res - 1 );
				float h = _heightfield.SampleNormalizedUV( u, v ) * maxH;
				// Sink below-ground cells so room floor / other colliders win raycasts.
				if ( h < _heightfield.GroundLevel )
					h = -1f;
				Vector3 b = _colliderBaseVerts[ i ];
				_displacedVerts[ i ] = new Vector3( b.x, h, b.z );
			}
		}

		_colliderMesh.vertices = _displacedVerts;
		_colliderMesh.RecalculateBounds();

		if ( forceCook )
			CookColliderMesh( force: false );

		GoldPileEditTiming.End();
	}

	/// <summary>
	/// Reassigns the MeshCollider without the null-swap cook. Unity 6 updates from MarkDynamic meshes
	/// when sharedMesh is set to the same instance again; null/reassign was forcing a full recook.
	/// </summary>
	void CookColliderMesh( bool force )
	{
		if ( _collider == null || _colliderMesh == null )
			return;

		GoldPileEditTiming.Begin( "GoldPile.CookCollider" );
		if ( _collider.sharedMesh != _colliderMesh || force )
			_collider.sharedMesh = _colliderMesh;
		else
		{
			// Same mesh instance: toggle enables PhysX to pick up vertex changes without nulling.
			bool wasEnabled = _collider.enabled;
			_collider.enabled = false;
			_collider.sharedMesh = _colliderMesh;
			_collider.enabled = wasEnabled;
		}

		GoldPileEditTiming.End();
	}
}
