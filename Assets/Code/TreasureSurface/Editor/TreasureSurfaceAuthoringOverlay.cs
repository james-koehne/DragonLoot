#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-only transparent green height overlay (full paint resolution).
/// Geometry is chunked and built across editor frames; drawing uses
/// SceneView + Graphics.DrawMeshNow so URP Scene view always shows it.
/// </summary>
static class TreasureSurfaceAuthoringOverlay
{
	static readonly Color OverlayColor = new Color( 0.15f, 1f, 0.3f, 0.55f );
	const float YBias = 0.05f;
	const int DefaultChunksPerFrame = 8;
	const string SessionKey = "TreasureSurface.ShowHeightOverlay";

	struct ChunkMesh
	{
		public Mesh Mesh;
		public int ChunkX;
		public int ChunkZ;
		public bool HasGeometry;
	}

	static Material s_Material;
	static readonly List<ChunkMesh> s_Chunks = new List<ChunkMesh>( 64 );
	static readonly Queue<int> s_BuildQueue = new Queue<int>( 64 );
	static readonly List<int> s_PriorityScratch = new List<int>( 64 );
	static readonly List<int> s_EmptyScratch = new List<int>( 64 );

	static int s_CachedAuthoringId;
	static int s_CachedRevision = -1;
	static int s_CachedChunkCountX;
	static int s_CachedChunkCountZ;
	static int s_CachedCellsPerChunk;
	static bool s_DeferRebuild;
	static bool s_IsVisible;
	static bool s_UpdateHooked;
	static bool s_SceneGuiHooked;
	static bool s_BuildIncomplete;

	static TreasureSurfaceAuthoring s_Authoring;
	static byte[] s_Trav;
	static float[] s_Heights;
	static int s_PaintX;
	static int s_PaintZ;
	static Vector3 s_Origin;
	static float s_HalfX;
	static float s_HalfZ;
	static float s_Cell;
	static int s_CellsPerChunk;
	static int s_ChunksPerFrame = DefaultChunksPerFrame;

	static readonly List<Vector3> s_Verts = new List<Vector3>( 8192 );
	static readonly List<int> s_Tris = new List<int>( 16384 );
	static readonly List<Color> s_Colors = new List<Color>( 8192 );

	public static bool IsVisible => s_IsVisible;

	public static void GetBuildStatus( out int traversableChunks, out int builtChunks, out int pendingChunks, out int paintTravCells )
	{
		traversableChunks = 0;
		builtChunks = 0;
		pendingChunks = s_BuildQueue.Count;
		paintTravCells = 0;

		if ( s_Trav != null )
		{
			for ( int i = 0; i < s_Trav.Length; i++ )
			{
				if ( s_Trav[ i ] != 0 )
					paintTravCells++;
			}
		}

		for ( int i = 0; i < s_Chunks.Count; i++ )
		{
			if ( s_Chunks[ i ].HasGeometry )
				builtChunks++;
		}

		traversableChunks = s_PriorityScratch.Count;
		if ( traversableChunks == 0 && s_Trav != null )
		{
			for ( int i = 0; i < s_Chunks.Count; i++ )
			{
				if ( ChunkHasTraversable( i ) )
					traversableChunks++;
			}
		}
	}

	public static void SetVisible( TreasureSurfaceAuthoring authoring, bool visible )
	{
		SessionState.SetBool( SessionKey, visible );
		if ( visible )
			Show( authoring );
		else
			Hide();
	}

	public static bool GetSessionVisible()
	{
		return SessionState.GetBool( SessionKey, false );
	}

	public static void Show( TreasureSurfaceAuthoring authoring )
	{
		if ( authoring == null )
		{
			Hide();
			return;
		}

		s_IsVisible = true;
		s_DeferRebuild = false;
		SessionState.SetBool( SessionKey, true );
		EnsureHooks();
		Sync( authoring, true );
	}

	public static void MaintainVisible( TreasureSurfaceAuthoring authoring )
	{
		if ( authoring == null || !s_IsVisible )
			return;

		EnsureHooks();
		Sync( authoring, false );
	}

	public static void Hide()
	{
		s_IsVisible = false;
		SessionState.SetBool( SessionKey, false );
		ClearBuildQueue();
		s_BuildIncomplete = false;
		DestroyMeshes();
		s_Authoring = null;
		s_Trav = null;
		s_Heights = null;
		RemoveHooks();
	}

