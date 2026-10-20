using UnityEngine;

/// <summary>
/// Red/Green control on a minecart station: stop all automation, or resume and immediate-send.
/// Visual brightness reflects whether global automation is currently running.
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
	const float DimEmissionIntensity = 0.35f;
	const float ActiveEmissionIntensity = 1.25f;
	const float HoverEmissionIntensity = 1.55f;
	const float HoverBrightness = 1.18f;
	const float PulseSpeed = 2.4f;
	const float PulseMin = 0.85f;
	const float PulseMax = 1.45f;

	[SerializeField]
	ButtonMode mode = ButtonMode.Stop;

	[SerializeField]
	MinecartStationBase station;

	Renderer _renderer;
	MaterialPropertyBlock _block;
	bool _hovered;
	bool _lastAutomationEnabled = true;

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
		_lastAutomationEnabled = MinecartStationBase.AutomationEnabled;
		ApplyButtonVisual( false );
	}

	void OnValidate()
	{
		ApplyModeDefaults();
	}

	void Update()
	{
		bool hovered = IsHoveredByPlayer();
		bool automationEnabled = MinecartStationBase.AutomationEnabled;
		bool needsPulse = mode == ButtonMode.ResumeAndSend && !automationEnabled;

		if ( hovered == _hovered && automationEnabled == _lastAutomationEnabled && !needsPulse )
			return;

		_hovered = hovered;
		_lastAutomationEnabled = automationEnabled;
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

	bool IsStatusActive()
	{
		bool automationEnabled = MinecartStationBase.AutomationEnabled;
		if ( mode == ButtonMode.Stop )
			return !automationEnabled;

		return automationEnabled;
	}

	void ApplyButtonVisual( bool hovered )
	{
		if ( _renderer == null )
			_renderer = GetComponent<Renderer>();
		if ( _renderer == null )
			return;

		if ( _block == null )
			_block = new MaterialPropertyBlock();

		bool automationEnabled = MinecartStationBase.AutomationEnabled;
		bool statusActive = IsStatusActive();

		Color color = mode == ButtonMode.Stop ? StopColor : ResumeColor;
		float intensity;

		if ( hovered && CanInteractForAutomation( automationEnabled ) )
		{
			color = Color.Lerp( color, Color.white, 0.35f ) * HoverBrightness;
			intensity = HoverEmissionIntensity;
		}
		else if ( mode == ButtonMode.ResumeAndSend && !automationEnabled )
		{
			float pulse = Mathf.Lerp( PulseMin, PulseMax, 0.5f + 0.5f * Mathf.Sin( Time.time * PulseSpeed ) );
			intensity = pulse;
		}
		else if ( statusActive )
		{
			intensity = ActiveEmissionIntensity;
		}
		else
		{
			color *= 0.45f;
			intensity = DimEmissionIntensity;
		}

		color.a = 1f;

		_renderer.GetPropertyBlock( _block );
		_block.SetColor( BaseColorId, color );
		_block.SetColor( ColorId, color );
		_block.SetColor( EmissionColorId, color );
		_block.SetFloat( EmissionIntensityId, intensity );
		_renderer.SetPropertyBlock( _block );
	}

	bool CanInteractForAutomation( bool automationEnabled )
	{
		if ( mode == ButtonMode.Stop )
			return automationEnabled;

		return !automationEnabled;
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null || station == null )
			return false;

		return CanInteractForAutomation( MinecartStationBase.AutomationEnabled );
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
