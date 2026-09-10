using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Left-dock IMGUI debug window (not game UI). Toggle with backtick.
/// Also hosts Num± sensitivity hotkeys. Escape closes the panel when open.
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

	const string FoldoutPrefsPrefix = "DragonLoot.Debug.Foldout.";
	const string VSyncPrefsKey = "DragonLoot.Debug.VSync";
	const string ShowFpsPrefsKey = "DragonLoot.Debug.ShowFps";
	const float FpsSmooth = 0.15f;
	const float FpsRedAt = 60f;
	const float FpsGreenAt = 120f;
	const int FpsHistorySize = 180;

	readonly DebugSettingsSection _settings = new DebugSettingsSection();
	readonly List<DebugOverlaySection> _sections = new List<DebugOverlaySection>();
	readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

	bool _open;
	bool _restartInProgress;
	bool _showFps;
	Vector2 _scroll;
	float _overlayHideTime = -1f;
	float _displayedSensitivity;
	float _smoothedFps;
	float _frameMs;
	float _minFps;
	float _maxFps;
	float _avgFps;
	readonly float[] _fpsHistory = new float[ FpsHistorySize ];
	int _fpsHistoryCount;
	int _fpsHistoryIndex;
	GUIStyle _fpsStyle;
	GUIStyle _fpsUnitStyle;
	GUIStyle _fpsStatStyle;

	public static bool IsOpen => s_instance != null && s_instance._open;

	public static bool ShowFps => s_instance != null && s_instance._showFps;

	void Awake()
	{
		s_instance = this;
		ApplyVSync( PlayerPrefs.GetInt( VSyncPrefsKey, 1 ) != 0, persist: false );
		_showFps = PlayerPrefs.GetInt( ShowFpsPrefsKey, 0 ) != 0;
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
		_sections.Add( new DebugCoinHallSection() );
		_sections.Add( new DebugUpgradesSection() );
		_sections.Add( new DebugWorldEventsSection() );
		_sections.Add( new DebugTutorialsSection() );
		_sections.Add( new DebugToastsSection() );
		_sections.Add( new DebugCoinSortingStationSection() );
		_sections.Add( new DebugCarrySection() );
		_sections.Add( new DebugArtifactCleaningSection() );
		_sections.Add( new DebugGroundCoinStackSection() );
		_sections.Add( new DebugInteractionSection() );
		_sections.Add( new DebugGoldPileSection() );
		_sections.Add( new DebugTreasureSurfaceSection() );
		_sections.Add( new DebugMinecartsSection() );
		_sections.Add( new DebugMapSection() );
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
		if ( _showFps )
			SampleFps();

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
	}

	void OnGUI()
	{
		if ( _open )
			DrawPanel();

		DrawFps();
		DrawSensitivityFlash();
	}

	void SampleFps()
	{
		float dt = Time.unscaledDeltaTime;
		_frameMs = dt * 1000f;
		float instant = dt > 0.0001f ? 1f / dt : 0f;
		_smoothedFps = _smoothedFps <= 0f ? instant : Mathf.Lerp( _smoothedFps, instant, FpsSmooth );

		_fpsHistory[ _fpsHistoryIndex ] = instant;
		_fpsHistoryIndex = ( _fpsHistoryIndex + 1 ) % FpsHistorySize;
		if ( _fpsHistoryCount < FpsHistorySize )
			_fpsHistoryCount++;

		float min = instant;
		float max = instant;
		float sum = 0f;
		for ( int i = 0; i < _fpsHistoryCount; i++ )
		{
			float sample = _fpsHistory[ i ];
			if ( sample < min )
				min = sample;
			if ( sample > max )
				max = sample;
			sum += sample;
		}

		_minFps = min;
		_maxFps = max;
		_avgFps = _fpsHistoryCount > 0 ? sum / _fpsHistoryCount : instant;
	}

	void ResetFpsHistory()
	{
		_smoothedFps = 0f;
		_frameMs = 0f;
		_minFps = 0f;
		_maxFps = 0f;
		_avgFps = 0f;
		_fpsHistoryCount = 0;
		_fpsHistoryIndex = 0;
	}

	static Color ColorForFps( float fps )
	{
		float t = Mathf.InverseLerp( FpsRedAt, FpsGreenAt, fps );
		return Color.Lerp( new Color( 1f, 0.18f, 0.12f, 1f ), new Color( 0.15f, 1f, 0.2f, 1f ), t );
	}

	void DrawFps()
	{
		if ( !_showFps )
			return;

		if ( _fpsStyle == null )
		{
			_fpsStyle = new GUIStyle( GUI.skin.label );
			_fpsStyle.fontSize = 18;
			_fpsStyle.fontStyle = FontStyle.Bold;
			_fpsStyle.alignment = TextAnchor.MiddleRight;
			_fpsStyle.normal.textColor = Color.white;

			_fpsUnitStyle = new GUIStyle( GUI.skin.label );
			_fpsUnitStyle.fontSize = 12;
			_fpsUnitStyle.alignment = TextAnchor.MiddleLeft;
			_fpsUnitStyle.normal.textColor = Color.white;

			_fpsStatStyle = new GUIStyle( GUI.skin.label );
			_fpsStatStyle.fontSize = 12;
			_fpsStatStyle.alignment = TextAnchor.UpperRight;
			_fpsStatStyle.normal.textColor = Color.white;
		}

		const float width = 140f;
		const float height = 78f;
		const float labelW = 36f;
		Rect box = new Rect( Screen.width - width - PanelPad, PanelPad, width, height );

		Color prevGui = GUI.color;
		GUI.color = new Color( 0f, 0f, 0f, 0.55f );
		GUI.Box( box, GUIContent.none );
		GUI.color = prevGui;

		Color fpsColor = ColorForFps( _smoothedFps );
		float innerX = box.x + 8f;
		float innerW = box.width - 16f;
		float valueX = innerX + labelW;
		float valueW = innerW - labelW;

		DrawFpsRow( innerX, box.y + 4f, labelW, valueX, valueW, 22f, "FPS", _smoothedFps.ToString( "0" ), fpsColor );
		DrawFpsRow( innerX, box.y + 26f, labelW, valueX, valueW, 22f, "ms", _frameMs.ToString( "0.0" ), fpsColor );

		string stats = "min " + _minFps.ToString( "0" ) + "   avg " + _avgFps.ToString( "0" ) + "   max " + _maxFps.ToString( "0" );
		DrawFpsLabel( new Rect( innerX, box.y + 52f, innerW, 20f ), stats, _fpsStatStyle, fpsColor );
	}

	void DrawFpsRow( float labelX, float y, float labelW, float valueX, float valueW, float height, string unit, string value, Color color )
	{
		DrawFpsLabel( new Rect( labelX, y, labelW, height ), unit, _fpsUnitStyle, color );
		DrawFpsLabel( new Rect( valueX, y, valueW, height ), value, _fpsStyle, color );
	}

	static void DrawFpsLabel( Rect rect, string text, GUIStyle style, Color color )
	{
		Color old = style.normal.textColor;
		style.normal.textColor = Color.black;
		GUI.Label( new Rect( rect.x + 1f, rect.y + 1f, rect.width, rect.height ), text, style );
		style.normal.textColor = color;
		GUI.Label( rect, text, style );
		style.normal.textColor = old;
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

	public static void SetVSyncEnabled( bool enabled )
	{
		ApplyVSync( enabled, persist: true );
	}

	public static void SetShowFps( bool show )
	{
		if ( s_instance != null )
		{
			s_instance._showFps = show;
			if ( show )
				s_instance.ResetFpsHistory();
		}

		PlayerPrefs.SetInt( ShowFpsPrefsKey, show ? 1 : 0 );
		PlayerPrefs.Save();
	}

	static void ApplyVSync( bool enabled, bool persist )
	{
		QualitySettings.vSyncCount = enabled ? 1 : 0;
		if ( !enabled )
			Application.targetFrameRate = -1;

		if ( !persist )
			return;

		PlayerPrefs.SetInt( VSyncPrefsKey, enabled ? 1 : 0 );
		PlayerPrefs.Save();
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
		else if ( !PauseMenuUI.IsOpen && !MapUI.IsOpen )
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
