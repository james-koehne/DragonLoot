using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Level paint grids for <see cref="TreasureSurfaceAuthoring"/>.
/// Paint bytes are NOT Unity-serialized on this ScriptableObject (Inspector-safe).
/// Editor: sidecar <c>.paintbin</c> next to this asset.
/// Player: optional <see cref="bakedPaint"/> TextAsset (.bytes) assigned for builds.
/// </summary>
[CreateAssetMenu(
	fileName = "TreasureSurfacePaint",
	menuName = "DragonLoot/Treasure Surface Paint",
	order = 21 )]
public class TreasureSurfacePaintAsset : ScriptableObject
{
	const int FileMagic = 0x42505354; // 'TSPB' little-endian
	const int FileVersion = 1;
	public const string SidecarExtension = ".paintbin";

	[SerializeField]
	int cellsX;

	[SerializeField]
	int cellsZ;

	[Tooltip( "Optional build-time paint blob (.bytes). Editor prefers the .paintbin sidecar." )]
	[SerializeField]
	TextAsset bakedPaint;

	/// <summary>Editor undo token only — indexes in-memory snapshots, not paint bytes.</summary>
	[SerializeField]
	[HideInInspector]
	int undoSnapshotId;

	[NonSerialized]
	byte[] _traversablePaint;

	[NonSerialized]
	byte[] _materialPaint;

	[NonSerialized]
	bool _loaded;

	[NonSerialized]
	bool _dirty;

	[NonSerialized]
	int _pendingUndoCaptureId;

#if UNITY_EDITOR
	[NonSerialized]
	bool _undoHooked;

	static readonly Dictionary<int, PaintSnapshot> UndoSnapshots = new Dictionary<int, PaintSnapshot>( 64 );
	static int s_NextUndoId = 1;

	struct PaintSnapshot
	{
		public int CellsX;
		public int CellsZ;
		public byte[] Trav;
		public byte[] Mat;
	}
#endif

	public int CellsX => cellsX;
	public int CellsZ => cellsZ;
	public byte[] TraversablePaint
	{
		get
		{
			EnsureLoaded();
			return _traversablePaint;
		}
	}

	public byte[] MaterialPaint
	{
		get
		{
			EnsureLoaded();
			return _materialPaint;
		}
	}

	public bool HasBuffers
	{
		get
		{
			EnsureLoaded();
			return _traversablePaint != null
				&& _materialPaint != null
				&& cellsX > 0
				&& cellsZ > 0
				&& _traversablePaint.Length == cellsX * cellsZ
				&& _materialPaint.Length == cellsX * cellsZ;
		}
	}

	public bool IsDirty => _dirty;

	void OnEnable()
	{
#if UNITY_EDITOR
		HookUndo();
#endif
		EnsureLoaded();
	}

	void OnDisable()
	{
#if UNITY_EDITOR
		UnhookUndo();
		if ( _dirty )
			EditorSaveToDisk();
#endif
	}

	/// <summary>
	/// Ensures buffers match the requested cell grid. Copies overlapping cells on resize.
	/// </summary>
	public void EnsureBuffers( int wantCellsX, int wantCellsZ, bool defaultNonTraversable )
	{
		EnsureLoaded();

		wantCellsX = Mathf.Max( 1, wantCellsX );
		wantCellsZ = Mathf.Max( 1, wantCellsZ );
		int count = wantCellsX * wantCellsZ;

		if ( _traversablePaint != null
			&& _materialPaint != null
			&& cellsX == wantCellsX
			&& cellsZ == wantCellsZ
			&& _traversablePaint.Length == count
			&& _materialPaint.Length == count )
			return;

		byte[] newTrav = new byte[ count ];
		byte[] newMat = new byte[ count ];
		byte fillTrav = defaultNonTraversable ? ( byte )0 : ( byte )1;

		for ( int i = 0; i < count; i++ )
		{
			newTrav[ i ] = fillTrav;
			newMat[ i ] = ( byte )TreasureSurfaceMaterial.Stone;
		}

		if ( _traversablePaint != null && _materialPaint != null && cellsX > 0 && cellsZ > 0 )
		{
			int copyX = Mathf.Min( cellsX, wantCellsX );
			int copyZ = Mathf.Min( cellsZ, wantCellsZ );
			for ( int z = 0; z < copyZ; z++ )
			{
				for ( int x = 0; x < copyX; x++ )
				{
					int oi = z * cellsX + x;
					int ni = z * wantCellsX + x;
					newTrav[ ni ] = _traversablePaint[ oi ];
					newMat[ ni ] = _materialPaint[ oi ];
				}
			}
		}

		_traversablePaint = newTrav;
		_materialPaint = newMat;
		cellsX = wantCellsX;
		cellsZ = wantCellsZ;
		_loaded = true;
		MarkDirty();
	}

