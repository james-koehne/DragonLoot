using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Left-dock IMGUI debug window (not game UI). Toggle with backtick.
/// Also hosts Num± sensitivity and R reload hotkeys. Escape closes the panel when open.
/// </summary>
[DisallowMultipleComponent]
public class DebugOverlay : MonoBehaviour
{
	public const float PanelWidth = 300f;

	const float SensitivityStep = 0.1f;
	const float OverlayDuration = 1.5f;
	const float PanelPad = 8f;

	static DebugOverlay s_instance;

	[SerializeField]
	Key toggleKey = Key.Backquote;

	[SerializeField]
	Key increaseSensitivityKey = Key.NumpadPlus;

	[SerializeField]
	Key decreaseSensitivityKey = Key.NumpadMinus;

	[SerializeField]
	Key quitKey = Key.Escape;

	[SerializeField]
	Key reloadKey = Key.R;

	const string FoldoutPrefsPrefix = "DragonLoot.Debug.Foldout.";

	readonly DebugSettingsSection _settings = new DebugSettingsSection();
	readonly List<DebugOverlaySection> _sections = new List<DebugOverlaySection>();
	readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

	bool _open;
	bool _restartInProgress;
	Vector2 _scroll;
	float _overlayHideTime = -1f;
	float _displayedSensitivity;

	public static bool IsOpen => s_instance != null && s_instance._open;

	void Awake()
	{
		s_instance = this;
		BuildSections();
	}

	void OnDestroy()
	{
		if ( s_instance == this )
			s_instance = null;

		if ( _open )
			SetOpen( false );

		AudioMaster.FlushPersist();
	}

	void BuildSections()
	{
		_sections.Clear();
		_sections.Add( new DebugSessionSection( this ) );
		_sections.Add( new DebugCameraSection() );
		_sections.Add( new DebugPlayerSection() );
		_sections.Add( new DebugAbilitiesSection() );
		_sections.Add( new DebugChestsSection() );
		_sections.Add( new DebugDoorsSection() );
		_sections.Add( new DebugUpgradesSection() );
		_sections.Add( new DebugWorldEventsSection() );
		_sections.Add( new DebugTutorialsSection() );
		_sections.Add( new DebugCoinSortingStationSection() );
		_sections.Add( new DebugCarrySection() );
		_sections.Add( new DebugArtifactCleaningSection() );
		_sections.Add( new DebugGroundCoinStackSection() );
		_sections.Add( new DebugInteractionSection() );
		_sections.Add( new DebugGoldPileSection() );
		_sections.Add( new DebugTreasureSurfaceSection() );
		_sections.Add( new DebugLoggingSection() );

		for ( int i = 0; i < _sections.Count; i++ )
		{
			string title = _sections[ i ].Title;
			if ( !_foldouts.ContainsKey( title ) )
				_foldouts[ title ] = LoadFoldoutOpen( title, defaultOpen: true );
		}
	}

	static string FoldoutPrefsKey( string title )
	{
		return FoldoutPrefsPrefix + title;
	}

	static bool LoadFoldoutOpen( string title, bool defaultOpen )
	{
		return PlayerPrefs.GetInt( FoldoutPrefsKey( title ), defaultOpen ? 1 : 0 ) != 0;
	}

	static void SaveFoldoutOpen( string title, bool open )
	{
		PlayerPrefs.SetInt( FoldoutPrefsKey( title ), open ? 1 : 0 );
		PlayerPrefs.Save();
	}

	void Update()
	{
		Keyboard keyboard = Keyboard.current;
		if ( keyboard == null )
			return;

		if ( keyboard[ toggleKey ].wasPressedThisFrame )
			SetOpen( !_open );

		if ( keyboard[ increaseSensitivityKey ].wasPressedThisFrame )
			AdjustSensitivity( SensitivityStep );

		if ( keyboard[ decreaseSensitivityKey ].wasPressedThisFrame )
			AdjustSensitivity( -SensitivityStep );

		if ( keyboard[ quitKey ].wasPressedThisFrame )
		{
			// Escape closes the debug panel when open. Game pause / quit is owned by PauseMenuUI.
			if ( _open )
				SetOpen( false );
		}

		if ( keyboard[ reloadKey ].wasPressedThisFrame )
			ReloadGame();
	}

