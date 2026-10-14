#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.SceneManagement;

/// <summary>
/// Greybox tool: greyscale heightmap → editable ProBuilder mesh with height curve remapping.
/// Menu: DragonLoot → Tools → Heightmap To ProBuilder
/// </summary>
public sealed class HeightmapToProBuilderWindow : EditorWindow
{
	const string PrefPrefix = "DragonLoot.HeightmapToProBuilder.";
	const int MaxResolution = 512;

	HeightmapProBuilderSettings _settings = new HeightmapProBuilderSettings();
	Vector2 _scroll;
	bool _foldSource = true;
	bool _foldHeight = true;
	bool _foldFootprint = true;
	bool _foldOutput = true;

	[MenuItem( DragonLootMenus.ToolsHeightmapToProBuilder )]
	public static void Open()
	{
		HeightmapToProBuilderWindow window = GetWindow<HeightmapToProBuilderWindow>( "Heightmap → ProBuilder" );
		window.minSize = new Vector2( 380f, 480f );
		window.Show();
	}

	void OnEnable()
	{
		if ( _settings == null )
			_settings = new HeightmapProBuilderSettings();
		LoadPrefs( _settings );
	}

	void OnDisable()
	{
		if ( _settings != null )
			SavePrefs( _settings );
	}

	void OnGUI()
	{
		if ( _settings == null )
			OnEnable();

		_scroll = EditorGUILayout.BeginScrollView( _scroll );

		EditorGUILayout.LabelField( "Heightmap → ProBuilder", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Bake a greyscale heightmap into an editable ProBuilder mesh for greybox vibe-testing. "
			+ "Create once, tweak settings, then Rebuild Selected.",
			MessageType.Info );

		DrawSource();
		DrawHeight();
		DrawFootprint();
		DrawOutput();

		EditorGUILayout.Space( 10f );
		using ( new EditorGUI.DisabledScope( _settings.heightmap == null ) )
		{
			if ( GUILayout.Button( "Create In Scene", GUILayout.Height( 28f ) ) )
				CreateInScene();
		}

		using ( new EditorGUI.DisabledScope( !CanRebuildSelection() ) )
		{
			if ( GUILayout.Button( "Rebuild Selected", GUILayout.Height( 24f ) ) )
				RebuildSelected();
		}

		using ( new EditorGUI.DisabledScope( !TryGetSelectedAuthoring( out _ ) ) )
		{
			if ( GUILayout.Button( "Load From Selected" ) )
				LoadFromSelected();
		}

		EditorGUILayout.EndScrollView();
	}

