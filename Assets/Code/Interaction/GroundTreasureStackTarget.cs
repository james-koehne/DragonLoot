using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Places held treasure as a settled kinematic stack on top of a loose physics treasure.
/// Always targets the highest coin in the aimed column, regardless of which coin was hit.
/// Settled stacks ignore forces so other loot cannot shove them over.
/// </summary>
public sealed class GroundTreasureStackTarget : ITreasurePlacementTarget
{
	static readonly List<TreasureItem> SupportBuffer = new List<TreasureItem>();
	static readonly Dictionary<int, float> PendingHeightByBottomId = new Dictionary<int, float>();
	static int _previewBottomId;
	static float _previewReservedHeight;

	/// <summary>
	/// Reserved vertical space above the settled column top (in-flight deposits + optional aim preview).
	/// </summary>
	public static float GetIncomingStackHeight( TreasureItem columnBottom, bool includePreview )
	{
		if ( columnBottom == null )
			return 0f;

		int id = columnBottom.GetInstanceID();
		float height = 0f;
		if ( PendingHeightByBottomId.TryGetValue( id, out float inFlight ) )
			height += inFlight;

		if ( includePreview && _previewBottomId == id )
			height += _previewReservedHeight;

		return height;
	}

	/// <summary>
	/// While aiming a valid ground-stack deposit, reserve thickness so throws / other placements stack above the ghost slot.
	/// </summary>
	public static void SetPreviewReservation( TreasureItem columnBottom, float height )
	{
		ClearPreviewReservation();
		if ( columnBottom == null || height <= 0.0001f )
			return;

		_previewBottomId = columnBottom.GetInstanceID();
		_previewReservedHeight = height;
	}

	public static void ClearPreviewReservation()
	{
		_previewBottomId = 0;
		_previewReservedHeight = 0f;
	}

	float _upBias;
	float _releaseSpeedScale;
	float _releaseUpScale;
	TreasureItem _baseItem;

	public GroundTreasureStackTarget( float upBias, float releaseSpeedScale, float releaseUpScale )
	{
		SetTuning( upBias, releaseSpeedScale, releaseUpScale );
	}

	public void SetTuning( float upBias, float releaseSpeedScale, float releaseUpScale )
	{
		_upBias = upBias;
		_releaseSpeedScale = releaseSpeedScale;
		_releaseUpScale = releaseUpScale;
	}

	public void SetBaseItem( TreasureItem item )
	{
		_baseItem = item;
	}

	public TreasureItem BaseItem => _baseItem;

	public bool CanPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( item == null || query.Player == null || _baseItem == null )
			return false;

		if ( !_baseItem.IsWorldLoose || _baseItem.IsReclaiming )
			return false;

		if ( !CanStackTogether( item, _baseItem ) )
			return false;

