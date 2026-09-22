using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AddressableAssets;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Aim volume on a slot anchor so placement resolves to the correct slot index.
/// Uses MeshColliders shaped from the required artifact visual (same meshes as the hologram).
/// </summary>
[DisallowMultipleComponent]
public class ArtifactPresentationSlotVolume : MonoBehaviour
{
	const string AimRootName = "SlotAimCollider";
	const float FallbackVolumeSize = 0.22f;
	const float FallbackVolumeHeight = 0.06f;

	[SerializeField]
	ArtifactPresentationTableInteractable table;

	[SerializeField]
	int slotIndex = -1;

	Transform _aimRoot;
	int _rebuildGeneration;

	public int SlotIndex => slotIndex;
	public ArtifactPresentationTableInteractable Table => table;

	public void Configure( ArtifactPresentationTableInteractable owner, int index )
	{
		table = owner;
		slotIndex = index;
	}

	public void SetAimCollidersEnabled( bool enabled )
	{
		EnsureAimRoot();
		if ( _aimRoot == null )
			return;

		Collider[] colliders = _aimRoot.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < colliders.Length; i++ )
		{
			if ( colliders[ i ] != null )
				colliders[ i ].enabled = enabled;
		}
	}

	/// <summary>
	/// Rebuilds aim colliders from the required artifact mesh. Falls back to a thin box when no mesh is available.
	/// </summary>
	public void RebuildAimCollider(
		TreasureDefinition definition,
		Vector3 socketRotation,
		Vector3 rotationOffset )
	{
		_rebuildGeneration++;
		StripLegacyAnchorColliders();
		EnsureAimRoot();
		ClearAimChildren();
		ApplyAimRootPose( definition, socketRotation, rotationOffset );

		if ( definition == null )
		{
			AttachFallbackBox();
			return;
		}

		if ( ArtifactPresentationPrefabSnapshot.TryGetCached( definition, out ArtifactPrefabMeshSnapshot cached )
			&& cached.HasParts )
		{
			AttachSnapshotColliders( cached );
			return;
		}

#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			ArtifactPrefabMeshSnapshot snapshot = LoadSnapshotEditor( definition );
			if ( snapshot.HasParts )
			{
				AttachSnapshotColliders( snapshot );
				return;
			}

			AttachFallbackBox();
			return;
		}
