using System.Collections.Generic;

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
	public const string CastingParam = "Casting";

	const int SupportBoneCount = 5;
	const int HeadBoneIndex = 5;

	static readonly int LookingHash = Animator.StringToHash( LookingParam );
	static readonly int CastingHash = Animator.StringToHash( CastingParam );

	[Header( "References" )]
	[SerializeField] Animator _animator;
	[SerializeField] Transform _spine01;
	[SerializeField] Transform _spine02;
	[SerializeField] Transform _neck01;
	[SerializeField] Transform _neck02;
	[SerializeField] Transform _neck03;
	[SerializeField] Transform _head;
	[Tooltip( "Mouth / breath emit point parented under the head." )]
	[SerializeField] Transform _fireBreathRoot;
	[SerializeField] DragonFireBreathVFX _fireBreath;

	[Header( "Look" )]
	[SerializeField] float _playerEyeHeight = 1.6f;
	[Tooltip( "When set, overrides baseline look-trigger aiming (e.g. incinerator)." )]
	[SerializeField] Transform _lookTargetOverride;

	[Header( "Aim" )]
	[SerializeField] float _lookBlendSpeed = 4f;
	[SerializeField] float _aimSmoothTime = 0.35f;
	[SerializeField] float _maxHeadDegreesPerSecond = 180f;
	[SerializeField] float _lookingAnimSpeed = 0.2f;
	[Tooltip( "Offset along the head look axis from the head bone (eye-ish aim origin)." )]
	[SerializeField] float _headLookOriginOffset = 0.25f;
	[Tooltip( "Local axis along support bones toward the next joint (calibrated on Awake if zero)." )]
	[SerializeField] Vector3 _lookLocalAxis = Vector3.zero;

	[Header( "Support Bones (spine_01 .. neck_03)" )]
	[Tooltip( "Per-bone multipliers on the look arc (normalized against the max entry)." )]
	[SerializeField] float[] _supportWeights = { 0.55f, 0.7f, 0.85f, 1f, 1f };
	[SerializeField] float[] _supportMaxYaw = { 28f, 32f, 40f, 48f, 55f };
	[SerializeField] float[] _supportMaxPitch = { 16f, 20f, 26f, 30f, 34f };
	[Tooltip( "Curve shaping along the chain (>1 puts more bend toward the neck)." )]
	[SerializeField] float _arcPower = 1.25f;
	[Tooltip( "How strongly the spine/neck follow the look direction (0-1)." )]
	[SerializeField] [Range( 0f, 1f )] float _arcStrength = 0.65f;

	[Header( "Head" )]
	[Tooltip( "Local axis on the head used as face forward (default +Z). Auto-detect picks the best ±X/±Y/±Z vs body forward." )]
	[SerializeField] Vector3 _headLookLocalAxis = Vector3.forward;
	[SerializeField] bool _autoDetectHeadLookAxis = true;
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
	bool _casting;
	bool _hasCastingParam;
	readonly HashSet<DragonLookTrigger> _activeLookTriggers = new HashSet<DragonLookTrigger>();

	public bool IsLooking => _looking;
	public bool IsCasting => _casting;
	public bool IsPlayerInLookTrigger => _activeLookTriggers.Count > 0;
	public Transform HeadBone => _head;
	public Transform FireBreathRoot => _fireBreathRoot != null ? _fireBreathRoot : _head;
	public DragonFireBreathVFX FireBreath => _fireBreath;

	public void SetLookTarget( Transform target )
	{
		_lookTargetOverride = target;
	}

	public void ClearLookTarget()
	{
		_lookTargetOverride = null;
	}

	public void NotifyLookTriggerEnter( DragonLookTrigger trigger )
	{
		if ( trigger == null )
			return;
		_activeLookTriggers.Add( trigger );
	}

	public void NotifyLookTriggerExit( DragonLookTrigger trigger )
	{
		if ( trigger == null )
			return;
		_activeLookTriggers.Remove( trigger );
	}

	public void BeginSpellCast( Transform aimTarget )
	{
		if ( aimTarget != null )
			SetLookTarget( aimTarget );

		if ( _casting )
			return;

		_casting = true;
		ApplyCastingParam( true );
		RestoreAnimatorSpeed();
	}

	public void EndSpellCast()
	{
		if ( !_casting )
		{
			ClearLookTarget();
			return;
		}

		_casting = false;
		ApplyCastingParam( false );
		ClearLookTarget();
	}

	void Awake()
	{
		if ( _animator == null )
			_animator = GetComponent<Animator>();

		ResolveBonesIfNeeded();
		BuildBoneArray();
		CalibrateLocalAlong();
		CalibrateHeadLookAxis();
		CacheLookingParam();
		CacheCastingParam();
		StoreBaseAnimatorSpeed();
	}

	void OnDisable()
	{
		if ( _casting )
		{
			_casting = false;
			ApplyCastingParam( false );
		}
		_activeLookTriggers.Clear();
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
		if ( !_casting )
			ApplyAnimatorSpeedDamp( _lookWeight );
		else
			RestoreAnimatorSpeed();

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
		// Spine/neck arc toward the player first; head locks on afterward.
		ApplySupportLookArc( _lookWeight );
		ApplyHeadLook( _lookWeight, rateLimit: true );
		ApplyHeadLook( _lookWeight, rateLimit: false );
	}

	void ResolveAim( out bool shouldLook, out Vector3 aimPoint )
	{
		shouldLook = false;
		aimPoint = _aimInitialized ? _smoothedAimPoint : ( transform.position + transform.forward * 2f );

		// Explicit override (incinerator, cinematics, etc.) always wins.
		if ( _lookTargetOverride != null )
		{
			shouldLook = true;
			aimPoint = _lookTargetOverride.position;
			return;
		}

		// Baseline: player inside any authored look-trigger sphere.
		if ( _activeLookTriggers.Count <= 0 || GameMode.Instance == null )
			return;

		PlayerController player = GameMode.Instance.Player;
		if ( player == null )
			return;

		shouldLook = true;
		aimPoint = GetPlayerAimPoint( player );
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

	void ApplyCastingParam( bool casting )
	{
		if ( _animator == null || !_hasCastingParam )
			return;

		_animator.SetBool( CastingHash, casting );
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

	void ApplySupportLookArc( float weight )
	{
		Vector3 bodyForward = Flatten( transform.forward );
		Vector3 softAim = SoftenRearAim( _smoothedAimPoint, bodyForward );
		float maxSupportWeight = GetMaxSupportWeight();
		float arcPower = Mathf.Max( 0.01f, _arcPower );
		float arcStrength = Mathf.Clamp01( _arcStrength );

		// Root → tip: progressive arc from spine base toward the look direction.
		for ( int i = 0; i < SupportBoneCount; i++ )
		{
			Transform bone = _bones[ i ];
			if ( bone == null )
				continue;

			Vector3 localAlong = GetLocalAlong( i );
			Vector3 animatedAlong = bone.rotation * localAlong;
			Vector3 toAim = softAim - bone.position;
			if ( toAim.sqrMagnitude < 0.0001f )
				continue;

			float t = Mathf.Pow( ( i + 1f ) / SupportBoneCount, arcPower );
			float weightMul = 1f;
			if ( maxSupportWeight > 0.0001f )
				weightMul = GetSupportWeight( i ) / maxSupportWeight;

			float blend = t * arcStrength * weightMul * weight;
			if ( blend <= 0.0001f )
				continue;

			Vector3 toAimDir = toAim.normalized;
			Vector3 arcDir = Vector3.Slerp( animatedAlong.normalized, toAimDir, blend );
			Vector3 desired = ClampDirectionInBoneFrame( animatedAlong, arcDir, GetSupportMaxYaw( i ), GetSupportMaxPitch( i ) );
			ApplyAimAxisRotation( bone, animatedAlong, desired, 1f );
		}
	}

	float GetMaxSupportWeight()
	{
		if ( _supportWeights == null || _supportWeights.Length == 0 )
			return 1f;

		float max = 0f;
		for ( int i = 0; i < _supportWeights.Length; i++ )
		{
			if ( _supportWeights[ i ] > max )
				max = _supportWeights[ i ];
		}

		return max > 0.0001f ? max : 1f;
	}

	void ApplyHeadLook( float weight, bool rateLimit )
	{
		Transform head = _bones[ HeadBoneIndex ];
		if ( head == null )
			return;

		float boneWeight = _headWeight * weight;
		if ( boneWeight <= 0.0001f )
			return;

		Vector3 animatedAlong = GetHeadLookWorldDir( head );
		Vector3 lookOrigin = GetHeadLookOrigin( head, animatedAlong );
		Vector3 toTarget = _smoothedAimPoint - lookOrigin;
		if ( toTarget.sqrMagnitude < 0.0001f )
			return;

		// Aim head forward directly at the player; clamp in the head's animated frame.
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

		// Support bones only — head uses _headLookLocalAxis (face forward), not jaw-along.
		for ( int i = 0; i < SupportBoneCount; i++ )
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

			Transform next = i + 1 < _bones.Length ? _bones[ i + 1 ] : null;
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

		if ( _localAlong.Length > HeadBoneIndex )
			_localAlong[ HeadBoneIndex ] = GetHeadLookLocalAxis();
	}

	void CalibrateHeadLookAxis()
	{
		if ( _head == null )
		{
			if ( _headLookLocalAxis.sqrMagnitude < 0.0001f )
				_headLookLocalAxis = Vector3.forward;
			else
				_headLookLocalAxis = _headLookLocalAxis.normalized;
			return;
		}

		if ( _autoDetectHeadLookAxis || _headLookLocalAxis.sqrMagnitude < 0.0001f )
			_headLookLocalAxis = PickBestHeadLookLocalAxis();
		else
			_headLookLocalAxis = _headLookLocalAxis.normalized;

		if ( _localAlong != null && _localAlong.Length > HeadBoneIndex )
			_localAlong[ HeadBoneIndex ] = _headLookLocalAxis;
	}

	Vector3 PickBestHeadLookLocalAxis()
	{
		Vector3 bodyForward = Flatten( transform.forward );
		Vector3[] candidates =
		{
			Vector3.forward, Vector3.back,
			Vector3.up, Vector3.down,
			Vector3.right, Vector3.left
		};

		Vector3 best = Vector3.forward;
		float bestDot = float.NegativeInfinity;
		for ( int i = 0; i < candidates.Length; i++ )
		{
			Vector3 world = _head.rotation * candidates[ i ];
			float dot = Vector3.Dot( Flatten( world ), bodyForward );
			if ( dot > bestDot )
			{
				bestDot = dot;
				best = candidates[ i ];
			}
		}

		return best;
	}

	Vector3 GetHeadLookLocalAxis()
	{
		if ( _headLookLocalAxis.sqrMagnitude < 0.0001f )
			return Vector3.forward;
		return _headLookLocalAxis.normalized;
	}

	Vector3 GetHeadLookWorldDir( Transform head )
	{
		return head.rotation * GetHeadLookLocalAxis();
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

	void CacheCastingParam()
	{
		_hasCastingParam = false;
		if ( _animator == null )
			return;

		int count = _animator.parameterCount;
		for ( int i = 0; i < count; i++ )
		{
			AnimatorControllerParameter param = _animator.GetParameter( i );
			if ( param.nameHash == CastingHash && param.type == AnimatorControllerParameterType.Bool )
			{
				_hasCastingParam = true;
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
	public void EditorAssign( Animator animator, Transform spine01, Transform spine02, Transform neck01, Transform neck02, Transform neck03, Transform head )
	{
		_animator = animator;
		_spine01 = spine01;
		_spine02 = spine02;
		_neck01 = neck01;
		_neck02 = neck02;
		_neck03 = neck03;
		_head = head;
	}

	public void EditorAssignFireBreath( Transform fireBreathRoot, DragonFireBreathVFX fireBreath )
	{
		_fireBreathRoot = fireBreathRoot;
		_fireBreath = fireBreath;
	}
#endif
}
