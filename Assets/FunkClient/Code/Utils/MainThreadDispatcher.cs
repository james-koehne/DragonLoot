using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

public class MainThreadDispatcher : MonoBehaviour
{
	private static MainThreadDispatcher _instance = null;
	private static readonly Queue<Action> _executionQueue = new Queue<Action>();

	[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.BeforeSceneLoad )]
	private static void Initialize()
	{
		if ( _instance != null )
		{
			return;
		}

		GameObject obj = new GameObject( "MainThreadDispatcher" );
		DontDestroyOnLoad( obj );

		_instance = obj.AddComponent<MainThreadDispatcher>();
	}

	void Update()
	{
		lock ( _executionQueue )
		{
			while ( _executionQueue.Count > 0 )
			{
				_executionQueue.Dequeue().Invoke();
			}
		}
	}

	public void Enqueue( Action action )
	{
		lock ( _executionQueue )
		{
			_executionQueue.Enqueue( action );
		}
	}

	public static void RunOnMainThread( Action action )
	{
		_instance.Enqueue( action );
	}

	public static Task RunOnMainThreadAsync( Action action )
	{
		var tcs = new TaskCompletionSource<bool>();

		_instance.Enqueue( () =>
		{
			try
			{
				action();
				tcs.SetResult( true );
			}
			catch ( Exception e )
			{
				tcs.SetException( e );
			}
		} );

		return tcs.Task;
	}
}
