using System.Collections;

using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Whole coin-stack pickup / place SFX from <see cref="CarryDefinition"/>, plus a staggered
/// burst of per-coin pickup clips when a stack settles into the hand.
/// </summary>
public static class CoinStackInteractSfx
{
	const float StackSpatialMinDistance = 1.25f;
	const float StackSpatialMaxDistance = 22f;

	static Feedbacks _stackHost;
	static PlayRandomSFXFeedback _stackSfx;

	public static void PlayStackPickup( Vector3 position )
	{
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		if ( carryDef == null )
			return;

		PlayStackClip(
			carryDef.coinStackPickupClips,
			carryDef.coinStackPickupVolumeMin,
			carryDef.coinStackPickupVolumeMax,
			carryDef.coinStackPickupPitchMin,
			carryDef.coinStackPickupPitchMax,
			position );
	}

	public static void PlayStackPlace( Vector3 position )
	{
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		if ( carryDef == null )
			return;

		PlayStackClip(
			carryDef.coinStackPlaceClips,
			carryDef.coinStackPlaceVolumeMin,
			carryDef.coinStackPlaceVolumeMax,
			carryDef.coinStackPlacePitchMin,
			carryDef.coinStackPlacePitchMax,
			position );
	}

	public static void PlayStackHandLand( TreasureDefinition coinDefinition, int stackCount, Vector3 position )
	{
		if ( coinDefinition == null || stackCount <= 0 )
			return;

		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		int playCount = carryDef != null
			? carryDef.ResolveStackHandLandPickupCount( stackCount )
			: Mathf.Min( stackCount, 3 );

		if ( playCount <= 0 )
			return;

		TreasureMotionHost.Run( HandLandRoutine( coinDefinition, playCount, position, carryDef ) );
	}

	static void PlayStackClip(
		AudioClip[] clips,
		float volumeMin,
		float volumeMax,
		float pitchMin,
		float pitchMax,
		Vector3 position )
	{
		if ( clips == null || clips.Length == 0 )
			return;

		EnsureStackHost();
		if ( _stackHost == null || _stackSfx == null )
			return;

		_stackSfx.Clips = clips;
		_stackSfx.VolumeMin = volumeMin;
		_stackSfx.VolumeMax = volumeMax;
		_stackSfx.PitchMin = pitchMin;
		_stackSfx.PitchMax = pitchMax;
		_stackSfx.SpatialBlend = 1f;
		_stackSfx.MinDistance = StackSpatialMinDistance;
		_stackSfx.MaxDistance = StackSpatialMaxDistance;

		_stackHost.transform.position = position;

		FeedbackContext context = new FeedbackContext();
		context.Position = position;
		_stackHost.Play( context );
	}

	static IEnumerator HandLandRoutine(
		TreasureDefinition coinDefinition,
		int playCount,
		Vector3 position,
		CarryDefinition carryDef )
	{
		if ( coinDefinition.pickupClips == null || coinDefinition.pickupClips.Length == 0 )
			yield break;

		float stagger = carryDef != null ? carryDef.stackHandLandPickupStaggerSeconds : 0.04f;
		stagger = Mathf.Max( 0f, stagger );
		float volumeScale = carryDef != null ? carryDef.stackHandLandPickupVolumeScale : 0.45f;
		volumeScale = Mathf.Clamp01( volumeScale );
		float spatialBlend = carryDef != null ? carryDef.stackHandLandPickupSpatialBlend : 0f;

		float volumeMin = coinDefinition.pickupVolumeMin * volumeScale;
		float volumeMax = coinDefinition.pickupVolumeMax * volumeScale;

		for ( int i = 0; i < playCount; i++ )
		{
			AudioClip clip = coinDefinition.pickupClips[ Random.Range( 0, coinDefinition.pickupClips.Length ) ];
			if ( clip != null )
			{
				FeedbackSfxPlayback.Play(
					clip,
					volumeMin,
					volumeMax,
					coinDefinition.pickupPitchMin,
					coinDefinition.pickupPitchMax,
					null,
					position,
					spatialBlend,
					StackSpatialMinDistance,
					StackSpatialMaxDistance );
			}

			if ( i + 1 < playCount && stagger > 0.0001f )
				yield return new WaitForSeconds( stagger );
		}
	}

	static void EnsureStackHost()
	{
		if ( _stackHost != null && _stackSfx != null )
			return;

		GameObject go = new GameObject( "[CoinStackInteractSfx]" );
		Object.DontDestroyOnLoad( go );
		_stackHost = go.AddComponent<Feedbacks>();
		_stackSfx = new PlayRandomSFXFeedback();
		_stackHost.AddFeedback( _stackSfx );
	}
}
