using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Gold-pile dig pour SFX at the carve point via FeedbackSystem.
/// </summary>
public static class TreasurePileDigSfx
{
	const float PourMinDistance = 1.25f;
	const float PourMaxDistance = 22f;

	static Feedbacks _host;
	static PlayRandomSFXFeedback _sfx;

	public static void PlayDigPour( Vector3 digPoint, int amount )
	{
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		if ( carryDef == null )
			return;

		AudioClip[] clips = carryDef.pileDigPourClips;
		if ( clips == null || clips.Length == 0 )
			return;

		EnsureHost();
		if ( _host == null || _sfx == null )
			return;

		float volume = carryDef.ResolvePileDigPourVolume( amount );
		_sfx.Clips = clips;
		_sfx.VolumeMin = volume;
		_sfx.VolumeMax = volume;
		_sfx.PitchMin = carryDef.pileDigPourPitchMin;
		_sfx.PitchMax = carryDef.pileDigPourPitchMax;
		_sfx.SpatialBlend = 1f;
		_sfx.MinDistance = PourMinDistance;
		_sfx.MaxDistance = PourMaxDistance;

		_host.transform.position = digPoint;

		FeedbackContext context = new FeedbackContext();
		context.Position = digPoint;
		_host.Play( context );
	}

	/// <summary>
	/// Quiet physical clink for an extra coin absorbed after a pile steal (no obtain ding).
	/// Advances the coin pickup combo so rapid digs keep climbing.
	/// </summary>
	public static void PlayExtraStealClink( TreasureDefinition definition, Vector3 position )
	{
		if ( definition == null || definition.category != TreasureCategory.Coin )
			return;

		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		float volumeScale = carryDef != null ? carryDef.pileStealExtraClinkVolumeScale : 0.4f;
		TreasureInteractSfx.PlayPhysicalPickupClink( definition, position, volumeScale, advanceCombo: true );
	}

	static void EnsureHost()
	{
		if ( _host != null && _sfx != null )
			return;

		GameObject go = new GameObject( "[TreasurePileDigSfx]" );
		Object.DontDestroyOnLoad( go );
		_host = go.AddComponent<Feedbacks>();
		_sfx = new PlayRandomSFXFeedback();
		_host.AddFeedback( _sfx );
	}
}
