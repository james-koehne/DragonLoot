using System.Collections.Generic;
using System.Text;

using UnityEngine;

/// <summary>
/// Dev toggle + Profiler markers + ranked phase summaries for gold-pile carve cost.
/// Enable from Debug Overlay → Gold Pile → Log edit timings.
/// </summary>
public static class GoldPileEditTiming
{
	const int MaxPhases = 48;

	public static bool Enabled;
	/// <summary>When true, also logs each phase as it completes (noisy).</summary>
	public static bool Verbose;
	public static string LastSummary { get; private set; } = "";

	struct Phase
	{
		public int CarveId;
		public string Name;
		public double Ms;
		public string Detail;
	}

	static readonly List<Phase> s_immediate = new List<Phase>( MaxPhases );
	static readonly List<Phase> s_deferred = new List<Phase>( MaxPhases );
	static readonly StringBuilder s_sb = new StringBuilder( 512 );

	static int s_nextCarveId;
	static int s_activeCarveId;
	static bool s_inImmediate;
	static bool s_hasDeferred;
	static int s_deferRecordFrame = -1;
	static Object s_context;
	static int s_units;
	static string s_sessionDetail;
	static System.Diagnostics.Stopwatch s_sessionWatch;

	public static void Begin( string name )
	{
		UnityEngine.Profiling.Profiler.BeginSample( name );
	}

	public static void End()
	{
		UnityEngine.Profiling.Profiler.EndSample();
	}

	public static System.Diagnostics.Stopwatch StartWatchIfEnabled()
	{
		return Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;
	}

	public static void LogIfEnabled( string message, Object context = null )
	{
		if ( !Enabled )
			return;
		if ( context != null )
			Debug.Log( message, context );
		else
			Debug.Log( message );
	}

	/// <summary>Starts a carve timing session. Call <see cref="EndCarveImmediate"/> when dig-frame work finishes.</summary>
	public static void BeginCarve( int units, Object context = null, string detail = null )
	{
		if ( !Enabled )
			return;

		// Flush any unfinished deferred leftovers from a prior carve.
		FlushDeferredIfAny();

		s_nextCarveId++;
		s_activeCarveId = s_nextCarveId;
		s_units = Mathf.Max( 1, units );
		s_context = context;
		s_sessionDetail = detail;
		s_immediate.Clear();
		s_deferred.Clear();
		s_inImmediate = true;
		s_hasDeferred = false;
		s_deferRecordFrame = -1;
		s_sessionWatch = System.Diagnostics.Stopwatch.StartNew();
	}

	public static void EndCarveImmediate()
	{
		if ( !Enabled || !s_inImmediate )
			return;

		s_inImmediate = false;
		double wallMs = s_sessionWatch != null ? s_sessionWatch.Elapsed.TotalMilliseconds : 0.0;
		LastSummary = BuildSummary( "immediate", s_activeCarveId, s_units, s_immediate, wallMs );
		LogIfEnabled( LastSummary, s_context as Object );
	}

	/// <summary>
	/// Call from LateUpdate paths. Flushes deferred phases once the frame after they were recorded,
	/// so upload + collider + densify + stamp land in one ranked summary.
	/// </summary>
	public static void TryFlushDeferredFromPriorFrame()
	{
		if ( !Enabled || !s_hasDeferred )
			return;
		if ( Time.frameCount <= s_deferRecordFrame )
			return;

		FlushDeferredIfAny();
	}

	/// <summary>Emits ranked deferred-phase summary if anything was recorded since last flush.</summary>
	public static void FlushDeferredIfAny()
	{
		if ( !Enabled || !s_hasDeferred || s_deferred.Count == 0 )
			return;

		LastSummary = BuildSummary( "deferred", s_activeCarveId, s_units, s_deferred, SumMs( s_deferred ) );
		LogIfEnabled( LastSummary, s_context as Object );

		s_deferred.Clear();
		s_hasDeferred = false;
		s_deferRecordFrame = -1;
	}

