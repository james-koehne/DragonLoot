using UnityEngine;

/// <summary>
/// Fixed-topology pile grid mesh. Visual displace via deform texture;
/// collider uses chunked MeshColliders cooked only for dirty tiles.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileTerrainMesh : MonoBehaviour, TreasureSparkleMaskRegistrar.IInstanceMaskSource
{
	const string DeformEnabledProp = "_DeformEnabled";
	const string DeformMapProp = "_DeformMap";
	const string DeformScaleProp = "_DeformScale";
	const string DeformWorldSizeProp = "_DeformWorldSize";
	const string DeformResolutionProp = "_DeformResolution";
	const string DeformNormalSoftenProp = "_DeformNormalSoften";
	const string DeformSampleBlurProp = "_DeformSampleBlur";
	const string GroundLevelProp = "_GroundLevelHeight";
	const int MinColliderResolution = 16;
	/// <summary>
	/// Collider grid matches heightfield resolution (divisor 1) so digs track the mound closely.
	/// Chunked dirty-tile cooks keep this affordable on large piles.
	/// </summary>
	const int ColliderResolutionDivisor = 1;
	/// <summary>World-space size of each collider tile (matches loot chunk default).</summary>
	const float ColliderChunkSize = GoldPileChunkGrid.DefaultChunkSize;
	/// <summary>Minimum seconds between MeshCollider cook batches while edits are pending.</summary>
	const float ColliderCookMinInterval = 0.2f;
	/// <summary>Max tiles to schedule async PhysX bakes per LateUpdate.</summary>
	const int MaxAsyncCooksPerFrame = 8;
	const HideFlags RuntimeMeshHideFlags = HideFlags.HideAndDontSave;

	[SerializeField]
	[Min( 8 )]
	int resolution = 64;

	[SerializeField]
	Material pileMaterial;

	[SerializeField]
	bool createChild = true;

	MeshFilter _filter;
	MeshRenderer _renderer;
	Mesh _visualMesh;
	Vector3[] _baseVerts;
	Vector2[] _uvs;
	int[] _tris;
	MaterialPropertyBlock _mpb;
	GoldPileHeightfield _heightfield;
	readonly GoldPileColliderTiles _colliderTiles = new GoldPileColliderTiles();
	bool _built;
	bool _colliderDirty;
	bool _colliderDirtyFull;
	int _colliderDirtyMinX;
	int _colliderDirtyMaxX;
	int _colliderDirtyMinZ;
	int _colliderDirtyMaxZ;
	int _colliderResolution;
	int _visualResolution = 64;
	float _deformNormalSoften;
	float _deformSampleBlur = 4f;
	bool _uploadPending;
	bool _mpbPending;
	bool _colliderCookPending;
	float _lastColliderCookTime = -1000f;
	Material _sparkleMaskMaterial;
	MaterialPropertyBlock _sparkleMaskMpb;
	static TreasureSparkleDefinition s_SparkleDefinition;

	public MeshRenderer PileRenderer => _renderer;
	/// <summary>First collider tile (compat). Prefer resolving hits via parent GoldPileTerrainMesh.</summary>
	public MeshCollider PileCollider => _colliderTiles.FirstCollider;
	public int ColliderTileCount => _colliderTiles.TileCount;
	public int ColliderResolution => _colliderResolution;
	public Transform MeshTransform => _filter != null ? _filter.transform : transform;
	public int VisualResolution => _visualResolution;

	/// <summary>
	/// Clears MeshFilter/collider references and destroys procedural meshes so they are not
	/// written into scene YAML. Safe to call before save; rebuild via Bind / editor preview.
	/// </summary>
	public void StripPersistedRuntimeGeometry()
	{
		_colliderTiles.Release();
		_colliderDirty = false;
		_colliderDirtyFull = false;
		_colliderCookPending = false;
		_built = false;

		if ( _filter != null )
		{
			Mesh assigned = _filter.sharedMesh;
			_filter.sharedMesh = null;
			if ( assigned != null && assigned != _visualMesh )
				DestroyProceduralMesh( assigned );
		}

		if ( _visualMesh != null )
		{
			DestroyProceduralMesh( _visualMesh );
			_visualMesh = null;
		}
	}

	static void DestroyProceduralMesh( Mesh mesh )
	{
		if ( mesh == null )
			return;

#if UNITY_EDITOR
		if ( !Application.isPlaying )
			DestroyImmediate( mesh );
		else
#endif
			Destroy( mesh );
	}

	/// <summary>
	/// Configures material, optional visual mesh resolution, and deform visual overrides.
	/// meshRes 0 or negative matches heightRes; collider matches heightfield res on Bind.
	/// Soften/blur override shared material defaults via MPB (procedural gold-pile shader).
	/// </summary>
	public void Configure(
		Material material,
		int heightRes,
		int meshRes = 0,
		float deformNormalSoften = 0f,
		float deformSampleBlur = 4f )
	{
		if ( material != null )
			pileMaterial = material;

		int height = Mathf.Max( 8, heightRes );
		_visualResolution = meshRes > 0 ? Mathf.Max( 8, meshRes ) : height;
		resolution = _visualResolution;
		_deformNormalSoften = Mathf.Clamp01( deformNormalSoften );
		_deformSampleBlur = Mathf.Clamp( deformSampleBlur, 0f, 4f );
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
			_colliderTiles.SetEnabled( true );
			SyncColliderImmediate();
		}
		else
			_colliderTiles.SetEnabled( false );
	}

	/// <summary>
	/// Enables colliders and queues displace + async PhysX cooks (no sync BakeMesh).
	/// Use after Bind(..., syncCollider: false) at runtime so scene Integrate stays cheap.
	/// </summary>
	public void BeginDeferredColliderCook()
	{
		if ( !_built || _heightfield == null )
			return;

		_colliderTiles.SetEnabled( true );
		_colliderDirtyFull = true;
		_colliderDirty = true;
		_colliderCookPending = true;
		_lastColliderCookTime = -1000f;
	}

	/// <summary>
	/// Queues deform upload + deferred collider tile sync (coalesced in LateUpdate).
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
		_colliderTiles.CancelAllBakes();
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
		_colliderTiles.SetEnabled( visible );
	}

	void LateUpdate()
	{
		GoldPileEditTiming.TryFlushDeferredFromPriorFrame();
		FlushPendingUpload();
		_colliderTiles.ApplyCompletedBakes();

		if ( _colliderDirty )
		{
			// Vert displace every dirty frame; PhysX cook is rate-limited below.
			SyncCollider( forceCook: false );
			_colliderDirty = false;
			_colliderDirtyFull = false;
			_colliderCookPending = true;
		}

		if ( !_colliderCookPending && !_colliderTiles.HasDirtyOrPending() )
		{
			return;
		}

		if ( !ShouldCookColliderNow() )
		{
			return;
		}

		int scheduled = _colliderTiles.RequestAsyncBakes( MaxAsyncCooksPerFrame );
		_colliderCookPending = _colliderTiles.HasDirtyOrPending();
		if ( scheduled > 0 )
			_lastColliderCookTime = Time.unscaledTime;
	}


	bool ShouldCookColliderNow()
	{
		if ( !Application.isPlaying )
			return true;

		float sinceCook = Time.unscaledTime - _lastColliderCookTime;
		return sinceCook >= ColliderCookMinInterval;
	}

	void FlushPendingUpload()
	{
		if ( !_uploadPending && !_mpbPending )
			return;

		GoldPileEditTiming.Begin( "GoldPile.FlushDeform" );
		System.Diagnostics.Stopwatch phaseSw = GoldPileEditTiming.StartWatchIfEnabled();

		if ( _uploadPending && _heightfield != null )
			_heightfield.UploadIfDirty();
		_uploadPending = false;

		bool applyMpb = _mpbPending;
		if ( applyMpb )
		{
			if ( phaseSw != null )
				phaseSw.Restart();
			ApplyMaterialAndDeform();
			_mpbPending = false;
			if ( phaseSw != null )
			{
				phaseSw.Stop();
				GoldPileEditTiming.Record( "flush.mpb", phaseSw.Elapsed.TotalMilliseconds );
			}
		}
		else
			_mpbPending = false;

		GoldPileEditTiming.End();
	}

	void OnDestroy()
	{
		_colliderTiles.Release();
		UnregisterSparkleMask();

		if ( _sparkleMaskMaterial != null )
		{
			if ( Application.isPlaying )
				Destroy( _sparkleMaskMaterial );
			else
				DestroyImmediate( _sparkleMaskMaterial );
			_sparkleMaskMaterial = null;
		}

		if ( _visualMesh != null )
		{
			DestroyProceduralMesh( _visualMesh );
			_visualMesh = null;
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

		// Colliders live on chunked tiles — remove legacy MeshColliders on terrain child and pile root.
		StripLegacyMeshCollider( root.gameObject );
		if ( root != transform )
			StripLegacyMeshCollider( gameObject );

		if ( root.GetComponent<GoldPileQualityBinder>() == null )
			root.gameObject.AddComponent<GoldPileQualityBinder>();

		RegisterSparkleMask();
	}

	static void StripLegacyMeshCollider( GameObject go )
	{
		if ( go == null )
			return;

		MeshCollider legacy = go.GetComponent<MeshCollider>();
		if ( legacy == null )
			return;

		if ( Application.isPlaying )
			Destroy( legacy );
		else
			DestroyImmediate( legacy );
	}

	void OnEnable()
	{
		RegisterSparkleMask();
	}

	void OnDisable()
	{
		UnregisterSparkleMask();
	}

	public TreasureSparkleDefinition.SparkleSourceKind MaskKind => TreasureSparkleDefinition.SparkleSourceKind.Pile;

	public bool TryGetSparkleWorldBounds( out Bounds bounds )
	{
		bounds = default;
		if ( _renderer == null || !_renderer.enabled )
			return false;

		bounds = _renderer.bounds;
		return true;
	}

	public void DrawSparkleMask( UnityEngine.Rendering.RasterCommandBuffer cmd, Material fallbackMaskMaterial )
	{
		if ( cmd == null || _filter == null || _filter.sharedMesh == null || _renderer == null )
			return;

		Material maskMaterial = _sparkleMaskMaterial != null ? _sparkleMaskMaterial : fallbackMaskMaterial;
		if ( maskMaterial == null || _sparkleMaskMpb == null )
			return;

		Mesh mesh = _filter.sharedMesh;
		Matrix4x4 matrix = _renderer.localToWorldMatrix;
		int submeshCount = Mathf.Max( 1, mesh.subMeshCount );
		for ( int submesh = 0; submesh < submeshCount; submesh++ )
			cmd.DrawMesh( mesh, matrix, maskMaterial, submesh, 0, _sparkleMaskMpb );
	}

	void EnsureSparkleMaskMaterial()
	{
		if ( _sparkleMaskMaterial != null )
			return;

		Shader shader = Shader.Find( "DragonLoot/Treasure Sparkle Pile Mask" );
		if ( shader == null )
			return;

		_sparkleMaskMaterial = new Material( shader );
		_sparkleMaskMaterial.hideFlags = HideFlags.HideAndDontSave;
		SyncSparkleMaskDeform();
	}

	void RegisterSparkleMask()
	{
		TreasureSparkleDefinition sparkleDefinition = TreasureSparkleRendererFeature.ActiveDefinition;
		if ( sparkleDefinition == null )
			sparkleDefinition = RuntimeDefinition.Resolve( ref s_SparkleDefinition );

		if ( sparkleDefinition != null && !sparkleDefinition.Allows( TreasureSparkleDefinition.SparkleSourceKind.Pile ) )
		{
			UnregisterSparkleMask();
			return;
		}

		EnsureSparkleMaskMaterial();
		SyncSparkleMaskDeform();

		// Piles use deform-aware instance mask draws — do not register the flat MeshRenderer.
		TreasureSparkleMaskRegistrar.Unregister( _renderer );
		TreasureSparkleMaskRegistrar.RegisterInstanceSource( this );
	}

	void UnregisterSparkleMask()
	{
		TreasureSparkleMaskRegistrar.UnregisterInstanceSource( this );
		if ( _renderer != null )
			TreasureSparkleMaskRegistrar.Unregister( _renderer );
	}

	void BuildTopology()
	{
		int heightRes = _heightfield != null ? _heightfield.Resolution : Mathf.Max( 8, resolution );
		int res = _visualResolution > 0 ? _visualResolution : heightRes;
		res = Mathf.Max( 8, res );
		_visualResolution = res;
		resolution = res;

		float size = _heightfield != null ? _heightfield.WorldSize : 4f;
		_colliderResolution = Mathf.Max( MinColliderResolution, heightRes / ColliderResolutionDivisor );

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
				// Texel centers: matches Point-filtered deform map and CPU grid heights / collider verts.
				_uvs[ i ] = new Vector2(
					( x + 0.5f ) / res,
					( z + 0.5f ) / res );
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

		_visualMesh.hideFlags = RuntimeMeshHideFlags;
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

		BuildColliderTiles( size );
		_built = true;
	}

	void BuildColliderTiles( float size )
	{
		_colliderTiles.Build(
			transform,
			_colliderResolution,
			size,
			ColliderChunkSize,
			gameObject.layer );
		_colliderTiles.MarkAllDirty();
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
		_mpb.SetFloat( DeformNormalSoftenProp, _deformNormalSoften );
		_mpb.SetFloat( DeformSampleBlurProp, _deformSampleBlur );
		_mpb.SetFloat( GroundLevelProp, _heightfield.GroundLevel );
		_renderer.SetPropertyBlock( _mpb );
		SyncSparkleMaskDeform();

		// Shader displacement is not reflected in vertex buffers; keep culling bounds tall enough.
		ExpandVisualBounds( _heightfield.WorldSize, _heightfield.MaxHeight );
	}

	void SyncSparkleMaskDeform()
	{
		if ( _heightfield == null || _heightfield.Texture == null )
			return;

		EnsureSparkleMaskMaterial();
		if ( _sparkleMaskMaterial == null )
			return;

		_sparkleMaskMaterial.SetFloat( DeformEnabledProp, 1f );
		_sparkleMaskMaterial.SetTexture( DeformMapProp, _heightfield.Texture );
		_sparkleMaskMaterial.SetFloat( DeformScaleProp, _heightfield.MaxHeight );
		_sparkleMaskMaterial.SetFloat( DeformWorldSizeProp, _heightfield.WorldSize );
		_sparkleMaskMaterial.SetFloat( DeformResolutionProp, _heightfield.Resolution );
		_sparkleMaskMaterial.SetFloat( GroundLevelProp, _heightfield.GroundLevel );
		_sparkleMaskMaterial.SetFloat( TreasureSparkleMaskPass.MaskWriteValueId, TreasureSparkleDefinition.MaskPile );

		if ( _sparkleMaskMpb == null )
			_sparkleMaskMpb = new MaterialPropertyBlock();

		_sparkleMaskMpb.Clear();
		_sparkleMaskMpb.SetFloat( DeformEnabledProp, 1f );
		_sparkleMaskMpb.SetTexture( DeformMapProp, _heightfield.Texture );
		_sparkleMaskMpb.SetFloat( DeformScaleProp, _heightfield.MaxHeight );
		_sparkleMaskMpb.SetFloat( DeformWorldSizeProp, _heightfield.WorldSize );
		_sparkleMaskMpb.SetFloat( DeformResolutionProp, _heightfield.Resolution );
		_sparkleMaskMpb.SetFloat( GroundLevelProp, _heightfield.GroundLevel );
		_sparkleMaskMpb.SetFloat( TreasureSparkleMaskPass.MaskWriteValueId, TreasureSparkleDefinition.MaskPile );
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
		if ( !_built || _heightfield == null || _colliderTiles.TileCount == 0 )
		{
			GoldPileEditTiming.End();
			return;
		}

		if ( _colliderDirtyFull )
			_colliderTiles.MarkAllDirty();
		else
		{
			_colliderTiles.MarkDirtyFromHeightfieldRect(
				_heightfield.Resolution,
				_colliderDirtyMinX,
				_colliderDirtyMaxX,
				_colliderDirtyMinZ,
				_colliderDirtyMaxZ,
				full: false );
		}

		_colliderTiles.SyncDirty( _heightfield, forceCook );
		GoldPileEditTiming.End();
	}
}