	public static void Invalidate()
	{
		s_CachedRevision = -1;
		s_BuildIncomplete = true;
		ClearBuildQueue();
	}

	public static void SetDeferTextureRebuild( bool defer )
	{
		s_DeferRebuild = defer;
		if ( defer )
		{
			ClearBuildQueue();
			s_BuildIncomplete = true;
		}
	}

	static void EnsureHooks()
	{
		if ( !s_UpdateHooked )
		{
			EditorApplication.update += OnEditorUpdate;
			s_UpdateHooked = true;
		}

		if ( !s_SceneGuiHooked )
		{
			SceneView.duringSceneGui += OnDuringSceneGui;
			s_SceneGuiHooked = true;
		}
	}

	static void RemoveHooks()
	{
		if ( s_UpdateHooked )
		{
			EditorApplication.update -= OnEditorUpdate;
			s_UpdateHooked = false;
		}

		if ( s_SceneGuiHooked )
		{
			SceneView.duringSceneGui -= OnDuringSceneGui;
			s_SceneGuiHooked = false;
		}
	}

	static void OnEditorUpdate()
	{
		if ( !s_IsVisible || s_DeferRebuild || s_Authoring == null )
			return;

		if ( s_BuildQueue.Count <= 0 )
		{
			s_BuildIncomplete = false;
			return;
		}

		int budget = Mathf.Max( 1, s_ChunksPerFrame );
		while ( budget-- > 0 && s_BuildQueue.Count > 0 )
		{
			int index = s_BuildQueue.Dequeue();
			if ( index >= 0 && index < s_Chunks.Count )
				BuildChunkMesh( index );
		}

		if ( s_BuildQueue.Count == 0 )
			s_BuildIncomplete = false;

		SceneView.RepaintAll();
	}

	static void OnDuringSceneGui( SceneView sceneView )
	{
		if ( !s_IsVisible || s_Authoring == null )
			return;

		if ( Event.current.type != EventType.Repaint )
			return;

		EnsureMaterial();
		if ( s_Material == null )
			return;

		Matrix4x4 matrix = s_Authoring.transform.localToWorldMatrix;
		s_Material.SetPass( 0 );

		for ( int i = 0; i < s_Chunks.Count; i++ )
		{
			ChunkMesh chunk = s_Chunks[ i ];
			if ( !chunk.HasGeometry || chunk.Mesh == null )
				continue;

			Graphics.DrawMeshNow( chunk.Mesh, matrix );
		}
	}

	static void Sync( TreasureSurfaceAuthoring authoring, bool force )
	{
		ScheduleIfNeeded( authoring, force );
	}

	static void DestroyMeshes()
	{
		for ( int i = 0; i < s_Chunks.Count; i++ )
		{
			if ( s_Chunks[ i ].Mesh != null )
				Object.DestroyImmediate( s_Chunks[ i ].Mesh );
		}

		s_Chunks.Clear();
		s_CachedRevision = -1;

		if ( s_Material != null )
		{
			Object.DestroyImmediate( s_Material );
			s_Material = null;
		}
	}

	static void ClearBuildQueue()
	{
		s_BuildQueue.Clear();
	}

	static void ScheduleIfNeeded( TreasureSurfaceAuthoring authoring, bool force )
	{
		authoring.EnsurePaintBuffers();
		int chunkCountX = authoring.ChunkCountX;
		int chunkCountZ = authoring.ChunkCountZ;
		int cellsPerChunk = authoring.CellsPerChunk;
		if ( chunkCountX <= 0 || chunkCountZ <= 0 || cellsPerChunk <= 0 )
			return;

		int id = authoring.GetInstanceID();
		int revision = authoring.PaintRevision;

		bool layoutChanged = s_CachedAuthoringId != id
			|| s_CachedChunkCountX != chunkCountX
			|| s_CachedChunkCountZ != chunkCountZ
			|| s_CachedCellsPerChunk != cellsPerChunk
			|| s_Chunks.Count != chunkCountX * chunkCountZ;

		if ( !force
			&& s_DeferRebuild
			&& !layoutChanged
			&& s_CachedAuthoringId == id )
			return;

		// Same authoring + revision: do not requeue. Let OnEditorUpdate drain the queue.
		if ( !force
			&& !layoutChanged
			&& s_CachedAuthoringId == id
			&& s_CachedRevision == revision )
			return;

		CachePaint( authoring );
		EnsureChunkSlots( chunkCountX, chunkCountZ, cellsPerChunk );
		s_ChunksPerFrame = Mathf.Max( 1, authoring.OverlayChunksPerFrame );

		s_CachedAuthoringId = id;
		s_CachedRevision = revision;
		s_CachedChunkCountX = chunkCountX;
		s_CachedChunkCountZ = chunkCountZ;
		s_CachedCellsPerChunk = cellsPerChunk;

		QueueAllChunksPrioritized();
		s_BuildIncomplete = s_BuildQueue.Count > 0;
		EnsureHooks();
		OnEditorUpdate();
	}

