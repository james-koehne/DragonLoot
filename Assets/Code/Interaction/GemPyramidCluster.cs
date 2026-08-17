using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Same-type gem pyramid coordinator. Does not own gems — members stay loose / pickable.
/// Slots are sparse and stable: removing a gem leaves a hole; other gems keep their poses.
/// Square packing grows without a max layer or gem cap.
/// </summary>
public sealed class GemPyramidCluster
{
	readonly List<TreasureItem> _members = new List<TreasureItem>( 16 );
	readonly Dictionary<TreasureItem, int> _slotByMember = new Dictionary<TreasureItem, int>( 16 );
	readonly HashSet<int> _occupiedSlots = new HashSet<int>();
	readonly HashSet<long> _occupiedPoseKeys = new HashSet<long>();

	public TreasureDefinition Definition { get; private set; }
	public Vector3 ContactPosition { get; private set; }
	public int Count => _members.Count;
	public IReadOnlyList<TreasureItem> Members => _members;

	public GemPyramidCluster( TreasureDefinition definition, Vector3 contact )
	{
		Definition = definition;
		ContactPosition = contact;
		SeatContactOnSurface();
	}

	public bool Contains( TreasureItem item )
	{
		return item != null && _slotByMember.ContainsKey( item );
	}

	public bool TryGetSlot( TreasureItem item, out int slot )
	{
		slot = -1;
		if ( item == null )
			return false;
		return _slotByMember.TryGetValue( item, out slot );
	}

	public bool IsFull( TreasureSurfaceDefinition surfaceDef )
	{
		// Pyramids grow without a hard cap.
		return false;
	}

	/// <summary>
	/// XZ catch radius for joining: covers the current footprint (and a few lookahead
	/// lattice slots) plus <see cref="TreasureSurfaceDefinition.gemPyramidJoinRadius"/>.
	/// </summary>
	public float GetCatchRadiusXZ( TreasureSurfaceDefinition surfaceDef )
	{
		float padding = surfaceDef != null ? Mathf.Max( 0.05f, surfaceDef.gemPyramidJoinRadius ) : 0.55f;
		float spacing = EstimateSpacing( surfaceDef );
		float extent = 0f;

		for ( int i = 0; i < _members.Count; i++ )
		{
			TreasureItem member = _members[ i ];
			if ( member == null )
				continue;
			extent = Mathf.Max( extent, XzDistance( ContactPosition, member.transform.position ) );
		}

		// Include upcoming lattice slots so the growing edge stays joinable.
		int lookAhead = Mathf.Max( _occupiedSlots.Count + 12, 16 );
		for ( int s = 0; s < lookAhead; s++ )
		{
			if ( !GemPyramidLattice.TryGetSlotLocal( s, spacing, out Vector3 local ) )
				break;
			float e = Mathf.Sqrt( local.x * local.x + local.z * local.z );
			if ( e > extent )
				extent = e;
		}

		return extent + padding + spacing;
	}

	/// <summary>Min XZ distance from a world point to this cluster (contact or any member).</summary>
	public float GetDistanceXZ( Vector3 worldPos )
	{
		float best = XzDistance( ContactPosition, worldPos );
		for ( int i = 0; i < _members.Count; i++ )
		{
			TreasureItem member = _members[ i ];
			if ( member == null )
				continue;
			float d = XzDistance( member.transform.position, worldPos );
			if ( d < best )
				best = d;
		}

		return best;
	}

	public bool IsInJoinRange( Vector3 worldPos, TreasureSurfaceDefinition surfaceDef )
	{
		return GetDistanceXZ( worldPos ) <= GetCatchRadiusXZ( surfaceDef );
	}

	static float EstimateSpacing( TreasureSurfaceDefinition surfaceDef )
	{
		float push = surfaceDef != null ? Mathf.Max( 0.05f, surfaceDef.gemPushRadius ) : 0.18f;
		float scale = surfaceDef != null ? Mathf.Max( 0.5f, surfaceDef.gemPyramidSpacingScale ) : 1.05f;
		return Mathf.Max( 0.05f, push * scale );
	}

