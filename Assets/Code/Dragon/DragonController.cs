using UnityEngine;

/// <summary>
/// Elder dragon gameplay hook: look at a target (explicit or auto player in range),
/// swap to Idle_Looking via Animator bool <c>Looking</c>, and aim spine/neck/head in LateUpdate.
/// </summary>
[DisallowMultipleComponent]
public class DragonController : MonoBehaviour
{
	public const string LookingParam = "Looking";

	static readonly int LookingHash = Animator.StringToHash( LookingParam );

	[Header( "References" )]
	[SerializeField] Animator _animator;
	[SerializeField] Transform _spine01;
	[SerializeField] Transform _spine02;
	[SerializeField] Transform _neck01;
	[SerializeField] Transform _neck02;
	[SerializeField] Transform _neck03;
	[SerializeField] Transform _head;

	[Header( "Auto Look" )]
	[SerializeField] float _autoLookRange = 50f;
	[SerializeField] float _playerEyeHeight = 1.6f;
	[Tooltip( "When set, overrides auto player look-at." )]
	[SerializeField] Transform _lookTargetOverride;

	[Header( "Aim" )]
	[SerializeField] float _lookBlendSpeed = 4f;
	[SerializeField] float _aimSmoothTime = 0.35f;
	[SerializeField] float _maxYawDegrees = 70f;
	[SerializeField] float _maxPitchDegrees = 35f;
	[Tooltip( "Per-bone look weights: spine_01, spine_02, neck_01, neck_02, neck_03, head." )]
	[SerializeField] float[] _chainWeights = { 0.08f, 0.12f, 0.15f, 0.2f, 0.22f, 0.23f };
	[Tooltip( "Local axis along each bone toward the next joint (calibrated on Awake if zero)." )]
	[SerializeField] Vector3 _lookLocalAxis = Vector3.zero;

	Transform[] _bones;
	Vector3[] _localAlong;
	float _lookWeight;
	bool _looking;
	bool _hasLookingParam;
	Vector3 _aimPoint;
	float _smoothedYaw;
	float _smoothedPitch;
	float _yawVelocity;
	float _pitchVelocity;
	bool _aimInitialized;

	public bool IsLooking => _looking;
	public float AutoLookRange => _autoLookRange;

	public void SetLookTarget( Transform target )
	{
		_lookTargetOverride = target;
	}

	public void ClearLookTarget()
	{
		_lookTargetOverride = null;
	}

	void Awake()
	{
		if ( _animator == null )
			_animator = GetComponent<Animator>();

		ResolveBonesIfNeeded();
		BuildBoneArray();
		CalibrateLocalAlong();
		CacheLookingParam();
	}

	void Update()
	{
		ResolveAim( out bool shouldLook, out Vector3 aimPoint );
		_aimPoint = aimPoint;

		if ( shouldLook != _looking )
		{
			_looking = shouldLook;
			ApplyLookingParam( _looking );
			if ( !_looking )
			{
				_yawVelocity = 0f;
				_pitchVelocity = 0f;
			}
		}
	}

	void LateUpdate()
	{
		float targetWeight = _looking ? 1f : 0f;
		_lookWeight = Mathf.MoveTowards( _lookWeight, targetWeight, _lookBlendSpeed * Time.deltaTime );
		if ( _lookWeight <= 0.0001f || _bones == null )
		{
			if ( _lookWeight <= 0.0001f )
				_aimInitialized = false;
			return;
		}

		UpdateSmoothedAimAngles( _aimPoint );
		ApplyLookAt( _lookWeight );
	}

	void ResolveAim( out bool shouldLook, out Vector3 aimPoint )
	{
		shouldLook = false;
		aimPoint = transform.position + transform.forward;

		if ( _lookTargetOverride != null )
		{
			shouldLook = true;
			aimPoint = _lookTargetOverride.position;
			return;
		}

		if ( GameMode.Instance == null )
			return;

		PlayerController player = GameMode.Instance.Player;
		if ( player == null )
			return;

		Vector3 from = _head != null ? _head.position : transform.position;
		Vector3 playerAim = GetPlayerAimPoint( player );
		float rangeSq = _autoLookRange * _autoLookRange;
		if ( ( playerAim - from ).sqrMagnitude > rangeSq )
			return;

		shouldLook = true;
		aimPoint = playerAim;
	}

	Vector3 GetPlayerAimPoint( PlayerController player )
	{
		Transform mount = player.CameraMount;
		if ( mount != null )
			return mount.position;

		return player.transform.position + Vector3.up * _playerEyeHeight;
	}

	void ApplyLookingParam( bool looking )
	{
		if ( _animator == null || !_hasLookingParam )
			return;

		_animator.SetBool( LookingHash, looking );
	}

