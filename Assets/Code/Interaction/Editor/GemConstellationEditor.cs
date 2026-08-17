using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( GemConstellationInteractable ) )]
public class GemConstellationEditor : Editor
{
	const float EdgePickDistance = 14f;
	const float EdgeButtonSize = 0.045f;
	const float NodeHandleSize = 0.07f;
	const float SelectedNodeHandleSize = 0.1f;

	enum EdgeVisualState
	{
		None,
		Auto,
		Forced,
		Excluded
	}

	GemConstellationInteractable _target;
	SerializedProperty _acceptanceMode;
	SerializedProperty _defaultAcceptedGem;
	SerializedProperty _slots;
	SerializedProperty _forcedConnections;
	SerializedProperty _excludedConnections;
	SerializedProperty _fillSlotsOnStart;
	SerializedProperty _slotFillChance;
	SerializedProperty _minFilledSlots;
	SerializedProperty _maxFilledSlots;
	SerializedProperty _randomGemPool;
	SerializedProperty _fillSeed;
	SerializedProperty _snapDuration;
	SerializedProperty _bounceScale;
	SerializedProperty _gemSocketRotation;
	SerializedProperty _countLabel;
	SerializedProperty _completedHighlight;
	SerializedProperty _lineVisual;
	SerializedProperty _drawGizmosAlways;

	int _selectedSlotIndex = -1;

	void OnEnable()
	{
		_target = ( GemConstellationInteractable )target;
		_acceptanceMode = serializedObject.FindProperty( "acceptanceMode" );
		_defaultAcceptedGem = serializedObject.FindProperty( "defaultAcceptedGem" );
		_slots = serializedObject.FindProperty( "slots" );
		_forcedConnections = serializedObject.FindProperty( "forcedConnections" );
		_excludedConnections = serializedObject.FindProperty( "excludedConnections" );
		_fillSlotsOnStart = serializedObject.FindProperty( "fillSlotsOnStart" );
		_slotFillChance = serializedObject.FindProperty( "slotFillChance" );
		_minFilledSlots = serializedObject.FindProperty( "minFilledSlots" );
		_maxFilledSlots = serializedObject.FindProperty( "maxFilledSlots" );
		_randomGemPool = serializedObject.FindProperty( "randomGemPool" );
		_fillSeed = serializedObject.FindProperty( "fillSeed" );
		_snapDuration = serializedObject.FindProperty( "snapDuration" );
		_bounceScale = serializedObject.FindProperty( "bounceScale" );
		_gemSocketRotation = serializedObject.FindProperty( "gemSocketRotation" );
		_countLabel = serializedObject.FindProperty( "countLabel" );
		_completedHighlight = serializedObject.FindProperty( "completedHighlight" );
		_lineVisual = serializedObject.FindProperty( "lineVisual" );
		_drawGizmosAlways = serializedObject.FindProperty( "drawGizmosAlways" );
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		EditorGUILayout.PropertyField( _acceptanceMode );
		if ( _acceptanceMode.enumValueIndex != ( int )GemConstellationAcceptanceMode.PerSlot )
			EditorGUILayout.PropertyField( _defaultAcceptedGem );

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "Nodes", EditorStyles.boldLabel );
		DrawNodeToolbar();
		EditorGUILayout.PropertyField( _slots, true );

		EditorGUILayout.PropertyField( _forcedConnections, true );
		EditorGUILayout.PropertyField( _excludedConnections, true );

