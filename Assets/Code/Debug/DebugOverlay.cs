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
	public const float DefaultPanelWidth = 360f;
	const float MinPanelWidth = 280f;
	const float MaxPanelWidth = 480f;
	const float WidthStep = 20f;

	const float SensitivityStep = 0.1f;
	const float OverlayDuration = 1.5f;
	const float PanelPad = 8f;

	const string FoldoutPrefsPrefix = "DragonLoot.Debug.Foldout.";
	const string FavoritePrefsPrefix = "DragonLoot.Debug.Favorite.";
	const string VSyncPrefsKey = "DragonLoot.Debug.VSync";
	const string ShowFpsPrefsKey = "DragonLoot.Debug.ShowFps";
	const string PanelWidthPrefsKey = "DragonLoot.Debug.PanelWidth";
	const string FavoritesOnlyPrefsKey = "DragonLoot.Debug.FavoritesOnly";
	const string RecentTeleportPrefsPrefix = "DragonLoot.Debug.RecentTeleport.";
	const int RecentTeleportSlots = 3;
	const float FpsSmooth = 0.15f;
	const float FpsRedAt = 60f;
	const float FpsGreenAt = 120f;
	const int FpsHistorySize = 180;

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
	Key quickSaveKey = Key.F5;

	[SerializeField]
	Key quickLoadKey = Key.F9;

	readonly DebugSettingsSection _settings = new DebugSettingsSection();
	readonly List<DebugOverlaySection> _sections = new List<DebugOverlaySection>();
	readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
	readonly Dictionary<string, bool> _favorites = new Dictionary<string, bool>();

	bool _open;
	bool _restartInProgress;
	bool _showFps;
	bool _favoritesOnly;
	bool _settingsOpen = true;
	Vector2 _scroll;
	float _panelWidth = DefaultPanelWidth;
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
	string _sectionFilter = string.Empty;
	string _lastAction = string.Empty;
	string _lastExpandedSection = string.Empty;
	bool _wasFiltering;
	GUIStyle _fpsStyle;
	GUIStyle _fpsUnitStyle;
	GUIStyle _fpsStatStyle;

	public static bool IsOpen => s_instance != null && s_instance._open;

	public static bool ShowFps => s_instance != null && s_instance._showFps;

	public static float PanelWidth => s_instance != null ? s_instance._panelWidth : DefaultPanelWidth;

	public static void SetLastAction( string message )
	{
		if ( s_instance == null )
			return;
		s_instance._lastAction = message != null ? message : string.Empty;
	}

	public static void RecordRecentTeleport( string spawnId )
	{
		if ( string.IsNullOrEmpty( spawnId ) )
			return;

		string[] recent = LoadRecentTeleports();
		List<string> next = new List<string>( RecentTeleportSlots );
		next.Add( spawnId );
		for ( int i = 0; i < recent.Length; i++ )
		{
			if ( string.IsNullOrEmpty( recent[ i ] ) || recent[ i ] == spawnId )
				continue;
			if ( next.Count >= RecentTeleportSlots )
				break;
			next.Add( recent[ i ] );
		}

		for ( int i = 0; i < RecentTeleportSlots; i++ )
		{
			string value = i < next.Count ? next[ i ] : string.Empty;
			PlayerPrefs.SetString( RecentTeleportPrefsPrefix + i, value );
		}

		PlayerPrefs.Save();
	}

	static string[] LoadRecentTeleports()
	{
		string[] recent = new string[ RecentTeleportSlots ];
		for ( int i = 0; i < RecentTeleportSlots; i++ )
			recent[ i ] = PlayerPrefs.GetString( RecentTeleportPrefsPrefix + i, string.Empty );
		return recent;
	}

	void Awake()
	{
		s_instance = this;
		ApplyVSync( PlayerPrefs.GetInt( VSyncPrefsKey, 1 ) != 0, persist: false );
		_showFps = PlayerPrefs.GetInt( ShowFpsPrefsKey, 0 ) != 0;
		_panelWidth = Mathf.Clamp( PlayerPrefs.GetFloat( PanelWidthPrefsKey, DefaultPanelWidth ), MinPanelWidth, MaxPanelWidth );
		_favoritesOnly = PlayerPrefs.GetInt( FavoritesOnlyPrefsKey, 0 ) != 0;
		_settingsOpen = LoadFoldoutOpen( "Settings", defaultOpen: true );
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
		_sections.Add( new DebugSaveStatesSection( this ) );
		_sections.Add( new DebugCameraSection() );
		_sections.Add( new DebugPlayerSection() );
		_sections.Add( new DebugAbilitiesSection() );
		_sections.Add( new DebugChestsSection() );
		_sections.Add( new DebugDoorsSection() );
		_sections.Add( new DebugCoinHallSection() );
		_sections.Add( new DebugFloatingPlatformsSection() );
		_sections.Add( new DebugUpgradesSection() );
		_sections.Add( new DebugWorldEventsSection() );
		_sections.Add( new DebugTutorialsSection() );
		_sections.Add( new DebugObjectivesSection() );
		_sections.Add( new DebugToastsSection() );
		_sections.Add( new DebugCoinSortingStationSection() );
		_sections.Add( new DebugRecyclingSection() );
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
				_foldouts[ title ] = LoadFoldoutOpen( title, defaultOpen: false );
			if ( !_favorites.ContainsKey( title ) )
				_favorites[ title ] = LoadFavorite( title );
		}
	}

	static string FoldoutPrefsKey( string title )
	{
		return FoldoutPrefsPrefix + title;
	}

	static string FavoritePrefsKey( string title )
	{
		return FavoritePrefsPrefix + title;
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

	static bool LoadFavorite( string title )
	{
		return PlayerPrefs.GetInt( FavoritePrefsKey( title ), 0 ) != 0;
	}

	static void SaveFavorite( string title, bool favorite )
	{
		PlayerPrefs.SetInt( FavoritePrefsKey( title ), favorite ? 1 : 0 );
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

		if ( keyboard[ quickSaveKey ].wasPressedThisFrame )
			DebugSaveStatesSection.QuickSave();

		if ( keyboard[ quickLoadKey ].wasPressedThisFrame )
			_ = DebugSaveStatesSection.QuickLoadAsync();
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
		Rect panel = new Rect( PanelPad, PanelPad, _panelWidth, Screen.height - PanelPad * 2f );
		GUI.Box( panel, GUIContent.none );

		GUILayout.BeginArea( new Rect( panel.x + 6f, panel.y + 6f, panel.width - 12f, panel.height - 12f ) );
		DrawHeader();
		DrawQuickActions();
		DrawNavToolbar();

		_scroll = GUILayout.BeginScrollView( _scroll );
		DrawSettingsFoldout();
		DrawSections();
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	void DrawHeader()
	{
		GUILayout.BeginHorizontal();
		GUILayout.Label( "DragonLoot Debug", GUILayout.ExpandWidth( true ) );
		if ( GUILayout.Button( "-", GUILayout.Width( 24f ) ) )
			SetPanelWidth( _panelWidth - WidthStep );
		if ( GUILayout.Button( "+", GUILayout.Width( 24f ) ) )
			SetPanelWidth( _panelWidth + WidthStep );
		GUILayout.EndHorizontal();
		GUILayout.Label( "` / Esc close  W:" + Mathf.RoundToInt( _panelWidth ) );
	}

	void DrawQuickActions()
	{
		GUILayout.BeginVertical( GUI.skin.box );
		GUILayout.Label( "Quick Actions" );

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Playtest Ready" ) )
			RunPlaytestReady();
		if ( GUILayout.Button( "Fill Coins" ) )
			RunFillCoins();
		if ( GUILayout.Button( "Clear Hands" ) )
			RunClearHands();
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Workshop" ) )
			RunTeleport( EventSceneAutoWire.IdVolumeWorkshop, "Workshop" );
		if ( GUILayout.Button( "Main Cave" ) )
			RunTeleport( EventSceneAutoWire.IdVolumeMainCave, "Main Cave" );
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( DoorProgress.UnlockAllDoors ? "Doors: ALL" : "Unlock Doors" ) )
		{
			bool next = !DoorProgress.UnlockAllDoors;
			DoorProgress.SetUnlockAllDoors( next );
			SetLastAction( next ? "Unlock all doors ON" : "Unlock all doors OFF" );
		}
		if ( GUILayout.Button( IsTutorialsDisabled() ? "Tutorials: OFF" : "Skip Tutorials" ) )
		{
			bool next = !IsTutorialsDisabled();
			SetTutorialsDisabled( next );
			SetLastAction( next ? "Tutorials disabled" : "Tutorials enabled" );
		}
		GUILayout.EndHorizontal();

		DrawRecentTeleports();

		if ( !string.IsNullOrEmpty( _lastAction ) )
			GUILayout.Label( _lastAction );

		GUILayout.EndVertical();
		GUILayout.Space( 4f );
	}

	void DrawRecentTeleports()
	{
		string[] recent = LoadRecentTeleports();
		bool any = false;
		for ( int i = 0; i < recent.Length; i++ )
		{
			if ( !string.IsNullOrEmpty( recent[ i ] ) )
			{
				any = true;
				break;
			}
		}

		if ( !any )
			return;

		GUILayout.Label( "Recent teleports" );
		GUILayout.BeginHorizontal();
		for ( int i = 0; i < recent.Length; i++ )
		{
			string id = recent[ i ];
			if ( string.IsNullOrEmpty( id ) )
				continue;
			string label = ResolveTeleportLabel( id );
			if ( GUILayout.Button( label ) )
				RunTeleport( id, label );
		}
		GUILayout.EndHorizontal();
	}

	static string ResolveTeleportLabel( string spawnId )
	{
		if ( DebugSpawnRegistry.TryGet( spawnId, out DebugSpawnPoint point ) && point != null )
			return point.DisplayName;

		for ( int i = 0; i < DebugSpawnRegistry.FallbackVolumes.Length; i++ )
		{
			DebugSpawnRegistry.FallbackVolume fallback = DebugSpawnRegistry.FallbackVolumes[ i ];
			if ( fallback.VolumeId == spawnId )
				return fallback.DisplayName;
		}

		return spawnId;
	}

	void DrawNavToolbar()
	{
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Collapse" ) )
			CollapseAll();
		if ( GUILayout.Button( "Favs" ) )
			ExpandFavorites();
		bool nextFavOnly = GUILayout.Toggle( _favoritesOnly, "Fav only", GUI.skin.button );
		if ( nextFavOnly != _favoritesOnly )
		{
			_favoritesOnly = nextFavOnly;
			PlayerPrefs.SetInt( FavoritesOnlyPrefsKey, _favoritesOnly ? 1 : 0 );
			PlayerPrefs.Save();
		}
		GUILayout.EndHorizontal();

		DebugOverlayGui.DrawSearchField( ref _sectionFilter, "Section" );
		GUILayout.Space( 4f );
	}

	void DrawSettingsFoldout()
	{
		string foldLabel = ( _settingsOpen ? "▼ " : "▶ " ) + _settings.Title;
		bool nextOpen = GUILayout.Toggle( _settingsOpen, foldLabel );
		if ( nextOpen != _settingsOpen )
		{
			_settingsOpen = nextOpen;
			SaveFoldoutOpen( "Settings", _settingsOpen );
		}

		if ( _settingsOpen )
		{
			GUILayout.BeginVertical( GUI.skin.box );
			_settings.Draw();
			GUILayout.EndVertical();
		}
	}

	void DrawSections()
	{
		string filter = _sectionFilter != null ? _sectionFilter.Trim() : string.Empty;
		bool filtering = filter.Length > 0;

		if ( _wasFiltering && !filtering && !string.IsNullOrEmpty( _lastExpandedSection ) )
		{
			_foldouts[ _lastExpandedSection ] = true;
			SaveFoldoutOpen( _lastExpandedSection, true );
		}

		_wasFiltering = filtering;

		for ( int i = 0; i < _sections.Count; i++ )
		{
			DebugOverlaySection section = _sections[ i ];
			string title = section.Title;
			bool favorite = false;
			_favorites.TryGetValue( title, out favorite );

			if ( filtering )
			{
				if ( title.IndexOf( filter, System.StringComparison.OrdinalIgnoreCase ) < 0 )
					continue;
			}
			else if ( _favoritesOnly && !favorite )
			{
				continue;
			}

			bool open = filtering;
			if ( !filtering )
				_foldouts.TryGetValue( title, out open );

			GUILayout.BeginHorizontal();
			bool nextFavorite = GUILayout.Toggle( favorite, favorite ? "★" : "☆", GUILayout.Width( 22f ) );
			if ( nextFavorite != favorite )
			{
				favorite = nextFavorite;
				_favorites[ title ] = favorite;
				SaveFavorite( title, favorite );
			}

			string foldLabel = ( open ? "▼ " : "▶ " ) + title;
			bool nextOpen = GUILayout.Toggle( open, foldLabel );
			GUILayout.EndHorizontal();

			if ( !filtering && nextOpen != open )
			{
				open = nextOpen;
				_foldouts[ title ] = open;
				SaveFoldoutOpen( title, open );
				if ( open )
					_lastExpandedSection = title;
			}

			if ( filtering )
				open = true;

			if ( open )
			{
				GUILayout.BeginVertical( GUI.skin.box );
				section.Draw();
				GUILayout.EndVertical();
			}
		}
	}

	void CollapseAll()
	{
		_settingsOpen = false;
		SaveFoldoutOpen( "Settings", false );
		for ( int i = 0; i < _sections.Count; i++ )
		{
			string title = _sections[ i ].Title;
			_foldouts[ title ] = false;
			SaveFoldoutOpen( title, false );
		}
		SetLastAction( "Collapsed all sections" );
	}

	void ExpandFavorites()
	{
		_favoritesOnly = false;
		PlayerPrefs.SetInt( FavoritesOnlyPrefsKey, 0 );
		for ( int i = 0; i < _sections.Count; i++ )
		{
			string title = _sections[ i ].Title;
			bool favorite = false;
			_favorites.TryGetValue( title, out favorite );
			_foldouts[ title ] = favorite;
			SaveFoldoutOpen( title, favorite );
		}
		PlayerPrefs.Save();
		SetLastAction( "Expanded favorites" );
	}

	void SetPanelWidth( float width )
	{
		_panelWidth = Mathf.Clamp( width, MinPanelWidth, MaxPanelWidth );
		PlayerPrefs.SetFloat( PanelWidthPrefsKey, _panelWidth );
		PlayerPrefs.Save();
	}

	void RunPlaytestReady()
	{
		Time.timeScale = 1f;
		SetTutorialsDisabled( true );

		PlayerController player = GetPlayer();
		AbilitySystem abilities = player != null ? AbilitySystem.Ensure( player ) : AbilitySystem.Instance;
		if ( abilities != null )
			abilities.UnlockAll();

		UpgradeSystem upgrades = player != null ? UpgradeSystem.Ensure( player ) : UpgradeSystem.Instance;
		if ( upgrades != null )
			upgrades.UnlockAll();

		SetLastAction( "Playtest Ready: abilities+upgrades unlocked, tutorials off" );
	}

	void RunFillCoins()
	{
		int added = DebugCarrySection.DebugAddCoinAssortment( 25 );
		SetLastAction( added > 0 ? "Filled " + added + " coins" : "Fill coins failed" );
	}

	void RunClearHands()
	{
		PlayerController player = GetPlayer();
		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry == null )
		{
			SetLastAction( "No carry" );
			return;
		}

		carry.Clear();
		SetLastAction( "Cleared hands" );
	}

	void RunTeleport( string spawnId, string label )
	{
		bool ok = DebugSpawnRegistry.TryTeleportTo( spawnId );
		if ( ok )
		{
			RecordRecentTeleport( spawnId );
			SetLastAction( "Teleported: " + label );
		}
		else
		{
			SetLastAction( "Teleport failed: " + label );
		}
	}

	static bool IsTutorialsDisabled()
	{
		if ( GameMode.Instance == null || GameMode.Instance.DebugDefinition == null )
			return false;
		return GameMode.Instance.DebugDefinition.disableTutorials;
	}

	static void SetTutorialsDisabled( bool disabled )
	{
		if ( GameMode.Instance == null || GameMode.Instance.DebugDefinition == null )
			return;
		GameMode.Instance.DebugDefinition.disableTutorials = disabled;
	}

	void DrawSensitivityFlash()
	{
		if ( Time.unscaledTime >= _overlayHideTime )
			return;

		float x = PanelPad;
		if ( _open )
			x = _panelWidth + PanelPad * 2f;

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
			await GameSaveSystem.RestartClean();
		}
		finally
		{
			_restartInProgress = false;
		}
	}

	public async void LoadSaveSlot( string slotId )
	{
		if ( _restartInProgress )
			return;

		_restartInProgress = true;
		try
		{
			if ( _open )
				SetOpen( false );
			await GameSaveSystem.RequestLoad( slotId );
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
