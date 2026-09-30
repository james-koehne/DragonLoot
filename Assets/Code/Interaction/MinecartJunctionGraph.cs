using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Editor-baked topology of overlapping <see cref="MinecartTrack"/> paths.
/// Carts query this at runtime for A/D branch choice near junctions.
/// Create via Dragon Loot → Minecart → Create Junction Graph.
/// Junction child meshes are shown only when every port track is ready (active + built).
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class MinecartJunctionGraph : MonoBehaviour
{
	public const string JunctionChildPrefix = "Junction_";

	static readonly List<MinecartJunctionGraph> All = new List<MinecartJunctionGraph>( 2 );

	const float DeadEndEpsilon = 0.05f;
	const float PortMatchEpsilon = 0.4f;
	// Bake cuts sit off port centers (~2.5–3.7m in current junctions). Matching must
	// absorb that offset without ignoring distances entirely (wrong-corner bug).
	const float RidePathDistanceEpsilon = 4.0f;

	static int ForceAllGapsDepth;
	static bool RefreshingVisuals;

	[Header( "Bake" )]
	[Tooltip( "Max world distance between rail samples/endpoints to form a junction." )]
	[SerializeField]
	[Min( 0.05f )]
	float detectRadius = 0.75f;

	[Tooltip( "Along-spline sample spacing used when searching for mid-path overlaps." )]
	[SerializeField]
	[Min( 0.1f )]
	float sampleStep = 0.4f;

	[Tooltip( "Merge nearby detections into one junction node." )]
	[SerializeField]
	[Min( 0.05f )]
	float clusterMergeRadius = 1.0f;

	[Tooltip( "Minimum along-track separation for a track to form a self-junction (mid-touch or end-to-mid). Filters trivial nearest-point hits." )]
	[SerializeField]
	[Min( 0.5f )]
	float minSelfSeparation = 2.0f;

	[Tooltip( "Half-length along each port cut from track visual/collider and filled by the junction mesh." )]
	[SerializeField]
	[Min( 0.05f )]
	float junctionMeshGap = 1.0f;

	[Header( "Visual Tweaks" )]
	[Tooltip( "Extra reach past the mesh gap for the shared join plane (cut + entry base)." )]
	[SerializeField]
	[Min( 0f )]
	float joinOverlap = 0.1f;

	[Tooltip( "How far junction rail ends extend past the track cut so they tuck under the last track samples and meet visually." )]
	[SerializeField]
	[Min( 0f )]
	float joinMeetSlop = 0.05f;

	[Tooltip( "Bezier control distance as a fraction of average entry radius from the junction center." )]
	[SerializeField]
	[Range( 0.1f, 1.5f )]
	float cornerControlScale = 0.85f;

	[Tooltip( "Sample rings per corner turn curve." )]
	[SerializeField]
	[Min( 4 )]
	int curveSamples = 12;

	[Tooltip( "Minimum corner angle (degrees) between junction arms to emit a turn curve. Blocks knife-edge and micro whiskers." )]
	[SerializeField]
	[Range( 20f, 90f )]
	float minCornerAngleDegrees = 35f;

	[Tooltip( "Half-width of the shaped sleeper strip under junction rails. 0 = use track sleeperSize.x * 0.5." )]
	[SerializeField]
	[Min( 0f )]
	float sleeperHalfWidth = 0f;

	[SerializeField]
	bool drawTurnCurves = true;

	[SerializeField]
	bool drawShapedSleeper = true;

	[Header( "Baked" )]
	[SerializeField]
	List<MinecartJunction> junctions = new List<MinecartJunction>( 8 );

	[SerializeField]
	List<MinecartTrackJunctionIndex> trackIndex = new List<MinecartTrackJunctionIndex>( 16 );

	[Header( "Gizmos" )]
	[SerializeField]
	bool drawGizmosAlways = true;

	[SerializeField]
	bool drawDebugGizmos = true;

	[SerializeField]
	float gizmoPortLength = 1.25f;

	[SerializeField]
	List<MinecartJunctionDebugEntry> debugEntries = new List<MinecartJunctionDebugEntry>( 8 );

	[SerializeField]
	List<MinecartJunctionDebugCorner> debugCorners = new List<MinecartJunctionDebugCorner>( 4 );

	[SerializeField]
	List<MinecartJunctionDebugCorner> debugSkippedCorners = new List<MinecartJunctionDebugCorner>( 4 );

	[SerializeField]
	List<MinecartJunctionDebugPolyline> debugThroughs = new List<MinecartJunctionDebugPolyline>( 4 );

	[SerializeField]
	List<MinecartJunctionDebugPolyline> debugTurns = new List<MinecartJunctionDebugPolyline>( 4 );

	bool[] _junctionVisualActive;
	bool _subscribedBuildEvents;
	bool _hasAppliedVisuals;
	bool _refreshQueued;

	public static IReadOnlyList<MinecartJunctionGraph> ActiveGraphs => All;

	public static bool ForceAllGapsActive => ForceAllGapsDepth > 0;

	public static void BeginForceAllGaps()
	{
		ForceAllGapsDepth++;
	}

	public static void EndForceAllGaps()
	{
		if ( ForceAllGapsDepth > 0 )
			ForceAllGapsDepth--;
	}

	public float DetectRadius => Mathf.Max( 0.05f, detectRadius );

	public float SampleStep => Mathf.Max( 0.1f, sampleStep );

	public float ClusterMergeRadius => Mathf.Max( 0.05f, clusterMergeRadius );

	public float MinSelfSeparation => Mathf.Max( 0.5f, minSelfSeparation );

	public float JunctionMeshGap => Mathf.Max( 0.05f, junctionMeshGap );

	public float JoinOverlap => Mathf.Max( 0f, joinOverlap );

	public float JoinMeetSlop => Mathf.Max( 0f, joinMeetSlop );

	/// <summary>Shared cut / join plane distance from each port along the track.</summary>
	public float JunctionJoinRadius => JunctionMeshGap + JoinOverlap;

	/// <summary>Track visual/collider cut — same as join plane so rails can meet.</summary>
	public float JunctionTrackCutRadius => JunctionJoinRadius;

	/// <summary>Where junction entries/through ends sit: slightly past the cut to tuck under track samples.</summary>
	public float JunctionEntryOffset => JunctionJoinRadius + JoinMeetSlop;

	public float CornerControlScale => Mathf.Clamp( cornerControlScale, 0.1f, 1.5f );

	public int CurveSamples => Mathf.Max( 4, curveSamples );

	public float MinCornerAngleDegrees => Mathf.Clamp( minCornerAngleDegrees, 20f, 90f );

	public float SleeperHalfWidthOverride => Mathf.Max( 0f, sleeperHalfWidth );

	public bool DrawTurnCurves => drawTurnCurves;

	public bool DrawShapedSleeper => drawShapedSleeper;

	public bool DrawDebugGizmos => drawDebugGizmos;

	public IReadOnlyList<MinecartJunction> Junctions => junctions;

	public int JunctionCount => junctions != null ? junctions.Count : 0;

	public static MinecartJunctionGraph FindActive()
	{
		EnsureRegistryPopulated();
		for ( int i = 0; i < All.Count; i++ )
		{
			MinecartJunctionGraph graph = All[ i ];
			if ( graph != null && graph.isActiveAndEnabled )
				return graph;
		}

		return null;
	}

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );

		SubscribeBuildEvents();
		if ( !Application.isPlaying )
			RefreshBuildVisuals();
	}

	void Start()
	{
		QueueOrRefreshBuildVisuals();
	}

	void LateUpdate()
	{
		if ( !_refreshQueued )
			return;

		_refreshQueued = false;
		RefreshBuildVisuals();
	}

	void OnDisable()
	{
		All.Remove( this );
		UnsubscribeBuildEvents();
	}

	void SubscribeBuildEvents()
	{
		if ( _subscribedBuildEvents )
			return;

		EventBus.Subscribe<BuildableCompletedEvent>( OnBuildableCompleted );
		_subscribedBuildEvents = true;
	}

	void UnsubscribeBuildEvents()
	{
		if ( !_subscribedBuildEvents )
			return;

		EventBus.Unsubscribe<BuildableCompletedEvent>( OnBuildableCompleted );
		_subscribedBuildEvents = false;
	}

	void OnBuildableCompleted( BuildableCompletedEvent evt )
	{
		if ( evt.Buildable != null )
		{
			MinecartTrack[] tracks = evt.Buildable.GetComponentsInChildren<MinecartTrack>( true );
			for ( int i = 0; i < tracks.Length; i++ )
			{
				MinecartTrack track = tracks[ i ];
				if ( track != null )
					track.RebuildWalkCollider();
			}
		}

		QueueOrRefreshBuildVisuals();
	}

	/// <summary>
	/// Called when a track is enabled/disabled so Scene/play preview can update junction visuals.
	/// </summary>
	public static void NotifyTrackActiveChanged( MinecartTrack track )
	{
		if ( RefreshingVisuals || ForceAllGapsActive )
			return;

		EnsureRegistryPopulated();
		for ( int i = 0; i < All.Count; i++ )
		{
			MinecartJunctionGraph graph = All[ i ];
			if ( graph == null || !graph.isActiveAndEnabled )
				continue;

			if ( track != null && !graph.TrackTouchesAnyJunction( track ) )
				continue;

			graph.QueueOrRefreshBuildVisuals();
		}
	}

	void QueueOrRefreshBuildVisuals()
	{
		if ( RefreshingVisuals || ForceAllGapsActive )
			return;

		// Edit mode: refresh immediately so Hierarchy toggles update Scene view now.
		if ( !Application.isPlaying )
		{
			RefreshBuildVisuals();
			return;
		}

		_refreshQueued = true;
	}

	void OnValidate()
	{
		detectRadius = Mathf.Max( 0.05f, detectRadius );
		sampleStep = Mathf.Max( 0.1f, sampleStep );
		clusterMergeRadius = Mathf.Max( 0.05f, clusterMergeRadius );
		minSelfSeparation = Mathf.Max( 0.5f, minSelfSeparation );
		junctionMeshGap = Mathf.Max( 0.05f, junctionMeshGap );
		joinOverlap = Mathf.Max( 0f, joinOverlap );
		joinMeetSlop = Mathf.Max( 0f, joinMeetSlop );
		cornerControlScale = Mathf.Clamp( cornerControlScale, 0.1f, 1.5f );
		curveSamples = Mathf.Max( 4, curveSamples );
		minCornerAngleDegrees = Mathf.Clamp( minCornerAngleDegrees, 20f, 90f );
		sleeperHalfWidth = Mathf.Max( 0f, sleeperHalfWidth );
		gizmoPortLength = Mathf.Max( 0.1f, gizmoPortLength );
	}

	static void EnsureRegistryPopulated()
	{
		if ( All.Count > 0 )
			return;

		MinecartJunctionGraph[] found = FindObjectsByType<MinecartJunctionGraph>( FindObjectsInactive.Exclude, FindObjectsSortMode.None );
		for ( int i = 0; i < found.Length; i++ )
		{
			MinecartJunctionGraph candidate = found[ i ];
			if ( candidate != null && !All.Contains( candidate ) )
				All.Add( candidate );
		}
	}

	public MinecartJunction GetJunction( int index )
	{
		if ( junctions == null || index < 0 || index >= junctions.Count )
			return null;

		return junctions[ index ];
	}

	/// <summary>
	/// Finds the nearest junction on <paramref name="track"/> whose along-track distance
	/// is within <paramref name="approachRadius"/> of <paramref name="distanceAlongTrack"/>.
	/// </summary>
	public bool TryFindApproachJunction( MinecartTrack track, float distanceAlongTrack, float approachRadius, out int junctionIndex, out float junctionDistance )
	{
		junctionIndex = -1;
		junctionDistance = 0f;
		if ( track == null || !track.IsTravelReady || approachRadius < 0f )
			return false;

		if ( trackIndex == null || trackIndex.Count == 0 )
			return false;

		float bestAbs = approachRadius;
		bool found = false;
		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			float sep = Mathf.Abs( track.SignedAlong( distanceAlongTrack, entry.distance ) );
			if ( sep > bestAbs )
				continue;

			bestAbs = sep;
			junctionIndex = entry.junctionIndex;
			junctionDistance = entry.distance;
			found = true;
		}

		return found;
	}

	/// <summary>
	/// True when the cart has left the approach zone of a previously committed junction.
	/// </summary>
	public bool IsOutsideApproach( MinecartTrack track, float distanceAlongTrack, int junctionIndex, float approachRadius )
	{
		if ( track == null || junctionIndex < 0 || approachRadius < 0f )
			return true;

		float junctionDistance;
		if ( !TryGetPortDistance( track, junctionIndex, distanceAlongTrack, out junctionDistance ) )
			return true;

		float sep = Mathf.Abs( track.SignedAlong( distanceAlongTrack, junctionDistance ) );
		return sep > approachRadius;
	}

	public bool TryGetPortDistance( MinecartTrack track, int junctionIndex, out float distance )
	{
		return TryGetPortDistance( track, junctionIndex, float.NaN, out distance );
	}

	/// <summary>
	/// Nearest port on <paramref name="track"/> at this junction to <paramref name="distanceAlongTrack"/>.
	/// When distanceAlongTrack is NaN, returns the first matching port (legacy).
	/// </summary>
	public bool TryGetPortDistance( MinecartTrack track, int junctionIndex, float distanceAlongTrack, out float distance )
	{
		distance = 0f;
		MinecartJunctionPort port;
		if ( !TryGetPort( track, junctionIndex, distanceAlongTrack, out port ) )
			return false;

		distance = port.distance;
		return true;
	}

	public bool TryGetPort( MinecartTrack track, int junctionIndex, out MinecartJunctionPort port )
	{
		return TryGetPort( track, junctionIndex, float.NaN, out port );
	}

	public bool TryGetPort( MinecartTrack track, int junctionIndex, float distanceAlongTrack, out MinecartJunctionPort port )
	{
		port = default;
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ports == null || track == null )
			return false;

		bool useNearest = !float.IsNaN( distanceAlongTrack );
		float bestAbs = float.MaxValue;
		bool found = false;
		for ( int i = 0; i < junction.ports.Count; i++ )
		{
			MinecartJunctionPort candidate = junction.ports[ i ];
			if ( candidate.track != track )
				continue;

			if ( !useNearest )
			{
				port = candidate;
				return true;
			}

			float sep = Mathf.Abs( track.SignedAlong( distanceAlongTrack, candidate.distance ) );
			if ( sep >= bestAbs )
				continue;

			bestAbs = sep;
			port = candidate;
			found = true;
		}

		return found;
	}

	/// <summary>
	/// Builds valid leave-options at a junction, excluding reverse of current travel on the arrival track.
	/// Cross-track and same-track other-port exits require a baked ride path.
	/// Same-track continue (same port) requires an enabled through option when one exists.
	/// </summary>
	public int BuildExits( int junctionIndex, MinecartTrack arrivalTrack, int travelSign, List<MinecartJunctionExit> results )
	{
		return BuildExits( junctionIndex, arrivalTrack, float.NaN, travelSign, results );
	}

	public int BuildExits( int junctionIndex, MinecartTrack arrivalTrack, float arrivalDistanceAlong, int travelSign, List<MinecartJunctionExit> results )
	{
		if ( results == null )
			return 0;

		results.Clear();
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ports == null )
			return 0;

		int sign = travelSign >= 0 ? 1 : -1;
		float arrivalPortDistance = arrivalDistanceAlong;
		MinecartJunctionPort arrivalPort;
		if ( TryGetPort( arrivalTrack, junctionIndex, arrivalDistanceAlong, out arrivalPort ) )
			arrivalPortDistance = arrivalPort.distance;

		for ( int i = 0; i < junction.ports.Count; i++ )
		{
			MinecartJunctionPort port = junction.ports[ i ];
			if ( port.track == null || !port.track.IsTravelReady )
				continue;

			TryAddExit( this, junctionIndex, results, port, 1, arrivalTrack, arrivalPortDistance, sign );
			TryAddExit( this, junctionIndex, results, port, -1, arrivalTrack, arrivalPortDistance, sign );
		}

		return results.Count;
	}

	static void TryAddExit( MinecartJunctionGraph graph, int junctionIndex, List<MinecartJunctionExit> results, MinecartJunctionPort port, int exitSign, MinecartTrack arrivalTrack, float arrivalPortDistance, int arrivalTravelSign )
	{
		if ( port.track == arrivalTrack && exitSign == -arrivalTravelSign )
			return;

		if ( !IsValidExitDirection( port, exitSign ) )
			return;

		Vector3 tangent = port.tangent;
		if ( tangent.sqrMagnitude < 0.0001f )
			return;

		bool sameTrack = port.track == arrivalTrack;
		bool samePort = sameTrack
			&& arrivalTrack != null
			&& Mathf.Abs( arrivalTrack.SignedAlong( arrivalPortDistance, port.distance ) ) <= PortMatchEpsilon;

		if ( sameTrack && samePort )
		{
			if ( !graph.IsSameTrackThroughAllowed( junctionIndex, arrivalTrack ) )
				return;
		}
		else
		{
			MinecartJunctionRidePath path;
			if ( !graph.TryFindRidePath( junctionIndex, arrivalTrack, arrivalPortDistance, arrivalTravelSign, port.track, port.distance, exitSign, out path ) )
				return;
		}

		results.Add( new MinecartJunctionExit
		{
			track = port.track,
			distance = port.distance,
			travelSign = exitSign,
			worldTangent = tangent * exitSign
		} );
	}

	/// <summary>
	/// Same-track continue is allowed when no through option exists for the track, or the matching through option is enabled.
	/// </summary>
	public bool IsSameTrackThroughAllowed( int junctionIndex, MinecartTrack track )
	{
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || track == null )
			return false;

		if ( junction.throughOptions == null || junction.throughOptions.Count == 0 )
			return true;

		bool foundOption = false;
		for ( int i = 0; i < junction.throughOptions.Count; i++ )
		{
			MinecartJunctionCornerOption option = junction.throughOptions[ i ];
			if ( option == null )
				continue;

			if ( option.trackA != track && option.trackB != track )
				continue;

			// Same-track through uses trackA == trackB == track.
			if ( option.trackA != null && option.trackB != null && option.trackA != option.trackB )
				continue;

			foundOption = true;
			if ( option.enabled )
				return true;
		}

		// No same-track through option authored for this track — continue is open (stub arms, etc.).
		if ( !foundOption )
			return true;

		return false;
	}

	public static bool IsValidExitDirection( MinecartJunctionPort port, int exitSign )
	{
		MinecartTrack track = port.track;
		if ( track == null || !track.IsTravelReady )
			return false;

		if ( track.IsClosed )
			return true;

		float length = track.Length;
		if ( exitSign > 0 && port.distance >= length - DeadEndEpsilon )
			return false;

		if ( exitSign < 0 && port.distance <= DeadEndEpsilon )
			return false;

		return true;
	}

	public static MinecartJunctionKind ResolveKind( MinecartJunctionKind preferred, List<MinecartJunctionPort> ports )
	{
		if ( preferred == MinecartJunctionKind.Cross || preferred == MinecartJunctionKind.Tee )
			return preferred;

		if ( ports == null || ports.Count == 0 )
			return MinecartJunctionKind.Cross;

		int stubs = 0;
		for ( int i = 0; i < ports.Count; i++ )
		{
			MinecartJunctionPort port = ports[ i ];
			bool canPos = IsValidExitDirection( port, 1 );
			bool canNeg = IsValidExitDirection( port, -1 );
			if ( canPos && canNeg )
				continue;

			if ( canPos || canNeg )
				stubs++;
		}

		return stubs > 0 ? MinecartJunctionKind.Tee : MinecartJunctionKind.Cross;
	}

	public bool TryFindRidePath( int junctionIndex, MinecartTrack fromTrack, int intoTravelSign, MinecartTrack toTrack, int outTravelSign, out MinecartJunctionRidePath path )
	{
		return TryFindRidePath( junctionIndex, fromTrack, float.NaN, intoTravelSign, toTrack, float.NaN, outTravelSign, out path );
	}

	public bool TryFindRidePath( int junctionIndex, MinecartTrack fromTrack, float fromDistance, int intoTravelSign, MinecartTrack toTrack, float toDistance, int outTravelSign, out MinecartJunctionRidePath path )
	{
		path = null;
		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ridePaths == null || fromTrack == null || toTrack == null )
			return false;

		int intoSign = intoTravelSign >= 0 ? 1 : -1;
		int outSign = outTravelSign >= 0 ? 1 : -1;
		bool matchFromDist = !float.IsNaN( fromDistance );
		bool matchToDist = !float.IsNaN( toDistance );

		// Tier 0: exact track + signs + distances.
		if ( TryPickRidePath( junction, fromTrack, toTrack, intoSign, outSign, fromDistance, toDistance, matchFromDist, matchToDist, requireInto: true, requireOut: true, out path ) )
			return true;

		// When callers pass port distances, never ignore them. Multi-port junctions (and
		// duplicate track refs) otherwise match the wrong corner — entry behind the cart.
		if ( matchFromDist || matchToDist )
			return false;

		// Legacy NaN distance lookups (network pathfinder, etc.): soft sign tiers.
		if ( TryPickRidePath( junction, fromTrack, toTrack, intoSign, outSign, fromDistance, toDistance, matchFromDist: false, matchToDist: false, requireInto: true, requireOut: true, out path ) )
			return true;

		if ( TryPickRidePath( junction, fromTrack, toTrack, intoSign, outSign, fromDistance, toDistance, matchFromDist: false, matchToDist: false, requireInto: true, requireOut: false, out path ) )
			return true;

		if ( TryPickRidePath( junction, fromTrack, toTrack, intoSign, outSign, fromDistance, toDistance, matchFromDist: false, matchToDist: false, requireInto: false, requireOut: false, out path ) )
			return true;

		return false;
	}

	static bool TryPickRidePath(
		MinecartJunction junction,
		MinecartTrack fromTrack,
		MinecartTrack toTrack,
		int intoSign,
		int outSign,
		float fromDistance,
		float toDistance,
		bool matchFromDist,
		bool matchToDist,
		bool requireInto,
		bool requireOut,
		out MinecartJunctionRidePath path )
	{
		path = null;
		MinecartJunctionRidePath best = null;
		float bestScore = float.MaxValue;

		for ( int i = 0; i < junction.ridePaths.Count; i++ )
		{
			MinecartJunctionRidePath candidate = junction.ridePaths[ i ];
			if ( candidate == null || candidate.fromTrack != fromTrack || candidate.toTrack != toTrack )
				continue;

			if ( candidate.worldPoints == null || candidate.worldPoints.Count < 2 )
				continue;

			int candInto = candidate.intoTravelSign >= 0 ? 1 : -1;
			int candOut = candidate.outTravelSign >= 0 ? 1 : -1;
			if ( requireInto && candInto != intoSign )
				continue;
			if ( requireOut && candOut != outSign )
				continue;

			if ( matchFromDist && Mathf.Abs( fromTrack.SignedAlong( fromDistance, candidate.fromDistance ) ) > RidePathDistanceEpsilon )
				continue;

			if ( matchToDist && Mathf.Abs( toTrack.SignedAlong( toDistance, candidate.toDistance ) ) > RidePathDistanceEpsilon )
				continue;

			float score = 0f;
			if ( candInto != intoSign )
				score += 10f;
			if ( candOut != outSign )
				score += 5f;
			if ( !float.IsNaN( fromDistance ) )
				score += Mathf.Abs( fromTrack.SignedAlong( fromDistance, candidate.fromDistance ) );
			if ( !float.IsNaN( toDistance ) )
				score += Mathf.Abs( toTrack.SignedAlong( toDistance, candidate.toDistance ) );

			if ( score >= bestScore )
				continue;

			bestScore = score;
			best = candidate;
		}

		path = best;
		return best != null;
	}

	/// <summary>True when exit is the same port as arrival (spline through), not a same-track branch to another port.</summary>
	public bool IsSamePortExit( MinecartTrack track, int junctionIndex, float arrivalDistanceAlong, float exitDistance )
	{
		if ( track == null )
			return false;

		float arrivalPort;
		if ( !TryGetPortDistance( track, junctionIndex, arrivalDistanceAlong, out arrivalPort ) )
			return Mathf.Abs( track.SignedAlong( arrivalDistanceAlong, exitDistance ) ) <= PortMatchEpsilon;

		return Mathf.Abs( track.SignedAlong( arrivalPort, exitDistance ) ) <= PortMatchEpsilon;
	}

	public void EditorApplyBake( List<MinecartJunction> bakedJunctions, List<MinecartTrackJunctionIndex> bakedIndex )
	{
		junctions = bakedJunctions != null ? bakedJunctions : new List<MinecartJunction>( 0 );
		trackIndex = bakedIndex != null ? bakedIndex : new List<MinecartTrackJunctionIndex>( 0 );
	}

	public MinecartJunctionKind EditorFindPreservedKind( Vector3 worldPosition )
	{
		MinecartJunction preserved = EditorFindPreservedJunction( worldPosition );
		return preserved != null ? preserved.kind : MinecartJunctionKind.Auto;
	}

	public MinecartJunction EditorFindPreservedJunction( Vector3 worldPosition )
	{
		if ( junctions == null )
			return null;

		float merge = ClusterMergeRadius;
		float best = merge * merge;
		MinecartJunction bestJunction = null;
		for ( int i = 0; i < junctions.Count; i++ )
		{
			MinecartJunction junction = junctions[ i ];
			if ( junction == null )
				continue;

			float sqr = ( junction.worldPosition - worldPosition ).sqrMagnitude;
			if ( sqr > best )
				continue;

			best = sqr;
			bestJunction = junction;
		}

		return bestJunction;
	}

	public static List<MinecartJunctionCornerOption> CloneCornerOptions( List<MinecartJunctionCornerOption> source )
	{
		if ( source == null || source.Count == 0 )
			return new List<MinecartJunctionCornerOption>( 4 );

		List<MinecartJunctionCornerOption> clone = new List<MinecartJunctionCornerOption>( source.Count );
		for ( int i = 0; i < source.Count; i++ )
		{
			MinecartJunctionCornerOption o = source[ i ];
			if ( o == null )
				continue;

			clone.Add( new MinecartJunctionCornerOption
			{
				label = o.label,
				trackA = o.trackA,
				trackB = o.trackB,
				angleA = o.angleA,
				angleB = o.angleB,
				cornerAngleDegrees = o.cornerAngleDegrees,
				enabled = o.enabled
			} );
		}

		return clone;
	}

	public static List<MinecartJunctionCornerOption> CloneThroughOptions( List<MinecartJunctionCornerOption> source )
	{
		return CloneCornerOptions( source );
	}

	public void EditorClearDebugBake()
	{
		if ( debugEntries == null )
			debugEntries = new List<MinecartJunctionDebugEntry>( 8 );
		else
			debugEntries.Clear();

		if ( debugCorners == null )
			debugCorners = new List<MinecartJunctionDebugCorner>( 4 );
		else
			debugCorners.Clear();

		if ( debugSkippedCorners == null )
			debugSkippedCorners = new List<MinecartJunctionDebugCorner>( 4 );
		else
			debugSkippedCorners.Clear();

		if ( debugThroughs == null )
			debugThroughs = new List<MinecartJunctionDebugPolyline>( 4 );
		else
			debugThroughs.Clear();

		if ( debugTurns == null )
			debugTurns = new List<MinecartJunctionDebugPolyline>( 4 );
		else
			debugTurns.Clear();
	}

	public void EditorAddDebugEntry( Vector3 worldPos, Vector3 inwardTangent, Color color )
	{
		if ( debugEntries == null )
			debugEntries = new List<MinecartJunctionDebugEntry>( 8 );

		debugEntries.Add( new MinecartJunctionDebugEntry
		{
			worldPos = worldPos,
			inwardTangent = inwardTangent,
			color = color
		} );
	}

	public void EditorAddDebugCorner( Vector3 start, Vector3 control, Vector3 end )
	{
		if ( debugCorners == null )
			debugCorners = new List<MinecartJunctionDebugCorner>( 4 );

		debugCorners.Add( new MinecartJunctionDebugCorner
		{
			start = start,
			control = control,
			end = end
		} );
	}

	public void EditorAddDebugSkippedCorner( Vector3 start, Vector3 control, Vector3 end )
	{
		if ( debugSkippedCorners == null )
			debugSkippedCorners = new List<MinecartJunctionDebugCorner>( 4 );

		debugSkippedCorners.Add( new MinecartJunctionDebugCorner
		{
			start = start,
			control = control,
			end = end
		} );
	}

	public void EditorAddDebugPolyline( bool turn, List<Vector3> worldPoints )
	{
		if ( worldPoints == null || worldPoints.Count < 2 )
			return;

		MinecartJunctionDebugPolyline line = new MinecartJunctionDebugPolyline
		{
			points = new List<Vector3>( worldPoints )
		};
		if ( turn )
		{
			if ( debugTurns == null )
				debugTurns = new List<MinecartJunctionDebugPolyline>( 4 );
			debugTurns.Add( line );
		}
		else
		{
			if ( debugThroughs == null )
				debugThroughs = new List<MinecartJunctionDebugPolyline>( 4 );
			debugThroughs.Add( line );
		}
	}

	public void CollectGapDistances( MinecartTrack track, List<float> distances )
	{
		if ( distances == null )
			return;

		distances.Clear();
		if ( track == null || trackIndex == null )
			return;

		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			distances.Add( entry.distance );
		}
	}

	/// <summary>
	/// True when every port at the junction is ready: track active+usable and buildable built (or no buildable).
	/// </summary>
	public bool IsJunctionVisuallyComplete( int junctionIndex )
	{
		if ( ForceAllGapsActive )
			return true;

		MinecartJunction junction = GetJunction( junctionIndex );
		if ( junction == null || junction.ports == null || junction.ports.Count < 2 )
			return false;

		for ( int i = 0; i < junction.ports.Count; i++ )
		{
			if ( !IsPortReady( junction.ports[ i ] ) )
				return false;
		}

		return true;
	}

	public static bool IsPortReady( MinecartJunctionPort port )
	{
		return port.track != null && port.track.IsTravelReady;
	}

	bool IsJunctionGapActive( int junctionIndex )
	{
		if ( ForceAllGapsActive )
			return true;

		if ( _junctionVisualActive != null
			&& junctionIndex >= 0
			&& junctionIndex < _junctionVisualActive.Length )
			return _junctionVisualActive[ junctionIndex ];

		return IsJunctionVisuallyComplete( junctionIndex );
	}

	bool TrackTouchesAnyJunction( MinecartTrack track )
	{
		if ( track == null || trackIndex == null )
			return false;

		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			if ( trackIndex[ i ].track == track )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Updates Junction_* visibility and remeshes affected tracks from the live gap mask.
	/// Safe in edit mode (ExecuteAlways) and play mode.
	/// </summary>
	public void RefreshBuildVisuals()
	{
		if ( RefreshingVisuals || ForceAllGapsActive )
			return;

		RefreshingVisuals = true;
		try
		{
			RefreshBuildVisualsInternal();
		}
		finally
		{
			RefreshingVisuals = false;
		}

#if UNITY_EDITOR
		if ( !Application.isPlaying )
		{
			UnityEditor.EditorUtility.SetDirty( this );
			UnityEditor.SceneView.RepaintAll();
		}
#endif
	}

	void RefreshBuildVisualsInternal()
	{
		int count = JunctionCount;
		if ( _junctionVisualActive == null || _junctionVisualActive.Length != count )
			_junctionVisualActive = new bool[ count ];

		for ( int i = 0; i < count; i++ )
		{
			bool complete = IsJunctionVisuallyComplete( i );
			_junctionVisualActive[ i ] = complete;

			Transform child = transform.Find( JunctionChildPrefix + i );
			if ( child != null && child.gameObject.activeSelf != complete )
				child.gameObject.SetActive( complete );
		}

		_hasAppliedVisuals = true;

		List<MinecartTrack> affected = new List<MinecartTrack>( 8 );
		CollectAllIndexedTracks( affected );
		for ( int i = 0; i < affected.Count; i++ )
			ApplyTrackVisualForCurrentGaps( affected[ i ] );
	}

	void CollectAllIndexedTracks( List<MinecartTrack> into )
	{
		if ( into == null || trackIndex == null )
			return;

		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrack track = trackIndex[ i ].track;
			if ( track == null || !track.IsUsable || !track.gameObject.activeInHierarchy || !track.enabled )
				continue;

			if ( !into.Contains( track ) )
				into.Add( track );
		}
	}

	void ApplyTrackVisualForCurrentGaps( MinecartTrack track )
	{
		if ( track == null || !track.IsUsable )
			return;

		if ( TrackHasOnlyCompleteJunctions( track ) && track.BakedMesh != null )
			MinecartTrackMeshRuntime.RestoreBakedVisual( track );
		else
			MinecartTrackMeshRuntime.RebuildInstanceVisual( track );
	}

	bool TrackHasOnlyCompleteJunctions( MinecartTrack track )
	{
		if ( track == null || trackIndex == null )
			return true;

		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			if ( !IsJunctionGapActive( entry.junctionIndex ) )
				return false;
		}

		return true;
	}

	/// <summary>Ports on <paramref name="track"/> with their junction indices (parallel lists).</summary>
	public void CollectPortsOnTrack( MinecartTrack track, List<float> distances, List<int> junctionIndices )
	{
		if ( distances == null || junctionIndices == null )
			return;

		distances.Clear();
		junctionIndices.Clear();
		if ( track == null || trackIndex == null )
			return;

		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			distances.Add( entry.distance );
			junctionIndices.Add( entry.junctionIndex );
		}
	}

	public bool IsDistanceInMeshGap( MinecartTrack track, float distance )
	{
		if ( track == null || trackIndex == null || trackIndex.Count == 0 )
			return false;

		float gap = JunctionTrackCutRadius;
		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			if ( !IsJunctionGapActive( entry.junctionIndex ) )
				continue;

			if ( Mathf.Abs( track.SignedAlong( distance, entry.distance ) ) <= gap )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Exact along-track distances where the track visual should end at each junction cut.
	/// Used so rail meshes terminate on the shared join plane instead of the previous sample.
	/// </summary>
	public void CollectTrackMeshCutDistances( MinecartTrack track, List<float> into )
	{
		if ( into == null || track == null || trackIndex == null )
			return;

		float gap = JunctionTrackCutRadius;
		for ( int i = 0; i < trackIndex.Count; i++ )
		{
			MinecartTrackJunctionIndex entry = trackIndex[ i ];
			if ( entry.track != track )
				continue;

			if ( !IsJunctionGapActive( entry.junctionIndex ) )
				continue;

			AddClampedCutDistance( track, entry.distance - gap, into );
			AddClampedCutDistance( track, entry.distance + gap, into );
		}
	}

	static void AddClampedCutDistance( MinecartTrack track, float distance, List<float> into )
	{
		float wrapped = track.WrapDistance( distance );
		if ( !track.IsClosed )
		{
			if ( distance < 0f )
				wrapped = 0f;
			else if ( distance > track.Length )
				wrapped = track.Length;
		}

		const float eps = 0.001f;
		for ( int i = 0; i < into.Count; i++ )
		{
			if ( Mathf.Abs( track.SignedAlong( into[ i ], wrapped ) ) <= eps )
				return;
		}

		into.Add( wrapped );
	}

	public static bool IsTrackDistanceInMeshGap( MinecartTrack track, float distance )
	{
		MinecartJunctionGraph graph = FindActive();
		if ( graph == null )
			return false;

		return graph.IsDistanceInMeshGap( track, distance );
	}

	public static void CollectTrackMeshCutDistancesFor( MinecartTrack track, List<float> into )
	{
		MinecartJunctionGraph graph = FindActive();
		if ( graph == null || into == null )
			return;

		graph.CollectTrackMeshCutDistances( track, into );
	}

	void OnDrawGizmos()
	{
		if ( drawGizmosAlways )
			DrawJunctionGizmos();
		if ( drawDebugGizmos && drawGizmosAlways )
			DrawDebugBakeGizmos();
	}

	void OnDrawGizmosSelected()
	{
		if ( !drawGizmosAlways )
			DrawJunctionGizmos();
		if ( drawDebugGizmos )
			DrawDebugBakeGizmos();
	}

	void DrawJunctionGizmos()
	{
		if ( junctions == null )
			return;

		float len = Mathf.Max( 0.1f, gizmoPortLength );
		for ( int i = 0; i < junctions.Count; i++ )
		{
			MinecartJunction junction = junctions[ i ];
			if ( junction == null )
				continue;

			Gizmos.color = new Color( 1f, 0.55f, 0.1f, 0.9f );
			Gizmos.DrawWireSphere( junction.worldPosition, 0.35f );
			Gizmos.DrawSphere( junction.worldPosition, 0.12f );

			if ( junction.ports == null )
				continue;

			for ( int p = 0; p < junction.ports.Count; p++ )
			{
				MinecartJunctionPort port = junction.ports[ p ];
				Vector3 tan = port.tangent;
				if ( tan.sqrMagnitude < 0.0001f )
					continue;

				tan.Normalize();
				Gizmos.color = new Color( 0.2f, 0.85f, 1f, 0.95f );
				Gizmos.DrawLine( junction.worldPosition, junction.worldPosition + tan * len );
				Gizmos.color = new Color( 1f, 0.3f, 0.55f, 0.85f );
				Gizmos.DrawLine( junction.worldPosition, junction.worldPosition - tan * len );
			}
		}
	}

	void DrawDebugBakeGizmos()
	{
		if ( debugEntries != null )
		{
			for ( int i = 0; i < debugEntries.Count; i++ )
			{
				MinecartJunctionDebugEntry entry = debugEntries[ i ];
				Gizmos.color = entry.color.a > 0.01f ? entry.color : Color.yellow;
				Gizmos.DrawSphere( entry.worldPos, 0.12f );
				Vector3 inward = entry.inwardTangent;
				if ( inward.sqrMagnitude > 0.0001f )
					Gizmos.DrawRay( entry.worldPos, inward.normalized * 0.6f );
			}
		}

		if ( debugCorners != null )
		{
			Gizmos.color = new Color( 1f, 0.9f, 0.1f, 1f );
			for ( int i = 0; i < debugCorners.Count; i++ )
			{
				MinecartJunctionDebugCorner corner = debugCorners[ i ];
				Gizmos.DrawLine( corner.start, corner.control );
				Gizmos.DrawLine( corner.control, corner.end );
				Gizmos.DrawWireSphere( corner.control, 0.1f );
				Gizmos.DrawWireSphere( corner.start, 0.08f );
				Gizmos.DrawWireSphere( corner.end, 0.08f );
			}
		}

		if ( debugSkippedCorners != null )
		{
			Gizmos.color = new Color( 1f, 0.25f, 0.15f, 0.85f );
			for ( int i = 0; i < debugSkippedCorners.Count; i++ )
			{
				MinecartJunctionDebugCorner corner = debugSkippedCorners[ i ];
				Gizmos.DrawLine( corner.start, corner.end );
				Gizmos.DrawWireSphere( corner.start, 0.06f );
				Gizmos.DrawWireSphere( corner.end, 0.06f );
			}
		}

		DrawDebugPolylines( debugThroughs, new Color( 0.2f, 1f, 0.4f, 1f ) );
		DrawDebugPolylines( debugTurns, new Color( 1f, 0.35f, 0.9f, 1f ) );
	}

	static void DrawDebugPolylines( List<MinecartJunctionDebugPolyline> lines, Color color )
	{
		if ( lines == null )
			return;

		Gizmos.color = color;
		for ( int i = 0; i < lines.Count; i++ )
		{
			MinecartJunctionDebugPolyline line = lines[ i ];
			if ( line == null || line.points == null || line.points.Count < 2 )
				continue;

			for ( int p = 0; p < line.points.Count - 1; p++ )
				Gizmos.DrawLine( line.points[ p ], line.points[ p + 1 ] );
		}
	}
}

