using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Plays pick/place SFX from <see cref="TreasureDefinition"/> clip lists via FeedbackSystem.
/// Uses a dedicated host so visual interact feedbacks are not interrupted.
/// Place SFX are 3D at the destination / landing point (e.g. coin stack top).
/// Pickup stays 2D. Coins optionally layer an obtain ding with a rapid-collect pitch combo.
/// </summary>
public static class TreasureInteractSfx
{
	const float PlaceMinDistance = 1.25f;
	const float PlaceMaxDistance = 22f;

	static Feedbacks _host;
	static PlayRandomSFXFeedback _sfx;

	static float _lastCoinPickupTime = -999f;
	static int _comboStep;

	public static void PlayPickup( TreasureItem item )
	{
		if ( item == null )
			return;

		Play( item.Definition, pickup: true, item.transform.position, physicalOnly: false, volumeScale: 1f, advanceCombo: true );
	}

	public static void PlayPlace( TreasureItem item )
	{
		if ( item == null )
			return;

		Play( item.Definition, pickup: false, item.transform.position, physicalOnly: false, volumeScale: 1f, advanceCombo: false );
	}

	public static void PlayPickup( TreasureDefinition definition, Vector3 position )
	{
		Play( definition, pickup: true, position, physicalOnly: false, volumeScale: 1f, advanceCombo: true );
	}

	public static void PlayPlace( TreasureDefinition definition, Vector3 position )
	{
		Play( definition, pickup: false, position, physicalOnly: false, volumeScale: 1f, advanceCombo: false );
	}

	/// <summary>
	/// Quiet physical pickup clink only (no obtain ding). Used for extra pile-steal coins.
	/// When <paramref name="advanceCombo"/> is true, still steps the coin pitch ladder.
	/// </summary>
	public static void PlayPhysicalPickupClink(
		TreasureDefinition definition,
		Vector3 position,
		float volumeScale,
		bool advanceCombo )
	{
		Play( definition, pickup: true, position, physicalOnly: true, volumeScale, advanceCombo );
	}

	static void Play(
		TreasureDefinition definition,
		bool pickup,
		Vector3 position,
		bool physicalOnly,
		float volumeScale,
		bool advanceCombo )
	{
		if ( definition == null )
			return;

		AudioClip[] clips = pickup ? definition.pickupClips : definition.placeClips;
		if ( clips == null || clips.Length == 0 )
			return;

		EnsureHost();
		if ( _host == null || _sfx == null )
			return;

		volumeScale = Mathf.Clamp01( volumeScale );
		int comboStep = 0;
		bool isCoin = definition.category == TreasureCategory.Coin;
		if ( pickup && isCoin && advanceCombo )
			comboStep = AdvanceCoinComboStep();

		_sfx.Clips = clips;
		if ( pickup )
		{
			_sfx.VolumeMin = definition.pickupVolumeMin * volumeScale;
			_sfx.VolumeMax = definition.pickupVolumeMax * volumeScale;
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

		if ( !pickup || physicalOnly || !isCoin )
			return;

		PlayRewardLayer( definition, position, comboStep );
	}

	static void PlayRewardLayer( TreasureDefinition definition, Vector3 position, int comboStep )
	{
		AudioClip[] rewardClips = definition.pickupRewardClips;
		if ( rewardClips == null || rewardClips.Length == 0 )
			return;

		AudioClip clip = rewardClips[ Random.Range( 0, rewardClips.Length ) ];
		if ( clip == null )
			return;

		float comboPitch = Mathf.Pow( 2f, comboStep / 12f );
		FeedbackSfxPlayback.Play(
			clip,
			definition.pickupRewardVolumeMin,
			definition.pickupRewardVolumeMax,
			comboPitch,
			comboPitch,
			null,
			position,
			0f );
	}

	static int AdvanceCoinComboStep()
	{
		CarryDefinition carryDef = null;
		carryDef = RuntimeDefinition.Resolve( ref carryDef );
		if ( carryDef == null || !carryDef.coinPickupComboEnabled )
		{
			_comboStep = 0;
			_lastCoinPickupTime = Time.unscaledTime;
			return 0;
		}

		float now = Time.unscaledTime;
		float window = Mathf.Max( 0.05f, carryDef.coinPickupComboWindowSeconds );
		if ( now - _lastCoinPickupTime > window )
			_comboStep = 0;
		else
			_comboStep = Mathf.Min( _comboStep + 1, Mathf.Max( 0, carryDef.coinPickupComboMaxSteps ) );

		_lastCoinPickupTime = now;
		return _comboStep;
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
