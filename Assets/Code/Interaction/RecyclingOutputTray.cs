using UnityEngine;

/// <summary>
/// Output tray sockets and lip collider for recycled resources.
/// </summary>
public class RecyclingOutputTray : MonoBehaviour
{
	[SerializeField]
	RecyclingMachine station;

	[SerializeField]
	Transform spawnPoint;

	[SerializeField]
	Transform[] scatterPoints;

	int _scatterIndex;

	public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

	public void BindStation( RecyclingMachine owner )
	{
		station = owner;
	}

	public void GetNextSpawnPose( out Vector3 position, out Quaternion rotation )
	{
		Transform point = SpawnPoint;
		if ( scatterPoints != null && scatterPoints.Length > 0 )
		{
			_scatterIndex = ( _scatterIndex + 1 ) % scatterPoints.Length;
			Transform scatter = scatterPoints[ _scatterIndex ];
			if ( scatter != null )
				point = scatter;
		}

		position = point.position;
		rotation = point.rotation;
		position += new Vector3(
			Random.Range( -0.08f, 0.08f ),
			0.05f,
			Random.Range( -0.08f, 0.08f ) );
	}

#if UNITY_EDITOR
	public void EditorSetSpawnPoint( Transform value )
	{
		spawnPoint = value;
	}

	public void EditorSetScatterPoints( Transform[] value )
	{
		scatterPoints = value;
	}
#endif
}
