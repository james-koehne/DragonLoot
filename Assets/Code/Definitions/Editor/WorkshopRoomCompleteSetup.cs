#if UNITY_EDITOR
using System.Collections.Generic;

using FeedbackSystem;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Idempotent setup for workshop room-complete world event, catalog entry, and scene gleam.
/// </summary>
public static class WorkshopRoomCompleteSetup
{
	const string EventPath = "Assets/Definitions/WorldEvents/WorldEvent_WorkshopRoomComplete.asset";
	const string CatalogPath = "Assets/Definitions/WorldEventCatalogDefinition.asset";
	const string ObjectivePath = "Assets/Definitions/Objectives/Objective_WorkshopClear.asset";
	const string MatPath = "Assets/Materials/Shaders/FloorGleam/M_FloorGleam.mat";
	const string EventId = "workshop_room_complete";
	const string HammerEventId = "workshop_access_hammer_delivery";
	const string LanternGroup = "workshop_lanterns";
	const string VolumeId = "volume_workshop";
	const string RootName = "WorkshopRoomComplete";

	[MenuItem( DragonLootMenus.DefinitionsEnsureWorkshopRoomComplete )]
	static void MenuEnsure()
	{
		EnsureAssets();
		EnsureScene();
		Debug.Log( "Workshop room complete assets + scene gleam ensured." );
	}

	static void EnsureAssets()
	{
		WorldEventDefinition worldEvent = AssetDatabase.LoadAssetAtPath<WorldEventDefinition>( EventPath );
		if ( worldEvent == null )
		{
			worldEvent = ScriptableObject.CreateInstance<WorldEventDefinition>();
			worldEvent.id = EventId;
			worldEvent.tags = new[] { "workshop", "room_complete" };
			worldEvent.manualOnly = true;
			worldEvent.actions = new[]
			{
				new WorldEventAction
				{
					type = WorldEventActionType.IgniteLanterns,
					lanternGroupId = LanternGroup,
					lanternIgniteFadeDuration = 0.75f
				}
			};
			AssetDatabase.CreateAsset( worldEvent, EventPath );
		}

		AddressableEditorUtil.TryRegister( EventPath, EventPath, "Definition" );

		WorldEventCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<WorldEventCatalogDefinition>( CatalogPath );
		if ( catalog != null )
		{
			if ( catalog.events == null )
				catalog.events = new List<WorldEventDefinition>();

			bool inCatalog = false;
			for ( int i = 0; i < catalog.events.Count; i++ )
			{
				WorldEventDefinition entry = catalog.events[ i ];
				if ( entry == worldEvent || ( entry != null && entry.id == EventId ) )
				{
					catalog.events[ i ] = worldEvent;
					inCatalog = true;
					break;
				}
			}

			if ( !inCatalog )
				catalog.events.Add( worldEvent );

			EditorUtility.SetDirty( catalog );
		}

		ObjectiveDefinition objective = AssetDatabase.LoadAssetAtPath<ObjectiveDefinition>( ObjectivePath );
		if ( objective != null )
		{
			List<string> ids = new List<string>();
			if ( objective.onCompleteWorldEventIds != null )
				ids.AddRange( objective.onCompleteWorldEventIds );

			// Hammer delivery is condition-driven (Access + enter workshop), not Clear complete.
			ids.RemoveAll( id => id == EventId || id == HammerEventId );
			ids.Insert( 0, EventId );
			objective.onCompleteWorldEventIds = ids.ToArray();
			EditorUtility.SetDirty( objective );
		}

		AssetDatabase.SaveAssets();
	}

