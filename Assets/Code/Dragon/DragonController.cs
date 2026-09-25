using UnityEngine;

/// <summary>
/// Elder dragon gameplay hook: look at a target (explicit or auto player in range),
/// swap to Idle_Looking via Animator bool <c>Looking</c>, and aim spine/neck/head in LateUpdate.
/// Head locks onto the player; spine/neck contribute supporting rotations.
/// </summary>
[DisallowMultipleComponent]
public class DragonController : MonoBehaviour
{
	public const string LookingParam = "Looking";

	const int SupportBoneCount = 5;
	const int HeadBoneIndex = 5;

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
	[SerializeField] float _lookExitPadding = 5f;
	[SerializeField] float _playerEyeHeight = 1.6f;
	[Tooltip( "When set, overrides auto player look-at." )]
	[SerializeField] Transform _lookTargetOverride;

	[Header( "Aim" )]
	[SerializeField] float _lookBlendSpeed = 4f;
	[SerializeField] float _aimSmoothTime = 0.35f;
	[SerializeField] float _maxHeadDegreesPerSecond = 180f;
	[SerializeField] float _lookingAnimSpeed = 0.2f;
	[Tooltip( "Offset along the head look axis from the head bone (eye-ish aim origin)." )]
	[SerializeField] float _headLookOriginOffset = 0.25f;
	[Tooltip( "Local axis along each bone toward the next joint (calibrated on Awake if zero)." )]
	[SerializeField] Vector3 _lookLocalAxis = Vector3.zero;

	[Header( "Support Bones (spine_01 .. neck_03)" )]
	[SerializeField] float[] _supportWeights = { 0.12f, 0.16f, 0.22f, 0.28f, 0.35f };
	[SerializeField] float[] _supportMaxYaw = { 20f, 25f, 35f, 40f, 45f };
	[SerializeField] float[] _supportMaxPitch = { 12f, 15f, 20f, 22f, 25f };

	[Header( "Head" )]
	[SerializeField] float _headWeight = 1f;
	[SerializeField] float _headMaxYaw = 80f;
	[SerializeField] float _headMaxPitch = 45f;

