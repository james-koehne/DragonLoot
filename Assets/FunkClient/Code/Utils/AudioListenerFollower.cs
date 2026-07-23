using UnityEngine;

public class AudioListenerFollower : MonoBehaviour
{
	private static AudioListenerFollower instance;

	private Transform target;

	private void Awake()
	{
		if ( instance != null )
		{
			Destroy( gameObject );
			return;
		}

		instance = this;
		DontDestroyOnLoad( gameObject );
	}

	private void Update()
	{
		if ( target != null )
		{
			transform.position = target.position;
			transform.rotation = target.rotation;
		}
	}

	public static void SetTarget( Transform newTarget )
	{
		if ( instance != null )
		{
			instance.target = newTarget;
		}
	}

	public static void ClearTarget()
	{
		if ( instance != null )
		{
			instance.target = null;
		}
	}
}