using UnityEngine;

/// <summary>
/// Level-authored layout + cell paint for the treasure surface.
/// Place in the Level scene; edit bounds and paint in the Scene view without Play Mode.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class TreasureSurfaceAuthoring : MonoBehaviour
{
	static TreasureSurfaceAuthoring _instance;

	[Header( "Definition (tuning)" )]
	[SerializeField]
	TreasureSurfaceDefinition definition;

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

	[SerializeField]
	[HideInInspector]
	byte[] traversablePaint;

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

	public static TreasureSurfaceAuthoring Instance => _instance;

	public TreasureSurfaceDefinition Definition => definition;
	public Vector3 WorldOrigin => worldOrigin;
	public float WorldSizeX => worldSizeX;
	public float WorldSizeZ => worldSizeZ;
	public float ChunkSize => chunkSize;
	public int CellsPerChunk => cellsPerChunk;
	public float BaseHeight => baseHeight;
	public bool DefaultNonTraversable => defaultNonTraversable;
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

	public void EnsurePaintBuffers()
	{
		int cellsX = TotalCellsX;
		int cellsZ = TotalCellsZ;
		int count = cellsX * cellsZ;

		if ( traversablePaint != null
			&& materialPaint != null
			&& paintCellsX == cellsX
			&& paintCellsZ == cellsZ
			&& traversablePaint.Length == count
			&& materialPaint.Length == count )
			return;

		byte[] newTrav = new byte[ count ];
		byte[] newMat = new byte[ count ];
		byte fillTrav = defaultNonTraversable ? ( byte )0 : ( byte )1;

		for ( int i = 0; i < count; i++ )
		{
			newTrav[ i ] = fillTrav;
			newMat[ i ] = ( byte )TreasureSurfaceMaterial.Stone;
		}

		// Copy overlapping cell indices when resolution changes (top-left aligned in grid space).
		if ( traversablePaint != null && materialPaint != null && paintCellsX > 0 && paintCellsZ > 0 )
		{
			int copyX = Mathf.Min( paintCellsX, cellsX );
			int copyZ = Mathf.Min( paintCellsZ, cellsZ );
			for ( int z = 0; z < copyZ; z++ )
			{
				for ( int x = 0; x < copyX; x++ )
				{
					int oi = z * paintCellsX + x;
					int ni = z * cellsX + x;
					newTrav[ ni ] = traversablePaint[ oi ];
					newMat[ ni ] = materialPaint[ oi ];
				}
			}
		}

		traversablePaint = newTrav;
		materialPaint = newMat;
		paintCellsX = cellsX;
		paintCellsZ = cellsZ;
		NotifyPaintChanged();
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
	}

#if UNITY_EDITOR
	public void EditorGetPaintArrays( out byte[] traversable, out byte[] materials, out int cellsX, out int cellsZ )
	{
		EnsurePaintBuffers();
		traversable = traversablePaint;
		materials = materialPaint;
		cellsX = paintCellsX;
		cellsZ = paintCellsZ;
	}
#endif

	public Vector3 CellCenterWorld( int cellX, int cellZ )
	{
		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;
		float cell = CellSize;
		float wx = worldOrigin.x - halfX + ( cellX + 0.5f ) * cell;
		float wz = worldOrigin.z - halfZ + ( cellZ + 0.5f ) * cell;
		return new Vector3( wx, baseHeight, wz );
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

	public bool TryGetPaint( int cellX, int cellZ, out bool traversable, out TreasureSurfaceMaterial material )
	{
		traversable = !defaultNonTraversable;
		material = TreasureSurfaceMaterial.Stone;
		EnsurePaintBuffers();
		if ( cellX < 0 || cellZ < 0 || cellX >= paintCellsX || cellZ >= paintCellsZ )
			return false;

		int i = cellZ * paintCellsX + cellX;
		traversable = traversablePaint[ i ] != 0;
		material = ( TreasureSurfaceMaterial )materialPaint[ i ];
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
		EnsurePaintBuffers();

		if ( chunkX < 0 || chunkZ < 0 || chunkX >= ChunkCountX || chunkZ >= ChunkCountZ )
			return false;

		int baseX = chunkX * cellsPerChunk;
		int baseZ = chunkZ * cellsPerChunk;
		int blocked = 0;
		int trav = 0;
		byte firstMat = 0;
		bool hasMat = false;

		for ( int z = 0; z < cellsPerChunk; z++ )
		{
			int worldZ = baseZ + z;
			if ( worldZ >= paintCellsZ )
				break;

			for ( int x = 0; x < cellsPerChunk; x++ )
			{
				int worldX = baseX + x;
				if ( worldX >= paintCellsX )
					break;

				int i = worldZ * paintCellsX + worldX;
				if ( traversablePaint[ i ] != 0 )
				{
					trav++;
					if ( !hasMat )
					{
						firstMat = materialPaint[ i ];
						hasMat = true;
					}
				}
				else
				{
					blocked++;
				}
			}
		}

		anyTraversable = trav > 0;
		fullyTraversable = trav > 0 && blocked == 0;
		if ( hasMat )
			material = ( TreasureSurfaceMaterial )firstMat;

		// Fully traversable with mixed materials still uses one square (first material tint).
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
		EnsurePaintBuffers();
		if ( cellX < 0 || cellZ < 0 || cellX >= paintCellsX || cellZ >= paintCellsZ )
			return;

		int i = cellZ * paintCellsX + cellX;
		traversablePaint[ i ] = traversable ? ( byte )1 : ( byte )0;
		materialPaint[ i ] = ( byte )material;
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
		EnsurePaintBuffers();
		if ( radiusMeters <= 0f )
			return;

		float halfX = worldSizeX * 0.5f;
		float halfZ = worldSizeZ * 0.5f;

		float cell = CellSize;
		int cellRadius = Mathf.Max( 0, Mathf.CeilToInt( radiusMeters / cell ) );
		if ( !TryWorldToCell( worldCenter, out int cx, out int cz ) )
		{
			// Still allow painting near the edge by clamping center into bounds.
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
				if ( x < 0 || z < 0 || x >= paintCellsX || z >= paintCellsZ )
					continue;

				float dx = ( x + 0.5f ) * cell - localCenterX;
				if ( dx * dx + dzSq > radiusSq )
					continue;

				int i = z * paintCellsX + x;
				if ( paintTraversable )
				{
					if ( traversablePaint[ i ] != travByte )
						traversablePaint[ i ] = travByte;
				}

				if ( paintMaterial )
				{
					if ( materialPaint[ i ] != matByte )
						materialPaint[ i ] = matByte;
				}
			}
		}

		NotifyPaintChanged();
	}

	public void FillAll( bool traversable, TreasureSurfaceMaterial material )
	{
		EnsurePaintBuffers();
		byte t = traversable ? ( byte )1 : ( byte )0;
		byte m = ( byte )material;
		for ( int i = 0; i < traversablePaint.Length; i++ )
		{
			traversablePaint[ i ] = t;
			materialPaint[ i ] = m;
		}

		NotifyPaintChanged();
	}

	public void ApplyPaintToChunk( TreasureChunk chunk )
	{
		if ( chunk == null || !chunk.Loaded || chunk.PaintTraversable == null )
			return;

		EnsurePaintBuffers();
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

				if ( worldCellX < 0 || worldCellZ < 0 || worldCellX >= paintCellsX || worldCellZ >= paintCellsZ )
				{
					chunk.PaintTraversable[ chunkIndex ] = defaultNonTraversable ? ( byte )0 : ( byte )1;
					chunk.PaintMaterial[ chunkIndex ] = ( byte )TreasureSurfaceMaterial.Stone;
					continue;
				}

				int paintIndex = worldCellZ * paintCellsX + worldCellX;
				chunk.PaintTraversable[ chunkIndex ] = traversablePaint[ paintIndex ];
				chunk.PaintMaterial[ chunkIndex ] = materialPaint[ paintIndex ];
			}
		}
	}

	void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color( 0.2f, 1f, 0.45f, 0.8f );
		Bounds b = WorldBounds;
		Gizmos.DrawWireCube( b.center, b.size );

		// Chunk grid only when selected — keep line count low.
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
