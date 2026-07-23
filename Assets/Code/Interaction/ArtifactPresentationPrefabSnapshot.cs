using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// One drawable mesh part from a treasure visual prefab (one MeshFilter × one submesh).
/// </summary>
public struct ArtifactPrefabMeshPart
{
	public Mesh Mesh;
	public int SubmeshIndex;
	public Material Material;
	public Vector3 LocalPosition;
	public Quaternion LocalRotation;
	public Vector3 LocalScale;

	public Matrix4x4 LocalMatrix => Matrix4x4.TRS( LocalPosition, LocalRotation, LocalScale );
}

/// <summary>
/// All mesh parts from a treasure visual prefab; shared by slot indicators, ghosts, and pile draws.
/// </summary>
public struct ArtifactPrefabMeshSnapshot
{
	public ArtifactPrefabMeshPart[] Parts;

	public bool HasParts => Parts != null && Parts.Length > 0;

	public ArtifactPrefabMeshPart Primary =>
		HasParts ? Parts[ 0 ] : default;
}

public static class ArtifactPresentationPrefabSnapshot
{
	static readonly Dictionary<string, ArtifactPrefabMeshSnapshot> CacheByKey =
		new Dictionary<string, ArtifactPrefabMeshSnapshot>();
	static readonly List<AsyncOperationHandle<GameObject>> LoadHandles =
		new List<AsyncOperationHandle<GameObject>>();

	public static string GetDefinitionKey( TreasureDefinition definition )
	{
		if ( definition == null || definition.prefab == null || !definition.prefab.RuntimeKeyIsValid() )
			return null;

		return definition.prefab.RuntimeKey.ToString();
	}

	public static bool TryGetCached( TreasureDefinition definition, out ArtifactPrefabMeshSnapshot snapshot )
	{
		snapshot = default;
		string key = GetDefinitionKey( definition );
		if ( string.IsNullOrEmpty( key ) )
			return false;

		return CacheByKey.TryGetValue( key, out snapshot );
	}

	public static ArtifactPrefabMeshSnapshot ExtractFromPrefabRoot( GameObject prefab )
	{
		ArtifactPrefabMeshSnapshot snapshot = default;
		if ( prefab == null )
			return snapshot;

		MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>( true );
		if ( filters == null || filters.Length == 0 )
			return snapshot;

		List<ArtifactPrefabMeshPart> parts = new List<ArtifactPrefabMeshPart>( filters.Length );
		Transform prefabRoot = prefab.transform;

		for ( int i = 0; i < filters.Length; i++ )
		{
			MeshFilter filter = filters[ i ];
			if ( filter == null || filter.sharedMesh == null )
				continue;

			Mesh mesh = filter.sharedMesh;
			Transform sourceTransform = filter.transform;
			Vector3 localPos = prefabRoot.InverseTransformPoint( sourceTransform.position );
			Quaternion localRot = Quaternion.Inverse( prefabRoot.rotation ) * sourceTransform.rotation;
			Vector3 localScale = sourceTransform.localScale;
			if ( sourceTransform.parent != prefabRoot )
			{
				Vector3 lossy = sourceTransform.lossyScale;
				Vector3 parentLossy = prefabRoot.lossyScale;
				localScale = new Vector3(
					SafeDivScale( lossy.x, parentLossy.x ),
					SafeDivScale( lossy.y, parentLossy.y ),
					SafeDivScale( lossy.z, parentLossy.z ) );
			}

			MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
			Material[] materials = renderer != null ? renderer.sharedMaterials : null;
			int submeshCount = Mathf.Max( 1, mesh.subMeshCount );
			int materialCount = materials != null ? materials.Length : 0;

			for ( int s = 0; s < submeshCount; s++ )
			{
				Material mat = null;
				if ( materialCount > 0 )
					mat = materials[ Mathf.Min( s, materialCount - 1 ) ];

				parts.Add( new ArtifactPrefabMeshPart
				{
					Mesh = mesh,
					SubmeshIndex = s,
					Material = mat,
					LocalPosition = localPos,
					LocalRotation = localRot,
					LocalScale = localScale
				} );
			}
		}

		snapshot.Parts = parts.ToArray();
		return snapshot;
	}