		if ( _acceptanceMode.enumValueIndex == ( int )GemConstellationAcceptanceMode.PerSlot )
			DrawPerSlotWarnings();

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "Start Fill", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( _fillSlotsOnStart );
		EditorGUILayout.PropertyField( _slotFillChance );
		EditorGUILayout.PropertyField( _minFilledSlots );
		EditorGUILayout.PropertyField( _maxFilledSlots );
		EditorGUILayout.PropertyField( _randomGemPool, true );
		EditorGUILayout.PropertyField( _fillSeed );
		if ( _fillSlotsOnStart.boolValue
			&& _acceptanceMode.enumValueIndex == ( int )GemConstellationAcceptanceMode.AnyGem
			&& _randomGemPool.arraySize == 0 )
		{
			EditorGUILayout.HelpBox(
				"Assign Random Gem Pool for mixed gems, or a Default Accepted Gem as a single-type fallback.",
				MessageType.Info );
		}

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "Placement", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( _snapDuration );
		EditorGUILayout.PropertyField( _bounceScale );
		EditorGUILayout.PropertyField( _gemSocketRotation );

		EditorGUILayout.Space();
		EditorGUILayout.LabelField( "Feedback", EditorStyles.boldLabel );
		EditorGUILayout.PropertyField( _countLabel );
		EditorGUILayout.PropertyField( _completedHighlight );
		EditorGUILayout.PropertyField( _lineVisual );
		EditorGUILayout.PropertyField( _drawGizmosAlways );

		EditorGUILayout.Space();
		if ( GUILayout.Button( "Rebuild Connections" ) )
		{
			Undo.RecordObject( _target, "Rebuild Constellation Connections" );
			_target.RebuildConnections();
			RefreshLineVisual();
			EditorUtility.SetDirty( _target );
		}

		EditorGUILayout.HelpBox(
			"Scene view:\n" +
			"• Drag a node handle to move it\n" +
			"• Click a node to select it\n" +
			"• Ctrl+Click empty space to add a node (auto-links to the previous last node)\n" +
			"• Delete / Backspace removes the selected node\n" +
			"• Click a mid-edge button to cycle: Auto/Forced → Off, or Link → Forced\n" +
			"• With a node selected, ghost links appear to every other node",
			MessageType.Info );

