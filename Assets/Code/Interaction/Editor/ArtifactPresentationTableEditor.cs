using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( ArtifactPresentationTableInteractable ) )]
public class ArtifactPresentationTableEditor : Editor
{
	int _previewRelevantHash;

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		SerializedProperty sameArtifactProp = serializedObject.FindProperty( "sameArtifactForAllSlots" );
		SerializedProperty sharedRequiredProp = serializedObject.FindProperty( "sharedRequiredArtifact" );
		bool sameArtifactForAllSlots = sameArtifactProp != null && sameArtifactProp.boolValue;

		SerializedProperty iterator = serializedObject.GetIterator();
		bool enterChildren = true;
		while ( iterator.NextVisible( enterChildren ) )
		{
			enterChildren = false;
			if ( iterator.propertyPath == "m_Script" )
			{
				using ( new EditorGUI.DisabledScope( true ) )
					EditorGUILayout.PropertyField( iterator, true );
				continue;
			}

			if ( iterator.propertyPath == "sharedRequiredArtifact" )
			{
				if ( sameArtifactForAllSlots )
					EditorGUILayout.PropertyField( iterator, true );
				continue;
			}

			if ( iterator.propertyPath == "slots" )
			{
				if ( sameArtifactForAllSlots )
					DrawSlotsWithoutRequiredArtifact( iterator, sharedRequiredProp );
				else
					EditorGUILayout.PropertyField( iterator, true );
				continue;
			}

			EditorGUILayout.PropertyField( iterator, true );
			if ( iterator.propertyPath == "sameArtifactForAllSlots" )
				sameArtifactForAllSlots = iterator.boolValue;
		}

		serializedObject.ApplyModifiedProperties();

		ArtifactPresentationTableInteractable table = ( ArtifactPresentationTableInteractable )target;
		int previewHash = ComputePreviewRelevantHash();
		bool previewDirty = previewHash != _previewRelevantHash;
		_previewRelevantHash = previewHash;
		if ( table == null )
			return;

		ArtifactPresentationSlotIndicators indicators = table.GetComponent<ArtifactPresentationSlotIndicators>();
		if ( indicators == null )
			indicators = table.GetComponentInChildren<ArtifactPresentationSlotIndicators>();

		if ( indicators != null && previewDirty && !Application.isPlaying )
		{
			indicators.Bind( table );
			SceneView.RepaintAll();
		}

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
			string emptyMessage = table.SameArtifactForAllSlots
				? "Add at least one slot with an anchor."
				: "Add at least one slot with an anchor and required artifact.";
			EditorGUILayout.HelpBox( emptyMessage, MessageType.Warning );
			return;
		}

		if ( table.SameArtifactForAllSlots )
		{
			TreasureDefinition shared = table.SharedRequiredArtifact;
			if ( shared == null )
			{
				EditorGUILayout.HelpBox( "Assign a shared required artifact.", MessageType.Error );
			}
			else if ( shared.category != TreasureCategory.Artifact )
			{
				EditorGUILayout.HelpBox(
					"Shared required artifact \"" + shared.displayName + "\" is not TreasureCategory.Artifact.",
					MessageType.Warning );
			}
		}

		for ( int i = 0; i < slots.Count; i++ )
		{
			ArtifactPresentationSlotEntry entry = slots[ i ];
			if ( entry.anchor == null )
			{
				EditorGUILayout.HelpBox( "Slot " + i + ": missing anchor transform.", MessageType.Error );
				continue;
			}

			if ( table.SameArtifactForAllSlots )
				continue;

			TreasureDefinition required = table.GetRequiredArtifact( i );
			if ( required == null )
			{
				EditorGUILayout.HelpBox( "Slot " + i + ": assign a required artifact definition.", MessageType.Error );
				continue;
			}

			if ( required.category != TreasureCategory.Artifact )
			{
				EditorGUILayout.HelpBox(
					"Slot " + i + ": \"" + required.displayName + "\" is not TreasureCategory.Artifact.",
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

	static void DrawSlotsWithoutRequiredArtifact( SerializedProperty slotsProp, SerializedProperty sharedRequiredProp )
	{
		slotsProp.isExpanded = EditorGUILayout.Foldout( slotsProp.isExpanded, slotsProp.displayName, true );
		if ( !slotsProp.isExpanded )
			return;

		EditorGUI.indentLevel++;
		slotsProp.arraySize = EditorGUILayout.IntField( "Size", slotsProp.arraySize );
		for ( int i = 0; i < slotsProp.arraySize; i++ )
		{
			SerializedProperty element = slotsProp.GetArrayElementAtIndex( i );
			element.isExpanded = EditorGUILayout.Foldout( element.isExpanded, "Slot " + i, true );
			if ( !element.isExpanded )
				continue;

			EditorGUI.indentLevel++;
			SerializedProperty anchorProp = element.FindPropertyRelative( "anchor" );
			if ( anchorProp != null )
				EditorGUILayout.PropertyField( anchorProp, true );

			Object shared = sharedRequiredProp != null ? sharedRequiredProp.objectReferenceValue : null;
			using ( new EditorGUI.DisabledScope( true ) )
				EditorGUILayout.ObjectField( "Required Artifact", shared, typeof( TreasureDefinition ), false );

			SerializedProperty rotationProp = element.FindPropertyRelative( "rotationOffset" );
			if ( rotationProp != null )
				EditorGUILayout.PropertyField( rotationProp, true );
			EditorGUI.indentLevel--;
		}

		EditorGUI.indentLevel--;
	}

	int ComputePreviewRelevantHash()
	{
		unchecked
		{
			int hash = 17;
			hash = MixBool( hash, serializedObject.FindProperty( "sameArtifactForAllSlots" ) );
			hash = MixObject( hash, serializedObject.FindProperty( "sharedRequiredArtifact" ) );
			hash = MixVector( hash, serializedObject.FindProperty( "socketRotation" ) );

			SerializedProperty slotsProp = serializedObject.FindProperty( "slots" );
			if ( slotsProp == null )
				return hash;

			hash = hash * 31 + slotsProp.arraySize;
			for ( int i = 0; i < slotsProp.arraySize; i++ )
			{
				SerializedProperty element = slotsProp.GetArrayElementAtIndex( i );
				hash = MixObject( hash, element.FindPropertyRelative( "anchor" ) );
				hash = MixObject( hash, element.FindPropertyRelative( "requiredArtifact" ) );
				hash = MixVector( hash, element.FindPropertyRelative( "rotationOffset" ) );
			}

			return hash;
		}
	}

	static int MixBool( int hash, SerializedProperty prop )
	{
		return hash * 31 + ( prop != null && prop.boolValue ? 1 : 0 );
	}

	static int MixObject( int hash, SerializedProperty prop )
	{
		Object value = prop != null ? prop.objectReferenceValue : null;
		return hash * 31 + ( value != null ? value.GetInstanceID() : 0 );
	}

	static int MixVector( int hash, SerializedProperty prop )
	{
		return hash * 31 + ( prop != null ? prop.vector3Value.GetHashCode() : 0 );
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
