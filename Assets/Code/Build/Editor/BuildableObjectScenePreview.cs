#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode Scene view only: draws a ghost of each <see cref="BuildableObject"/> Built mesh
/// when Built/Ghost are inactive. Uses <see cref="Graphics.DrawMeshNow"/> so nothing is
/// parented into the scene, saved, or marked dirty. Clicks on the ghost select the buildable.
/// </summary>
[InitializeOnLoad]
static class BuildableObjectScenePreview
{
	const string GhostMaterialPath = "Assets/Materials/Shaders/Placement/M_BuildGhost.mat";
	static readonly int ControlHint = "BuildableObjectScenePreview".GetHashCode();

	static Material s_Material;
	static BuildableObject[] s_Cache = System.Array.Empty<BuildableObject>();
	static double s_LastCacheTime = -1d;
	static bool s_HierarchyDirty = true;

	static BuildableObjectScenePreview()
	{
		SceneView.duringSceneGui += OnDuringSceneGui;
		EditorApplication.hierarchyChanged += () => s_HierarchyDirty = true;
	}

	static void OnDuringSceneGui( SceneView sceneView )
	{
		if ( Application.isPlaying )
			return;

		Event e = Event.current;
		if ( e == null )
			return;

		int controlId = GUIUtility.GetControlID( ControlHint, FocusType.Passive );
		HandleGhostSelection( e, controlId );

		if ( e.type != EventType.Repaint )
			return;

		EnsureMaterial();
		if ( s_Material == null )
			return;

		RefreshCacheIfNeeded();
		if ( s_Cache.Length == 0 )
			return;

		// Disable distance fade so authoring preview stays fully visible.
		s_Material.SetFloat( "_FadeStart", 9999f );
		s_Material.SetFloat( "_FadeEnd", 10000f );
		s_Material.SetFloat( "_AlphaMul", 1f );
		s_Material.SetFloat( "_CompleteGlow", 0f );
		if ( sceneView != null && sceneView.camera != null )
			s_Material.SetVector( "_PlayerWorldPos", sceneView.camera.transform.position );

		if ( !s_Material.SetPass( 0 ) )
			return;

		for ( int i = 0; i < s_Cache.Length; i++ )
		{
			BuildableObject buildable = s_Cache[ i ];
			if ( buildable == null )
				continue;

			if ( !TryGetPreviewMeshRoot( buildable, out Transform meshRoot ) )
				continue;

			DrawMeshRoot( meshRoot );
		}
	}

	static void HandleGhostSelection( Event e, int controlId )
	{
		if ( e.type == EventType.MouseUp && GUIUtility.hotControl == controlId )
		{
			GUIUtility.hotControl = 0;
			e.Use();
			return;
		}

		if ( e.type != EventType.MouseDown || e.button != 0 || e.alt )
			return;

		RefreshCacheIfNeeded();
		if ( s_Cache.Length == 0 )
			return;

		if ( !TryPickGhost( e.mousePosition, out BuildableObject hit, out float ghostDist ) )
			return;

		// Don't steal clicks from closer real Scene objects.
		GameObject picked = HandleUtility.PickGameObject( e.mousePosition, false );
		if ( picked != null )
		{
			BuildableObject pickedBuildable = picked.GetComponentInParent<BuildableObject>();
			if ( pickedBuildable != hit )
			{
				Ray ray = HandleUtility.GUIPointToWorldRay( e.mousePosition );
				if ( TryGetObjectPickDistance( ray, picked, out float pickedDist ) && pickedDist < ghostDist )
					return;
			}
		}

		Selection.activeGameObject = hit.gameObject;
		GUIUtility.hotControl = controlId;
		e.Use();
		SceneView.RepaintAll();
	}

	static bool TryPickGhost( Vector2 guiPoint, out BuildableObject best, out float bestDist )
	{
		best = null;
		bestDist = float.PositiveInfinity;

		Ray ray = HandleUtility.GUIPointToWorldRay( guiPoint );
		for ( int i = 0; i < s_Cache.Length; i++ )
		{
			BuildableObject buildable = s_Cache[ i ];
			if ( buildable == null )
				continue;
			if ( !TryGetPreviewMeshRoot( buildable, out Transform meshRoot ) )
				continue;

			MeshFilter[] filters = meshRoot.GetComponentsInChildren<MeshFilter>( true );
			for ( int f = 0; f < filters.Length; f++ )
			{
				MeshFilter filter = filters[ f ];
				if ( filter == null || filter.sharedMesh == null )
					continue;

				if ( !TryIntersectRayMesh( ray, filter.sharedMesh, filter.transform.localToWorldMatrix, out float hitDist ) )
					continue;
				if ( hitDist >= bestDist )
					continue;

				bestDist = hitDist;
				best = buildable;
			}
		}

		return best != null;
	}

