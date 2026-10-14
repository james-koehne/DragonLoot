using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Industrial shredder: hop junk → pull chain to charge → shred → spit resources.
/// </summary>
public class RecyclingMachine : MonoBehaviour, ITreasureOwner
{
	public enum MachineState
	{
		Idle = 0,
		Loading = 1,
		Charged = 2,
		Shredding = 3,
		Output = 4
	}

	static readonly List<RecyclingMachine> AllStations = new List<RecyclingMachine>( 8 );
	static readonly List<TreasureItem> DumpScratch = new List<TreasureItem>( 32 );

	[Header( "Parts" )]
	[SerializeField]
	RecyclingHopper hopper;

	[SerializeField]
	RecyclingPullChainInteractable pullChain;

	[SerializeField]
	RecyclingPullChainMotion pullChainMotion;

	[SerializeField]
	RecyclingOutputTray outputTray;

	[SerializeField]
	RecyclingChargeGauge chargeGauge;

	[SerializeField]
	Transform hopperPileRoot;

	[SerializeField]
	Transform shredderMouth;

	[SerializeField]
	Transform rollers;

	[Header( "Definition" )]
	[SerializeField]
	RecyclingMachineDefinition definitionOverride;

	[SerializeField]
	RecyclingRecipeCatalog recipeCatalogOverride;

	[Header( "Feedbacks" )]
	[SerializeField]
	Feedbacks onPull;

	[SerializeField]
	Feedbacks onPullCatch;

	[SerializeField]
	Feedbacks onPullDenied;

	[SerializeField]
	Feedbacks onChargeFull;

	[SerializeField]
	Feedbacks onChainRetract;

	[SerializeField]
	Feedbacks onShredWindup;

	[SerializeField]
	Feedbacks onShredLoop;

	[SerializeField]
	Feedbacks onShredSpinDown;

	[SerializeField]
	Feedbacks onHeavyLoad;

	[SerializeField]
	Feedbacks onItemConsumed;

	[SerializeField]
	Feedbacks onChewWood;

	[SerializeField]
	Feedbacks onChewMetal;

	[SerializeField]
	Feedbacks onOutputSpawn;

	[SerializeField]
	Feedbacks onHopperFull;

	[SerializeField]
	Feedbacks onHopperAccept;

	[SerializeField]
	[Min( 0f )]
	float rollerSpinDegreesPerSecond = 420f;

	RecyclingMachineDefinition _definition;
	RecyclingRecipeCatalog _catalog;
	readonly List<TreasureItem> _hopperItems = new List<TreasureItem>( 16 );
	MachineState _state = MachineState.Idle;
	float _charge;
	float _pullActiveUntil;
	float _runtimeRemaining;
	float _chewAccumulator;
	float _windupRemaining;
	float _spinDownRemaining;
	float _heavyLoadRemaining;
	float _rollerAngle;
	bool _shredLoopPlaying;
	bool _defaultLevelEnsured;
	Coroutine _acceptRoutine;
	Coroutine _chewRoutine;

	public static IReadOnlyList<RecyclingMachine> All => AllStations;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.RecyclingMachine;

	public RecyclingMachineDefinition Definition => ResolveDefinition();

	public RecyclingRecipeCatalog Catalog => ResolveCatalog();

	public MachineState State => _state;

	public RecyclingHopper Hopper => hopper;

	public int HopperCount => _hopperItems.Count;

	public int HopperCapacity
	{
		get
		{
			RecyclingMachineDefinition def = ResolveDefinition();
			if ( def == null )
				return 4;
			return def.ResolveHopperCapacity( StationLevel );
		}
	}

	public bool IsHopperFull => _hopperItems.Count >= HopperCapacity;

	public int RemainingCapacity => Mathf.Max( 0, HopperCapacity - _hopperItems.Count );

	public float ChargeNormalized => Mathf.Clamp01( _charge );

	public int StationLevel => ResolveStationLevel();

	public bool CanAcceptPull =>
		_state != MachineState.Shredding && _state != MachineState.Output;

	public bool IsPulling => Time.time <= _pullActiveUntil;

	void OnEnable()
	{
		if ( !AllStations.Contains( this ) )
			AllStations.Add( this );
	}

