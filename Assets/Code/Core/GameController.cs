using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene-level coordinator for run flow. Wire system references in the inspector.
/// </summary>
public class GameController : MonoBehaviour
{
	const string DefaultLevelScenePath = "Assets/Scenes/Level.unity";
	const string GameSceneName = "Game";

	[Header( "Flow" )]
	public GameState CurrentState;

	[Header( "Level" )]
	public string levelScenePath = DefaultLevelScenePath;

	[Header( "Systems (assign in inspector or extend InitializeSystems)" )]
	public PlayerController PlayerController;

	EnvironmentDefinition _environmentDefinition;

	bool _flowBootstrapped;
	bool _runBootstrapped;

	/// <summary>
	/// Called once from <see cref="GameMode"/> after this instance is spawned from Addressables and wired. Not invoked from Unity <c>Start</c>.
	/// </summary>
	public async Task InitializeFromGameMode()
	{
		if ( _runBootstrapped )
			return;

		_runBootstrapped = true;

		InitializeSystems();
		await LoadLevelAdditive();
		if ( TreasureSurfaceWorld.Instance != null )
			TreasureSurfaceWorld.Instance.RebindAuthoring();
		TreasureSurfaceBlocker.RefreshAll();
		ApplyEnvironmentLighting();
		PlacePlayerAtSpawn();
		HallwayFogBlend hallwayFog = HallwayFogBlend.Active;
		if ( hallwayFog != null )
		{
			hallwayFog.NotifyPlayerPlaced();
			hallwayFog.ForceApply();
		}
		BindTreasureCounters();
		StartGame();
	}

	void ApplyEnvironmentLighting()
	{
		EnvironmentDefinition env = RuntimeDefinition.Resolve( ref _environmentDefinition );
		if ( env == null )
			return;

		Light sun = null;
		LevelSceneMarkers markers = LevelSceneMarkers.Instance;
		if ( markers != null )
			sun = markers.sunLight;

		env.Apply( sun );
	}

	/// <summary>
	/// Resolve references and wake dependent services. Systems may self-init in their own <c>Start</c>/<c>Awake</c>.
	/// </summary>
	void InitializeSystems()
	{
		TreasureCounterManager.EnsureExists();
		TreasureSurfaceWorld.EnsureExists();
		MapSystem.EnsureExists();
		WorldEventSystem.EnsureExists();
		MusicAmbienceSystem.EnsureExists();
	}

	void BindTreasureCounters()
	{
		TreasureCounterManager manager = TreasureCounterManager.EnsureExists();
		manager.BindSection( "Section 1" );
	}

	async Task LoadLevelAdditive()
	{
		string scenePath = string.IsNullOrEmpty( levelScenePath ) ? DefaultLevelScenePath : levelScenePath;

		Scene existing = SceneManager.GetSceneByPath( scenePath );
		if ( !existing.IsValid() )
			existing = SceneManager.GetSceneByName( GetSceneNameFromPath( scenePath ) );

		if ( !existing.isLoaded )
		{
			AsyncOperation handle = SceneManager.LoadSceneAsync( scenePath, LoadSceneMode.Additive );
			if ( handle == null )
			{
				Debug.LogError( $"GameController: failed to start additive load for '{scenePath}'." );
				EnsureGameSceneActive();
				return;
			}

			TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
			handle.completed += _ => tcs.SetResult( true );
			await tcs.Task;
		}

		EnsureGameSceneActive();
	}

	static string GetSceneNameFromPath( string path )
	{
		int slash = path.LastIndexOf( '/' );
		string file = slash >= 0 ? path.Substring( slash + 1 ) : path;
		int dot = file.LastIndexOf( '.' );
		return dot >= 0 ? file.Substring( 0, dot ) : file;
	}

	static void EnsureGameSceneActive()
	{
		Scene gameScene = SceneManager.GetSceneByName( GameSceneName );
		if ( gameScene.IsValid() && gameScene.isLoaded )
			SceneManager.SetActiveScene( gameScene );
	}

	public void RespawnPlayer()
	{
		PlacePlayerAtSpawn();
		DebugSpawnRegistry.ApplySkipIntroIfSelected();
	}

	void PlacePlayerAtSpawn()
	{
		if ( PlayerController == null )
			return;

#if UNITY_EDITOR
		if ( TryPlacePlayerAtSceneCamera() )
			return;
#endif

		if ( DebugSpawnRegistry.TryGetSelectedPose( out Vector3 debugPosition, out Quaternion debugRotation ) )
		{
			PlayerController.TeleportTo( debugPosition, debugRotation );
			return;
		}

		LevelSceneMarkers markers = LevelSceneMarkers.Instance;
		if ( markers == null || markers.playerSpawn == null )
			return;

		PlayerController.TeleportTo( markers.playerSpawn.position, markers.playerSpawn.rotation );
	}

#if UNITY_EDITOR
	bool TryPlacePlayerAtSceneCamera()
	{
		if ( GameMode.Instance == null )
			return false;

		DebugDefinition debug = GameMode.Instance.DebugDefinition;
		if ( debug == null || !debug.spawnAtSceneCamera )
			return false;

		if ( !EditorSceneCameraSpawn.TryGetPose( out Vector3 camPosition, out Quaternion camRotation ) )
			return false;

		Vector3 forward = camRotation * Vector3.forward;
		forward.y = 0f;
		Quaternion bodyRotation = forward.sqrMagnitude > 0.0001f
			? Quaternion.LookRotation( forward.normalized, Vector3.up )
			: Quaternion.Euler( 0f, camRotation.eulerAngles.y, 0f );

		Vector3 spawnPosition = camPosition;
		if ( PlayerController.CameraMount != null )
		{
			Vector3 mountOffset = bodyRotation * PlayerController.CameraMount.localPosition;
			spawnPosition = camPosition - mountOffset;
		}

		PlayerController.TeleportTo( spawnPosition, bodyRotation );

		if ( GameMode.Instance.cameraController != null )
		{
			FirstPersonCameraController look = GameMode.Instance.cameraController.FirstPerson;
			if ( look != null )
				look.SetPitch( camRotation.eulerAngles.x );
		}

		return true;
	}
#endif

	public void StartGame()
	{
		if ( PlayerController != null )
			PlayerController.ResetForNewRun();

		EnterState( GameState.GameRun );
	}

	void Update()
	{
		switch ( CurrentState )
		{
			case GameState.GameRun:
				break;
			case GameState.GameEnd:
				break;
		}
	}

	public void EnterState( GameState newState )
	{
		if ( _flowBootstrapped && newState == CurrentState )
			return;

		if ( _flowBootstrapped )
			OnExitState( CurrentState );

		CurrentState = newState;
		_flowBootstrapped = true;
		OnEnterState( CurrentState );
	}

	void OnExitState( GameState leaving )
	{
		switch ( leaving )
		{
			case GameState.GameRun:
				break;
			case GameState.GameEnd:
				break;
		}
	}

	void OnEnterState( GameState entering )
	{
		switch ( entering )
		{
			case GameState.GameRun:
				if ( PlayerController != null )
					PlayerController.SetGameplayInputEnabled( true );
				break;
			case GameState.GameEnd:
				if ( PlayerController != null )
					PlayerController.SetGameplayInputEnabled( false );
				break;
		}
	}
}
