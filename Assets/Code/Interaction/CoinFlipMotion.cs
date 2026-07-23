using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Flight arcs for treasure tweens: coins get a tall arc + flip; gems / other items get a mild arc only.
/// </summary>
public static class CoinFlipMotion
{
	public const float DefaultArcHeight = 0.65f;
	public const float DefaultSpins = 1.25f;
	public const float DefaultDuration = 0.32f;

	/// <summary>Milder place/pickup arc for gems and non-coin treasure (no flip).</summary>
	public const float DefaultItemArcHeight = 0.08f;

	public const float DefaultItemArcDuration = 0.22f;

	public static bool IsCoin( TreasureItem item )
	{
		return item != null
			&& item.Definition != null
			&& item.Definition.category == TreasureCategory.Coin;
	}

	public static bool IsCoin( TreasureDefinition definition )
	{
		return definition != null && definition.category == TreasureCategory.Coin;
	}

	public static float SmoothStep( float u )
	{
		u = Mathf.Clamp01( u );
		return u * u * ( 3f - 2f * u );
	}

	/// <summary>Linear blend with a vertical (or along-up) parabolic arc.</summary>
	public static Vector3 EvaluateArcPosition( Vector3 start, Vector3 end, float u, float arcHeight, Vector3 up )
	{
		float ease = SmoothStep( u );
		Vector3 pos = Vector3.Lerp( start, end, ease );
		if ( arcHeight > 0.0001f && up.sqrMagnitude > 0.0001f )
			pos += up.normalized * ( arcHeight * Mathf.Sin( Mathf.Clamp01( u ) * Mathf.PI ) );
		return pos;
	}

	public static Vector3 EvaluateArcPosition( Vector3 start, Vector3 end, float u, float arcHeight )
	{
		return EvaluateArcPosition( start, end, u, arcHeight, ResolveArcUp( start, end ) );
	}

	/// <summary>Unit vector for parabolic bump in the vertical plane containing travel.</summary>
	public static Vector3 ResolveArcUp( Vector3 start, Vector3 end )
	{
		Vector3 travel = end - start;
		if ( travel.sqrMagnitude < 0.0001f )
			return Vector3.up;

		Vector3 side = Vector3.Cross( travel, Vector3.up );
		if ( side.sqrMagnitude < 0.0001f )
			return Vector3.up;

		Vector3 arcUp = Vector3.Cross( side, travel );
		if ( arcUp.sqrMagnitude < 0.0001f )
			return Vector3.up;

		return arcUp.normalized;
	}

	/// <summary>
	/// Slerps toward <paramref name="end"/> while spinning end-over-end around a flip axis
	/// (horizontal, perpendicular to travel) — reads as flipping a coin through the air.
	/// </summary>
	public static Quaternion EvaluateFlipRotation( Quaternion start, Quaternion end, Vector3 startPos, Vector3 endPos, float u, float spins )
	{
		float ease = SmoothStep( u );
		Quaternion baseRot = Quaternion.Slerp( start, end, ease );

		Vector3 travel = endPos - startPos;
		Vector3 axis = Vector3.Cross( Vector3.up, travel );
		if ( axis.sqrMagnitude < 0.0001f )
			axis = Vector3.right;
		else
			axis.Normalize();

		float angle = spins * 360f * Mathf.Clamp01( u );
		return Quaternion.AngleAxis( angle, axis ) * baseRot;
	}

	/// <summary>Local-space arc for non-coin pickup (no spin).</summary>
	public static Vector3 EvaluateLocalArc( Vector3 startLocalPos, Vector3 endLocalPos, float u, float arcHeight )
	{
		float ease = SmoothStep( u );
		Vector3 localPos = Vector3.Lerp( startLocalPos, endLocalPos, ease );
		localPos.y += arcHeight * Mathf.Sin( Mathf.Clamp01( u ) * Mathf.PI );
		return localPos;
	}

	/// <summary>Local-space flip for hand pickup (arc on local Y, flip around local right).</summary>
	public static void EvaluateLocalFlip(
		Vector3 startLocalPos,
		Quaternion startLocalRot,
		Vector3 endLocalPos,
		Quaternion endLocalRot,
		float u,
		float arcHeight,
		float spins,
		out Vector3 localPos,
		out Quaternion localRot )
	{
		float ease = SmoothStep( u );
		localPos = Vector3.Lerp( startLocalPos, endLocalPos, ease );
		localPos.y += arcHeight * Mathf.Sin( Mathf.Clamp01( u ) * Mathf.PI );

		Quaternion baseRot = Quaternion.Slerp( startLocalRot, endLocalRot, ease );
		float angle = spins * 360f * Mathf.Clamp01( u );
		localRot = Quaternion.AngleAxis( angle, Vector3.right ) * baseRot;
	}

	/// <summary>
	/// World-space flip for a cluster of coins to their end poses. Caller settles ownership after.
	/// </summary>
	public static IEnumerator AnimateWorldFlips(
		IReadOnlyList<TreasureItem> items,
		Vector3[] endPositions,
		Quaternion[] endRotations,
		float duration = DefaultDuration,
		float arcHeight = DefaultArcHeight,
		float spins = DefaultSpins )
	{
		if ( items == null || endPositions == null || endRotations == null )
			yield break;

		int count = Mathf.Min( items.Count, Mathf.Min( endPositions.Length, endRotations.Length ) );
		if ( count <= 0 )
			yield break;

		Vector3[] startPos = new Vector3[ count ];
		Quaternion[] startRot = new Quaternion[ count ];
		Vector3[] startScale = new Vector3[ count ];
		Vector3[] endScale = new Vector3[ count ];

		for ( int i = 0; i < count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				continue;

			Transform t = item.transform;
			t.SetParent( null, true );
			item.ApplyWorldScale();

			Rigidbody body = item.Body;
			if ( body != null )
			{
				body.isKinematic = true;
				body.detectCollisions = false;
				body.useGravity = false;
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}

			startPos[ i ] = t.position;
			startRot[ i ] = t.rotation;
			startScale[ i ] = t.localScale;
			endScale[ i ] = item.GetWorldScale();
		}

		duration = Mathf.Max( 0.05f, duration );
		float elapsed = 0f;
		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			float scaleEase = SmoothStep( u );

			for ( int i = 0; i < count; i++ )
			{
				TreasureItem item = items[ i ];
				if ( item == null )
					continue;

				Transform t = item.transform;
				t.position = EvaluateArcPosition( startPos[ i ], endPositions[ i ], u, arcHeight );
				t.rotation = EvaluateFlipRotation( startRot[ i ], endRotations[ i ], startPos[ i ], endPositions[ i ], u, spins );
				t.localScale = Vector3.Lerp( startScale[ i ], endScale[ i ], scaleEase );
			}

			yield return null;
		}

		for ( int i = 0; i < count; i++ )
		{
			TreasureItem item = items[ i ];
			if ( item == null )
				continue;

			item.transform.SetPositionAndRotation( endPositions[ i ], endRotations[ i ] );
			item.ApplyWorldScale();
		}
	}
}
