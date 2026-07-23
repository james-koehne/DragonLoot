#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only textured plane via a hidden MeshRenderer in the authoring scene (no Handles discs).
/// </summary>
static class TreasureSurfaceAuthoringOverlay
{
	const int MaxTextureSize = 512;

	static readonly Color32 BlockedPixel = new Color32( 200, 50, 50, 48 );
	static readonly Color32[] MaterialPixels = BuildMaterialPixels();

	static Material s_Material;
	static Texture2D s_Texture;
	static Mesh s_UnitQuadMesh;
	static Color32[] s_Pixels;
	static GameObject s_PreviewRoot;
	static MeshFilter s_PreviewFilter;
	static MeshRenderer s_PreviewRenderer;
	static int s_CachedAuthoringId;
	static int s_CachedRevision = -1;
	static int s_CachedWidth;
	static int s_CachedHeight;
	static bool s_DeferTextureRebuild;
	static Vector3 s_LastPosition;
	static Vector3 s_LastScale;
	static bool s_IsVisible;

	public static void Show( TreasureSurfaceAuthoring authoring )
	{
		if ( authoring == null )
		{
			Hide();
			return;
		}

		s_IsVisible = true;
		if ( !Sync( authoring, true ) )
			Hide();
	}

	public static void MaintainVisible( TreasureSurfaceAuthoring authoring )
	{
		if ( authoring == null || !s_IsVisible )
			return;

		Sync( authoring, false );
	}

	public static void Hide()
	{
		s_IsVisible = false;
		if ( s_PreviewRoot != null )
		{
			s_PreviewRenderer.enabled = false;
			s_PreviewRoot.SetActive( false );
		}
	}

	public static void Invalidate()
	{
		s_CachedRevision = -1;
	}

	public static void SetDeferTextureRebuild( bool defer )
	{
		s_DeferTextureRebuild = defer;
	}

	static bool Sync( TreasureSurfaceAuthoring authoring, bool forceTexture )
	{
		Texture2D tex = GetOrRebuildTexture( authoring, forceTexture );
		if ( tex == null )
			return false;

		EnsureMaterial();
		EnsureUnitQuadMesh();
		EnsurePreviewObject( authoring );

		if ( s_PreviewRenderer.sharedMaterial != s_Material )
			s_PreviewRenderer.sharedMaterial = s_Material;

		if ( s_PreviewFilter.sharedMesh != s_UnitQuadMesh )
			s_PreviewFilter.sharedMesh = s_UnitQuadMesh;

		if ( s_Material.mainTexture != tex )
			ApplyTextureToMaterial( tex );

		float halfX = authoring.WorldSizeX * 0.5f;
		float halfZ = authoring.WorldSizeZ * 0.5f;
		float y = authoring.BaseHeight + 0.03f;
		Vector3 position = new Vector3( authoring.WorldOrigin.x - halfX, y, authoring.WorldOrigin.z - halfZ );
		Vector3 scale = new Vector3( authoring.WorldSizeX, 1f, authoring.WorldSizeZ );

		Transform t = s_PreviewRoot.transform;
		if ( position != s_LastPosition )
		{
			t.position = position;
			s_LastPosition = position;
		}

		if ( scale != s_LastScale )
		{
			t.localScale = scale;
			s_LastScale = scale;
		}

		if ( t.rotation != Quaternion.identity )
			t.rotation = Quaternion.identity;

		if ( !s_PreviewRoot.activeSelf )
			s_PreviewRoot.SetActive( true );

		if ( !s_PreviewRenderer.enabled )
			s_PreviewRenderer.enabled = true;

		return true;
	}

	static void EnsurePreviewObject( TreasureSurfaceAuthoring authoring )
	{
		if ( s_PreviewRoot == null )
		{
			s_PreviewRoot = new GameObject( "~TreasureSurfacePaintOverlay" )
			{
				hideFlags = HideFlags.HideAndDontSave
			};

			s_PreviewFilter = s_PreviewRoot.AddComponent<MeshFilter>();
			s_PreviewRenderer = s_PreviewRoot.AddComponent<MeshRenderer>();
			s_PreviewRenderer.shadowCastingMode = ShadowCastingMode.Off;
			s_PreviewRenderer.receiveShadows = false;
			s_PreviewRenderer.lightProbeUsage = LightProbeUsage.Off;
			s_PreviewRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
			s_PreviewRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
			s_PreviewRoot.SetActive( false );
		}

		Scene targetScene = authoring.gameObject.scene;
		if ( targetScene.IsValid() && s_PreviewRoot.scene != targetScene )
			EditorSceneManager.MoveGameObjectToScene( s_PreviewRoot, targetScene );
	}