	static void CachePaint( TreasureSurfaceAuthoring authoring )
	{
		s_Authoring = authoring;
		authoring.EditorGetPaintArrays( out s_Trav, out _, out s_Heights, out s_PaintX, out s_PaintZ );
		s_Origin = authoring.WorldOrigin;
		s_HalfX = authoring.WorldSizeX * 0.5f;
		s_HalfZ = authoring.WorldSizeZ * 0.5f;
		s_Cell = authoring.CellSize;
		s_CellsPerChunk = authoring.CellsPerChunk;
	}

	static void EnsureChunkSlots( int chunkCountX, int chunkCountZ, int cellsPerChunk )
	{
		int want = chunkCountX * chunkCountZ;
		if ( s_Chunks.Count == want
			&& s_CachedChunkCountX == chunkCountX
			&& s_CachedChunkCountZ == chunkCountZ
			&& s_CachedCellsPerChunk == cellsPerChunk )
			return;

		DestroyMeshes();
		ClearBuildQueue();

		s_Chunks.Capacity = Mathf.Max( s_Chunks.Capacity, want );
		for ( int cz = 0; cz < chunkCountZ; cz++ )
		{
			for ( int cx = 0; cx < chunkCountX; cx++ )
			{
				s_Chunks.Add( new ChunkMesh
				{
					Mesh = null,
					ChunkX = cx,
					ChunkZ = cz,
					HasGeometry = false
				} );
			}
		}
	}

	static void QueueAllChunksPrioritized()
	{
		ClearBuildQueue();
		s_PriorityScratch.Clear();
		s_EmptyScratch.Clear();

		for ( int i = 0; i < s_Chunks.Count; i++ )
		{
			if ( ChunkHasTraversable( i ) )
				s_PriorityScratch.Add( i );
			else
				s_EmptyScratch.Add( i );
		}

		for ( int i = 0; i < s_PriorityScratch.Count; i++ )
			s_BuildQueue.Enqueue( s_PriorityScratch[ i ] );
		for ( int i = 0; i < s_EmptyScratch.Count; i++ )
			s_BuildQueue.Enqueue( s_EmptyScratch[ i ] );
	}

	static bool ChunkHasTraversable( int index )
	{
		if ( s_Trav == null || index < 0 || index >= s_Chunks.Count )
			return false;

		ChunkMesh chunk = s_Chunks[ index ];
		int baseCellX = chunk.ChunkX * s_CellsPerChunk;
		int baseCellZ = chunk.ChunkZ * s_CellsPerChunk;
		int res = s_CellsPerChunk;

		for ( int lz = 0; lz < res; lz++ )
		{
			int worldZ = baseCellZ + lz;
			if ( worldZ < 0 || worldZ >= s_PaintZ )
				continue;

			int row = worldZ * s_PaintX;
			for ( int lx = 0; lx < res; lx++ )
			{
				int worldX = baseCellX + lx;
				if ( worldX < 0 || worldX >= s_PaintX )
					continue;
				if ( s_Trav[ row + worldX ] != 0 )
					return true;
			}
		}

		return false;
	}

