using UnityEngine;

/// <summary>
/// Level-authored layout + cell paint for the treasure surface.
/// Place in the Level scene; edit bounds and paint in the Scene view without Play Mode.
/// Paint grids live on a <see cref="TreasureSurfacePaintAsset"/> sidecar (.paintbin), not in scene YAML.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class TreasureSurfaceAuthoring : MonoBehaviour
{
	static TreasureSurfaceAuthoring _instance;

	[Header( "Definition (tuning)" )]
	[SerializeField]
	TreasureSurfaceDefinition definition;

	[Header( "Paint Asset" )]
	[Tooltip( "Binary paint grids (PreferBinarySerialization). Prefer one asset per level scene." )]
	[SerializeField]
	TreasureSurfacePaintAsset paintAsset;

	[Header( "Layout (level)" )]
	[SerializeField]
	Vector3 worldOrigin = Vector3.zero;

	[SerializeField]
	[Min( 8f )]
	float worldSizeX = 128f;

	[SerializeField]
	[Min( 8f )]
	float worldSizeZ = 128f;

	[SerializeField]
	[Min( 1f )]
	float chunkSize = 8f;

	[SerializeField]
	[Min( 8 )]
	int cellsPerChunk = 64;

	[SerializeField]
	float baseHeight = 0f;

	[Header( "Paint" )]
	[Tooltip( "When true, newly allocated cells start non-traversable so you paint where the surface is allowed." )]
	[SerializeField]
	bool defaultNonTraversable = true;

	[Header( "Height Bake" )]
	[SerializeField]
	[Tooltip( "Layers included when baking per-cell authored heights (and height-paint cursor hits). Steep faces are skipped; only normals within Max Slope of world up count." )]
	LayerMask heightBakeMask = ~0;

	[SerializeField]
	[Tooltip( "World Y where bake rays start. Used when Start Mode is Absolute." )]
	float heightBakeRayStartY = 32f;

	[SerializeField]
	[Tooltip( "How bake ray start Y is chosen." )]
	HeightBakeRayStartMode heightBakeRayStartMode = HeightBakeRayStartMode.AboveMaxWorldY;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Extra height added above the chosen start reference (maxWorldY / baseHeight / absolute)." )]
	float heightBakeRayPad = 2f;

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Maximum downward cast distance from the ray start." )]
	float heightBakeRayDistance = 64f;

	[SerializeField]
	[Tooltip( "Added to hit Y after a successful cast (e.g. sit slightly above collider)." )]
	float heightBakeHitOffset = 0f;

	[SerializeField]
	[Tooltip( "When true, only traversable cells are sampled. When false, every cell is baked." )]
	bool heightBakeTraversableOnly = true;

	[SerializeField]
	[Tooltip( "Height written on miss (cells are always marked non-traversable when the ray misses)." )]
	HeightBakeMissBehavior heightBakeMissBehavior = HeightBakeMissBehavior.SetBaseHeight;

	[SerializeField]
	[Tooltip( "Whether bake rays hit triggers." )]
	QueryTriggerInteraction heightBakeTriggerInteraction = QueryTriggerInteraction.Ignore;

	[SerializeField]
	[Range( 0f, 89f )]
	[Tooltip( "Hits steeper than this from world up are skipped so rays continue to floors, ramps, and stairs." )]
	float heightBakeMaxSlopeDegrees = 45f;

	[Header( "Height Overlay" )]
	[SerializeField]
	[Min( 1 )]
	[Tooltip( "How many surface chunks of overlay mesh to build each editor frame." )]
	int overlayChunksPerFrame = 8;

	/// <summary>Legacy scene-inline paint. Migrated into <see cref="paintAsset"/> then cleared.</summary>
	[SerializeField]
	[HideInInspector]
	byte[] traversablePaint;

	/// <summary>Legacy scene-inline paint. Migrated into <see cref="paintAsset"/> then cleared.</summary>
	[SerializeField]
	[HideInInspector]
	byte[] materialPaint;

	[SerializeField]
	[HideInInspector]
	int paintCellsX;

	[SerializeField]
	[HideInInspector]
	int paintCellsZ;

	[SerializeField]
	[HideInInspector]
	int paintRevision;

	int _paintNotifyBatchDepth;

	const int HeightBakeHitBufferSize = 32;
	RaycastHit[] _heightBakeHits;

	public static TreasureSurfaceAuthoring Instance => _instance;

	public TreasureSurfaceDefinition Definition => definition;
	public TreasureSurfacePaintAsset PaintAsset => paintAsset;
	public Vector3 WorldOrigin => worldOrigin;
	public float WorldSizeX => worldSizeX;
	public float WorldSizeZ => worldSizeZ;
	public float ChunkSize => chunkSize;
	public int CellsPerChunk => cellsPerChunk;
	public float BaseHeight => baseHeight;
	public bool DefaultNonTraversable => defaultNonTraversable;
	public LayerMask HeightBakeMask => heightBakeMask;
	public float HeightBakeRayStartY => heightBakeRayStartY;
	public HeightBakeRayStartMode HeightBakeStartMode => heightBakeRayStartMode;
	public float HeightBakeRayPad => heightBakeRayPad;
	public float HeightBakeRayDistance => heightBakeRayDistance;
	public float HeightBakeHitOffset => heightBakeHitOffset;
	public bool HeightBakeTraversableOnly => heightBakeTraversableOnly;
	public HeightBakeMissBehavior HeightBakeMissMode => heightBakeMissBehavior;
	public QueryTriggerInteraction HeightBakeTriggerInteraction => heightBakeTriggerInteraction;
	public float HeightBakeMaxSlopeDegrees => heightBakeMaxSlopeDegrees;
	public int OverlayChunksPerFrame => overlayChunksPerFrame;
	public float CellSize => Mathf.Max( 0.01f, chunkSize / Mathf.Max( 1, cellsPerChunk ) );
	public int ChunkCountX => Mathf.Max( 1, Mathf.CeilToInt( worldSizeX / Mathf.Max( 1f, chunkSize ) ) );
	public int ChunkCountZ => Mathf.Max( 1, Mathf.CeilToInt( worldSizeZ / Mathf.Max( 1f, chunkSize ) ) );
	public int TotalCellsX => ChunkCountX * cellsPerChunk;
	public int TotalCellsZ => ChunkCountZ * cellsPerChunk;
	public int PaintRevision => paintRevision;

	public Bounds WorldBounds
	{
		get
		{
			Vector3 size = new Vector3( worldSizeX, 0.1f, worldSizeZ );
			return new Bounds( new Vector3( worldOrigin.x, baseHeight, worldOrigin.z ), size );
		}
	}

	void OnEnable()
	{
		_instance = this;
		EnsurePaintBuffers();

		if ( Application.isPlaying && TreasureSurfaceWorld.Instance != null )
			TreasureSurfaceWorld.Instance.RebindAuthoring();
	}

	void OnDisable()
	{
		if ( _instance == this )
			_instance = null;
	}

	void OnValidate()
	{
		worldSizeX = Mathf.Max( 8f, worldSizeX );
		worldSizeZ = Mathf.Max( 8f, worldSizeZ );
		chunkSize = Mathf.Max( 1f, chunkSize );
		cellsPerChunk = Mathf.Max( 8, cellsPerChunk );
		heightBakeMaxSlopeDegrees = Mathf.Clamp( heightBakeMaxSlopeDegrees, 0f, 89f );
		EnsurePaintBuffers();
	}

	public void SetDefinition( TreasureSurfaceDefinition def )
	{
		definition = def;
		if ( def == null )
			return;

		worldOrigin = def.worldOrigin;
		worldSizeX = def.worldSizeX;
		worldSizeZ = def.worldSizeZ;
		chunkSize = def.chunkSize;
		cellsPerChunk = def.cellsPerChunk;
		baseHeight = def.baseHeight;
		EnsurePaintBuffers();
	}

	public void SetLayout(
		Vector3 origin,
		float sizeX,
		float sizeZ,
		float chunk,
		int cells,
		float height )
	{
		worldOrigin = origin;
		worldSizeX = Mathf.Max( 8f, sizeX );
		worldSizeZ = Mathf.Max( 8f, sizeZ );
		chunkSize = Mathf.Max( 1f, chunk );
		cellsPerChunk = Mathf.Max( 8, cells );
		baseHeight = height;
		EnsurePaintBuffers();
	}

	/// <summary>Pushes layout into the runtime definition used by TreasureSurfaceWorld.</summary>
	public void ApplyLayoutToDefinition( TreasureSurfaceDefinition def )
	{
		if ( def == null )
			return;

		def.worldOrigin = worldOrigin;
		def.worldSizeX = worldSizeX;
		def.worldSizeZ = worldSizeZ;
		def.chunkSize = chunkSize;
		def.cellsPerChunk = cellsPerChunk;
		def.baseHeight = baseHeight;
	}

	public void SetPaintAsset( TreasureSurfacePaintAsset asset )
	{
		paintAsset = asset;
		EnsurePaintBuffers();
	}

	public void EnsurePaintBuffers()
	{
#if UNITY_EDITOR
		if ( HasLegacyPaint() || paintAsset == null )
			EditorEnsurePaintAssetAssigned();
		MigrateLegacyPaintIntoAsset();
#endif
		if ( paintAsset == null )
			return;

		paintAsset.EnsureBuffers( TotalCellsX, TotalCellsZ, defaultNonTraversable, baseHeight );
		paintAsset.EnsureHeightDefaults( baseHeight );
		paintCellsX = paintAsset.CellsX;
		paintCellsZ = paintAsset.CellsZ;
	}

	public void BeginPaintNotifyBatch()
	{
		_paintNotifyBatchDepth++;
	}

	public void EndPaintNotifyBatch()
	{
		if ( _paintNotifyBatchDepth <= 0 )
			return;

		_paintNotifyBatchDepth--;
		if ( _paintNotifyBatchDepth == 0 )
			NotifyPaintChanged();
	}

	void NotifyPaintChanged()
	{
		if ( _paintNotifyBatchDepth > 0 )
			return;

		paintRevision++;
#if UNITY_EDITOR
		if ( paintAsset != null )
			paintAsset.MarkDirty();
#endif
	}

	bool TryGetPaintArrays(
		out byte[] traversable,
		out byte[] materials,
		out float[] heights,
		out int cellsX,
		out int cellsZ )
	{
		EnsurePaintBuffers();
		if ( paintAsset == null || !paintAsset.HasBuffers )
		{
			traversable = null;
			materials = null;
			heights = null;
			cellsX = 0;
			cellsZ = 0;
			return false;
		}

		traversable = paintAsset.TraversablePaint;
		materials = paintAsset.MaterialPaint;
		heights = paintAsset.HeightPaint;
		cellsX = paintAsset.CellsX;
		cellsZ = paintAsset.CellsZ;
		return true;
	}

	/// <summary>
	/// Fast path for map bake: traversable paint buffer without per-cell lookups.
	/// </summary>
	public bool TryGetPaintTraversable( out byte[] traversable, out int cellsX, out int cellsZ )
	{
		if ( !TryGetPaintArrays( out traversable, out _, out _, out cellsX, out cellsZ ) )
			return false;
		return traversable != null;
	}

#if UNITY_EDITOR
	public void EditorGetPaintArrays( out byte[] traversable, out byte[] materials, out int cellsX, out int cellsZ )
	{
		EditorGetPaintArrays( out traversable, out materials, out _, out cellsX, out cellsZ );
	}

	public void EditorGetPaintArrays(
		out byte[] traversable,
		out byte[] materials,
		out float[] heights,
		out int cellsX,
		out int cellsZ )
	{
		if ( !TryGetPaintArrays( out traversable, out materials, out heights, out cellsX, out cellsZ ) )
		{
			traversable = System.Array.Empty<byte>();
			materials = System.Array.Empty<byte>();
			heights = System.Array.Empty<float>();
			cellsX = 0;
			cellsZ = 0;
		}
	}

	const string DefaultPaintAssetFolder = "Assets/Definitions/TreasureSurface";
	const string DefaultPaintAssetPath = DefaultPaintAssetFolder + "/TreasureSurfacePaint_Level.asset";

	bool _paintAssetCreateQueued;

	bool HasLegacyPaint()
	{
		return traversablePaint != null
			&& materialPaint != null
			&& paintCellsX > 0
			&& paintCellsZ > 0
			&& traversablePaint.Length >= paintCellsX * paintCellsZ
			&& materialPaint.Length >= paintCellsX * paintCellsZ;
	}

	void EditorEnsurePaintAssetAssigned()
	{
		if ( paintAsset != null )
			return;

		TreasureSurfacePaintAsset existing =
			UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureSurfacePaintAsset>( DefaultPaintAssetPath );
		if ( existing != null )
		{
			paintAsset = existing;
			UnityEditor.EditorUtility.SetDirty( this );
			return;
		}

		if ( _paintAssetCreateQueued )
			return;

		_paintAssetCreateQueued = true;
		UnityEditor.EditorApplication.delayCall += EditorCreateDefaultPaintAssetIfNeeded;
	}

	void EditorCreateDefaultPaintAssetIfNeeded()
	{
		_paintAssetCreateQueued = false;
		if ( this == null || paintAsset != null )
			return;

		TreasureSurfacePaintAsset existing =
			UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureSurfacePaintAsset>( DefaultPaintAssetPath );
		if ( existing != null )
		{
			paintAsset = existing;
			UnityEditor.EditorUtility.SetDirty( this );
			MigrateLegacyPaintIntoAsset();
			EnsurePaintBuffers();
			return;
		}

		if ( !UnityEditor.AssetDatabase.IsValidFolder( "Assets/Definitions" ) )
			UnityEditor.AssetDatabase.CreateFolder( "Assets", "Definitions" );
		if ( !UnityEditor.AssetDatabase.IsValidFolder( DefaultPaintAssetFolder ) )
			UnityEditor.AssetDatabase.CreateFolder( "Assets/Definitions", "TreasureSurface" );

		TreasureSurfacePaintAsset created = ScriptableObject.CreateInstance<TreasureSurfacePaintAsset>();
		created.name = "TreasureSurfacePaint_Level";
		UnityEditor.AssetDatabase.CreateAsset( created, DefaultPaintAssetPath );
		paintAsset = created;
		UnityEditor.EditorUtility.SetDirty( this );
		MigrateLegacyPaintIntoAsset();
		EnsurePaintBuffers();
		UnityEditor.AssetDatabase.SaveAssets();
	}

	void MigrateLegacyPaintIntoAsset()
	{
		if ( paintAsset == null || !HasLegacyPaint() )
			return;

		// Legacy scene arrays are authoritative for a one-time migrate.
		paintAsset.ImportLegacy( traversablePaint, materialPaint, paintCellsX, paintCellsZ, baseHeight );

		traversablePaint = null;
		materialPaint = null;
		UnityEditor.EditorUtility.SetDirty( this );
	}

	/// <summary>Editor menu / tools: force migrate + clear legacy scene arrays.</summary>
	public bool EditorMigratePaintToAsset( bool saveAssets )
	{
		EditorCreateDefaultPaintAssetIfNeeded();
		if ( paintAsset == null )
		{
			TreasureSurfacePaintAsset existing =
				UnityEditor.AssetDatabase.LoadAssetAtPath<TreasureSurfacePaintAsset>( DefaultPaintAssetPath );
			paintAsset = existing;
		}

		MigrateLegacyPaintIntoAsset();
		EnsurePaintBuffers();
		if ( saveAssets )
			UnityEditor.AssetDatabase.SaveAssets();
		return paintAsset != null;
	}
#endif

	public Vector3 CellCenterWorld( int cellX, int cellZ )
	{
		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		float cell = CellSize;
		float wx = worldOrigin.x - halfX + ( cellX + 0.5f ) * cell;
		float wz = worldOrigin.z - halfZ + ( cellZ + 0.5f ) * cell;
		float wy = baseHeight;
		if ( TryGetPaintArrays( out _, out _, out float[] heights, out int cellsX, out int cellsZ )
			&& cellX >= 0
			&& cellZ >= 0
			&& cellX < cellsX
			&& cellZ < cellsZ )
		{
			wy = heights[ cellZ * cellsX + cellX ];
		}

		return new Vector3( wx, wy, wz );
	}

	public float GetPaintHeight( int cellX, int cellZ )
	{
		if ( !TryGetPaintArrays( out _, out _, out float[] heights, out int cellsX, out int cellsZ ) )
			return baseHeight;
		if ( cellX < 0 || cellZ < 0 || cellX >= cellsX || cellZ >= cellsZ )
			return baseHeight;
		return heights[ cellZ * cellsX + cellX ];
	}

	public bool TryWorldToCell( Vector3 world, out int cellX, out int cellZ )
	{
		cellX = 0;
		cellZ = 0;
		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		float localX = world.x - worldOrigin.x + halfX;
		float localZ = world.z - worldOrigin.z + halfZ;
		if ( localX < 0f || localZ < 0f || localX >= worldSizeX || localZ >= worldSizeZ )
			return false;

		float cell = CellSize;
		cellX = Mathf.Clamp( Mathf.FloorToInt( localX / cell ), 0, TotalCellsX - 1 );
		cellZ = Mathf.Clamp( Mathf.FloorToInt( localZ / cell ), 0, TotalCellsZ - 1 );
		return true;
	}

	/// <summary>
	/// Computes the XZ AABB of traversable painted cells (cell centers). Returns false when none.
	/// </summary>
	public bool TryComputeTraversableBoundsXZ( out float minX, out float maxX, out float minZ, out float maxZ )
	{
		minX = 0f;
		maxX = 0f;
		minZ = 0f;
		maxZ = 0f;
		if ( !TryGetPaintArrays( out byte[] trav, out _, out _, out int cellsX, out int cellsZ ) )
			return false;

		bool any = false;
		minX = float.MaxValue;
		maxX = float.MinValue;
		minZ = float.MaxValue;
		maxZ = float.MinValue;
		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		float cell = CellSize;

		for ( int z = 0; z < cellsZ; z++ )
		{
			int row = z * cellsX;
			float wz = worldOrigin.z - halfZ + ( z + 0.5f ) * cell;
			for ( int x = 0; x < cellsX; x++ )
			{
				if ( trav[ row + x ] == 0 )
					continue;

				float wx = worldOrigin.x - halfX + ( x + 0.5f ) * cell;
				if ( wx < minX )
					minX = wx;
				if ( wx > maxX )
					maxX = wx;
				if ( wz < minZ )
					minZ = wz;
				if ( wz > maxZ )
					maxZ = wz;
				any = true;
			}
		}

		return any;
	}

	/// <summary>
	/// True when world XZ sits on authored traversable Treasure surface paint,
	/// including a Chebyshev neighborhood (1 = 3x3). Missing authoring does not block
	/// so isolated pile tools still work.
	/// </summary>
	public static bool HasTreasureSurfaceBelowWorld( Vector3 world, int neighborhoodCells = 1 )
	{
		TreasureSurfaceAuthoring authoring = Instance;
		if ( authoring == null )
			return true;

		if ( !authoring.TryWorldToCell( world, out int cellX, out int cellZ ) )
			return false;

		return authoring.HasTraversableNeighborhood( cellX, cellZ, neighborhoodCells );
	}

	public bool HasTraversableNeighborhood( int cellX, int cellZ, int neighborhoodCells )
	{
		int radius = Mathf.Max( 1, neighborhoodCells );
		if ( !TryGetPaintArrays( out byte[] trav, out _, out _, out int cellsX, out int cellsZ ) )
			return !defaultNonTraversable;

		for ( int z = cellZ - radius; z <= cellZ + radius; z++ )
		{
			if ( z < 0 || z >= cellsZ )
				return false;

			int row = z * cellsX;
			for ( int x = cellX - radius; x <= cellX + radius; x++ )
			{
				if ( x < 0 || x >= cellsX )
					return false;
				if ( trav[ row + x ] == 0 )
					return false;
			}
		}

		return true;
	}

	public bool TryGetPaint( int cellX, int cellZ, out bool traversable, out TreasureSurfaceMaterial material )
	{
		return TryGetPaint( cellX, cellZ, out traversable, out material, out _ );
	}

	public bool TryGetPaint(
		int cellX,
		int cellZ,
		out bool traversable,
		out TreasureSurfaceMaterial material,
		out float height )
	{
		traversable = !defaultNonTraversable;
		material = TreasureSurfaceMaterial.Stone;
		height = baseHeight;
		if ( !TryGetPaintArrays( out byte[] trav, out byte[] mats, out float[] heights, out int cellsX, out int cellsZ ) )
			return false;
		if ( cellX < 0 || cellZ < 0 || cellX >= cellsX || cellZ >= cellsZ )
			return false;

		int i = cellZ * cellsX + cellX;
		traversable = trav[ i ] != 0;
		material = ( TreasureSurfaceMaterial )mats[ i ];
		height = heights[ i ];
		return true;
	}

	/// <summary>
	/// Overlay helper: full chunk solid, empty, or mixed (needs per-cell draw).
	/// </summary>
	public bool TryDescribeChunkPaint(
		int chunkX,
		int chunkZ,
		out bool fullyTraversable,
		out bool anyTraversable,
		out TreasureSurfaceMaterial material )
	{
		fullyTraversable = false;
		anyTraversable = false;
		material = TreasureSurfaceMaterial.Stone;
		if ( !TryGetPaintArrays( out byte[] trav, out byte[] mats, out _, out int cellsXOut, out int cellsZOut ) )
			return false;

		if ( chunkX < 0 || chunkZ < 0 || chunkX >= ChunkCountX || chunkZ >= ChunkCountZ )
			return false;

		int baseX = chunkX * cellsPerChunk;
		int baseZ = chunkZ * cellsPerChunk;
		int blocked = 0;
		int travCount = 0;
		byte firstMat = 0;
		bool hasMat = false;

		for ( int z = 0; z < cellsPerChunk; z++ )
		{
			int worldZ = baseZ + z;
			if ( worldZ >= cellsZOut )
				break;

			for ( int x = 0; x < cellsPerChunk; x++ )
			{
				int worldX = baseX + x;
				if ( worldX >= cellsXOut )
					break;

				int i = worldZ * cellsXOut + worldX;
				if ( trav[ i ] != 0 )
				{
					travCount++;
					if ( !hasMat )
					{
						firstMat = mats[ i ];
						hasMat = true;
					}
				}
				else
				{
					blocked++;
				}
			}
		}

		anyTraversable = travCount > 0;
		fullyTraversable = travCount > 0 && blocked == 0;
		if ( hasMat )
			material = ( TreasureSurfaceMaterial )firstMat;

		return true;
	}

	public void GetChunkWorldRect( int chunkX, int chunkZ, out Vector3 min, out Vector3 max )
	{
		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		float minX = worldOrigin.x - halfX + chunkX * chunkSize;
		float minZ = worldOrigin.z - halfZ + chunkZ * chunkSize;
		min = new Vector3( minX, baseHeight, minZ );
		max = new Vector3( minX + chunkSize, baseHeight, minZ + chunkSize );
	}

	public void SetPaintCell( int cellX, int cellZ, bool traversable, TreasureSurfaceMaterial material )
	{
		SetPaintCell( cellX, cellZ, traversable, material, GetPaintHeight( cellX, cellZ ), paintHeight: false );
	}

	public void SetPaintCell(
		int cellX,
		int cellZ,
		bool traversable,
		TreasureSurfaceMaterial material,
		float height,
		bool paintHeight )
	{
		if ( !TryGetPaintArrays( out byte[] trav, out byte[] mats, out float[] heights, out int cellsX, out int cellsZ ) )
			return;
		if ( cellX < 0 || cellZ < 0 || cellX >= cellsX || cellZ >= cellsZ )
			return;

		int i = cellZ * cellsX + cellX;
		trav[ i ] = traversable ? ( byte )1 : ( byte )0;
		mats[ i ] = ( byte )material;
		if ( paintHeight )
			heights[ i ] = height;
		NotifyPaintChanged();
	}

	/// <summary>
	/// Paints a disc of cells. Radius is in world meters; each covered cell is set exactly.
	/// </summary>
	public void PaintBrush(
		Vector3 worldCenter,
		float radiusMeters,
		bool traversable,
		TreasureSurfaceMaterial material,
		bool paintTraversable,
		bool paintMaterial )
	{
		PaintBrush(
			worldCenter,
			radiusMeters,
			traversable,
			material,
			paintTraversable,
			paintMaterial,
			paintHeight: false,
			height: baseHeight );
	}

	public void PaintBrush(
		Vector3 worldCenter,
		float radiusMeters,
		bool traversable,
		TreasureSurfaceMaterial material,
		bool paintTraversable,
		bool paintMaterial,
		bool paintHeight,
		float height )
	{
		if ( !TryGetPaintArrays( out byte[] trav, out byte[] mats, out float[] heights, out int cellsX, out int cellsZ ) )
			return;
		if ( radiusMeters <= 0f )
			return;

		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;

		float cell = CellSize;
		int cellRadius = Mathf.Max( 0, Mathf.CeilToInt( radiusMeters / cell ) );
		if ( !TryWorldToCell( worldCenter, out int cx, out int cz ) )
		{
			float localX = Mathf.Clamp( worldCenter.x - worldOrigin.x + halfX, 0f, worldSizeX - 0.001f );
			float localZ = Mathf.Clamp( worldCenter.z - worldOrigin.z + halfZ, 0f, worldSizeZ - 0.001f );
			cx = Mathf.FloorToInt( localX / cell );
			cz = Mathf.FloorToInt( localZ / cell );
		}
		float localCenterX = worldCenter.x - worldOrigin.x + halfX;
		float localCenterZ = worldCenter.z - worldOrigin.z + halfZ;
		float radiusSq = radiusMeters * radiusMeters;
		byte travByte = traversable ? ( byte )1 : ( byte )0;
		byte matByte = ( byte )material;

		for ( int z = cz - cellRadius; z <= cz + cellRadius; z++ )
		{
			float dz = ( z + 0.5f ) * cell - localCenterZ;
			float dzSq = dz * dz;
			if ( dzSq > radiusSq )
				continue;

			for ( int x = cx - cellRadius; x <= cx + cellRadius; x++ )
			{
				if ( x < 0 || z < 0 || x >= cellsX || z >= cellsZ )
					continue;

				float dx = ( x + 0.5f ) * cell - localCenterX;
				if ( dx * dx + dzSq > radiusSq )
					continue;

				int i = z * cellsX + x;
				if ( paintTraversable )
				{
					if ( trav[ i ] != travByte )
						trav[ i ] = travByte;
				}

				if ( paintMaterial )
				{
					if ( mats[ i ] != matByte )
						mats[ i ] = matByte;
				}

				if ( paintHeight )
				{
					// Absolute height paint only writes onto traversable cells.
					if ( trav[ i ] != 0 )
						heights[ i ] = height;
				}
			}
		}

		NotifyPaintChanged();
	}

	public void FillAll( bool traversable, TreasureSurfaceMaterial material )
	{
		if ( !TryGetPaintArrays( out byte[] trav, out byte[] mats, out _, out _, out _ ) )
			return;

		byte t = traversable ? ( byte )1 : ( byte )0;
		byte m = ( byte )material;
		for ( int i = 0; i < trav.Length; i++ )
		{
			trav[ i ] = t;
			mats[ i ] = m;
		}

		NotifyPaintChanged();
	}

	public void FillAllHeights( float height )
	{
		if ( !TryGetPaintArrays( out _, out _, out float[] heights, out _, out _ ) )
			return;

		for ( int i = 0; i < heights.Length; i++ )
			heights[ i ] = height;

		NotifyPaintChanged();
	}

	/// <summary>
	/// Raycasts downward per cell using Height Bake settings and writes hit Y into height paint.
	/// Only hits whose normal is within <see cref="heightBakeMaxSlopeDegrees"/> of world up are kept.
	/// </summary>
	public int BakeHeightsFromRaycasts()
	{
		ResolveBakeRay( out float startY, out float distance );
		return BakeHeightsFromRaycasts(
			heightBakeMask,
			startY,
			distance,
			heightBakeHitOffset,
			heightBakeTraversableOnly,
			heightBakeMissBehavior,
			heightBakeTriggerInteraction );
	}

	public void ResolveBakeRay( out float startY, out float distance )
	{
		float maxY = definition != null ? definition.maxWorldY : baseHeight + 32f;
		switch ( heightBakeRayStartMode )
		{
			case HeightBakeRayStartMode.AboveBaseHeight:
				startY = baseHeight + heightBakeRayPad;
				break;
			case HeightBakeRayStartMode.Absolute:
				startY = heightBakeRayStartY + heightBakeRayPad;
				break;
			default:
				startY = Mathf.Max( maxY, baseHeight ) + heightBakeRayPad;
				break;
		}

		distance = Mathf.Max( 0.1f, heightBakeRayDistance );
	}

	public bool TryRaycastWalkableSurface( Ray ray, float distance, out RaycastHit hit )
	{
		return TryRaycastWalkableSurface( ray.origin, ray.direction, distance, heightBakeMask, heightBakeTriggerInteraction, out hit );
	}

	public bool TryRaycastWalkableSurface(
		Vector3 origin,
		Vector3 direction,
		float distance,
		LayerMask layerMask,
		QueryTriggerInteraction triggerInteraction,
		out RaycastHit hit )
	{
		hit = default;
		if ( _heightBakeHits == null || _heightBakeHits.Length != HeightBakeHitBufferSize )
			_heightBakeHits = new RaycastHit[ HeightBakeHitBufferSize ];

		int count = Physics.RaycastNonAlloc( origin, direction, _heightBakeHits, Mathf.Max( 0.1f, distance ), layerMask, triggerInteraction );
		int best = -1;
		float bestDistance = float.MaxValue;
		float maxSlope = Mathf.Clamp( heightBakeMaxSlopeDegrees, 0f, 89f );
		for ( int i = 0; i < count; i++ )
		{
			RaycastHit candidate = _heightBakeHits[ i ];
			if ( Vector3.Angle( candidate.normal, Vector3.up ) > maxSlope )
				continue;
			if ( candidate.distance >= bestDistance )
				continue;

			bestDistance = candidate.distance;
			best = i;
		}

		if ( best < 0 )
			return false;

		hit = _heightBakeHits[ best ];
		return true;
	}

	public int BakeHeightsFromRaycasts( LayerMask layerMask, float rayStartY, float rayDistance )
	{
		return BakeHeightsFromRaycasts(
			layerMask,
			rayStartY,
			rayDistance,
			heightBakeHitOffset,
			heightBakeTraversableOnly,
			heightBakeMissBehavior,
			heightBakeTriggerInteraction );
	}

	public int BakeHeightsFromRaycasts(
		LayerMask layerMask,
		float rayStartY,
		float rayDistance,
		float hitOffset,
		bool traversableOnly,
		HeightBakeMissBehavior missBehavior,
		QueryTriggerInteraction triggerInteraction )
	{
		if ( !TryGetPaintArrays( out byte[] trav, out _, out float[] heights, out int cellsX, out int cellsZ ) )
			return 0;

		int written = BakeHeightsFromRaycastsRegion(
			trav,
			heights,
			cellsX,
			cellsZ,
			0,
			0,
			cellsX - 1,
			cellsZ - 1,
			useRadius: false,
			radiusSq: 0f,
			localCenterX: 0f,
			localCenterZ: 0f,
			layerMask,
			rayStartY,
			rayDistance,
			hitOffset,
			traversableOnly,
			missBehavior,
			triggerInteraction );

		NotifyPaintChanged();
		return written;
	}

	/// <summary>
	/// Raycasts downward for cells inside a world-space disc using Height Bake settings.
	/// Same hit / miss rules as <see cref="BakeHeightsFromRaycasts"/>.
	/// </summary>
	public int PaintBrushBakeHeights( Vector3 worldCenter, float radiusMeters )
	{
		ResolveBakeRay( out float startY, out float distance );
		return PaintBrushBakeHeights(
			worldCenter,
			radiusMeters,
			heightBakeMask,
			startY,
			distance,
			heightBakeHitOffset,
			heightBakeTraversableOnly,
			heightBakeMissBehavior,
			heightBakeTriggerInteraction );
	}

	public int PaintBrushBakeHeights(
		Vector3 worldCenter,
		float radiusMeters,
		LayerMask layerMask,
		float rayStartY,
		float rayDistance,
		float hitOffset,
		bool traversableOnly,
		HeightBakeMissBehavior missBehavior,
		QueryTriggerInteraction triggerInteraction )
	{
		if ( !TryGetPaintArrays( out byte[] trav, out _, out float[] heights, out int cellsX, out int cellsZ ) )
			return 0;
		if ( radiusMeters <= 0f )
			return 0;

		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		float cell = CellSize;
		int cellRadius = Mathf.Max( 0, Mathf.CeilToInt( radiusMeters / cell ) );
		if ( !TryWorldToCell( worldCenter, out int cx, out int cz ) )
		{
			float localX = Mathf.Clamp( worldCenter.x - worldOrigin.x + halfX, 0f, worldSizeX - 0.001f );
			float localZ = Mathf.Clamp( worldCenter.z - worldOrigin.z + halfZ, 0f, worldSizeZ - 0.001f );
			cx = Mathf.FloorToInt( localX / cell );
			cz = Mathf.FloorToInt( localZ / cell );
		}

		float localCenterX = worldCenter.x - worldOrigin.x + halfX;
		float localCenterZ = worldCenter.z - worldOrigin.z + halfZ;
		float radiusSq = radiusMeters * radiusMeters;

		int written = BakeHeightsFromRaycastsRegion(
			trav,
			heights,
			cellsX,
			cellsZ,
			cx - cellRadius,
			cz - cellRadius,
			cx + cellRadius,
			cz + cellRadius,
			useRadius: true,
			radiusSq,
			localCenterX,
			localCenterZ,
			layerMask,
			rayStartY,
			rayDistance,
			hitOffset,
			traversableOnly,
			missBehavior,
			triggerInteraction );

		NotifyPaintChanged();
		return written;
	}

	int BakeHeightsFromRaycastsRegion(
		byte[] trav,
		float[] heights,
		int cellsX,
		int cellsZ,
		int minX,
		int minZ,
		int maxX,
		int maxZ,
		bool useRadius,
		float radiusSq,
		float localCenterX,
		float localCenterZ,
		LayerMask layerMask,
		float rayStartY,
		float rayDistance,
		float hitOffset,
		bool traversableOnly,
		HeightBakeMissBehavior missBehavior,
		QueryTriggerInteraction triggerInteraction )
	{
		int written = 0;
		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		float cell = CellSize;
		Vector3 down = Vector3.down;
		float dist = Mathf.Max( 0.1f, rayDistance );

		int z0 = Mathf.Max( 0, minZ );
		int z1 = Mathf.Min( cellsZ - 1, maxZ );
		int x0 = Mathf.Max( 0, minX );
		int x1 = Mathf.Min( cellsX - 1, maxX );

		for ( int z = z0; z <= z1; z++ )
		{
			float dzSq = 0f;
			if ( useRadius )
			{
				float dz = ( z + 0.5f ) * cell - localCenterZ;
				dzSq = dz * dz;
				if ( dzSq > radiusSq )
					continue;
			}

			for ( int x = x0; x <= x1; x++ )
			{
				if ( useRadius )
				{
					float dx = ( x + 0.5f ) * cell - localCenterX;
					if ( dx * dx + dzSq > radiusSq )
						continue;
				}

				int i = z * cellsX + x;
				if ( traversableOnly && trav[ i ] == 0 )
					continue;

				float wx = worldOrigin.x - halfX + ( x + 0.5f ) * cell;
				float wz = worldOrigin.z - halfZ + ( z + 0.5f ) * cell;
				Vector3 origin = new Vector3( wx, rayStartY, wz );
				if ( TryRaycastWalkableSurface( origin, down, dist, layerMask, triggerInteraction, out RaycastHit hit ) )
				{
					heights[ i ] = hit.point.y + hitOffset;
					written++;
				}
				else
				{
					// No walkable floor, ramp, or stair under this cell — block travel.
					trav[ i ] = 0;
					if ( missBehavior == HeightBakeMissBehavior.SetBaseHeight )
						heights[ i ] = baseHeight;
				}
			}
		}

		return written;
	}

	public void ApplyPaintToChunk( TreasureChunk chunk )
	{
		if ( chunk == null || !chunk.Loaded || chunk.PaintTraversable == null )
			return;

		if ( !TryGetPaintArrays( out byte[] trav, out byte[] mats, out float[] heights, out int cellsX, out int cellsZ ) )
			return;

		int res = chunk.Resolution;
		int baseCellX = chunk.Coord.X * cellsPerChunk;
		int baseCellZ = chunk.Coord.Z * cellsPerChunk;

		for ( int z = 0; z < res; z++ )
		{
			int worldCellZ = baseCellZ + z;
			for ( int x = 0; x < res; x++ )
			{
				int worldCellX = baseCellX + x;
				int chunkIndex = chunk.Index( x, z );

				if ( worldCellX < 0 || worldCellZ < 0 || worldCellX >= cellsX || worldCellZ >= cellsZ )
				{
					chunk.PaintTraversable[ chunkIndex ] = defaultNonTraversable ? ( byte )0 : ( byte )1;
					chunk.PaintMaterial[ chunkIndex ] = ( byte )TreasureSurfaceMaterial.Stone;
					chunk.BaseHeight[ chunkIndex ] = baseHeight;
					chunk.Height[ chunkIndex ] = baseHeight;
					chunk.SmoothedHeight[ chunkIndex ] = baseHeight;
					continue;
				}

				int paintIndex = worldCellZ * cellsX + worldCellX;
				chunk.PaintTraversable[ chunkIndex ] = trav[ paintIndex ];
				chunk.PaintMaterial[ chunkIndex ] = mats[ paintIndex ];
				float h = heights[ paintIndex ];
				chunk.BaseHeight[ chunkIndex ] = h;
				chunk.Height[ chunkIndex ] = h;
				chunk.SmoothedHeight[ chunkIndex ] = h;
			}
		}
	}

	void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color( 0.2f, 1f, 0.45f, 0.8f );
		Bounds b = WorldBounds;
		Gizmos.DrawWireCube( b.center, b.size );

		Gizmos.color = new Color( 0.35f, 0.8f, 1f, 0.25f );
		float y = baseHeight + 0.02f;
		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		int countX = ChunkCountX;
		int countZ = ChunkCountZ;
		for ( int x = 0; x <= countX; x++ )
		{
			float wx = worldOrigin.x - halfX + x * chunkSize;
			Gizmos.DrawLine(
				new Vector3( wx, y, worldOrigin.z - halfZ ),
				new Vector3( wx, y, worldOrigin.z - halfZ + worldSizeZ ) );
		}

		for ( int z = 0; z <= countZ; z++ )
		{
			float wz = worldOrigin.z - halfZ + z * chunkSize;
			Gizmos.DrawLine(
				new Vector3( worldOrigin.x - halfX, y, wz ),
				new Vector3( worldOrigin.x - halfX + worldSizeX, y, wz ) );
		}
	}
}

public enum HeightBakeRayStartMode
{
	Absolute = 0,
	AboveMaxWorldY = 1,
	AboveBaseHeight = 2
}

public enum HeightBakeMissBehavior
{
	/// <summary>On miss: mark non-traversable and set height to baseHeight.</summary>
	SetBaseHeight = 0,
	/// <summary>On miss: mark non-traversable and leave height unchanged.</summary>
	KeepExisting = 1
}
