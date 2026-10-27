using FeedbackSystem;

using UnityEngine;

/// <summary>
/// Complete-display FX root: tints an EnvironmentLit mesh (emission) and a SoftShaft mesh (color).
/// Place on the CompleteEffect object under a coin or artifact display.
/// On show (play mode): flashes CompleteGlow exposure and plays reveal Feedbacks (burst + looping motes).
/// Optional one-shot mode: quick alpha-in at peak glow, then slow fade-out (door unlock, etc.).
/// </summary>
[DisallowMultipleComponent]
public class DisplayCompleteEffect : MonoBehaviour
{
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int ColorId = Shader.PropertyToID( "_Color" );

	[Tooltip( "Mesh using DragonLoot/EnvironmentLit — Setup writes _EmissionColor." )]
	[SerializeField]
	MeshRenderer litRenderer;

	[Tooltip( "Mesh using DragonLoot/SoftShaft — Setup writes _Color." )]
	[SerializeField]
	MeshRenderer softShaftRenderer;

	[Tooltip( "Optional. When unset, resolved from Soft Shaft Renderer." )]
	[SerializeField]
	CompleteGlowVolume glowVolume;

	[Tooltip( "Optional. Played on show (burst + looping motes); stopped on hide." )]
	[SerializeField]
	Feedbacks revealFeedbacks;

	[SerializeField]
	[ColorUsage( true, true )]
	Color defaultColor = new Color( 1f, 0.72f, 0.28f, 1f );

	[Tooltip( "Scales lit-mesh emission only. SoftShaft / CompleteGlow use the full Setup color." )]
	[SerializeField]
	[Min( 0f )]
	float litEmissionMult = 1f;

	[Header( "One-Shot Fade" )]
	[Tooltip( "If true, alpha fades in quickly at peak glow, then fades out slowly and ends at 0. Coin displays leave this off so the glow stays." )]
	[SerializeField]
	bool fadeOutAfterShow;

	[SerializeField]
	[Min( 0.01f )]
	float fadeInDuration = 0.1f;

	[SerializeField]
	[Min( 0.01f )]
	float fadeOutDuration = 1.5f;

	MaterialPropertyBlock _block;
	Color _appliedColor;
	bool _hasAppliedColor;

	public MeshRenderer LitRenderer => litRenderer;
	public MeshRenderer SoftShaftRenderer => softShaftRenderer;
	public CompleteGlowVolume GlowVolume => glowVolume;
	public Feedbacks RevealFeedbacks => revealFeedbacks;
	public Color DefaultColor => defaultColor;
	public bool FadeOutAfterShow => fadeOutAfterShow;

	/// <summary>Applies <paramref name="color"/> to lit emission and CompleteGlow / SoftShaft tint.</summary>
	public void Setup( Color color )
	{
		_appliedColor = color;
		_hasAppliedColor = true;
		ApplyColor( color );
	}

	public float ResolveShowDuration()
	{
		CompleteGlowVolume volume = ResolveGlowVolume();
		if ( fadeOutAfterShow )
		{
			if ( volume != null )
				return volume.ResolveRevealFadeTotalDuration( fadeInDuration, fadeOutDuration );
			return Mathf.Max( 0.01f, fadeInDuration ) + Mathf.Max( 0.01f, fadeOutDuration );
		}

		if ( volume != null && volume.Settings != null )
			return Mathf.Max( 0.05f, volume.Settings.FlashDuration ) + 0.15f;

		return 0.75f;
	}

#if UNITY_EDITOR
	public void EditorSetFadeOutAfterShow( bool enabled, float fadeIn = 0.1f, float fadeOut = 1.5f )
	{
		fadeOutAfterShow = enabled;
		fadeInDuration = Mathf.Max( 0.01f, fadeIn );
		fadeOutDuration = Mathf.Max( 0.01f, fadeOut );
	}
#endif

	void Awake()
	{
		if ( !_hasAppliedColor )
			Setup( defaultColor );
	}

	void OnEnable()
	{
		if ( _hasAppliedColor )
			ApplyColor( _appliedColor );

		if ( !Application.isPlaying )
			return;

		CompleteGlowVolume volume = ResolveGlowVolume();
		if ( volume != null )
		{
			if ( fadeOutAfterShow )
				volume.PlayRevealFadeOut( fadeInDuration, fadeOutDuration );
			else
				volume.PlayExposureFlash();
		}

		if ( revealFeedbacks != null )
			revealFeedbacks.Play();
	}

	void OnDisable()
	{
		if ( !Application.isPlaying )
			return;

		if ( revealFeedbacks != null )
			revealFeedbacks.Stop();

		CompleteGlowVolume volume = ResolveGlowVolume();
		if ( volume != null )
		{
			volume.CancelExposureFlash();
			// Keep alpha at 0 — restoring resting appearance would flash full brightness on hide.
			volume.CancelRevealFadeOut( false );
		}
	}

	void OnValidate()
	{
		litEmissionMult = Mathf.Max( 0f, litEmissionMult );
		fadeInDuration = Mathf.Max( 0.01f, fadeInDuration );
		fadeOutDuration = Mathf.Max( 0.01f, fadeOutDuration );
		if ( _hasAppliedColor )
			ApplyColor( _appliedColor );
		else
			ApplyColor( defaultColor );
	}

	CompleteGlowVolume ResolveGlowVolume()
	{
		if ( glowVolume != null )
			return glowVolume;

		if ( softShaftRenderer != null )
			glowVolume = softShaftRenderer.GetComponent<CompleteGlowVolume>();

		return glowVolume;
	}

	void EnsureBlock()
	{
		if ( _block == null )
			_block = new MaterialPropertyBlock();
	}

	void ApplyColor( Color color )
	{
		EnsureBlock();

		float litMult = Mathf.Max( 0f, litEmissionMult );
		Color litColor = new Color( color.r * litMult, color.g * litMult, color.b * litMult, color.a );
		ApplyEmission( litRenderer, litColor );

		CompleteGlowVolume volume = ResolveGlowVolume();
		if ( volume != null )
			volume.SetColor( color );
		else
			ApplyShaftColor( softShaftRenderer, color );
	}

	void ApplyEmission( MeshRenderer renderer, Color color )
	{
		if ( renderer == null )
			return;

		renderer.GetPropertyBlock( _block );
		_block.SetColor( EmissionColorId, color );
		renderer.SetPropertyBlock( _block );
	}

	void ApplyShaftColor( MeshRenderer renderer, Color color )
	{
		if ( renderer == null )
			return;

		renderer.GetPropertyBlock( _block );
		_block.SetColor( ColorId, color );
		renderer.SetPropertyBlock( _block );
	}
}