	static bool TryIntersectRayMesh( Ray ray, Mesh mesh, Matrix4x4 localToWorld, out float distance )
	{
		distance = float.PositiveInfinity;
		if ( mesh == null )
			return false;

		Bounds worldBounds = TransformBounds( mesh.bounds, localToWorld );
		if ( !worldBounds.IntersectRay( ray, out float boundsDist ) )
			return false;

		// Non-readable meshes can't expose triangles — fall back to bounds.
		if ( !mesh.isReadable )
		{
			distance = boundsDist;
			return true;
		}

		Vector3[] verts = mesh.vertices;
		int[] tris = mesh.triangles;
		bool hit = false;
		for ( int i = 0; i < tris.Length; i += 3 )
		{
			Vector3 v0 = localToWorld.MultiplyPoint3x4( verts[ tris[ i ] ] );
			Vector3 v1 = localToWorld.MultiplyPoint3x4( verts[ tris[ i + 1 ] ] );
			Vector3 v2 = localToWorld.MultiplyPoint3x4( verts[ tris[ i + 2 ] ] );
			if ( !RayIntersectsTriangle( ray, v0, v1, v2, out float t ) )
				continue;
			if ( t >= distance )
				continue;
			distance = t;
			hit = true;
		}

		return hit;
	}

	static bool RayIntersectsTriangle( Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t )
	{
		t = 0f;
		const float epsilon = 1e-6f;
		Vector3 edge1 = v1 - v0;
		Vector3 edge2 = v2 - v0;
		Vector3 p = Vector3.Cross( ray.direction, edge2 );
		float det = Vector3.Dot( edge1, p );
		if ( det > -epsilon && det < epsilon )
			return false;

		float invDet = 1f / det;
		Vector3 s = ray.origin - v0;
		float u = Vector3.Dot( s, p ) * invDet;
		if ( u < 0f || u > 1f )
			return false;

		Vector3 q = Vector3.Cross( s, edge1 );
		float v = Vector3.Dot( ray.direction, q ) * invDet;
		if ( v < 0f || u + v > 1f )
			return false;

		t = Vector3.Dot( edge2, q ) * invDet;
		return t >= 0f;
	}

	static Bounds TransformBounds( Bounds localBounds, Matrix4x4 localToWorld )
	{
		Vector3 center = localBounds.center;
		Vector3 extents = localBounds.extents;
		Bounds world = default;
		bool has = false;
		for ( int x = -1; x <= 1; x += 2 )
		{
			for ( int y = -1; y <= 1; y += 2 )
			{
				for ( int z = -1; z <= 1; z += 2 )
				{
					Vector3 corner = center + new Vector3( extents.x * x, extents.y * y, extents.z * z );
					Vector3 worldCorner = localToWorld.MultiplyPoint3x4( corner );
					if ( !has )
					{
						world = new Bounds( worldCorner, Vector3.zero );
						has = true;
					}
					else
						world.Encapsulate( worldCorner );
				}
			}
		}

		return world;
	}

	static bool TryGetObjectPickDistance( Ray ray, GameObject go, out float distance )
	{
		distance = float.PositiveInfinity;
		if ( go == null )
			return false;

		Collider[] colliders = go.GetComponentsInChildren<Collider>();
		bool any = false;
		for ( int i = 0; i < colliders.Length; i++ )
		{
			Collider collider = colliders[ i ];
			if ( collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy )
				continue;
			if ( !collider.Raycast( ray, out RaycastHit hit, float.PositiveInfinity ) )
				continue;
			if ( hit.distance < distance )
				distance = hit.distance;
			any = true;
		}

		if ( any )
			return true;

		Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy )
				continue;
			if ( !renderer.bounds.IntersectRay( ray, out float boundsDist ) )
				continue;
			if ( boundsDist < distance )
				distance = boundsDist;
			any = true;
		}

		return any;
	}

	static bool TryGetPreviewMeshRoot( BuildableObject buildable, out Transform meshRoot )
	{
		meshRoot = null;
		Transform builtRoot = buildable.BuiltRoot;
		Transform ghostRoot = buildable.GhostRoot;

		// Already visible as solid Built or live Ghost — no overlay / pick proxy.
		if ( builtRoot != null && builtRoot.gameObject.activeInHierarchy )
			return false;
		if ( ghostRoot != null && ghostRoot.gameObject.activeInHierarchy )
			return false;

		meshRoot = builtRoot != null ? builtRoot : ghostRoot;
		return meshRoot != null;
	}

	static void DrawMeshRoot( Transform meshRoot )
	{
		MeshFilter[] filters = meshRoot.GetComponentsInChildren<MeshFilter>( true );
		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			Graphics.DrawMeshNow( filter.sharedMesh, filter.transform.localToWorldMatrix );
		}
	}

	static void RefreshCacheIfNeeded()
	{
		double now = EditorApplication.timeSinceStartup;
		if ( !s_HierarchyDirty && s_Cache != null && now - s_LastCacheTime < 0.5d )
			return;

		s_HierarchyDirty = false;
		s_LastCacheTime = now;
		s_Cache = Object.FindObjectsByType<BuildableObject>( FindObjectsInactive.Include );
	}

	static void EnsureMaterial()
	{
		if ( s_Material != null )
			return;

		Material shared = AssetDatabase.LoadAssetAtPath<Material>( GhostMaterialPath );
		if ( shared != null )
		{
			s_Material = new Material( shared );
		}
		else
		{
			Shader shader = Shader.Find( "DragonLoot/Build Ghost" );
			if ( shader == null )
				shader = Shader.Find( "Universal Render Pipeline/Unlit" );
			if ( shader == null )
				return;

			s_Material = new Material( shader );
			s_Material.SetColor( "_BaseColor", new Color( 0.35f, 0.75f, 1f, 0.4f ) );
		}

		s_Material.name = "BuildableObjectScenePreview";
		s_Material.hideFlags = HideFlags.HideAndDontSave;
	}
}
#endif
