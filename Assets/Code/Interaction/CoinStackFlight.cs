using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Temporary coin-stack cylinder that flies between world poses without spawning individual coins.
/// </summary>
public static class CoinStackFlight
{
	static readonly List<TreasureDefinition> BindScratch = new List<TreasureDefinition>( 64 );

	public static void FlyToHand(
		IReadOnlyList<TreasureDefinition> definitions,
		Vector3 startWorldPos,
		Quaternion startWorldRot,
		float variationSeed,
		Transform endParent,
		Vector3 endLocalPos,
		float duration,
		Action onArrived )
	{
		if ( definitions == null || definitions.Count == 0 || endParent == null )
		{
			if ( onArrived != null )
				onArrived();
			return;
		}

		TreasureMotionHost.Run( FlyRoutine(
			definitions,
			startWorldPos,
			startWorldRot,
			variationSeed,
			endParent,
			endLocalPos,
			followParent: true,
			useHeldScale: true,
			duration,
			onArrived ) );
	}

	public static void FlyToWorld(
		IReadOnlyList<TreasureDefinition> definitions,
		Vector3 startWorldPos,
		Quaternion startWorldRot,
		float variationSeed,
		Vector3 endWorldPos,
		Quaternion endWorldRot,
		float duration,
		Action onArrived )
	{
		if ( definitions == null || definitions.Count == 0 )
		{
			if ( onArrived != null )
				onArrived();
			return;
		}

		TreasureMotionHost.Run( FlyRoutine(
			definitions,
			startWorldPos,
			startWorldRot,
			variationSeed,
			null,
			endWorldPos,
			followParent: false,
			useHeldScale: false,
			duration,
			onArrived,
			endWorldRot ) );
	}

	static IEnumerator FlyRoutine(
		IReadOnlyList<TreasureDefinition> definitions,
		Vector3 startWorldPos,
		Quaternion startWorldRot,
		float variationSeed,
		Transform endParent,
		Vector3 endPosOrLocal,
		bool followParent,
		bool useHeldScale,
		float duration,
		Action onArrived,
		Quaternion endWorldRot = default )
	{
		GameObject go = new GameObject( "CoinStackFlight" );
		go.transform.SetPositionAndRotation( startWorldPos, startWorldRot );

		CoinStackCylinderVisual visual = null;
		BindScratch.Clear();
		for ( int i = 0; i < definitions.Count; i++ )
		{
			if ( definitions[ i ] != null )
				BindScratch.Add( definitions[ i ] );
		}

		bool[] covered = null;
		CoinColumnCylinderBinder.BindDefinitions(
			ref visual,
			go.transform,
			BindScratch,
			snap: true,
			covered,
			useHeldScale: useHeldScale,
			variationSeed: variationSeed );
		if ( visual != null )
			visual.SetVariationSeed( variationSeed );

		duration = Mathf.Max( 0.05f, duration );
		float elapsed = 0f;
		Vector3 start = startWorldPos;
		Quaternion startRot = startWorldRot;

		while ( elapsed < duration )
		{
			elapsed += Time.deltaTime;
			float u = Mathf.Clamp01( elapsed / duration );
			float ease = CoinFlipMotion.SmoothStep( u );

			Vector3 end;
			Quaternion endRot;
			if ( followParent )
			{
				if ( endParent == null )
					break;
				end = endParent.TransformPoint( endPosOrLocal );
				endRot = endParent.rotation;
			}
			else
			{
				end = endPosOrLocal;
				endRot = endWorldRot == default ? startRot : endWorldRot;
			}

			float arc = Mathf.Lerp( 0.12f, 0.28f, Mathf.Clamp01( BindScratch.Count / 40f ) );
			Vector3 pos = Vector3.Lerp( start, end, ease );
			pos.y += Mathf.Sin( ease * Mathf.PI ) * arc;
			go.transform.SetPositionAndRotation( pos, Quaternion.Slerp( startRot, endRot, ease ) );
			yield return null;
		}

		if ( go != null )
			UnityEngine.Object.Destroy( go );

		BindScratch.Clear();
		if ( onArrived != null )
			onArrived();
	}
}
