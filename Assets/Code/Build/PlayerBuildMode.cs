using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Build mode: unlocked by interacting with the world hammer, then toggled with F.
/// Stashes carry visuals, shows a tool-only hammer, reveals nearby buildable ghosts,
/// and hold ContextualInteract on an aimed ghost to complete it.
/// </summary>
public class PlayerBuildMode : MonoBehaviour
{
	const int RaycastBufferSize = 32;
	const int MaxSwingHits = 8;
	const string HeldHammerAddress = "Assets/Prefabs/Build/HeldHammer.prefab";
	const string BuildStartFeedbackChild = "OnBuildStartFeedbacks";
	const string BuildCompleteFeedbackChild = "OnBuildCompleteFeedbacks";
	const string BuildHitFeedbackChild = "OnBuildHitFeedbacks";

	enum EquipPhase
	{
		None,
		Drawing,
		WorldPickup,
		Held,
		Holstering
	}

	readonly RaycastHit[] _rayHits = new RaycastHit[ RaycastBufferSize ];
	readonly List<BuildableObject> _visibleScratch = new List<BuildableObject>( 32 );
	readonly List<Renderer> _outlineScratch = new List<Renderer>( 16 );
	readonly bool[] _swingHitPlayed = new bool[ MaxSwingHits ];

	BuildModeDefinition _definition;
	CarryDefinition _carryDefinition;
	PlayerController _player;
	PlayerInteraction _interaction;
	FirstPersonCameraController _cameraLook;
	bool _inputEnabled = true;
	bool _active;
	float _charge;
	BuildableObject _aimed;
	Vector3 _aimHitPoint;
	bool _hasAimHitPoint;
	HoverOutlineVisualSettings _cachedBuildOutline;
	[SerializeField] Feedbacks _onBuildStartFeedbacks;
	[SerializeField] Feedbacks _onBuildCompleteFeedbacks;
	[SerializeField] Feedbacks _onBuildHitFeedbacks;
	AmbientLoopSfxFeedback _buildStartSfx;
	PlaySFXFeedback _buildCompleteSfx;
	PlayRandomSFXFeedback _buildHitSfx;
	GameObject _hammerInstance;
	AsyncOperationHandle<GameObject>? _hammerHandle;
	bool _usingProceduralHammer;
	bool _triedHeldHammerAddress;
	Transform _hammerAttach;

	EquipPhase _equipPhase;
	float _equipT = 1f;
	float _bobPhase;
	float _swayPhase;
	Vector3 _smoothedMotionOffset;
	Vector3 _jumpReactionOffset;
	float _jumpReactionT;

	WorldHammerInteractable _takenWorldHammer;
	bool _worldHammerUnlocked;
	Vector3 _worldPickupStartLocalPos;
	Quaternion _worldPickupStartLocalRot = Quaternion.identity;
	bool _worldPickupLocalReady;
	Vector3 _worldPickupWorldPos;
	Quaternion _worldPickupWorldRot = Quaternion.identity;

	BuildModeDefinition Definition => RuntimeDefinition.Resolve( ref _definition );
	CarryDefinition CarryDef => RuntimeDefinition.Resolve( ref _carryDefinition );

	public bool IsActive => _active;
	public bool HasUnlockedWorldHammer => _worldHammerUnlocked;
	public BuildableObject AimedBuildable => _active ? _aimed : null;
	public float ChargeProgress01
	{
		get
		{
			float hold = GetHoldSeconds();
			if ( hold <= 0.01f )
				return 0f;
			return Mathf.Clamp01( _charge / hold );
		}
	}

	public bool IsCharging => _active && _aimed != null && _charge > 0.001f;

	public void Setup( PlayerController player, PlayerInteraction interaction, FirstPersonCameraController cameraLook )
	{
		_player = player;
		_interaction = interaction;
		_cameraLook = cameraLook;
		EnsureBuildFeedbacks();
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
		if ( !_inputEnabled && _active )
			BeginExitBuildMode();
	}

	public void SetHammerAttach( Transform attach )
	{
		_hammerAttach = attach;
	}

	void Update()
	{
		if ( !_inputEnabled || _player == null || !_player.GameplayInputEnabled )
		{
			if ( _active || _equipPhase == EquipPhase.Holstering )
				BeginExitBuildMode( immediate: true );
			return;
		}

		if ( _player.IsDrivingMinecart || _player.IsCinematicLocked )
		{
			if ( _active || _equipPhase == EquipPhase.Holstering )
				BeginExitBuildMode( immediate: true );
			return;
		}

		GameInput input = GetGameInput();
		if ( input == null )
			return;

		if ( input.BuildModeToggle != null && input.BuildModeToggle.WasPressedThisFrame() )
		{
			if ( _active )
				BeginExitBuildMode();
			else if ( _equipPhase == EquipPhase.Holstering )
				FinishExitBuildMode();
			else if ( _worldHammerUnlocked )
				EnterBuildMode();
		}

		// Holster / world-pickup tick even when _active flips false during holster.
		if ( _equipPhase == EquipPhase.Drawing
			|| _equipPhase == EquipPhase.WorldPickup
			|| _equipPhase == EquipPhase.Holstering )
			TickEquip();

		if ( !_active )
			return;

		TickVisibleGhosts();
		if ( _equipPhase == EquipPhase.Held || _equipPhase == EquipPhase.Drawing || _equipPhase == EquipPhase.WorldPickup )
			TickAimAndCharge( input );
		UpdateBuildOutline();
	}

