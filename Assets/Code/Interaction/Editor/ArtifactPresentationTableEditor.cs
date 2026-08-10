using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( ArtifactPresentationTableInteractable ) )]
public class ArtifactPresentationTableEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		ArtifactPresentationTableInteractable table = ( ArtifactPresentationTableInteractable )target;
		if ( table == null )
			return;

		ArtifactPresentationSlotIndicators indicators = table.GetComponent<ArtifactPresentationSlotIndicators>();
		if ( indicators == null )
			indicators = table.GetComponentInChildren<ArtifactPresentationSlotIndicators>();

		if ( indicators != null )
		{
			EditorGUILayout.Space( 8f );
			EditorGUILayout.LabelField( "Edit-mode previews", EditorStyles.boldLabel );

			SerializedObject indicatorsSo = new SerializedObject( indicators );
			SerializedProperty showProp = indicatorsSo.FindProperty( "showEditModePreviews" );
			if ( showProp != null )
			{
				EditorGUILayout.PropertyField( showProp, new GUIContent( "Show slot previews" ) );
				indicatorsSo.ApplyModifiedProperties();
			}

			EditorGUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Refresh slot previews" ) )
			{
				indicators.PurgeOrphanIndicators();
				indicators.Bind( table );
				SceneView.RepaintAll();
			}

			if ( GUILayout.Button( "Purge orphan indicators" ) )
			{
				int removed = indicators.PurgeOrphanIndicators();
				Debug.Log( "Purged " + removed + " SlotIndicator orphan(s) on '" + table.name + "'." );
				SceneView.RepaintAll();
			}
			EditorGUILayout.EndHorizontal();

			if ( GUILayout.Button( "Ensure slot aim volumes" ) )
			{
				EnsureSlotVolumesOnTable( table );
				EditorUtility.SetDirty( table );
			}
		}

		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Slot validation", EditorStyles.boldLabel );

		IReadOnlyList<ArtifactPresentationSlotEntry> slots = table.Slots;
		if ( slots == null || slots.Count == 0 )
		{
			EditorGUILayout.HelpBox( "Add at least one slot with an anchor and required artifact.", MessageType.Warning );
			return;
		}

		for ( int i = 0; i < slots.Count; i++ )
		{
			ArtifactPresentationSlotEntry entry = slots[ i ];
			if ( entry.anchor == null )
			{
				EditorGUILayout.HelpBox( "Slot " + i + ": missing anchor transform.", MessageType.Error );
				continue;
			}

			if ( entry.requiredArtifact == null )
			{
				EditorGUILayout.HelpBox( "Slot " + i + ": assign a required artifact definition.", MessageType.Error );
				continue;
			}

			if ( entry.requiredArtifact.category != TreasureCategory.Artifact )
			{
				EditorGUILayout.HelpBox(
					"Slot " + i + ": \"" + entry.requiredArtifact.displayName + "\" is not TreasureCategory.Artifact.",
					MessageType.Warning );
			}
		}
	}

	[MenuItem( DragonLootMenus.TreasurePurgeSlotOrphans )]
	static void PurgeAllSceneOrphans()
	{
		ArtifactPresentationSlotIndicators[] all = Object.FindObjectsByType<ArtifactPresentationSlotIndicators>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );

		int removed = 0;
		for ( int i = 0; i < all.Length; i++ )
		{
			if ( all[ i ] == null )
				continue;
			if ( PrefabUtility.IsPartOfPrefabAsset( all[ i ] ) )
				continue;

			removed += all[ i ].PurgeOrphanIndicators();
		}

		Debug.Log( "Purged " + removed + " SlotIndicator orphan(s) from open scenes." );
		SceneView.RepaintAll();
	}

	static void EnsureSlotVolumesOnTable( ArtifactPresentationTableInteractable table )
	{
		if ( table == null )
			return;

		IReadOnlyList<ArtifactPresentationSlotEntry> slots = table.Slots;
		if ( slots == null )
			return;

		const float volumeSize = 0.22f;
		const float volumeHeight = 0.06f;

		for ( int i = 0; i < slots.Count; i++ )
		{
			Transform anchor = slots[ i ].anchor;
			if ( anchor == null )
				continue;

			BoxCollider box = anchor.GetComponent<BoxCollider>();
			if ( box == null )
			{
				box = anchor.gameObject.AddComponent<BoxCollider>();
				box.center = Vector3.zero;
				box.size = new Vector3( volumeSize, volumeHeight, volumeSize );
			}

			ArtifactPresentationSlotVolume volume = anchor.GetComponent<ArtifactPresentationSlotVolume>();
			if ( volume == null )
				volume = anchor.gameObject.AddComponent<ArtifactPresentationSlotVolume>();
			volume.Configure( table, i );
		}
	}
}
