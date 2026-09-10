using UnityEngine;

/// <summary>
/// Oriented box that tints a region of the treasure-surface map over base colours.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class MapRegionVolume : MonoBehaviour
{
	[SerializeField]
	Vector3 _size = new Vector3( 8f, 4f, 8f );

	[SerializeField]
	Color _color = new Color( 0.35f, 0.45f, 0.7f, 1f );

	[SerializeField]
	[Tooltip( "When true, paints empty (non-walkable) cells too. When false, only gold/walkable." )]
	bool _fillEmpty;

	Vector3 _lastPosition;
	Quaternion _lastRotation;
	Vector3 _lastSize;
	Color _lastColor;
	bool _lastFillEmpty;

	public Vector3 Size => _size;
	public Color Color => _color;
	public bool FillEmpty => _fillEmpty;

	void OnEnable()
	{
		CacheTransform();
		MapOverlayRegistrar.RegisterVolume( this );
	}

	void OnDisable()
	{
		MapOverlayRegistrar.UnregisterVolume( this );
	}

	void LateUpdate()
	{
		if ( !isActiveAndEnabled )
			return;

		if ( transform.position != _lastPosition
			|| transform.rotation != _lastRotation
			|| _size != _lastSize
			|| _color != _lastColor
			|| _fillEmpty != _lastFillEmpty )
		{
			CacheTransform();
			MapOverlayRegistrar.NotifyVolumeChanged();
		}
	}

	void CacheTransform()
	{
		_lastPosition = transform.position;
		_lastRotation = transform.rotation;
		_lastSize = _size;
		_lastColor = _color;
		_lastFillEmpty = _fillEmpty;
	}

	public bool ContainsWorldXZ( Vector3 worldPos )
	{
		_size.x = Mathf.Max( 0.01f, _size.x );
		_size.y = Mathf.Max( 0.01f, _size.y );
		_size.z = Mathf.Max( 0.01f, _size.z );

		Vector3 local = transform.InverseTransformPoint( worldPos );
		float halfX = _size.x * 0.5f;
		float halfZ = _size.z * 0.5f;
		return local.x >= -halfX && local.x <= halfX && local.z >= -halfZ && local.z <= halfZ;
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		_size.x = Mathf.Max( 0.01f, _size.x );
		_size.y = Mathf.Max( 0.01f, _size.y );
		_size.z = Mathf.Max( 0.01f, _size.z );
		CacheTransform();
		MapOverlayRegistrar.NotifyVolumeChanged();
		UnityEditor.SceneView.RepaintAll();
	}

	void OnDrawGizmos()
	{
		DrawGizmo( selected: false );
	}

	void OnDrawGizmosSelected()
	{
		DrawGizmo( selected: true );
	}

	void DrawGizmo( bool selected )
	{
		Vector3 size = new Vector3(
			Mathf.Max( 0.01f, _size.x ),
			Mathf.Max( 0.01f, _size.y ),
			Mathf.Max( 0.01f, _size.z ) );

		Gizmos.matrix = Matrix4x4.TRS( transform.position, transform.rotation, size );
		Color fill = _color;
		fill.a = selected ? 0.22f : 0.1f;
		Gizmos.color = fill;
		Gizmos.DrawCube( Vector3.zero, Vector3.one );

		Color wire = _color;
		wire.a = selected ? 0.95f : 0.55f;
		Gizmos.color = wire;
		Gizmos.DrawWireCube( Vector3.zero, Vector3.one );
		Gizmos.matrix = Matrix4x4.identity;
	}
#endif
}
