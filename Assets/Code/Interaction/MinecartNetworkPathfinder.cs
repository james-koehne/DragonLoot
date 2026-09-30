using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Dijkstra over junction ports + along-track links so auto carts can travel between any connected tracks.
/// </summary>
public static class MinecartNetworkPathfinder
{
	public struct RouteLeg
	{
		public MinecartTrack Track;
		public float TargetDistance;
		public int TravelSign;
		public int JunctionIndex;
		public MinecartTrack ExitTrack;
		public float ExitDistance;
		public int ExitTravelSign;

		public bool TransfersAtJunction => JunctionIndex >= 0 && ExitTrack != null;
	}

	struct NodeKey : System.IEquatable<NodeKey>
	{
		public int TrackId;
		public int JunctionIndex;
		public int QuantizedDistance;
		public int ArriveSign;

		public bool Equals( NodeKey other )
		{
			return TrackId == other.TrackId
				&& JunctionIndex == other.JunctionIndex
				&& QuantizedDistance == other.QuantizedDistance
				&& ArriveSign == other.ArriveSign;
		}

		public override bool Equals( object obj )
		{
			return obj is NodeKey other && Equals( other );
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = TrackId;
				hash = ( hash * 397 ) ^ JunctionIndex;
				hash = ( hash * 397 ) ^ QuantizedDistance;
				hash = ( hash * 397 ) ^ ArriveSign;
				return hash;
			}
		}
	}

	struct SearchNode
	{
		public NodeKey Key;
		public float Cost;
		public MinecartTrack Track;
		public float Distance;
		public int JunctionIndex;
		public int ArriveSign;
		public int Parent;
		public int ViaJunction;
		public MinecartTrack ViaExitTrack;
		public float ViaExitDistance;
		public int ViaExitSign;
		public int ViaTravelSign;
		public float ViaFromDistance;
	}

	const float Quantize = 0.1f;
	const float JunctionBaseCost = 1f;
	const float GoalEpsilon = 0.05f;

	static readonly List<float> PortDistScratch = new List<float>( 32 );
	static readonly List<int> PortJuncScratch = new List<int>( 32 );
	static readonly List<int> Heap = new List<int>( 64 );
	static readonly List<SearchNode> Nodes = new List<SearchNode>( 128 );
	static readonly Dictionary<NodeKey, float> Best = new Dictionary<NodeKey, float>( 128 );

	public static bool TryFindRoute(
		MinecartTrack fromTrack,
		float fromDistance,
		MinecartTrack toTrack,
		float toDistance,
		List<RouteLeg> into,
		out float totalCost )
	{
		totalCost = 0f;
		if ( into == null || fromTrack == null || toTrack == null || !fromTrack.IsTravelReady || !toTrack.IsTravelReady )
			return false;

		into.Clear();

		if ( fromTrack == toTrack )
			return BuildSameTrackRoute( fromTrack, fromDistance, toDistance, into, out totalCost );

		MinecartJunctionGraph graph = MinecartJunctionGraph.FindActive();
		if ( graph == null )
			return false;

		Nodes.Clear();
		Best.Clear();
		Heap.Clear();

		PushNode( new SearchNode
		{
			Key = MakeKey( fromTrack, -1, fromDistance, 0 ),
			Cost = 0f,
			Track = fromTrack,
			Distance = fromDistance,
			JunctionIndex = -1,
			ArriveSign = 0,
			Parent = -1,
			ViaJunction = -1
		} );

		int goalIndex = -1;
		float goalCost = float.MaxValue;
		int expandGuard = 0;

		while ( Heap.Count > 0 && expandGuard++ < 8192 )
		{
			int currentIndex = PopHeap();
			SearchNode current = Nodes[ currentIndex ];

			float bestKnown;
			if ( Best.TryGetValue( current.Key, out bestKnown ) && current.Cost > bestKnown + 0.0001f )
				continue;

			if ( current.Track == toTrack )
			{
				float remain = Mathf.Abs( toTrack.SignedAlong( current.Distance, toDistance ) );
				float arriveCost = current.Cost + remain;
				if ( arriveCost < goalCost )
				{
					goalCost = arriveCost;
					goalIndex = currentIndex;
				}
			}

			ExpandToPortsOnTrack( graph, currentIndex, toTrack, toDistance );
			ExpandJunctionTransfers( graph, currentIndex );
		}

		if ( goalIndex < 0 )
			return false;

		totalCost = goalCost;
		return BuildLegs( fromTrack, fromDistance, toTrack, toDistance, goalIndex, into );
	}

	public static bool TryEstimateCost(
		MinecartTrack fromTrack,
		float fromDistance,
		MinecartTrack toTrack,
		float toDistance,
		out float cost )
	{
		List<RouteLeg> scratch = new List<RouteLeg>( 8 );
		return TryFindRoute( fromTrack, fromDistance, toTrack, toDistance, scratch, out cost );
	}

	static bool BuildSameTrackRoute(
		MinecartTrack track,
		float fromDistance,
		float toDistance,
		List<RouteLeg> into,
		out float totalCost )
	{
		float sep = track.SignedAlong( fromDistance, toDistance );
		totalCost = Mathf.Abs( sep );
		int sign = Mathf.Abs( sep ) <= GoalEpsilon ? 1 : ( sep >= 0f ? 1 : -1 );
		into.Add( new RouteLeg
		{
			Track = track,
			TargetDistance = toDistance,
			TravelSign = sign,
			JunctionIndex = -1
		} );
		return true;
	}

	static void ExpandToPortsOnTrack(
		MinecartJunctionGraph graph,
		int currentIndex,
		MinecartTrack goalTrack,
		float goalDistance )
	{
		SearchNode current = Nodes[ currentIndex ];
		MinecartTrack track = current.Track;
		if ( track == null )
			return;

		PortDistScratch.Clear();
		PortJuncScratch.Clear();
		graph.CollectPortsOnTrack( track, PortDistScratch, PortJuncScratch );

		for ( int i = 0; i < PortDistScratch.Count; i++ )
		{
			float portDist = PortDistScratch[ i ];
			int junctionIndex = PortJuncScratch[ i ];
			float sep = track.SignedAlong( current.Distance, portDist );
			int travelSign;
			float stepCost;
			if ( Mathf.Abs( sep ) <= GoalEpsilon )
			{
				if ( junctionIndex < 0 )
					continue;
				// Already at this port — enqueue as a junction-capable node.
				travelSign = current.ArriveSign != 0 ? current.ArriveSign : 1;
				stepCost = 0f;
			}
			else
			{
				travelSign = sep >= 0f ? 1 : -1;
				stepCost = Mathf.Abs( sep );
			}

			TryEnqueue(
				track,
				portDist,
				junctionIndex,
				travelSign,
				current.Cost + stepCost,
				currentIndex,
				viaJunction: -1,
				viaExitTrack: null,
				viaExitDistance: 0f,
				viaExitSign: 0,
				viaTravelSign: travelSign );
		}

		if ( track == goalTrack )
		{
			float sep = track.SignedAlong( current.Distance, goalDistance );
			if ( Mathf.Abs( sep ) > GoalEpsilon )
			{
				int travelSign = sep >= 0f ? 1 : -1;
				TryEnqueue(
					track,
					goalDistance,
					-1,
					travelSign,
					current.Cost + Mathf.Abs( sep ),
					currentIndex,
					viaJunction: -1,
					viaExitTrack: null,
					viaExitDistance: 0f,
					viaExitSign: 0,
					viaTravelSign: travelSign );
			}
		}
	}

	static void ExpandJunctionTransfers( MinecartJunctionGraph graph, int currentIndex )
	{
		SearchNode current = Nodes[ currentIndex ];
		if ( current.JunctionIndex < 0 || current.Track == null )
			return;

		MinecartJunction junction = graph.GetJunction( current.JunctionIndex );
		if ( junction == null || junction.ridePaths == null )
			return;

		int arriveSign = current.ArriveSign != 0 ? ( current.ArriveSign >= 0 ? 1 : -1 ) : 0;
		for ( int i = 0; i < junction.ridePaths.Count; i++ )
		{
			MinecartJunctionRidePath path = junction.ridePaths[ i ];
			if ( path == null || path.fromTrack != current.Track || path.toTrack == null )
				continue;

			if ( !path.toTrack.IsTravelReady )
				continue;

			if ( path.worldPoints == null || path.worldPoints.Count < 2 )
				continue;

			int intoSign = path.intoTravelSign >= 0 ? 1 : -1;
			// Only take exits that match how we arrived at this port (when known).
			if ( arriveSign != 0 && intoSign != arriveSign )
				continue;

			int outSign = path.outTravelSign >= 0 ? 1 : -1;
			bool sameTrackThrough = path.toTrack == current.Track
				&& graph.IsSamePortExit( current.Track, current.JunctionIndex, current.Distance, path.toDistance );
			float stepCost = sameTrackThrough ? 0.05f : JunctionBaseCost + Mathf.Max( 0.01f, path.length );

			TryEnqueue(
				path.toTrack,
				path.toDistance,
				current.JunctionIndex,
				outSign,
				current.Cost + stepCost,
				currentIndex,
				viaJunction: current.JunctionIndex,
				viaExitTrack: path.toTrack,
				viaExitDistance: path.toDistance,
				viaExitSign: outSign,
				viaTravelSign: intoSign,
				viaFromDistance: path.fromDistance );
		}
	}

	static void TryEnqueue(
		MinecartTrack track,
		float distance,
		int junctionIndex,
		int arriveSign,
		float cost,
		int parent,
		int viaJunction,
		MinecartTrack viaExitTrack,
		float viaExitDistance,
		int viaExitSign,
		int viaTravelSign,
		float viaFromDistance = 0f )
	{
		if ( track == null )
			return;

		NodeKey key = MakeKey( track, junctionIndex, distance, arriveSign );
		float known;
		if ( Best.TryGetValue( key, out known ) && known <= cost + 0.0001f )
			return;

		Best[ key ] = cost;
		PushNode( new SearchNode
		{
			Key = key,
			Cost = cost,
			Track = track,
			Distance = distance,
			JunctionIndex = junctionIndex,
			ArriveSign = arriveSign,
			Parent = parent,
			ViaJunction = viaJunction,
			ViaExitTrack = viaExitTrack,
			ViaExitDistance = viaExitDistance,
			ViaExitSign = viaExitSign,
			ViaTravelSign = viaTravelSign,
			ViaFromDistance = viaFromDistance
		} );
	}

	static void PushNode( SearchNode node )
	{
		int index = Nodes.Count;
		Nodes.Add( node );
		Heap.Add( index );
		int i = Heap.Count - 1;
		while ( i > 0 )
		{
			int parent = ( i - 1 ) / 2;
			if ( Nodes[ Heap[ parent ] ].Cost <= Nodes[ Heap[ i ] ].Cost )
				break;

			int tmp = Heap[ parent ];
			Heap[ parent ] = Heap[ i ];
			Heap[ i ] = tmp;
			i = parent;
		}
	}

	static int PopHeap()
	{
		int root = Heap[ 0 ];
		int last = Heap[ Heap.Count - 1 ];
		Heap.RemoveAt( Heap.Count - 1 );
		if ( Heap.Count == 0 )
			return root;

		Heap[ 0 ] = last;
		int i = 0;
		while ( true )
		{
			int left = i * 2 + 1;
			int right = left + 1;
			int smallest = i;
			if ( left < Heap.Count && Nodes[ Heap[ left ] ].Cost < Nodes[ Heap[ smallest ] ].Cost )
				smallest = left;
			if ( right < Heap.Count && Nodes[ Heap[ right ] ].Cost < Nodes[ Heap[ smallest ] ].Cost )
				smallest = right;
			if ( smallest == i )
				break;

			int tmp = Heap[ i ];
			Heap[ i ] = Heap[ smallest ];
			Heap[ smallest ] = tmp;
			i = smallest;
		}

		return root;
	}

	static bool BuildLegs(
		MinecartTrack fromTrack,
		float fromDistance,
		MinecartTrack toTrack,
		float toDistance,
		int goalIndex,
		List<RouteLeg> into )
	{
		List<SearchNode> chain = new List<SearchNode>( 24 );
		int cursor = goalIndex;
		int guard = 0;
		while ( cursor >= 0 && guard++ < 512 )
		{
			chain.Add( Nodes[ cursor ] );
			cursor = Nodes[ cursor ].Parent;
		}

		chain.Reverse();
		if ( chain.Count == 0 )
			return false;

		MinecartTrack legTrack = fromTrack;
		float legFrom = fromDistance;
		for ( int i = 1; i < chain.Count; i++ )
		{
			SearchNode node = chain[ i ];
			if ( node.ViaJunction < 0 || node.ViaExitTrack == null )
				continue;

			float approachDist = node.ViaFromDistance;
			float sep = legTrack.SignedAlong( legFrom, approachDist );
			int travelSign = Mathf.Abs( sep ) <= GoalEpsilon
				? ( node.ViaTravelSign != 0 ? node.ViaTravelSign : 1 )
				: ( sep >= 0f ? 1 : -1 );

			into.Add( new RouteLeg
			{
				Track = legTrack,
				TargetDistance = approachDist,
				TravelSign = travelSign,
				JunctionIndex = node.ViaJunction,
				ExitTrack = node.ViaExitTrack,
				ExitDistance = node.ViaExitDistance,
				ExitTravelSign = node.ViaExitSign
			} );

			legTrack = node.ViaExitTrack;
			legFrom = node.ViaExitDistance;
		}

		if ( legTrack != toTrack )
			return false;

		float finalSep = toTrack.SignedAlong( legFrom, toDistance );
		into.Add( new RouteLeg
		{
			Track = toTrack,
			TargetDistance = toDistance,
			TravelSign = Mathf.Abs( finalSep ) <= GoalEpsilon ? 1 : ( finalSep >= 0f ? 1 : -1 ),
			JunctionIndex = -1
		} );
		return true;
	}

	static NodeKey MakeKey( MinecartTrack track, int junctionIndex, float distance, int arriveSign )
	{
		return new NodeKey
		{
			TrackId = track != null ? track.GetInstanceID() : 0,
			JunctionIndex = junctionIndex,
			QuantizedDistance = Mathf.RoundToInt( distance / Quantize ),
			ArriveSign = arriveSign
		};
	}
}
