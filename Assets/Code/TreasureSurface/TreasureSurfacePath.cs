using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Result of a treasure-surface path query. Waypoints are world positions on cell centers.
/// </summary>
public sealed class TreasureSurfacePath
{
	static readonly Vector3[] EmptyWaypoints = new Vector3[ 0 ];

	readonly Vector3[] _waypoints;
	readonly float _length;

	public bool Success { get; }
	public IReadOnlyList<Vector3> Waypoints => _waypoints;
	public float Length => _length;
	public int WaypointCount => _waypoints.Length;

	public static TreasureSurfacePath Failed { get; } = new TreasureSurfacePath( success: false, EmptyWaypoints, 0f );

	public TreasureSurfacePath( bool success, Vector3[] waypoints, float length )
	{
		Success = success;
		_waypoints = waypoints != null ? waypoints : EmptyWaypoints;
		_length = length;
	}

	public static TreasureSurfacePath FromWaypoints( List<Vector3> waypoints )
	{
		if ( waypoints == null || waypoints.Count == 0 )
			return Failed;

		Vector3[] copy = waypoints.ToArray();
		float length = 0f;
		for ( int i = 1; i < copy.Length; i++ )
			length += Vector3.Distance( copy[ i - 1 ], copy[ i ] );

		return new TreasureSurfacePath( success: true, copy, length );
	}
}
