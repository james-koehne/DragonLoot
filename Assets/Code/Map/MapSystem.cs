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

	[SerializeField]
	MapDefinition _definition;

	readonly List<MapRegionVolume> _volumes = new List<MapRegionVolume>( 32 );
	readonly List<TreasurePileVisual> _piles = new List<TreasurePileVisual>( 16 );

	MapDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	Color32[] _basePixels;
	byte[] _discovery;
	Color32[] _displayPixels;
	Texture2D _displayTexture;

	int _resolution;
	Bounds _mapBounds;
	bool _hasBounds;
	bool _baseReady;
	bool _displayDirty;

	BakePhase _phase = BakePhase.Idle;
	int _bakeRow;
	int _bakePileIndex;
	int _lastPaintRevision = -1;
	int _lastOverlayRevision = -1;
	int _cachedPileCount = -1;
	bool _forceImmediateBake;
	float _nextPileRefreshTime;
	bool _debugIgnoreDiscovery;
	float _lastBakeElapsedMs;
	string _bakePhaseName = "Idle";

	int _lastDiscoveryMinX;
	int _lastDiscoveryMaxX;
	int _lastDiscoveryMinY;
	int _lastDiscoveryMaxY;
	bool _hasLastDiscoveryRect;

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

		def.EnsureDefaults();
		EnsureBuffers( def.resolution );

		bool mapOpen = MapUI.IsOpen;
		if ( mapOpen )
			_forceImmediateBake = true;

		TickDiscovery( def );
		TickBake( def );
		FlushDisplayIfNeeded();
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

		FlushDisplayIfNeeded();
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

		MapDefinition def = Definition;
		if ( def != null )
			RebuildFullDisplay( def );
		FlushDisplayIfNeeded();
	}

	public void DebugClearDiscovery()
	{
		if ( _discovery == null )
			return;

		System.Array.Clear( _discovery, 0, _discovery.Length );
		_hasLastDiscoveryRect = false;

		MapDefinition def = Definition;
		if ( def != null )
			RebuildFullDisplay( def );
		FlushDisplayIfNeeded();
	}

	public void DebugSetIgnoreDiscovery( bool ignore )
	{
		if ( _debugIgnoreDiscovery == ignore )
			return;

		_debugIgnoreDiscovery = ignore;
		MapDefinition def = Definition;
		if ( def != null )
			RebuildFullDisplay( def );
		FlushDisplayIfNeeded();
	}

	public void DebugForceRebuild()
	{
		_lastPaintRevision = -1;
		_lastOverlayRevision = -1;
		_cachedPileCount = -1;
		_nextPileRefreshTime = 0f;
		RequestImmediateBake();
	}

	public float GetDiscoveryCoverage01()
	{
		if ( _discovery == null || _discovery.Length == 0 )
			return 0f;

		int discovered = 0;
		for ( int i = 0; i < _discovery.Length; i++ )
		{
			if ( _discovery[ i ] > 8 )
				discovered++;
		}

		return discovered / ( float )_discovery.Length;
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
		_displayTexture = new Texture2D( _resolution, _resolution, TextureFormat.RGBA32, false, false );
		_displayTexture.name = "MapDisplay";
		_displayTexture.filterMode = FilterMode.Bilinear;
		_displayTexture.wrapMode = TextureWrapMode.Clamp;
		_baseReady = false;
		_hasBounds = false;
		_phase = BakePhase.Idle;
		_bakeRow = 0;
		_hasLastDiscoveryRect = false;
		FillArray( _basePixels, Color32From( Color.black ) );
		System.Array.Clear( _discovery, 0, _discovery.Length );
		FillArray( _displayPixels, Color32From( Color.black ) );
		_displayTexture.SetPixels32( _displayPixels );
		_displayTexture.Apply( false, false );
		_displayDirty = false;
	}

	void TickBake( MapDefinition def )
	{
		TreasureSurfaceAuthoring authoring = TreasureSurfaceAuthoring.Instance;
		int paintRevision = authoring != null ? authoring.PaintRevision : -1;
		int overlayRevision = MapOverlayRegistrar.Revision;
		RefreshPileCacheIfNeeded();

		bool needsRebuild = paintRevision != _lastPaintRevision
			|| overlayRevision != _lastOverlayRevision
			|| _piles.Count != _cachedPileCount
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
					break;

				case BakePhase.FillBase:
					FillBaseRows( authoring, def, sw, budgetMs );
					break;

				case BakePhase.StampGold:
					StampGoldRows( def, sw, budgetMs );
					break;

				case BakePhase.StampRegions:
					StampRegions( def );
					_phase = BakePhase.UploadBase;
					break;

				case BakePhase.UploadBase:
					_lastOverlayRevision = overlayRevision;
					_cachedPileCount = _piles.Count;
					_baseReady = true;
					RebuildFullDisplay( def );
					_phase = BakePhase.Idle;
					_bakePhaseName = "Idle";
					_lastBakeElapsedMs = ( float )sw.Elapsed.TotalMilliseconds;
					_forceImmediateBake = false;
					return;
			}

			if ( !_forceImmediateBake && sw.Elapsed.TotalMilliseconds >= budgetMs )
				return;
		}
	}

	void BeginBake( bool force )
	{
		_forceImmediateBake = force || _forceImmediateBake;
		_baseReady = false;
		_phase = BakePhase.ScanBounds;
		_bakePhaseName = "ScanBounds";
		_bakeRow = 0;
		_bakePileIndex = 0;
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

	void FillBaseRows( TreasureSurfaceAuthoring authoring, MapDefinition def, Stopwatch sw, float budgetMs )
	{
		Color32 empty = Color32From( def.emptyColor );
		Color32 walkable = Color32From( def.walkableColor );
		if ( _bakeRow == 0 )
			FillArray( _basePixels, empty );

		int res = _resolution;
		while ( _bakeRow < res )
		{
			int y = _bakeRow;
			for ( int x = 0; x < res; x++ )
			{
				Vector3 world = PixelToWorld( x, y );
				bool traversable = false;
				if ( authoring != null
					&& authoring.TryWorldToCell( world, out int cellX, out int cellZ )
					&& authoring.TryGetPaint( cellX, cellZ, out bool paintTrav, out _ ) )
				{
					traversable = paintTrav;
				}

				_basePixels[ y * res + x ] = traversable ? walkable : empty;
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

		float cell = worldSize / ( hfRes - 1 );
		float half = worldSize * 0.5f;
		Transform root = pile.transform;

		for ( int z = 0; z < hfRes; z++ )
		{
			float localZ = -half + z * cell;
			for ( int x = 0; x < hfRes; x++ )
			{
				float localX = -half + x * cell;
				if ( !heightfield.ExistsAtLocal( localX, localZ ) )
					continue;

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

	void StampRegions( MapDefinition def )
	{
		MapOverlayRegistrar.CollectVolumes( _volumes );
		if ( _volumes.Count == 0 )
			return;

		Color32 empty = Color32From( def.emptyColor );
		int res = _resolution;

		for ( int y = 0; y < res; y++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				int i = y * res + x;
				Vector3 world = PixelToWorld( x, y );
				Color32 current = _basePixels[ i ];
				bool isEmpty = ColorsClose( current, empty );

				for ( int v = 0; v < _volumes.Count; v++ )
				{
					MapRegionVolume volume = _volumes[ v ];
					if ( volume == null || !volume.ContainsWorldXZ( world ) )
						continue;

					if ( isEmpty && !volume.FillEmpty )
						continue;

					_basePixels[ i ] = Color32From( volume.Color );
					break;
				}
			}
		}
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
		float sizeX = _mapBounds.size.x;
		float sizeZ = _mapBounds.size.z;
		float metersPerPixelX = sizeX / _resolution;
		float metersPerPixelZ = sizeZ / _resolution;
		float metersPerPixel = Mathf.Max( metersPerPixelX, metersPerPixelZ );
		int pixelRadius = Mathf.CeilToInt( radius / Mathf.Max( 0.001f, metersPerPixel ) ) + 1;
		float soft = Mathf.Clamp01( def.discoverySoftness );
		float softStart = radius * ( 1f - soft );
		float softRange = Mathf.Max( 0.001f, radius - softStart );

		int minX = Mathf.Max( 0, cx - pixelRadius );
		int maxX = Mathf.Min( _resolution - 1, cx + pixelRadius );
		int minY = Mathf.Max( 0, cy - pixelRadius );
		int maxY = Mathf.Min( _resolution - 1, cy + pixelRadius );

		bool changed = false;
		for ( int y = minY; y <= maxY; y++ )
		{
			for ( int x = minX; x <= maxX; x++ )
			{
				Vector3 world = PixelToWorld( x, y );
				float dx = world.x - pos.x;
				float dz = world.z - pos.z;
				float dist = Mathf.Sqrt( dx * dx + dz * dz );
				if ( dist > radius )
					continue;

				float visibility = 1f;
				if ( dist > softStart )
					visibility = 1f - Mathf.Clamp01( ( dist - softStart ) / softRange );

				byte value = ( byte )Mathf.Clamp( Mathf.RoundToInt( visibility * 255f ), 0, 255 );
				int i = y * _resolution + x;
				if ( value <= _discovery[ i ] )
					continue;

				_discovery[ i ] = value;
				changed = true;
			}
		}

		if ( !changed )
			return;

		CompositeDisplayRect( def, minX, maxX, minY, maxY );
		if ( _hasLastDiscoveryRect )
		{
			minX = Mathf.Min( minX, _lastDiscoveryMinX );
			maxX = Mathf.Max( maxX, _lastDiscoveryMaxX );
			minY = Mathf.Min( minY, _lastDiscoveryMinY );
			maxY = Mathf.Max( maxY, _lastDiscoveryMaxY );
		}

		_lastDiscoveryMinX = minX;
		_lastDiscoveryMaxX = maxX;
		_lastDiscoveryMinY = minY;
		_lastDiscoveryMaxY = maxY;
		_hasLastDiscoveryRect = true;
		_displayDirty = true;
	}

	void RebuildFullDisplay( MapDefinition def )
	{
		CompositeDisplayRect( def, 0, _resolution - 1, 0, _resolution - 1 );
		_displayDirty = true;
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

	void FlushDisplayIfNeeded()
	{
		if ( !_displayDirty || _displayTexture == null || _displayPixels == null )
			return;

		_displayTexture.SetPixels32( _displayPixels );
		_displayTexture.Apply( false, false );
		_displayDirty = false;
	}

	void RefreshPileCacheIfNeeded()
	{
		if ( Time.unscaledTime < _nextPileRefreshTime && _piles.Count > 0 )
			return;

		_nextPileRefreshTime = Time.unscaledTime + 1f;

		TreasurePileVisual[] found = Object.FindObjectsByType<TreasurePileVisual>(
			FindObjectsInactive.Exclude,
			FindObjectsSortMode.None );
		_piles.Clear();
		if ( found == null )
			return;

		for ( int i = 0; i < found.Length; i++ )
		{
			if ( found[ i ] != null )
				_piles.Add( found[ i ] );
		}
	}

	Vector3 PixelToWorld( int px, int py )
	{
		float u = ( px + 0.5f ) / _resolution;
		float v = ( py + 0.5f ) / _resolution;
		return new Vector3(
			Mathf.Lerp( _mapBounds.min.x, _mapBounds.max.x, u ),
			_mapBounds.center.y,
			Mathf.Lerp( _mapBounds.min.z, _mapBounds.max.z, v ) );
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
