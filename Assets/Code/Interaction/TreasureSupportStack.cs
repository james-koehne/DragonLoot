using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Builds a connected vertical column of Physics treasure around a selected item.
/// Uses XZ proximity + center height (not surface-gap contact), so overlapping coin
/// colliders still resolve as one stack.
/// </summary>
public static class TreasureSupportStack
{
	const float OverlapExpand = 0.05f;
	const float MaxCenterStep = 0.35f;
	const float XzRadiusScale = 1.15f;
	const int MaxOverlap = 64;
	const float ColumnSearchHalfHeight = 8f;

	static readonly Collider[] OverlapBuffer = new Collider[ MaxOverlap ];
	static readonly HashSet<TreasureItem> Visited = new HashSet<TreasureItem>();
	static readonly Queue<int> IndexFrontier = new Queue<int>();
	static readonly List<TreasureItem> SortBuffer = new List<TreasureItem>();
	static readonly List<TreasureItem> ColumnBuffer = new List<TreasureItem>();

	/// <summary>
	/// Fills <paramref name="results"/> bottom-to-top starting at <paramref name="selected"/>,
	/// then every Physics item stacked above it in the same column.
	/// </summary>
	public static void Collect( TreasureItem selected, List<TreasureItem> results )
	{
		results.Clear();
		if ( selected == null )
			return;

		CollectColumn( selected, ColumnBuffer );
		if ( ColumnBuffer.Count == 0 )
		{
			results.Add( selected );
			return;
		}

		int selectedIndex = 0;
		for ( int i = 0; i < ColumnBuffer.Count; i++ )
		{
			if ( ColumnBuffer[ i ] == selected )
			{
				selectedIndex = i;
				break;
			}
		}

		for ( int i = selectedIndex; i < ColumnBuffer.Count; i++ )
			results.Add( ColumnBuffer[ i ] );

		ColumnBuffer.Clear();
	}

	/// <summary>
	/// Full connected column containing <paramref name="member"/>, bottom-to-top.
	/// Aiming at any coin still resolves the whole tower.
	/// </summary>
	public static void CollectColumn( TreasureItem member, List<TreasureItem> results )
	{
		results.Clear();
		if ( member == null )
			return;

		if ( !member.IsWorldLoose )
		{
			results.Add( member );
			return;
		}

		GatherColumnCandidates( member, SortBuffer );
		if ( SortBuffer.Count == 0 )
		{
			results.Add( member );
			return;
		}

		SortBuffer.Sort( CompareBottomToTop );

		// Coin towers: ignore non-coin blockers in the Y-sorted candidate list so a mid-stack
		// hit still reaches the true bottom (cylinder host) and top.
		if ( CoinColumnCylinderBinder.IsCoin( member.Definition ) )
			FilterConnectedCoinColumn( member, SortBuffer, results );
		else
			FilterConnectedComponent( member, SortBuffer, results );

		SortBuffer.Clear();

		if ( results.Count == 0 )
			results.Add( member );
	}

	/// <summary>
	/// Same-type coin column containing <paramref name="seed"/>, skipping non-coin candidates that
	/// would otherwise break adjacency in the Y-sorted overlap list.
	/// </summary>
	static void FilterConnectedCoinColumn( TreasureItem seed, List<TreasureItem> sortedBottomToTop, List<TreasureItem> results )
	{
		results.Clear();
		if ( seed == null || sortedBottomToTop == null || sortedBottomToTop.Count == 0 )
			return;

		TreasureDefinition seedDef = seed.Definition;
		if ( !CoinColumnCylinderBinder.IsCoin( seedDef ) )
		{
			FilterConnectedComponent( seed, sortedBottomToTop, results );
			return;
		}

		int seedIndex = -1;
		for ( int i = 0; i < sortedBottomToTop.Count; i++ )
		{
			if ( sortedBottomToTop[ i ] == seed )
			{
				seedIndex = i;
				break;
			}
		}

		if ( seedIndex < 0 )
		{
			results.Add( seed );
			return;
		}

		Visited.Clear();
		IndexFrontier.Clear();
		Visited.Add( seed );
		IndexFrontier.Enqueue( seedIndex );

		while ( IndexFrontier.Count > 0 )
		{
			int index = IndexFrontier.Dequeue();
			TryEnqueueCoinNeighbor( sortedBottomToTop, seedDef, index, -1 );
			TryEnqueueCoinNeighbor( sortedBottomToTop, seedDef, index, 1 );
		}

		for ( int i = 0; i < sortedBottomToTop.Count; i++ )
		{
			TreasureItem item = sortedBottomToTop[ i ];
			if ( item != null && Visited.Contains( item ) )
				results.Add( item );
		}

		Visited.Clear();
		IndexFrontier.Clear();
	}