	Transform[] _bones;
	Vector3[] _localAlong;
	float _lookWeight;
	bool _looking;
	bool _hasLookingParam;
	Vector3 _rawAimPoint;
	Vector3 _smoothedAimPoint;
	Vector3 _aimVelocity;
	bool _aimInitialized;
	Quaternion _smoothedHeadRotation;
	bool _headRotationInitialized;
	float _baseAnimatorSpeed = 1f;
	bool _storedBaseAnimatorSpeed;

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
		StoreBaseAnimatorSpeed();
	}

	void OnDisable()
	{
		RestoreAnimatorSpeed();
	}

	void Update()
	{
		ResolveAim( out bool shouldLook, out Vector3 aimPoint );
		_rawAimPoint = aimPoint;

		if ( shouldLook != _looking )
		{
			_looking = shouldLook;
			ApplyLookingParam( _looking );
			if ( !_looking )
			{
				_aimVelocity = Vector3.zero;
				_headRotationInitialized = false;
			}
		}
	}

	void LateUpdate()
	{
		float targetWeight = _looking ? 1f : 0f;
		_lookWeight = Mathf.MoveTowards( _lookWeight, targetWeight, _lookBlendSpeed * Time.deltaTime );
		ApplyAnimatorSpeedDamp( _lookWeight );

		if ( _lookWeight <= 0.0001f || _bones == null )
		{
			if ( _lookWeight <= 0.0001f )
			{
				_aimInitialized = false;
				_headRotationInitialized = false;
			}
			return;
		}

		UpdateSmoothedAimPoint( _rawAimPoint );
		// Head locks onto the player first; support bones then share the load; head re-locks.
		ApplyHeadLook( _lookWeight, rateLimit: true );
		ApplySupportLookTowardHeadTarget( _lookWeight );
		ApplyHeadLook( _lookWeight, rateLimit: false );
	}

	void ResolveAim( out bool shouldLook, out Vector3 aimPoint )
	{
		shouldLook = false;
		aimPoint = _aimInitialized ? _smoothedAimPoint : ( transform.position + transform.forward * 2f );

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
		float distance = Vector3.Distance( from, playerAim );

		float enterRange = _autoLookRange;
		float exitRange = _autoLookRange + Mathf.Max( 0f, _lookExitPadding );

		if ( _looking )
			shouldLook = distance <= exitRange;
		else
			shouldLook = distance <= enterRange;

		if ( !shouldLook )
			return;

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

	void StoreBaseAnimatorSpeed()
	{
		if ( _animator == null || _storedBaseAnimatorSpeed )
			return;

		_baseAnimatorSpeed = _animator.speed;
		_storedBaseAnimatorSpeed = true;
	}

	void ApplyAnimatorSpeedDamp( float lookWeight )
	{
		if ( _animator == null )
			return;

		StoreBaseAnimatorSpeed();
		float speed = Mathf.Lerp( _baseAnimatorSpeed, _lookingAnimSpeed, lookWeight );
		_animator.speed = speed;
	}

	void RestoreAnimatorSpeed()
	{
		if ( _animator == null || !_storedBaseAnimatorSpeed )
			return;

		_animator.speed = _baseAnimatorSpeed;
	}

	void UpdateSmoothedAimPoint( Vector3 worldTarget )
	{
		if ( !_aimInitialized )
		{
			_smoothedAimPoint = worldTarget;
			_aimVelocity = Vector3.zero;
			_aimInitialized = true;
			return;
		}

		float smooth = Mathf.Max( 0.01f, _aimSmoothTime );
		_smoothedAimPoint = Vector3.SmoothDamp( _smoothedAimPoint, worldTarget, ref _aimVelocity, smooth );
	}

	void ApplySupportLookTowardHeadTarget( float weight )
	{
		Transform head = _bones[ HeadBoneIndex ];
		if ( head == null )
			return;

		Vector3 headAlong = GetLocalAlong( HeadBoneIndex );
		Vector3 bodyForward = Flatten( transform.forward );
		Vector3 softAim = SoftenRearAim( _smoothedAimPoint, bodyForward );

		// Tip → root so neck absorbs most of the residual, spine the least.
		for ( int i = SupportBoneCount - 1; i >= 0; i-- )
		{
			Transform bone = _bones[ i ];
			if ( bone == null )
				continue;

			float boneWeight = GetSupportWeight( i ) * weight;
			if ( boneWeight <= 0.0001f )
				continue;

			Vector3 localAlong = GetLocalAlong( i );
			Vector3 animatedAlong = bone.rotation * localAlong;

			Vector3 currentHeadAlong = head.rotation * headAlong;
			Vector3 lookOrigin = GetHeadLookOrigin( head, currentHeadAlong );
			Vector3 desiredHeadDir = ( softAim - lookOrigin );
			if ( desiredHeadDir.sqrMagnitude < 0.0001f )
				continue;

			desiredHeadDir.Normalize();
			Quaternion fix = Quaternion.FromToRotation( currentHeadAlong.normalized, desiredHeadDir );
			Vector3 proposedAlong = fix * animatedAlong;
			Vector3 desired = ClampDirectionInBoneFrame( animatedAlong, proposedAlong.normalized, GetSupportMaxYaw( i ), GetSupportMaxPitch( i ) );
			ApplyAimAxisRotation( bone, animatedAlong, desired, boneWeight );
		}
	}

	void ApplyHeadLook( float weight, bool rateLimit )
	{
		Transform head = _bones[ HeadBoneIndex ];
		if ( head == null )
			return;

		float boneWeight = _headWeight * weight;
		if ( boneWeight <= 0.0001f )
			return;

		Vector3 localAlong = GetLocalAlong( HeadBoneIndex );
		Vector3 animatedAlong = head.rotation * localAlong;
		Vector3 lookOrigin = GetHeadLookOrigin( head, animatedAlong );
		Vector3 toTarget = _smoothedAimPoint - lookOrigin;
		if ( toTarget.sqrMagnitude < 0.0001f )
			return;

		// Aim directly at the player from the head; clamp in the head's animated frame.
		Vector3 desired = ClampDirectionInBoneFrame( animatedAlong, toTarget.normalized, _headMaxYaw, _headMaxPitch );
		Quaternion delta = Quaternion.FromToRotation( animatedAlong, desired );
		Quaternion targetWorld = delta * head.rotation;
		Quaternion blended = Quaternion.Slerp( head.rotation, targetWorld, boneWeight );

		if ( !rateLimit )
		{
			head.rotation = blended;
			_smoothedHeadRotation = blended;
			_headRotationInitialized = true;
			return;
		}

		if ( !_headRotationInitialized )
		{
			_smoothedHeadRotation = blended;
			_headRotationInitialized = true;
		}
		else
		{
			float maxDegrees = Mathf.Max( 1f, _maxHeadDegreesPerSecond ) * Time.deltaTime;
			_smoothedHeadRotation = Quaternion.RotateTowards( _smoothedHeadRotation, blended, maxDegrees );
		}

		head.rotation = _smoothedHeadRotation;
	}

	Vector3 SoftenRearAim( Vector3 worldTarget, Vector3 bodyForward )
	{
		Transform pivot = _spine01 != null ? _spine01 : transform;
		Vector3 toTarget = worldTarget - pivot.position;
		if ( toTarget.sqrMagnitude < 0.0001f )
			return worldTarget;

		Vector3 flatToTarget = toTarget;
		flatToTarget.y = 0f;
		float horizMag = flatToTarget.magnitude;
		if ( horizMag < 0.0001f )
			return worldTarget;

		Vector3 flatDir = flatToTarget / horizMag;
		float forwardDot = Vector3.Dot( bodyForward, flatDir );
		float rearSoft = Mathf.InverseLerp( -0.15f, 0.35f, forwardDot );
		if ( rearSoft >= 0.999f )
			return worldTarget;

		Vector3 blendedFlat = Vector3.Slerp( bodyForward, flatDir, rearSoft ).normalized;
		Vector3 softened = blendedFlat * horizMag;
		softened.y = toTarget.y;
		return pivot.position + softened;
	}

	Vector3 GetHeadLookOrigin( Transform head, Vector3 worldAlong )
	{
		float scale = Mathf.Max( 0.01f, head.lossyScale.x );
		return head.position + worldAlong.normalized * ( _headLookOriginOffset * scale );
	}

	static void ApplyAimAxisRotation( Transform bone, Vector3 animatedAlong, Vector3 desiredAlong, float weight )
	{
		if ( animatedAlong.sqrMagnitude < 0.0001f || desiredAlong.sqrMagnitude < 0.0001f )
			return;

		Quaternion delta = Quaternion.FromToRotation( animatedAlong.normalized, desiredAlong.normalized );
		Quaternion targetWorld = delta * bone.rotation;
		bone.rotation = Quaternion.Slerp( bone.rotation, targetWorld, weight );
	}

	static Vector3 ClampDirectionInBoneFrame( Vector3 referenceAlong, Vector3 desired, float maxYaw, float maxPitch )
	{
		if ( referenceAlong.sqrMagnitude < 0.0001f )
			return desired;

		Vector3 up = Vector3.up;
		if ( Mathf.Abs( Vector3.Dot( referenceAlong.normalized, up ) ) > 0.98f )
			up = Vector3.forward;

		Quaternion basis = Quaternion.LookRotation( referenceAlong.normalized, up );
		Vector3 local = Quaternion.Inverse( basis ) * desired;
		float yaw = Mathf.Atan2( local.x, local.z ) * Mathf.Rad2Deg;
		float pitch = Mathf.Asin( Mathf.Clamp( local.y, -1f, 1f ) ) * Mathf.Rad2Deg;
		yaw = Mathf.Clamp( yaw, -maxYaw, maxYaw );
		pitch = Mathf.Clamp( pitch, -maxPitch, maxPitch );
		Quaternion localRot = Quaternion.Euler( -pitch, yaw, 0f );
		return basis * ( localRot * Vector3.forward );
	}

	static Vector3 Flatten( Vector3 v )
	{
		v.y = 0f;
		if ( v.sqrMagnitude < 0.0001f )
			return Vector3.forward;
		return v.normalized;
	}

	Vector3 GetLocalAlong( int index )
	{
		if ( _localAlong == null || index < 0 || index >= _localAlong.Length )
			return Vector3.forward;

		Vector3 along = _localAlong[ index ];
		if ( along.sqrMagnitude < 0.0001f )
			return Vector3.forward;
		return along.normalized;
	}

	float GetSupportWeight( int index )
	{
		if ( _supportWeights == null || index < 0 || index >= _supportWeights.Length )
			return 0f;
		return _supportWeights[ index ];
	}

	float GetSupportMaxYaw( int index )
	{
		if ( _supportMaxYaw == null || index < 0 || index >= _supportMaxYaw.Length )
			return 30f;
		return _supportMaxYaw[ index ];
	}

	float GetSupportMaxPitch( int index )
	{
		if ( _supportMaxPitch == null || index < 0 || index >= _supportMaxPitch.Length )
			return 15f;
		return _supportMaxPitch[ index ];
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
