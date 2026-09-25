using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Toggle build mode with F: stash carry visuals, show a tool-only hammer, reveal nearby buildable ghosts,
/// and hold ContextualInteract on an aimed ghost to complete it.
/// </summary>
public class PlayerBuildMode : MonoBehaviour
{
	const int RaycastBufferSize = 32;

	readonly RaycastHit[] _rayHits = new RaycastHit[ RaycastBufferSize ];
	readonly List<BuildableObject> _visibleScratch = new List<BuildableObject>( 32 );

	BuildModeDefinition _definition;
	PlayerController _player;
	PlayerInteraction _interaction;
	bool _inputEnabled = true;
	bool _active;
	float _charge;
	BuildableObject _aimed;
	GameObject _hammerInstance;
	AsyncOperationHandle<GameObject>? _hammerHandle;
	bool _usingProceduralHammer;
	Transform _hammerAttach;

	BuildModeDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	public bool IsActive => _active;
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

	public void Setup( PlayerController player, PlayerInteraction interaction )
	{
		_player = player;
		_interaction = interaction;
	}

	public void SetInputEnabled( bool enabled )
	{
		_inputEnabled = enabled;
		if ( !_inputEnabled && _active )
			ExitBuildMode();
	}

	public void SetHammerAttach( Transform attach )
	{
		_hammerAttach = attach;
	}

	void Update()
	{
		if ( !_inputEnabled || _player == null || !_player.GameplayInputEnabled )
		{
			if ( _active )
				ExitBuildMode();
			return;
		}

		if ( _player.IsDrivingMinecart || _player.IsCinematicLocked )
		{
			if ( _active )
				ExitBuildMode();
			return;
		}

		GameInput input = GetGameInput();
		if ( input == null )
			return;

		if ( input.BuildModeToggle != null && input.BuildModeToggle.WasPressedThisFrame() )
		{
			if ( _active )
				ExitBuildMode();
			else
				EnterBuildMode();
		}

		if ( !_active )
			return;

		TickVisibleGhosts();
		TickAimAndCharge( input );
	}

	void LateUpdate()
	{
		if ( !_active )
			return;

		UpdateHammerPose();
	}

	void OnDestroy()
	{
		ReleaseHammer();
		HideAllGhosts();
	}

	void EnterBuildMode()
	{
		if ( _active )
			return;

		_active = true;
		_charge = 0f;
		_aimed = null;

		EnsureHammerAttachRoot();

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry != null )
			carry.SetBuildModeHidden( true );

