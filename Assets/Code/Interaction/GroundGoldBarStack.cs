using System.Collections;
using System.Collections.Generic;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Player-made ground gold-bar stack. Owns live interleaved bars (pair, then pair on top
/// rotated 90°). One shared box collider; kinematic. Thrown bars hop-join after they sleep.
/// </summary>
[RequireComponent( typeof( BoxCollider ) )]
public class GroundGoldBarStack : InteractableBase, ITreasureOwner, ITreasurePlacementTarget
{
	static readonly List<GroundGoldBarStack> All = new List<GroundGoldBarStack>( 32 );
	static readonly Collider[] AbsorbOverlap = new Collider[ 32 ];
	static readonly List<TreasureItem> TakeBuffer = new List<TreasureItem>( 64 );

	readonly List<TreasureItem> _items = new List<TreasureItem>( 16 );
	readonly List<TreasureItem> _inFlight = new List<TreasureItem>( 8 );
	readonly List<int> _inFlightSlotIndices = new List<int>( 8 );
	bool _absorbingNearby;
	bool _taking;
	bool _destroying;
	BoxCollider _box;
	TreasureDefinition _definition;

	[SerializeField]
	Feedbacks onLandFeedback;

	[SerializeField]
	Feedbacks onRemoveFeedback;

	public TreasureOwnerKind OwnerKind => TreasureOwnerKind.GroundGoldBarStack;

	public int Count => _items.Count;
	public int InFlightCount => _inFlight.Count;
	public bool HasInFlight => _inFlight.Count > 0;
	public bool IsFull => Count >= GoldBarStack.GroundMaxHeight;
	public Vector3 ContactPosition => transform.position;
	public TreasureDefinition StackDefinition => _definition;
	public IReadOnlyList<TreasureItem> Items => _items;

	public static IReadOnlyList<GroundGoldBarStack> ActiveStacks => All;

	public static GroundGoldBarStack CreateAt( Vector3 contactPosition, Quaternion rotation )
	{
		GameObject go = new GameObject( "GroundGoldBarStack" );
		go.transform.SetPositionAndRotation( contactPosition, TreasureOrientation.FlattenUpright( rotation ) );
		int layer = LayerMask.NameToLayer( "Collectable" );
		if ( layer >= 0 )
			go.layer = layer;
		GroundGoldBarStack stack = go.AddComponent<GroundGoldBarStack>();
		stack.EnsureCollider();
		return stack;
	}

	public static GroundGoldBarStack FindNearest( Vector3 worldPos, float radius )
	{
		GroundGoldBarStack best = null;
		float bestSq = radius * radius;
		for ( int i = 0; i < All.Count; i++ )
		{
			GroundGoldBarStack stack = All[ i ];
			if ( stack == null || stack._destroying || stack.IsFull )
				continue;

			Vector3 delta = stack.ContactPosition - worldPos;
			float sq = delta.x * delta.x + delta.z * delta.z;
			if ( sq > bestSq )
				continue;

			bestSq = sq;
			best = stack;
		}

		return best;
	}

	public static GroundGoldBarStack FindNearestAlongRay( Ray ray, float radius, float maxDistance )
	{
		GroundGoldBarStack best = null;
		float bestPerpSq = radius * radius;
		float maxDist = Mathf.Max( 0.01f, maxDistance );
		Vector3 origin = ray.origin;
		Vector3 dir = ray.direction;
		if ( dir.sqrMagnitude < 0.0001f )
			return null;
		dir.Normalize();

		for ( int i = 0; i < All.Count; i++ )
		{
			GroundGoldBarStack stack = All[ i ];
			if ( stack == null || stack._destroying || stack.IsFull )
				continue;

			Vector3 contact = stack.ContactPosition;
			Vector3 to = contact - origin;
			float along = Vector3.Dot( to, dir );
			if ( along < -radius || along > maxDist + radius )
				continue;

			Vector3 closest = origin + dir * Mathf.Clamp( along, 0f, maxDist );
			Vector3 delta = contact - closest;
			float xzSq = delta.x * delta.x + delta.z * delta.z;
			if ( xzSq > bestPerpSq )
				continue;

			bestPerpSq = xzSq;
			best = stack;
		}

		return best;
	}

	public static GroundGoldBarStack FindCompatible( TreasureItem bar, float radius )
	{
		if ( bar == null || !GoldBarStack.IsStackable( bar ) )
			return null;

		if ( bar.Owner is GroundGoldBarStack owned )
			return owned;

		GroundGoldBarStack nearest = FindNearest( bar.transform.position, radius );
		if ( nearest != null && nearest.CanAccept( bar.Definition ) )
			return nearest;

		return null;
	}

	/// <summary>
	/// After a thrown bar sleeps: hop onto a nearby stack, or pair with another loose bar.
	/// </summary>
	public static bool TryJoinLoose( TreasureItem bar )
	{
		if ( bar == null || !GoldBarStack.IsStackable( bar ) )
			return false;
		if ( bar.IsInFlight || bar.IsReclaiming || !bar.IsWorldLoose )
			return false;
		if ( bar.Owner is GroundGoldBarStack )
			return true;

		float radius = GoldBarStack.ResolveJoinRadius( bar.Definition );
		GroundGoldBarStack nearby = FindCompatible( bar, radius );
		if ( nearby != null && nearby.CanAccept( bar.Definition ) )
		{
			nearby.BeginAppendFlight( bar );
			return true;
		}

		TreasureItem partner = FindNearbyLoosePartner( bar, radius );
		if ( partner == null )
			return false;

		Quaternion rot = TreasureOrientation.FlattenUpright( partner.transform.rotation );
		GroundGoldBarStack stack = CreateAt( partner.transform.position, rot );
		stack.AbsorbSettledImmediate( partner );
		stack.BeginAppendFlight( bar );
		return true;
	}

	static TreasureItem FindNearbyLoosePartner( TreasureItem bar, float radius )
	{
		if ( bar == null )
			return null;

		int hits = Physics.OverlapSphereNonAlloc(
			bar.transform.position,
			radius,
			AbsorbOverlap,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		TreasureItem best = null;
		float bestSq = radius * radius;
		for ( int i = 0; i < hits; i++ )
		{
			Collider col = AbsorbOverlap[ i ];
			if ( col == null )
				continue;

			TreasureItem candidate = col.GetComponentInParent<TreasureItem>();
			if ( candidate == null || candidate == bar )
				continue;
			if ( !candidate.IsWorldLoose || candidate.IsInFlight || candidate.IsReclaiming )
				continue;
			if ( candidate.Owner is GroundGoldBarStack )
				continue;
			if ( !GoldBarStack.IsStackable( candidate ) || !GoldBarStack.AreSameType( bar.Definition, candidate.Definition ) )
				continue;

			Vector3 delta = candidate.transform.position - bar.transform.position;
			float sq = delta.x * delta.x + delta.z * delta.z;
			if ( sq > bestSq )
				continue;

			bestSq = sq;
			best = candidate;
		}

		return best;
	}

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );
		EnsureCollider();
		EnsureCountFeedback();
		SetInteractionName( "Gold Bar Stack" );
	}

	void OnDisable()
	{
		All.Remove( this );
	}

	void OnDestroy()
	{
		_destroying = true;
		All.Remove( this );
	}

	public void ReleaseTreasure( TreasureItem item )
	{
		Remove( item );
	}

	public void Remove( TreasureItem item )
	{
		if ( item == null )
			return;

		int flightIndex = _inFlight.IndexOf( item );
		if ( flightIndex >= 0 )
		{
			int slotIndex = flightIndex < _inFlightSlotIndices.Count ? _inFlightSlotIndices[ flightIndex ] : -1;
			_inFlight.RemoveAt( flightIndex );
			if ( flightIndex < _inFlightSlotIndices.Count )
				_inFlightSlotIndices.RemoveAt( flightIndex );
			if ( slotIndex >= 0 && slotIndex < _items.Count )
			{
				_items.RemoveAt( slotIndex );
				DecrementInFlightSlotIndicesAfter( slotIndex );
			}

			RefreshCollider();
			if ( Count <= 0 && !HasInFlight )
				DestroyIfEmpty();
			return;
		}

		int index = _items.IndexOf( item );
		if ( index < 0 )
			return;

		_items.RemoveAt( index );
		RestackSettled();
		RefreshCollider();
		if ( Count <= 0 && !HasInFlight )
			DestroyIfEmpty();
		else
			PlayRemoveFeedback();
	}

	public bool CanAccept( TreasureDefinition definition )
	{
		if ( !GoldBarStack.IsStackable( definition ) || IsFull )
			return false;
		if ( _definition == null )
			return true;
		return GoldBarStack.AreSameType( _definition, definition );
	}

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		return item != null && CanAccept( item.Definition );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null )
			return false;

		int index = Count;
		GoldBarStackLattice.TryGetWorldPose( index, item, ContactPosition, transform.rotation, out Vector3 pos, out Quaternion rot );
		preview.SetItemMesh( pos, rot, item.GetWorldScale(), CanPlace( item, in query ) );
		return true;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanPlace( item, in query ) )
			return false;

		PlayerController player = query.Player;
		if ( player == null )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return false;

		if ( !carry.TryConsumeActive( out TreasureItem one ) || one == null )
			return false;

		if ( !CanAccept( one.Definition ) )
		{
			one.EnterPhysics( one.transform.position, one.transform.rotation );
			return false;
		}

		BeginAppendFlight( one );
		return true;
	}

	public void AbsorbSettledImmediate( TreasureItem item )
	{
		if ( item == null || !CanAccept( item.Definition ) )
			return;

		GoldBarStackLattice.CacheFromItem( item );
		if ( _definition == null )
			_definition = item.Definition;

		int index = _items.Count;
		_items.Add( item );
		ApplySettledPose( item, index );
		RefreshCollider();
		PlayLandFeedback();
		AbsorbNearbyLooseBars();
	}

	public void BeginAppendFlight( TreasureItem item )
	{
		if ( item == null || item.Definition == null || !CanAccept( item.Definition ) )
			return;

		GoldBarStackLattice.CacheFromItem( item );
		if ( _definition == null )
			_definition = item.Definition;

		TreasureSurfaceWorld surfaceWorld = TreasureSurfaceWorld.Instance;
		if ( surfaceWorld != null && surfaceWorld.Simulator != null )
			surfaceWorld.Simulator.Unregister( item );

		int slotIndex = _items.Count;
		_items.Add( null );
		_inFlight.Add( item );
		_inFlightSlotIndices.Add( slotIndex );
		GoldBarStackLattice.TryGetWorldPose( slotIndex, item, ContactPosition, transform.rotation, out Vector3 endPos, out Quaternion endRot );

		item.ClaimPendingStackOwner( this );
		item.BeginFlight();
		RefreshCollider();

		GoldBarStackSettings settings = GoldBarStack.Settings;
		float duration = settings != null ? settings.hopDuration : 0.28f;
		float hop = settings != null ? settings.hopHeight : 0.12f;
		TreasureMotionHost.Run( AppendFlightRoutine( item, endPos, endRot, duration, hop ) );
		AbsorbNearbyLooseBars();
	}

	IEnumerator AppendFlightRoutine( TreasureItem item, Vector3 endPos, Quaternion endRot, float duration, float hop )
	{
		List<TreasureItem> cluster = new List<TreasureItem>( 1 ) { item };
		Vector3[] ends = { endPos };
		Quaternion[] rots = { endRot };
		yield return CoinFlipMotion.AnimateWorldFlips(
			cluster,
			ends,
			rots,
			duration,
			arcHeight: hop * 0.35f,
			spins: 0f,
			useHopThenArc: true,
			hopHeight: hop,
			riseFraction: 0.32f,
			secondaryArcHeight: hop * 0.25f );

		if ( item == null || _destroying )
			yield break;

		int flightIndex = _inFlight.IndexOf( item );
		if ( flightIndex < 0 )
			yield break;

		int slotIndex = flightIndex < _inFlightSlotIndices.Count ? _inFlightSlotIndices[ flightIndex ] : _items.Count;
		_inFlight.RemoveAt( flightIndex );
		if ( flightIndex < _inFlightSlotIndices.Count )
			_inFlightSlotIndices.RemoveAt( flightIndex );

		item.EndFlight();

		if ( item.State == TreasureItemState.Held && item.Owner is PlayerCarry carry && carry.ContainsItem( item ) )
		{
			if ( slotIndex >= 0 && slotIndex < _items.Count )
			{
				_items.RemoveAt( slotIndex );
				DecrementInFlightSlotIndicesAfter( slotIndex );
			}

			RefreshCollider();
			if ( Count <= 0 && !HasInFlight )
				DestroyIfEmpty();
			yield break;
		}

		if ( slotIndex < 0 || slotIndex >= _items.Count )
		{
			slotIndex = _items.Count;
			_items.Add( item );
		}
		else
			_items[ slotIndex ] = item;

		ApplySettledPose( item, slotIndex );
		TreasureInteractSfx.PlayPlace( item.Definition, endPos );
		RefreshCollider();
		PlayLandFeedback();
		AbsorbNearbyLooseBars();
	}

	void ApplySettledPose( TreasureItem item, int index )
	{
		if ( item == null )
			return;

		GoldBarStackLattice.TryGetWorldPose( index, item, ContactPosition, transform.rotation, out Vector3 pos, out Quaternion rot );
		Vector3 localPos = transform.InverseTransformPoint( pos );
		Quaternion localRot = Quaternion.Inverse( transform.rotation ) * rot;
		item.EnterStacked( this, transform, localPos, localRot );
		item.SetMeshVisible( true );
		SetItemCollidersEnabled( item, false );
	}

	void RestackSettled()
	{
		for ( int i = 0; i < _items.Count; i++ )
		{
			TreasureItem member = _items[ i ];
			if ( member == null || member.IsInFlight )
				continue;
			ApplySettledPose( member, i );
		}
	}

	void DecrementInFlightSlotIndicesAfter( int removedIndex )
	{
		for ( int i = 0; i < _inFlightSlotIndices.Count; i++ )
		{
			if ( _inFlightSlotIndices[ i ] > removedIndex )
				_inFlightSlotIndices[ i ]--;
		}
	}

	public void AbsorbNearbyLooseBars()
	{
		if ( _absorbingNearby || _destroying || IsFull )
			return;

		float radius = GoldBarStack.ResolveJoinRadius( _definition );
		if ( radius <= 0.0001f )
			return;

		_absorbingNearby = true;
		try
		{
			int hits = Physics.OverlapSphereNonAlloc(
				ContactPosition,
				radius,
				AbsorbOverlap,
				Physics.DefaultRaycastLayers,
				QueryTriggerInteraction.Ignore );

			for ( int i = 0; i < hits; i++ )
			{
				if ( IsFull )
					break;

				Collider col = AbsorbOverlap[ i ];
				if ( col == null )
					continue;

				TreasureItem candidate = col.GetComponentInParent<TreasureItem>();
				if ( candidate == null || !candidate.IsWorldLoose || candidate.IsInFlight || candidate.IsReclaiming )
					continue;
				if ( candidate.Owner is GroundGoldBarStack )
					continue;
				if ( !CanAccept( candidate.Definition ) )
					continue;

				Vector3 delta = candidate.transform.position - ContactPosition;
				float xzSq = delta.x * delta.x + delta.z * delta.z;
				if ( xzSq > radius * radius )
					continue;

				BeginAppendFlight( candidate );
			}
		}
		finally
		{
			_absorbingNearby = false;
		}
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null || _taking || HasInFlight || Count <= 0 )
			return false;

		PlayerCarry carry = player.Carry;
		if ( carry == null )
			return false;

		TreasureItem top = _items[ _items.Count - 1 ];
		return top != null && top.Definition != null && carry.CanAdd( top.Definition );
	}

	public override void Interact( PlayerController player )
	{
		if ( player == null || _taking || HasInFlight || Count <= 0 )
			return;

		TryTakeTop( player );
	}

	public bool TryTakeTop( PlayerController player )
	{
		if ( player == null || player.Carry == null || _taking || HasInFlight || Count <= 0 )
			return false;

		TreasureItem top = _items[ _items.Count - 1 ];
		if ( top == null || top.Definition == null )
			return false;

		PlayerCarry carry = player.Carry;
		if ( !carry.CanAdd( top.Definition ) )
			return false;

		_taking = true;
		_items.RemoveAt( _items.Count - 1 );
		RestackSettled();
		RefreshCollider();

		carry.TrySetSelectedBucket( CarryBucketKind.Artifact );
		bool received = carry.TryAddExisting( top );
		if ( !received )
		{
			_items.Add( top );
			ApplySettledPose( top, _items.Count - 1 );
			RefreshCollider();
			_taking = false;
			return false;
		}

		_taking = false;
		if ( Count <= 0 && !HasInFlight )
			DestroyIfEmpty();
		else
			PlayRemoveFeedback();
		return true;
	}

	public bool TryConsumeAllItems( List<TreasureItem> into )
	{
		if ( into == null || _destroying )
			return false;

		TakeBuffer.Clear();
		for ( int i = 0; i < _items.Count; i++ )
		{
			if ( _items[ i ] != null )
				TakeBuffer.Add( _items[ i ] );
		}

		for ( int i = 0; i < _inFlight.Count; i++ )
		{
			TreasureItem flight = _inFlight[ i ];
			if ( flight == null )
				continue;
			flight.EndFlight();
			TakeBuffer.Add( flight );
		}

		_destroying = true;
		_items.Clear();
		_inFlight.Clear();
		_inFlightSlotIndices.Clear();
		All.Remove( this );

		for ( int i = 0; i < TakeBuffer.Count; i++ )
		{
			TreasureItem member = TakeBuffer[ i ];
			if ( member == null )
				continue;
			member.DetachOwnerSilently();
			member.transform.SetParent( null, true );
			into.Add( member );
		}

		TakeBuffer.Clear();
		Destroy( gameObject );
		return into.Count > 0;
	}

	void EnsureCollider()
	{
		if ( _box == null )
			_box = GetComponent<BoxCollider>();
		if ( _box == null )
			_box = gameObject.AddComponent<BoxCollider>();
		_box.isTrigger = false;
	}

	void RefreshCollider()
	{
		EnsureCollider();
		if ( _box == null )
			return;

		int visualCount = Mathf.Max( 1, Count + InFlightCount );
		GoldBarStackLattice.GoldBarSize size = GoldBarStackLattice.Measure( _definition, Count > 0 ? _items[ 0 ] : null );
		GoldBarStackSettings settings = GoldBarStack.Settings;
		float height = GoldBarStackLattice.GetStackHeight( visualCount, size, settings );
		float radius = GoldBarStackLattice.GetFootprintRadius( size, settings );
		float span = Mathf.Max( 0.12f, radius * 2f );

		_box.enabled = Count > 0 || HasInFlight;
		_box.size = new Vector3( span, Mathf.Max( height, 0.08f ), span );
		_box.center = new Vector3( 0f, height * 0.5f, 0f );
	}

	static void SetItemCollidersEnabled( TreasureItem item, bool enabled )
	{
		if ( item == null )
			return;

		Collider[] colliders = item.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < colliders.Length; i++ )
		{
			if ( colliders[ i ] != null )
				colliders[ i ].enabled = enabled;
		}
	}

	void DestroyIfEmpty()
	{
		if ( _destroying )
			return;
		if ( Count > 0 || HasInFlight )
			return;
		_destroying = true;
		All.Remove( this );
		Destroy( gameObject );
	}

	void PlayLandFeedback()
	{
		EnsureCountFeedback();
		if ( onRemoveFeedback != null )
			onRemoveFeedback.Stop();
		if ( onLandFeedback != null )
			onLandFeedback.Play();
	}

	void PlayRemoveFeedback()
	{
		EnsureCountFeedback();
		if ( onLandFeedback != null )
			onLandFeedback.Stop();
		if ( onRemoveFeedback != null )
			onRemoveFeedback.Play();
	}

	void EnsureCountFeedback()
	{
		if ( onLandFeedback == null )
			onLandFeedback = CreatePunchFeedback( "OnAddFeedbacks", new Vector3( 0.07f, -0.09f, 0.07f ), 0.18f );
		if ( onRemoveFeedback == null )
			onRemoveFeedback = CreatePunchFeedback( "OnRemoveFeedbacks", new Vector3( -0.045f, 0.05f, -0.045f ), 0.14f );
	}

	Feedbacks CreatePunchFeedback( string childName, Vector3 punch, float duration )
	{
		Transform existing = transform.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();

		feedbacks.Initialize();
		if ( feedbacks.FeedbackList != null && feedbacks.FeedbackList.Count > 0 )
			return feedbacks;

		feedbacks.AddFeedback( new PunchScaleFeedback
		{
			Target = transform,
			Punch = punch,
			Duration = duration
		} );
		return feedbacks;
	}
}
