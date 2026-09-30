using System.Collections;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Sip a held coffee cup with ContextualInteract when no other E focus is available.
/// Raises/tilts the cup to camera, plays random sip SFX, then refreshes move-speed buff.
/// </summary>
public class PlayerCoffeeSip : MonoBehaviour
{
	const string SipClipAAddress = "Assets/Audio/SFX/Machines/pixabay_sipping-coffee-6063.mp3";
	const string SipClipBAddress = "Assets/Audio/SFX/Machines/pixabay_slurping-coffee-46191.mp3";

	[SerializeField]
	CoffeeMachineDefinition definition;

	[SerializeField]
	Feedbacks sipFeedbacks;

	PlayerController _player;
	CoffeeMachineDefinition _resolvedDefinition;
	Coroutine _sipRoutine;
	bool _clipsLoadStarted;
	PlayRandomSFXFeedback _randomSfx;

	public bool IsSipping => _sipRoutine != null;

	public bool CanOfferSip
	{
		get
		{
			if ( IsSipping )
				return false;

			if ( _player == null || _player.Carry == null )
				return false;

			if ( !TryGetHeldCup( out CoffeeCupState cup, out _ ) || cup == null || !cup.HasSipsRemaining )
				return false;

			PlayerInteraction interaction = _player.Interaction;
			if ( interaction == null )
				return false;

			IInteractable focus = interaction.Current;
			if ( focus != null && focus.CanInteract( _player ) && !InteractableBase.IsPickupInteract( focus ) )
				return false;

			return true;
		}
	}

	CoffeeMachineDefinition ResolvedDefinition
	{
		get
		{
			if ( definition != null )
				return definition;
			return RuntimeDefinition.Resolve( ref _resolvedDefinition );
		}
	}

	public void Setup( PlayerController player )
	{
		_player = player;
		EnsureSipFeedbacks();
	}

	void Update()
	{
		if ( _player == null || IsSipping )
			return;

		GameInput input = GetGameInput();
		if ( input == null || input.ContextualInteract == null )
			return;

		if ( !input.ContextualInteract.WasPressedThisFrame() )
			return;

		if ( !CanOfferSip )
			return;

		_sipRoutine = StartCoroutine( SipRoutine() );
	}

	static GameInput GetGameInput()
	{
		return InputController.Instance != null ? InputController.Instance.GameInput : null;
	}

	IEnumerator SipRoutine()
	{
		if ( !TryGetHeldCup( out CoffeeCupState cup, out TreasureItem item ) || cup == null || item == null )
		{
			_sipRoutine = null;
			yield break;
		}

		if ( !cup.TrySip() )
		{
			_sipRoutine = null;
			yield break;
		}

		PlaySipFeedback();

		CoffeeMachineDefinition def = ResolvedDefinition;
		Vector3 sipLocalPos = RuntimeDefinition.GetVector3( def, d => d.sipLocalPosition, new Vector3( 0.08f, -0.12f, 0.32f ) );
		Vector3 sipLocalEuler = RuntimeDefinition.GetVector3( def, d => d.sipLocalEuler, new Vector3( -35f, 15f, 0f ) );
		float raiseSeconds = RuntimeDefinition.Get( def, d => d.sipRaiseSeconds, 0.18f );
		float holdSeconds = RuntimeDefinition.Get( def, d => d.sipHoldSeconds, 0.22f );
		float returnSeconds = RuntimeDefinition.Get( def, d => d.sipReturnSeconds, 0.2f );
		float buffMul = RuntimeDefinition.Get( def, d => d.sipBuffMultiplier, 1.25f );
		float buffDur = RuntimeDefinition.Get( def, d => d.sipBuffDurationSeconds, 30f );

		Transform cupXf = item.transform;
		Transform cam = ResolveCameraTransform();
		Vector3 startLocalPos = cupXf.localPosition;
		Quaternion startLocalRot = cupXf.localRotation;

		if ( cam != null )
		{
			Vector3 sipPos = cam.TransformPoint( sipLocalPos );
			Quaternion sipRot = cam.rotation * Quaternion.Euler( sipLocalEuler );
			yield return TweenWorldPose( cupXf, cupXf.position, cupXf.rotation, sipPos, sipRot, raiseSeconds );
			if ( holdSeconds > 0f )
				yield return new WaitForSeconds( holdSeconds );
			yield return TweenLocalPose( cupXf, cupXf.localPosition, cupXf.localRotation, startLocalPos, startLocalRot, returnSeconds );
		}

		_player.ApplyMoveSpeedBuff( buffMul, buffDur );

		if ( !cup.HasSipsRemaining )
		{
			PlayerCarry carry = _player.Carry;
			if ( carry != null && carry.TryConsumeActive( out TreasureItem consumed ) && consumed != null )
				TreasureItemFactory.Despawn( consumed );
		}

		_sipRoutine = null;
	}

	Transform ResolveCameraTransform()
	{
		if ( _player == null )
			return null;

		if ( _player.CameraMount != null )
			return _player.CameraMount;

		Camera cam = Camera.main;
		return cam != null ? cam.transform : null;
	}