		PlayerCarry carry = query.Player.Carry;
		return carry != null && carry.Count > 0;
	}

	static bool CanStackTogether( TreasureItem placing, TreasureItem onto )
	{
		if ( placing == null || onto == null )
			return false;

		TreasureDefinition placingDef = placing.Definition;
		TreasureDefinition ontoDef = onto.Definition;
		if ( placingDef == null || ontoDef == null )
			return false;

		// Gems never form loose floor stacks (table rules may still use canStack separately).
		if ( placingDef.category == TreasureCategory.Gem || ontoDef.category == TreasureCategory.Gem )
			return false;

		return placingDef.canStack && ontoDef.canStack;
	}

	/// <summary>Whether held treasure may stack onto a loose world column (ground deposit).</summary>
	public static bool CanStackLoose( TreasureItem placing, TreasureItem onto )
	{
		return CanStackTogether( placing, onto );
	}

	public bool TryGetPlacementPreview( TreasureItem item, in PlacementQuery query, out PlacementPreview preview )
	{
		preview = default;
		if ( item == null || _baseItem == null )
			return false;

		if ( !TryResolveStackPoint( item, out Vector3 placePos, out Quaternion placeRot ) )
			return false;

		Vector3 scale = item.GetWorldScale();
		TreasureItem bottom = TreasureSupportStack.FindColumnBottom( _baseItem );
		if ( bottom == null )
			bottom = _baseItem;

		preview.SetItemMesh(
			placePos,
			placeRot,
			scale,
			CanPlace( item, in query ) );
		return true;
	}

	public bool TryPlace( TreasureItem item, in PlacementQuery query )
	{
		if ( !CanPlace( item, in query ) )
			return false;

		PlayerController player = query.Player;
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry == null )
			return false;

		// Coin ground deposits go through owned GroundCoinStack (spam-safe logical slots).
		if ( GroundCoinStack.IsGroundStackableCoin( item )
			&& _baseItem != null
			&& GroundCoinStack.IsGroundStackableCoin( _baseItem ) )
		{
			if ( !carry.TryRemoveBottomCluster( out List<TreasureItem> coinCluster )
				|| coinCluster == null
				|| coinCluster.Count == 0 )
				return false;

			GroundCoinStack stack = GroundCoinStack.FindStackForLooseCoin( _baseItem );
			if ( stack == null )
			{
				stack = GroundCoinStack.CreateAt( _baseItem.transform.position, _baseItem.transform.rotation );
				stack.AbsorbSettledImmediate( _baseItem );
			}
			else if ( !( _baseItem.Owner is GroundCoinStack owned && owned == stack ) )
			{
				stack.TryAbsorbLooseImmediate( _baseItem );
			}

			for ( int i = 0; i < coinCluster.Count; i++ )
			{
				TreasureItem member = coinCluster[ i ];
				if ( member == null )
					continue;

				if ( !stack.CanAccept( member.Definition ) )
				{
					member.EnterPhysics( member.transform.position, member.transform.rotation );
					continue;
				}

				stack.BeginAppendFlight( member, stack.transform.rotation );
			}

			stack.TryMergeNearby();
			stack.AbsorbNearbyLooseCoins();
			ClearPreviewReservation();
			return true;
		}

		if ( !TryResolveStackPoint( item, out Vector3 placePos, out Quaternion placeRot ) )
			return false;

		if ( !carry.TryRemoveBottomCluster( out List<TreasureItem> cluster ) || cluster == null || cluster.Count == 0 )
			return false;

		BuildStackEndPoses( cluster, placePos, placeRot, out Vector3[] ends, out Quaternion[] rots );

		TreasureItem baseItem = _baseItem;
		TreasureItem columnBottom = ResolveColumnBottom( baseItem );
		float reservedHeight = MeasureClusterHeight( cluster );
		ClearPreviewReservation();
		AddPendingHeight( columnBottom, reservedHeight );

		for ( int i = 0; i < cluster.Count; i++ )
		{
			if ( cluster[ i ] != null )
				cluster[ i ].BeginFlight();
		}

		bool flipCoin = CoinFlipMotion.IsCoin( item );
		TreasureMotionHost.Run( CoinFlipGroundStackRoutine(
			cluster,
			ends,
			rots,
			baseItem,
			columnBottom,
			reservedHeight,
			flipCoin ? CoinFlipMotion.DefaultDuration : CoinFlipMotion.DefaultItemArcDuration,
			flipCoin ? CoinFlipMotion.DefaultArcHeight : CoinFlipMotion.DefaultItemArcHeight,
			flipCoin ? CoinFlipMotion.DefaultSpins : 0f ) );
		return true;
	}

	static IEnumerator CoinFlipGroundStackRoutine(
		List<TreasureItem> cluster,
		Vector3[] ends,
		Quaternion[] rots,
		TreasureItem baseItem,
		TreasureItem columnBottom,
		float reservedHeight,
		float duration,
		float arcHeight,
		float spins )
	{
		yield return CoinFlipMotion.AnimateWorldFlips( cluster, ends, rots, duration, arcHeight, spins );

		// Clear reservation before items become world-loose so the next place cannot
		// double-count settledHeight + pending for the same coins.
		RemovePendingHeight( columnBottom, reservedHeight );

		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			member.EndFlight();

			// Picked up (or otherwise claimed) mid-flight — leave it alone.
			if ( member.State == TreasureItemState.Held && member.Owner is PlayerCarry carry && carry.ContainsItem( member ) )
				continue;

			member.EnterSettledPhysics( ends[ i ], rots[ i ] );
			CoinGemInteractFeedback.PlayPlace( member );
			TreasureInteractSfx.PlayPlace( member.Definition, ends[ i ] );
		}

		SettleSupportColumn( baseItem != null ? baseItem : columnBottom );
	}

	static void BuildStackEndPoses(
		List<TreasureItem> cluster,
		Vector3 anchorDropPos,
		Quaternion anchorRot,
		out Vector3[] ends,
		out Quaternion[] rots )
	{
		ends = new Vector3[ cluster.Count ];
		rots = new Quaternion[ cluster.Count ];

		float stackedY = 0f;
		for ( int i = 0; i < cluster.Count; i++ )
		{
			TreasureItem member = cluster[ i ];
			if ( member == null )
				continue;

			ends[ i ] = anchorDropPos + Vector3.up * stackedY;
			rots[ i ] = TreasureOrientation.FlattenUpright( anchorRot );
			stackedY += TreasureStackSpacing.GetStep( member );
		}
	}

	public void Remove( TreasureItem item )
	{
		// Ground stack does not own items after physics release.
	}

	bool TryResolveStackPoint( TreasureItem placing, out Vector3 placePos, out Quaternion placeRot )
	{
		placePos = default;
		placeRot = Quaternion.identity;
		if ( _baseItem == null )
			return false;

		TreasureSupportStack.CollectColumn( _baseItem, SupportBuffer );
		if ( SupportBuffer.Count == 0 )
			SupportBuffer.Add( _baseItem );

		TreasureItem bottom = SupportBuffer[ 0 ];
		if ( bottom == null )
			bottom = _baseItem;

		float settledHeight = MeasureSettledColumnHeight( SupportBuffer );
		float pending = GetIncomingStackHeight( bottom, includePreview: false );
		Vector3 bottomPos = bottom.transform.position;
		// Next center = bottom center + one step per settled coin + in-flight reservations.
		placePos = new Vector3( bottomPos.x, bottomPos.y + settledHeight + pending, bottomPos.z );
		placeRot = TreasureOrientation.FlattenUpright( bottom.transform.rotation );
		SupportBuffer.Clear();
		return true;
	}

	static TreasureItem ResolveColumnBottom( TreasureItem seed )
	{
		if ( seed == null )
			return null;

		TreasureSupportStack.CollectColumn( seed, SupportBuffer );
		TreasureItem bottom = SupportBuffer.Count > 0 ? SupportBuffer[ 0 ] : seed;
		SupportBuffer.Clear();
		return bottom;
	}

	static float MeasureClusterHeight( List<TreasureItem> cluster )
	{
		float height = 0f;
		if ( cluster == null )
			return height;

		for ( int i = 0; i < cluster.Count; i++ )
			height += TreasureStackSpacing.GetStep( cluster[ i ] );

		return height;
	}

	static float MeasureSettledColumnHeight( List<TreasureItem> columnBottomToTop )
	{
		float height = 0f;
		if ( columnBottomToTop == null )
			return height;

		for ( int i = 0; i < columnBottomToTop.Count; i++ )
		{
			TreasureItem item = columnBottomToTop[ i ];
			if ( item == null || item.IsReclaiming || item.IsInFlight || !item.IsWorldLoose )
				continue;

			height += TreasureStackSpacing.GetStep( item );
		}

		return height;
	}

	static void AddPendingHeight( TreasureItem bottom, float height )
	{
		if ( bottom == null || height <= 0.0001f )
			return;

		int id = bottom.GetInstanceID();
		PendingHeightByBottomId.TryGetValue( id, out float current );
		PendingHeightByBottomId[ id ] = current + height;
	}

	static void RemovePendingHeight( TreasureItem bottom, float height )
	{
		if ( bottom == null || height <= 0.0001f )
			return;

		int id = bottom.GetInstanceID();
		if ( !PendingHeightByBottomId.TryGetValue( id, out float current ) )
			return;

		current -= height;
		if ( current <= 0.0001f )
			PendingHeightByBottomId.Remove( id );
		else
			PendingHeightByBottomId[ id ] = current;
	}

	/// <summary>
	/// World Y for the next coin center above the settled top of this column (for throws / auto-stack).
	/// </summary>
	public static bool TryGetNextStackCenterY( TreasureItem columnSeed, out float centerY )
	{
		centerY = 0f;
		if ( columnSeed == null )
			return false;

		TreasureSupportStack.CollectColumn( columnSeed, SupportBuffer );
		if ( SupportBuffer.Count == 0 )
			SupportBuffer.Add( columnSeed );

		TreasureItem bottom = SupportBuffer[ 0 ];
		if ( bottom == null )
		{
			SupportBuffer.Clear();
			return false;
		}

		float settledHeight = MeasureSettledColumnHeight( SupportBuffer );
		float pending = GetIncomingStackHeight( bottom, includePreview: true );
		centerY = bottom.transform.position.y + settledHeight + pending;
		SupportBuffer.Clear();
		return true;
	}

	static void SettleSupportColumn( TreasureItem baseItem )
	{
		RestackColumnByThickness( baseItem );
	}

	/// <summary>
	/// Re-seats an entire loose column using definition thickness from the bottom coin —
	/// same spacing as hand / table / coin-stack piles.
	/// </summary>
	public static void RestackColumnByThickness( TreasureItem seed )
	{
		if ( seed == null )
			return;

		List<TreasureItem> column = new List<TreasureItem>();
		TreasureSupportStack.CollectColumn( seed, column );
		if ( column.Count == 0 )
		{
			if ( seed.IsWorldLoose && !seed.IsInFlight )
				seed.SettlePhysicsInPlace();
			return;
		}

		TreasureItem bottom = column[ 0 ];
		if ( bottom == null )
			return;

		Vector3 basePos = bottom.transform.position;
		Quaternion baseRot = TreasureOrientation.FlattenUpright( bottom.transform.rotation );
		float stackedY = 0f;

		for ( int i = 0; i < column.Count; i++ )
		{
			TreasureItem member = column[ i ];
			if ( member == null )
				continue;

			member.ApplyWorldScale();

			Vector3 pos = basePos + Vector3.up * stackedY;
			Quaternion rot = TreasureOrientation.FlattenUpright( member.transform.rotation );
			if ( i == 0 )
				rot = baseRot;

			if ( !member.IsInFlight )
				member.ApplySettledWorldPose( pos, rot );

			stackedY += TreasureStackSpacing.GetStep( member );
		}

		CoinColumnCylinderBinder.BindToBottomItem( bottom, column, snap: true );
	}
}
