using System;

using UnityEngine;

/// <summary>
/// Delay / rise / hold / fall envelope evaluated as a 0→1→0 weight for reveal punches.
/// </summary>
[Serializable]
public struct RevealPunchChannel
{
	[Min( 0f )]
	public float delay;

	[Min( 0f )]
	public float rise;

	[Min( 0f )]
	public float hold;

	[Min( 0f )]
	public float fall;

	[Min( 0f )]
	public float peak;

	public float TotalDuration => delay + rise + hold + fall;

	public bool IsActive => peak > 0f;

	public bool IsScaleActive => peak > 1f + 1e-4f;

	public float EvaluateWeight( float elapsed )
	{
		if ( elapsed < delay )
			return 0f;

		float local = elapsed - delay;
		if ( rise > 0f && local < rise )
			return SmoothStep( local / rise );

		local -= rise;
		if ( hold > 0f && local < hold )
			return 1f;

		local -= hold;
		if ( fall > 0f )
			return 1f - SmoothStep( Mathf.Clamp01( local / fall ) );

		return 0f;
	}

	public float EvaluateScale( float elapsed, float restScale )
	{
		if ( !IsScaleActive )
			return restScale;

		float weight = EvaluateWeight( elapsed );
		return Mathf.Lerp( restScale, peak, weight );
	}

	static float SmoothStep( float t )
	{
		t = Mathf.Clamp01( t );
		return t * t * ( 3f - 2f * t );
	}
}
