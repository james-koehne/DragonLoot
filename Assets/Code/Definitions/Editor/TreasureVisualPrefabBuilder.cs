#if UNITY_EDITOR
using System.IO;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds collectable treasure visual prefabs (layer, physics, colliders) from a source prefab or mesh.
/// Shared by Fantasy Pack installer and the Treasure Creation window.
/// </summary>
public static class TreasureVisualPrefabBuilder
{
	public const int CollectableLayer = 6;

	/// <summary>
	/// Instantiates <paramref name="source"/>, strips animators, applies Collectable layer + physics,
	/// and saves as a prefab at <paramref name="visualPath"/>. Returns the asset GUID, or null on failure.
	/// </summary>
	public static string CreateOrUpdateFromPrefab( GameObject source, string visualPath )
	{
		if ( source == null || string.IsNullOrEmpty( visualPath ) )
			return null;

		EnsureParentFolder( visualPath );

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( source );
		if ( instance == null )
			instance = Object.Instantiate( source );

		if ( PrefabUtility.IsPartOfPrefabInstance( instance ) )
		{
			PrefabUtility.UnpackPrefabInstance(
				instance,
				PrefabUnpackMode.Completely,
				InteractionMode.AutomatedAction );
		}

		PrepareRoot( instance, visualPath );
		GameObject saved = PrefabUtility.SaveAsPrefabAsset( instance, visualPath );
		Object.DestroyImmediate( instance );

		if ( saved == null )
			return null;

		return AssetDatabase.AssetPathToGUID( visualPath );
	}

	/// <summary>
	/// Builds a simple mesh visual (MeshFilter + MeshRenderer) with optional materials, then physics.
	/// </summary>
	public static string CreateOrUpdateFromMesh( Mesh mesh, Material[] materials, string visualPath )
	{
		if ( mesh == null || string.IsNullOrEmpty( visualPath ) )
			return null;

		EnsureParentFolder( visualPath );

		string visualName = Path.GetFileNameWithoutExtension( visualPath );
		GameObject instance = new GameObject( visualName );
		MeshFilter filter = instance.AddComponent<MeshFilter>();
		filter.sharedMesh = mesh;
		MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
		if ( materials != null && materials.Length > 0 )
			renderer.sharedMaterials = materials;

		PrepareRoot( instance, visualPath );
		GameObject saved = PrefabUtility.SaveAsPrefabAsset( instance, visualPath );
		Object.DestroyImmediate( instance );

		if ( saved == null )
			return null;

		return AssetDatabase.AssetPathToGUID( visualPath );
	}

	static void PrepareRoot( GameObject instance, string visualPath )
	{
		string visualName = Path.GetFileNameWithoutExtension( visualPath );
		instance.name = visualName;
		instance.transform.SetPositionAndRotation( Vector3.zero, Quaternion.identity );
		instance.transform.localScale = Vector3.one;

		StripAnimators( instance );
		SetLayerRecursive( instance, CollectableLayer );
		EnsurePhysicsComponents( instance );
	}

	public static void StripAnimators( GameObject root )
	{
		Animator[] animators = root.GetComponentsInChildren<Animator>( true );
		for ( int i = 0; i < animators.Length; i++ )
		{
			if ( animators[ i ] != null )
				Object.DestroyImmediate( animators[ i ] );
		}
	}

	public static void SetLayerRecursive( GameObject root, int layer )
	{
		Transform[] transforms = root.GetComponentsInChildren<Transform>( true );
		for ( int i = 0; i < transforms.Length; i++ )
			transforms[ i ].gameObject.layer = layer;
	}

	public static void EnsurePhysicsComponents( GameObject root )
	{
		Collider[] existing = root.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < existing.Length; i++ )
		{
			if ( existing[ i ] != null )
				Object.DestroyImmediate( existing[ i ] );
		}

		Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>( true );
		for ( int i = 0; i < bodies.Length; i++ )
		{
			if ( bodies[ i ] != null && bodies[ i ].gameObject != root )
				Object.DestroyImmediate( bodies[ i ] );
		}

		AddColliders( root );

		Rigidbody body = root.GetComponent<Rigidbody>();
		if ( body == null )
			body = root.AddComponent<Rigidbody>();
		body.mass = 0.25f;
		body.linearDamping = 0.6f;
		body.angularDamping = 0.6f;
		body.useGravity = true;
		body.isKinematic = true;
		body.interpolation = RigidbodyInterpolation.Interpolate;
		body.collisionDetectionMode = CollisionDetectionMode.Discrete;
	}

	static void AddColliders( GameObject root )
	{
		MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>( true );
		int added = 0;
		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			Mesh mesh = filter.sharedMesh;
			if ( mesh.vertexCount > 0 && mesh.vertexCount <= 255 )
			{
				MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
				meshCollider.sharedMesh = mesh;
				meshCollider.convex = true;
				added++;
			}
			else
			{
				Bounds localBounds = mesh.bounds;
				BoxCollider box = filter.gameObject.AddComponent<BoxCollider>();
				box.center = localBounds.center;
				box.size = localBounds.size;
				added++;
			}
		}

		if ( added == 0 )
		{
			Bounds bounds = CalculateRendererBounds( root );
			BoxCollider fallback = root.AddComponent<BoxCollider>();
			if ( bounds.size.sqrMagnitude > 0.0001f )
			{
				fallback.center = root.transform.InverseTransformPoint( bounds.center );
				Vector3 lossy = root.transform.lossyScale;
				fallback.size = new Vector3(
					SafeDiv( bounds.size.x, lossy.x ),
					SafeDiv( bounds.size.y, lossy.y ),
					SafeDiv( bounds.size.z, lossy.z ) );
			}
			else
			{
				fallback.size = Vector3.one * 0.5f;
			}
		}
	}

	static Bounds CalculateRendererBounds( GameObject root )
	{
		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		if ( renderers == null || renderers.Length == 0 )
			return new Bounds( root.transform.position, Vector3.zero );

		Bounds bounds = renderers[ 0 ].bounds;
		for ( int i = 1; i < renderers.Length; i++ )
		{
			if ( renderers[ i ] != null )
				bounds.Encapsulate( renderers[ i ].bounds );
		}
		return bounds;
	}

	static float SafeDiv( float a, float b )
	{
		return Mathf.Abs( b ) < 0.0001f ? a : a / b;
	}

	static void EnsureParentFolder( string assetPath )
	{
		string parent = Path.GetDirectoryName( assetPath );
		if ( parent != null )
			AddressableEditorUtil.EnsureFolder( parent.Replace( '\\', '/' ) );
	}
}
#endif
