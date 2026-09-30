using UnityEngine;
using UnityEngine.AddressableAssets;

[CreateAssetMenu( fileName = "BuildModeDefinition", menuName = "Definitions/BuildModeDefinition" )]
public class BuildModeDefinition : ScriptableObject
{
	[Header( "Ghost Distance Fade" )]
	[Tooltip( "World meters from the buildable (nearest bounds point / shader fragment) where the ghost stays fully visible. Same for all buildables." )]
	[Min( 0.1f )]
	public float fadeStart = 12f;

	[Tooltip( "World meters from the nearest point on the buildable bounds where the ghost fully fades and the GameObject turns off. Same distance for all; large props still activate when you are within this range of any part of their bounds." )]
	[Min( 0.1f )]
	public float fadeEnd = 18f;

	[Header( "Build Hold" )]
	[Tooltip( "Seconds to hold ContextualInteract while aiming a ghost to complete the build." )]
	[Min( 0.1f )]
	public float buildHoldSeconds = 2f;

	[Tooltip( "Max aim ray distance for selecting a build ghost. 0 uses the larger of fadeEnd and the player's interact range." )]
	[Min( 0f )]
	public float aimMaxDistance = 0f;

	[Header( "Ghost Visual" )]
	[Tooltip( "Must be assigned so the ghost shader is included in player builds (Shader.Find is stripped otherwise)." )]
	public Shader ghostShader;

	public Color ghostColor = new Color( 0.35f, 0.75f, 1f, 0.4f );

	[Range( 0.5f, 8f )]
	public float ghostFresnelPower = 2.4f;

	[Range( 0f, 2f )]
	public float ghostFresnelBoost = 0.7f;

	[Range( 0f, 1f )]
	public float ghostPulseAmount = 0.12f;

	[Min( 0f )]
	public float ghostPulseSpeed = 0.85f;

	[Range( 0f, 2f )]
	public float ghostRimIntensity = 1.15f;

	[Range( 0f, 1f )]
	public float ghostCoreIntensity = 0.28f;

	[Header( "Complete Reveal" )]
	[Tooltip( "HDR yellow flash color while the ghost covers the built mesh popping in." )]
	[ColorUsage( true, true )]
	public Color completeGlowColor = new Color( 1.4f, 0.95f, 0.28f, 1f );

	[Tooltip( "Seconds to ramp the ghost to full yellow before / as the built mesh activates." )]
	[Min( 0.05f )]
	public float completeFlashSeconds = 0.18f;

	[Tooltip( "Seconds to hold full yellow after activating the built mesh." )]
	[Min( 0f )]
	public float completeHoldSeconds = 0.08f;

	[Tooltip( "Seconds to fade the yellow ghost out after the hold." )]
	[Min( 0.05f )]
	public float completeFadeSeconds = 0.4f;

	[Header( "Hover Outline" )]
	[Tooltip( "Outline settings when aiming a build ghost. Falls back to pickable gold if unset." )]
	public HoverOutlineVisualSettings buildOutline = HoverOutlineVisualSettings.DefaultPickable();

	[Header( "Build SFX" )]
	[Tooltip( "Optional looping bed while holding to build. Ignored when Build Hit Clips has any entry." )]
	public AudioClip buildStartClip;

	[Tooltip( "Played when a build completes (yellow reveal begins)." )]
	public AudioClip buildCompleteClip;

	[Tooltip( "Impact SFX pool — one random clip plays at each hammer swing hit. When any clip is set, Build Start Clip is not played." )]
	public AudioClip[] buildHitClips;

	[Min( 0f )]
	public float buildStartVolume = 1f;

	[Min( 0f )]
	public float buildCompleteVolume = 1f;

	[Tooltip( "Can exceed 1 for boost." )]
	[Min( 0f )]
	public float buildHitVolumeMin = 1f;

	[Tooltip( "Can exceed 1 for boost." )]
	[Min( 0f )]
	public float buildHitVolumeMax = 1f;

	[Range( -3f, 3f )]
	public float buildHitPitchMin = 0.95f;

	[Range( -3f, 3f )]
	public float buildHitPitchMax = 1.05f;

	public bool HasBuildHitClips
	{
		get
		{
			if ( buildHitClips == null || buildHitClips.Length == 0 )
				return false;
			for ( int i = 0; i < buildHitClips.Length; i++ )
			{
				if ( buildHitClips[ i ] != null )
					return true;
			}

			return false;
		}
	}

	[Header( "Hammer Swing (while building)" )]
	[Tooltip( "Number of hammer swings during a full build hold." )]
	[Min( 1 )]
	public int hammerSwingCount = 3;

	[Tooltip( "Within each swing (0–1), when the strike lands and a build hit clip plays. Raise/lower to sync with your audio." )]
	[Range( 0.05f, 0.95f )]
	public float hammerSwingHitNormalized = 0.55f;

	[Tooltip( "Additive local euler at full wind-up (raised back) relative to the held rest pose." )]
	public Vector3 hammerSwingWindupEuler = new Vector3( -70f, 12f, -18f );

	[Tooltip( "Additive local euler at impact (past rest) relative to the held rest pose." )]
	public Vector3 hammerSwingImpactEuler = new Vector3( 28f, -8f, 22f );

	[Tooltip( "Additive local position at full wind-up." )]
	public Vector3 hammerSwingWindupOffset = new Vector3( 0.02f, 0.08f, -0.06f );