	static void TryEnqueueCoinNeighbor(
		List<TreasureItem> sorted,
		TreasureDefinition seedDef,
		int fromIndex,
		int direction )
	{
		if ( sorted == null || direction == 0 )
			return;

		TreasureItem from = sorted[ fromIndex ];
		if ( from == null )
			return;

		int i = fromIndex + direction;
		while ( i >= 0 && i < sorted.Count )
		{
			TreasureItem candidate = sorted[ i ];
			if ( candidate == null )
			{
				i += direction;
				continue;
			}

			if ( Visited.Contains( candidate ) )
				return;

			TreasureDefinition def = candidate.Definition;
			if ( !CoinColumnCylinderBinder.IsCoin( def ) )
			{
				// Skip non-coins in the overlap list; they must not split a coin tower.
				i += direction;
				continue;
			}

			if ( !SameCoinDefinition( seedDef, def ) )
				return;

			if ( !AreStackStepNeighbors( from, candidate ) && !AreStackStepNeighbors( candidate, from ) )
				return;

			Visited.Add( candidate );
			IndexFrontier.Enqueue( i );
			return;
		}
	}

	static bool SameCoinDefinition( TreasureDefinition a, TreasureDefinition b )
	{
		if ( a == b )
			return true;
		if ( a == null || b == null )
			return false;
		if ( !string.IsNullOrEmpty( a.id ) && a.id == b.id )
			return true;
		return false;
	}

	/// <summary>Lowest Physics support in the resting column containing <paramref name="item"/>.</summary>
	public static TreasureItem FindColumnBottom( TreasureItem item )
	{
		if ( item == null )
			return null;

		CollectColumn( item, ColumnBuffer );
		TreasureItem bottom = ColumnBuffer.Count > 0 ? ColumnBuffer[ 0 ] : item;
		ColumnBuffer.Clear();
		return bottom;
	}

	/// <summary>Highest Physics item in the resting column containing <paramref name="member"/>.</summary>
	public static TreasureItem FindColumnTop( TreasureItem member )
	{
		if ( member == null )
			return null;

		CollectColumn( member, ColumnBuffer );
		TreasureItem top = ColumnBuffer.Count > 0 ? ColumnBuffer[ ColumnBuffer.Count - 1 ] : member;
		ColumnBuffer.Clear();
		return top;
	}

	static void GatherColumnCandidates( TreasureItem seed, List<TreasureItem> into )
	{
		into.Clear();
		Bounds seedBounds = GetWorldBounds( seed );
		float radius = Mathf.Max( 0.04f, Mathf.Max( seedBounds.extents.x, seedBounds.extents.z ) * XzRadiusScale );
		Vector3 boxCenter = seedBounds.center;
		Vector3 halfExtents = new Vector3( radius + OverlapExpand, ColumnSearchHalfHeight, radius + OverlapExpand );

		int count = Physics.OverlapBoxNonAlloc(
			boxCenter,
			halfExtents,
			OverlapBuffer,
			Quaternion.identity,
			Physics.DefaultRaycastLayers,
			QueryTriggerInteraction.Ignore );

		float radiusSq = radius * radius;
		float seedX = seedBounds.center.x;
		float seedZ = seedBounds.center.z;

		Visited.Clear();
		for ( int i = 0; i < count; i++ )
		{
			Collider col = OverlapBuffer[ i ];
			if ( col == null )
				continue;

			TreasureItem candidate = col.GetComponentInParent<TreasureItem>();
			if ( candidate == null || !candidate.IsWorldLoose )
				continue;

			if ( Visited.Contains( candidate ) )
				continue;

			Vector3 center = GetWorldBounds( candidate ).center;
			float dx = center.x - seedX;
			float dz = center.z - seedZ;
			if ( dx * dx + dz * dz > radiusSq )
				continue;

			Visited.Add( candidate );
			into.Add( candidate );
		}

		if ( !Visited.Contains( seed ) )
			into.Add( seed );

		Visited.Clear();
	}

	static void FilterConnectedComponent( TreasureItem seed, List<TreasureItem> sortedBottomToTop, List<TreasureItem> results )
	{
		results.Clear();
		if ( sortedBottomToTop == null || sortedBottomToTop.Count == 0 )
			return;

		int seedIndex = -1;
		for ( int i = 0; i < sortedBottomToTop.Count; i++ )
		{
			if ( sortedBottomToTop[ i ] == seed )
			{
				seedIndex = i;
				break;
			}
		}

		if ( seedIndex < 0 )
		{
			results.Add( seed );
			return;
		}

		Visited.Clear();
		IndexFrontier.Clear();
		Visited.Add( seed );
		IndexFrontier.Enqueue( seedIndex );

		while ( IndexFrontier.Count > 0 )
		{
			int index = IndexFrontier.Dequeue();
			TryEnqueueNeighbor( sortedBottomToTop, index - 1 );
			TryEnqueueNeighbor( sortedBottomToTop, index + 1 );
		}

		for ( int i = 0; i < sortedBottomToTop.Count; i++ )
		{
			TreasureItem item = sortedBottomToTop[ i ];
			if ( item != null && Visited.Contains( item ) )
				results.Add( item );
		}

		Visited.Clear();
		IndexFrontier.Clear();
	}