	void OnDisable()
	{
		StopShredLoop();
		AllStations.Remove( this );
	}

	void Awake()
	{
		ResolveDefinition();
		ResolveCatalog();
		BindParts();
		RefreshStateFromHopper();
		RefreshGauge();
	}

	void Update()
	{
		TickPullGrace();
		TickShred( Time.deltaTime );
		TickRollers( Time.deltaTime );
		RefreshGauge();
	}

	void BindParts()
	{
		if ( hopper == null )
			hopper = GetComponentInChildren<RecyclingHopper>( true );
		if ( pullChain == null )
			pullChain = GetComponentInChildren<RecyclingPullChainInteractable>( true );
		if ( pullChainMotion == null )
			pullChainMotion = GetComponentInChildren<RecyclingPullChainMotion>( true );
		if ( outputTray == null )
			outputTray = GetComponentInChildren<RecyclingOutputTray>( true );
		if ( chargeGauge == null )
			chargeGauge = GetComponentInChildren<RecyclingChargeGauge>( true );

		if ( hopper != null )
			hopper.BindStation( this );
		if ( pullChain != null )
			pullChain.BindStation( this );
		if ( pullChainMotion != null )
			pullChainMotion.BindStation( this );
		if ( outputTray != null )
			outputTray.BindStation( this );
	}

	RecyclingMachineDefinition ResolveDefinition()
	{
		if ( definitionOverride != null )
		{
			_definition = definitionOverride;
			return _definition;
		}

		return RuntimeDefinition.Resolve( ref _definition );
	}

	RecyclingRecipeCatalog ResolveCatalog()
	{
		if ( recipeCatalogOverride != null )
		{
			_catalog = recipeCatalogOverride;
			return _catalog;
		}

		return RuntimeDefinition.Resolve( ref _catalog );
	}

	void EnsureDefaultUpgradeLevel()
	{
		RecyclingMachineDefinition def = ResolveDefinition();
		string id = def != null
			? def.ResolveUpgradeId()
			: RecyclingMachineDefinition.DefaultUpgradeId;
		UpgradeSystem.TryEnsureDefaultStationLevel( id, 1, ref _defaultLevelEnsured );
	}

	int ResolveStationLevel()
	{
		EnsureDefaultUpgradeLevel();
		RecyclingMachineDefinition def = ResolveDefinition();
		string id = def != null
			? def.ResolveUpgradeId()
			: RecyclingMachineDefinition.DefaultUpgradeId;
		return UpgradeSystem.ResolveStationLevel( id, 1 );
	}

	public bool HasRecipe( TreasureDefinition definition )
	{
		RecyclingRecipeCatalog catalog = ResolveCatalog();
		return catalog != null && catalog.HasRecipe( definition );
	}

	public bool CanAcceptJunk( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;
		if ( _state == MachineState.Shredding || _state == MachineState.Output )
			return false;
		if ( item.Definition.category != TreasureCategory.Junk )
			return false;
		if ( !HasRecipe( item.Definition ) )
			return false;
		if ( IsHopperFull )
			return false;
		return true;
	}

	public bool TryAcceptJunkItem( TreasureItem item, bool fromHand )
	{
		if ( item == null )
			return false;

		if ( !CanAcceptJunk( item ) )
		{
			NotifyHopperReject( item );
			return false;
		}

		_hopperItems.Add( item );
		RefreshStateFromHopper();
		PlayFeedback( onHopperAccept );

		if ( _acceptRoutine != null )
			StopCoroutine( _acceptRoutine );
		_acceptRoutine = StartCoroutine( SnapIntoHopperRoutine( item ) );
		return true;
	}

	public bool TryDumpCarryJunk( PlayerCarry carry )
	{
		if ( carry == null )
			return false;
		if ( RemainingCapacity <= 0 )
		{
			PlayFeedback( onHopperFull );
			return false;
		}

		DumpScratch.Clear();
		carry.CopyBucketItemsInOrder( CarryBucketKind.Junk, DumpScratch );
		if ( DumpScratch.Count == 0 )
			return false;

		int accepted = 0;
		for ( int i = 0; i < DumpScratch.Count; i++ )
		{
			if ( RemainingCapacity <= 0 )
				break;

			TreasureItem item = DumpScratch[ i ];
			if ( item == null || !HasRecipe( item.Definition ) )
				continue;

			if ( !carry.TryDetachItem( item ) )
				continue;

			if ( !TryAcceptJunkItem( item, fromHand: true ) )
			{
				item.EnterPhysics( item.transform.position, item.transform.rotation );
				continue;
			}

			accepted++;
		}

		DumpScratch.Clear();
		return accepted > 0;
	}