	void LateUpdate()
	{
		if ( !_active && _equipPhase != EquipPhase.Holstering )
			return;

		UpdateHammerPose();
	}

	void OnDestroy()
	{
		ClearBuildOutline();
		RestoreTakenWorldHammer();
		ReleaseHammer();
		HideAllGhosts();
	}

	/// <summary>
	/// Takes the world hammer prop into the hand and enters build mode, using a fly-in that
	/// overrides the default draw toss/spin.
	/// </summary>
	public bool TryTakeWorldHammer( WorldHammerInteractable worldHammer )
	{
		if ( worldHammer == null || _active || _equipPhase != EquipPhase.None )
			return false;
		if ( !_inputEnabled || _player == null || !_player.GameplayInputEnabled )
			return false;

		_takenWorldHammer = worldHammer;
		_worldHammerUnlocked = true;
		_worldPickupWorldPos = worldHammer.transform.position;
		_worldPickupWorldRot = worldHammer.transform.rotation;
		_worldPickupLocalReady = false;
		worldHammer.gameObject.SetActive( false );

		UnlockRewardToastUI.NotifyMessage( "Unlocked Build Mode" );
		EnterBuildMode( fromWorld: true );
		return true;
	}

	void EnterBuildMode()
	{
		EnterBuildMode( fromWorld: false );
	}

	void EnterBuildMode( bool fromWorld )
	{
		if ( _active )
			return;
		if ( !fromWorld && !_worldHammerUnlocked )
			return;

		_active = true;
		_charge = 0f;
		_aimed = null;
		_equipPhase = fromWorld ? EquipPhase.WorldPickup : EquipPhase.Drawing;
		_equipT = 0f;
		_smoothedMotionOffset = Vector3.zero;
		_jumpReactionOffset = Vector3.zero;
		_jumpReactionT = 0f;
		if ( !fromWorld )
		{
			_takenWorldHammer = null;
			_worldPickupLocalReady = false;
		}

		EnsureHammerAttachRoot();

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry != null )
			carry.SetBuildModeHidden( true );

