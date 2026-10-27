using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Ordered station loop for manual "send to next" dispatch. Wire stations in the Inspector.
/// </summary>
public class MinecartStationLoop : MonoBehaviour
{
	static readonly List<MinecartStationLoop> All = new List<MinecartStationLoop>( 4 );

	[SerializeField]
	List<MinecartStationBase> stations = new List<MinecartStationBase>();

	public IReadOnlyList<MinecartStationBase> Stations => stations;

	public static IReadOnlyList<MinecartStationLoop> ActiveLoops => All;

	void OnEnable()
	{
		if ( !All.Contains( this ) )
			All.Add( this );
	}

	void OnDisable()
	{
		All.Remove( this );
	}

	public bool TryGetNext( MinecartStationBase current, out MinecartStationBase next )
	{
		next = null;
		if ( current == null || stations == null || stations.Count == 0 )
			return false;

		int index = -1;
		for ( int i = 0; i < stations.Count; i++ )
		{
			if ( stations[ i ] != current )
				continue;
			index = i;
			break;
		}

		if ( index < 0 )
			return false;

		int count = stations.Count;
		for ( int step = 1; step <= count; step++ )
		{
			MinecartStationBase candidate = stations[ ( index + step ) % count ];
			if ( candidate == null || candidate == current || !candidate.isActiveAndEnabled )
				continue;

			next = candidate;
			return true;
		}

		return false;
	}

	public bool Contains( MinecartStationBase station )
	{
		if ( station == null || stations == null )
			return false;

		for ( int i = 0; i < stations.Count; i++ )
		{
			if ( stations[ i ] == station )
				return true;
		}

		return false;
	}

	/// <summary>First active loop that lists <paramref name="station"/>.</summary>
	public static MinecartStationLoop FindContaining( MinecartStationBase station )
	{
		if ( station == null )
			return null;

		for ( int i = 0; i < All.Count; i++ )
		{
			MinecartStationLoop loop = All[ i ];
			if ( loop != null && loop.isActiveAndEnabled && loop.Contains( station ) )
				return loop;
		}

		return null;
	}
}
