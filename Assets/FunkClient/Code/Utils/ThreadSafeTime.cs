using System.Diagnostics;

public static class ThreadSafeTime
{
	private static readonly Stopwatch stopwatch = Stopwatch.StartNew();

	public static float Now => (float)stopwatch.Elapsed.TotalSeconds;
}