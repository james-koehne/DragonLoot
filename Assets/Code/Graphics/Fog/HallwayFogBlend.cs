using UnityEngine;

/// <summary>
/// Blends intro hallway fog from thick density / zero area-light fog tint toward
/// <see cref="EnvironmentDefinition"/> and <see cref="AreaLightingDefinition"/> values
/// as the player walks along a local Z axis. Entering the ledge volume or reaching the
/// end completes any remaining fade to base fog before locking.
/// </summary>
public class HallwayFogBlend : MonoBehaviour
{
	const float SameZEpsilon = 0.05f;

	static HallwayFogBlend _active;

	[SerializeField]
	Transform _blendReference;

	[SerializeField]
	[Tooltip( "Local Z on _blendReference where fog is thickest (negative values are fine). Not world Z." )]
	float _startZ;

	[SerializeField]
	[Tooltip( "Local Z on _blendReference where fog reaches EnvironmentDefinition density." )]
	float _endZ = 20f;

	[SerializeField]
	[Tooltip( "On enable, set _startZ from LevelSceneMarkers.playerSpawn local Z on the blend reference." )]
	bool _snapStartToSpawnOnEnable;

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Fog density at the hallway start. End density comes from EnvironmentDefinition." )]
	float _startFogDensity = 0.02f;

	[SerializeField]
	[Tooltip( "Maps hallway T to density blend. 0 = start density, 1 = base density." )]
	AnimationCurve _densityCurve = AnimationCurve.Linear( 0f, 0f, 1f, 1f );

	[SerializeField]
	[Tooltip( "Maps hallway T to area-light fog influence scale. Keep near 0 until fog clears." )]
	AnimationCurve _fogInfluenceCurve = new AnimationCurve(
		new Keyframe( 0f, 0f ),
		new Keyframe( 0.75f, 0f ),
		new Keyframe( 1f, 1f ) );

	[SerializeField]
	[Tooltip( "Locks blend when this volume id is entered. Default intro ledge volume." )]
	string _lockVolumeId = EventSceneAutoWire.IdVolumeHallwayEnd;

