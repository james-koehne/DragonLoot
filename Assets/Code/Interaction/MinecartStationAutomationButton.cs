using UnityEngine;

/// <summary>
/// Station control button: stop all automation, resume automation, or go/call (send docked cart / call cart).
/// Blocked presses show a mid-screen hint via <see cref="PouchFullHintUI"/> (same as pouch-full).
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class MinecartStationAutomationButton : InteractableBase
{
	public enum ButtonMode
	{
		Stop = 0,
		Resume = 1,
		GoNext = 2
	}

	static readonly int BaseColorId = Shader.PropertyToID( "_BaseColor" );
	static readonly int ColorId = Shader.PropertyToID( "_Color" );
	static readonly int EmissionColorId = Shader.PropertyToID( "_EmissionColor" );
	static readonly int EmissionIntensityId = Shader.PropertyToID( "_EmissionIntensity" );
	static readonly Color StopColor = new Color( 0.85f, 0.12f, 0.12f, 1f );
	static readonly Color ResumeColor = new Color( 0.15f, 0.75f, 0.22f, 1f );
	static readonly Color GoNextColor = new Color( 0.15f, 0.55f, 0.95f, 1f );
	const float DimEmissionIntensity = 0.35f;
	const float ActiveEmissionIntensity = 1.25f;
	const float HoverEmissionIntensity = 1.55f;
	const float HoverBrightness = 1.18f;

	[SerializeField]
	ButtonMode mode = ButtonMode.Stop;

	[SerializeField]
	MinecartStationBase station;

	Renderer _renderer;
	MaterialPropertyBlock _block;
	bool _hovered;
	bool _lastAutomationEnabled = true;
	bool _lastActionReady;
	string _lastPrompt;

	public ButtonMode Mode => mode;

	void Reset()
	{
		RefreshInteractionPrompt();
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<MinecartStationBase>();

		_renderer = GetComponent<Renderer>();
		RefreshInteractionPrompt();
		_lastAutomationEnabled = MinecartStationBase.AutomationEnabled;
		_lastActionReady = IsActionReady();
		_lastPrompt = InteractionName;
		ApplyButtonVisual( false );
	}

	void OnValidate()
	{
		RefreshInteractionPrompt();
	}

	void Update()
	{
		RefreshInteractionPrompt();

		bool hovered = IsHoveredByPlayer();
		bool automationEnabled = MinecartStationBase.AutomationEnabled;
		bool actionReady = IsActionReady();
		string prompt = InteractionName;

		if ( hovered == _hovered
		     && automationEnabled == _lastAutomationEnabled
		     && actionReady == _lastActionReady
		     && prompt == _lastPrompt )
			return;

		_hovered = hovered;
		_lastAutomationEnabled = automationEnabled;
		_lastActionReady = actionReady;
		_lastPrompt = prompt;
		ApplyButtonVisual( _hovered );
	}

	void RefreshInteractionPrompt()
	{
		if ( mode == ButtonMode.Stop )
		{
			SetInteractionName( "Stop Automation" );
			return;
		}

		if ( mode == ButtonMode.Resume )
		{
			SetInteractionName( "Resume Automation" );
			return;
		}

		if ( station == null )
		{
			SetInteractionName( "Send to Next Station" );
			return;
		}

		// Always show the intended action on hover; blocked reasons appear mid-screen on press.
		SetInteractionName( station.IsCartDocked ? "Send to Next Station" : "Call Cart" );
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

	bool IsActionReady()
	{
		if ( mode == ButtonMode.Stop )
			return MinecartStationBase.AutomationEnabled;
		if ( mode == ButtonMode.Resume )
			return !MinecartStationBase.AutomationEnabled;
		if ( station == null )
			return false;
		return station.TryGetGoCallState( out _, out _ );
	}

	bool IsStatusActive()
	{
		bool automationEnabled = MinecartStationBase.AutomationEnabled;
		if ( mode == ButtonMode.Stop )
			return !automationEnabled;
		if ( mode == ButtonMode.Resume )
			return automationEnabled;
		return IsActionReady();
	}

	void ApplyButtonVisual( bool hovered )
	{
		if ( _renderer == null )
			_renderer = GetComponent<Renderer>();
		if ( _renderer == null )
			return;

		if ( _block == null )
			_block = new MaterialPropertyBlock();

		bool actionReady = IsActionReady();
		bool statusActive = IsStatusActive();

		Color color = ResolveModeColor();
		float intensity;

		if ( hovered )
		{
			color = Color.Lerp( color, Color.white, actionReady ? 0.35f : 0.18f ) * HoverBrightness;
			intensity = actionReady ? HoverEmissionIntensity : ActiveEmissionIntensity;
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

	Color ResolveModeColor()
	{
		if ( mode == ButtonMode.Stop )
			return StopColor;
		if ( mode == ButtonMode.Resume )
			return ResumeColor;
		return GoNextColor;
	}

	public override bool CanInteract( PlayerController player )
	{
		// Always allow press so blocked actions can show a mid-screen error hint.
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
			if ( !MinecartStationBase.AutomationEnabled )
			{
				NotifyBlocked( "Automation already stopped" );
				return;
			}

			MinecartStationBase.StopAllAutomation();
			return;
		}

		if ( mode == ButtonMode.Resume )
		{
			if ( MinecartStationBase.AutomationEnabled )
			{
				NotifyBlocked( "Automation already running" );
				return;
			}

			MinecartStationBase.ResumeAutomation();
			return;
		}

		string failurePrompt;
		if ( station.TryPerformGoCallAction( out failurePrompt ) )
			return;

		NotifyBlocked( string.IsNullOrEmpty( failurePrompt ) ? "Cannot use station control" : failurePrompt );
	}

	static void NotifyBlocked( string message )
	{
		PouchFullHintUI.Notify( message );
	}

	public void EditorSetMode( ButtonMode value )
	{
		mode = value;
		RefreshInteractionPrompt();
	}

	public void EditorSetStation( MinecartStationBase value )
	{
		station = value;
	}
}