	void DrawSource()
	{
		_foldSource = EditorGUILayout.BeginFoldoutHeaderGroup( _foldSource, "Source" );
		if ( _foldSource )
		{
			_settings.heightmap = ( Texture2D )EditorGUILayout.ObjectField(
				"Heightmap",
				_settings.heightmap,
				typeof( Texture2D ),
				false );
			_settings.sampleChannel = ( HeightmapProBuilderSettings.SampleChannel )EditorGUILayout.EnumPopup(
				"Sample Channel",
				_settings.sampleChannel );
			_settings.invertSample = EditorGUILayout.Toggle( "Invert", _settings.invertSample );
			_settings.flipU = EditorGUILayout.Toggle( "Flip U", _settings.flipU );
			_settings.flipV = EditorGUILayout.Toggle( "Flip V", _settings.flipV );

			if ( _settings.heightmap != null )
			{
				Rect preview = GUILayoutUtility.GetRect( 0f, 128f, GUILayout.ExpandWidth( true ) );
				EditorGUI.DrawPreviewTexture( preview, _settings.heightmap, null, ScaleMode.ScaleToFit );
				EditorGUILayout.LabelField(
					$"Texture { _settings.heightmap.width }×{ _settings.heightmap.height }",
					EditorStyles.miniLabel );
			}
		}
		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void DrawHeight()
	{
		_foldHeight = EditorGUILayout.BeginFoldoutHeaderGroup( _foldHeight, "Height Mapping" );
		if ( _foldHeight )
		{
			_settings.heightCurve = EditorGUILayout.CurveField(
				"Height Curve",
				_settings.heightCurve,
				Color.green,
				new Rect( 0f, 0f, 1f, 1f ) );
			_settings.blackHeight = EditorGUILayout.FloatField( "Black Height (Y)", _settings.blackHeight );
			_settings.whiteHeight = EditorGUILayout.FloatField( "White Height (Y)", _settings.whiteHeight );
			_settings.yOffset = EditorGUILayout.FloatField( "Y Offset", _settings.yOffset );
			EditorGUILayout.HelpBox(
				"y = Lerp(black, white, curve.Evaluate(sample)) + yOffset",
				MessageType.None );
		}
		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void DrawFootprint()
	{
		_foldFootprint = EditorGUILayout.BeginFoldoutHeaderGroup( _foldFootprint, "Footprint / Topology" );
		if ( _foldFootprint )
		{
			_settings.worldSizeX = EditorGUILayout.FloatField( "World Size X", _settings.worldSizeX );
			_settings.worldSizeZ = EditorGUILayout.FloatField( "World Size Z", _settings.worldSizeZ );
			_settings.resolutionX = EditorGUILayout.IntSlider( "Resolution X", _settings.resolutionX, 2, MaxResolution );
			_settings.resolutionZ = EditorGUILayout.IntSlider( "Resolution Z", _settings.resolutionZ, 2, MaxResolution );
			_settings.pivot = ( HeightmapProBuilderSettings.PivotMode )EditorGUILayout.EnumPopup(
				"Pivot",
				_settings.pivot );

			using ( new EditorGUI.DisabledScope( _settings.heightmap == null ) )
			{
				if ( GUILayout.Button( "Match Texture (clamped)" ) )
				{
					_settings.resolutionX = Mathf.Clamp( _settings.heightmap.width, 2, MaxResolution );
					_settings.resolutionZ = Mathf.Clamp( _settings.heightmap.height, 2, MaxResolution );
				}
			}

			int quads = ( _settings.resolutionX - 1 ) * ( _settings.resolutionZ - 1 );
			int verts = _settings.resolutionX * _settings.resolutionZ;
			EditorGUILayout.LabelField( $"~{ verts } verts, { quads } quads", EditorStyles.miniLabel );
		}
		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void DrawOutput()
	{
		_foldOutput = EditorGUILayout.BeginFoldoutHeaderGroup( _foldOutput, "Output" );
		if ( _foldOutput )
		{
			_settings.objectName = EditorGUILayout.TextField( "Object Name", _settings.objectName );
			_settings.material = ( Material )EditorGUILayout.ObjectField(
				"Material",
				_settings.material,
				typeof( Material ),
				false );
			_settings.addMeshCollider = EditorGUILayout.Toggle( "Mesh Collider", _settings.addMeshCollider );
			_settings.uvMode = ( HeightmapProBuilderSettings.UvMode )EditorGUILayout.EnumPopup(
				"UV Mode",
				_settings.uvMode );
			using ( new EditorGUI.DisabledScope( _settings.uvMode != HeightmapProBuilderSettings.UvMode.WorldMeters ) )
			{
				_settings.uvWorldTiling = EditorGUILayout.FloatField( "UV World Tiling", _settings.uvWorldTiling );
			}
			_settings.cullBelowThreshold = EditorGUILayout.Toggle(
				"Cull Quads Below Threshold",
				_settings.cullBelowThreshold );
			using ( new EditorGUI.DisabledScope( !_settings.cullBelowThreshold ) )
			{
				_settings.cullThresholdY = EditorGUILayout.FloatField( "Cull Threshold Y", _settings.cullThresholdY );
			}
		}
		EditorGUILayout.EndFoldoutHeaderGroup();
	}

	void CreateInScene()
	{
		_settings.ClampResolutions();
		if ( _settings.heightmap == null )
		{
			EditorUtility.DisplayDialog( "Heightmap → ProBuilder", "Assign a heightmap texture.", "OK" );
			return;
		}

		if ( !TryBuildGeometry( _settings, out Vector3[] positions, out Face[] faces, out Vector2[] uvs ) )
			return;

		ProBuilderMesh pb = ProBuilderMesh.Create( positions, faces );
		GameObject go = pb.gameObject;
		go.name = string.IsNullOrEmpty( _settings.objectName ) ? "Heightmap ProBuilder" : _settings.objectName;

		ApplyUVs( pb, faces, uvs );
		ApplyMaterial( pb, _settings.material );
		ApplyCollider( go, _settings.addMeshCollider );

		HeightmapProBuilderAuthoring authoring = go.AddComponent<HeightmapProBuilderAuthoring>();
		if ( authoring.settings == null )
			authoring.settings = new HeightmapProBuilderSettings();
		authoring.settings.CopyFrom( _settings );

		PlaceInActiveScene( go );
		Undo.RegisterCreatedObjectUndo( go, "Create Heightmap ProBuilder" );
		Selection.activeGameObject = go;
		EditorGUIUtility.PingObject( go );
		SavePrefs( _settings );
	}

	void RebuildSelected()
	{
		if ( !TryGetSelectedAuthoring( out HeightmapProBuilderAuthoring authoring ) )
			return;

		ProBuilderMesh pb = authoring.GetComponent<ProBuilderMesh>();
		if ( pb == null )
		{
			EditorUtility.DisplayDialog(
				"Heightmap → ProBuilder",
				"Selected object has HeightmapProBuilderAuthoring but no ProBuilderMesh.",
				"OK" );
			return;
		}

		_settings.ClampResolutions();
		if ( _settings.heightmap == null )
		{
			EditorUtility.DisplayDialog( "Heightmap → ProBuilder", "Assign a heightmap texture in the window.", "OK" );
			return;
		}

		if ( !TryBuildGeometry( _settings, out Vector3[] positions, out Face[] faces, out Vector2[] uvs ) )
			return;

		Undo.RecordObject( pb, "Rebuild Heightmap ProBuilder" );
		Undo.RecordObject( authoring, "Rebuild Heightmap ProBuilder" );

		if ( authoring.settings == null )
			authoring.settings = new HeightmapProBuilderSettings();
		authoring.settings.CopyFrom( _settings );
		string keepName = _settings.objectName;
		if ( !string.IsNullOrEmpty( keepName ) )
			pb.gameObject.name = keepName;

		pb.RebuildWithPositionsAndFaces( positions, faces );
		ApplyUVs( pb, faces, uvs );
		ApplyMaterial( pb, _settings.material );
		ApplyCollider( pb.gameObject, _settings.addMeshCollider );

		EditorUtility.SetDirty( pb );
		EditorUtility.SetDirty( authoring );
		if ( !EditorApplication.isPlaying )
			EditorSceneManager.MarkSceneDirty( pb.gameObject.scene );

		SavePrefs( _settings );
	}

	void LoadFromSelected()
	{
		if ( !TryGetSelectedAuthoring( out HeightmapProBuilderAuthoring authoring ) )
			return;

		if ( authoring.settings != null )
			_settings.CopyFrom( authoring.settings );
		Repaint();
	}

	static bool TryGetSelectedAuthoring( out HeightmapProBuilderAuthoring authoring )
	{
		authoring = null;
		GameObject go = Selection.activeGameObject;
		if ( go == null )
			return false;
		return go.TryGetComponent( out authoring );
	}

	static bool CanRebuildSelection()
	{
		if ( !TryGetSelectedAuthoring( out HeightmapProBuilderAuthoring authoring ) )
			return false;
		return authoring.GetComponent<ProBuilderMesh>() != null;
	}

	static void PlaceInActiveScene( GameObject go )
	{
		Scene scene = SceneManager.GetActiveScene();
		if ( scene.IsValid() )
			SceneManager.MoveGameObjectToScene( go, scene );

		SceneView view = SceneView.lastActiveSceneView;
		if ( view != null && view.camera != null )
		{
			Ray ray = view.camera.ViewportPointToRay( new Vector3( 0.5f, 0.5f, 0f ) );
			Vector3 pos = ray.origin + ray.direction * 10f;
			if ( Physics.Raycast( ray, out RaycastHit hit, 500f ) )
				pos = hit.point;
			go.transform.position = pos;
		}
	}

	static void ApplyMaterial( ProBuilderMesh pb, Material material )
	{
		if ( pb == null || material == null )
			return;
		Renderer renderer = pb.GetComponent<MeshRenderer>();
		if ( renderer != null )
			renderer.sharedMaterial = material;
	}

	static void ApplyCollider( GameObject go, bool addCollider )
	{
		MeshCollider collider = go.GetComponent<MeshCollider>();
		if ( addCollider )
		{
			if ( collider == null )
				collider = Undo.AddComponent<MeshCollider>( go );
			ProBuilderMesh pb = go.GetComponent<ProBuilderMesh>();
			if ( pb != null )
				pb.Refresh( RefreshMask.Collisions );
			else if ( collider != null )
			{
				MeshFilter filter = go.GetComponent<MeshFilter>();
				if ( filter != null )
					collider.sharedMesh = filter.sharedMesh;
			}
		}
		else if ( collider != null )
		{
			Undo.DestroyObjectImmediate( collider );
		}
	}

	static void ApplyUVs( ProBuilderMesh pb, Face[] faces, Vector2[] uvs )
	{
		if ( pb == null || uvs == null || faces == null )
			return;

		for ( int i = 0; i < faces.Length; i++ )
		{
			if ( faces[ i ] != null )
				faces[ i ].manualUV = true;
		}

		pb.textures = uvs;
		pb.ToMesh();
		pb.Refresh();
	}

	static bool TryBuildGeometry(
		HeightmapProBuilderSettings settings,
		out Vector3[] positions,
		out Face[] faces,
		out Vector2[] uvs )
	{
		positions = null;
		faces = null;
		uvs = null;

		settings.ClampResolutions();
		int resX = settings.resolutionX;
		int resZ = settings.resolutionZ;
		int vertCount = resX * resZ;

		Texture2D readable = null;
		try
		{
			EditorUtility.DisplayProgressBar( "Heightmap → ProBuilder", "Reading heightmap…", 0.05f );
			readable = BlitToReadable( settings.heightmap );
			if ( readable == null )
			{
				EditorUtility.DisplayDialog( "Heightmap → ProBuilder", "Failed to read heightmap pixels.", "OK" );
				return false;
			}

			EditorUtility.DisplayProgressBar( "Heightmap → ProBuilder", "Sampling heights…", 0.2f );
			positions = new Vector3[ vertCount ];
			uvs = new Vector2[ vertCount ];

			float sizeX = settings.worldSizeX;
			float sizeZ = settings.worldSizeZ;
			float originX = settings.pivot == HeightmapProBuilderSettings.PivotMode.Center ? -sizeX * 0.5f : 0f;
			float originZ = settings.pivot == HeightmapProBuilderSettings.PivotMode.Center ? -sizeZ * 0.5f : 0f;
			float stepX = resX > 1 ? sizeX / ( resX - 1 ) : 0f;
			float stepZ = resZ > 1 ? sizeZ / ( resZ - 1 ) : 0f;
			float uvTiling = Mathf.Max( 0.0001f, settings.uvWorldTiling );

			AnimationCurve curve = settings.heightCurve != null
				? settings.heightCurve
				: AnimationCurve.Linear( 0f, 0f, 1f, 1f );

			for ( int z = 0; z < resZ; z++ )
			{
				if ( ( z & 15 ) == 0 )
				{
					float t = 0.2f + 0.5f * ( z / ( float )Mathf.Max( 1, resZ - 1 ) );
					if ( EditorUtility.DisplayCancelableProgressBar( "Heightmap → ProBuilder", "Sampling heights…", t ) )
						return false;
				}

				float vNorm = resZ > 1 ? z / ( float )( resZ - 1 ) : 0f;
				float sampleV = settings.flipV ? 1f - vNorm : vNorm;

				for ( int x = 0; x < resX; x++ )
				{
					float uNorm = resX > 1 ? x / ( float )( resX - 1 ) : 0f;
					float sampleU = settings.flipU ? 1f - uNorm : uNorm;

					float sample = SampleChannel( readable.GetPixelBilinear( sampleU, sampleV ), settings.sampleChannel );
					if ( settings.invertSample )
						sample = 1f - sample;
					sample = Mathf.Clamp01( sample );

					float mapped = Mathf.Clamp01( curve.Evaluate( sample ) );
					float y = Mathf.Lerp( settings.blackHeight, settings.whiteHeight, mapped ) + settings.yOffset;

					float wx = originX + x * stepX;
					float wz = originZ + z * stepZ;
					int i = z * resX + x;
					positions[ i ] = new Vector3( wx, y, wz );

					if ( settings.uvMode == HeightmapProBuilderSettings.UvMode.WorldMeters )
						uvs[ i ] = new Vector2( wx / uvTiling, wz / uvTiling );
					else
						uvs[ i ] = new Vector2( uNorm, vNorm );
				}
			}

			EditorUtility.DisplayProgressBar( "Heightmap → ProBuilder", "Building faces…", 0.85f );
			List<Face> faceList = new List<Face>( ( resX - 1 ) * ( resZ - 1 ) );
			for ( int z = 0; z < resZ - 1; z++ )
			{
				for ( int x = 0; x < resX - 1; x++ )
				{
					int i00 = z * resX + x;
					int i10 = i00 + 1;
					int i01 = ( z + 1 ) * resX + x;
					int i11 = i01 + 1;

					if ( settings.cullBelowThreshold )
					{
						float thr = settings.cullThresholdY;
						if ( positions[ i00 ].y <= thr
							&& positions[ i10 ].y <= thr
							&& positions[ i01 ].y <= thr
							&& positions[ i11 ].y <= thr )
						{
							continue;
						}
					}

					// Upward normal for XZ plane: (i00, i01, i10) / (i10, i01, i11)
					faceList.Add( new Face( new[] { i00, i01, i10, i10, i01, i11 } ) );
				}
			}

			if ( faceList.Count == 0 )
			{
				EditorUtility.DisplayDialog(
					"Heightmap → ProBuilder",
					"No faces generated. Check cull threshold or resolution.",
					"OK" );
				return false;
			}

			faces = faceList.ToArray();
			return true;
		}
		finally
		{
			EditorUtility.ClearProgressBar();
			if ( readable != null )
				DestroyImmediate( readable );
		}
	}

	static float SampleChannel( Color c, HeightmapProBuilderSettings.SampleChannel channel )
	{
		switch ( channel )
		{
			case HeightmapProBuilderSettings.SampleChannel.R:
				return c.r;
			case HeightmapProBuilderSettings.SampleChannel.G:
				return c.g;
			case HeightmapProBuilderSettings.SampleChannel.B:
				return c.b;
			case HeightmapProBuilderSettings.SampleChannel.A:
				return c.a;
			default:
				return c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
		}
	}

	static Texture2D BlitToReadable( Texture source )
	{
		if ( source == null )
			return null;

		int width = Mathf.Max( 1, source.width );
		int height = Mathf.Max( 1, source.height );
		RenderTexture rt = RenderTexture.GetTemporary( width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear );
		RenderTexture previous = RenderTexture.active;
		Graphics.Blit( source, rt );
		RenderTexture.active = rt;

		Texture2D readable = new Texture2D( width, height, TextureFormat.RGBA32, false, true );
		readable.wrapMode = TextureWrapMode.Clamp;
		readable.filterMode = FilterMode.Bilinear;
		readable.ReadPixels( new Rect( 0f, 0f, width, height ), 0, 0, false );
		readable.Apply( false, false );

		RenderTexture.active = previous;
		RenderTexture.ReleaseTemporary( rt );
		return readable;
	}

	static void LoadPrefs( HeightmapProBuilderSettings s )
	{
		if ( s == null )
			return;

		string guid = EditorPrefs.GetString( PrefPrefix + "HeightmapGuid", string.Empty );
		if ( !string.IsNullOrEmpty( guid ) )
		{
			string path = AssetDatabase.GUIDToAssetPath( guid );
			if ( !string.IsNullOrEmpty( path ) )
				s.heightmap = AssetDatabase.LoadAssetAtPath<Texture2D>( path );
		}

		s.sampleChannel = ( HeightmapProBuilderSettings.SampleChannel )EditorPrefs.GetInt(
			PrefPrefix + "SampleChannel",
			( int )HeightmapProBuilderSettings.SampleChannel.Luminance );
		s.invertSample = EditorPrefs.GetBool( PrefPrefix + "Invert", false );
		s.flipU = EditorPrefs.GetBool( PrefPrefix + "FlipU", false );
		s.flipV = EditorPrefs.GetBool( PrefPrefix + "FlipV", false );
		s.blackHeight = EditorPrefs.GetFloat( PrefPrefix + "BlackHeight", 0f );
		s.whiteHeight = EditorPrefs.GetFloat( PrefPrefix + "WhiteHeight", 20f );
		s.yOffset = EditorPrefs.GetFloat( PrefPrefix + "YOffset", 0f );
		s.worldSizeX = EditorPrefs.GetFloat( PrefPrefix + "WorldSizeX", 64f );
		s.worldSizeZ = EditorPrefs.GetFloat( PrefPrefix + "WorldSizeZ", 64f );
		s.resolutionX = EditorPrefs.GetInt( PrefPrefix + "ResolutionX", 65 );
		s.resolutionZ = EditorPrefs.GetInt( PrefPrefix + "ResolutionZ", 65 );
		s.pivot = ( HeightmapProBuilderSettings.PivotMode )EditorPrefs.GetInt(
			PrefPrefix + "Pivot",
			( int )HeightmapProBuilderSettings.PivotMode.Center );
		s.objectName = EditorPrefs.GetString( PrefPrefix + "ObjectName", "Heightmap ProBuilder" );
		s.addMeshCollider = EditorPrefs.GetBool( PrefPrefix + "MeshCollider", true );
		s.uvMode = ( HeightmapProBuilderSettings.UvMode )EditorPrefs.GetInt(
			PrefPrefix + "UvMode",
			( int )HeightmapProBuilderSettings.UvMode.Normalized );
		s.uvWorldTiling = EditorPrefs.GetFloat( PrefPrefix + "UvWorldTiling", 1f );
		s.cullBelowThreshold = EditorPrefs.GetBool( PrefPrefix + "Cull", false );
		s.cullThresholdY = EditorPrefs.GetFloat( PrefPrefix + "CullY", 0f );

		string matGuid = EditorPrefs.GetString( PrefPrefix + "MaterialGuid", string.Empty );
		if ( !string.IsNullOrEmpty( matGuid ) )
		{
			string matPath = AssetDatabase.GUIDToAssetPath( matGuid );
			if ( !string.IsNullOrEmpty( matPath ) )
				s.material = AssetDatabase.LoadAssetAtPath<Material>( matPath );
		}

		s.ClampResolutions();
	}

	static void SavePrefs( HeightmapProBuilderSettings s )
	{
		if ( s == null )
			return;

		string guid = string.Empty;
		if ( s.heightmap != null )
		{
			string path = AssetDatabase.GetAssetPath( s.heightmap );
			if ( !string.IsNullOrEmpty( path ) )
				guid = AssetDatabase.AssetPathToGUID( path );
		}
		EditorPrefs.SetString( PrefPrefix + "HeightmapGuid", guid );

		EditorPrefs.SetInt( PrefPrefix + "SampleChannel", ( int )s.sampleChannel );
		EditorPrefs.SetBool( PrefPrefix + "Invert", s.invertSample );
		EditorPrefs.SetBool( PrefPrefix + "FlipU", s.flipU );
		EditorPrefs.SetBool( PrefPrefix + "FlipV", s.flipV );
		EditorPrefs.SetFloat( PrefPrefix + "BlackHeight", s.blackHeight );
		EditorPrefs.SetFloat( PrefPrefix + "WhiteHeight", s.whiteHeight );
		EditorPrefs.SetFloat( PrefPrefix + "YOffset", s.yOffset );
		EditorPrefs.SetFloat( PrefPrefix + "WorldSizeX", s.worldSizeX );
		EditorPrefs.SetFloat( PrefPrefix + "WorldSizeZ", s.worldSizeZ );
		EditorPrefs.SetInt( PrefPrefix + "ResolutionX", s.resolutionX );
		EditorPrefs.SetInt( PrefPrefix + "ResolutionZ", s.resolutionZ );
		EditorPrefs.SetInt( PrefPrefix + "Pivot", ( int )s.pivot );
		EditorPrefs.SetString( PrefPrefix + "ObjectName", s.objectName ?? "Heightmap ProBuilder" );
		EditorPrefs.SetBool( PrefPrefix + "MeshCollider", s.addMeshCollider );
		EditorPrefs.SetInt( PrefPrefix + "UvMode", ( int )s.uvMode );
		EditorPrefs.SetFloat( PrefPrefix + "UvWorldTiling", s.uvWorldTiling );
		EditorPrefs.SetBool( PrefPrefix + "Cull", s.cullBelowThreshold );
		EditorPrefs.SetFloat( PrefPrefix + "CullY", s.cullThresholdY );

		string matGuid = string.Empty;
		if ( s.material != null )
		{
			string matPath = AssetDatabase.GetAssetPath( s.material );
			if ( !string.IsNullOrEmpty( matPath ) )
				matGuid = AssetDatabase.AssetPathToGUID( matPath );
		}
		EditorPrefs.SetString( PrefPrefix + "MaterialGuid", matGuid );
	}
}
#endif