public enum MinecartJunctionKind
{
	Auto = 0,
	Cross = 1,
	Tee = 2
}

[Serializable]
public class MinecartJunction
{
	public Vector3 worldPosition;

	[Tooltip( "Auto detects Cross (X) vs Tee (T) from ports. Override to force." )]
	public MinecartJunctionKind kind = MinecartJunctionKind.Auto;

	[Tooltip( "Resolved kind after bake (Auto → Cross or Tee)." )]
	public MinecartJunctionKind resolvedKind = MinecartJunctionKind.Cross;

	public List<MinecartJunctionPort> ports = new List<MinecartJunctionPort>( 4 );

	[Tooltip( "Candidate turn curves at this junction. Toggle Enabled to choose which corners mesh." )]
	public List<MinecartJunctionCornerOption> cornerOptions = new List<MinecartJunctionCornerOption>( 4 );

	[Tooltip( "Straight through fills (continuous track or opposite stubs). Toggle Enabled per path." )]
	public List<MinecartJunctionCornerOption> throughOptions = new List<MinecartJunctionCornerOption>( 4 );

	public List<MinecartJunctionRidePath> ridePaths = new List<MinecartJunctionRidePath>( 8 );
}

[Serializable]
public class MinecartJunctionCornerOption
{
	public string label = "Corner";
	public MinecartTrack trackA;
	public MinecartTrack trackB;
	public float angleA;
	public float angleB;
	public float cornerAngleDegrees;
	public bool enabled = true;
}

