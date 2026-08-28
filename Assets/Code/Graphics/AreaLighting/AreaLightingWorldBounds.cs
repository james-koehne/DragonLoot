using UnityEngine;

/// <summary>
/// World-space extent for the area-ambient 3D irradiance volume. Place one per level.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class AreaLightingWorldBounds : MonoBehaviour
{
	static AreaLightingWorldBounds _active;
	static int _revision;

	[SerializeField]
	Vector3 _size = new Vector3( 200f, 60f, 200f );

	Vector3 _lastPosition;
	Quaternion _lastRotation;
	Vector3 _lastSize;

	public static AreaLightingWorldBounds Active => _active;

	public static int Revision => _revision;

	public Vector3 WorldSize => _size;

	public Vector3 WorldOrigin => transform.position - _size * 0.5f;

	public Bounds WorldBounds => new Bounds( transform.position, _size );

	public static void NotifyChanged()
	{
		_revision++;
	}

	void OnEnable()
	{
		if ( _active != null && _active != this )
			Debug.LogWarning( "Multiple AreaLightingWorldBounds in the scene; using the most recently enabled instance.", this );

		_active = this;
		CacheTransform();
		NotifyChanged();
	}

	void OnDisable()
	{
		if ( _active == this )
			_active = null;

		NotifyChanged();
	}

	void LateUpdate()
	{
		if ( !isActiveAndEnabled )
			return;

		if ( transform.position != _lastPosition || transform.rotation != _lastRotation || _size != _lastSize )
		{
			CacheTransform();
			NotifyChanged();
		}
	}

	void CacheTransform()
	{
		_lastPosition = transform.position;
		_lastRotation = transform.rotation;
		_lastSize = _size;
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		_size.x = Mathf.Max( 1f, _size.x );
		_size.y = Mathf.Max( 1f, _size.y );
		_size.z = Mathf.Max( 1f, _size.z );
		CacheTransform();
		NotifyChanged();
		UnityEditor.SceneView.RepaintAll();
	}

	void OnDrawGizmos()
	{
		Vector3 size = new Vector3(
			Mathf.Max( 1f, _size.x ),
			Mathf.Max( 1f, _size.y ),
			Mathf.Max( 1f, _size.z ) );

		Gizmos.color = new Color( 0.35f, 0.55f, 0.95f, 0.12f );
		Gizmos.matrix = Matrix4x4.TRS( transform.position, transform.rotation, size );
		Gizmos.DrawCube( Vector3.zero, Vector3.one );

		Gizmos.color = new Color( 0.35f, 0.55f, 0.95f, 0.65f );
		Gizmos.DrawWireCube( Vector3.zero, Vector3.one );
		Gizmos.matrix = Matrix4x4.identity;
	}
#endif
}
