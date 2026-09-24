using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Persistent cyan/green/red hologram meshes for empty artifact presentation slots.
/// Builds every MeshFilter/submesh from the required artifact visual.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class ArtifactPresentationSlotIndicators : MonoBehaviour
{
	const string IndicatorNamePrefix = "SlotIndicator_";

	static readonly int TintColorId = Shader.PropertyToID( "_TintColor" );
	static readonly Color CyanTint = new Color( 0.35f, 0.95f, 1.4f, 0.55f );
	static readonly Color DimCyanTint = new Color( 0.35f, 0.95f, 1.4f, 0.28f );

	[SerializeField]
	Material indicatorMaterial;

	[SerializeField]
	Mesh fallbackMesh;

	[SerializeField]
	bool showEditModePreviews = true;

	[Header( "Distance Fade" )]
	[Tooltip( "Fully opaque at this distance and closer (meters from the player)." )]
	[SerializeField]
	[Min( 0f )]
	float fadeNearDistance = 12f;

	[Tooltip( "Fully hidden beyond this distance. Default 15m." )]
	[SerializeField]
	[Min( 0f )]
	float fadeFarDistance = 15f;

	ArtifactPresentationTableInteractable _table;
	SlotVisual[] _slotVisuals;
	bool _rebuildQueued;
#if UNITY_EDITOR
	int _editModeRequirementFingerprint;
#endif

	struct SlotVisual
	{
		public GameObject Root;
		public MeshRenderer[] Renderers;
		public MaterialPropertyBlock PropertyBlock;
	}

	public bool ShowEditModePreviews
	{
		get => showEditModePreviews;
		set
		{
			showEditModePreviews = value;
			if ( !Application.isPlaying )
				QueueEditModeRebuild();
		}
	}

	public float FadeNearDistance => fadeNearDistance;
	public float FadeFarDistance => fadeFarDistance;

	public void Bind( ArtifactPresentationTableInteractable table )
	{
		_table = table;
		if ( _table != null && !_table.ShowSlotIndicators )
		{
			ClearForDisabledHolograms();
			return;
		}

		if ( Application.isPlaying )
			RebuildVisuals();
		else
			QueueEditModeRebuild();
	}

	public void ClearForDisabledHolograms()
	{
		ClearVisuals();
	}

	public void RefreshEditModePreviews()
	{
		if ( Application.isPlaying )
			return;

		QueueEditModeRebuild();
	}

	/// <summary>
	/// Removes tracked indicators and any orphaned SlotIndicator_* children left by failed rebuilds.
	/// </summary>
	public int PurgeOrphanIndicators()
	{
		return ClearVisuals();
	}

	void OnEnable()
	{
		if ( Application.isPlaying )
			return;

		ResolveTable();
		QueueEditModeRebuild();
	}

	void OnValidate()
	{
		fadeNearDistance = Mathf.Max( 0f, fadeNearDistance );
		fadeFarDistance = Mathf.Max( fadeNearDistance, fadeFarDistance );

		if ( Application.isPlaying )
			return;

		QueueEditModeRebuild();
	}

	void OnDestroy()
	{
		ClearVisuals();
	}

	void QueueEditModeRebuild()
	{
#if UNITY_EDITOR
		if ( _rebuildQueued )
			return;

		_rebuildQueued = true;
		EditorApplication.delayCall += ProcessQueuedEditModeRebuild;
#else
		RebuildVisuals();
#endif
	}

#if UNITY_EDITOR
	void ProcessQueuedEditModeRebuild()
	{
		_rebuildQueued = false;
		if ( this == null )
			return;

		if ( !CanMutateHierarchy() )
			return;

		ResolveTable();

		if ( !showEditModePreviews )
		{
			ClearVisuals();
			RememberEditModeFingerprint();
			return;
		}

		if ( _table != null )
			RebuildVisuals();
	}

	bool CanMutateHierarchy()
	{
		if ( PrefabUtility.IsPartOfPrefabAsset( this ) )
			return false;

		if ( !gameObject.scene.IsValid() )
			return false;

		return true;
	}
#endif

#if UNITY_EDITOR
	void Update()
	{
		if ( Application.isPlaying )
			return;

		TickEditModePreviewSync();
	}
#endif

	void LateUpdate()
	{
		if ( _table == null || _slotVisuals == null )
			return;

		if ( !Application.isPlaying )
			return;

		for ( int i = 0; i < _slotVisuals.Length; i++ )
			UpdateSlotVisualState( i );
	}

	public void RefreshSlot( int slotIndex, bool occupied )
	{
		if ( _slotVisuals == null || slotIndex < 0 || slotIndex >= _slotVisuals.Length )
			return;

		UpdateSlotVisualState( slotIndex );
	}

	public void RefreshAimFeedback()
	{
		if ( _slotVisuals == null )
			return;

		for ( int i = 0; i < _slotVisuals.Length; i++ )
			UpdateSlotVisualState( i );
	}

	void RebuildVisuals()
	{
#if UNITY_EDITOR
		if ( !Application.isPlaying && !CanMutateHierarchy() )
			return;
#endif
		ClearVisuals();
		ResolveTable();
		if ( _table == null )
		{
			RememberEditModeFingerprint();
			return;
		}

		if ( !_table.ShowSlotIndicators )
		{
			RememberEditModeFingerprint();
			return;
		}

		if ( !Application.isPlaying && !showEditModePreviews )
		{
			RememberEditModeFingerprint();
			return;
		}

		int count = _table.SlotCount;
		_slotVisuals = new SlotVisual[ count ];
		IReadOnlyList<ArtifactPresentationSlotEntry> slots = _table.Slots;
		if ( slots == null )
			return;

		for ( int i = 0; i < count; i++ )
		{
			ArtifactPresentationSlotEntry entry = slots[ i ];
			Transform anchor = entry.anchor;
			Transform parent = anchor != null ? anchor : _table.transform;

			GameObject root = new GameObject( IndicatorNamePrefix + i );
			root.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
			root.transform.SetParent( parent, false );
			root.transform.localPosition = Vector3.zero;
			root.transform.localRotation = ArtifactPresentationDisplayPose.GetSocketLocalRotation(
				_table.SocketRotation,
				entry.rotationOffset );

			_slotVisuals[ i ] = new SlotVisual
			{
				Root = root,
				Renderers = System.Array.Empty<MeshRenderer>(),
				PropertyBlock = new MaterialPropertyBlock()
			};

			TreasureDefinition required = _table.GetRequiredArtifact( i );
			if ( required != null )
			{
				if ( Application.isPlaying )
					StartCoroutine( LoadMeshRoutine( i, required, root.transform ) );
				else
					BuildVisualFromDefinition( i, required, root.transform );
			}
			else if ( fallbackMesh != null )
				AttachFallbackMesh( i, root.transform );

			ArtifactPresentationDisplayPose.ApplyDefinitionWorldScale( root.transform, required );
			UpdateSlotVisualState( i );
		}

		RememberEditModeFingerprint();
	}

	void RememberEditModeFingerprint()
	{
#if UNITY_EDITOR
		_editModeRequirementFingerprint = ComputeRequirementFingerprint();
#endif
	}

	void ResolveTable()
	{
		if ( _table != null )
			return;

		_table = GetComponent<ArtifactPresentationTableInteractable>();
		if ( _table == null )
			_table = GetComponentInParent<ArtifactPresentationTableInteractable>();
	}

#if UNITY_EDITOR
	void TickEditModePreviewSync()
	{
		if ( !CanMutateHierarchy() )
			return;

		ResolveTable();
		if ( _table == null )
			return;

		int fingerprint = ComputeRequirementFingerprint();
		if ( fingerprint == _editModeRequirementFingerprint )
			return;

		QueueEditModeRebuild();
	}

	int ComputeRequirementFingerprint()
	{
		if ( _table == null )
			return 0;

		unchecked
		{
			int hash = showEditModePreviews ? 1 : 0;
			hash = hash * 31 + _table.SlotCount;
			hash = hash * 31 + ( _table.SameArtifactForAllSlots ? 1 : 0 );
			hash = hash * 31 + DefinitionId( _table.SharedRequiredArtifact );
			hash = hash * 31 + _table.SocketRotation.GetHashCode();

			IReadOnlyList<ArtifactPresentationSlotEntry> slots = _table.Slots;
			if ( slots == null )
				return hash;

			for ( int i = 0; i < slots.Count; i++ )
			{
				hash = hash * 31 + DefinitionId( _table.GetRequiredArtifact( i ) );
				hash = hash * 31 + slots[ i ].rotationOffset.GetHashCode();
			}

			return hash;
		}
	}

	static int DefinitionId( TreasureDefinition definition )
	{
		return definition != null ? definition.GetInstanceID() : 0;
	}
#endif

	int ClearVisuals()
	{
		int removed = 0;
		if ( _slotVisuals != null )
		{
			for ( int i = 0; i < _slotVisuals.Length; i++ )
			{
				if ( _slotVisuals[ i ].Root != null )
				{
					DestroyVisualObject( _slotVisuals[ i ].Root );
					removed++;
				}
			}

			_slotVisuals = null;
		}

		removed += DestroyOrphanIndicatorObjects();
		return removed;
	}

	int DestroyOrphanIndicatorObjects()
	{
		Transform searchRoot = _table != null ? _table.transform : transform;
		if ( searchRoot == null )
			return 0;

		List<GameObject> orphans = new List<GameObject>( 16 );
		CollectOrphanIndicatorsRecursive( searchRoot, orphans );

		int removed = 0;
		for ( int i = 0; i < orphans.Count; i++ )
		{
			if ( orphans[ i ] == null )
				continue;

			DestroyVisualObject( orphans[ i ] );
			removed++;
		}

		return removed;
	}

	static void CollectOrphanIndicatorsRecursive( Transform parent, List<GameObject> results )
	{
		if ( parent == null || results == null )
			return;

		for ( int i = 0; i < parent.childCount; i++ )
		{
			Transform child = parent.GetChild( i );
			if ( child == null )
				continue;

			string childName = child.name;
			if ( childName != null && childName.StartsWith( IndicatorNamePrefix, System.StringComparison.Ordinal ) )
				results.Add( child.gameObject );
			else
				CollectOrphanIndicatorsRecursive( child, results );
		}
	}

	void BuildVisualFromDefinition( int slotIndex, TreasureDefinition definition, Transform indicatorRoot )
	{
		if ( definition == null || indicatorRoot == null )
			return;

		ArtifactPrefabMeshSnapshot snapshot = default;
#if UNITY_EDITOR
		AssetReferenceGameObject prefabRef = definition.prefab;
		if ( prefabRef != null && prefabRef.RuntimeKeyIsValid() )
		{
			string path = AssetDatabase.GUIDToAssetPath( prefabRef.AssetGUID );
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( path );
			snapshot = ArtifactPresentationPrefabSnapshot.ExtractFromPrefabRoot( prefab );
			ArtifactPresentationPrefabSnapshot.CacheSnapshot( definition, snapshot );
		}
#endif
		AttachSnapshotMeshes( slotIndex, snapshot, indicatorRoot );
	}

	IEnumerator LoadMeshRoutine( int slotIndex, TreasureDefinition definition, Transform indicatorRoot )
	{
		if ( definition == null || indicatorRoot == null )
			yield break;

		System.Threading.Tasks.Task<ArtifactPrefabMeshSnapshot> task =
			ArtifactPresentationPrefabSnapshot.LoadFromDefinitionAsync( definition );
		while ( !task.IsCompleted )
			yield return null;

		AttachSnapshotMeshes( slotIndex, task.Result, indicatorRoot );
		ArtifactPresentationDisplayPose.ApplyDefinitionWorldScale( indicatorRoot, definition );
		UpdateSlotVisualState( slotIndex );
	}

	void AttachSnapshotMeshes( int slotIndex, ArtifactPrefabMeshSnapshot snapshot, Transform indicatorRoot )
	{
		if ( indicatorRoot == null )
			return;

		List<MeshRenderer> renderers = new List<MeshRenderer>( 4 );

		if ( snapshot.HasParts )
		{
			Dictionary<string, List<ArtifactPrefabMeshPart>> byPose =
				new Dictionary<string, List<ArtifactPrefabMeshPart>>();
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

				if ( !byPose.TryGetValue( key, out List<ArtifactPrefabMeshPart> list ) )
				{
					list = new List<ArtifactPrefabMeshPart>( 2 );
					byPose[ key ] = list;
				}

				list.Add( part );
			}

			int visualIndex = 0;
			foreach ( KeyValuePair<string, List<ArtifactPrefabMeshPart>> pair in byPose )
			{
				List<ArtifactPrefabMeshPart> parts = pair.Value;
				ArtifactPrefabMeshPart first = parts[ 0 ];
				MeshRenderer renderer = CreateVisualRenderer(
					indicatorRoot,
					"Visual_" + visualIndex,
					first.Mesh,
					first.LocalPosition,
					first.LocalRotation,
					first.LocalScale,
					parts.Count );
				if ( renderer != null )
					renderers.Add( renderer );
				visualIndex++;
			}
		}
		else if ( fallbackMesh != null )
		{
			MeshRenderer renderer = CreateVisualRenderer(
				indicatorRoot,
				"Visual",
				fallbackMesh,
				Vector3.zero,
				Quaternion.identity,
				Vector3.one,
				1 );
			if ( renderer != null )
				renderers.Add( renderer );
		}

		if ( _slotVisuals != null && slotIndex >= 0 && slotIndex < _slotVisuals.Length )
		{
			SlotVisual visual = _slotVisuals[ slotIndex ];
			visual.Renderers = renderers.ToArray();
			_slotVisuals[ slotIndex ] = visual;
		}
	}

	MeshRenderer CreateVisualRenderer(
		Transform parent,
		string name,
		Mesh mesh,
		Vector3 localPosition,
		Quaternion localRotation,
		Vector3 localScale,
		int materialSlotCount )
	{
		if ( mesh == null || parent == null )
			return null;

		GameObject visualGo = new GameObject( name );
		visualGo.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
		visualGo.transform.SetParent( parent, false );
		visualGo.transform.localPosition = localPosition;
		visualGo.transform.localRotation = localRotation;
		visualGo.transform.localScale = localScale;

		MeshFilter filter = visualGo.AddComponent<MeshFilter>();
		filter.sharedMesh = mesh;
		MeshRenderer renderer = visualGo.AddComponent<MeshRenderer>();
		renderer.shadowCastingMode = ShadowCastingMode.Off;
		renderer.receiveShadows = false;

		int slotCount = Mathf.Max( 1, materialSlotCount );
		if ( indicatorMaterial != null )
		{
			Material[] mats = new Material[ slotCount ];
			for ( int i = 0; i < slotCount; i++ )
				mats[ i ] = indicatorMaterial;
			renderer.sharedMaterials = mats;
		}

		return renderer;
	}

	void AttachFallbackMesh( int slotIndex, Transform indicatorRoot )
	{
		MeshRenderer renderer = CreateVisualRenderer(
			indicatorRoot,
			"Visual",
			fallbackMesh,
			Vector3.zero,
			Quaternion.identity,
			Vector3.one,
			1 );

		if ( _slotVisuals != null && slotIndex >= 0 && slotIndex < _slotVisuals.Length )
		{
			SlotVisual visual = _slotVisuals[ slotIndex ];
			visual.Renderers = renderer != null
				? new[] { renderer }
				: System.Array.Empty<MeshRenderer>();
			_slotVisuals[ slotIndex ] = visual;
		}
	}

	void UpdateSlotVisualState( int slotIndex )
	{
		if ( _slotVisuals == null || slotIndex < 0 || slotIndex >= _slotVisuals.Length || _table == null )
			return;

		SlotVisual visual = _slotVisuals[ slotIndex ];
		if ( visual.Root == null )
			return;

		bool occupied = Application.isPlaying && _table.IsSlotOccupied( slotIndex );
		bool lockedByPrerequisite = Application.isPlaying && !_table.IsSlotPrerequisiteMet( slotIndex );
		bool hideForAimFeedback = Application.isPlaying
			&& _table.IsAimFeedbackFresh
			&& _table.AimedSlotIndex == slotIndex;

		float distanceFade = Application.isPlaying ? EvaluateDistanceFade( visual.Root.transform.position ) : 1f;

		// Hide the cyan base hologram while the placement ghost shows valid/invalid feedback,
		// when occupied, while a prerequisite slot is empty, or when fully faded by distance.
		if ( occupied || lockedByPrerequisite || hideForAimFeedback || distanceFade <= 0.001f )
		{
			visual.Root.SetActive( false );
			return;
		}

		visual.Root.SetActive( true );

		Color tint = CyanTint;
		if ( Application.isPlaying && _table.IsAimFeedbackFresh && _table.AimedSlotIndex >= 0 )
			tint = DimCyanTint;

		tint.a *= distanceFade;

		if ( visual.PropertyBlock == null )
			visual.PropertyBlock = new MaterialPropertyBlock();

		visual.PropertyBlock.SetColor( TintColorId, tint );

		MeshRenderer[] renderers = visual.Renderers;
		if ( renderers == null )
			return;

		for ( int r = 0; r < renderers.Length; r++ )
		{
			MeshRenderer renderer = renderers[ r ];
			if ( renderer == null )
				continue;

			int matCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 1;
			for ( int m = 0; m < matCount; m++ )
				renderer.SetPropertyBlock( visual.PropertyBlock, m );
		}
	}

	float EvaluateDistanceFade( Vector3 worldPos )
	{
		if ( !TryGetViewerPosition( out Vector3 viewerPos ) )
			return 0f;

		float near = fadeNearDistance;
		float far = Mathf.Max( near, fadeFarDistance );
		float distance = Vector3.Distance( viewerPos, worldPos );
		if ( distance <= near )
			return 1f;
		if ( distance >= far )
			return 0f;

		return 1f - Mathf.InverseLerp( near, far, distance );
	}

	static bool TryGetViewerPosition( out Vector3 position )
	{
		position = Vector3.zero;
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
		{
			position = GameMode.Instance.Player.transform.position;
			return true;
		}

		Camera camera = Camera.main;
		if ( camera == null )
			return false;

		position = camera.transform.position;
		return true;
	}

	void DestroyVisualObject( GameObject go )
	{
		if ( go == null )
			return;

		if ( Application.isPlaying )
		{
			Destroy( go );
			return;
		}

#if UNITY_EDITOR
		DestroyImmediate( go );
#endif
	}
}
