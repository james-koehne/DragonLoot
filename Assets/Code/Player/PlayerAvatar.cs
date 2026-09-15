using UnityEngine;

/// <summary>
/// Drives the third-person player mesh (idle / walk) and shows it only when the camera
/// is detached for a cinematic or orbiting a minecart and far enough from CameraMount.
/// Wire Model Root + Animator in the Inspector; assign this component on PlayerController.
/// Animator Speed is 0-1 (planar speed / desired move speed) for walk playback multipliers.
/// </summary>
[DisallowMultipleComponent]
public class PlayerAvatar : MonoBehaviour
{
	const string DefaultSpeedParam = "Speed";
	const float DefaultHideDistance = 0.75f;

	[Header( "Model" )]
	[SerializeField] Transform modelRoot;
	[SerializeField] Animator animator;

	[Header( "Animator" )]
	[SerializeField] string speedParam = DefaultSpeedParam;

	[Header( "Visibility" )]
	[SerializeField] [Min( 0.05f )] float hideDistance = DefaultHideDistance;

	PlayerController _player;
	Renderer[] _renderers;
	int _speedParamHash;
	bool _visible;
	bool _hasSpeedParam;

	void Awake()
	{
		CacheRenderers();
		CacheAnimatorParam();
		SetRenderersEnabled( false );
		_visible = false;
	}

	public void Setup( PlayerController player )
	{
		_player = player;
	}

	void LateUpdate()
	{
		if ( _player == null )
			return;

		TickAnimator();
		TickVisibility();
	}

	void TickAnimator()
	{
		if ( animator == null || !_hasSpeedParam )
			return;

		float fullSpeed = Mathf.Max( 0.01f, _player.DesiredPlanarSpeed );
		float speed01 = Mathf.Clamp01( _player.PlanarSpeed / fullSpeed );
		animator.SetFloat( _speedParamHash, speed01 );
	}

	void TickVisibility()
	{
		bool thirdPersonMoment = _player.IsCinematicCameraDetached || IsMinecartOrbitActive();
		bool farEnough = IsCameraFarEnough();
		bool shouldShow = thirdPersonMoment && farEnough;
		if ( shouldShow == _visible )
			return;

		_visible = shouldShow;
		SetRenderersEnabled( _visible );
	}

	bool IsCameraFarEnough()
	{
		Transform mount = _player.CameraMount;
		if ( mount == null )
			return false;

		Vector3 cameraPos;
		if ( !TryResolveCameraWorldPosition( out cameraPos ) )
			return false;

		return ( cameraPos - mount.position ).sqrMagnitude >= hideDistance * hideDistance;
	}

	static bool TryResolveCameraWorldPosition( out Vector3 position )
	{
		position = Vector3.zero;
		if ( GameMode.Instance == null || GameMode.Instance.cameraController == null )
			return false;

		CameraController rig = GameMode.Instance.cameraController;
		FirstPersonCameraController firstPerson = rig.FirstPerson;
		if ( firstPerson != null && firstPerson.Camera != null )
		{
			position = firstPerson.Camera.transform.position;
			return true;
		}

		position = rig.transform.position;
		return true;
	}

	static bool IsMinecartOrbitActive()
	{
		if ( GameMode.Instance == null || GameMode.Instance.cameraController == null )
			return false;

		MinecartOrbitCamera orbit = GameMode.Instance.cameraController.GetComponent<MinecartOrbitCamera>();
		return orbit != null && orbit.IsActive;
	}

	void CacheRenderers()
	{
		if ( modelRoot == null )
		{
			_renderers = System.Array.Empty<Renderer>();
			return;
		}

		_renderers = modelRoot.GetComponentsInChildren<Renderer>( true );
	}

	void CacheAnimatorParam()
	{
		_hasSpeedParam = false;
		if ( animator == null || string.IsNullOrEmpty( speedParam ) )
			return;

		_speedParamHash = Animator.StringToHash( speedParam );
		for ( int i = 0; i < animator.parameterCount; i++ )
		{
			AnimatorControllerParameter param = animator.GetParameter( i );
			if ( param.nameHash == _speedParamHash && param.type == AnimatorControllerParameterType.Float )
			{
				_hasSpeedParam = true;
				return;
			}
		}
	}

	void SetRenderersEnabled( bool enabled )
	{
		if ( _renderers == null )
			return;

		for ( int i = 0; i < _renderers.Length; i++ )
		{
			Renderer renderer = _renderers[ i ];
			if ( renderer != null )
				renderer.enabled = enabled;
		}
	}
}