		SpawnHammer();
		EventBus.Publish( new BuildModeEnteredEvent() );
	}

	void EnsureHammerAttachRoot()
	{
		if ( _hammerAttach != null )
			return;

		Transform parent = null;
		if ( _player != null && _player.CameraMount != null )
			parent = _player.CameraMount;
		else if ( _player != null )
			parent = _player.transform;

		GameObject attachGo = new GameObject( "BuildHammerAttach" );
		_hammerAttach = attachGo.transform;
		_hammerAttach.SetParent( parent, false );

		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		Vector3 activeOffset = carryDef != null ? carryDef.activeItemOffset : new Vector3( 0f, -0.12f, 0.55f );
		_hammerAttach.localPosition = activeOffset;
		_hammerAttach.localRotation = Quaternion.identity;
		_hammerAttach.localScale = Vector3.one;
	}

	void ExitBuildMode()
	{
		if ( !_active )
			return;

		_active = false;
		CancelCharge();
		HideAllGhosts();
		ReleaseHammer();

		PlayerCarry carry = _player != null ? _player.Carry : null;
		if ( carry != null )
			carry.SetBuildModeHidden( false );

		EventBus.Publish( new BuildModeExitedEvent() );
	}

	void TickVisibleGhosts()
	{
		BuildModeDefinition def = Definition;
		float showRadius = def != null ? def.showRadius : 20f;
		Vector3 playerPos = _player.transform.position;

		_visibleScratch.Clear();
		IReadOnlyList<BuildableObject> all = BuildableObject.Active;
		for ( int i = 0; i < all.Count; i++ )
		{
			BuildableObject buildable = all[ i ];
			if ( buildable == null )
				continue;

			bool show = buildable.IsEligibleToShow( playerPos, showRadius );
			buildable.SetGhostVisible( show );
			if ( show )
			{
				buildable.UpdateGhostFade( playerPos, def );
				_visibleScratch.Add( buildable );
			}
		}
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
			_aimed = aimed;
			_charge = 0f;
		}

		bool held = input.ContextualInteract != null && input.ContextualInteract.IsPressed();
		if ( !held || _aimed == null )
		{
			CancelCharge();
			return;
		}

		_charge += Time.deltaTime;
		if ( _charge < GetHoldSeconds() )
			return;

		BuildableObject toComplete = _aimed;
		CancelCharge();
		if ( toComplete != null )
			toComplete.Complete();
	}

	BuildableObject ResolveAimedBuildable()
	{
		if ( _interaction == null || !_interaction.TryGetAimRay( out Ray ray ) )
			return null;

		float maxDistance = GetAimMaxDistance();
		int mask = PhysicsLayers.DefaultAndCollectableMask;
		int hitCount = Physics.RaycastNonAlloc( ray, _rayHits, maxDistance, mask, QueryTriggerInteraction.Ignore );
		if ( hitCount <= 0 )
			return null;

		float bestDist = float.MaxValue;
		BuildableObject best = null;
		for ( int i = 0; i < hitCount; i++ )
		{
			RaycastHit hit = _rayHits[ i ];
			if ( hit.collider == null )
				continue;

			BuildableObject buildable = hit.collider.GetComponentInParent<BuildableObject>();
			if ( buildable == null || buildable.IsBuilt )
				continue;
			if ( !_visibleScratch.Contains( buildable ) )
				continue;
			if ( hit.distance >= bestDist )
				continue;

			bestDist = hit.distance;
			best = buildable;
		}

		return best;
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

		if ( _interaction != null )
			return Mathf.Max( 0.1f, _interaction.InteractRange );

		return 8f;
	}

	void CancelCharge()
	{
		_charge = 0f;
	}

	void SpawnHammer()
	{
		ReleaseHammer();

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

		_hammerInstance = CreateProceduralHammer( attach );
		_usingProceduralHammer = true;
		ApplyHammerLocalPose();
	}

	void OnHammerLoaded( AsyncOperationHandle<GameObject> handle )
	{
		if ( handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null )
		{
			Transform attach = ResolveHammerAttach();
			if ( attach != null )
			{
				_hammerInstance = CreateProceduralHammer( attach );
				_usingProceduralHammer = true;
				ApplyHammerLocalPose();
			}
			return;
		}

		_hammerInstance = handle.Result;
		_usingProceduralHammer = false;
		ApplyHammerLocalPose();
	}

	void ApplyHammerLocalPose()
	{
		if ( _hammerInstance == null )
			return;

		BuildModeDefinition def = Definition;
		Transform t = _hammerInstance.transform;
		t.localPosition = def != null ? def.hammerLocalOffset : new Vector3( 0.05f, -0.05f, 0.08f );
		t.localRotation = Quaternion.Euler( def != null ? def.hammerLocalEuler : new Vector3( 10f, 0f, -20f ) );
		float scale = def != null ? def.hammerLocalScale : 0.35f;
		t.localScale = Vector3.one * scale;
	}

	void UpdateHammerPose()
	{
		if ( _hammerInstance == null )
			return;

		Transform attach = ResolveHammerAttach();
		if ( attach == null )
			return;

		if ( _hammerInstance.transform.parent != attach )
			_hammerInstance.transform.SetParent( attach, false );

		ApplyHammerLocalPose();
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