	static void EnsureScene()
	{
		Material gleamMat = AssetDatabase.LoadAssetAtPath<Material>( MatPath );
		if ( gleamMat == null )
		{
			Debug.LogWarning( "WorkshopRoomCompleteSetup: missing " + MatPath );
			return;
		}

		QuestVolume volume = FindWorkshopVolume();
		if ( volume == null )
		{
			Debug.LogWarning( "WorkshopRoomCompleteSetup: no volume_workshop in open scenes." );
			return;
		}

		BoxCollider box = volume.GetComponent<BoxCollider>();
		Bounds bounds = box != null
			? new Bounds( volume.transform.TransformPoint( box.center ), Vector3.Scale( box.size, volume.transform.lossyScale ) )
			: new Bounds( volume.transform.position, new Vector3( 20f, 4f, 20f ) );

		Scene scene = volume.gameObject.scene;
		GameObject existing = FindNamedInScene( scene, RootName );
		if ( existing != null )
			Undo.DestroyObjectImmediate( existing );

		float floorY = volume.transform.position.y + 0.08f;
		Vector3 rayOrigin = volume.transform.position + Vector3.up * 5f;
		if ( Physics.Raycast( rayOrigin, Vector3.down, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore ) )
			floorY = hit.point.y + 0.08f;

		GameObject root = new GameObject( RootName );
		SceneManager.MoveGameObjectToScene( root, scene );
		Undo.RegisterCreatedObjectUndo( root, RootName );
		root.transform.position = new Vector3( bounds.center.x, floorY, bounds.center.z );
		bool sweepAlongZ = bounds.size.z >= bounds.size.x;
		root.transform.rotation = Quaternion.LookRotation( sweepAlongZ ? Vector3.forward : Vector3.right, Vector3.up );

		GameObject plane = GameObject.CreatePrimitive( PrimitiveType.Quad );
		Undo.RegisterCreatedObjectUndo( plane, "FloorGleamOverlay" );
		plane.name = "FloorGleamOverlay";
		plane.transform.SetParent( root.transform, false );
		plane.transform.localRotation = Quaternion.Euler( 90f, 0f, 0f );
		float width = Mathf.Max( bounds.size.x, 4f );
		float depth = Mathf.Max( bounds.size.z, 4f );
		plane.transform.localScale = new Vector3( width, depth, 1f );

		Collider col = plane.GetComponent<Collider>();
		if ( col != null )
			Object.DestroyImmediate( col );

		MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
		meshRenderer.sharedMaterial = gleamMat;
		meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		meshRenderer.receiveShadows = false;
		meshRenderer.enabled = false;

		RoomFloorGleamController gleam = root.AddComponent<RoomFloorGleamController>();
		gleam.EditorAssign( meshRenderer, root.transform, 1.5f, 0.2f, new Color( 1.35f, 0.95f, 0.45f, 1f ) );

		Feedbacks feedbacks = root.AddComponent<Feedbacks>();
		FloorGleamFeedback gleamFeedback = new FloorGleamFeedback();
		gleamFeedback.Controller = gleam;
		feedbacks.AddFeedback( gleamFeedback );

		WorldEventFeedbackPlayer player = root.AddComponent<WorldEventFeedbackPlayer>();
		player.EditorAssign( EventId, feedbacks );

		ConfigureWorkshopLanterns( bounds );
		EditorSceneManager.MarkSceneDirty( scene );
	}

	static QuestVolume FindWorkshopVolume()
	{
		QuestVolume[] volumes = Object.FindObjectsByType<QuestVolume>( FindObjectsInactive.Exclude );
		QuestVolume best = null;
		float bestArea = -1f;
		for ( int i = 0; i < volumes.Length; i++ )
		{
			QuestVolume v = volumes[ i ];
			if ( v == null || v.Id != VolumeId )
				continue;

			BoxCollider box = v.GetComponent<BoxCollider>();
			float area = box != null ? Mathf.Abs( box.size.x * box.size.z ) : 1f;
			if ( area <= bestArea )
				continue;

			bestArea = area;
			best = v;
		}

		return best;
	}

	static GameObject FindNamedInScene( Scene scene, string name )
	{
		GameObject[] roots = scene.GetRootGameObjects();
		for ( int i = 0; i < roots.Length; i++ )
		{
			Transform[] all = roots[ i ].GetComponentsInChildren<Transform>( true );
			for ( int j = 0; j < all.Length; j++ )
			{
				if ( all[ j ] != null && all[ j ].name == name )
					return all[ j ].gameObject;
			}
		}

		return null;
	}

	static void ConfigureWorkshopLanterns( Bounds bounds )
	{
		LanternActivator[] lanterns = Object.FindObjectsByType<LanternActivator>( FindObjectsInactive.Exclude );
		Bounds padded = bounds;
		padded.Expand( new Vector3( 2f, 20f, 2f ) );
		for ( int i = 0; i < lanterns.Length; i++ )
		{
			LanternActivator lantern = lanterns[ i ];
			if ( lantern == null || !padded.Contains( lantern.transform.position ) )
				continue;

			Undo.RecordObject( lantern, "Workshop lanterns Manual" );
			lantern.EditorAssignManualGroup( LanternGroup );
			EditorUtility.SetDirty( lantern );
		}
	}
}
#endif
