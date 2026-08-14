using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class GameMode : MonoBehaviour, IGameMode
{
	public static GameMode Instance;

	private CoreDefinition _coreDefinition = null;
	public CoreDefinition CoreDefinition
	{
		get
		{
			if ( _coreDefinition == null )
			{
				_coreDefinition = GameInstance.GetDefinition<CoreDefinition>();
			}

			return _coreDefinition;
		}
	}

	private DebugDefinition _debugDefinition = null;
	public DebugDefinition DebugDefinition
	{
		get
		{
			if ( _debugDefinition == null )
			{
				if ( !Application.isEditor )
				{
					_debugDefinition = ScriptableObject.CreateInstance<DebugDefinition>();
				}
				else
				{
					_debugDefinition = CoreDefinition.DebugDefinition;
				}
			}

			return _debugDefinition;
		}
	}

	public bool IsInGame => Game != null;

	private bool loaded = false;
	public Task Loaded => TaskEx.WaitUntil( () => loaded );

	public CameraController cameraController = null;
	public InputController InputController = null;
	public InterfaceController InterfaceController = null;

	public GameController Game = null;

	public PlayerController Player = null;

	private void Awake()
	{
		Instance = this;

		init();

		InputController = GetComponent<InputController>();

		if ( GameInstance.gameDef != null && GameInstance.gameDef.SteamEnabled && SteamManager.Instance != null && SteamManager.Instance.Initialized )
			SteamKeyboard.EnsureOverlayCallback();
	}

	void OnDestroy()
	{
		Instance = null;
	}

	private async void init()
	{
		AsyncOperationHandle<GameObject> asyncSpawnCamera = CoreDefinition.cameraAssetRef.InstantiateAsync();
		AsyncOperationHandle<GameObject> asyncSpawnPlayer = CoreDefinition.playerControllerAssetRef.InstantiateAsync();
		AsyncOperationHandle<GameObject> asyncSpawnGame = CoreDefinition.gameControllerAssetRef.InstantiateAsync();

		var firstBatch = new List<Task> { asyncSpawnCamera.Task, asyncSpawnPlayer.Task, asyncSpawnGame.Task };
		await Task.WhenAll( firstBatch );

		GameObject cameraObj = asyncSpawnCamera.Status == AsyncOperationStatus.Succeeded
			? asyncSpawnCamera.Result
			: null;
		GameObject playerObj = asyncSpawnPlayer.Status == AsyncOperationStatus.Succeeded
			? asyncSpawnPlayer.Result
			: null;

		if ( cameraObj != null )
			cameraController = cameraObj.GetComponent<CameraController>();

		if ( playerObj != null )
			Player = playerObj.GetComponent<PlayerController>();

		if ( asyncSpawnGame.Status == AsyncOperationStatus.Succeeded )
		{
			Game = asyncSpawnGame.Result.GetComponent<GameController>();
			WireGameController( Game );
		}

		WireCameraAndPlayer();

		if ( Game != null )
			await Game.InitializeFromGameMode();

		AsyncOperationHandle<GameObject> asyncSpawnInterface = CoreDefinition.interfaceAssetRef.InstantiateAsync();

		await asyncSpawnInterface.Task;

		if ( asyncSpawnInterface.Status == AsyncOperationStatus.Succeeded )
		{
			GameObject interfacePrefab = asyncSpawnInterface.Result;

			InterfaceController = interfacePrefab.GetComponent<InterfaceController>();

			InterfaceController.Setup();
			await InterfaceController.SetInterfaceState( InterfaceState.Game );

			CrosshairUI crosshair = interfacePrefab.GetComponentInChildren<CrosshairUI>( true );
			if ( crosshair == null )
				crosshair = interfacePrefab.AddComponent<CrosshairUI>();
			crosshair.Setup();

			InteractionContextUI contextUi = interfacePrefab.GetComponentInChildren<InteractionContextUI>( true );
			if ( contextUi == null )
				contextUi = interfacePrefab.AddComponent<InteractionContextUI>();
			contextUi.Setup();

			InteractionProgressRingUI progressRing = interfacePrefab.GetComponentInChildren<InteractionProgressRingUI>( true );
			if ( progressRing == null )
			{
				CrosshairUI host = interfacePrefab.GetComponentInChildren<CrosshairUI>( true );
				if ( host != null )
					progressRing = host.gameObject.AddComponent<InteractionProgressRingUI>();
				else
					progressRing = interfacePrefab.AddComponent<InteractionProgressRingUI>();
			}

			progressRing.Setup();

			DragonDialogueUI dialogueUi = interfacePrefab.GetComponentInChildren<DragonDialogueUI>( true );
			if ( dialogueUi != null )
				dialogueUi.Setup();

			QuestObjectiveUI objectiveUi = interfacePrefab.GetComponentInChildren<QuestObjectiveUI>( true );
			if ( objectiveUi != null )
				objectiveUi.Setup();

			QuestCompassUI compassUi = interfacePrefab.GetComponentInChildren<QuestCompassUI>( true );
			if ( compassUi != null )
				compassUi.Setup();

			QuestWorldMarker worldMarker = interfacePrefab.GetComponentInChildren<QuestWorldMarker>( true );
			if ( worldMarker != null )
				worldMarker.Setup();

			QuestSystem quests = QuestSystem.EnsureExists();
			quests.StartOrResumeCatalog();
		}

		if ( GetComponent<DebugOverlay>() == null )
			gameObject.AddComponent<DebugOverlay>();

		loaded = true;
	}

	void WireGameController( GameController game )
	{
		if ( game == null )
			return;

		game.PlayerController = Player;
	}

	void WireCameraAndPlayer()
	{
		if ( cameraController != null )
			cameraController.Setup();

		if ( Player != null && cameraController != null && Player.CameraMount != null )
		{
			cameraController.transform.SetParent( Player.CameraMount, false );
			cameraController.transform.localPosition = Vector3.zero;
			cameraController.transform.localRotation = Quaternion.identity;
		}

		if ( Player != null && cameraController != null )
			Player.Setup( cameraController.FirstPerson );
	}

	public void HandleDisconnection()
	{

	}

	public void LeaveCurrentMatch( bool sendCloseMatch = true )
	{
	}
}