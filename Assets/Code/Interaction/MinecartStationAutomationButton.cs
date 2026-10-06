using UnityEngine;

/// <summary>
/// Red/Green control on a minecart station: stop all automation, or resume and immediate-send.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class MinecartStationAutomationButton : InteractableBase
{
	public enum ButtonMode
	{
		Stop = 0,
		ResumeAndSend = 1
	}

	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int ColorId = Shader.PropertyToID( "_Color" );
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int EmissionIntensityId = Shader.PropertyToID( "_EmissionIntensity" );
	static readonly Color StopColor = new Color( 0.85f, 0.12f, 0.12f, 1f );
	static readonly Color ResumeColor = new Color( 0.15f, 0.75f, 0.22f, 1f );
	const float IdleEmissionIntensity = 1.1f;
	const float HoverEmissionIntensity = 1.55f;
	const float HoverBrightness = 1.18f;

	[SerializeField]
	ButtonMode mode = ButtonMode.Stop;

	[SerializeField]
	MinecartStationBase station;

	Renderer _renderer;
	MaterialPropertyBlock _block;
	bool _hovered;

	void Reset()
	{
		ApplyModeDefaults();
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<MinecartStationBase>();

		_renderer = GetComponent<Renderer>();
		ApplyModeDefaults();
		ApplyButtonVisual( false );
	}

	void OnValidate()
	{
		ApplyModeDefaults();
	}

	void Update()
	{
		bool hovered = IsHoveredByPlayer();
		if ( hovered == _hovered )
			return;

		_hovered = hovered;
		ApplyButtonVisual( _hovered );
	}

	void ApplyModeDefaults()
	{
		if ( mode == ButtonMode.Stop )
			SetInteractionName( "Stop Automation" );
		else
			SetInteractionName( "Resume & Send" );
	}

	bool IsHoveredByPlayer()
	{
		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return false;

		PlayerInteraction interaction = GameMode.Instance.Player.Interaction;
		if ( interaction == null )
			return false;

		return interaction.Current == (IInteractable)this;
	}

	void ApplyButtonVisual( bool hovered )
	{
		if ( _renderer == null )
			_renderer = GetComponent<Renderer>();
		if ( _renderer == null )
			return;

		if ( _block == null )
			_block = new MaterialPropertyBlock();

		Color color = mode == ButtonMode.Stop ? StopColor : ResumeColor;
		if ( hovered )
			color = Color.Lerp( color, Color.white, 0.35f ) * HoverBrightness;

		color.a = 1f;
		float intensity = hovered ? HoverEmissionIntensity : IdleEmissionIntensity;

		_renderer.GetPropertyBlock( _block );
		_block.SetColor( BaseColorId, color );
		_block.SetColor( ColorId, color );
		_block.SetColor( EmissionColorId, color );
		_block.SetFloat( EmissionIntensityId, intensity );
		_renderer.SetPropertyBlock( _block );
	}

	public override bool CanInteract( PlayerController player )
	{
		return IsAvailable && player != null && station != null;
	}

	public override void Interact( PlayerController player )
	{
		if ( station == null )
			station = GetComponentInParent<MinecartStationBase>();
		if ( station == null )
			return;

		if ( mode == ButtonMode.Stop )
		{
			MinecartStationBase.StopAllAutomation();
			return;
		}

		MinecartStationBase.ResumeAutomation();
		station.RequestImmediateSend();
	}

	public void EditorSetMode( ButtonMode value )
	{
		mode = value;
		ApplyModeDefaults();
	}

	public void EditorSetStation( MinecartStationBase value )
	{
		station = value;
	}
}
