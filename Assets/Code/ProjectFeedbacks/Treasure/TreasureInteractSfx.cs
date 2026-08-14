using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays pick/place SFX from <see cref="TreasureDefinition"/> clip lists via FeedbackSystem.
/// Uses a dedicated host so visual interact feedbacks are not interrupted.
/// Place SFX are 3D at the destination / landing point (e.g. coin stack top).
/// Pickup stays 2D.
/// </summary>
public static class TreasureInteractSfx
{
	const float PlaceMinDistance = 1.25f;
	const float PlaceMaxDistance = 22f;

	static Feedbacks _host;
	static PlayRandomSFXFeedback _sfx;

	public static void PlayPickup( TreasureItem item )
	{
		if ( item == null )
			return;

		Play( item.Definition, pickup: true, item.transform.position );
	}

	public static void PlayPlace( TreasureItem item )
	{
		if ( item == null )
			return;

		Play( item.Definition, pickup: false, item.transform.position );
	}

	public static void PlayPickup( TreasureDefinition definition, Vector3 position )
	{
		Play( definition, pickup: true, position );
	}

	public static void PlayPlace( TreasureDefinition definition, Vector3 position )
	{
		Play( definition, pickup: false, position );
	}

	static void Play( TreasureDefinition definition, bool pickup, Vector3 position )
	{
		if ( definition == null )
			return;

		AudioClip[] clips = pickup ? definition.pickupClips : definition.placeClips;
		if ( clips == null || clips.Length == 0 )
			return;

		EnsureHost();
		if ( _host == null || _sfx == null )
			return;

		_sfx.Clips = clips;
		if ( pickup )
		{
			_sfx.VolumeMin = definition.pickupVolumeMin;
			_sfx.VolumeMax = definition.pickupVolumeMax;
			_sfx.PitchMin = definition.pickupPitchMin;
			_sfx.PitchMax = definition.pickupPitchMax;
			_sfx.SpatialBlend = 0f;
		}
		else
		{
			_sfx.VolumeMin = definition.placeVolumeMin;
			_sfx.VolumeMax = definition.placeVolumeMax;
			_sfx.PitchMin = definition.placePitchMin;
			_sfx.PitchMax = definition.placePitchMax;
			_sfx.SpatialBlend = 1f;
			_sfx.MinDistance = PlaceMinDistance;
			_sfx.MaxDistance = PlaceMaxDistance;
		}

		_host.transform.position = position;

		FeedbackContext context = new FeedbackContext();
		context.Position = position;
		_host.Play( context );
	}

	static void EnsureHost()
	{
		if ( _host != null && _sfx != null )
			return;

		GameObject go = new GameObject( "[TreasureInteractSfx]" );
		Object.DontDestroyOnLoad( go );
		_host = go.AddComponent<Feedbacks>();
		_sfx = new PlayRandomSFXFeedback();
		_host.AddFeedback( _sfx );
	}
}