	[Tooltip( "Additive local position at impact." )]
	public Vector3 hammerSwingImpactOffset = new Vector3( 0.04f, -0.05f, 0.08f );

	[Header( "Hammer Tool" )]
	[Tooltip( "Addressable HeldHammer viewmodel shown under the camera in build mode. If empty, a simple procedural hammer is used." )]
	public AssetReferenceGameObject hammerPrefab;

	[Tooltip( "Local offset of the held hammer under CameraMount (FPS bottom-right)." )]
	public Vector3 hammerLocalOffset = new Vector3( 0.32f, -0.28f, 0.45f );

	[Tooltip( "Local euler degrees for the held hammer." )]
	public Vector3 hammerLocalEuler = new Vector3( 8f, 25f, -15f );

	[Tooltip( "Uniform scale for the held hammer viewmodel." )]
	[Min( 0.01f )]
	public float hammerLocalScale = 0.45f;

	[Header( "Hammer Draw / Holster" )]
	[Tooltip( "Seconds to animate the hammer in when entering build mode." )]
	[Min( 0.05f )]
	public float hammerDrawSeconds = 0.42f;

	[Tooltip( "Seconds to animate the hammer out when exiting build mode." )]
	[Min( 0.05f )]
	public float hammerHolsterSeconds = 0.22f;

	[Tooltip( "Extra local offset at the start of draw / end of holster (camera-local)." )]
	public Vector3 hammerHolsterOffset = new Vector3( 0.12f, -0.35f, -0.15f );

	[Tooltip( "Local euler at the start of draw / end of holster." )]
	public Vector3 hammerHolsterEuler = new Vector3( 40f, 35f, -25f );

	[Tooltip( "Peak camera-local upward toss during the draw (0 at start/end)." )]
	[Min( 0f )]
	public float hammerDrawTossHeight = 0.14f;

	[Tooltip( "Full end-over-end revolutions during the draw (settles to rest at the end)." )]
	[Min( 0f )]
	public float hammerDrawSpinRevolutions = 1.15f;

	[Tooltip( "Local axis the hammer spins around during draw (normalized at runtime)." )]
	public Vector3 hammerDrawSpinAxis = new Vector3( 1f, 0.15f, 0.35f );

	[Tooltip( "Seconds to fly the world hammer into the hand when picking it up." )]
	[Min( 0.05f )]
	public float hammerWorldPickupSeconds = 0.45f;

	[Tooltip( "World-space arc height while the world hammer flies into the hand." )]
	[Min( 0f )]
	public float hammerWorldPickupArcHeight = 0.35f;

	[Tooltip( "Slight camera-local upward bump mid-holster (no spin)." )]
	[Min( 0f )]
	public float hammerHolsterBumpHeight = 0.06f;

	void OnValidate()
	{
		fadeStart = Mathf.Max( 0.1f, fadeStart );
		fadeEnd = Mathf.Max( fadeStart + 0.01f, fadeEnd );
		buildHoldSeconds = Mathf.Max( 0.1f, buildHoldSeconds );
		aimMaxDistance = Mathf.Max( 0f, aimMaxDistance );
		ghostPulseSpeed = Mathf.Max( 0f, ghostPulseSpeed );
		completeFlashSeconds = Mathf.Max( 0.05f, completeFlashSeconds );
		completeHoldSeconds = Mathf.Max( 0f, completeHoldSeconds );
		completeFadeSeconds = Mathf.Max( 0.05f, completeFadeSeconds );
		if ( buildOutline != null )
			buildOutline.Validate();
		else
			buildOutline = HoverOutlineVisualSettings.DefaultPickable();
		buildStartVolume = Mathf.Max( 0f, buildStartVolume );
		buildCompleteVolume = Mathf.Max( 0f, buildCompleteVolume );
		buildHitVolumeMin = Mathf.Max( 0f, buildHitVolumeMin );
		buildHitVolumeMax = Mathf.Max( buildHitVolumeMin, buildHitVolumeMax );
		buildHitPitchMin = Mathf.Clamp( buildHitPitchMin, -3f, 3f );
		buildHitPitchMax = Mathf.Clamp( Mathf.Max( buildHitPitchMin, buildHitPitchMax ), -3f, 3f );
		hammerSwingCount = Mathf.Max( 1, hammerSwingCount );
		hammerSwingHitNormalized = Mathf.Clamp( hammerSwingHitNormalized, 0.05f, 0.95f );
		hammerLocalScale = Mathf.Max( 0.01f, hammerLocalScale );
		hammerDrawSeconds = Mathf.Max( 0.05f, hammerDrawSeconds );
		hammerHolsterSeconds = Mathf.Max( 0.05f, hammerHolsterSeconds );
		hammerDrawTossHeight = Mathf.Max( 0f, hammerDrawTossHeight );
		hammerDrawSpinRevolutions = Mathf.Max( 0f, hammerDrawSpinRevolutions );
		hammerWorldPickupSeconds = Mathf.Max( 0.05f, hammerWorldPickupSeconds );
		hammerWorldPickupArcHeight = Mathf.Max( 0f, hammerWorldPickupArcHeight );
		hammerHolsterBumpHeight = Mathf.Max( 0f, hammerHolsterBumpHeight );
		if ( hammerDrawSpinAxis.sqrMagnitude < 0.0001f )
			hammerDrawSpinAxis = new Vector3( 1f, 0.15f, 0.35f );
	}
}