		SpawnHammer();
		EventBus.Publish( new BuildModeEnteredEvent() );
	}

	void BeginExitBuildMode( bool immediate = false )
	{
		if ( !_active && _equipPhase != EquipPhase.Drawing && _equipPhase != EquipPhase.WorldPickup && _equipPhase != EquipPhase.Held && _equipPhase != EquipPhase.Holstering )
			return;

		_active = false;
		CancelCharge();
		ClearBuildOutline();
		HideAllGhosts();

		if ( immediate || _hammerInstance == null || _equipPhase == EquipPhase.None )
		{
			FinishExitBuildMode();
			return;
		}

		_equipPhase = EquipPhase.Holstering;
		if ( _equipT < 0.001f )
			_equipT = 1f;
	}

	void FinishExitBuildMode()
	{
		_equipPhase = EquipPhase.None;
		_equipT = 0f;
		ReleaseHammer();
		RestoreTakenWorldHammer();

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry != null )
			carry.SetBuildModeHidden( false );

		EventBus.Publish( new BuildModeExitedEvent() );
	}

	void RestoreTakenWorldHammer()
	{
		if ( _takenWorldHammer == null )
			return;

		_takenWorldHammer.RestoreToWorld();
		_takenWorldHammer = null;
		_worldPickupLocalReady = false;
	}

	void TickEquip()
	{
		BuildModeDefinition def = Definition;
		if ( _equipPhase == EquipPhase.Drawing )
		{
			float duration = def != null ? def.hammerDrawSeconds : 0.42f;
			_equipT = Mathf.MoveTowards( _equipT, 1f, Time.deltaTime / Mathf.Max( 0.05f, duration ) );
			if ( _equipT >= 0.999f )
			{
				_equipT = 1f;
				_equipPhase = EquipPhase.Held;
			}
		}
		else if ( _equipPhase == EquipPhase.WorldPickup )
		{
			float duration = def != null ? def.hammerWorldPickupSeconds : 0.45f;
			_equipT = Mathf.MoveTowards( _equipT, 1f, Time.deltaTime / Mathf.Max( 0.05f, duration ) );
			if ( _equipT >= 0.999f )
			{
				_equipT = 1f;
				_equipPhase = EquipPhase.Held;
				_worldPickupLocalReady = false;
			}
		}
		else if ( _equipPhase == EquipPhase.Holstering )
		{
			float duration = def != null ? def.hammerHolsterSeconds : 0.22f;
			_equipT = Mathf.MoveTowards( _equipT, 0f, Time.deltaTime / Mathf.Max( 0.05f, duration ) );
			if ( _equipT <= 0.001f )
				FinishExitBuildMode();
		}
	}

	void EnsureHammerAttachRoot()
	{
		Transform parent = ResolveCameraParent();
		if ( parent == null )
			return;

		if ( _hammerAttach == null )
		{
			GameObject attachGo = new GameObject( "BuildHammerAttach" );
			_hammerAttach = attachGo.transform;
		}

		if ( _hammerAttach.parent != parent )
			_hammerAttach.SetParent( parent, false );

		_hammerAttach.localScale = Vector3.one;
	}

	Transform ResolveCameraParent()
	{
		if ( _cameraLook != null )
			return _cameraLook.transform;

		if ( _player != null && _player.CameraMount != null )
			return _player.CameraMount;

		return _player != null ? _player.transform : null;
	}

	void TickVisibleGhosts()
	{
		BuildModeDefinition def = Definition;
		float fadeEnd = def != null ? def.fadeEnd : 18f;
		Vector3 playerPos = _player.transform.position;

		_visibleScratch.Clear();
		IReadOnlyList<BuildableObject> all = BuildableObject.Active;
		bool anyShown = false;
		for ( int i = 0; i < all.Count; i++ )
		{
			BuildableObject buildable = all[ i ];
			if ( buildable == null )
				continue;

			// Active when within fadeEnd of the bounds (nearest point) so long tracks show from either end.
			bool show = buildable.IsEligibleToShow( playerPos, fadeEnd );
			bool wasVisible = buildable.IsGhostVisible;
			buildable.SetGhostVisible( show );
			if ( show )
			{
				buildable.UpdateGhostFade( playerPos, def );
				_visibleScratch.Add( buildable );
				if ( !wasVisible )
					anyShown = true;
			}
		}

		if ( anyShown )
			Physics.SyncTransforms();
	}

	void HideAllGhosts()
	{
		IReadOnlyList<BuildableObject> all = BuildableObject.Active;
		for ( int i = 0; i < all.Count; i++ )
		{
			BuildableObject buildable = all[ i ];
			if ( buildable != null )
				buildable.SetGhostVisible( false );
		}
	}

	void TickAimAndCharge( GameInput input )
	{
		BuildableObject aimed = ResolveAimedBuildable();
		if ( aimed != _aimed )
		{
			StopBuildStartFeedback();
			ResetSwingHits();
			_aimed = aimed;
			_charge = 0f;
		}

		bool heldContextual = input.ContextualInteract != null && input.ContextualInteract.IsPressed();
		bool heldPrimary = input.Interact != null && input.Interact.IsPressed();
		bool held = heldContextual || heldPrimary;
		if ( !held || _aimed == null )
		{
			CancelCharge();
			return;
		}

		if ( _charge <= 0f )
		{
			ResetSwingHits();
			PlayBuildStartFeedback( _aimed );
		}

		_charge += Time.deltaTime;
		TickSwingHits( _aimed );
		if ( _charge < GetHoldSeconds() )
			return;

		BuildableObject toComplete = _aimed;
		CancelCharge();
		_aimed = null;
		ClearBuildOutline();
		if ( toComplete != null )
		{
			PlayBuildCompleteFeedback( toComplete );
			toComplete.Complete();
		}
	}

	void UpdateBuildOutline()
	{
		if ( !_active || _aimed == null || _aimed.IsCompleting )
		{
			ClearBuildOutline();
			return;
		}

		IReadOnlyList<Renderer> renderers = _aimed.GetGhostOutlineRenderers();
		_outlineScratch.Clear();
		if ( renderers != null )
		{
			for ( int i = 0; i < renderers.Count; i++ )
			{
				Renderer renderer = renderers[ i ];
				if ( renderer != null )
					_outlineScratch.Add( renderer );
			}
		}

		if ( _outlineScratch.Count == 0 )
		{
			ClearBuildOutline();
			return;
		}

		HoverOutlineVisualSettings settings = ResolveBuildOutlineSettings();
		HoverOutlineRegistrar.SetTarget(
			HoverOutlineRegistrar.Owner.BuildMode,
			_outlineScratch,
			settings,
			GetInstanceID() );
	}

	void ClearBuildOutline()
	{
		HoverOutlineRegistrar.ClearIfOwner( HoverOutlineRegistrar.Owner.BuildMode, GetInstanceID() );
	}

	HoverOutlineVisualSettings ResolveBuildOutlineSettings()
	{
		BuildModeDefinition def = Definition;
		if ( def != null && def.buildOutline != null )
		{
			_cachedBuildOutline = def.buildOutline.Clone();
			_cachedBuildOutline.Validate();
			return _cachedBuildOutline;
		}

		_cachedBuildOutline = HoverOutlineVisualSettings.DefaultPickable();
		_cachedBuildOutline.Validate();
		return _cachedBuildOutline;
	}

	BuildableObject ResolveAimedBuildable()
	{
		_hasAimHitPoint = false;
		if ( _interaction == null || !_interaction.TryGetAimRay( out Ray ray ) )
			return null;

		float maxDistance = GetAimMaxDistance();
		int hitCount = Physics.RaycastNonAlloc( ray, _rayHits, maxDistance, ~0, QueryTriggerInteraction.Ignore );
		if ( hitCount <= 0 )
			return null;

		// Only the nearest surface along the aim ray counts — no AABB / parent-collider shortcuts.
		float closestDist = float.MaxValue;
		RaycastHit closestHit = default;
		bool found = false;
		for ( int i = 0; i < hitCount; i++ )
		{
			RaycastHit hit = _rayHits[ i ];
			if ( hit.collider == null || hit.distance >= closestDist )
				continue;

			closestDist = hit.distance;
			closestHit = hit;
			found = true;
		}

		if ( !found || closestHit.collider == null )
			return null;

		BuildableObject buildable = closestHit.collider.GetComponentInParent<BuildableObject>();
		if ( buildable == null || buildable.IsBuilt || buildable.IsCompleting )
			return null;
		if ( !_visibleScratch.Contains( buildable ) )
			return null;
		if ( !buildable.IsGhostAimCollider( closestHit.collider ) )
			return null;

		_aimHitPoint = closestHit.point;
		_hasAimHitPoint = true;
		return buildable;
	}

	float GetHoldSeconds()
	{
		BuildModeDefinition def = Definition;
		return def != null ? Mathf.Max( 0.1f, def.buildHoldSeconds ) : 2f;
	}

	float GetAimMaxDistance()
	{
		BuildModeDefinition def = Definition;
		if ( def != null && def.aimMaxDistance > 0.01f )
			return def.aimMaxDistance;

		float fadeEnd = def != null ? def.fadeEnd : 18f;
		float interact = _interaction != null ? _interaction.InteractRange : 8f;
		return Mathf.Max( 0.1f, Mathf.Max( fadeEnd, interact ) );
	}

	void EnsureBuildFeedbacks()
	{
		_buildStartSfx = EnsureAmbientLoopFeedback( ref _onBuildStartFeedbacks, BuildStartFeedbackChild );
		_buildCompleteSfx = EnsureSfxFeedback( ref _onBuildCompleteFeedbacks, BuildCompleteFeedbackChild, spatial: true );
		_buildHitSfx = EnsureRandomSfxFeedback( ref _onBuildHitFeedbacks, BuildHitFeedbackChild, spatial: true );
		RefreshBuildSfxFromDefinition();
	}

	AmbientLoopSfxFeedback EnsureAmbientLoopFeedback( ref Feedbacks feedbacks, string childName )
	{
		if ( feedbacks == null )
		{
			Transform existing = transform.Find( childName );
			GameObject host = existing != null ? existing.gameObject : new GameObject( childName );
			if ( existing == null )
				host.transform.SetParent( transform, false );

			feedbacks = host.GetComponent<Feedbacks>();
			if ( feedbacks == null )
				feedbacks = host.AddComponent<Feedbacks>();
		}

		feedbacks.Initialize();

		AmbientLoopSfxFeedback loop = null;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			loop = feedbacks.FeedbackList[ i ] as AmbientLoopSfxFeedback;
			if ( loop != null )
				break;
		}

		if ( loop == null )
		{
			loop = new AmbientLoopSfxFeedback();
			loop.SpatialBlend = 1f;
			loop.MinDistance = 1.25f;
			loop.MaxDistance = 28f;
			feedbacks.AddFeedback( loop );
		}

		loop.FadeInSeconds = 0.08f;
		loop.FadeOutSeconds = 0.4f;
		return loop;
	}

	PlaySFXFeedback EnsureSfxFeedback( ref Feedbacks feedbacks, string childName, bool spatial )
	{
		if ( feedbacks == null )
		{
			Transform existing = transform.Find( childName );
			GameObject host = existing != null ? existing.gameObject : new GameObject( childName );
			if ( existing == null )
				host.transform.SetParent( transform, false );

			feedbacks = host.GetComponent<Feedbacks>();
			if ( feedbacks == null )
				feedbacks = host.AddComponent<Feedbacks>();
		}

		feedbacks.Initialize();

		PlaySFXFeedback sfx = null;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			sfx = feedbacks.FeedbackList[ i ] as PlaySFXFeedback;
			if ( sfx != null )
				break;
		}

		if ( sfx == null )
		{
			sfx = new PlaySFXFeedback();
			sfx.SpatialBlend = spatial ? 1f : 0f;
			sfx.MinDistance = 1.25f;
			sfx.MaxDistance = 28f;
			sfx.FollowCallerTransform = false;
			feedbacks.AddFeedback( sfx );
		}

		return sfx;
	}

	PlayRandomSFXFeedback EnsureRandomSfxFeedback( ref Feedbacks feedbacks, string childName, bool spatial )
	{
		if ( feedbacks == null )
		{
			Transform existing = transform.Find( childName );
			GameObject host = existing != null ? existing.gameObject : new GameObject( childName );
			if ( existing == null )
				host.transform.SetParent( transform, false );

			feedbacks = host.GetComponent<Feedbacks>();
			if ( feedbacks == null )
				feedbacks = host.AddComponent<Feedbacks>();
		}

		feedbacks.Initialize();

		// Drop a legacy single-clip PlaySFXFeedback if present so the random pool owns the chain.
		for ( int i = feedbacks.FeedbackList.Count - 1; i >= 0; i-- )
		{
			if ( feedbacks.FeedbackList[ i ] is PlaySFXFeedback )
				feedbacks.FeedbackList.RemoveAt( i );
		}

		PlayRandomSFXFeedback sfx = null;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			sfx = feedbacks.FeedbackList[ i ] as PlayRandomSFXFeedback;
			if ( sfx != null )
				break;
		}

		if ( sfx == null )
		{
			sfx = new PlayRandomSFXFeedback();
			sfx.MinDistance = 1.25f;
			sfx.MaxDistance = 28f;
			feedbacks.AddFeedback( sfx );
		}

		sfx.SpatialBlend = spatial ? 1f : 0f;
		return sfx;
	}

	void RefreshBuildSfxFromDefinition()
	{
		BuildModeDefinition def = Definition;
		if ( _buildStartSfx != null )
		{
			_buildStartSfx.Clip = def != null ? def.buildStartClip : null;
			_buildStartSfx.Volume = def != null ? Mathf.Clamp01( def.buildStartVolume ) : 1f;
		}

		if ( _buildCompleteSfx != null )
		{
			_buildCompleteSfx.Clip = def != null ? def.buildCompleteClip : null;
			float vol = def != null ? def.buildCompleteVolume : 1f;
			_buildCompleteSfx.VolumeMin = vol;
			_buildCompleteSfx.VolumeMax = vol;
		}

		if ( _buildHitSfx != null )
		{
			_buildHitSfx.Clips = def != null ? def.buildHitClips : null;
			_buildHitSfx.VolumeMin = def != null ? def.buildHitVolumeMin : 1f;
			_buildHitSfx.VolumeMax = def != null ? def.buildHitVolumeMax : 1f;
			_buildHitSfx.PitchMin = def != null ? def.buildHitPitchMin : 0.95f;
			_buildHitSfx.PitchMax = def != null ? def.buildHitPitchMax : 1.05f;
			_buildHitSfx.SpatialBlend = 1f;
		}
	}

	void PlayBuildStartFeedback( BuildableObject buildable )
	{
		EnsureBuildFeedbacks();
		RefreshBuildSfxFromDefinition();

		// Timed hit impacts replace the looping start bed when hit clips are assigned.
		BuildModeDefinition def = Definition;
		if ( def != null && def.HasBuildHitClips )
			return;

		if ( _onBuildStartFeedbacks == null || _buildStartSfx == null || _buildStartSfx.Clip == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = buildable != null ? buildable.gameObject : null;
		context.Position = buildable != null ? buildable.GetWorldCenter() : transform.position;
		_onBuildStartFeedbacks.Play( context );
	}

	void PlayBuildCompleteFeedback( BuildableObject buildable )
	{
		EnsureBuildFeedbacks();
		RefreshBuildSfxFromDefinition();
		if ( _onBuildCompleteFeedbacks == null || _buildCompleteSfx == null || _buildCompleteSfx.Clip == null )
			return;

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = buildable != null ? buildable.gameObject : null;
		context.Position = buildable != null ? buildable.GetWorldCenter() : transform.position;
		_onBuildCompleteFeedbacks.Play( context );
	}

	void CancelCharge()
	{
		_charge = 0f;
		ResetSwingHits();
		StopBuildStartFeedback();
	}

	void ResetSwingHits()
	{
		for ( int i = 0; i < _swingHitPlayed.Length; i++ )
			_swingHitPlayed[ i ] = false;
	}

	void TickSwingHits( BuildableObject buildable )
	{
		BuildModeDefinition def = Definition;
		int swingCount = def != null ? Mathf.Clamp( def.hammerSwingCount, 1, MaxSwingHits ) : 3;
		float hitNorm = def != null ? def.hammerSwingHitNormalized : 0.55f;
		float hold = GetHoldSeconds();
		float swingDur = hold / swingCount;

		for ( int i = 0; i < swingCount; i++ )
		{
			if ( _swingHitPlayed[ i ] )
				continue;

			float hitTime = ( i + hitNorm ) * swingDur;
			if ( _charge < hitTime )
				break;

			_swingHitPlayed[ i ] = true;
			PlayBuildHitFeedback( buildable );
		}
	}

	void PlayBuildHitFeedback( BuildableObject buildable )
	{
		EnsureBuildFeedbacks();
		RefreshBuildSfxFromDefinition();
		if ( _onBuildHitFeedbacks == null || _buildHitSfx == null )
			return;

		BuildModeDefinition def = Definition;
		if ( def == null || !def.HasBuildHitClips )
			return;

		Vector3 hitPos = transform.position;
		if ( _hasAimHitPoint )
			hitPos = _aimHitPoint;
		else if ( buildable != null )
			hitPos = buildable.GetWorldCenter();

		FeedbackContext context = new FeedbackContext();
		context.Source = gameObject;
		context.Target = buildable != null ? buildable.gameObject : null;
		context.Position = hitPos;
		_onBuildHitFeedbacks.Play( context );
	}

	/// <summary>
	/// Soft-stop the hold loop so AmbientLoopSfxFeedback can fade out.
	/// Do not call Feedbacks.Stop — that hard-cancels the ticker and cuts the fade.
	/// </summary>
	void StopBuildStartFeedback()
	{
		if ( _buildStartSfx == null && _onBuildStartFeedbacks != null )
			EnsureBuildFeedbacks();

		if ( _buildStartSfx != null )
			_buildStartSfx.Stop();
	}

	void SpawnHammer()
	{
		ReleaseHammer();
		_triedHeldHammerAddress = false;

		Transform attach = ResolveHammerAttach();
		if ( attach == null )
			return;

		BuildModeDefinition def = Definition;
		if ( def != null && def.hammerPrefab != null && def.hammerPrefab.RuntimeKeyIsValid() )
		{
			AsyncOperationHandle<GameObject> handle = def.hammerPrefab.InstantiateAsync( attach );
			_hammerHandle = handle;
			handle.Completed += OnHammerLoaded;
			return;
		}

		_triedHeldHammerAddress = true;
		SpawnHammerByAddress( attach, HeldHammerAddress );
	}

	void SpawnHammerByAddress( Transform attach, string address )
	{
		AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync( address, attach );
		_hammerHandle = handle;
		handle.Completed += OnHammerLoaded;
	}

	void OnHammerLoaded( AsyncOperationHandle<GameObject> handle )
	{
		if ( handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null )
		{
			Transform attach = ResolveHammerAttach();
			if ( attach != null && !_triedHeldHammerAddress )
			{
				_triedHeldHammerAddress = true;
				_hammerHandle = null;
				SpawnHammerByAddress( attach, HeldHammerAddress );
				return;
			}

			if ( attach != null )
			{
				Debug.LogWarning( "PlayerBuildMode: HeldHammer failed to load; using procedural fallback." );
				_hammerInstance = CreateProceduralHammer( attach );
				_usingProceduralHammer = true;
				CaptureWorldPickupLocalStart();
				ApplyHammerLocalPose();
			}
			return;
		}

		_hammerInstance = handle.Result;
		_usingProceduralHammer = false;
		CaptureWorldPickupLocalStart();
		ApplyHammerLocalPose();
	}

	void CaptureWorldPickupLocalStart()
	{
		if ( _equipPhase != EquipPhase.WorldPickup || _hammerAttach == null )
			return;

		_worldPickupStartLocalPos = _hammerAttach.InverseTransformPoint( _worldPickupWorldPos );
		_worldPickupStartLocalRot = Quaternion.Inverse( _hammerAttach.rotation ) * _worldPickupWorldRot;
		_worldPickupLocalReady = true;
	}

	void ApplyHammerLocalPose()
	{
		if ( _hammerInstance == null )
			return;

		if ( _equipPhase == EquipPhase.WorldPickup )
		{
			ApplyWorldPickupPose();
			return;
		}

		if ( _equipPhase == EquipPhase.Holstering )
		{
			ApplyHolsterPose();
			return;
		}

		if ( _equipPhase == EquipPhase.Held && IsCharging )
		{
			ApplySwingPose();
			return;
		}

		ApplyDrawPose();
	}

	void ApplySwingPose()
	{
		BuildModeDefinition def = Definition;
		Vector3 restPos = def != null ? def.hammerLocalOffset : new Vector3( 0.32f, -0.28f, 0.45f );
		Vector3 restEuler = def != null ? def.hammerLocalEuler : new Vector3( 8f, 90f, -15f );
		float scale = def != null ? def.hammerLocalScale : 0.45f;
		Vector3 windupEuler = def != null ? def.hammerSwingWindupEuler : new Vector3( -70f, 12f, -18f );
		Vector3 impactEuler = def != null ? def.hammerSwingImpactEuler : new Vector3( 28f, -8f, 22f );
		Vector3 windupPos = def != null ? def.hammerSwingWindupOffset : new Vector3( 0.02f, 0.08f, -0.06f );
		Vector3 impactPos = def != null ? def.hammerSwingImpactOffset : new Vector3( 0.04f, -0.05f, 0.08f );
		int swingCount = def != null ? Mathf.Max( 1, def.hammerSwingCount ) : 3;
		float hitNorm = def != null ? Mathf.Clamp( def.hammerSwingHitNormalized, 0.05f, 0.95f ) : 0.55f;

		float hold = GetHoldSeconds();
		float charge01 = hold > 0.0001f ? Mathf.Clamp01( _charge / hold ) : 1f;
		float swingPhase = charge01 * swingCount;
		float swingT = swingPhase - Mathf.Floor( swingPhase );
		if ( charge01 >= 0.999f )
			swingT = 1f;

		EvaluateSwingOffsets( swingT, hitNorm, windupPos, impactPos, windupEuler, impactEuler, out Vector3 posOff, out Vector3 eulerOff );

		Transform hammer = _hammerInstance.transform;
		hammer.localPosition = restPos + posOff;
		hammer.localRotation = Quaternion.Euler( restEuler + eulerOff );
		hammer.localScale = Vector3.one * scale;
	}

	static void EvaluateSwingOffsets(
		float swingT,
		float hitNorm,
		Vector3 windupPos,
		Vector3 impactPos,
		Vector3 windupEuler,
		Vector3 impactEuler,
		out Vector3 posOff,
		out Vector3 eulerOff )
	{
		swingT = Mathf.Clamp01( swingT );
		hitNorm = Mathf.Clamp( hitNorm, 0.05f, 0.95f );

		if ( swingT <= hitNorm )
		{
			float u = CoinFlipMotion.SmoothStep( swingT / hitNorm );
			posOff = Vector3.Lerp( Vector3.zero, windupPos, u );
			eulerOff = Vector3.Lerp( Vector3.zero, windupEuler, u );
			return;
		}

		float after = ( swingT - hitNorm ) / Mathf.Max( 0.0001f, 1f - hitNorm );
		// Fast slam windup -> impact, then settle impact -> rest.
		const float slamPortion = 0.28f;
		if ( after <= slamPortion )
		{
			float u = CoinFlipMotion.SmoothStep( after / slamPortion );
			// Ease-in-out with a snappy end: use squared for acceleration into the hit.
			u = u * u;
			posOff = Vector3.Lerp( windupPos, impactPos, u );
			eulerOff = Vector3.Lerp( windupEuler, impactEuler, u );
			return;
		}

		float settle = CoinFlipMotion.SmoothStep( ( after - slamPortion ) / ( 1f - slamPortion ) );
		posOff = Vector3.Lerp( impactPos, Vector3.zero, settle );
		eulerOff = Vector3.Lerp( impactEuler, Vector3.zero, settle );
	}

	void ApplyDrawPose()
	{
		BuildModeDefinition def = Definition;
		Vector3 restPos = def != null ? def.hammerLocalOffset : new Vector3( 0.32f, -0.28f, 0.45f );
		Vector3 restEuler = def != null ? def.hammerLocalEuler : new Vector3( 8f, 25f, -15f );
		Vector3 holsterPos = def != null ? def.hammerHolsterOffset : new Vector3( 0.12f, -0.35f, -0.15f );
		Vector3 holsterEuler = def != null ? def.hammerHolsterEuler : new Vector3( 40f, 35f, -25f );
		float scale = def != null ? def.hammerLocalScale : 0.45f;
		float tossHeight = def != null ? def.hammerDrawTossHeight : 0.14f;
		float spinRevs = def != null ? def.hammerDrawSpinRevolutions : 1.15f;
		Vector3 spinAxis = def != null ? def.hammerDrawSpinAxis : new Vector3( 1f, 0.15f, 0.35f );
		if ( spinAxis.sqrMagnitude < 0.0001f )
			spinAxis = Vector3.right;
		else
			spinAxis.Normalize();

		float t = Mathf.Clamp01( _equipT );
		float ease = CoinFlipMotion.SmoothStep( t );
		float rise = Mathf.Sin( t * Mathf.PI );
		float spinT = 1f - ( ( 1f - t ) * ( 1f - t ) );

		Transform hammer = _hammerInstance.transform;
		Vector3 basePos = Vector3.Lerp( holsterPos, restPos, ease );
		Vector3 toss = new Vector3( 0f, tossHeight, tossHeight * 0.25f ) * rise;
		hammer.localPosition = basePos + toss;

		Quaternion baseRot = Quaternion.Slerp( Quaternion.Euler( holsterEuler ), Quaternion.Euler( restEuler ), ease );
		float spinDeg = spinRevs * 360f * ( 1f - spinT );
		Quaternion spin = Quaternion.AngleAxis( spinDeg, spinAxis );
		hammer.localRotation = baseRot * spin;

		float scaleMul = Mathf.Lerp( 0.7f, 1f, ease ) + 0.08f * rise;
		hammer.localScale = Vector3.one * ( scale * scaleMul );
	}

	void ApplyHolsterPose()
	{
		BuildModeDefinition def = Definition;
		Vector3 restPos = def != null ? def.hammerLocalOffset : new Vector3( 0.32f, -0.28f, 0.45f );
		Vector3 restEuler = def != null ? def.hammerLocalEuler : new Vector3( 8f, 25f, -15f );
		Vector3 holsterPos = def != null ? def.hammerHolsterOffset : new Vector3( 0.12f, -0.35f, -0.15f );
		Vector3 holsterEuler = def != null ? def.hammerHolsterEuler : new Vector3( 40f, 35f, -25f );
		float scale = def != null ? def.hammerLocalScale : 0.45f;
		float bump = def != null ? def.hammerHolsterBumpHeight : 0.06f;

		// _equipT: 1 = held, 0 = holstered
		float leave = 1f - Mathf.Clamp01( _equipT );
		float ease = CoinFlipMotion.SmoothStep( leave );
		float bumpY = Mathf.Sin( leave * Mathf.PI ) * bump;

		Transform hammer = _hammerInstance.transform;
		hammer.localPosition = Vector3.Lerp( restPos, holsterPos, ease ) + new Vector3( 0f, bumpY, 0f );
		hammer.localRotation = Quaternion.Slerp( Quaternion.Euler( restEuler ), Quaternion.Euler( holsterEuler ), ease );
		hammer.localScale = Vector3.one * Mathf.Lerp( scale, scale * 0.85f, ease );
	}

	void ApplyWorldPickupPose()
	{
		if ( !_worldPickupLocalReady )
			CaptureWorldPickupLocalStart();
		if ( !_worldPickupLocalReady )
			return;

		BuildModeDefinition def = Definition;
		Vector3 restPos = def != null ? def.hammerLocalOffset : new Vector3( 0.32f, -0.28f, 0.45f );
		Vector3 restEuler = def != null ? def.hammerLocalEuler : new Vector3( 8f, 25f, -15f );
		float scale = def != null ? def.hammerLocalScale : 0.45f;
		float arcHeight = def != null ? def.hammerWorldPickupArcHeight : 0.35f;

		float t = Mathf.Clamp01( _equipT );
		float ease = CoinFlipMotion.SmoothStep( t );

		// Arc in attach-local space using camera-up so the flight peaks toward the player view.
		Vector3 localUp = _hammerAttach != null
			? _hammerAttach.InverseTransformDirection( Vector3.up )
			: Vector3.up;
		if ( localUp.sqrMagnitude < 0.0001f )
			localUp = Vector3.up;
		else
			localUp.Normalize();

		Transform hammer = _hammerInstance.transform;
		hammer.localPosition = CoinFlipMotion.EvaluateArcPosition(
			_worldPickupStartLocalPos,
			restPos,
			ease,
			arcHeight,
			localUp );
		hammer.localRotation = Quaternion.Slerp( _worldPickupStartLocalRot, Quaternion.Euler( restEuler ), ease );
		hammer.localScale = Vector3.one * Mathf.Lerp( scale * 0.9f, scale, ease );
	}

	void UpdateHammerPose()
	{
		EnsureHammerAttachRoot();
		if ( _hammerAttach == null )
			return;

		TickHandMotion();
		_hammerAttach.localPosition = _smoothedMotionOffset;
		_hammerAttach.localRotation = Quaternion.identity;

		if ( _hammerInstance == null )
			return;

		if ( _hammerInstance.transform.parent != _hammerAttach )
			_hammerInstance.transform.SetParent( _hammerAttach, false );

		if ( _equipPhase == EquipPhase.WorldPickup && !_worldPickupLocalReady )
			CaptureWorldPickupLocalStart();

		ApplyHammerLocalPose();
	}

	void TickHandMotion()
	{
		CarryDefinition carry = CarryDef;
		float dt = Time.deltaTime;
		bool grounded = _player == null || _player.IsGrounded;
		float moveSpeed = _player != null ? _player.PlanarSpeed : 0f;
		Vector3 localMove = _player != null ? _player.LocalPlanarVelocity : Vector3.zero;

		float bobFullSpeed = carry != null ? carry.bobFullSpeed : 4f;
		float move01 = Mathf.Clamp01( moveSpeed / Mathf.Max( 0.01f, bobFullSpeed ) );
		float idleBob = carry != null ? carry.idleBobScale : 0.15f;
		float airBob = carry != null ? carry.airBobScale : 0f;
		float bobWeight = grounded ? Mathf.Lerp( idleBob, 1f, move01 ) : airBob;

		float bobAmp = carry != null ? carry.bobAmplitude : 0.025f;
		float bobFreq = carry != null ? carry.bobFrequency : 8f;
		if ( grounded && bobWeight > 0.001f )
			_bobPhase += dt * bobFreq * Mathf.Lerp( 0.35f, 1f, move01 );
		float bobY = Mathf.Sin( _bobPhase ) * bobAmp * bobWeight;

		Vector3 swayAmp = carry != null ? carry.swayAmplitude : new Vector3( 0.02f, 0.01f, 0.015f );
		float swayFreq = carry != null ? carry.swayFrequency : 1.6f;
		_swayPhase += dt * swayFreq;
		float swayWeight = grounded ? bobWeight : Mathf.Max( bobWeight, 0.35f );
		Vector3 sway = new Vector3(
			Mathf.Sin( _swayPhase ) * swayAmp.x,
			Mathf.Cos( _swayPhase * 0.7f ) * swayAmp.y,
			Mathf.Sin( _swayPhase * 1.3f ) * swayAmp.z ) * swayWeight;

		float strafeSway = carry != null ? carry.strafeSway : 0.04f;
		float moveSway = carry != null ? carry.moveSway : 0.03f;
		Vector3 moveOffset = new Vector3(
			Mathf.Clamp( localMove.x, -1f, 1f ) * strafeSway,
			0f,
			Mathf.Clamp( localMove.z, -1f, 1f ) * moveSway );

		TickJumpReaction( carry );
		Vector3 targetMotion = new Vector3( 0f, bobY, 0f ) + sway + moveOffset + _jumpReactionOffset;
		float motionSmooth = carry != null ? carry.handMotionSmoothSpeed : 12f;
		_smoothedMotionOffset = Vector3.Lerp( _smoothedMotionOffset, targetMotion, 1f - Mathf.Exp( -motionSmooth * dt ) );
	}

	void TickJumpReaction( CarryDefinition carry )
	{
		Vector3 impulse = carry != null ? carry.jumpReactionOffset : new Vector3( 0f, -0.08f, -0.03f );
		float duration = carry != null ? Mathf.Max( 0.05f, carry.jumpReactionDuration ) : 0.28f;

		if ( _player != null && _player.WasJumpThisFrame )
		{
			_jumpReactionOffset = impulse;
			_jumpReactionT = 1f;
		}

		if ( _jumpReactionT <= 0.001f )
		{
			_jumpReactionOffset = Vector3.zero;
			return;
		}

		_jumpReactionT = Mathf.MoveTowards( _jumpReactionT, 0f, Time.deltaTime / duration );
		float ease = _jumpReactionT * _jumpReactionT;
		_jumpReactionOffset = impulse * ease;
	}

	Transform ResolveHammerAttach()
	{
		EnsureHammerAttachRoot();
		return _hammerAttach != null ? _hammerAttach : transform;
	}

	void ReleaseHammer()
	{
		if ( _hammerHandle.HasValue )
		{
			AsyncOperationHandle<GameObject> handle = _hammerHandle.Value;
			if ( handle.IsValid() )
			{
				if ( handle.IsDone && handle.Result != null )
					Addressables.ReleaseInstance( handle.Result );
				else
					handle.Completed += completed =>
					{
						if ( completed.Status == AsyncOperationStatus.Succeeded && completed.Result != null )
							Addressables.ReleaseInstance( completed.Result );
					};
			}

			_hammerHandle = null;
			_hammerInstance = null;
			_usingProceduralHammer = false;
			_triedHeldHammerAddress = false;
			return;
		}

		if ( _hammerInstance != null )
		{
			if ( _usingProceduralHammer )
				Destroy( _hammerInstance );
			else
				Addressables.ReleaseInstance( _hammerInstance );
			_hammerInstance = null;
		}

		_usingProceduralHammer = false;
		_triedHeldHammerAddress = false;
	}

	static GameObject CreateProceduralHammer( Transform parent )
	{
		GameObject root = new GameObject( "BuildHammerTool" );
		root.transform.SetParent( parent, false );

		GameObject handle = GameObject.CreatePrimitive( PrimitiveType.Cylinder );
		handle.name = "Handle";
		handle.transform.SetParent( root.transform, false );
		handle.transform.localPosition = new Vector3( 0f, 0.12f, 0f );
		handle.transform.localScale = new Vector3( 0.04f, 0.14f, 0.04f );
		Object.Destroy( handle.GetComponent<Collider>() );

		GameObject head = GameObject.CreatePrimitive( PrimitiveType.Cube );
		head.name = "Head";
		head.transform.SetParent( root.transform, false );
		head.transform.localPosition = new Vector3( 0f, 0.28f, 0f );
		head.transform.localScale = new Vector3( 0.16f, 0.08f, 0.08f );
		Object.Destroy( head.GetComponent<Collider>() );

		return root;
	}

	static GameInput GetGameInput()
	{
		InputController inputController = InputController.Instance;
		return inputController != null ? inputController.GameInput : null;
	}
}