	static void BuildChunkMesh( int index )
	{
		if ( s_Authoring == null || s_Trav == null || s_Heights == null )
			return;
		if ( index < 0 || index >= s_Chunks.Count )
			return;

		ChunkMesh chunk = s_Chunks[ index ];
		int baseCellX = chunk.ChunkX * s_CellsPerChunk;
		int baseCellZ = chunk.ChunkZ * s_CellsPerChunk;
		int res = s_CellsPerChunk;

		s_Verts.Clear();
		s_Tris.Clear();
		s_Colors.Clear();

		// World-space verts — DrawMeshNow applies authoring localToWorld, so convert to local.
		Matrix4x4 worldToLocal = s_Authoring.transform.worldToLocalMatrix;

		for ( int lz = 0; lz < res; lz++ )
		{
			int worldZ = baseCellZ + lz;
			if ( worldZ < 0 || worldZ >= s_PaintZ )
				continue;

			int row = worldZ * s_PaintX;
			for ( int lx = 0; lx < res; lx++ )
			{
				int worldX = baseCellX + lx;
				if ( worldX < 0 || worldX >= s_PaintX )
					continue;

				int i = row + worldX;
				if ( s_Trav[ i ] == 0 )
					continue;

				float y = s_Heights[ i ] + YBias;
				float minX = s_Origin.x - s_HalfX + worldX * s_Cell;
				float maxX = minX + s_Cell;
				float minZ = s_Origin.z - s_HalfZ + worldZ * s_Cell;
				float maxZ = minZ + s_Cell;

				int v = s_Verts.Count;
				s_Verts.Add( worldToLocal.MultiplyPoint3x4( new Vector3( minX, y, minZ ) ) );
				s_Verts.Add( worldToLocal.MultiplyPoint3x4( new Vector3( maxX, y, minZ ) ) );
				s_Verts.Add( worldToLocal.MultiplyPoint3x4( new Vector3( maxX, y, maxZ ) ) );
				s_Verts.Add( worldToLocal.MultiplyPoint3x4( new Vector3( minX, y, maxZ ) ) );
				s_Colors.Add( OverlayColor );
				s_Colors.Add( OverlayColor );
				s_Colors.Add( OverlayColor );
				s_Colors.Add( OverlayColor );
				s_Tris.Add( v );
				s_Tris.Add( v + 2 );
				s_Tris.Add( v + 1 );
				s_Tris.Add( v );
				s_Tris.Add( v + 3 );
				s_Tris.Add( v + 2 );
			}
		}

		if ( s_Verts.Count == 0 )
		{
			if ( chunk.Mesh != null )
			{
				Object.DestroyImmediate( chunk.Mesh );
				chunk.Mesh = null;
			}

			chunk.HasGeometry = false;
			s_Chunks[ index ] = chunk;
			return;
		}

		if ( chunk.Mesh == null )
		{
			chunk.Mesh = new Mesh
			{
				name = $"TreasureSurfaceOverlay_{chunk.ChunkX}_{chunk.ChunkZ}",
				hideFlags = HideFlags.HideAndDontSave
			};
		}
		else
			chunk.Mesh.Clear();

		if ( s_Verts.Count > 65000 )
			chunk.Mesh.indexFormat = IndexFormat.UInt32;
		else
			chunk.Mesh.indexFormat = IndexFormat.UInt16;

		chunk.Mesh.SetVertices( s_Verts );
		chunk.Mesh.SetColors( s_Colors );
		chunk.Mesh.SetTriangles( s_Tris, 0, true );
		chunk.Mesh.RecalculateNormals();
		chunk.Mesh.RecalculateBounds();
		chunk.HasGeometry = true;
		s_Chunks[ index ] = chunk;
	}

	static void EnsureMaterial()
	{
		if ( s_Material != null )
			return;

		Shader shader = Shader.Find( "Hidden/Internal-Colored" );
		if ( shader == null )
			shader = Shader.Find( "GUI/Text Shader" );
		if ( shader == null )
			shader = Shader.Find( "Unlit/Color" );

		s_Material = new Material( shader )
		{
			hideFlags = HideFlags.HideAndDontSave,
			name = "TreasureSurfaceHeightOverlayMat"
		};

		s_Material.color = OverlayColor;
		if ( s_Material.HasProperty( "_Color" ) )
			s_Material.SetColor( "_Color", OverlayColor );

		if ( s_Material.HasProperty( "_ZTest" ) )
			s_Material.SetInt( "_ZTest", ( int )CompareFunction.LessEqual );
		if ( s_Material.HasProperty( "_ZWrite" ) )
			s_Material.SetInt( "_ZWrite", 0 );
		if ( s_Material.HasProperty( "_Cull" ) )
			s_Material.SetInt( "_Cull", ( int )CullMode.Off );
		if ( s_Material.HasProperty( "_SrcBlend" ) )
			s_Material.SetInt( "_SrcBlend", ( int )BlendMode.SrcAlpha );
		if ( s_Material.HasProperty( "_DstBlend" ) )
			s_Material.SetInt( "_DstBlend", ( int )BlendMode.OneMinusSrcAlpha );

		s_Material.renderQueue = ( int )RenderQueue.Transparent;
	}
}
#endif
