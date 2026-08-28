using UnityEngine;

/// <summary>
/// Oriented box that contributes flat ambient color into the world-fixed area-ambient volume.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class AreaLightVolume : MonoBehaviour
{
	[SerializeField]
	Vector3 _size = new Vector3( 10f, 5f, 10f );

	[SerializeField]
	[ColorUsage( true, true )]
	Color _color = new Color( 1f, 0.85f, 0.65f, 1f );

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Brightness of this volume in the baked irradiance field." )]
	float _intensity = 0.5f;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "How early the fade begins inside the box. Higher = softer core-to-edge transition." )]
	float _edgeSoftness = 0.7f;

	[SerializeField]
	[Range( 0f, 2f )]
	[Tooltip( "How far past the box faces the tint continues, as a fraction of the box half-extent." )]
	float _falloffDistance = 0.65f;

	Vector3 _lastPosition;
	Quaternion _lastRotation;
	Vector3 _lastSize;
	float _lastEdgeSoftness;
	float _lastFalloffDistance;
	float _lastIntensity;
	Color _lastColor;

	public Vector3 Size => _size;

	public Color Color => _color;

	public float Intensity => _intensity;

	public float EdgeSoftness => _edgeSoftness;

	public float FalloffDistance => _falloffDistance;

	void OnEnable()
	{
		CacheTransform();
		AreaLightVolumeRegistrar.Register( this );
	}

	void OnDisable()
	{
		AreaLightVolumeRegistrar.Unregister( this );
	}

	void LateUpdate()
	{
		if ( !isActiveAndEnabled )
			return;

		if ( transform.position != _lastPosition
			|| transform.rotation != _lastRotation
			|| _size != _lastSize
			|| !Mathf.Approximately( _edgeSoftness, _lastEdgeSoftness )
			|| !Mathf.Approximately( _falloffDistance, _lastFalloffDistance )
			|| !Mathf.Approximately( _intensity, _lastIntensity )
			|| _color != _lastColor )
		{
			CacheTransform();
			AreaLightVolumeRegistrar.NotifyChanged();
		}
	}

	void CacheTransform()
	{
		_lastPosition = transform.position;
		_lastRotation = transform.rotation;
		_lastSize = _size;
		_lastEdgeSoftness = _edgeSoftness;
		_lastFalloffDistance = _falloffDistance;
		_lastIntensity = _intensity;
		_lastColor = _color;
	}

	public bool TryGetWorldBounds( out Bounds bounds )
	{
		_size.x = Mathf.Max( 0.01f, _size.x );
		_size.y = Mathf.Max( 0.01f, _size.y );
		_size.z = Mathf.Max( 0.01f, _size.z );

		Matrix4x4 localToWorld = Matrix4x4.TRS( transform.position, transform.rotation, _size );
		Vector3 center = localToWorld.MultiplyPoint3x4( Vector3.zero );
		Vector3 ext = new Vector3(
			Mathf.Abs( localToWorld.m00 ) + Mathf.Abs( localToWorld.m01 ) + Mathf.Abs( localToWorld.m02 ),
			Mathf.Abs( localToWorld.m10 ) + Mathf.Abs( localToWorld.m11 ) + Mathf.Abs( localToWorld.m12 ),
			Mathf.Abs( localToWorld.m20 ) + Mathf.Abs( localToWorld.m21 ) + Mathf.Abs( localToWorld.m22 ) );
		ext *= 0.5f;

		float extend = Mathf.Max( 0f, _falloffDistance );
		ext *= 1f + extend;

		if ( ext.x <= 1e-4f || ext.y <= 1e-4f || ext.z <= 1e-4f )
		{
			bounds = default;
			return false;
		}

		bounds = new Bounds( center, ext * 2f );
		return true;
	}

	public AreaLightingDefinition.GpuAreaVolume ToGpuVolume()
	{
		Vector3 size = new Vector3(
			Mathf.Max( 0.01f, _size.x ),
			Mathf.Max( 0.01f, _size.y ),
			Mathf.Max( 0.01f, _size.z ) );

		Matrix4x4 localToWorld = Matrix4x4.TRS( transform.position, transform.rotation, size );
		Vector4 hdrColor = (Vector4)( _color * _intensity );
		hdrColor.w = 1f;

		return new AreaLightingDefinition.GpuAreaVolume
		{
			worldToLocal = localToWorld.inverse,
			color = hdrColor,
			softness = Mathf.Clamp01( _edgeSoftness ),
			falloffExtend = Mathf.Max( 0f, _falloffDistance )
		};
	}

#if UNITY_EDITOR
	void OnValidate()
	{
		CacheTransform();
		AreaLightVolumeRegistrar.NotifyChanged();
		UnityEditor.SceneView.RepaintAll();
	}

	void OnDrawGizmosSelected()
	{
		Vector3 size = new Vector3(
			Mathf.Max( 0.01f, _size.x ),
			Mathf.Max( 0.01f, _size.y ),
			Mathf.Max( 0.01f, _size.z ) );

		Matrix4x4 matrix = Matrix4x4.TRS( transform.position, transform.rotation, size );

		Color fill = _color;
		fill.a = 0.18f;
		Gizmos.color = fill;
		Gizmos.matrix = matrix;
		Gizmos.DrawCube( Vector3.zero, Vector3.one );

		Color wire = _color;
		wire.a = 0.85f;
		Gizmos.color = wire;
		Gizmos.DrawWireCube( Vector3.zero, Vector3.one );

		float extend = Mathf.Max( 0f, _falloffDistance );
		if ( extend > 1e-3f )
		{
			Color outer = _color;
			outer.a = 0.35f;
			Gizmos.color = outer;
			Gizmos.matrix = Matrix4x4.TRS( transform.position, transform.rotation, size * ( 1f + extend ) );
			Gizmos.DrawWireCube( Vector3.zero, Vector3.one );
		}

		Gizmos.matrix = Matrix4x4.identity;
	}
#endif
}
