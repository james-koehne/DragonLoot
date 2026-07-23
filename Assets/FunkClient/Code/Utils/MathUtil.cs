using UnityEngine;

using Random = UnityEngine.Random;

public static class MathUtil
{
	/// <summary>
	/// Normalizes an angle to the range [0, 360).
	/// </summary>
	public static float NormalizeAngle( float angle )
	{
		return Mathf.Repeat( angle, 360f );
	}

	/// <summary>
	/// Normalizes an angle to the range [-180, 180].
	/// </summary>
	public static float NormalizeAngleSigned( float angle )
	{
		angle = Mathf.Repeat( angle + 180f, 360f ) - 180f;
		return angle;
	}

	/// <summary>
	/// Returns the shortest angular difference between two angles.
	/// </summary>
	public static float AngleDifference( float from, float to )
	{
		return Mathf.DeltaAngle( from, to );
	}

	/// <summary>
	/// Checks if an angle is within a certain tolerance of a target angle.
	/// </summary>
	public static bool IsAngleWithinTolerance( float current, float target, float tolerance )
	{
		return Mathf.Abs( AngleDifference( current, target ) ) <= tolerance;
	}

	/// <summary>
	/// Clamps an angle within a specified min and max range.
	/// </summary>
	public static float ClampAngle( float angle, float min, float max )
	{
		angle = NormalizeAngleSigned( angle );
		return Mathf.Clamp( angle, min, max );
	}

	/// <summary>
	/// Returns true if all angles in two Vector3 Euler angles are within a given tolerance.
	/// </summary>
	public static bool AreEulerAnglesWithinTolerance( Vector3 current, Vector3 target, float tolerance )
	{
		return IsAngleWithinTolerance( current.x, target.x, tolerance ) &&
			   IsAngleWithinTolerance( current.y, target.y, tolerance ) &&
			   IsAngleWithinTolerance( current.z, target.z, tolerance );
	}

	public static float MapRange( float input, float A, float B, float C, float D )
	{
		// Check for division by zero
		if ( B - A == 0 )
		{
			Debug.LogError( "Invalid range: B cannot be equal to A" );
			return 0;
		}

		// Map the input from [A, B] to [C, D]
		float mappedValue = C + ( ( input - A ) / ( B - A ) ) * ( D - C );
		return mappedValue;
	}

	public static Vector3? GetRectWorldCenter( RectTransform rectTransform )
	{
		if ( rectTransform == null )
			return null;

		Vector3[] worldCorners = new Vector3[ 4 ];
		rectTransform.GetWorldCorners( worldCorners );

		return ( worldCorners[ 0 ] + worldCorners[ 2 ] ) / 2f;
	}

	public static int RandomRange( int minInclusive, int maxExclusive )
	{
		return Random.Range( minInclusive, maxExclusive );
	}

	public static float RandomRange( float minInclusive, float maxInclusive )
	{
		return Random.Range( minInclusive, maxInclusive );
	}

	public static Vector3 RandomPointOnPolarCap( float capPercentage = 0.25f, float radius = 1f )
	{
		bool isTop = Random.value < 0.5f;

		float minYNormalized = 1f - capPercentage * 2f;
		float yNormalized = isTop
			? Random.Range( minYNormalized, 1f )
			: Random.Range( -1f, -minYNormalized );

		float horizontalRadius = Mathf.Sqrt( 1f - yNormalized * yNormalized );

		float angle = Random.Range( 0f, Mathf.PI * 2f );
		float x = horizontalRadius * Mathf.Cos( angle );
		float z = horizontalRadius * Mathf.Sin( angle );

		return new Vector3( x, yNormalized, z ) * radius;
	}

	public static int WrapIncrement( int index, int count )
	{
		return ( index + 1 ) % count;
	}

	public static int WrapDecrement( int index, int count )
	{
		return ( index - 1 + count ) % count;
	}
}