[Serializable]
public class MinecartJunctionRidePath
{
	public MinecartTrack fromTrack;
	public float fromDistance;
	public int intoTravelSign;
	public MinecartTrack toTrack;
	public float toDistance;
	public int outTravelSign;
	public List<Vector3> worldPoints = new List<Vector3>( 12 );
	public float length;

	public bool TryEvaluate( float distanceAlong, out Vector3 worldPos, out Vector3 worldTangent )
	{
		worldPos = Vector3.zero;
		worldTangent = Vector3.forward;
		if ( worldPoints == null || worldPoints.Count < 2 )
			return false;

		float target = Mathf.Clamp( distanceAlong, 0f, Mathf.Max( 0.0001f, length ) );
		float traveled = 0f;
		for ( int i = 0; i < worldPoints.Count - 1; i++ )
		{
			Vector3 a = worldPoints[ i ];
			Vector3 b = worldPoints[ i + 1 ];
			float seg = Vector3.Distance( a, b );
			if ( seg < 0.00001f )
				continue;

			if ( traveled + seg >= target - 0.00001f )
			{
				float t = Mathf.Clamp01( ( target - traveled ) / seg );
				worldPos = Vector3.Lerp( a, b, t );
				Vector3 tan = b - a;
				if ( tan.sqrMagnitude < 0.0001f )
					tan = Vector3.forward;
				else
					tan.Normalize();

				worldTangent = tan;
				return true;
			}

			traveled += seg;
		}

		worldPos = worldPoints[ worldPoints.Count - 1 ];
		Vector3 endTan = worldPoints[ worldPoints.Count - 1 ] - worldPoints[ worldPoints.Count - 2 ];
		if ( endTan.sqrMagnitude < 0.0001f )
			endTan = Vector3.forward;
		else
			endTan.Normalize();

		worldTangent = endTan;
		return true;
	}

	public static float MeasureLength( List<Vector3> points )
	{
		if ( points == null || points.Count < 2 )
			return 0f;

		float sum = 0f;
		for ( int i = 0; i < points.Count - 1; i++ )
			sum += Vector3.Distance( points[ i ], points[ i + 1 ] );

		return sum;
	}
}

[Serializable]
public struct MinecartJunctionPort
{
	public MinecartTrack track;
	public float distance;
	public Vector3 tangent;
}

[Serializable]
public struct MinecartTrackJunctionIndex
{
	public MinecartTrack track;
	public float distance;
	public int junctionIndex;
}

public struct MinecartJunctionExit
{
	public MinecartTrack track;
	public float distance;
	public int travelSign;
	public Vector3 worldTangent;
}

[Serializable]
public class MinecartJunctionDebugEntry
{
	public Vector3 worldPos;
	public Vector3 inwardTangent;
	public Color color;
}

[Serializable]
public class MinecartJunctionDebugCorner
{
	public Vector3 start;
	public Vector3 control;
	public Vector3 end;
}

[Serializable]
public class MinecartJunctionDebugPolyline
{
	public List<Vector3> points = new List<Vector3>( 8 );
}
