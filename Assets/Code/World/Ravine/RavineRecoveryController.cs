using System.Collections;

using UnityEngine;

/// <summary>
/// Caches last safe grounded pose near a ravine and arcs the player back when a fall trigger fires.
/// </summary>
[DisallowMultipleComponent]
public class RavineRecoveryController : MonoBehaviour
{
	public static RavineRecoveryController Active { get; private set; }

	[SerializeField]
	RavineDefinition _definition;

	[SerializeField]
	[Tooltip( "Optional. Used when no safe grounded pose has been cached yet." )]
	Transform _fallbackSafePoint;

	bool _hasSafePose;
	Vector3 _safePosition;
	bool _recovering;
	Coroutine _recoveryRoutine;

	RavineDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	float SafeGroundDistance => RuntimeDefinition.Get( Definition, d => d.safeGroundDistance, 1f );
	float FallContinueDuration => RuntimeDefinition.Get( Definition, d => d.fallContinueDuration, 0.85f );
	float ArcDuration => RuntimeDefinition.Get( Definition, d => d.arcDuration, 1.1f );
	float ArcHeight => RuntimeDefinition.Get( Definition, d => d.arcHeight, 2.5f );
	float ArcClearanceAboveTarget => RuntimeDefinition.Get( Definition, d => d.arcClearanceAboveTarget, 2f );
	float WalkGroundProbeDistance => RuntimeDefinition.Get( Definition, d => d.walkGroundProbeDistance, 1.25f );

	public bool IsRecovering => _recovering;
	public bool HasSafePose => _hasSafePose;
	public Vector3 SafePosition => _safePosition;

	void OnEnable()
	{
		Active = this;
	}

	void OnDisable()
	{
		if ( Active == this )
			Active = null;

		if ( _recoveryRoutine != null )
		{
			StopCoroutine( _recoveryRoutine );
			_recoveryRoutine = null;
		}

		_recovering = false;
	}

	void Update()
	{
		if ( _recovering )
			return;

		PlayerController player = ResolvePlayer();
		if ( player == null || !player.IsGrounded )
			return;

		TryCacheSafePose( player );
	}

	public void TryCacheSafePose( PlayerController player )
	{
		if ( player == null )
			return;

		GetPlayerCapsule( player, out Vector3 center, out float height, out float radius );
		if ( !RavineWalkableLedge.IsFarEnoughFromLedge(
			    player.transform.position,
			    SafeGroundDistance,
			    center,
			    height,
			    radius,
			    WalkGroundProbeDistance ) )
			return;

		_safePosition = player.transform.position;
		_hasSafePose = true;
	}

	static void GetPlayerCapsule( PlayerController player, out Vector3 center, out float height, out float radius )
	{
		center = Vector3.up * 0.9f;
		height = 1.8f;
		radius = 0.3f;

		CharacterController cc = player.GetComponent<CharacterController>();
		if ( cc == null )
			return;

		center = cc.center;
		height = cc.height;
		radius = Mathf.Max( 0.05f, cc.radius * 0.9f );
	}

	public bool BeginRecovery( PlayerController player )
	{
		if ( player == null || _recovering )
			return false;

		if ( !_hasSafePose )
		{
			if ( _fallbackSafePoint != null )
			{
				_safePosition = _fallbackSafePoint.position;
				_hasSafePose = true;
			}
			else if ( !TryResolveSpawnFallback( out _safePosition ) )
			{
				Debug.LogWarning( "RavineRecoveryController: no safe pose cached and no fallback; respawning." );
				if ( GameMode.Instance != null && GameMode.Instance.Game != null )
					GameMode.Instance.Game.RespawnPlayer();
				return false;
			}
			else
				_hasSafePose = true;
		}

		_recovering = true;
		_recoveryRoutine = StartCoroutine( RecoveryRoutine( player ) );
		return true;
	}

	IEnumerator RecoveryRoutine( PlayerController player )
	{
		// Keep falling under normal physics for a beat before yanking back.
		float fallContinue = Mathf.Max( FallContinueDuration, 0f );
		float fallElapsed = 0f;
		while ( fallElapsed < fallContinue )
		{
			fallElapsed += Time.deltaTime;
			yield return null;
		}

		player.SetCinematicBodyLock( true );

		// Ignore world collision while yanking back so overhangs cannot abort the recovery.
		Vector3 start = player.transform.position;
		Vector3 end = _safePosition;
		float duration = Mathf.Max( ArcDuration, 0.05f );
		float clearance = Mathf.Max( ArcClearanceAboveTarget, 0.05f );
		float peakY = end.y + clearance;
		float hopHeight = peakY - Mathf.Max( start.y, end.y );
		hopHeight = Mathf.Max( hopHeight, 0.05f );
		float secondaryArc = Mathf.Min( ArcHeight, clearance * 0.35f );
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			Vector3 desired = CoinFlipMotion.EvaluateHopThenArcPosition( start, end, u, hopHeight, 0.45f, secondaryArc );
			Vector3 delta = desired - player.transform.position;
			player.SnapToWorldPosition( desired );
			player.NotifyCinematicTravel( delta, Time.deltaTime );
			yield return null;
		}

		Vector3 finalDelta = end - player.transform.position;
		player.SnapToWorldPosition( end );
		player.NotifyCinematicTravel( finalDelta, Time.deltaTime );

		player.SetCinematicBodyLock( false );
		// SetCinematicBodyLock(false) already settles; ensure jump is restored if settle didn't see ground yet.
		player.SettleStationary();

		_recovering = false;
		_recoveryRoutine = null;

		// CC was disabled during the arc, so OnTriggerExit often never fired — rearm explicitly.
		RavineFallTrigger.RearmAll();
	}

	static bool TryResolveSpawnFallback( out Vector3 position )
	{
		position = Vector3.zero;
		LevelSceneMarkers markers = LevelSceneMarkers.Instance;
		if ( markers == null || markers.playerSpawn == null )
			return false;

		position = markers.playerSpawn.position;
		return true;
	}

	static PlayerController ResolvePlayer()
	{
		if ( GameMode.Instance == null )
			return null;

		return GameMode.Instance.Player;
	}
}
