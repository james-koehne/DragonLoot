using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Distance-based footstep one-shots plus jump / landing SFX from <see cref="AudioDefinition"/>.
/// Sharp planar heading changes also plant an extra step. Clips and ranges are read live from the definition.
/// </summary>
[DisallowMultipleComponent]
public class PlayerFootsteps : MonoBehaviour
{
	const float MinSpeedForSteps = 0.35f;

	[SerializeField]
	[Tooltip( "Optional override. When null, resolves AudioDefinition via Addressables." )]
	AudioDefinition _definition;

	PlayerController _player;
	Feedbacks _groundFeedbacks;
	Feedbacks _goldPileFeedbacks;
	Feedbacks _jumpFeedbacks;
	PlayRandomSFXFeedback _groundSfx;
	PlayRandomSFXFeedback _goldPileSfx;
	PlayRandomSFXFeedback _jumpSfx;
	float _distanceAccumulator;
	Vector3 _lastTurnDir;
	bool _hasTurnDir;

	AudioDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	void Awake()
	{
		_player = GetComponent<PlayerController>();
		EnsureFeedbackHosts();
	}

	void LateUpdate()
	{
		if ( _player == null )
			_player = GetComponent<PlayerController>();
		if ( _player == null )
			return;

		if ( _player.WasJumpThisFrame )
			PlayJump();

		if ( _player.WasLandingThisFrame )
		{
			_distanceAccumulator = 0f;
			ClearTurnDir();
			PlayStep( landing: true );
			return;
		}

		if ( !_player.IsGrounded
		     || _player.MovementState == PlayerMovementState.Sliding
		     || _player.MovementState == PlayerMovementState.Airborne
		     || _player.MovementState == PlayerMovementState.Gliding )
		{
			_distanceAccumulator = 0f;
			ClearTurnDir();
			return;
		}

		float speed = _player.PlanarSpeed;
		if ( speed < MinSpeedForSteps )
		{
			_distanceAccumulator = 0f;
			ClearTurnDir();
			return;
		}

		AudioDefinition def = Definition;
		if ( TryPlayTurnStep( def ) )
			return;

		float stride = def != null ? def.stepStrideDistance : 1.1f;
		if ( stride < 0.05f )
			stride = 1.1f;

		_distanceAccumulator += speed * Time.deltaTime;
		if ( _distanceAccumulator < stride )
			return;

		_distanceAccumulator -= stride;
		PlayStep( landing: false );
	}

	bool TryPlayTurnStep( AudioDefinition def )
	{
		float threshold = def != null ? def.stepTurnDegrees : 90f;
		Vector3 intent = _player.FlatMoveIntent;
		intent.y = 0f;
		bool hasIntent = intent.sqrMagnitude > 0.0001f;
		if ( !hasIntent )
			return false;

		Vector3 moveDir = intent.normalized;
		bool played = false;
		if ( _hasTurnDir && threshold > 0.01f )
		{
			float angle = Vector3.Angle( _lastTurnDir, moveDir );
			if ( angle >= threshold )
			{
				_distanceAccumulator = 0f;
				PlayStep( landing: false );
				played = true;
			}
		}

		_lastTurnDir = moveDir;
		_hasTurnDir = true;
		return played;
	}

	void ClearTurnDir()
	{
		_hasTurnDir = false;
		_lastTurnDir = Vector3.zero;
	}

	void PlayStep( bool landing )
	{
		AudioDefinition def = Definition;
		if ( def == null )
			return;

		float volumeMin = landing ? def.landStepVolumeMin : def.stepVolumeMin;
		float volumeMax = landing ? def.landStepVolumeMax : def.stepVolumeMax;

		bool onGoldPile = IsOnGoldPile(
			_player != null ? _player.GroundCollider : null,
			_player != null ? _player.transform.position : Vector3.zero );
		if ( onGoldPile )
		{
			ApplyAndPlay(
				_goldPileFeedbacks,
				_goldPileSfx,
				def.goldPileStepClips,
				volumeMin,
				volumeMax,
				def.stepPitchMin,
				def.stepPitchMax );
		}
		else
		{
			ApplyAndPlay(
				_groundFeedbacks,
				_groundSfx,
				def.groundStepClips,
				volumeMin,
				volumeMax,
				def.stepPitchMin,
				def.stepPitchMax );
		}
	}

	void PlayJump()
	{
		AudioDefinition def = Definition;
		if ( def == null )
			return;

		ApplyAndPlay(
			_jumpFeedbacks,
			_jumpSfx,
			def.jumpClips,
			def.jumpVolumeMin,
			def.jumpVolumeMax,
			def.jumpPitchMin,
			def.jumpPitchMax );

		PlayStep( landing: false );
	}

	static void ApplyAndPlay(
		Feedbacks feedbacks,
		PlayRandomSFXFeedback sfx,
		AudioClip[] clips,
		float volumeMin,
		float volumeMax,
		float pitchMin,
		float pitchMax )
	{
		if ( feedbacks == null || sfx == null )
			return;
		if ( clips == null || clips.Length == 0 )
			return;

		sfx.Clips = clips;
		sfx.VolumeMin = volumeMin;
		sfx.VolumeMax = volumeMax;
		sfx.PitchMin = pitchMin;
		sfx.PitchMax = pitchMax;
		feedbacks.Play();
	}

	static bool IsOnGoldPile( Collider ground, Vector3 worldPos )
	{
		if ( ground == null )
			return false;

		if ( !PlacementFloorSurface.IsHeightfieldPileCollider( ground ) )
			return false;

		TreasurePileVisual pile = ground.GetComponentInParent<TreasurePileVisual>();
		if ( pile == null )
		{
			TreasurePileInteractable interactable = ground.GetComponentInParent<TreasurePileInteractable>();
			if ( interactable != null )
				pile = interactable.PileVisual;
		}

		if ( pile == null )
			return false;

		return pile.HasPileSurfaceAt( worldPos );
	}

	void EnsureFeedbackHosts()
	{
		if ( _groundFeedbacks == null )
		{
			GameObject groundHost = new GameObject( "FootstepGroundFeedbacks" );
			groundHost.transform.SetParent( transform, false );
			_groundFeedbacks = groundHost.AddComponent<Feedbacks>();
			_groundSfx = new PlayRandomSFXFeedback();
			_groundFeedbacks.AddFeedback( _groundSfx );
		}

		if ( _goldPileFeedbacks == null )
		{
			GameObject pileHost = new GameObject( "FootstepGoldPileFeedbacks" );
			pileHost.transform.SetParent( transform, false );
			_goldPileFeedbacks = pileHost.AddComponent<Feedbacks>();
			_goldPileSfx = new PlayRandomSFXFeedback();
			_goldPileFeedbacks.AddFeedback( _goldPileSfx );
		}

		if ( _jumpFeedbacks == null )
		{
			GameObject jumpHost = new GameObject( "JumpFeedbacks" );
			jumpHost.transform.SetParent( transform, false );
			_jumpFeedbacks = jumpHost.AddComponent<Feedbacks>();
			_jumpSfx = new PlayRandomSFXFeedback();
			_jumpFeedbacks.AddFeedback( _jumpSfx );
		}
	}
}