	public void NotifyHopperReject( TreasureItem item )
	{
		PlayFeedback( onHopperFull );
	}

	public void NotifyHopperItemReleased( TreasureItem item )
	{
		if ( item == null )
			return;
		_hopperItems.Remove( item );
		RefreshStateFromHopper();
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		NotifyHopperItemReleased( item );
	}

	public void NotifyPullPulse()
	{
		RecyclingMachineDefinition def = ResolveDefinition();
		float grace = def != null ? def.pullHoldGrace : 0.22f;
		_pullActiveUntil = Time.time + Mathf.Max( 0.05f, grace );

		if ( _hopperItems.Count <= 0 )
		{
			if ( pullChainMotion != null )
				pullChainMotion.TwitchDenied();
			PlayFeedback( onPullDenied );
			return;
		}

		if ( _state == MachineState.Charged )
		{
			PlayFeedback( onPullDenied );
			return;
		}

		if ( _state == MachineState.Shredding || _state == MachineState.Output )
		{
			PlayFeedback( onPullDenied );
			return;
		}

		float before = _charge;
		float perPull = def != null ? def.chargePerPull : 0.2f;
		_charge = Mathf.Clamp01( _charge + Mathf.Max( 0.05f, perPull ) );

		PlayFeedback( onPull );
		if ( pullChainMotion != null )
			pullChainMotion.PlayPullStroke( before );

		if ( before < 0.999f && _charge >= 0.999f )
		{
			_charge = 1f;
			SetState( MachineState.Charged );
			PlayFeedback( onChargeFull );
			BeginShred();
		}
		else
		{
			SetState( MachineState.Loading );
		}
	}

	public void NotifyPullCatch()
	{
		PlayFeedback( onPullCatch );
	}

	IEnumerator SnapIntoHopperRoutine( TreasureItem item )
	{
		if ( item == null )
			yield break;

		Transform pile = hopperPileRoot != null ? hopperPileRoot : ( hopper != null ? hopper.transform : transform );
		RecyclingMachineDefinition def = ResolveDefinition();
		float duration = def != null ? def.intakeSnapSeconds : 0.28f;
		float arc = def != null ? def.intakeArcHeight : 0.35f;

		item.BeginFlight();
		item.ClaimPendingStackOwner( this );

		Transform t = item.transform;
		t.SetParent( null, true );
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 startScale = t.lossyScale;
		Vector3 endScale = item.GetWorldScale();

		int index = _hopperItems.IndexOf( item );
		GetPilePose( index, out Vector3 endPos, out Quaternion endRot );

		float elapsed = 0f;
		duration = Mathf.Max( 0.05f, duration );
		while ( elapsed < duration )
		{
			if ( item == null || !_hopperItems.Contains( item ) )
				yield break;

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			float ease = CoinFlipMotion.SmoothStep( u );
			t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endPos, u, arc );
			t.rotation = Quaternion.Slerp( startRot, endRot, ease );
			item.ApplyDesiredWorldScale( Vector3.Lerp( startScale, endScale, ease ) );
			yield return null;
		}

		if ( item == null || !_hopperItems.Contains( item ) )
			yield break;

