using UnityEngine;

/// <summary>
/// Scene-authored destination for tutorials: trigger zone + optional map pin.
/// While a tutorial references <see cref="Id"/> via <see cref="TutorialDefinition.mapMarkerId"/>,
/// the pin pulses on the full-screen map (always visible, fog bypass).
/// Enter/exit publishes <see cref="VolumeEnteredEvent"/> / <see cref="VolumeExitedEvent"/> with this id.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent( typeof( SphereCollider ) )]
public sealed class TutorialMapMarker : MonoBehaviour
{
	[SerializeField]
	string _id = "marker_";

	[SerializeField]
	[Tooltip( "Optional short label under the map pin (e.g. Constellation)." )]
	string _mapLabel;

	[SerializeField]
	[Min( 0.5f )]
	float _zoneRadius = 6f;

	string _lastId;
	string _lastLabel;
	float _lastRadius;

	public string Id => _id;
	public string MapLabel => _mapLabel;
	public Vector3 WorldPosition => transform.position;
	public float ZoneRadius => _zoneRadius;

	void Reset()
	{
		ApplyCollider();
	}

	void OnEnable()
	{
		ApplyCollider();
		CacheState();
		MapOverlayRegistrar.RegisterTutorialMarker( this );
	}

	void OnDisable()
	{
		MapOverlayRegistrar.UnregisterTutorialMarker( this );
	}

	void LateUpdate()
	{
		if ( !isActiveAndEnabled )
			return;

		if ( _id != _lastId || _mapLabel != _lastLabel || !Mathf.Approximately( _zoneRadius, _lastRadius ) )
		{
			ApplyCollider();
			CacheState();
			MapOverlayRegistrar.NotifyChanged();
		}
	}

	void CacheState()
	{
		_lastId = _id;
		_lastLabel = _mapLabel;
		_lastRadius = _zoneRadius;
	}

	void ApplyCollider()
	{
		SphereCollider col = GetComponent<SphereCollider>();
		if ( col == null )
			return;
		col.isTrigger = true;
		col.radius = Mathf.Max( 0.5f, _zoneRadius );
		col.center = Vector3.zero;
	}

	void OnTriggerEnter( Collider other )
	{
		if ( string.IsNullOrEmpty( _id ) )
			return;
		if ( !IsPlayer( other ) )
			return;

		EventBus.Publish( new VolumeEnteredEvent
		{
			VolumeId = _id,
			Volume = null
		} );
	}

	void OnTriggerExit( Collider other )
	{
		if ( string.IsNullOrEmpty( _id ) )
			return;
		if ( !IsPlayer( other ) )
			return;

		EventBus.Publish( new VolumeExitedEvent
		{
			VolumeId = _id,
			Volume = null
		} );
	}

	static bool IsPlayer( Collider other )
	{
		if ( other == null )
			return false;
		return other.GetComponentInParent<PlayerController>() != null;
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		ApplyCollider();
		CacheState();
		MapOverlayRegistrar.NotifyChanged();
		UnityEditor.SceneView.RepaintAll();
	}

	void OnDrawGizmos()
	{
		Gizmos.color = new Color( 0.35f, 0.85f, 1f, 0.35f );
		Gizmos.DrawSphere( transform.position, Mathf.Max( 0.5f, _zoneRadius ) );
		Gizmos.color = new Color( 0.35f, 0.85f, 1f, 0.9f );
		Gizmos.DrawWireSphere( transform.position, Mathf.Max( 0.5f, _zoneRadius ) );
		UnityEditor.Handles.Label(
			transform.position + Vector3.up * 0.6f,
			string.IsNullOrEmpty( _id ) ? "Tutorial Map Marker" : _id );
	}

	[UnityEditor.MenuItem( "GameObject/DragonLoot/Tutorial Map Marker", false, 12 )]
	static void CreateTutorialMapMarker( UnityEditor.MenuCommand command )
	{
		GameObject go = new GameObject( "TutorialMapMarker" );
		UnityEditor.GameObjectUtility.SetParentAndAlign( go, command.context as GameObject );
		go.AddComponent<SphereCollider>();
		TutorialMapMarker marker = go.AddComponent<TutorialMapMarker>();
		UnityEditor.Undo.RegisterCreatedObjectUndo( go, "Create Tutorial Map Marker" );
		UnityEditor.Selection.activeGameObject = go;
		marker.EditorSetDefaults( EventSceneAutoWire.IdMarkerGemConstellation, "Constellation", 6f );
	}

	public void EditorSetDefaults( string id, string mapLabel, float zoneRadius )
	{
		_id = id;
		_mapLabel = mapLabel;
		_zoneRadius = zoneRadius;
		ApplyCollider();
		CacheState();
	}
#endif
}