	void UpdateSmoothedAimAngles( Vector3 worldTarget )
	{
		Transform pivot = _spine01 != null ? _spine01 : transform;
		Vector3 toTarget = worldTarget - pivot.position;
		if ( toTarget.sqrMagnitude < 0.0001f )
			return;

		Vector3 worldDir = toTarget.normalized;
		Vector3 bodyForward = Flatten( transform.forward );
		Vector3 bodyRight = Flatten( transform.right );

		float forwardDot = Vector3.Dot( bodyForward, Flatten( worldDir ) );
		float rightDot = Vector3.Dot( bodyRight, Flatten( worldDir ) );
		float desiredYaw = Mathf.Atan2( rightDot, forwardDot ) * Mathf.Rad2Deg;

		// Soften the rear hemisphere so L/R crosses behind go through center instead of flipping clamp edges.
		float rearSoft = Mathf.InverseLerp( -0.15f, 0.35f, forwardDot );
		desiredYaw *= rearSoft;
		desiredYaw = Mathf.Clamp( desiredYaw, -_maxYawDegrees, _maxYawDegrees );

		float pitch = Mathf.Asin( Mathf.Clamp( Vector3.Dot( worldDir, Vector3.up ), -1f, 1f ) ) * Mathf.Rad2Deg;
		float desiredPitch = Mathf.Clamp( pitch, -_maxPitchDegrees, _maxPitchDegrees );

		if ( !_aimInitialized )
		{
			_smoothedYaw = desiredYaw;
			_smoothedPitch = desiredPitch;
			_yawVelocity = 0f;
			_pitchVelocity = 0f;
			_aimInitialized = true;
			return;
		}

		float smooth = Mathf.Max( 0.01f, _aimSmoothTime );
		_smoothedYaw = Mathf.SmoothDamp( _smoothedYaw, desiredYaw, ref _yawVelocity, smooth );
		_smoothedPitch = Mathf.SmoothDamp( _smoothedPitch, desiredPitch, ref _pitchVelocity, smooth );
		_smoothedYaw = Mathf.Clamp( _smoothedYaw, -_maxYawDegrees, _maxYawDegrees );
		_smoothedPitch = Mathf.Clamp( _smoothedPitch, -_maxPitchDegrees, _maxPitchDegrees );
	}

	void ApplyLookAt( float weight )
	{
		float bodyYaw = transform.eulerAngles.y;
		Vector3 desiredWorldDir = Quaternion.Euler( -_smoothedPitch, bodyYaw + _smoothedYaw, 0f ) * Vector3.forward;
		if ( desiredWorldDir.sqrMagnitude < 0.0001f )
			desiredWorldDir = transform.forward;
		desiredWorldDir.Normalize();

		for ( int i = 0; i < _bones.Length; i++ )
		{
			Transform bone = _bones[ i ];
			if ( bone == null )
				continue;

			float boneWeight = GetChainWeight( i ) * weight;
			if ( boneWeight <= 0.0001f )
				continue;

			Vector3 localAlong = _localAlong[ i ];
			if ( localAlong.sqrMagnitude < 0.0001f )
				localAlong = Vector3.forward;

			Vector3 animatedAlong = bone.rotation * localAlong.normalized;
			Quaternion delta = Quaternion.FromToRotation( animatedAlong, desiredWorldDir );
			Quaternion targetWorld = delta * bone.rotation;
			bone.rotation = Quaternion.Slerp( bone.rotation, targetWorld, boneWeight );
		}
	}

	static Vector3 Flatten( Vector3 v )
	{
		v.y = 0f;
		if ( v.sqrMagnitude < 0.0001f )
			return Vector3.forward;
		return v.normalized;
	}

	float GetChainWeight( int index )
	{
		if ( _chainWeights == null || index < 0 || index >= _chainWeights.Length )
			return 0f;
		return _chainWeights[ index ];
	}

	void ResolveBonesIfNeeded()
	{
		if ( _spine01 == null )
			_spine01 = FindDeep( transform, "spine_01" );
		if ( _spine02 == null )
			_spine02 = FindDeep( transform, "spine_02" );
		if ( _neck01 == null )
			_neck01 = FindDeep( transform, "neck_01" );
		if ( _neck02 == null )
			_neck02 = FindDeep( transform, "neck_02" );
		if ( _neck03 == null )
			_neck03 = FindDeep( transform, "neck_03" );
		if ( _head == null )
			_head = FindDeep( transform, "head" );
	}

	void BuildBoneArray()
	{
		_bones = new Transform[] { _spine01, _spine02, _neck01, _neck02, _neck03, _head };
		_localAlong = new Vector3[ _bones.Length ];
	}

	void CalibrateLocalAlong()
	{
		Vector3 authored = _lookLocalAxis;
		bool useAuthored = authored.sqrMagnitude > 0.0001f;

		for ( int i = 0; i < _bones.Length; i++ )
		{
			Transform bone = _bones[ i ];
			if ( bone == null )
			{
				_localAlong[ i ] = Vector3.forward;
				continue;
			}

			if ( useAuthored )
			{
				_localAlong[ i ] = authored.normalized;
				continue;
			}

			Transform next = i + 1 < _bones.Length ? _bones[ i + 1 ] : FindDeep( bone, "jaw" );
			if ( next != null )
			{
				Vector3 worldAlong = next.position - bone.position;
				if ( worldAlong.sqrMagnitude > 0.0001f )
				{
					_localAlong[ i ] = Quaternion.Inverse( bone.rotation ) * worldAlong.normalized;
					continue;
				}
			}

			_localAlong[ i ] = Vector3.forward;
		}
	}

	void CacheLookingParam()
	{
		_hasLookingParam = false;
		if ( _animator == null )
			return;

		int count = _animator.parameterCount;
		for ( int i = 0; i < count; i++ )
		{
			AnimatorControllerParameter param = _animator.GetParameter( i );
			if ( param.nameHash == LookingHash && param.type == AnimatorControllerParameterType.Bool )
			{
				_hasLookingParam = true;
				return;
			}
		}
	}

	static Transform FindDeep( Transform root, string name )
	{
		if ( root == null )
			return null;
		if ( root.name == name )
			return root;

		for ( int i = 0; i < root.childCount; i++ )
		{
			Transform found = FindDeep( root.GetChild( i ), name );
			if ( found != null )
				return found;
		}

		return null;
	}

#if UNITY_EDITOR
	public void EditorAssign( Animator animator, Transform spine01, Transform spine02, Transform neck01, Transform neck02, Transform neck03, Transform head, float autoLookRange )
	{
		_animator = animator;
		_spine01 = spine01;
		_spine02 = spine02;
		_neck01 = neck01;
		_neck02 = neck02;
		_neck03 = neck03;
		_head = head;
		_autoLookRange = autoLookRange;
	}
#endif
}
