using System.Collections;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Hold-E multi-step espresso craft: grind → tamp (incl. stow) → lock + cup under spout → brew → pickup.
/// Animated empty cup is visual-only; a filled pickup cup is spawned at CupReady after brew.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class CoffeeMachineInteractable : InteractableBase, ITreasureOwner
{
	public enum CraftStep
	{
		Grind = 0,
		Tamp = 1,
		LockAndCup = 2,
		Brew = 3,
		CupReady = 4
	}

	[Header( "Parts" )]
	[SerializeField]
	Transform portafilter;

	[SerializeField]
	Transform tamp;

	[Header( "Portafilter poses" )]
	[SerializeField]
	Transform portafilterRest;

	[SerializeField]
	Transform portafilterUnderGrinder;

	[SerializeField]
	Transform portafilterTamp;

	[SerializeField]
	Transform portafilterApproachHead;

	[SerializeField]
	Transform portafilterLocked;

	[Header( "Tamp poses" )]
	[SerializeField]
	Transform tampMat;

	[SerializeField]
	Transform tampLift;

	[SerializeField]
	Transform tampHover;

	[SerializeField]
	Transform tampPress;

	[Header( "Cup poses" )]
	[SerializeField]
	Transform cupPark;

	[SerializeField]
	Transform cupUnderSpout;

	[SerializeField]
	Transform cupReady;

	[Header( "Cup visuals" )]
	[Tooltip( "Cafe_Cup_1 — animated empty cup only (not pickable)." )]
	[FormerlySerializedAs( "emptyCup" )]
	[SerializeField]
	GameObject animatedCup;

	[Header( "Feedbacks" )]
	[SerializeField]
	Feedbacks grindFeedbacks;

	[SerializeField]
	Feedbacks tampFeedbacks;

	[SerializeField]
	Feedbacks lockFeedbacks;

	[SerializeField]
	Feedbacks brewFeedbacks;

	[Header( "Cup spawn" )]
	[Tooltip( "Filled Cafe_Cup_2 treasure definition used to spawn the grabable cup." )]
	[SerializeField]
	TreasureDefinition coffeeCupDefinition;

	[Header( "Definition" )]
	[SerializeField]
	CoffeeMachineDefinition definition;

	[Header( "Timing fallbacks (used when Definition is null)" )]
	[SerializeField]
	[Min( 0.1f )]
	float holdSeconds = 1f;

	[SerializeField]
	[Min( 0.05f )]
	float moveSeconds = 0.35f;

	[SerializeField]
	[Min( 0f )]
	float moveArcHeight = 0.08f;

	[SerializeField]
	[Min( 0.05f )]
	float tampPressSeconds = 0.22f;

	[SerializeField]
	[Min( 0f )]
	float tampHoldSeconds = 0.12f;

	[SerializeField]
	[Min( 0.05f )]
	float lockTwistSeconds = 0.4f;

	[SerializeField]
	[Min( 0.05f )]
	float resetMoveSeconds = 0.22f;

	CraftStep _step = CraftStep.Grind;
	float _charge;
	bool _busy;
	bool _resetQueued;
	Coroutine _routine;
	Feedbacks _activeFeedback;
	float _sfxWaitUntil;
	TreasureItem _pickupCup;
	CoffeeMachineDefinition _resolvedDefinition;

	float HoldSeconds => RuntimeDefinition.Get( ResolvedDefinition, d => d.holdSeconds, holdSeconds );
	float MoveSeconds => RuntimeDefinition.Get( ResolvedDefinition, d => d.moveSeconds, moveSeconds );
	float MoveArcHeight => RuntimeDefinition.Get( ResolvedDefinition, d => d.moveArcHeight, moveArcHeight );
	float TampPressSeconds => RuntimeDefinition.Get( ResolvedDefinition, d => d.tampPressSeconds, tampPressSeconds );
	float TampHoldSeconds => RuntimeDefinition.Get( ResolvedDefinition, d => d.tampHoldSeconds, tampHoldSeconds );
	float LockTwistSeconds => RuntimeDefinition.Get( ResolvedDefinition, d => d.lockTwistSeconds, lockTwistSeconds );
	float ResetMoveSeconds => RuntimeDefinition.Get( ResolvedDefinition, d => d.resetMoveSeconds, resetMoveSeconds );

	CoffeeMachineDefinition ResolvedDefinition
	{
		get
		{
			if ( definition != null )
				return definition;
			return RuntimeDefinition.Resolve( ref _resolvedDefinition );
		}
	}

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.CoffeeMachine;
	public CraftStep CurrentStep => _step;
	public bool IsBusy => _busy;
	public bool IsCharging => _charge > 0.001f && !_busy && _step != CraftStep.CupReady;
	public float ChargeProgress01
	{
		get
		{
			if ( !IsCharging )
				return 0f;
			float hold = Mathf.Max( 0.1f, HoldSeconds );
			return Mathf.Clamp01( _charge / hold );
		}
	}

	public bool AllowsCupPickup => _step == CraftStep.CupReady && !_busy && _pickupCup != null;

	public string HoldPromptLabel
	{
		get
		{
			switch ( _step )
			{
				case CraftStep.Grind:
					return "Hold to grind";
				case CraftStep.Tamp:
					return "Hold to tamp";
				case CraftStep.LockAndCup:
					return "Hold to lock";
				case CraftStep.Brew:
					return "Hold to brew";
				default:
					return null;
			}
		}
	}

	void Awake()
	{
		RefreshInteractionName();
		EnsureAnimatedCupInstance();
		SnapIdlePoses();
		ShowAnimatedCupAtPark();
		ClearPickupCup();
	}

	void OnDisable()
	{
		if ( _routine != null )
		{
			StopCoroutine( _routine );
			_routine = null;
		}

		_busy = false;
		_charge = 0f;
		_activeFeedback = null;
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !base.CanInteract( player ) || player == null )
			return false;

		if ( _busy || _step == CraftStep.CupReady )
			return false;

		return true;
	}

	public override void Interact( PlayerController player )
	{
		// Hold charge is driven by PlayerInteraction; Interact is a no-op.
	}

	public bool CanChargeHold( PlayerController player )
	{
		return CanInteract( player );
	}

	public void TickHoldCharge( float deltaTime )
	{
		if ( _busy || _step == CraftStep.CupReady )
		{
			_charge = 0f;
			return;
		}

		_charge += Mathf.Max( 0f, deltaTime );
		if ( _charge < Mathf.Max( 0.1f, HoldSeconds ) )
			return;

		_charge = 0f;
		BeginCurrentStep();
	}

	public void CancelHoldCharge()
	{
		_charge = 0f;
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		if ( item == null || item != _pickupCup )
			return;

		_pickupCup = null;
		if ( _step == CraftStep.CupReady && !_resetQueued )
			BeginResetAfterCupTaken();
	}

	void BeginCurrentStep()
	{
		if ( _busy || _step == CraftStep.CupReady )
			return;

		_busy = true;
		_charge = 0f;
		RefreshInteractionName();

		if ( _routine != null )
			StopCoroutine( _routine );

		_routine = StartCoroutine( RunStepRoutine( _step ) );
	}

	IEnumerator RunStepRoutine( CraftStep step )
	{
		switch ( step )
		{
			case CraftStep.Grind:
				yield return GrindRoutine();
				SetStep( CraftStep.Tamp );
				break;
			case CraftStep.Tamp:
				yield return TampRoutine();
				SetStep( CraftStep.LockAndCup );
				break;
			case CraftStep.LockAndCup:
				yield return LockAndCupRoutine();
				SetStep( CraftStep.Brew );
				break;
			case CraftStep.Brew:
				yield return BrewRoutine();
				yield return SpawnFilledCupAndDeliverRoutine();
				SetStep( CraftStep.CupReady );
				break;
		}

		_busy = false;
		_routine = null;
		_activeFeedback = null;
		RefreshInteractionName();
	}

	IEnumerator GrindRoutine()
	{
		PlayStepFeedback( grindFeedbacks );
		yield return MoveTransform( portafilter, portafilterUnderGrinder, MoveSeconds, MoveArcHeight );
		yield return WaitForStepFeedback();
		yield return MoveTransform( portafilter, portafilterTamp, MoveSeconds, MoveArcHeight * 0.5f );
	}

	IEnumerator TampRoutine()
	{
		PlayStepFeedback( tampFeedbacks );
		yield return MoveTransform( tamp, tampLift, MoveSeconds * 0.7f, 0f );
		yield return MoveTransform( tamp, tampHover, MoveSeconds, MoveArcHeight );
		yield return MoveTransform( tamp, tampPress, TampPressSeconds, 0f, easeIn: true );
		if ( TampHoldSeconds > 0f )
			yield return new WaitForSeconds( TampHoldSeconds );
		yield return MoveTransform( tamp, tampHover, TampPressSeconds, 0f );
		yield return MoveTransform( tamp, tampLift, MoveSeconds * 0.6f, MoveArcHeight * 0.5f );
		yield return MoveTransform( tamp, tampMat, MoveSeconds, MoveArcHeight );
		yield return WaitForStepFeedback();
	}

	IEnumerator LockAndCupRoutine()
	{
		PlayStepFeedback( lockFeedbacks );
		yield return MoveTransform( portafilter, portafilterApproachHead, MoveSeconds, MoveArcHeight );
		yield return MoveTransform( portafilter, portafilterLocked, LockTwistSeconds, 0f );

		EnsureAnimatedCupInstance();
		Transform cup = ResolveAnimatedCupTransform();
		if ( cup == null )
			Debug.LogWarning( "CoffeeMachineInteractable: Animated Cup is missing — assign a nested Cafe_Cup_1 instance (not the prefab asset).", this );
		else if ( cupUnderSpout == null )
			Debug.LogWarning( "CoffeeMachineInteractable: Cup Under Spout socket is not assigned.", this );
		else
			yield return MoveTransform( cup, cupUnderSpout, MoveSeconds, MoveArcHeight * 0.35f );

		yield return WaitForStepFeedback();
	}

	IEnumerator BrewRoutine()
	{
		// Cup is already under the spout from LockAndCup.
		PlayStepFeedback( brewFeedbacks );
		Transform cup = ResolveAnimatedCupTransform();
		if ( cup != null )
		{
			Vector3 startScale = cup.localScale;
			Vector3 endScale = startScale * 1.04f;
			float fillSeconds = Mathf.Max( 0.35f, GetActiveFeedbackWaitSeconds() );
			float elapsed = 0f;
			while ( elapsed < fillSeconds )
			{
				elapsed += Time.deltaTime;
				float u = CoinFlipMotion.SmoothStep( Mathf.Clamp01( elapsed / fillSeconds ) );
				cup.localScale = Vector3.Lerp( startScale, endScale, u );
				yield return null;
			}

			cup.localScale = startScale;
		}

		yield return WaitForStepFeedback();
	}

	IEnumerator SpawnFilledCupAndDeliverRoutine()
	{
		ClearPickupCup();
		HideAnimatedCup();

		if ( coffeeCupDefinition == null )
		{
			Debug.LogWarning( "CoffeeMachineInteractable: coffeeCupDefinition is not assigned.", this );
			yield break;
		}

		Transform spout = cupUnderSpout != null ? cupUnderSpout : transform;
		Transform ready = cupReady != null ? cupReady : spout;
		Vector3 spawnPos = spout.position;
		Quaternion spawnRot = spout.rotation;

		// Parent to this machine instance (scene object), never a prefab asset socket alone.
		TreasureItem spawned = TreasureItemFactory.SpawnSync( coffeeCupDefinition, spawnPos, spawnRot, transform );
		if ( spawned == null )
		{
			Debug.LogWarning( "CoffeeMachineInteractable: failed to spawn filled cup.", this );
			yield break;
		}

		_pickupCup = spawned;

		CoffeeCupState state = spawned.GetComponent<CoffeeCupState>();
		if ( state == null )
			state = spawned.gameObject.AddComponent<CoffeeCupState>();
		state.DisablePickup();

		if ( spawned.GetComponent<TreasureItemInteractable>() == null )
			spawned.gameObject.AddComponent<TreasureItemInteractable>();

		spawned.EnterDisplayed( this, transform, spawnPos, spawnRot );

		if ( ready != spout )
			yield return MoveTransform( spawned.transform, ready, MoveSeconds, MoveArcHeight * 0.25f );

		Vector3 readyPos = ready.position;
		Quaternion readyRot = ready.rotation;
		spawned.EnterDisplayed( this, transform, readyPos, readyRot );
		state.PrepareFilledCup();
	}

	void BeginResetAfterCupTaken()
	{
		_resetQueued = true;
		if ( _routine != null )
			StopCoroutine( _routine );
		_routine = StartCoroutine( ResetRoutine() );
	}

	IEnumerator ResetRoutine()
	{
		_busy = true;
		_charge = 0f;
		ClearPickupCup();

		yield return MoveTransform( portafilter, portafilterApproachHead, ResetMoveSeconds, 0f );
		yield return MoveTransform( portafilter, portafilterRest, ResetMoveSeconds, MoveArcHeight );
		SnapTo( tamp, tampMat );

		ShowAnimatedCupAtPark();
		SetStep( CraftStep.Grind );
		_busy = false;
		_resetQueued = false;
		_routine = null;
		RefreshInteractionName();
	}

	/// <summary>
	/// Animated cup must be a scene/prefab-instance child. Prefab-asset refs cannot be moved at runtime.
	/// </summary>
	void EnsureAnimatedCupInstance()
	{
		if ( animatedCup != null && animatedCup.scene.IsValid() )
			return;

		Transform existing = transform.Find( "AnimatedCup" );
		if ( existing == null )
		{
			for ( int i = 0; i < transform.childCount; i++ )
			{
				Transform child = transform.GetChild( i );
				if ( child != null && child.name.IndexOf( "Cup", System.StringComparison.OrdinalIgnoreCase ) >= 0
					&& child.name.IndexOf( "Ready", System.StringComparison.OrdinalIgnoreCase ) < 0 )
				{
					// Prefer an empty visual already nested under the machine.
					if ( child.GetComponent<TreasureItem>() == null )
					{
						existing = child;
						break;
					}
				}
			}
		}

		if ( existing != null )
		{
			animatedCup = existing.gameObject;
			return;
		}

		GameObject template = animatedCup;
		if ( template == null )
			return;

		GameObject instance = Instantiate( template, transform );
		instance.name = "AnimatedCup";
		animatedCup = instance;
	}

	void ShowAnimatedCupAtPark()
	{
		EnsureAnimatedCupInstance();
		if ( animatedCup == null )
			return;

		animatedCup.SetActive( true );
		Transform cup = animatedCup.transform;
		if ( cupPark != null )
			cup.SetPositionAndRotation( cupPark.position, cupPark.rotation );

		// Animated cup must never be pickable.
		CoffeeCupState state = animatedCup.GetComponent<CoffeeCupState>();
		if ( state != null )
			state.DisablePickup();

		TreasureItemInteractable interactable = animatedCup.GetComponent<TreasureItemInteractable>();
		if ( interactable != null )
			interactable.enabled = false;

		TreasureItem treasure = animatedCup.GetComponent<TreasureItem>();
		if ( treasure != null )
			treasure.enabled = false;
	}

	void HideAnimatedCup()
	{
		if ( animatedCup != null )
			animatedCup.SetActive( false );
	}

	void ClearPickupCup()
	{
		if ( _pickupCup == null )
			return;

		TreasureItem cup = _pickupCup;
		_pickupCup = null;

		// Only destroy cups we still own (not ones the player already took).
		if ( cup != null && ReferenceEquals( cup.Owner, this ) )
			TreasureItemFactory.Despawn( cup );
	}

	Transform ResolveAnimatedCupTransform()
	{
		return animatedCup != null ? animatedCup.transform : null;
	}

	void SetStep( CraftStep step )
	{
		_step = step;
		RefreshInteractionName();
	}

	void RefreshInteractionName()
	{
		string prompt = HoldPromptLabel;
		SetInteractionName( string.IsNullOrEmpty( prompt ) ? "Coffee machine" : prompt );
	}

	void SnapIdlePoses()
	{
		SnapTo( portafilter, portafilterRest );
		SnapTo( tamp, tampMat );
	}

	static void SnapTo( Transform target, Transform pose )
	{
		if ( target == null || pose == null )
			return;

		target.SetPositionAndRotation( pose.position, pose.rotation );
	}

	IEnumerator MoveTransform( Transform target, Transform pose, float duration, float arcHeight, bool easeIn = false )
	{
		if ( target == null || pose == null )
			yield break;

		Vector3 startPos = target.position;
		Quaternion startRot = target.rotation;
		Vector3 endPos = pose.position;
		Quaternion endRot = pose.rotation;
		float seconds = Mathf.Max( 0.05f, duration );
		float elapsed = 0f;
		while ( elapsed < seconds )
		{
			elapsed += Time.deltaTime;
			float raw = Mathf.Clamp01( elapsed / seconds );
			float u = easeIn ? ( raw * raw ) : CoinFlipMotion.SmoothStep( raw );
			Vector3 pos = arcHeight > 0.0001f
				? CoinFlipMotion.EvaluateArcPosition( startPos, endPos, u, arcHeight )
				: Vector3.Lerp( startPos, endPos, u );
			target.SetPositionAndRotation( pos, Quaternion.Slerp( startRot, endRot, u ) );
			yield return null;
		}

		target.SetPositionAndRotation( endPos, endRot );
	}

	void PlayStepFeedback( Feedbacks feedbacks )
	{
		_activeFeedback = feedbacks;
		_sfxWaitUntil = Time.time;
		if ( feedbacks == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = transform.position;
		feedbacks.Play( context );

		float wait = GetFeedbackClipSeconds( feedbacks );
		_sfxWaitUntil = Time.time + wait;
	}

	IEnumerator WaitForStepFeedback()
	{
		while ( Time.time < _sfxWaitUntil || IsFeedbackPlaying( _activeFeedback ) )
			yield return null;
	}

	float GetActiveFeedbackWaitSeconds()
	{
		float remaining = _sfxWaitUntil - Time.time;
		return Mathf.Max( 0.35f, remaining );
	}

	static float GetFeedbackClipSeconds( Feedbacks feedbacks )
	{
		if ( feedbacks == null || feedbacks.FeedbackList == null )
			return 0f;

		float max = 0f;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			PlaySFXFeedback sfx = feedbacks.FeedbackList[ i ] as PlaySFXFeedback;
			if ( sfx == null || sfx.Clip == null )
				continue;
			max = Mathf.Max( max, sfx.Clip.length );
		}

		return max;
	}

	static bool IsFeedbackPlaying( Feedbacks feedback )
	{
		if ( feedback == null )
			return false;

		if ( feedback.IsPlaying )
			return true;

		FeedbackTicker ticker = feedback.Ticker;
		return ticker != null && ticker.HasActive;
	}
}