	static IEnumerator TweenWorldPose(
		Transform target,
		Vector3 startPos,
		Quaternion startRot,
		Vector3 endPos,
		Quaternion endRot,
		float duration )
	{
		if ( target == null )
			yield break;

		float seconds = Mathf.Max( 0.05f, duration );
		float elapsed = 0f;
		while ( elapsed < seconds )
		{
			if ( target == null )
				yield break;

			elapsed += Time.deltaTime;
			float u = CoinFlipMotion.SmoothStep( Mathf.Clamp01( elapsed / seconds ) );
			target.SetPositionAndRotation(
				Vector3.Lerp( startPos, endPos, u ),
				Quaternion.Slerp( startRot, endRot, u ) );
			yield return null;
		}

		if ( target != null )
			target.SetPositionAndRotation( endPos, endRot );
	}

	static IEnumerator TweenLocalPose(
		Transform target,
		Vector3 startLocalPos,
		Quaternion startLocalRot,
		Vector3 endLocalPos,
		Quaternion endLocalRot,
		float duration )
	{
		if ( target == null )
			yield break;

		float seconds = Mathf.Max( 0.05f, duration );
		float elapsed = 0f;
		while ( elapsed < seconds )
		{
			if ( target == null )
				yield break;

			elapsed += Time.deltaTime;
			float u = CoinFlipMotion.SmoothStep( Mathf.Clamp01( elapsed / seconds ) );
			target.localPosition = Vector3.Lerp( startLocalPos, endLocalPos, u );
			target.localRotation = Quaternion.Slerp( startLocalRot, endLocalRot, u );
			yield return null;
		}

		if ( target != null )
		{
			target.localPosition = endLocalPos;
			target.localRotation = endLocalRot;
		}
	}

	bool TryGetHeldCup( out CoffeeCupState cup, out TreasureItem item )
	{
		cup = null;
		item = null;
		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry == null || !carry.TryPeekActive( out item ) || item == null )
			return false;

		cup = item.GetComponent<CoffeeCupState>();
		return cup != null;
	}

	void EnsureSipFeedbacks()
	{
		if ( sipFeedbacks == null )
		{
			Transform existing = transform.Find( "OnCoffeeSipFeedbacks" );
			GameObject host = existing != null ? existing.gameObject : new GameObject( "OnCoffeeSipFeedbacks" );
			if ( existing == null )
				host.transform.SetParent( transform, false );

			sipFeedbacks = host.GetComponent<Feedbacks>();
			if ( sipFeedbacks == null )
				sipFeedbacks = host.AddComponent<Feedbacks>();
		}

		sipFeedbacks.Initialize();

		_randomSfx = null;
		for ( int i = 0; i < sipFeedbacks.FeedbackList.Count; i++ )
		{
			_randomSfx = sipFeedbacks.FeedbackList[ i ] as PlayRandomSFXFeedback;
			if ( _randomSfx != null )
				break;
		}

		if ( _randomSfx == null )
		{
			_randomSfx = new PlayRandomSFXFeedback();
			_randomSfx.SpatialBlend = 0f;
			sipFeedbacks.AddFeedback( _randomSfx );
		}

		ApplySipVolumeFromDefinition();
		EnsureSipClipsLoaded();
	}

	void ApplySipVolumeFromDefinition()
	{
		if ( _randomSfx == null )
			return;

		CoffeeMachineDefinition def = ResolvedDefinition;
		float volMin = RuntimeDefinition.Get( def, d => d.sipVolumeMin, 1.5f );
		float volMax = RuntimeDefinition.Get( def, d => d.sipVolumeMax, 1.5f );
		_randomSfx.VolumeMin = volMin;
		_randomSfx.VolumeMax = volMax;
	}

	void EnsureSipClipsLoaded()
	{
		if ( _randomSfx == null )
			return;

		CoffeeMachineDefinition def = ResolvedDefinition;
		if ( def != null && ( def.sipClipA != null || def.sipClipB != null ) )
		{
			AssignClips( def.sipClipA, def.sipClipB );
			return;
		}

		if ( _randomSfx.Clips != null && _randomSfx.Clips.Length >= 2
			&& _randomSfx.Clips[ 0 ] != null && _randomSfx.Clips[ 1 ] != null )
			return;

		if ( _clipsLoadStarted )
			return;

		_clipsLoadStarted = true;
		LoadClip( SipClipAAddress, 0 );
		LoadClip( SipClipBAddress, 1 );
	}

	void AssignClips( AudioClip a, AudioClip b )
	{
		if ( _randomSfx == null )
			return;

		AudioClip[] clips = new AudioClip[ 2 ];
		clips[ 0 ] = a;
		clips[ 1 ] = b != null ? b : a;
		_randomSfx.Clips = clips;
	}

	void LoadClip( string address, int index )
	{
		AsyncOperationHandle<AudioClip> handle = Addressables.LoadAssetAsync<AudioClip>( address );
		handle.Completed += op =>
		{
			if ( op.Status != AsyncOperationStatus.Succeeded || op.Result == null || _randomSfx == null )
				return;

			AudioClip[] clips = _randomSfx.Clips;
			if ( clips == null || clips.Length < 2 )
				clips = new AudioClip[ 2 ];

			clips[ index ] = op.Result;
			_randomSfx.Clips = clips;
		};
	}

	void PlaySipFeedback()
	{
		EnsureSipFeedbacks();
		ApplySipVolumeFromDefinition();
		if ( sipFeedbacks == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = transform.position;
		sipFeedbacks.Play( context );
	}
}
