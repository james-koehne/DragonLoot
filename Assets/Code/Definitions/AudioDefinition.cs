using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Music, ambience, and footstep audio configuration.
/// Asset name must be <c>AudioDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// Min/max pairs are corrected at play time (not in OnValidate).
/// </summary>
[CreateAssetMenu( fileName = "AudioDefinition", menuName = "Definitions/AudioDefinition" )]
public class AudioDefinition : ScriptableObject
{
	[Header( "Master" )]
	[Range( 0f, 1f )]
	[Tooltip( "Designer-wide multiplier applied to all game audio. Combined with the user's saved master volume." )]
	public float masterVolume = 1f;

	[Header( "Music" )]
	[Tooltip( "Playlist played sequentially. Session start index is random; then wraps forever." )]
	public AudioClip[] musicTracks;

	[Range( 0f, 1f )]
	public float musicVolume = 0.6f;

	[Tooltip( "How many times each music clip plays before advancing to the next. 1 = play once then switch." )]
	[Min( 1 )]
	public int musicPlaysPerTrack = 1;

	[Tooltip( "Crossfade seconds when switching music tracks. 0 = hard cut. Overlaps the end of the last play of the current track." )]
	[Min( 0f )]
	[FormerlySerializedAs( "musicFadeSeconds" )]
	public float musicCrossfadeSeconds;

	[Header( "Ambience" )]
	[Tooltip( "Playlist played sequentially from index 0, wrapping forever. Plays alongside music." )]
	public AudioClip[] ambienceTracks;

	[Range( 0f, 1f )]
	public float ambienceVolume = 0.45f;

	[Tooltip( "How many times each ambience clip plays before advancing to the next. 1 = play once then switch." )]
	[Min( 1 )]
	public int ambiencePlaysPerTrack = 1;

	[Tooltip( "Crossfade seconds when switching ambience tracks. 0 = hard cut. Overlaps the end of the last play of the current track." )]
	[Min( 0f )]
	[FormerlySerializedAs( "ambienceFadeSeconds" )]
	public float ambienceCrossfadeSeconds;

	[Header( "Footsteps" )]
	[Tooltip( "Random one-shots when walking on regular ground." )]
	public AudioClip[] groundStepClips;

	[Tooltip( "Random one-shots when walking on a gold pile." )]
	public AudioClip[] goldPileStepClips;

	[Tooltip( "Planar distance between footstep sounds while moving." )]
	[Min( 0.05f )]
	public float stepStrideDistance = 1.1f;

	[Tooltip( "Minimum planar heading change in degrees that plays an extra footstep. 90 = left-to-forward, 180 = left-to-right reversal. 0 = disabled." )]
	[Range( 0f, 180f )]
	public float stepTurnDegrees = 90f;

	[Range( 0f, 1f )]
	public float stepVolumeMin = 0.7f;

	[Range( 0f, 1f )]
	public float stepVolumeMax = 0.9f;

	[Range( 0f, 1f )]
	[Tooltip( "Volume range for the footstep played when landing from a jump / fall." )]
	public float landStepVolumeMin = 0.85f;

	[Range( 0f, 1f )]
	public float landStepVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float stepPitchMin = 0.92f;

	[Range( -3f, 3f )]
	public float stepPitchMax = 1.08f;

	[Header( "Jump" )]
	[Tooltip( "Random one-shots when the player jumps. Leave empty until clips are authored." )]
	public AudioClip[] jumpClips;

	[Range( 0f, 1f )]
	public float jumpVolumeMin = 1f;

	[Range( 0f, 1f )]
	public float jumpVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float jumpPitchMin = 1f;

	[Range( -3f, 3f )]
	public float jumpPitchMax = 1f;
}
