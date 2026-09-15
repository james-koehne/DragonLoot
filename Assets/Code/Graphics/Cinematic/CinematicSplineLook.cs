using System;

using Unity.Mathematics;

using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Shared Unity Spline sampling for intro cinematics.
/// </summary>
public static class CinematicSplineLook
{
	const float MinSplineLength = 0.05f;

	public static bool TryEvaluate(
		SplineContainer container,
		float normalizedT,
		out Vector3 position,
		out Vector3 tangent,
		out Vector3 up )
	{
		position = Vector3.zero;
		tangent = Vector3.forward;
		up = Vector3.up;
		if ( container == null || container.Spline == null || container.Spline.Count < 2 )
			return false;

		float length = container.CalculateLength();
		if ( length < MinSplineLength )
			return false;

		float t = Mathf.Clamp01( normalizedT );
		float3 pos;
		float3 tan;
		float3 up3;
		SplineUtility.Evaluate( container.Spline, t, out pos, out tan, out up3 );
		Transform xf = container.transform;
		position = xf.TransformPoint( (Vector3)pos );
		tangent = xf.TransformDirection( (Vector3)tan );
		up = xf.TransformDirection( (Vector3)up3 );
		if ( tangent.sqrMagnitude < 0.0001f )
			tangent = xf.forward;
		else
			tangent.Normalize();

		if ( up.sqrMagnitude < 0.0001f )
			up = Vector3.up;
		else
			up.Normalize();

		return true;
	}

	public static bool TryEvaluatePosition( SplineContainer container, float normalizedT, out Vector3 position )
	{
		Vector3 tangent;
		Vector3 up;
		return TryEvaluate( container, normalizedT, out position, out tangent, out up );
	}

	public static Quaternion LookRotation( Vector3 from, Vector3 lookPoint, Vector3 worldUp )
	{
		Vector3 delta = lookPoint - from;
		if ( delta.sqrMagnitude < 0.0001f )
			return Quaternion.LookRotation( Vector3.forward, worldUp );

		return Quaternion.LookRotation( delta.normalized, worldUp );
	}

	public static float NormalizePitch( float eulerX )
	{
		if ( eulerX > 180f )
			eulerX -= 360f;
		return eulerX;
	}
}
