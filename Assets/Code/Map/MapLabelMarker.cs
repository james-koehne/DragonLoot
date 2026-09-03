using UnityEngine;

/// <summary>
/// World-space map label (e.g. "Dragon's Chamber") shown once its texel is discovered.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class MapLabelMarker : MonoBehaviour
{
	[SerializeField]
	string _label = "Area";

	string _lastLabel;

	public string Label => _label;
	public Vector3 WorldPosition => transform.position;

	void OnEnable()
	{
		_lastLabel = _label;
		MapOverlayRegistrar.RegisterLabel( this );
	}

	void OnDisable()
	{
		MapOverlayRegistrar.UnregisterLabel( this );
	}

	void LateUpdate()
	{
		if ( !isActiveAndEnabled )
			return;

		if ( _label != _lastLabel )
		{
			_lastLabel = _label;
			MapOverlayRegistrar.NotifyChanged();
		}
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		_lastLabel = _label;
		MapOverlayRegistrar.NotifyChanged();
		UnityEditor.SceneView.RepaintAll();
	}

	void OnDrawGizmos()
	{
		Gizmos.color = new Color( 0.95f, 0.88f, 0.55f, 0.9f );
		Gizmos.DrawSphere( transform.position, 0.25f );
		Gizmos.DrawWireSphere( transform.position, 0.45f );
		UnityEditor.Handles.Label( transform.position + Vector3.up * 0.6f, string.IsNullOrEmpty( _label ) ? "Map Label" : _label );
	}
#endif
}