#endif

		if ( Application.isPlaying )
		{
			AttachFallbackBox();
			StartCoroutine( LoadAimColliderRoutine( definition, _rebuildGeneration ) );
		}
		else
			AttachFallbackBox();
	}

	IEnumerator LoadAimColliderRoutine( TreasureDefinition definition, int generation )
	{
		if ( definition == null )
			yield break;

		System.Threading.Tasks.Task<ArtifactPrefabMeshSnapshot> task =
			ArtifactPresentationPrefabSnapshot.LoadFromDefinitionAsync( definition );
		while ( !task.IsCompleted )
			yield return null;

		if ( this == null || generation != _rebuildGeneration )
			yield break;

		if ( task.Result.HasParts )
		{
			ClearAimChildren();
			ApplyAimRootPose( definition, table != null ? table.SocketRotation : Vector3.zero, GetRotationOffset() );
			AttachSnapshotColliders( task.Result );
		}
		else
		{
			ClearAimChildren();
			AttachFallbackBox();
		}

		if ( table != null && table.IsSlotOccupied( slotIndex ) )
			SetAimCollidersEnabled( false );
	}

	Vector3 GetRotationOffset()
	{
		if ( table == null || table.Slots == null || slotIndex < 0 || slotIndex >= table.Slots.Count )
			return Vector3.zero;

		return table.Slots[ slotIndex ].rotationOffset;
	}

	void ApplyAimRootPose( TreasureDefinition definition, Vector3 socketRotation, Vector3 rotationOffset )
	{
		EnsureAimRoot();
		if ( _aimRoot == null )
			return;

		_aimRoot.localPosition = Vector3.zero;
		_aimRoot.localRotation = ArtifactPresentationDisplayPose.GetSocketLocalRotation(
			socketRotation,
			rotationOffset );
		ArtifactPresentationDisplayPose.ApplyDefinitionWorldScale( _aimRoot, definition );
	}

	void AttachSnapshotColliders( ArtifactPrefabMeshSnapshot snapshot )
	{
		if ( _aimRoot == null || !snapshot.HasParts )
			return;

		Dictionary<string, ArtifactPrefabMeshPart> uniqueParts =
			new Dictionary<string, ArtifactPrefabMeshPart>();

		for ( int i = 0; i < snapshot.Parts.Length; i++ )
		{
			ArtifactPrefabMeshPart part = snapshot.Parts[ i ];
			if ( part.Mesh == null )
				continue;

			string key = part.Mesh.GetInstanceID() + "|"
				+ part.LocalPosition.x + "," + part.LocalPosition.y + "," + part.LocalPosition.z + "|"
				+ part.LocalRotation.x + "," + part.LocalRotation.y + "," + part.LocalRotation.z + ","
				+ part.LocalRotation.w + "|"
				+ part.LocalScale.x + "," + part.LocalScale.y + "," + part.LocalScale.z;

			if ( !uniqueParts.ContainsKey( key ) )
				uniqueParts[ key ] = part;
		}

		int partIndex = 0;
		foreach ( KeyValuePair<string, ArtifactPrefabMeshPart> pair in uniqueParts )
		{
			ArtifactPrefabMeshPart part = pair.Value;
			GameObject partGo = new GameObject( "AimPart_" + partIndex );
			partGo.transform.SetParent( _aimRoot, false );
			partGo.transform.localPosition = part.LocalPosition;
			partGo.transform.localRotation = part.LocalRotation;
			partGo.transform.localScale = part.LocalScale;
			partGo.layer = gameObject.layer;

			MeshCollider meshCollider = partGo.AddComponent<MeshCollider>();
			meshCollider.sharedMesh = part.Mesh;
			// Non-convex matches the hologram silhouette; slots have no Rigidbody so this is valid for raycasts.
			meshCollider.convex = false;
			meshCollider.isTrigger = false;
			partIndex++;
		}

		if ( partIndex == 0 )
			AttachFallbackBox();
	}

	void AttachFallbackBox()
	{
		EnsureAimRoot();
		if ( _aimRoot == null )
			return;

		BoxCollider box = _aimRoot.gameObject.GetComponent<BoxCollider>();
		if ( box == null )
			box = _aimRoot.gameObject.AddComponent<BoxCollider>();

		box.center = Vector3.zero;
		box.size = new Vector3( FallbackVolumeSize, FallbackVolumeHeight, FallbackVolumeSize );
		box.isTrigger = false;
		box.enabled = true;
	}

	void EnsureAimRoot()
	{
		if ( _aimRoot != null )
			return;

		Transform existing = transform.Find( AimRootName );
		if ( existing != null )
		{
			_aimRoot = existing;
			return;
		}

		GameObject rootGo = new GameObject( AimRootName );
		rootGo.transform.SetParent( transform, false );
		rootGo.layer = gameObject.layer;
		_aimRoot = rootGo.transform;
	}

	void ClearAimChildren()
	{
		EnsureAimRoot();
		if ( _aimRoot == null )
			return;

		BoxCollider boxOnRoot = _aimRoot.GetComponent<BoxCollider>();
		if ( boxOnRoot != null )
			DestroyAimObject( boxOnRoot );

		for ( int i = _aimRoot.childCount - 1; i >= 0; i-- )
		{
			Transform child = _aimRoot.GetChild( i );
			if ( child != null )
				DestroyAimObject( child.gameObject );
		}
	}

	void StripLegacyAnchorColliders()
	{
		BoxCollider box = GetComponent<BoxCollider>();
		if ( box != null )
			DestroyAimObject( box );

		MeshCollider mesh = GetComponent<MeshCollider>();
		if ( mesh != null )
			DestroyAimObject( mesh );
	}

	static void DestroyAimObject( Object target )
	{
		if ( target == null )
			return;

#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			DestroyImmediate( target );
			return;
		}
#endif
		Destroy( target );
	}

#if UNITY_EDITOR
	static ArtifactPrefabMeshSnapshot LoadSnapshotEditor( TreasureDefinition definition )
	{
		ArtifactPrefabMeshSnapshot snapshot = default;
		if ( definition == null )
			return snapshot;

		AssetReferenceGameObject prefabRef = definition.prefab;
		if ( prefabRef == null || !prefabRef.RuntimeKeyIsValid() )
			return snapshot;

		string path = AssetDatabase.GUIDToAssetPath( prefabRef.AssetGUID );
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( path );
		snapshot = ArtifactPresentationPrefabSnapshot.ExtractFromPrefabRoot( prefab );
		ArtifactPresentationPrefabSnapshot.CacheSnapshot( definition, snapshot );
		return snapshot;
	}
#endif

	public static bool TryResolveSlotIndex( Collider collider, out int slotIndex )
	{
		slotIndex = -1;
		if ( collider == null )
			return false;

		ArtifactPresentationSlotVolume volume = collider.GetComponent<ArtifactPresentationSlotVolume>();
		if ( volume == null )
			volume = collider.GetComponentInParent<ArtifactPresentationSlotVolume>();
		if ( volume == null || volume.slotIndex < 0 )
			return false;

		slotIndex = volume.slotIndex;
		return true;
	}
}