	[SerializeField]
	[Tooltip( "Also lock when hallway T reaches 1 from player position." )]
	bool _lockWhenPastEnd = true;

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "Seconds to finish fading fog to base after entering the lock volume if not already at T=1." )]
	float _ledgeCompleteDuration = 2f;

	EnvironmentDefinition _environmentDefinition;
	bool _locked;
	bool _subscribed;
	bool _playerPlaced;
	bool _completingFade;
	float _completeStartT;
	float _completeElapsed;
	float _currentT;
	float _currentDensity;
	float _currentFogInfluenceScale = 1f;
	float _playerLocalZ;

	public static HallwayFogBlend Active => _active;

	public float PlayerLocalZ => _playerLocalZ;

	public float StartZ => _startZ;

	public float EndZ => _endZ;

	public float CurrentT => _currentT;

	public float CurrentDensity => _currentDensity;

	public float CurrentFogInfluenceScale => _currentFogInfluenceScale;

	public bool IsLocked => _locked;

	public bool IsBlendReady => _playerPlaced;

	public bool IsCompletingFade => _completingFade;

	void Awake()
	{
		if ( _blendReference == null )
			_blendReference = transform;
	}

	void OnEnable()
	{
		_active = this;
		if ( _snapStartToSpawnOnEnable )
			SnapStartZToPlayerSpawn();
		Subscribe();
		ApplyFromPlayer( allowLock: false );
	}

	void OnDisable()
	{
		Unsubscribe();
		ClearOverrides();
		if ( _active == this )
			_active = null;
	}

	void LateUpdate()
	{
		if ( _locked )
			return;

		ApplyFromPlayer( allowLock: true );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;

		EventBus.Subscribe<VolumeEnteredEvent>( OnVolumeEntered );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;

		EventBus.Unsubscribe<VolumeEnteredEvent>( OnVolumeEntered );
		_subscribed = false;
	}

	void OnVolumeEntered( VolumeEnteredEvent evt )
	{
		if ( _locked || string.IsNullOrEmpty( _lockVolumeId ) || !_playerPlaced )
			return;

		if ( evt.VolumeId != _lockVolumeId )
			return;

		BeginCompleteFade();
	}

	void BeginCompleteFade()
	{
		if ( _locked || _completingFade )
			return;

		if ( _currentT >= 1f - SameZEpsilon )
		{
			LockAtBase();
			return;
		}

		_completingFade = true;
		_completeStartT = _currentT;
		_completeElapsed = 0f;
	}

	void TickCompleteFade()
	{
		_completeElapsed += Time.deltaTime;
		float duration = Mathf.Max( 0.01f, _ledgeCompleteDuration );
		float normalized = Mathf.Clamp01( _completeElapsed / duration );
		float t = Mathf.Lerp( _completeStartT, 1f, normalized );
		ApplyBlend( t );

		if ( normalized >= 1f - SameZEpsilon )
			LockAtBase();
	}

	void ApplyFromPlayer( bool allowLock )
	{
		PlayerController player = null;
		if ( GameMode.Instance != null )
			player = GameMode.Instance.Player;

		if ( !_playerPlaced )
		{
			ApplyBlend( 0f );
			return;
		}

		if ( _completingFade )
		{
			TickCompleteFade();
			return;
		}

		if ( player == null )
		{
			ApplyBlend( 0f );
			return;
		}

		Transform reference = _blendReference != null ? _blendReference : transform;
		_playerLocalZ = reference.InverseTransformPoint( player.transform.position ).z;
		float t = SampleHallwayT( _playerLocalZ );
		ApplyBlend( t );

		if ( allowLock && _lockWhenPastEnd && t >= 1f - SameZEpsilon )
			LockAtBase();
	}

	public void NotifyPlayerPlaced()
	{
		_playerPlaced = true;
	}

	void ApplyBlend( float hallwayT )
	{
		_currentT = Mathf.Clamp01( hallwayT );
		float densityBlend = EvaluateCurve( _densityCurve, _currentT );
		float baseDensity = ResolveBaseFogDensity();
		_currentDensity = Mathf.Lerp( _startFogDensity, baseDensity, densityBlend );
		_currentFogInfluenceScale = EvaluateCurve( _fogInfluenceCurve, _currentT );

		EnvironmentDefinition.SetFogDensityOverride( _currentDensity );
		AreaLightingDefinition.SetRuntimeFogInfluenceScale( _currentFogInfluenceScale );
	}

	void LockAtBase()
	{
		if ( _locked )
			return;

		_locked = true;
		_currentT = 1f;
		_currentDensity = ResolveBaseFogDensity();
		_currentFogInfluenceScale = 1f;
		ClearOverrides();
	}

	void ClearOverrides()
	{
		EnvironmentDefinition.ClearFogDensityOverride();
		AreaLightingDefinition.ClearRuntimeFogInfluenceScale();

		EnvironmentDefinition env = RuntimeDefinition.Resolve( ref _environmentDefinition );
		if ( env != null )
			RenderSettings.fogDensity = env.fogDensity;
	}

	float SampleHallwayT( float localZ )
	{
		float span = _endZ - _startZ;
		if ( Mathf.Abs( span ) <= SameZEpsilon )
			return 1f;

		return Mathf.Clamp01( ( localZ - _startZ ) / span );
	}

	[ContextMenu( "Snap Start Z To Player Spawn" )]
	public void SnapStartZToPlayerSpawn()
	{
		LevelSceneMarkers markers = LevelSceneMarkers.Instance;
		if ( markers == null || markers.playerSpawn == null )
			return;

		Transform reference = _blendReference != null ? _blendReference : transform;
		_startZ = reference.InverseTransformPoint( markers.playerSpawn.position ).z;
	}

	float ResolveBaseFogDensity()
	{
		EnvironmentDefinition env = RuntimeDefinition.Resolve( ref _environmentDefinition );
		return env != null ? env.fogDensity : 0.002f;
	}

	static float EvaluateCurve( AnimationCurve curve, float t )
	{
		if ( curve == null || curve.length == 0 )
			return t;

		return Mathf.Clamp01( curve.Evaluate( t ) );
	}

	public void ForceApply()
	{
		if ( _locked )
			return;

		ApplyFromPlayer( allowLock: true );
	}

	public void DebugLockAtBase()
	{
		_playerPlaced = true;
		_completingFade = false;
		if ( _locked )
			return;

		LockAtBase();
	}

	public void DebugReset()
	{
		_locked = false;
		_playerPlaced = false;
		_completingFade = false;
		_completeElapsed = 0f;
		ApplyFromPlayer( allowLock: true );
	}

	public static void DebugResetAll()
	{
		if ( _active != null )
			_active.DebugReset();
	}

#if UNITY_EDITOR
	void OnDrawGizmosSelected()
	{
		Transform reference = _blendReference != null ? _blendReference : transform;
		DrawBlendPlane( reference, _startZ, new Color( 0.9f, 0.35f, 0.2f, 0.85f ) );
		DrawBlendPlane( reference, _endZ, new Color( 0.2f, 0.85f, 0.45f, 0.85f ) );

		LevelSceneMarkers markers = LevelSceneMarkers.Instance;
		if ( markers != null && markers.playerSpawn != null )
		{
			float spawnLocalZ = reference.InverseTransformPoint( markers.playerSpawn.position ).z;
			DrawBlendPlane( reference, spawnLocalZ, new Color( 0.95f, 0.9f, 0.2f, 0.85f ) );
		}

		if ( Application.isPlaying && GameMode.Instance != null && GameMode.Instance.Player != null )
		{
			float playerLocalZ = reference.InverseTransformPoint( GameMode.Instance.Player.transform.position ).z;
			DrawBlendPlane( reference, playerLocalZ, new Color( 0.35f, 0.65f, 1f, 0.85f ) );
		}
	}

	static void DrawBlendPlane( Transform reference, float localZ, Color color )
	{
		Vector3 center = reference.TransformPoint( new Vector3( 0f, 0f, localZ ) );
		Vector3 right = reference.right * 4f;
		Vector3 up = reference.up * 3f;

		Gizmos.color = color;
		Gizmos.DrawLine( center - right - up, center + right - up );
		Gizmos.DrawLine( center + right - up, center + right + up );
		Gizmos.DrawLine( center + right + up, center - right + up );
		Gizmos.DrawLine( center - right + up, center - right - up );
	}
#endif
}