	static void TryEnqueueNeighbor( List<TreasureItem> sorted, int neighborIndex )
	{
		if ( neighborIndex < 0 || neighborIndex >= sorted.Count )
			return;

		TreasureItem neighbor = sorted[ neighborIndex ];
		if ( neighbor == null || Visited.Contains( neighbor ) )
			return;

		// Neighbors in the Y-sorted list must be within a stack step of some visited member.
		// Check against immediate list neighbors that are already visited (contiguous tower).
		bool adjacentToVisited = false;
		if ( neighborIndex > 0 && Visited.Contains( sorted[ neighborIndex - 1 ] )
			&& AreStackStepNeighbors( sorted[ neighborIndex - 1 ], neighbor ) )
			adjacentToVisited = true;
		if ( !adjacentToVisited
			&& neighborIndex + 1 < sorted.Count
			&& Visited.Contains( sorted[ neighborIndex + 1 ] )
			&& AreStackStepNeighbors( neighbor, sorted[ neighborIndex + 1 ] ) )
			adjacentToVisited = true;

		if ( !adjacentToVisited )
			return;

		Visited.Add( neighbor );
		IndexFrontier.Enqueue( neighborIndex );
	}

	static bool AreStackStepNeighbors( TreasureItem lower, TreasureItem upper )
	{
		if ( lower == null || upper == null )
			return false;

		// Proximity alone must not glue non-stackable loot (e.g. gems) into a floor column.
		if ( !GroundTreasureStackTarget.CanStackLoose( upper, lower ) )
			return false;

		Bounds a = GetWorldBounds( lower );
		Bounds b = GetWorldBounds( upper );
		float dy = Mathf.Abs( b.center.y - a.center.y );
		float dx = b.center.x - a.center.x;
		float dz = b.center.z - a.center.z;
		float xzSq = dx * dx + dz * dz;

		// Overlapping / nearly identical centers (spam deposit race): still one column if XZ aligns.
		if ( dy < 0.0001f )
		{
			float align = Mathf.Max( 0.01f, Mathf.Min( a.extents.x, a.extents.z ) * 0.4f );
			return xzSq <= align * align;
		}

		float stepLower = TreasureStackSpacing.GetStep( lower );
		float stepUpper = TreasureStackSpacing.GetStep( upper );
		float maxStep = Mathf.Max( MaxCenterStep, stepLower + stepUpper + 0.06f );
		maxStep = Mathf.Max( maxStep, ( a.extents.y + b.extents.y ) + 0.08f );
		return dy <= maxStep;
	}

	static int CompareBottomToTop( TreasureItem a, TreasureItem b )
	{
		if ( a == b )
			return 0;
		if ( a == null )
			return 1;
		if ( b == null )
			return -1;

		float ay = a.transform.position.y;
		float by = b.transform.position.y;
		int cmp = ay.CompareTo( by );
		if ( cmp != 0 )
			return cmp;

		return a.GetInstanceID().CompareTo( b.GetInstanceID() );
	}

	static Bounds GetWorldBounds( TreasureItem item )
	{
		if ( item == null )
			return new Bounds( Vector3.zero, Vector3.one * 0.1f );

		Collider[] colliders = item.GetComponentsInChildren<Collider>( true );
		bool hasBounds = false;
		Bounds bounds = new Bounds( item.transform.position, Vector3.zero );

		for ( int i = 0; i < colliders.Length; i++ )
		{
			Collider col = colliders[ i ];
			if ( col == null || !col.enabled )
				continue;

			if ( !hasBounds )
			{
				bounds = col.bounds;
				hasBounds = true;
			}
			else
				bounds.Encapsulate( col.bounds );
		}

		if ( hasBounds )
			return bounds;

		// Do not use binder cylinder renderers — their tall bounds shift the center and
		// break mid-stack column connectivity / leftover cleanup.
		Renderer[] renderers = item.GetComponentsInChildren<Renderer>( true );
		if ( renderers != null )
		{
			for ( int i = 0; i < renderers.Length; i++ )
			{
				Renderer renderer = renderers[ i ];
				if ( renderer == null )
					continue;
				if ( CoinColumnCylinderBinder.IsBinderVisualRenderer( renderer.transform, item.transform ) )
					continue;

				if ( !hasBounds )
				{
					bounds = renderer.bounds;
					hasBounds = true;
				}
				else
					bounds.Encapsulate( renderer.bounds );
			}
		}

		if ( !hasBounds )
			bounds = new Bounds( item.transform.position, Vector3.one * 0.1f );

		return bounds;
	}
}