	static void EnsureUnitQuadMesh()
	{
		if ( s_UnitQuadMesh != null )
			return;

		s_UnitQuadMesh = new Mesh
		{
			name = "TreasureSurfacePaintOverlayUnitQuad",
			hideFlags = HideFlags.HideAndDontSave
		};

		s_UnitQuadMesh.vertices = new[]
		{
			new Vector3( 0f, 0f, 0f ),
			new Vector3( 1f, 0f, 0f ),
			new Vector3( 1f, 0f, 1f ),
			new Vector3( 0f, 0f, 1f )
		};
		s_UnitQuadMesh.uv = new[]
		{
			new Vector2( 0f, 0f ),
			new Vector2( 1f, 0f ),
			new Vector2( 1f, 1f ),
			new Vector2( 0f, 1f )
		};
		s_UnitQuadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
		s_UnitQuadMesh.RecalculateNormals();
		s_UnitQuadMesh.RecalculateBounds();
	}

	static Texture2D GetOrRebuildTexture( TreasureSurfaceAuthoring authoring, bool force )
	{
		authoring.EnsurePaintBuffers();
		int cellsX = authoring.TotalCellsX;
		int cellsZ = authoring.TotalCellsZ;
		if ( cellsX <= 0 || cellsZ <= 0 )
			return null;

		ComputeTextureSize( authoring.WorldSizeX, authoring.WorldSizeZ, out int texW, out int texH );
		int id = authoring.GetInstanceID();
		int revision = authoring.PaintRevision;

		if ( !force
			&& s_DeferTextureRebuild
			&& s_Texture != null
			&& s_CachedAuthoringId == id
			&& s_CachedWidth == texW
			&& s_CachedHeight == texH )
			return s_Texture;

		if ( !force
			&& s_Texture != null
			&& s_CachedAuthoringId == id
			&& s_CachedRevision == revision
			&& s_CachedWidth == texW
			&& s_CachedHeight == texH )
			return s_Texture;

		if ( s_Texture == null || s_CachedWidth != texW || s_CachedHeight != texH )
		{
			if ( s_Texture != null )
				Object.DestroyImmediate( s_Texture );

			s_Texture = new Texture2D( texW, texH, TextureFormat.RGBA32, false, true )
			{
				name = "TreasureSurfacePaintOverlayTex",
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				hideFlags = HideFlags.HideAndDontSave
			};

			s_CachedWidth = texW;
			s_CachedHeight = texH;
		}

		int pixelCount = texW * texH;
		if ( s_Pixels == null || s_Pixels.Length != pixelCount )
			s_Pixels = new Color32[ pixelCount ];

		FillPixels( authoring, texW, texH, s_Pixels );
		s_Texture.SetPixels32( s_Pixels );
		s_Texture.Apply( false, false );

		s_CachedAuthoringId = id;
		s_CachedRevision = revision;
		return s_Texture;
	}

	static void ComputeTextureSize( float worldSizeX, float worldSizeZ, out int width, out int height )
	{
		float aspect = worldSizeX / Mathf.Max( 0.001f, worldSizeZ );
		if ( aspect >= 1f )
		{
			width = MaxTextureSize;
			height = Mathf.Max( 1, Mathf.RoundToInt( MaxTextureSize / aspect ) );
		}
		else
		{
			height = MaxTextureSize;
			width = Mathf.Max( 1, Mathf.RoundToInt( MaxTextureSize * aspect ) );
		}
	}