	void OnGUI()
	{
		if ( _open )
			DrawPanel();

		DrawSensitivityFlash();
	}

	void DrawPanel()
	{
		Rect panel = new Rect( PanelPad, PanelPad, PanelWidth, Screen.height - PanelPad * 2f );
		GUI.Box( panel, GUIContent.none );

		GUILayout.BeginArea( new Rect( panel.x + 6f, panel.y + 6f, panel.width - 12f, panel.height - 12f ) );
		GUILayout.Label( "DragonLoot Debug" );
		GUILayout.Label( "` to close" );
		GUILayout.Space( 4f );

		GUILayout.BeginVertical( GUI.skin.box );
		GUILayout.Label( _settings.Title );
		_settings.Draw();
		GUILayout.EndVertical();
		GUILayout.Space( 4f );

		_scroll = GUILayout.BeginScrollView( _scroll );
		for ( int i = 0; i < _sections.Count; i++ )
		{
			DebugOverlaySection section = _sections[ i ];
			string title = section.Title;
			bool open = true;
			_foldouts.TryGetValue( title, out open );
			string foldLabel = ( open ? "▼ " : "▶ " ) + title;
			bool nextOpen = GUILayout.Toggle( open, foldLabel );
			if ( nextOpen != open )
			{
				open = nextOpen;
				_foldouts[ title ] = open;
				SaveFoldoutOpen( title, open );
			}
			if ( open )
			{
				GUILayout.BeginVertical( GUI.skin.box );
				section.Draw();
				GUILayout.EndVertical();
			}
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	void DrawSensitivityFlash()
	{
		if ( Time.unscaledTime >= _overlayHideTime )
			return;

		float x = PanelPad;
		if ( _open )
			x = PanelWidth + PanelPad * 2f;

		const float width = 220f;
		const float height = 36f;
		Rect r = new Rect( x, PanelPad, width, height );
		GUI.Box( r, GUIContent.none );
		GUILayout.BeginArea( new Rect( x + 6f, PanelPad + 6f, width - 12f, height - 8f ) );
		GUILayout.Label( $"Sensitivity: {_displayedSensitivity:0.00}" );
		GUILayout.EndArea();
	}

	public void SetOpen( bool open )
	{
		if ( _open == open )
			return;

		_open = open;
		if ( !_open )
			AudioMaster.FlushPersist();

		PlayerController player = GetPlayer();

		if ( _open )
		{
			if ( player != null )
				player.SetGameplayInputEnabled( false );
			else
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
		}
		else if ( !PauseMenuUI.IsOpen )
		{
			if ( player != null )
				player.SetGameplayInputEnabled( true );
			else
			{
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
		}
	}

	public void AdjustSensitivity( float delta )
	{
		FirstPersonCameraController cameraLook = GetCameraLook();
		if ( cameraLook == null )
			return;

		_displayedSensitivity = cameraLook.AdjustLookSensitivity( delta );
		_overlayHideTime = Time.unscaledTime + OverlayDuration;
	}

	public void QuitGame()
	{
#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#else
		Application.Quit();
#endif
	}

	public async void ReloadGame()
	{
		if ( _restartInProgress )
			return;

		_restartInProgress = true;
		try
		{
			if ( _open )
				SetOpen( false );
			await GameInstance.RestartGame();
		}
		finally
		{
			_restartInProgress = false;
		}
	}

	public void RespawnPlayer()
	{
		if ( GameMode.Instance == null || GameMode.Instance.Game == null )
			return;
		GameMode.Instance.Game.RespawnPlayer();
	}

	public static PlayerController GetPlayer()
	{
		return GameMode.Instance != null ? GameMode.Instance.Player : null;
	}

	public static FirstPersonCameraController GetCameraLook()
	{
		if ( GameMode.Instance == null || GameMode.Instance.cameraController == null )
			return null;
		return GameMode.Instance.cameraController.FirstPerson;
	}
}