		item.EndFlight();
		item.EnterDisplayed( this, pile, endPos, endRot );
		item.ApplyWorldScale();
		_acceptRoutine = null;
	}

	void GetPilePose( int index, out Vector3 position, out Quaternion rotation )
	{
		Transform pile = hopperPileRoot != null ? hopperPileRoot : ( hopper != null ? hopper.transform : transform );
		float jitterX = ( index % 3 ) * 0.12f - 0.12f;
		float jitterZ = ( index / 3 ) * 0.1f - 0.1f;
		float jitterY = index * 0.08f;
		position = pile.position + pile.TransformVector( new Vector3( jitterX, jitterY, jitterZ ) );
		rotation = pile.rotation * Quaternion.Euler(
			Random.Range( -12f, 12f ),
			Random.Range( -30f, 30f ),
			Random.Range( -12f, 12f ) );
	}

	void BeginShred()
	{
		if ( _hopperItems.Count <= 0 )
		{
			_charge = 0f;
			SetState( MachineState.Idle );
			return;
		}

		RecyclingMachineDefinition def = ResolveDefinition();
		_runtimeRemaining = def != null ? def.ResolveRuntimeSeconds( StationLevel ) : 4f;
		_windupRemaining = def != null ? def.shredWindupSeconds : 0.35f;
		_chewAccumulator = 0f;
		_spinDownRemaining = 0f;

		float fill = HopperCapacity > 0 ? (float)_hopperItems.Count / HopperCapacity : 0f;
		float heavyThreshold = def != null ? def.heavyLoadThreshold : 0.7f;
		_heavyLoadRemaining = fill >= heavyThreshold
			? ( def != null ? def.heavyLoadSeconds : 1f )
			: 0f;

		SetState( MachineState.Shredding );
		if ( pullChainMotion != null )
			pullChainMotion.SnapRetract();
		PlayFeedback( onChainRetract );
		PlayFeedback( onShredWindup );
		if ( _heavyLoadRemaining > 0f )
			PlayFeedback( onHeavyLoad );
	}

	void TickPullGrace()
	{
		// Reserved for future pull-hold visuals; charge is pulse-based.
	}

	void TickShred( float dt )
	{
		if ( _state == MachineState.Output )
		{
			_spinDownRemaining -= dt;
			if ( _spinDownRemaining <= 0f )
			{
				_charge = 0f;
				StopShredLoop();
				SetState( _hopperItems.Count > 0 ? MachineState.Loading : MachineState.Idle );
			}

			return;
		}

		if ( _state != MachineState.Shredding )
			return;

		if ( _windupRemaining > 0f )
		{
			_windupRemaining -= dt;
			if ( _windupRemaining <= 0f )
			{
				_windupRemaining = 0f;
				StartShredLoop();
			}

			return;
		}

		if ( _heavyLoadRemaining > 0f )
			_heavyLoadRemaining -= dt;

		_runtimeRemaining -= dt;
		RecyclingMachineDefinition def = ResolveDefinition();
		float interval = def != null ? def.ResolveChewInterval( StationLevel ) : 0.55f;
		_chewAccumulator += dt;

		while ( _chewAccumulator >= interval && _hopperItems.Count > 0 && _chewRoutine == null )
		{
			_chewAccumulator -= interval;
			int batch = def != null ? def.ResolveItemsPerCycle( StationLevel ) : 1;
			_chewRoutine = StartCoroutine( ChewBatchRoutine( batch ) );
		}

		if ( _runtimeRemaining <= 0f || ( _hopperItems.Count <= 0 && _chewRoutine == null ) )
			BeginSpinDown();
	}

	void BeginSpinDown()
	{
		RecyclingMachineDefinition def = ResolveDefinition();
		_spinDownRemaining = def != null ? def.shredSpinDownSeconds : 0.55f;
		_runtimeRemaining = 0f;
		StopShredLoop();
		PlayFeedback( onShredSpinDown );
		SetState( MachineState.Output );
	}

	IEnumerator ChewBatchRoutine( int batch )
	{
		int remaining = Mathf.Max( 1, batch );
		while ( remaining > 0 && _hopperItems.Count > 0 )
		{
			TreasureItem item = _hopperItems[ 0 ];
			_hopperItems.RemoveAt( 0 );
			yield return ChewOneRoutine( item );
			remaining--;
		}

		_chewRoutine = null;
		if ( _state == MachineState.Shredding && _hopperItems.Count <= 0 )
			BeginSpinDown();
	}

	IEnumerator ChewOneRoutine( TreasureItem item )
	{
		if ( item == null )
			yield break;

		RecyclingRecipeDefinition recipe = null;
		RecyclingRecipeCatalog catalog = ResolveCatalog();
		if ( catalog != null )
			catalog.TryGetRecipe( item.Definition, out recipe );

		RecyclingMachineDefinition def = ResolveDefinition();
		float yankSeconds = def != null ? def.chewYankSeconds : 0.18f;
		Transform mouth = shredderMouth != null ? shredderMouth : transform;

		item.BeginFlight();
		Transform t = item.transform;
		t.SetParent( null, true );
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 endPos = mouth.position;
		Quaternion endRot = mouth.rotation;
		Vector3 startScale = t.lossyScale;

		float elapsed = 0f;
		yankSeconds = Mathf.Max( 0.05f, yankSeconds );
		while ( elapsed < yankSeconds )
		{
			if ( item == null )
				yield break;

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / yankSeconds );
			float squash = 1f - 0.35f * u;
			t.position = Vector3.Lerp( startPos, endPos, u * u );
			t.rotation = Quaternion.Slerp( startRot, endRot, u );
			item.ApplyDesiredWorldScale( startScale * squash );
			yield return null;
		}

		PlayFeedback( onItemConsumed );
		if ( recipe != null )
		{
			if ( recipe.chewWood )
				PlayFeedback( onChewWood );
			if ( recipe.chewMetal )
				PlayFeedback( onChewMetal );
		}

		if ( item != null )
			TreasureItemFactory.Despawn( item );

		if ( recipe != null )
			SpawnRecipeOutputs( recipe );
	}

	void SpawnRecipeOutputs( RecyclingRecipeDefinition recipe )
	{
		if ( recipe == null || recipe.outputs == null )
			return;

		RecyclingMachineDefinition def = ResolveDefinition();
		float yieldMul = def != null ? def.ResolveYieldMultiplier( StationLevel ) : 1f;

		for ( int i = 0; i < recipe.outputs.Length; i++ )
		{
			RecyclingOutputEntry entry = recipe.outputs[ i ];
			if ( entry == null || entry.resource == null )
				continue;

			int count = Mathf.Max( 1, Mathf.RoundToInt( entry.count * yieldMul ) );
			for ( int n = 0; n < count; n++ )
				StartCoroutine( EjectResourceRoutine( entry.resource ) );
		}
	}

	IEnumerator EjectResourceRoutine( TreasureDefinition resource )
	{
		if ( resource == null )
			yield break;

		RecyclingMachineDefinition def = ResolveDefinition();
		float duration = def != null ? def.outputFlightSeconds : 0.32f;
		float arc = def != null ? def.outputArcHeight : 0.4f;
		float toss = def != null ? def.outputTossForce : 1.2f;

		Transform mouth = shredderMouth != null ? shredderMouth : transform;
		Vector3 start = mouth.position;
		Quaternion startRot = mouth.rotation;

		Vector3 end;
		Quaternion endRot;
		if ( outputTray != null )
			outputTray.GetNextSpawnPose( out end, out endRot );
		else
		{
			end = transform.position + transform.forward * 0.8f + Vector3.up * 0.2f;
			endRot = transform.rotation;
		}

		TreasureItem spawned = TreasureItemFactory.SpawnSync( resource, start, startRot, null );
		if ( spawned == null )
			yield break;

		spawned.BeginFlight();
		PlayFeedback( onOutputSpawn );

		Transform t = spawned.transform;
		float elapsed = 0f;
		duration = Mathf.Max( 0.05f, duration );
		while ( elapsed < duration )
		{
			if ( spawned == null )
				yield break;

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			t.position = CoinFlipMotion.EvaluateArcPosition( start, end, u, arc );
			t.rotation = Quaternion.Slerp( startRot, endRot, CoinFlipMotion.SmoothStep( u ) );
			yield return null;
		}

		if ( spawned == null )
			yield break;

		spawned.EndFlight();
		Vector3 velocity = ( end - start ).normalized * toss + Vector3.up * ( toss * 0.35f );
		spawned.EnterPhysics( end, endRot, velocity );
	}

	void TickRollers( float dt )
	{
		if ( rollers == null )
			return;

		float speed = 0f;
		if ( _state == MachineState.Shredding )
		{
			speed = rollerSpinDegreesPerSecond;
			if ( _windupRemaining > 0f )
				speed *= 0.35f;
			else if ( _heavyLoadRemaining > 0f )
			{
				RecyclingMachineDefinition def = ResolveDefinition();
				float scale = def != null ? def.heavyLoadSpinScale : 0.55f;
				speed *= scale;
			}
		}
		else if ( _state == MachineState.Output )
		{
			RecyclingMachineDefinition def = ResolveDefinition();
			float spinDown = def != null ? def.shredSpinDownSeconds : 0.55f;
			float u = spinDown > 0.01f ? Mathf.Clamp01( _spinDownRemaining / spinDown ) : 0f;
			speed = rollerSpinDegreesPerSecond * u * 0.6f;
		}

		if ( speed <= 0.01f )
			return;

		_rollerAngle += speed * dt;
		rollers.localRotation = Quaternion.Euler( _rollerAngle, 0f, 0f );
	}

	void StartShredLoop()
	{
		if ( _shredLoopPlaying )
			return;
		_shredLoopPlaying = true;
		if ( onShredLoop != null )
			onShredLoop.Play( BuildContext() );
	}

	void StopShredLoop()
	{
		if ( !_shredLoopPlaying )
			return;
		_shredLoopPlaying = false;
		if ( onShredLoop != null )
			onShredLoop.Stop();
	}

	void RefreshStateFromHopper()
	{
		if ( _state == MachineState.Shredding || _state == MachineState.Output )
			return;

		if ( _hopperItems.Count <= 0 )
		{
			_charge = 0f;
			SetState( MachineState.Idle );
		}
		else if ( _state != MachineState.Charged )
		{
			SetState( MachineState.Loading );
		}
	}

	void SetState( MachineState next )
	{
		_state = next;
	}

	void RefreshGauge()
	{
		if ( chargeGauge == null )
			return;

		RecyclingMachineDefinition def = ResolveDefinition();
		float normalized;
		if ( _state == MachineState.Shredding || _state == MachineState.Output )
		{
			float maxRuntime = def != null ? def.ResolveRuntimeSeconds( StationLevel ) : 4f;
			normalized = maxRuntime > 0.01f ? Mathf.Clamp01( _runtimeRemaining / maxRuntime ) : 0f;
			if ( _state == MachineState.Output )
				normalized = 0f;
		}
		else
		{
			normalized = ChargeNormalized;
		}

		Color color = def != null ? def.EvaluateGaugeColor( normalized ) : Color.Lerp( Color.red, Color.green, normalized );
		chargeGauge.SetNormalized( normalized, color );
	}

	void PlayFeedback( Feedbacks feedbacks )
	{
		if ( feedbacks == null )
			return;
		feedbacks.Play( BuildContext() );
	}

	FeedbackContext BuildContext()
	{
		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = gameObject;
		context.Position = transform.position;
		return context;
	}

	/// <summary>Debug: pulse the pull chain <paramref name="times"/> (charges / starts shred when full).</summary>
	public void DebugPullPulses( int times )
	{
		int count = Mathf.Max( 1, times );
		for ( int i = 0; i < count; i++ )
			NotifyPullPulse();
	}

	/// <summary>Debug: eject hopper contents into physics and reset charge when idle/loading.</summary>
	public void DebugClearHopper()
	{
		if ( _state == MachineState.Shredding || _state == MachineState.Output )
			return;

		for ( int i = _hopperItems.Count - 1; i >= 0; i-- )
		{
			TreasureItem item = _hopperItems[ i ];
			_hopperItems.RemoveAt( i );
			if ( item == null )
				continue;
			item.EnterPhysics( item.transform.position + transform.forward * 0.4f, item.transform.rotation );
		}

		_charge = 0f;
		RefreshStateFromHopper();
		RefreshGauge();
	}

	public string ResolvePersistenceId()
	{
		return GameSavePersistenceIds.Resolve( this );
	}

	public void CaptureSave( GameSaveStationEntry entry )
	{
		if ( entry == null )
			return;

		entry.persistenceId = ResolvePersistenceId();
		entry.kind = "recycling";
		entry.state = (int)_state;
		entry.charge = _charge;
		if ( entry.hopperIds == null )
			entry.hopperIds = new List<string>();
		else
			entry.hopperIds.Clear();

		for ( int i = 0; i < _hopperItems.Count; i++ )
		{
			TreasureItem item = _hopperItems[ i ];
			if ( item == null || item.Definition == null )
				continue;
			string id = GameSaveTreasureIds.GetId( item.Definition );
			if ( !string.IsNullOrEmpty( id ) )
				entry.hopperIds.Add( id );
		}
	}

	public void ApplySave( GameSaveStationEntry entry )
	{
		if ( entry == null )
			return;

		for ( int i = _hopperItems.Count - 1; i >= 0; i-- )
		{
			TreasureItem item = _hopperItems[ i ];
			_hopperItems.RemoveAt( i );
			if ( item != null )
				TreasureItemFactory.Despawn( item );
		}

		_charge = Mathf.Clamp01( entry.charge );
		_state = MachineState.Idle;

		if ( entry.hopperIds != null )
		{
			for ( int i = 0; i < entry.hopperIds.Count; i++ )
			{
				TreasureDefinition def = GameSaveTreasureIds.Resolve( entry.hopperIds[ i ] );
				if ( def == null )
					continue;
				TreasureItem item = TreasureItemFactory.SpawnSync( def, transform.position, transform.rotation, null );
				if ( item == null )
					continue;
				_hopperItems.Add( item );
				item.EnterDisplayed( this, transform.position, transform.rotation );
			}
		}

		if ( entry.state >= 0 && entry.state <= (int)MachineState.Output )
			_state = (MachineState)entry.state;
		RefreshStateFromHopper();
		RefreshGauge();
	}

	/// <summary>
	/// Maps a collider on this station to the hopper placement target, ignoring the pull chain.
	/// </summary>
	public static RecyclingHopper ResolveHopperPlacementFromCollider( Collider collider )
	{
		if ( collider == null )
			return null;

		if ( collider.GetComponentInParent<RecyclingPullChainInteractable>() != null )
			return null;

		RecyclingMachine station = collider.GetComponentInParent<RecyclingMachine>();
		if ( station == null )
			return null;

		return station.hopper != null
			? station.hopper
			: station.GetComponentInChildren<RecyclingHopper>( true );
	}