	static void FillPixels( TreasureSurfaceAuthoring authoring, int texW, int texH, Color32[] pixels )
	{
		authoring.EditorGetPaintArrays( out byte[] trav, out byte[] mat, out int cellsX, out int cellsZ );
		if ( trav == null || mat == null || cellsX <= 0 || cellsZ <= 0 )
			return;

		for ( int py = 0; py < texH; py++ )
		{
			int cellZ = py * cellsZ / texH;
			if ( cellZ >= cellsZ )
				cellZ = cellsZ - 1;

			int row = cellZ * cellsX;
			int pyOffset = py * texW;

			for ( int px = 0; px < texW; px++ )
			{
				int cellX = px * cellsX / texW;
				if ( cellX >= cellsX )
					cellX = cellsX - 1;

				int cellIndex = row + cellX;
				int i = pyOffset + px;
				if ( trav[ cellIndex ] == 0 )
				{
					pixels[ i ] = BlockedPixel;
					continue;
				}

				byte matByte = mat[ cellIndex ];
				if ( matByte < MaterialPixels.Length )
					pixels[ i ] = MaterialPixels[ matByte ];
				else
					pixels[ i ] = MaterialPixels[ 0 ];
			}
		}
	}

	static Color32[] BuildMaterialPixels()
	{
		int count = System.Enum.GetValues( typeof( TreasureSurfaceMaterial ) ).Length;
		Color32[] table = new Color32[ count ];
		for ( int i = 0; i < count; i++ )
		{
			Color c = MaterialColor( ( TreasureSurfaceMaterial )i );
			table[ i ] = new Color32(
				( byte )( c.r * 255f ),
				( byte )( c.g * 255f ),
				( byte )( c.b * 255f ),
				( byte )( c.a * 255f ) );
		}

		return table;
	}

	static void EnsureMaterial()
	{
		if ( s_Material != null )
			return;

		Shader shader = Shader.Find( "Universal Render Pipeline/Unlit" );
		if ( shader == null )
			shader = Shader.Find( "Unlit/Transparent" );
		if ( shader == null )
			shader = Shader.Find( "Sprites/Default" );

		s_Material = new Material( shader )
		{
			hideFlags = HideFlags.HideAndDontSave
		};

		if ( s_Material.HasProperty( "_Surface" ) )
			s_Material.SetFloat( "_Surface", 1f );
		if ( s_Material.HasProperty( "_Blend" ) )
			s_Material.SetFloat( "_Blend", 0f );
		if ( s_Material.HasProperty( "_ZWrite" ) )
			s_Material.SetFloat( "_ZWrite", 0f );
		if ( s_Material.HasProperty( "_Cull" ) )
			s_Material.SetInt( "_Cull", ( int )CullMode.Off );

		s_Material.renderQueue = ( int )RenderQueue.Transparent;
		s_Material.EnableKeyword( "_SURFACE_TYPE_TRANSPARENT" );
	}

	static void ApplyTextureToMaterial( Texture2D tex )
	{
		s_Material.mainTexture = tex;
		if ( s_Material.HasProperty( "_BaseMap" ) )
			s_Material.SetTexture( "_BaseMap", tex );
		if ( s_Material.HasProperty( "_MainTex" ) )
			s_Material.SetTexture( "_MainTex", tex );

		Color white = Color.white;
		if ( s_Material.HasProperty( "_BaseColor" ) )
			s_Material.SetColor( "_BaseColor", white );
		if ( s_Material.HasProperty( "_Color" ) )
			s_Material.SetColor( "_Color", white );
	}

	static Color MaterialColor( TreasureSurfaceMaterial material )
	{
		switch ( material )
		{
			case TreasureSurfaceMaterial.Gold: return new Color( 1f, 0.85f, 0.2f, 0.55f );
			case TreasureSurfaceMaterial.Gems: return new Color( 0.4f, 0.9f, 1f, 0.55f );
			case TreasureSurfaceMaterial.Wood: return new Color( 0.7f, 0.45f, 0.2f, 0.55f );
			case TreasureSurfaceMaterial.Metal: return new Color( 0.75f, 0.75f, 0.8f, 0.55f );
			case TreasureSurfaceMaterial.Cloth: return new Color( 0.7f, 0.4f, 0.7f, 0.55f );
			default: return new Color( 0.25f, 1f, 0.4f, 0.5f );
		}
	}
}
#endif