	static float XzDistance( Vector3 a, Vector3 b )
	{
		float dx = a.x - b.x;
		float dz = a.z - b.z;
		return Mathf.Sqrt( dx * dx + dz * dz );
	}

	public bool TryPreviewNextPose(
		TreasureItem gem,
		TreasureSurfaceDefinition surfaceDef,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		int slot = FindLowestFreeSlot();
		return TryBuildWorldPose( gem, slot, surfaceDef, out worldPos, out worldRot );
	}

	/// <summary>Register gem into the lowest free slot. Never moves existing members.</summary>
	public void AddOrUpdate( TreasureItem gem, TreasureSurfaceDefinition surfaceDef, bool animateJoining )
	{
		if ( gem == null || gem.Definition == null || surfaceDef == null )
			return;

		if ( !GemPyramidRegistry.AreSameGemType( Definition, gem.Definition ) )
			return;

		if ( _slotByMember.ContainsKey( gem ) )
			return;

		if ( _members.Count == 0 )
		{
			ContactPosition = gem.transform.position;
			SeatContactOnSurface();
		}

		int slot = FindLowestFreeSlot();
		if ( slot < 0 )
			return;

		if ( !TryBuildWorldPose( gem, slot, surfaceDef, out Vector3 pos, out Quaternion rot ) )
			return;

		long poseKey = PoseKey( pos );
		if ( _occupiedPoseKeys.Contains( poseKey ) )
		{
			slot = FindNextFreeSlotWithUniquePose( gem, surfaceDef, slot + 1, out pos, out rot, out poseKey );
			if ( slot < 0 )
				return;
		}

		_members.Add( gem );
		_slotByMember[ gem ] = slot;
		_occupiedSlots.Add( slot );
		_occupiedPoseKeys.Add( poseKey );

		if ( slot == 0 && _members.Count == 1 )
		{
			// Keep the seed on the aimed XZ: slot 0 is a 2×2 corner, so shift contact
			// so Contact + local0 lands on the aimed point.
			Vector3 seeded = gem.transform.position;
			float radius = Mathf.Max(
				surfaceDef.gemPushRadius,
				TreasureSurfaceSeat.EstimatePushRadius( gem, surfaceDef.gemPushRadius ) );
			float spacing = Mathf.Max( 0.05f, radius * surfaceDef.gemPyramidSpacingScale );
			if ( GemPyramidLattice.TryGetSlotLocal( 0, spacing, out Vector3 local0 ) )
			{
				ContactPosition = new Vector3(
					seeded.x - local0.x,
					ContactPosition.y,
					seeded.z - local0.z );
				SeatContactOnSurface();
			}
			else
			{
				ContactPosition = new Vector3( seeded.x, ContactPosition.y, seeded.z );
				SeatContactOnSurface();
			}

			if ( !TryBuildWorldPose( gem, 0, surfaceDef, out pos, out _ ) )
				return;
			_occupiedPoseKeys.Remove( poseKey );
			poseKey = PoseKey( pos );
			_occupiedPoseKeys.Add( poseKey );

			// Lone floor gems keep their rolled orientation; pyramids right themselves on join.
			Vector3 seedPos = gem.transform.position;
			seedPos.y = pos.y;
			ApplySettledPose( gem, seedPos, gem.transform.rotation );
			return;
		}

		if ( animateJoining )
			GemPyramidRegistry.TuckGemToPose( gem, pos, rot, surfaceDef );
		else
			ApplySettledPose( gem, pos, rot );
	}

	/// <summary>Remove gem without moving the rest of the pyramid.</summary>
	public void Remove( TreasureItem gem, TreasureSurfaceDefinition surfaceDef, bool animateCollapse )
	{
		if ( gem == null || !_slotByMember.TryGetValue( gem, out int slot ) )
			return;

		if ( TryBuildWorldPose( gem, slot, surfaceDef, out Vector3 pos, out _ ) )
			_occupiedPoseKeys.Remove( PoseKey( pos ) );

		_members.Remove( gem );
		_slotByMember.Remove( gem );
		_occupiedSlots.Remove( slot );

		if ( _members.Count == 0 )
		{
			_occupiedPoseKeys.Clear();
			GemPyramidRegistry.DestroyCluster( this );
			return;
		}
	}