#if UNITY_EDITOR
	public void EditorSetHopper( RecyclingHopper value ) => hopper = value;
	public void EditorSetPullChain( RecyclingPullChainInteractable value ) => pullChain = value;
	public void EditorSetPullChainMotion( RecyclingPullChainMotion value ) => pullChainMotion = value;
	public void EditorSetOutputTray( RecyclingOutputTray value ) => outputTray = value;
	public void EditorSetChargeGauge( RecyclingChargeGauge value ) => chargeGauge = value;
	public void EditorSetHopperPileRoot( Transform value ) => hopperPileRoot = value;
	public void EditorSetShredderMouth( Transform value ) => shredderMouth = value;
	public void EditorSetRollers( Transform value ) => rollers = value;
	public void EditorSetDefinition( RecyclingMachineDefinition value ) => definitionOverride = value;
	public void EditorSetCatalog( RecyclingRecipeCatalog value ) => recipeCatalogOverride = value;

	public void EditorSetFeedbacks(
		Feedbacks pull,
		Feedbacks pullCatch,
		Feedbacks pullDenied,
		Feedbacks chargeFull,
		Feedbacks chainRetract,
		Feedbacks shredWindup,
		Feedbacks shredLoop,
		Feedbacks shredSpinDown,
		Feedbacks heavyLoad,
		Feedbacks itemConsumed,
		Feedbacks chewWood,
		Feedbacks chewMetal,
		Feedbacks outputSpawn,
		Feedbacks hopperFull,
		Feedbacks hopperAccept )
	{
		onPull = pull;
		onPullCatch = pullCatch;
		onPullDenied = pullDenied;
		onChargeFull = chargeFull;
		onChainRetract = chainRetract;
		onShredWindup = shredWindup;
		onShredLoop = shredLoop;
		onShredSpinDown = shredSpinDown;
		onHeavyLoad = heavyLoad;
		onItemConsumed = itemConsumed;
		onChewWood = chewWood;
		onChewMetal = chewMetal;
		onOutputSpawn = outputSpawn;
		onHopperFull = hopperFull;
		onHopperAccept = hopperAccept;
	}
#endif
}