		serializedObject.ApplyModifiedProperties();
	}

	void DrawNodeToolbar()
	{
		EditorGUILayout.BeginHorizontal();

		if ( GUILayout.Button( "Add Node", GUILayout.Height( 22f ) ) )
		{
			AddSlot( GetDefaultNewSlotWorldPosition() );
			GUIUtility.ExitGUI();
		}

		EditorGUI.BeginDisabledGroup( _selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.arraySize );
		if ( GUILayout.Button( "Remove Selected", GUILayout.Height( 22f ) ) )
		{
			RemoveSlot( _selectedSlotIndex );
			GUIUtility.ExitGUI();
		}
		EditorGUI.EndDisabledGroup();

		EditorGUILayout.EndHorizontal();

		if ( _selectedSlotIndex >= 0 && _selectedSlotIndex < _slots.arraySize )
			EditorGUILayout.LabelField( "Selected", "Node " + _selectedSlotIndex );
		else
			EditorGUILayout.LabelField( "Selected", "None" );
	}

	void OnSceneGUI()
	{
		if ( _target == null )
			return;

		serializedObject.Update();
		_target.RebuildConnections();

		DrawConnectionHandles();
		DrawSlotHandles();
		HandleSceneInput();

		serializedObject.ApplyModifiedProperties();
	}

	void DrawSlotHandles()
	{
		int count = _slots.arraySize;
		for ( int i = 0; i < count; i++ )
		{
			Transform anchor = GetSlotAnchor( i );
			if ( anchor == null )
				continue;

			bool selected = i == _selectedSlotIndex;
			Vector3 pos = anchor.position;
			float size = selected ? SelectedNodeHandleSize : NodeHandleSize;

			Handles.color = selected
				? new Color( 1f, 0.85f, 0.2f, 1f )
				: ( _target.IsSlotOccupied( i )
					? new Color( 0.2f, 1f, 0.55f, 0.95f )
					: new Color( 0.55f, 0.75f, 1f, 0.85f ) );

			Handles.Label( pos + Vector3.up * ( size + 0.02f ), selected ? "Node " + i + " ★" : "Node " + i );

			if ( selected )
			{
				Handles.SphereHandleCap( 0, pos, Quaternion.identity, size, EventType.Repaint );

				EditorGUI.BeginChangeCheck();
				Quaternion handleRot = Tools.pivotRotation == PivotRotation.Local
					? anchor.rotation
					: Quaternion.identity;
				Vector3 newPos = Handles.PositionHandle( pos, handleRot );
				if ( EditorGUI.EndChangeCheck() )
				{
					Undo.RecordObject( anchor, "Move Constellation Node" );
					anchor.position = newPos;
					EditorUtility.SetDirty( anchor );
					_target.RebuildConnections();
					RefreshLineVisual();
					EditorUtility.SetDirty( _target );
				}
			}
			else if ( Handles.Button( pos, Quaternion.identity, size, size, Handles.SphereHandleCap ) )
			{
				_selectedSlotIndex = i;
				Repaint();
			}
		}
	}

	void DrawConnectionHandles()
	{
		int count = _slots.arraySize;
		if ( count < 2 )
			return;

		bool focusSelected = _selectedSlotIndex >= 0 && _selectedSlotIndex < count;

		for ( int i = 0; i < count; i++ )
		{
			for ( int j = i + 1; j < count; j++ )
			{
				EdgeVisualState state = GetEdgeState( i, j );
				bool involvesSelected = focusSelected
					&& ( i == _selectedSlotIndex || j == _selectedSlotIndex );

				// Always show live/forced/excluded edges.
				// Ghost "Link" edges only appear from the selected node.
				if ( state == EdgeVisualState.None && !involvesSelected )
					continue;

				DrawEdgeVisual( i, j );
			}
		}
	}

	void DrawEdgeVisual( int slotA, int slotB )
	{
		_target.GetSlotWorldPose( slotA, out Vector3 a, out _ );
		_target.GetSlotWorldPose( slotB, out Vector3 b, out _ );
		Vector3 mid = ( a + b ) * 0.5f;
		EdgeVisualState state = GetEdgeState( slotA, slotB );

		switch ( state )
		{
			case EdgeVisualState.Forced:
				Handles.color = new Color( 1f, 0.55f, 0.15f, 0.95f );
				Handles.DrawLine( a, b, 3f );
				break;
			case EdgeVisualState.Auto:
				Handles.color = new Color( 0.35f, 0.85f, 1f, 0.85f );
				Handles.DrawLine( a, b, 2f );
				break;
			case EdgeVisualState.Excluded:
				Handles.color = new Color( 1f, 0.25f, 0.25f, 0.7f );
				Handles.DrawDottedLine( a, b, 4f );
				break;
			default:
				Handles.color = new Color( 0.7f, 0.7f, 0.75f, 0.35f );
				Handles.DrawDottedLine( a, b, 3f );
				break;
		}

		float size = EdgeButtonSize * HandleUtility.GetHandleSize( mid );
		Handles.color = GetEdgeButtonColor( state );
		if ( Handles.Button( mid, Quaternion.identity, size, size, Handles.DotHandleCap ) )
			CycleEdgeConnection( slotA, slotB );

		string label = state switch
		{
			EdgeVisualState.Forced => "Forced",
			EdgeVisualState.Auto => "Auto",
			EdgeVisualState.Excluded => "Off",
			_ => "Link",
		};
		Handles.Label( mid + Vector3.up * ( size + 0.01f ), label );
	}

	static Color GetEdgeButtonColor( EdgeVisualState state )
	{
		switch ( state )
		{
			case EdgeVisualState.Forced:
				return new Color( 1f, 0.55f, 0.15f, 1f );
			case EdgeVisualState.Auto:
				return new Color( 0.35f, 0.95f, 1f, 1f );
			case EdgeVisualState.Excluded:
				return new Color( 1f, 0.3f, 0.3f, 1f );
			default:
				return new Color( 0.85f, 0.85f, 0.9f, 0.85f );
		}
	}

	EdgeVisualState GetEdgeState( int slotA, int slotB )
	{
		if ( _target.IsPairExcluded( slotA, slotB ) )
			return EdgeVisualState.Excluded;
		if ( _target.IsPairForced( slotA, slotB ) )
			return EdgeVisualState.Forced;

		IReadOnlyList<GemConstellationResolvedConnection> resolved = _target.ResolvedConnections;
		for ( int i = 0; i < resolved.Count; i++ )
		{
			if ( ( resolved[ i ].SlotA == slotA && resolved[ i ].SlotB == slotB )
				|| ( resolved[ i ].SlotA == slotB && resolved[ i ].SlotB == slotA ) )
			{
				return resolved[ i ].IsForced ? EdgeVisualState.Forced : EdgeVisualState.Auto;
			}
		}

		return EdgeVisualState.None;
	}

	void CycleEdgeConnection( int slotA, int slotB )
	{
		Undo.RecordObject( _target, "Toggle Constellation Connection" );

		EdgeVisualState state = GetEdgeState( slotA, slotB );
		switch ( state )
		{
			case EdgeVisualState.None:
				// Non-chain pair: force an extra link.
				if ( !_target.IsPairForced( slotA, slotB ) )
					_target.ToggleForcedConnection( slotA, slotB );
				break;

			case EdgeVisualState.Auto:
				// Chain neighbor: turn off.
				if ( !_target.IsPairExcluded( slotA, slotB ) )
					_target.ToggleExcludedConnection( slotA, slotB );
				break;

			case EdgeVisualState.Forced:
				if ( _target.IsChainNeighbor( slotA, slotB ) )
				{
					// Chain + forced → Off
					if ( _target.IsPairForced( slotA, slotB ) )
						_target.ToggleForcedConnection( slotA, slotB );
					if ( !_target.IsPairExcluded( slotA, slotB ) )
						_target.ToggleExcludedConnection( slotA, slotB );
				}
				else
				{
					// Extra link → remove (back to None)
					if ( _target.IsPairForced( slotA, slotB ) )
						_target.ToggleForcedConnection( slotA, slotB );
				}
				break;

			case EdgeVisualState.Excluded:
				// Off → restore (Auto for chain neighbors, None otherwise)
				if ( _target.IsPairExcluded( slotA, slotB ) )
					_target.ToggleExcludedConnection( slotA, slotB );
				break;
		}

		serializedObject.Update();
		RefreshLineVisual();
		EditorUtility.SetDirty( _target );
		SceneView.RepaintAll();
		Repaint();
	}

	void HandleSceneInput()
	{
		Event e = Event.current;
		if ( e == null )
			return;

		if ( e.type == EventType.KeyDown
			&& ( e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace )
			&& _selectedSlotIndex >= 0
			&& _selectedSlotIndex < _slots.arraySize )
		{
			RemoveSlot( _selectedSlotIndex );
			e.Use();
			return;
		}

		if ( e.type != EventType.MouseDown || e.button != 0 || e.alt )
			return;

		// Ctrl+Click: add a node at the cursor on the constellation plane.
		if ( e.control || e.command )
		{
			if ( TryGetPlacementPoint( e.mousePosition, out Vector3 worldPos ) )
			{
				AddSlot( worldPos );
				e.Use();
			}
			return;
		}

		if ( TryPickSlot( e.mousePosition, out int slotIndex ) )
		{
			_selectedSlotIndex = slotIndex;
			e.Use();
			Repaint();
			return;
		}

		HandleEdgeInput( e );
	}

	void HandleEdgeInput( Event e )
	{
		int count = _slots.arraySize;
		if ( count < 2 )
			return;

		Vector2 mouse = e.mousePosition;
		float bestDist = EdgePickDistance;
		int bestA = -1;
		int bestB = -1;

		for ( int i = 0; i < count; i++ )
		{
			for ( int j = i + 1; j < count; j++ )
			{
				EdgeVisualState state = GetEdgeState( i, j );
				bool focusSelected = _selectedSlotIndex >= 0 && _selectedSlotIndex < count;
				bool involvesSelected = focusSelected
					&& ( i == _selectedSlotIndex || j == _selectedSlotIndex );
				if ( state == EdgeVisualState.None && !involvesSelected )
					continue;

				_target.GetSlotWorldPose( i, out Vector3 a, out _ );
				_target.GetSlotWorldPose( j, out Vector3 b, out _ );
				Vector2 guiA = HandleUtility.WorldToGUIPoint( a );
				Vector2 guiB = HandleUtility.WorldToGUIPoint( b );
				float dist = HandleUtility.DistancePointToLineSegment( mouse, guiA, guiB );
				if ( dist < bestDist )
				{
					bestDist = dist;
					bestA = i;
					bestB = j;
				}
			}
		}

		if ( bestA < 0 )
			return;

		CycleEdgeConnection( bestA, bestB );
		e.Use();
	}

	bool TryPickSlot( Vector2 mousePosition, out int slotIndex )
	{
		slotIndex = -1;
		const float screenPickRadius = 18f;
		float bestDist = screenPickRadius;

		for ( int i = 0; i < _slots.arraySize; i++ )
		{
			Transform anchor = GetSlotAnchor( i );
			if ( anchor == null )
				continue;

			float score = Vector2.Distance( mousePosition, HandleUtility.WorldToGUIPoint( anchor.position ) );
			if ( score < bestDist )
			{
				bestDist = score;
				slotIndex = i;
			}
		}

		return slotIndex >= 0;
	}

	bool TryGetPlacementPoint( Vector2 mousePosition, out Vector3 worldPos )
	{
		Ray ray = HandleUtility.GUIPointToWorldRay( mousePosition );
		Plane plane = new Plane( _target.transform.forward, _target.transform.position );
		if ( plane.Raycast( ray, out float enter ) )
		{
			worldPos = ray.GetPoint( enter );
			return true;
		}

		worldPos = _target.transform.TransformPoint( new Vector3( 0f, 0.75f, 0.02f ) );
		return true;
	}

	Vector3 GetDefaultNewSlotWorldPosition()
	{
		if ( _slots.arraySize == 0 )
			return _target.transform.TransformPoint( new Vector3( 0f, 0.75f, 0.02f ) );

		Vector3 sum = Vector3.zero;
		int counted = 0;
		for ( int i = 0; i < _slots.arraySize; i++ )
		{
			Transform anchor = GetSlotAnchor( i );
			if ( anchor == null )
				continue;
			sum += anchor.position;
			counted++;
		}

		if ( counted == 0 )
			return _target.transform.TransformPoint( new Vector3( 0f, 0.75f, 0.02f ) );

		Vector3 center = sum / counted;
		return center + _target.transform.right * 0.15f;
	}

	void AddSlot( Vector3 worldPosition )
	{
		serializedObject.Update();

		Transform slotsRoot = FindOrCreateSlotsRoot();
		int index = _slots.arraySize;
		GameObject slotGo = new GameObject( "Slot_" + index );
		Undo.RegisterCreatedObjectUndo( slotGo, "Add Constellation Node" );
		slotGo.transform.SetParent( slotsRoot, false );
		slotGo.transform.position = worldPosition;
		slotGo.transform.rotation = _target.transform.rotation;

		_slots.arraySize = index + 1;
		SerializedProperty element = _slots.GetArrayElementAtIndex( index );
		element.FindPropertyRelative( "anchor" ).objectReferenceValue = slotGo.transform;
		element.FindPropertyRelative( "overrideGem" ).objectReferenceValue = null;
		element.FindPropertyRelative( "gemRotationOffset" ).vector3Value = Vector3.zero;

		serializedObject.ApplyModifiedProperties();
		_selectedSlotIndex = index;

		Undo.RecordObject( _target, "Add Constellation Node" );
		_target.RebuildConnections();
		RefreshLineVisual();
		EditorUtility.SetDirty( _target );
		SceneView.RepaintAll();
		Repaint();
	}

	void RemoveSlot( int slotIndex )
	{
		if ( slotIndex < 0 || slotIndex >= _slots.arraySize )
			return;

		serializedObject.Update();

		Transform anchor = GetSlotAnchor( slotIndex );
		RemapConnectionPairsAfterRemoval( _forcedConnections, slotIndex );
		RemapConnectionPairsAfterRemoval( _excludedConnections, slotIndex );

		_slots.DeleteArrayElementAtIndex( slotIndex );
		serializedObject.ApplyModifiedProperties();

		if ( anchor != null )
			Undo.DestroyObjectImmediate( anchor.gameObject );

		RenameSlotAnchors();

		if ( _selectedSlotIndex == slotIndex )
			_selectedSlotIndex = -1;
		else if ( _selectedSlotIndex > slotIndex )
			_selectedSlotIndex--;

		Undo.RecordObject( _target, "Remove Constellation Node" );
		_target.RebuildConnections();
		RefreshLineVisual();
		EditorUtility.SetDirty( _target );
		SceneView.RepaintAll();
		Repaint();
	}

	static void RemapConnectionPairsAfterRemoval( SerializedProperty pairsProp, int removedIndex )
	{
		if ( pairsProp == null || !pairsProp.isArray )
			return;

		for ( int i = pairsProp.arraySize - 1; i >= 0; i-- )
		{
			SerializedProperty pair = pairsProp.GetArrayElementAtIndex( i );
			int a = pair.FindPropertyRelative( "slotA" ).intValue;
			int b = pair.FindPropertyRelative( "slotB" ).intValue;

			if ( a == removedIndex || b == removedIndex )
			{
				pairsProp.DeleteArrayElementAtIndex( i );
				continue;
			}

			if ( a > removedIndex )
				pair.FindPropertyRelative( "slotA" ).intValue = a - 1;
			if ( b > removedIndex )
				pair.FindPropertyRelative( "slotB" ).intValue = b - 1;
		}
	}

	void RenameSlotAnchors()
	{
		for ( int i = 0; i < _slots.arraySize; i++ )
		{
			Transform anchor = GetSlotAnchor( i );
			if ( anchor == null )
				continue;

			string desired = "Slot_" + i;
			if ( anchor.gameObject.name == desired )
				continue;

			Undo.RecordObject( anchor.gameObject, "Rename Constellation Slot" );
			anchor.gameObject.name = desired;
		}
	}

	Transform GetSlotAnchor( int index )
	{
		if ( index < 0 || index >= _slots.arraySize )
			return null;

		SerializedProperty element = _slots.GetArrayElementAtIndex( index );
		return element.FindPropertyRelative( "anchor" ).objectReferenceValue as Transform;
	}

	Transform FindOrCreateSlotsRoot()
	{
		Transform existing = _target.transform.Find( "Slots" );
		if ( existing != null )
			return existing;

		for ( int i = 0; i < _slots.arraySize; i++ )
		{
			Transform anchor = GetSlotAnchor( i );
			if ( anchor != null && anchor.parent != null )
				return anchor.parent;
		}

		GameObject slotsRoot = new GameObject( "Slots" );
		Undo.RegisterCreatedObjectUndo( slotsRoot, "Create Slots Root" );
		slotsRoot.transform.SetParent( _target.transform, false );
		return slotsRoot.transform;
	}

	void RefreshLineVisual()
	{
		GemConstellationLineVisual visual = _lineVisual != null
			? _lineVisual.objectReferenceValue as GemConstellationLineVisual
			: null;
		if ( visual != null )
			visual.Rebuild( _target.ResolvedConnections );
	}

	void DrawPerSlotWarnings()
	{
		if ( _target == null || _target.Slots == null )
			return;

		for ( int i = 0; i < _target.Slots.Count; i++ )
		{
			if ( _target.Slots[ i ].overrideGem != null )
				continue;

			EditorGUILayout.HelpBox( "Slot " + i + " is missing overrideGem (required in PerSlot mode).", MessageType.Warning );
		}
	}
}