	/// <summary>Replaces buffers with a copy of legacy scene-serialized paint.</summary>
	public void ImportLegacy( byte[] trav, byte[] mat, int legacyCellsX, int legacyCellsZ )
	{
		if ( trav == null || mat == null || legacyCellsX <= 0 || legacyCellsZ <= 0 )
			return;

		int count = legacyCellsX * legacyCellsZ;
		if ( trav.Length < count || mat.Length < count )
			return;

		cellsX = legacyCellsX;
		cellsZ = legacyCellsZ;
		_traversablePaint = new byte[ count ];
		_materialPaint = new byte[ count ];
		Array.Copy( trav, _traversablePaint, count );
		Array.Copy( mat, _materialPaint, count );
		_loaded = true;
		MarkDirty();
#if UNITY_EDITOR
		EditorSaveToDisk();
#endif
	}

	public void MarkDirty()
	{
		_dirty = true;
#if UNITY_EDITOR
		if ( _pendingUndoCaptureId != 0 )
		{
			UndoSnapshots[ _pendingUndoCaptureId ] = CaptureSnapshot();
			_pendingUndoCaptureId = 0;
		}

		EditorUtility.SetDirty( this );
#endif
	}

	void EnsureLoaded()
	{
		if ( _loaded )
			return;

#if UNITY_EDITOR
		if ( EditorLoadFromDisk() )
		{
			_loaded = true;
			return;
		}
#endif
		if ( TryLoadFromBakedTextAsset() )
		{
			_loaded = true;
			return;
		}

		_traversablePaint = null;
		_materialPaint = null;
		_loaded = true;
	}

	bool TryLoadFromBakedTextAsset()
	{
		if ( bakedPaint == null || bakedPaint.bytes == null || bakedPaint.bytes.Length < 16 )
			return false;

		if ( !TryReadPaintBytes( bakedPaint.bytes, out int x, out int z, out byte[] trav, out byte[] mat ) )
			return false;

		cellsX = x;
		cellsZ = z;
		_traversablePaint = trav;
		_materialPaint = mat;
		_dirty = false;
		return true;
	}

#if UNITY_EDITOR
	public string EditorSidecarAssetPath()
	{
		string assetPath = AssetDatabase.GetAssetPath( this );
		if ( string.IsNullOrEmpty( assetPath ) )
			return null;

		return Path.ChangeExtension( assetPath, null ) + SidecarExtension;
	}

