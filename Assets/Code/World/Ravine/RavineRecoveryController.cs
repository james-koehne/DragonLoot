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
	float PlayerCapsulePad => RuntimeDefinition.Get( Definition, d => d.playerCapsulePad, 0.05f );
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

		Vector3 start = player.transform.position;
		Vector3 end = _safePosition;
		float duration = Mathf.Max( ArcDuration, 0.05f );
		float clearance = Mathf.Max( ArcClearanceAboveTarget, 0.05f );
		float peakY = end.y + clearance;
		float hopHeight = peakY - Mathf.Max( start.y, end.y );
		hopHeight = Mathf.Max( hopHeight, 0.05f );
		float secondaryArc = Mathf.Min( ArcHeight, clearance * 0.35f );
		float elapsed = 0f;
		Vector3 previous = start;

		CharacterController cc = player.GetComponent<CharacterController>();
		float radius = 0.3f;
		float heightCc = 1.8f;
		float skin = 0.08f;
		Vector3 center = Vector3.up * 0.9f;
		if ( cc != null )
		{
			radius = cc.radius;
			heightCc = cc.height;
			skin = cc.skinWidth;
			center = cc.center;
		}

		float pad = PlayerCapsulePad;
		radius = Mathf.Max( 0.05f, radius - skin + pad );

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			Vector3 desired = CoinFlipMotion.EvaluateHopThenArcPosition( start, end, u, hopHeight, 0.45f, secondaryArc );
			Vector3 resolved = ResolveArcPosition( previous, desired, center, heightCc, radius );
			Vector3 delta = resolved - player.transform.position;
			player.SnapToWorldPosition( resolved );
			player.NotifyCinematicTravel( delta, Time.deltaTime );
			previous = resolved;
			yield return null;
		}

		Vector3 finalPos = ResolveArcPosition( previous, end, center, heightCc, radius );
		Vector3 finalDelta = finalPos - player.transform.position;
		player.SnapToWorldPosition( finalPos );
		player.NotifyCinematicTravel( finalDelta, Time.deltaTime );

		player.SetCinematicBodyLock( false );
		// SetCinematicBodyLock(false) already settles; ensure jump is restored if settle didn't see ground yet.
		player.SettleStationary();

		_recovering = false;
		_recoveryRoutine = null;

		// CC was disabled during the arc, so OnTriggerExit often never fired — rearm explicitly.
		RavineFallTrigger.RearmAll();
	}

	static Vector3 ResolveArcPosition(
		Vector3 from,
		Vector3 to,
		Vector3 capsuleCenter,
		float capsuleHeight,
		float radius )
	{
		Vector3 delta = to - from;
		float distance = delta.magnitude;
		if ( distance <= 1e-5f )
			return to;

		Vector3 direction = delta / distance;
		GetCapsuleEnds( from, capsuleCenter, capsuleHeight, radius, out Vector3 p1, out Vector3 p2 );

		if ( Physics.CapsuleCast(
			    p1,
			    p2,
			    radius,
			    direction,
			    out RaycastHit hit,
			    distance,
			    Physics.DefaultRaycastLayers,
			    QueryTriggerInteraction.Ignore ) )
		{
			float travel = Mathf.Max( 0f, hit.distance - 0.02f );
			return from + direction * travel;
		}

		GetCapsuleEnds( to, capsuleCenter, capsuleHeight, radius, out Vector3 endP1, out Vector3 endP2 );
		if ( Physics.CheckCapsule( endP1, endP2, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore ) )
		{
			float lo = 0f;
			float hi = 1f;
			Vector3 best = from;
			for ( int i = 0; i < 8; i++ )
			{
				float mid = ( lo + hi ) * 0.5f;
				Vector3 sample = Vector3.Lerp( from, to, mid );
				GetCapsuleEnds( sample, capsuleCenter, capsuleHeight, radius, out Vector3 s1, out Vector3 s2 );
				if ( Physics.CheckCapsule( s1, s2, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore ) )
					hi = mid;
				else
				{
					best = sample;
					lo = mid;
				}
			}

			return best;
		}

		return to;
	}

	static void GetCapsuleEnds( Vector3 position, Vector3 center, float height, float radius, out Vector3 p1, out Vector3 p2 )
	{
		float half = Mathf.Max( height * 0.5f - radius, 0f );
		Vector3 worldCenter = position + center;
		p1 = worldCenter + Vector3.up * half;
		p2 = worldCenter - Vector3.up * half;
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
