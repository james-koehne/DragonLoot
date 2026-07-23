using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

public static class TaskEx
{
	public static Task DelayAsync( int ms ) => DelayAsync( ms, CancellationToken.None );
	public static Task DelayAsync( float ms ) => DelayAsync( ms, CancellationToken.None );

	public static async Task DelayAsync( int ms, CancellationToken token )
	{
		token.ThrowIfCancellationRequested();

		if ( ms == 0 )
			return;

		double wallMs = getScaledDelayWallMilliseconds( ms );
		await delayWallMillisecondsAsync( wallMs, token );
	}

	public static async Task DelayAsync( float ms, CancellationToken token )
	{
		token.ThrowIfCancellationRequested();

		if ( ms == 0.0f )
			return;

		double wallMs = getScaledDelayWallMilliseconds( ms );
		await delayWallMillisecondsAsync( wallMs, token );
	}

	private static async Task delayWallMillisecondsAsync( double wallMs, CancellationToken token )
	{
		if ( wallMs <= 0 )
			return;

		var sw = Stopwatch.StartNew();
		while ( sw.Elapsed.TotalMilliseconds < wallMs )
		{
			token.ThrowIfCancellationRequested();
			await Task.Yield();
		}
	}

	private static double getScaledDelayWallMilliseconds( double gameTimeMs )
	{
		float scale = GameInstance.CachedTimeScale;
		if ( float.IsNaN( scale ) || float.IsInfinity( scale ) || scale <= 0f )
		{
			return gameTimeMs;
		}

		return gameTimeMs / scale;
	}

	public static Task WaitWhile( Func<bool> condition, int timeout = -1 )
		=> WaitWhile( condition, CancellationToken.None, timeout );

	public static async Task WaitWhile( Func<bool> condition, CancellationToken token, int timeout = -1 )
	{
		token.ThrowIfCancellationRequested();

		var sw = Stopwatch.StartNew();
		while ( condition() )
		{
			token.ThrowIfCancellationRequested();
			if ( timeout >= 0 && sw.ElapsedMilliseconds >= timeout )
				throw new TimeoutException();

			await AwaitWaitPoll( token );
		}
	}

	public static Task WaitUntil( Func<bool> condition, int timeout = -1 )
		=> WaitUntil( condition, CancellationToken.None, timeout );

	public static async Task WaitUntil( Func<bool> condition, CancellationToken token, int timeout = -1 )
	{
		token.ThrowIfCancellationRequested();

		var sw = Stopwatch.StartNew();
		while ( !condition() )
		{
			token.ThrowIfCancellationRequested();
			if ( timeout >= 0 && sw.ElapsedMilliseconds >= timeout )
				throw new TimeoutException();

			await AwaitWaitPoll( token );
		}
	}

	private static async Task AwaitWaitPoll( CancellationToken token )
	{
		await Task.Yield();
	}

	public static async void RunAfter( float seconds, Action action )
	{
		if ( action == null )
			return;

		int ms = (int)( seconds * 1000 );
		await DelayAsync( ms );
		action?.Invoke();
	}

	/// <summary>
	/// Use with <see cref="ContinueWith"/> (<c>OnlyOnFaulted</c>) or call after a task completes to log unhandled exceptions from fire-and-forget work.
	/// </summary>
	public static void LogIfFaulted( Task completedTask )
	{
		if ( completedTask == null || !completedTask.IsFaulted )
			return;

		Exception ex = completedTask.Exception?.GetBaseException();
		if ( ex != null )
			UnityEngine.Debug.LogException( ex );
	}

	/// <summary>
	/// Observes a fire-and-forget task so faults are logged instead of being swallowed.
	/// </summary>
	public static void ObserveFaults( this Task task )
	{
		if ( task == null )
			return;

		if ( task.IsCompleted )
		{
			LogIfFaulted( task );
			return;
		}

		_ = task.ContinueWith( LogIfFaulted, TaskContinuationOptions.OnlyOnFaulted );
	}
}
