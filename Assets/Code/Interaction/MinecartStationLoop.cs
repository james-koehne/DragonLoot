using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Ordered station loop for manual "send to next" dispatch. Wire stations in the Inspector.
/// </summary>
public class MinecartStationLoop : MonoBehaviour
{
	[SerializeField]
	List<MinecartStationBase> stations = new List<MinecartStationBase>();

	public IReadOnlyList<MinecartStationBase> Stations => stations;

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
}
