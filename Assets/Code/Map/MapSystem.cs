using System.Collections.Generic;
using System.Diagnostics;

using UnityEngine;

/// <summary>
/// Bakes a top-down map from treasure-surface walkable paint, gold piles, and region volumes.
/// Discovery fog-of-war is stamped around the player each frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSystem : MonoBehaviour
{
	enum BakePhase
	{
		Idle,
		ScanBounds,
		FillBase,
		StampGold,
		StampRegions,
		UploadBase
	}

	static MapSystem _instance;
	static readonly List<TreasurePileVisual> RegisteredPiles = new List<TreasurePileVisual>( 16 );
	static int _pileRevision;

	[SerializeField]
	MapDefinition _definition;

	readonly List<MapRegionVolume> _volumes = new List<MapRegionVolume>( 32 );
	readonly List<TreasurePileVisual> _piles = new List<TreasurePileVisual>( 16 );

	MapDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	Color32[] _basePixels;
	byte[] _discovery;
	Color32[] _displayPixels;
	Color32[] _uploadScratch;
	Texture2D _displayTexture;

	int _resolution;
	Bounds _mapBounds;
	bool _hasBounds;
	bool _baseReady;
	bool _displayDirty;
	bool _gpuUploadPending;

	BakePhase _phase = BakePhase.Idle;
	int _bakeRow;
	int _bakePileIndex;
	int _bakeVolumeIndex;
	int _lastPaintRevision = -1;
	int _lastVolumeRevision = -1;
	int _lastPileRevision = -1;
	bool _forceImmediateBake;
	bool _debugIgnoreDiscovery;
	float _lastBakeElapsedMs;
	float _bakeAccumulatedMs;
	string _bakePhaseName = "Idle";

	int _dirtyMinX;
	int _dirtyMaxX;
	int _dirtyMinY;
	int _dirtyMaxY;
	bool _hasDirtyRect;

	int _lastDiscoveryCx = int.MinValue;
	int _lastDiscoveryCy = int.MinValue;
	float _lastDiscoveryRadius = -1f;
	float _lastDiscoverySoftness = -1f;

	bool _coverageDirty = true;
	float _cachedDiscoveryCoverage;

	byte[] _fillTrav;
	int _fillCellsX;
	int _fillCellsZ;
	float _fillOriginX;
	float _fillOriginZ;
	float _fillHalfX;
	float _fillHalfZ;
	float _fillWorldSizeX;
	float _fillWorldSizeZ;
	float _fillInvCell;
	float _fillMapMinX;
	float _fillMapMinZ;
	float _fillMppX;
	float _fillMppZ;
	bool _fillHasPaint;

	public static MapSystem Instance => _instance;
	public Texture2D DisplayTexture => _displayTexture;
	public bool HasBounds => _hasBounds;
	public Bounds MapBounds => _mapBounds;
	public int Resolution => _resolution;
	public bool IsBaseReady => _baseReady;
	public bool DebugIgnoreDiscovery => _debugIgnoreDiscovery;
	public float LastBakeElapsedMs => _lastBakeElapsedMs;
	public string BakePhaseName => _bakePhaseName;
	public int CachedPileCount => _piles.Count;

	public static MapSystem EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		GameObject go = new GameObject( "MapSystem" );
		_instance = go.AddComponent<MapSystem>();
		return _instance;
	}

	public static void RegisterPile( TreasurePileVisual pile )
	{
		if ( pile == null )
			return;

		for ( int i = 0; i < RegisteredPiles.Count; i++ )
		{
			if ( RegisteredPiles[ i ] == pile )
				return;
		}

		RegisteredPiles.Add( pile );
		_pileRevision++;
	}

	public static void UnregisterPile( TreasurePileVisual pile )
	{
		if ( pile == null )
			return;

		if ( RegisteredPiles.Remove( pile ) )
			_pileRevision++;
	}

	void Awake()
	{
		if ( _instance != null && _instance != this )
		{
			Destroy( gameObject );
			return;
		}

		_instance = this;
	}

	void OnDestroy()
	{
		ReleaseTextures();
		if ( _instance == this )
			_instance = null;
	}

	void Update()
	{
		MapDefinition def = Definition;
		if ( def == null )
			return;

		EnsureBuffers( def.resolution );

		TickDiscovery( def );
		TickBake( def );
		FlushDisplayIfNeeded( def, forceUpload: false );
	}

	public void RequestImmediateBake()
	{
		_forceImmediateBake = true;
		if ( _phase == BakePhase.Idle )
			BeginBake( force: true );

		MapDefinition def = Definition;
		if ( def == null )
			return;

		def.EnsureDefaults();
		EnsureBuffers( def.resolution );

		Stopwatch sw = Stopwatch.StartNew();
		float budgetMs = 250f;
		while ( _phase != BakePhase.Idle && sw.Elapsed.TotalMilliseconds < budgetMs )
			TickBake( def );

		if ( _gpuUploadPending )
		{
			RebuildFullDisplay( def );
			_gpuUploadPending = false;
		}

		FlushDisplayIfNeeded( def, forceUpload: true );
	}

	public bool TryWorldToUv( Vector3 worldPos, out Vector2 uv )
	{
		uv = default;
		if ( !_hasBounds )
			return false;

		float sizeX = _mapBounds.size.x;
		float sizeZ = _mapBounds.size.z;
		if ( sizeX < 1e-4f || sizeZ < 1e-4f )
			return false;

		uv.x = ( worldPos.x - _mapBounds.min.x ) / sizeX;
		uv.y = ( worldPos.z - _mapBounds.min.z ) / sizeZ;
		return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
	}

	public bool TryUvToWorld( Vector2 uv, out Vector3 worldPos )
	{
		worldPos = default;
		if ( !_hasBounds )
			return false;

		worldPos = new Vector3(
			Mathf.Lerp( _mapBounds.min.x, _mapBounds.max.x, uv.x ),
			_mapBounds.center.y,
			Mathf.Lerp( _mapBounds.min.z, _mapBounds.max.z, uv.y ) );
		return true;
	}

	public bool IsDiscoveredAtWorld( Vector3 worldPos )
	{
		if ( !_hasBounds || _discovery == null )
			return false;

		if ( !TryWorldToPixel( worldPos, out int px, out int py ) )
			return false;

		return _discovery[ py * _resolution + px ] > 8;
	}

	public void CollectLabels( List<MapLabelMarker> results )
	{
		MapOverlayRegistrar.CollectLabels( results );
	}

	public MapDefinition GetDefinition()
	{
		return Definition;
	}

	public void DebugDiscoverAll()
	{
		if ( _discovery == null )
			return;

		for ( int i = 0; i < _discovery.Length; i++ )
			_discovery[ i ] = 255;

		_coverageDirty = true;
		MapDefinition def = Definition;
		if ( def != null )
			RebuildFullDisplay( def );
		FlushDisplayIfNeeded( def, forceUpload: true );
	}

	public void DebugClearDiscovery()
	{
		if ( _discovery == null )
			return;

		System.Array.Clear( _discovery, 0, _discovery.Length );
		_lastDiscoveryCx = int.MinValue;
		_lastDiscoveryCy = int.MinValue;
		_coverageDirty = true;

		MapDefinition def = Definition;
		if ( def != null )
			RebuildFullDisplay( def );
		FlushDisplayIfNeeded( def, forceUpload: true );
	}

	public void DebugSetIgnoreDiscovery( bool ignore )
	{
		if ( _debugIgnoreDiscovery == ignore )
			return;

		_debugIgnoreDiscovery = ignore;
		MapDefinition def = Definition;
		if ( def != null )
			RebuildFullDisplay( def );
		FlushDisplayIfNeeded( def, forceUpload: true );
	}

	public void DebugForceRebuild()
	{
		_lastPaintRevision = -1;
		_lastVolumeRevision = -1;
		_lastPileRevision = -1;
		RequestImmediateBake();
	}

	public float GetDiscoveryCoverage01()
	{
		if ( _discovery == null || _discovery.Length == 0 )
			return 0f;

		if ( !_coverageDirty )
			return _cachedDiscoveryCoverage;

		int discovered = 0;
		for ( int i = 0; i < _discovery.Length; i++ )
		{
			if ( _discovery[ i ] > 8 )
				discovered++;
		}

		_cachedDiscoveryCoverage = discovered / ( float )_discovery.Length;
		_coverageDirty = false;
		return _cachedDiscoveryCoverage;
	}

	void EnsureBuffers( int resolution )
	{
		resolution = Mathf.Clamp( resolution, 64, 1024 );
		if ( _displayTexture != null && _resolution == resolution && _basePixels != null )
			return;

		ReleaseTextures();
		_resolution = resolution;
		int count = _resolution * _resolution;
		_basePixels = new Color32[ count ];
		_discovery = new byte[ count ];
		_displayPixels = new Color32[ count ];
		_uploadScratch = null;
		_displayTexture = new Texture2D( _resolution, _resolution, TextureFormat.RGBA32, false, false );
		_displayTexture.name = "MapDisplay";
		_displayTexture.filterMode = FilterMode.Bilinear;
		_displayTexture.wrapMode = TextureWrapMode.Clamp;
		_baseReady = false;
		_hasBounds = false;
		_phase = BakePhase.Idle;
		_bakeRow = 0;
		_hasDirtyRect = false;
		_gpuUploadPending = false;
		_coverageDirty = true;
		_lastDiscoveryCx = int.MinValue;
		_lastDiscoveryCy = int.MinValue;
		FillArray( _basePixels, Color32From( Color.black ) );
		System.Array.Clear( _discovery, 0, _discovery.Length );
		FillArray( _displayPixels, Color32From( Color.black ) );
		_displayTexture.SetPixels32( _displayPixels );
		_displayTexture.Apply( false, false );
		_displayDirty = false;
	}

	void SyncPileCache()
	{
		_piles.Clear();
		for ( int i = 0; i < RegisteredPiles.Count; i++ )
		{
			TreasurePileVisual pile = RegisteredPiles[ i ];
			if ( pile != null && pile.isActiveAndEnabled )
				_piles.Add( pile );
		}
	}

	void TickBake( MapDefinition def )
	{
		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		int paintRevision = authoring != null ? authoring.PaintRevision : -1;
		int volumeRevision = MapOverlayRegistrar.VolumeRevision;
		SyncPileCache();

		bool needsRebuild = paintRevision != _lastPaintRevision
			|| volumeRevision != _lastVolumeRevision
			|| _pileRevision != _lastPileRevision
			|| !_baseReady;

		if ( _phase == BakePhase.Idle )
		{
			if ( needsRebuild )
				BeginBake( force: false );
			return;
		}

		bool busy = !_forceImmediateBake && Time.deltaTime > def.busyFrameDeltaSeconds;
		if ( busy )
			return;

		float budgetMs = _forceImmediateBake ? 1000f : def.bakeBudgetMs;
		Stopwatch sw = Stopwatch.StartNew();

		while ( _phase != BakePhase.Idle )
		{
			_bakePhaseName = _phase.ToString();
			switch ( _phase )
			{
				case BakePhase.ScanBounds:
					if ( !TryScanWalkableBounds( authoring, def ) )
					{
						_phase = BakePhase.Idle;
						_bakePhaseName = "Idle";
						_forceImmediateBake = false;
						return;
					}
					_lastPaintRevision = paintRevision;
					_phase = BakePhase.FillBase;
					_bakeRow = 0;
					CacheFillPaint( authoring );
					break;

				case BakePhase.FillBase:
					FillBaseRows( def, sw, budgetMs );
					break;

				case BakePhase.StampGold:
					StampGoldRows( def, sw, budgetMs );
					break;

				case BakePhase.StampRegions:
					StampRegions( def, sw, budgetMs );
					break;

				case BakePhase.UploadBase:
					_lastVolumeRevision = volumeRevision;
					_lastPileRevision = _pileRevision;
					_baseReady = true;
					RebuildFullDisplay( def );
					_phase = BakePhase.Idle;
					_bakePhaseName = "Idle";
					_bakeAccumulatedMs += ( float )sw.Elapsed.TotalMilliseconds;
					_lastBakeElapsedMs = _bakeAccumulatedMs;
					_forceImmediateBake = false;
					return;
			}

			if ( !_forceImmediateBake && sw.Elapsed.TotalMilliseconds >= budgetMs )
			{
				_bakeAccumulatedMs += ( float )sw.Elapsed.TotalMilliseconds;
				return;
			}
		}

		_bakeAccumulatedMs += ( float )sw.Elapsed.TotalMilliseconds;
	}

	void BeginBake( bool force )
	{
		_forceImmediateBake = force || _forceImmediateBake;
		_baseReady = false;
		_phase = BakePhase.ScanBounds;
		_bakePhaseName = "ScanBounds";
		_bakeRow = 0;
		_bakePileIndex = 0;
		_bakeVolumeIndex = 0;
		_bakeAccumulatedMs = 0f;
	}

	bool TryScanWalkableBounds( TreasureSurfaceAuthoring authoring, MapDefinition def )
	{
		_hasBounds = false;
		if ( authoring == null )
			return false;

		if ( !authoring.TryComputeTraversableBoundsXZ( out float minX, out float maxX, out float minZ, out float maxZ ) )
			return false;

		float pad = def.boundsPadding;
		minX -= pad;
		maxX += pad;
		minZ -= pad;
		maxZ += pad;

		float sizeX = Mathf.Max( 1f, maxX - minX );
		float sizeZ = Mathf.Max( 1f, maxZ - minZ );
		Vector3 center = new Vector3( ( minX + maxX ) * 0.5f, authoring.BaseHeight, ( minZ + maxZ ) * 0.5f );
		_mapBounds = new Bounds( center, new Vector3( sizeX, 1f, sizeZ ) );
		_hasBounds = true;
		return true;
	}

	void CacheFillPaint( TreasureSurfaceAuthoring authoring )
	{
		_fillHasPaint = false;
		_fillTrav = null;
		if ( authoring == null )
			return;

		if ( !authoring.TryGetPaintTraversable( out _fillTrav, out _fillCellsX, out _fillCellsZ ) )
			return;

		_fillHasPaint = true;
		_fillOriginX = authoring.WorldOrigin.x;
		_fillOriginZ = authoring.WorldOrigin.z;
		_fillWorldSizeX = authoring.WorldSizeX;
		_fillWorldSizeZ = authoring.WorldSizeZ;
		_fillHalfX = _fillWorldSizeX * 0.5f;
		_fillHalfZ = _fillWorldSizeZ * 0.5f;
		float cell = authoring.CellSize;
		_fillInvCell = 1f / Mathf.Max( 0.01f, cell );
		_fillMapMinX = _mapBounds.min.x;
		_fillMapMinZ = _mapBounds.min.z;
		_fillMppX = _mapBounds.size.x / _resolution;
		_fillMppZ = _mapBounds.size.z / _resolution;
	}

	void FillBaseRows( MapDefinition def, Stopwatch sw, float budgetMs )
	{
		Color32 empty = Color32From( def.emptyColor );
		Color32 walkable = Color32From( def.walkableColor );
		int res = _resolution;
		byte[] trav = _fillTrav;
		int cellsX = _fillCellsX;
		int cellsZ = _fillCellsZ;
		bool hasPaint = _fillHasPaint && trav != null;
		float originX = _fillOriginX;
		float originZ = _fillOriginZ;
		float halfX = _fillHalfX;
		float halfZ = _fillHalfZ;
		float worldSizeX = _fillWorldSizeX;
		float worldSizeZ = _fillWorldSizeZ;
		float invCell = _fillInvCell;
		float mapMinX = _fillMapMinX;
		float mapMinZ = _fillMapMinZ;
		float mppX = _fillMppX;
		float mppZ = _fillMppZ;

		while ( _bakeRow < res )
		{
			int y = _bakeRow;
			int row = y * res;
			float wz = mapMinZ + ( y + 0.5f ) * mppZ;
			float localZ = wz - originZ + halfZ;
			bool rowInZ = hasPaint && localZ >= 0f && localZ < worldSizeZ;
			int cellZ = rowInZ ? ( int )( localZ * invCell ) : -1;
			if ( cellZ < 0 || cellZ >= cellsZ )
				rowInZ = false;

			for ( int x = 0; x < res; x++ )
			{
				bool traversable = false;
				if ( rowInZ )
				{
					float wx = mapMinX + ( x + 0.5f ) * mppX;
					float localX = wx - originX + halfX;
					if ( localX >= 0f && localX < worldSizeX )
					{
						int cellX = ( int )( localX * invCell );
						if ( cellX >= 0 && cellX < cellsX )
							traversable = trav[ cellZ * cellsX + cellX ] != 0;
					}
				}

				_basePixels[ row + x ] = traversable ? walkable : empty;
			}

			_bakeRow++;
			if ( !_forceImmediateBake && sw.Elapsed.TotalMilliseconds >= budgetMs )
				return;
		}

		_bakeRow = 0;
		_bakePileIndex = 0;
		_phase = BakePhase.StampGold;
	}

	void StampGoldRows( MapDefinition def, Stopwatch sw, float budgetMs )
	{
		Color32 gold = Color32From( def.goldColor );
		int res = _resolution;

		while ( _bakePileIndex < _piles.Count )
		{
			TreasurePileVisual pile = _piles[ _bakePileIndex ];
			if ( pile != null )
				StampPileGold( pile, gold, res );

			_bakePileIndex++;
			if ( !_forceImmediateBake && sw.Elapsed.TotalMilliseconds >= budgetMs )
				return;
		}

		_bakeVolumeIndex = 0;
		MapOverlayRegistrar.CollectVolumes( _volumes );
		_phase = BakePhase.StampRegions;
	}

	void StampPileGold( TreasurePileVisual pile, Color32 gold, int res )
	{
		GoldPileHeightfield heightfield = pile.Heightfield;
		if ( heightfield == null || !heightfield.IsInitialized )
		{
			StampPileGoldAuthored( pile, gold, res );
			return;
		}

		float worldSize = heightfield.WorldSize;
		int hfRes = heightfield.Resolution;
		if ( worldSize < 0.01f || hfRes < 2 )
			return;

		float maxHeight = Mathf.Max( 0.01f, heightfield.MaxHeight );
		float groundNorm = heightfield.GroundLevel / maxHeight;
		float half = worldSize * 0.5f;
		Transform root = pile.transform;

		for ( int z = 0; z < hfRes; z++ )
		{
			for ( int x = 0; x < hfRes; x++ )
			{
				if ( heightfield.GetCellNormalizedHeight( x, z ) < groundNorm )
					continue;

				heightfield.CellCenterLocal( x, z, out float localX, out float localZ );
				Vector3 world = root.TransformPoint( new Vector3( localX, 0f, localZ ) );
				if ( !TryWorldToPixel( world, out int px, out int py ) )
					continue;

				_basePixels[ py * res + px ] = gold;
			}
		}
	}

	void StampPileGoldAuthored( TreasurePileVisual pile, Color32 gold, int res )
	{
		float worldSize = pile.AuthoredWorldSize;
		if ( worldSize < 0.01f )
			return;

		int steps = Mathf.Clamp( Mathf.CeilToInt( worldSize / Mathf.Max( 0.05f, _mapBounds.size.x / res ) ), 8, 128 );
		float half = worldSize * 0.5f;
		Transform root = pile.transform;

		for ( int z = 0; z <= steps; z++ )
		{
			float u = z / ( float )steps;
			float localZ = Mathf.Lerp( -half, half, u );
			for ( int x = 0; x <= steps; x++ )
			{
				float t = x / ( float )steps;
				float localX = Mathf.Lerp( -half, half, t );
				Vector3 world = root.TransformPoint( new Vector3( localX, 0f, localZ ) );
				if ( !pile.HasAnyHeightAtWorld( world ) )
					continue;
				if ( !TryWorldToPixel( world, out int px, out int py ) )
					continue;
				_basePixels[ py * res + px ] = gold;
			}
		}
	}

	void StampRegions( MapDefinition def, Stopwatch sw, float budgetMs )
	{
		if ( _volumes.Count == 0 )
		{
			_phase = BakePhase.UploadBase;
			return;
		}

		Color32 empty = Color32From( def.emptyColor );
		int res = _resolution;
		float mapMinX = _mapBounds.min.x;
		float mapMinZ = _mapBounds.min.z;
		float mppX = _mapBounds.size.x / res;
		float mppZ = _mapBounds.size.z / res;
		float mapY = _mapBounds.center.y;

		while ( _bakeVolumeIndex < _volumes.Count )
		{
			MapRegionVolume volume = _volumes[ _bakeVolumeIndex ];
			_bakeVolumeIndex++;
			if ( volume == null )
				continue;

			if ( !TryGetVolumePixelBounds( volume, out int vminX, out int vmaxX, out int vminY, out int vmaxY ) )
				continue;

			Color32 tint = Color32From( volume.Color );
			bool fillEmpty = volume.FillEmpty;
			Matrix4x4 worldToLocal = volume.transform.worldToLocalMatrix;
			Vector3 size = volume.Size;
			float halfX = Mathf.Max( 0.01f, size.x ) * 0.5f;
			float halfZ = Mathf.Max( 0.01f, size.z ) * 0.5f;

			for ( int y = vminY; y <= vmaxY; y++ )
			{
				int row = y * res;
				float wz = mapMinZ + ( y + 0.5f ) * mppZ;
				for ( int x = vminX; x <= vmaxX; x++ )
				{
					float wx = mapMinX + ( x + 0.5f ) * mppX;
					Vector3 local = worldToLocal.MultiplyPoint3x4( new Vector3( wx, mapY, wz ) );
					if ( local.x < -halfX || local.x > halfX || local.z < -halfZ || local.z > halfZ )
						continue;

					int i = row + x;
					if ( !fillEmpty && ColorsClose( _basePixels[ i ], empty ) )
						continue;

					_basePixels[ i ] = tint;
				}
			}

			if ( !_forceImmediateBake && sw.Elapsed.TotalMilliseconds >= budgetMs )
				return;
		}

		_phase = BakePhase.UploadBase;
	}

	bool TryGetVolumePixelBounds( MapRegionVolume volume, out int minX, out int maxX, out int minY, out int maxY )
	{
		minX = 0;
		maxX = 0;
		minY = 0;
		maxY = 0;
		if ( !_hasBounds )
			return false;

		Transform t = volume.transform;
		Vector3 size = volume.Size;
		float hx = Mathf.Max( 0.01f, size.x ) * 0.5f;
		float hz = Mathf.Max( 0.01f, size.z ) * 0.5f;

		float minWx = float.MaxValue;
		float maxWx = float.MinValue;
		float minWz = float.MaxValue;
		float maxWz = float.MinValue;
		ExpandCorner( t, -hx, -hz, ref minWx, ref maxWx, ref minWz, ref maxWz );
		ExpandCorner( t, hx, -hz, ref minWx, ref maxWx, ref minWz, ref maxWz );
		ExpandCorner( t, -hx, hz, ref minWx, ref maxWx, ref minWz, ref maxWz );
		ExpandCorner( t, hx, hz, ref minWx, ref maxWx, ref minWz, ref maxWz );

		float sizeX = _mapBounds.size.x;
		float sizeZ = _mapBounds.size.z;
		if ( sizeX < 1e-4f || sizeZ < 1e-4f )
			return false;

		float mapMinX = _mapBounds.min.x;
		float mapMaxX = _mapBounds.max.x;
		float mapMinZ = _mapBounds.min.z;
		float mapMaxZ = _mapBounds.max.z;
		if ( maxWx < mapMinX || minWx > mapMaxX || maxWz < mapMinZ || minWz > mapMaxZ )
			return false;

		minX = Mathf.Clamp( Mathf.FloorToInt( ( minWx - mapMinX ) / sizeX * _resolution ), 0, _resolution - 1 );
		maxX = Mathf.Clamp( Mathf.FloorToInt( ( maxWx - mapMinX ) / sizeX * _resolution ), 0, _resolution - 1 );
		minY = Mathf.Clamp( Mathf.FloorToInt( ( minWz - mapMinZ ) / sizeZ * _resolution ), 0, _resolution - 1 );
		maxY = Mathf.Clamp( Mathf.FloorToInt( ( maxWz - mapMinZ ) / sizeZ * _resolution ), 0, _resolution - 1 );
		if ( minX > maxX || minY > maxY )
			return false;
		return true;
	}

	static void ExpandCorner(
		Transform t,
		float localX,
		float localZ,
		ref float minWx,
		ref float maxWx,
		ref float minWz,
		ref float maxWz )
	{
		Vector3 world = t.TransformPoint( new Vector3( localX, 0f, localZ ) );
		if ( world.x < minWx )
			minWx = world.x;
		if ( world.x > maxWx )
			maxWx = world.x;
		if ( world.z < minWz )
			minWz = world.z;
		if ( world.z > maxWz )
			maxWz = world.z;
	}

	void TickDiscovery( MapDefinition def )
	{
		if ( !_hasBounds || _discovery == null )
			return;

		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null )
			return;

		Vector3 pos = player.transform.position;
		if ( !TryWorldToPixel( pos, out int cx, out int cy ) )
			return;

		float radius = def.discoveryRadius;
		float soft = Mathf.Clamp01( def.discoverySoftness );
		if ( cx == _lastDiscoveryCx
			&& cy == _lastDiscoveryCy
			&& Mathf.Approximately( radius, _lastDiscoveryRadius )
			&& Mathf.Approximately( soft, _lastDiscoverySoftness ) )
			return;

		_lastDiscoveryCx = cx;
		_lastDiscoveryCy = cy;
		_lastDiscoveryRadius = radius;
		_lastDiscoverySoftness = soft;

		float sizeX = _mapBounds.size.x;
		float sizeZ = _mapBounds.size.z;
		float mppX = sizeX / _resolution;
		float mppZ = sizeZ / _resolution;
		float metersPerPixel = Mathf.Max( mppX, mppZ );
		int pixelRadius = Mathf.CeilToInt( radius / Mathf.Max( 0.001f, metersPerPixel ) ) + 1;
		float softStart = radius * ( 1f - soft );
		float softRange = Mathf.Max( 0.001f, radius - softStart );
		float radiusSq = radius * radius;
		float softStartSq = softStart * softStart;

		float mapMinX = _mapBounds.min.x;
		float mapMinZ = _mapBounds.min.z;
		float posX = pos.x;
		float posZ = pos.z;

		int minX = Mathf.Max( 0, cx - pixelRadius );
		int maxX = Mathf.Min( _resolution - 1, cx + pixelRadius );
		int minY = Mathf.Max( 0, cy - pixelRadius );
		int maxY = Mathf.Min( _resolution - 1, cy + pixelRadius );

		bool changed = false;
		int dirtyMinX = maxX;
		int dirtyMaxX = minX;
		int dirtyMinY = maxY;
		int dirtyMaxY = minY;

		for ( int y = minY; y <= maxY; y++ )
		{
			float wz = mapMinZ + ( y + 0.5f ) * mppZ;
			float dz = wz - posZ;
			float dzSq = dz * dz;
			int row = y * _resolution;
			for ( int x = minX; x <= maxX; x++ )
			{
				float wx = mapMinX + ( x + 0.5f ) * mppX;
				float dx = wx - posX;
				float distSq = dx * dx + dzSq;
				if ( distSq > radiusSq )
					continue;

				byte value;
				if ( soft <= 0f || distSq <= softStartSq )
					value = 255;
				else
				{
					float dist = Mathf.Sqrt( distSq );
					float visibility = 1f - Mathf.Clamp01( ( dist - softStart ) / softRange );
					value = ( byte )Mathf.Clamp( Mathf.RoundToInt( visibility * 255f ), 0, 255 );
				}

				int i = row + x;
				if ( value <= _discovery[ i ] )
					continue;

				_discovery[ i ] = value;
				changed = true;
				if ( x < dirtyMinX )
					dirtyMinX = x;
				if ( x > dirtyMaxX )
					dirtyMaxX = x;
				if ( y < dirtyMinY )
					dirtyMinY = y;
				if ( y > dirtyMaxY )
					dirtyMaxY = y;
			}
		}

		if ( !changed )
			return;

		_coverageDirty = true;

		if ( MapUI.IsOpen )
		{
			CompositeDisplayRect( def, dirtyMinX, dirtyMaxX, dirtyMinY, dirtyMaxY );
			MarkDirtyRect( dirtyMinX, dirtyMaxX, dirtyMinY, dirtyMaxY );
			_displayDirty = true;
		}
		else
		{
			_gpuUploadPending = true;
		}
	}

	void RebuildFullDisplay( MapDefinition def )
	{
		CompositeDisplayRect( def, 0, _resolution - 1, 0, _resolution - 1 );
		MarkDirtyRect( 0, _resolution - 1, 0, _resolution - 1 );
		_displayDirty = true;
		_gpuUploadPending = false;
	}

	void CompositeDisplayRect( MapDefinition def, int minX, int maxX, int minY, int maxY )
	{
		if ( _basePixels == null || _discovery == null || _displayPixels == null )
			return;

		Color32 empty = Color32From( def.emptyColor );
		int res = _resolution;
		minX = Mathf.Clamp( minX, 0, res - 1 );
		maxX = Mathf.Clamp( maxX, 0, res - 1 );
		minY = Mathf.Clamp( minY, 0, res - 1 );
		maxY = Mathf.Clamp( maxY, 0, res - 1 );

		for ( int y = minY; y <= maxY; y++ )
		{
			int row = y * res;
			for ( int x = minX; x <= maxX; x++ )
			{
				int i = row + x;
				Color32 baseColor = _basePixels[ i ];
				float discover = _debugIgnoreDiscovery ? 1f : _discovery[ i ] / 255f;
				_displayPixels[ i ] = LerpColor32( empty, baseColor, discover );
			}
		}
	}

	void MarkDirtyRect( int minX, int maxX, int minY, int maxY )
	{
		if ( !_hasDirtyRect )
		{
			_dirtyMinX = minX;
			_dirtyMaxX = maxX;
			_dirtyMinY = minY;
			_dirtyMaxY = maxY;
			_hasDirtyRect = true;
			return;
		}

		if ( minX < _dirtyMinX )
			_dirtyMinX = minX;
		if ( maxX > _dirtyMaxX )
			_dirtyMaxX = maxX;
		if ( minY < _dirtyMinY )
			_dirtyMinY = minY;
		if ( maxY > _dirtyMaxY )
			_dirtyMaxY = maxY;
	}

	void FlushDisplayIfNeeded( MapDefinition def, bool forceUpload )
	{
		if ( ( MapUI.IsOpen || forceUpload ) && _gpuUploadPending && def != null )
		{
			RebuildFullDisplay( def );
			_gpuUploadPending = false;
		}

		if ( !_displayDirty || _displayTexture == null || _displayPixels == null )
			return;

		if ( !MapUI.IsOpen && !forceUpload )
			return;

		if ( _hasDirtyRect
			&& _dirtyMinX <= _dirtyMaxX
			&& _dirtyMinY <= _dirtyMaxY )
		{
			int minX = Mathf.Clamp( _dirtyMinX, 0, _resolution - 1 );
			int maxX = Mathf.Clamp( _dirtyMaxX, 0, _resolution - 1 );
			int minY = Mathf.Clamp( _dirtyMinY, 0, _resolution - 1 );
			int maxY = Mathf.Clamp( _dirtyMaxY, 0, _resolution - 1 );
			int width = maxX - minX + 1;
			int height = maxY - minY + 1;
			int needed = width * height;
			if ( needed > 0 && ( width != _resolution || height != _resolution ) )
			{
				if ( _uploadScratch == null || _uploadScratch.Length != needed )
					_uploadScratch = new Color32[ needed ];

				int dst = 0;
				for ( int y = minY; y <= maxY; y++ )
				{
					int srcRow = y * _resolution + minX;
					for ( int x = 0; x < width; x++ )
						_uploadScratch[ dst++ ] = _displayPixels[ srcRow + x ];
				}

				_displayTexture.SetPixels32( minX, minY, width, height, _uploadScratch );
				_displayTexture.Apply( false, false );
				_hasDirtyRect = false;
				_displayDirty = false;
				return;
			}
		}

		_displayTexture.SetPixels32( _displayPixels );
		_displayTexture.Apply( false, false );
		_hasDirtyRect = false;
		_displayDirty = false;
	}

	bool TryWorldToPixel( Vector3 worldPos, out int px, out int py )
	{
		px = 0;
		py = 0;
		if ( !_hasBounds )
			return false;

		float sizeX = _mapBounds.size.x;
		float sizeZ = _mapBounds.size.z;
		if ( sizeX < 1e-4f || sizeZ < 1e-4f )
			return false;

		float u = ( worldPos.x - _mapBounds.min.x ) / sizeX;
		float v = ( worldPos.z - _mapBounds.min.z ) / sizeZ;
		if ( u < 0f || u > 1f || v < 0f || v > 1f )
			return false;

		px = Mathf.Clamp( Mathf.FloorToInt( u * _resolution ), 0, _resolution - 1 );
		py = Mathf.Clamp( Mathf.FloorToInt( v * _resolution ), 0, _resolution - 1 );
		return true;
	}

	void ReleaseTextures()
	{
		if ( _displayTexture != null )
		{
			Destroy( _displayTexture );
			_displayTexture = null;
		}

		_basePixels = null;
		_discovery = null;
		_displayPixels = null;
		_uploadScratch = null;
		_fillTrav = null;
	}

	static Color32 Color32From( Color color )
	{
		return new Color32(
			( byte )Mathf.Clamp( Mathf.RoundToInt( color.r * 255f ), 0, 255 ),
			( byte )Mathf.Clamp( Mathf.RoundToInt( color.g * 255f ), 0, 255 ),
			( byte )Mathf.Clamp( Mathf.RoundToInt( color.b * 255f ), 0, 255 ),
			( byte )Mathf.Clamp( Mathf.RoundToInt( color.a * 255f ), 0, 255 ) );
	}

	static Color32 LerpColor32( Color32 a, Color32 b, float t )
	{
		t = Mathf.Clamp01( t );
		return new Color32(
			( byte )Mathf.RoundToInt( a.r + ( b.r - a.r ) * t ),
			( byte )Mathf.RoundToInt( a.g + ( b.g - a.g ) * t ),
			( byte )Mathf.RoundToInt( a.b + ( b.b - a.b ) * t ),
			( byte )Mathf.RoundToInt( a.a + ( b.a - a.a ) * t ) );
	}

	static bool ColorsClose( Color32 a, Color32 b )
	{
		return Mathf.Abs( a.r - b.r ) <= 2
			&& Mathf.Abs( a.g - b.g ) <= 2
			&& Mathf.Abs( a.b - b.b ) <= 2;
	}

	static void FillArray( Color32[] pixels, Color32 color )
	{
		for ( int i = 0; i < pixels.Length; i++ )
			pixels[ i ] = color;
	}
}