	public string EditorSidecarAbsolutePath()
	{
		string assetPath = EditorSidecarAssetPath();
		if ( string.IsNullOrEmpty( assetPath ) )
			return null;

		return Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), assetPath ) );
	}

	public bool EditorSaveToDisk()
	{
		EnsureLoaded();

		if ( _traversablePaint == null || _materialPaint == null || cellsX <= 0 || cellsZ <= 0 )
			return false;

		string abs = EditorSidecarAbsolutePath();
		string assetPath = EditorSidecarAssetPath();
		if ( string.IsNullOrEmpty( abs ) || string.IsNullOrEmpty( assetPath ) )
			return false;

		string folder = Path.GetDirectoryName( abs );
		if ( !string.IsNullOrEmpty( folder ) && !Directory.Exists( folder ) )
			Directory.CreateDirectory( folder );

		WritePaintFile( abs, cellsX, cellsZ, _traversablePaint, _materialPaint );
		_dirty = false;

		AssetDatabase.ImportAsset( assetPath, ImportAssetOptions.ForceUpdate );
		EditorUtility.SetDirty( this );
		return true;
	}

	public bool EditorReloadFromDisk()
	{
		_loaded = false;
		_traversablePaint = null;
		_materialPaint = null;
		_dirty = false;
		EnsureLoaded();
		return HasBuffers;
	}

	bool EditorLoadFromDisk()
	{
		string abs = EditorSidecarAbsolutePath();
		if ( string.IsNullOrEmpty( abs ) || !File.Exists( abs ) )
			return false;

		if ( !TryReadPaintFile( abs, out int x, out int z, out byte[] trav, out byte[] mat ) )
			return false;

		cellsX = x;
		cellsZ = z;
		_traversablePaint = trav;
		_materialPaint = mat;
		_dirty = false;
		return true;
	}

	public void EditorPushUndo( string undoName )
	{
		EnsureLoaded();
		HookUndo();

		// Refresh snapshot for the current token (pre-edit state).
		if ( undoSnapshotId == 0 )
			undoSnapshotId = s_NextUndoId++;
		UndoSnapshots[ undoSnapshotId ] = CaptureSnapshot();

		Undo.RecordObject( this, undoName );

		// New token for post-edit state; MarkDirty captures buffers after the stroke.
		int newId = s_NextUndoId++;
		undoSnapshotId = newId;
		_pendingUndoCaptureId = newId;
		EditorUtility.SetDirty( this );
	}

	/// <summary>
	/// Writes sidecar and a matching .bytes TextAsset for player builds (optional).
	/// </summary>
	public void EditorBakeTextAsset()
	{
		if ( !EditorSaveToDisk() )
			return;

		string sidecar = EditorSidecarAssetPath();
		string bytesPath = Path.ChangeExtension( sidecar, ".bytes" );
		File.Copy( EditorSidecarAbsolutePath(), Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), bytesPath ) ), true );
		AssetDatabase.ImportAsset( bytesPath, ImportAssetOptions.ForceUpdate );
		bakedPaint = AssetDatabase.LoadAssetAtPath<TextAsset>( bytesPath );
		EditorUtility.SetDirty( this );
	}

	void HookUndo()
	{
		if ( _undoHooked )
			return;

		Undo.undoRedoPerformed += OnUndoRedoPerformed;
		_undoHooked = true;
	}

	void UnhookUndo()
	{
		if ( !_undoHooked )
			return;

		Undo.undoRedoPerformed -= OnUndoRedoPerformed;
		_undoHooked = false;
	}

	void OnUndoRedoPerformed()
	{
		if ( UndoSnapshots.TryGetValue( undoSnapshotId, out PaintSnapshot snap ) )
			ApplySnapshot( snap );
	}

	PaintSnapshot CaptureSnapshot()
	{
		PaintSnapshot snap = new PaintSnapshot
		{
			CellsX = cellsX,
			CellsZ = cellsZ,
			Trav = null,
			Mat = null
		};

		if ( _traversablePaint != null )
		{
			snap.Trav = new byte[ _traversablePaint.Length ];
			Array.Copy( _traversablePaint, snap.Trav, _traversablePaint.Length );
		}

		if ( _materialPaint != null )
		{
			snap.Mat = new byte[ _materialPaint.Length ];
			Array.Copy( _materialPaint, snap.Mat, _materialPaint.Length );
		}

		return snap;
	}

	void ApplySnapshot( PaintSnapshot snap )
	{
		cellsX = snap.CellsX;
		cellsZ = snap.CellsZ;
		_traversablePaint = snap.Trav;
		_materialPaint = snap.Mat;
		_loaded = true;
		_dirty = true;
	}
#endif

	static void WritePaintFile( string absolutePath, int x, int z, byte[] trav, byte[] mat )
	{
		int count = x * z;
		using ( FileStream fs = File.Create( absolutePath ) )
		using ( BinaryWriter bw = new BinaryWriter( fs ) )
		{
			bw.Write( FileMagic );
			bw.Write( FileVersion );
			bw.Write( x );
			bw.Write( z );
			bw.Write( trav, 0, count );
			bw.Write( mat, 0, count );
		}
	}

	static bool TryReadPaintFile(
		string absolutePath,
		out int x,
		out int z,
		out byte[] trav,
		out byte[] mat )
	{
		x = 0;
		z = 0;
		trav = null;
		mat = null;

		try
		{
			byte[] data = File.ReadAllBytes( absolutePath );
			return TryReadPaintBytes( data, out x, out z, out trav, out mat );
		}
		catch ( Exception )
		{
			return false;
		}
	}

	static bool TryReadPaintBytes(
		byte[] data,
		out int x,
		out int z,
		out byte[] trav,
		out byte[] mat )
	{
		x = 0;
		z = 0;
		trav = null;
		mat = null;

		if ( data == null || data.Length < 16 )
			return false;

		using ( MemoryStream ms = new MemoryStream( data, writable: false ) )
		using ( BinaryReader br = new BinaryReader( ms ) )
		{
			if ( br.ReadInt32() != FileMagic )
				return false;
			int version = br.ReadInt32();
			if ( version != FileVersion )
				return false;

			x = br.ReadInt32();
			z = br.ReadInt32();
			if ( x <= 0 || z <= 0 )
				return false;

			int count = x * z;
			if ( data.Length < 16 + count * 2 )
				return false;

			trav = br.ReadBytes( count );
			mat = br.ReadBytes( count );
			return trav.Length == count && mat.Length == count;
		}
	}
}