	public static async Task<ArtifactPrefabMeshSnapshot> LoadFromDefinitionAsync( TreasureDefinition definition )
	{
		ArtifactPrefabMeshSnapshot snapshot = default;
		if ( definition == null )
			return snapshot;

		string key = GetDefinitionKey( definition );
		if ( string.IsNullOrEmpty( key ) )
			return snapshot;

		if ( CacheByKey.TryGetValue( key, out snapshot ) )
			return snapshot;

		AssetReferenceGameObject prefabRef = definition.prefab;
		AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>( prefabRef.RuntimeKey );
		await handle.Task;
		if ( handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null )
		{
			if ( handle.IsValid() )
				Addressables.Release( handle );
			return default;
		}

		LoadHandles.Add( handle );
		snapshot = ExtractFromPrefabRoot( handle.Result );
		CacheByKey[ key ] = snapshot;
		return snapshot;
	}

	public static void CacheSnapshot( TreasureDefinition definition, in ArtifactPrefabMeshSnapshot snapshot )
	{
		string key = GetDefinitionKey( definition );
		if ( string.IsNullOrEmpty( key ) )
			return;

		CacheByKey[ key ] = snapshot;
	}

	public static void ReleaseAllHandles()
	{
		for ( int i = 0; i < LoadHandles.Count; i++ )
		{
			if ( LoadHandles[ i ].IsValid() )
				Addressables.Release( LoadHandles[ i ] );
		}

		LoadHandles.Clear();
	}

	public static float SafeDivScale( float a, float b )
	{
		if ( Mathf.Abs( b ) < 0.0001f )
			return a;
		return a / b;
	}
}

public static class ArtifactPresentationDisplayPose
{
	public static Quaternion GetSocketLocalRotation( Vector3 socketRotation, Vector3 rotationOffset )
	{
		return Quaternion.Euler( socketRotation ) * Quaternion.Euler( rotationOffset );
	}

	public static void ApplyDefinitionWorldScale( Transform itemRoot, TreasureDefinition definition )
	{
		if ( itemRoot == null )
			return;

		Vector3 desired = definition != null ? definition.worldScale : Vector3.one * 0.35f;
		Transform parent = itemRoot.parent;
		if ( parent == null )
		{
			itemRoot.localScale = desired;
			return;
		}

		Vector3 parentLossy = parent.lossyScale;
		itemRoot.localScale = new Vector3(
			ArtifactPresentationPrefabSnapshot.SafeDivScale( desired.x, parentLossy.x ),
			ArtifactPresentationPrefabSnapshot.SafeDivScale( desired.y, parentLossy.y ),
			ArtifactPresentationPrefabSnapshot.SafeDivScale( desired.z, parentLossy.z ) );
	}

	public static void GetItemRootWorldPose(
		Transform anchor,
		Vector3 socketRotation,
		Vector3 rotationOffset,
		out Vector3 worldPosition,
		out Quaternion worldRotation )
	{
		if ( anchor == null )
		{
			worldPosition = Vector3.zero;
			worldRotation = Quaternion.identity;
			return;
		}

		worldPosition = anchor.position;
		worldRotation = anchor.rotation * GetSocketLocalRotation( socketRotation, rotationOffset );
	}

	public static void ParentItemToSlotAnchor(
		Transform itemRoot,
		Transform anchor,
		Vector3 socketRotation,
		Vector3 rotationOffset,
		TreasureDefinition definition )
	{
		if ( itemRoot == null || anchor == null )
			return;

		itemRoot.SetParent( anchor, false );
		itemRoot.localPosition = Vector3.zero;
		itemRoot.localRotation = GetSocketLocalRotation( socketRotation, rotationOffset );
		ApplyDefinitionWorldScale( itemRoot, definition );
	}
}
