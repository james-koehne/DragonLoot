using System.Collections;

using UnityEngine;

/// <summary>
/// Single-slot artifact washer: hand-place or physics-drop dirty Artifacts onto intake,
/// belt travel cleans them, exit parks for pickup.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class CleaningStationInteractable : InteractableBase, ITreasureOwner, ITreasurePlacementTarget
{
	static readonly Collider[] OverlapBuffer = new Collider[ 32 ];

	enum Phase
	{
		Empty,
		Travelling,
		AwaitingPickup
	}

	[Header( "Sockets" )]
	[SerializeField]
	Transform intakeSocket;

	[SerializeField]
	Transform cleanerSocket;

	[SerializeField]
	Transform exitSocket;

	[Header( "Physics Intake" )]
	[SerializeField]
	Collider intakeVolume;

	[SerializeField]
	[Min( 0.02f )]
	float intakeScanInterval = 0.1f;

	[Header( "Overrides" )]
	[Tooltip( "Optional; null resolves TreasureCleaningDefinition via Addressables." )]
	[SerializeField]
	TreasureCleaningDefinition definitionOverride;

	[SerializeField]
	[Min( 1f )]
	float bounceScale = 1.1f;

	TreasureCleaningDefinition _definition;
	TreasureItem _occupant;
	Phase _phase = Phase.Empty;
	float _travelElapsed;
	float _travelDuration = 4f;
	float _nextScanTime;
	Coroutine _intakeSnapRoutine;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.CleaningStation;

	public bool IsOccupied => _occupant != null;

	/// <summary>True only when the item has finished cleaning and sits at the exit.</summary>
	public bool AllowsPickup => _phase == Phase.AwaitingPickup && _occupant != null;

	public bool IsUnlocked
	{
		get
		{
			TreasureCleaningDefinition def = ResolveDefinition();
			return def == null || def.stationUnlocked;
		}
	}

	public TreasureItem Occupant => _occupant;

	void Awake()
	{
		SetInteractionName( "Cleaning Station" );
		EnsureSockets();
		ResolveDefinition();
	}

	void OnDisable()
	{
		if ( _intakeSnapRoutine != null )
		{
			StopCoroutine( _intakeSnapRoutine );
			_intakeSnapRoutine = null;
		}
	}

	void Update()
	{
		if ( _phase == Phase.Travelling )
			TickTravel( Time.deltaTime );
	}

	void FixedUpdate()
	{
		if ( !IsUnlocked || IsOccupied || intakeVolume == null )
			return;
		if ( Time.time < _nextScanTime )
			return;

		_nextScanTime = Time.time + Mathf.Max( 0.02f, intakeScanInterval );
		ScanIntakeVolume();
	}

	public override bool CanInteract( PlayerController player )
	{
		return false;
	}

	public override void Interact( PlayerController player )
	{
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		if ( item == null || item != _occupant )
			return;

		ClearOccupant( freeItemOwnership: false );
	}

	public void Remove( TreasureItem item )
	{
		ReleaseTreasure( item );
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		return CanAcceptDirtyArtifact( item );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		EnsureSockets();
		Transform socket = intakeSocket != null ? intakeSocket : transform;
		Vector3 scale = item.GetWorldScale();
		bool valid = CanAcceptDirtyArtifact( item );
		preview.SetItemMesh( socket.position, socket.rotation, scale, valid );
		return true;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanAcceptDirtyArtifact( item ) )
			return false;

		PlayerController player = query.Player;
		if ( player == null || player.Carry == null )
			return false;

		if ( !player.Carry.TryConsumeActive( out TreasureItem removed ) || removed == null || removed != item )
		{
			if ( removed != null && removed != item )
				removed.EnterPhysics( removed.transform.position, removed.transform.rotation );
			return false;
		}

		BeginProcessing( removed, fromHand: true );
		return true;
	}

	bool CanAcceptDirtyArtifact( TreasureItem item )
	{
		if ( item == null || !IsUnlocked || IsOccupied )
			return false;
		if ( !item.RequiresCleaning || !item.IsDirty )
			return false;
		if ( item.Definition == null || item.Definition.category != TreasureCategory.Artifact )
			return false;
		return true;
	}

	void ScanIntakeVolume()
	{
		Bounds bounds = intakeVolume.bounds;
		int hits = Physics.OverlapBoxNonAlloc(
			bounds.center,
			bounds.extents,
			OverlapBuffer,
			intakeVolume.transform.rotation,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		for ( int i = 0; i < hits; i++ )
		{
			Collider hit = OverlapBuffer[ i ];
			if ( hit == null || hit == intakeVolume )
				continue;

			TreasureItem item = hit.GetComponentInParent<TreasureItem>();
			if ( item == null )
				continue;
			if ( item.IsInFlight || item.State == TreasureItemState.Held )
				continue;
			if ( !item.IsWorldLoose )
				continue;
			if ( !CanAcceptDirtyArtifact( item ) )
				continue;

			BeginProcessing( item, fromHand: false );
			return;
		}
	}

	void BeginProcessing( TreasureItem item, bool fromHand )
	{
		if ( item == null || IsOccupied )
			return;

		EnsureSockets();
		TreasureCleaningDefinition def = ResolveDefinition();
		_travelDuration = RuntimeDefinition.Get( def, d => d.stationBeltTravelSeconds, 4f );
		_travelElapsed = 0f;
		_occupant = item;
		_phase = Phase.Travelling;

		Transform intake = intakeSocket != null ? intakeSocket : transform;
		if ( fromHand )
		{
			if ( _intakeSnapRoutine != null )
				StopCoroutine( _intakeSnapRoutine );
			_intakeSnapRoutine = StartCoroutine( SnapToIntakeRoutine( item, intake ) );
		}
		else
		{
			item.BeginFlight();
			item.EndFlight();
			item.EnterDisplayed( this, intake, intake.position, intake.rotation );
			item.ApplyWorldScale();
		}
	}

	IEnumerator SnapToIntakeRoutine( TreasureItem item, Transform intake )
	{
		if ( item == null || intake == null )
			yield break;

		item.BeginFlight();
		item.ClaimPendingStackOwner( this );

		Transform t = item.transform;
		t.SetParent( null, true );
		Vector3 startPos = t.position;
		Quaternion startRot = t.rotation;
		Vector3 startScale = t.lossyScale;
		Vector3 endScale = item.GetWorldScale();
		Vector3 endPos = intake.position;
		Quaternion endRot = intake.rotation;

		TreasureCleaningDefinition def = ResolveDefinition();
		float duration = RuntimeDefinition.Get( def, d => d.stationIntakeSnapSeconds, 0.22f );
		duration = Mathf.Max( duration, CoinFlipMotion.DefaultItemArcDuration );
		float arcHeight = CoinFlipMotion.DefaultItemArcHeight;
		float elapsed = 0f;

		while ( elapsed < duration )
		{
			if ( item == null || _occupant != item )
				yield break;

			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			float ease = CoinFlipMotion.SmoothStep( u );
			float bounce = 1f + ( bounceScale - 1f ) * Mathf.Sin( u * Mathf.PI );

			t.position = CoinFlipMotion.EvaluateArcPosition( startPos, endPos, u, arcHeight );
			t.rotation = Quaternion.Slerp( startRot, endRot, ease );
			item.ApplyDesiredWorldScale( Vector3.Lerp( startScale, endScale, ease ) * bounce );
			yield return null;
		}

		_intakeSnapRoutine = null;
		if ( item == null || _occupant != item )
			yield break;

		item.EndFlight();
		item.EnterDisplayed( this, intake, endPos, endRot );
		item.ApplyWorldScale();
	}

	void TickTravel( float deltaTime )
	{
		if ( _occupant == null )
		{
			_phase = Phase.Empty;
			return;
		}

		// Wait until intake snap finishes before advancing along the belt.
		if ( _intakeSnapRoutine != null )
			return;

		_travelElapsed += Mathf.Max( 0f, deltaTime );
		float duration = Mathf.Max( 0.05f, _travelDuration );
		float u = Mathf.Clamp01( _travelElapsed / duration );

		EnsureSockets();
		GetPathPose( u, out Vector3 pos, out Quaternion rot );
		Transform parent = ResolvePathParent( u );
		Transform t = _occupant.transform;
		if ( parent != null && t.parent != parent )
			t.SetParent( parent, true );
		t.SetPositionAndRotation( pos, rot );
		_occupant.ApplyWorldScale();

		float cleanTarget = u;
		float deltaClean = cleanTarget - _occupant.CleanProgress;
		if ( deltaClean > 0f )
			_occupant.ApplyCleaning( deltaClean );

		if ( u < 1f )
			return;

		_occupant.SetClean();
		Transform exit = exitSocket != null ? exitSocket : transform;
		_occupant.EnterDisplayed( this, exit, exit.position, exit.rotation );
		_phase = Phase.AwaitingPickup;
	}

	void GetPathPose( float u, out Vector3 pos, out Quaternion rot )
	{
		Transform intake = intakeSocket != null ? intakeSocket : transform;
		Transform exit = exitSocket != null ? exitSocket : transform;
		Transform cleaner = cleanerSocket != null ? cleanerSocket : null;

		if ( cleaner == null )
		{
			pos = Vector3.Lerp( intake.position, exit.position, u );
			rot = Quaternion.Slerp( intake.rotation, exit.rotation, u );
			return;
		}

		if ( u < 0.5f )
		{
			float local = u / 0.5f;
			pos = Vector3.Lerp( intake.position, cleaner.position, local );
			rot = Quaternion.Slerp( intake.rotation, cleaner.rotation, local );
		}
		else
		{
			float local = ( u - 0.5f ) / 0.5f;
			pos = Vector3.Lerp( cleaner.position, exit.position, local );
			rot = Quaternion.Slerp( cleaner.rotation, exit.rotation, local );
		}
	}

	Transform ResolvePathParent( float u )
	{
		if ( u < 0.5f )
			return intakeSocket != null ? intakeSocket : transform;
		if ( cleanerSocket != null && u < 0.95f )
			return cleanerSocket;
		return exitSocket != null ? exitSocket : transform;
	}

	void ClearOccupant( bool freeItemOwnership )
	{
		TreasureItem item = _occupant;
		_occupant = null;
		_phase = Phase.Empty;
		_travelElapsed = 0f;

		if ( _intakeSnapRoutine != null )
		{
			StopCoroutine( _intakeSnapRoutine );
			_intakeSnapRoutine = null;
		}

		if ( freeItemOwnership && item != null && item.Owner == this )
		{
			// Ownership cleared by caller path when needed.
		}
	}

	TreasureCleaningDefinition ResolveDefinition()
	{
		if ( definitionOverride != null )
		{
			_definition = definitionOverride;
			return _definition;
		}

		return RuntimeDefinition.Resolve( ref _definition );
	}

	void EnsureSockets()
	{
		if ( intakeSocket == null )
		{
			Transform found = transform.Find( "IntakeSocket" );
			intakeSocket = found != null ? found : transform;
		}

		if ( cleanerSocket == null )
		{
			Transform found = transform.Find( "CleanerSocket" );
			if ( found != null )
				cleanerSocket = found;
		}

		if ( exitSocket == null )
		{
			Transform found = transform.Find( "ExitSocket" );
			exitSocket = found != null ? found : transform;
		}

		if ( intakeVolume == null )
		{
			Transform found = transform.Find( "IntakeVolume" );
			if ( found != null )
				intakeVolume = found.GetComponent<Collider>();
		}
	}
}