	public static void Record( string name, double ms, string detail = null )
	{
		if ( !Enabled )
			return;

		Phase phase = new Phase
		{
			CarveId = s_activeCarveId,
			Name = name,
			Ms = ms,
			Detail = detail
		};

		if ( s_inImmediate )
		{
			if ( s_immediate.Count < MaxPhases )
				s_immediate.Add( phase );
		}
		else
		{
			s_hasDeferred = true;
			s_deferRecordFrame = Time.frameCount;
			if ( s_deferred.Count < MaxPhases )
				s_deferred.Add( phase );
		}

		if ( Verbose )
		{
			string line = detail != null && detail.Length > 0
				? $"[GoldPileEdit] carve#{s_activeCarveId} {name}={ms:F2}ms ({detail})"
				: $"[GoldPileEdit] carve#{s_activeCarveId} {name}={ms:F2}ms";
			LogIfEnabled( line, s_context as Object );
		}
	}

	/// <summary>Profiler sample + optional ms record into the active carve session.</summary>
	public static Scope Measure( string phaseName, string detail = null )
	{
		return new Scope( phaseName, detail );
	}

	public struct Scope : System.IDisposable
	{
		readonly string _phase;
		readonly string _detail;
		readonly System.Diagnostics.Stopwatch _sw;

		public Scope( string phase, string detail )
		{
			_phase = phase;
			_detail = detail;
			UnityEngine.Profiling.Profiler.BeginSample( phase );
			_sw = Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;
		}

		public void Dispose()
		{
			if ( _sw != null )
			{
				_sw.Stop();
				Record( _phase, _sw.Elapsed.TotalMilliseconds, _detail );
			}

			UnityEngine.Profiling.Profiler.EndSample();
		}
	}

	static double SumMs( List<Phase> phases )
	{
		double sum = 0.0;
		for ( int i = 0; i < phases.Count; i++ )
			sum += phases[ i ].Ms;
		return sum;
	}

	static string BuildSummary( string kind, int carveId, int units, List<Phase> phases, double wallMs )
	{
		s_sb.Clear();
		s_sb.Append( "[GoldPileEdit] carve#" ).Append( carveId )
			.Append( ' ' ).Append( kind )
			.Append( " units=" ).Append( units )
			.Append( " wall=" ).Append( wallMs.ToString( "F2" ) ).Append( "ms" );
		if ( s_sessionDetail != null && s_sessionDetail.Length > 0 )
			s_sb.Append( " [" ).Append( s_sessionDetail ).Append( ']' );

		if ( phases.Count == 0 )
		{
			s_sb.Append( " (no phases)" );
			return s_sb.ToString();
		}

		// Rank by cost descending without allocating a sorted copy when small.
		int n = phases.Count;
		int[] order = new int[ n ];
		for ( int i = 0; i < n; i++ )
			order[ i ] = i;
		for ( int i = 0; i < n - 1; i++ )
		{
			int best = i;
			for ( int j = i + 1; j < n; j++ )
			{
				if ( phases[ order[ j ] ].Ms > phases[ order[ best ] ].Ms )
					best = j;
			}
			if ( best != i )
			{
				int tmp = order[ i ];
				order[ i ] = order[ best ];
				order[ best ] = tmp;
			}
		}

		double recorded = SumMs( phases );
		s_sb.Append( " recorded=" ).Append( recorded.ToString( "F2" ) ).Append( "ms |" );
		for ( int i = 0; i < n; i++ )
		{
			Phase p = phases[ order[ i ] ];
			s_sb.Append( ' ' ).Append( p.Name ).Append( '=' ).Append( p.Ms.ToString( "F2" ) );
			if ( p.Detail != null && p.Detail.Length > 0 )
				s_sb.Append( '(' ).Append( p.Detail ).Append( ')' );
		}

		return s_sb.ToString();
	}
}