	int FindLowestFreeSlot()
	{
		for ( int s = 0; s <= _occupiedSlots.Count; s++ )
		{
			if ( !_occupiedSlots.Contains( s ) )
				return s;
		}

		return _occupiedSlots.Count;
	}

	int FindNextFreeSlotWithUniquePose(
		TreasureItem gem,
		TreasureSurfaceDefinition surfaceDef,
		int startSlot,
		out Vector3 pos,
		out Quaternion rot,
		out long poseKey )
	{
		pos = ContactPosition;
		rot = Quaternion.identity;
		poseKey = 0;

		// Search a generous window beyond current occupancy; lattice grows on demand.
		int limit = Mathf.Max( startSlot + 64, _occupiedSlots.Count + 64 );
		for ( int s = Mathf.Max( 0, startSlot ); s < limit; s++ )
		{
			if ( _occupiedSlots.Contains( s ) )
				continue;
			if ( !TryBuildWorldPose( gem, s, surfaceDef, out pos, out rot ) )
				continue;
			poseKey = PoseKey( pos );
			if ( _occupiedPoseKeys.Contains( poseKey ) )
				continue;
			return s;
		}

		return -1;
	}

	static long PoseKey( Vector3 worldPos )
	{
		int x = Mathf.RoundToInt( worldPos.x * 1000f );
		int y = Mathf.RoundToInt( worldPos.y * 1000f );
		int z = Mathf.RoundToInt( worldPos.z * 1000f );
		return ( ( long )x & 0x1FFFFF ) << 42 | ( ( long )y & 0x1FFFFF ) << 21 | ( ( long )z & 0x1FFFFF );
	}

	void SeatContactOnSurface()
	{
		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world == null || world.Sampler == null )
			return;

		if ( world.Sampler.TrySample( ContactPosition, out TreasureSurfaceSample sample ) && sample.Traversable )
			ContactPosition = new Vector3( ContactPosition.x, sample.Height, ContactPosition.z );
	}

	bool TryBuildWorldPose(
		TreasureItem gem,
		int slotIndex,
		TreasureSurfaceDefinition surfaceDef,
		out Vector3 worldPos,
		out Quaternion worldRot )
	{
		worldPos = ContactPosition;
		worldRot = gem != null ? gem.transform.rotation : Quaternion.identity;
		if ( gem == null || surfaceDef == null || slotIndex < 0 )
			return false;

		float radius = Mathf.Max(
			surfaceDef.gemPushRadius,
			TreasureSurfaceSeat.EstimatePushRadius( gem, surfaceDef.gemPushRadius ) );
		float spacing = Mathf.Max( 0.05f, radius * surfaceDef.gemPyramidSpacingScale );

		if ( !GemPyramidLattice.TryGetSlotLocal( slotIndex, spacing, out Vector3 local ) )
			return false;

		float lift = TreasureSurfaceSeat.GetStableContactLift( gem );
		Vector3 candidate = ContactPosition + new Vector3( local.x, 0f, local.z );
		worldPos = new Vector3( candidate.x, ContactPosition.y + lift + local.y, candidate.z );
		worldRot = TreasureOrientation.FlattenUpright( gem.transform.rotation );
		return true;
	}

	public static void ApplySettledPose( TreasureItem gem, Vector3 pos, Quaternion rot )
	{
		if ( gem == null )
			return;

		TreasureSurfaceWorld world = TreasureSurfaceWorld.Instance;
		if ( world != null && world.Simulator != null )
			world.Simulator.Unregister( gem );

		GemPyramidRegistry.ApplySettledPoseSuppressed( gem, pos, rot );
	}
}
