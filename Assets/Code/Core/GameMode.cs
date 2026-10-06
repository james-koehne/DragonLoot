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
					DebugDefinition authored = CoreDefinition.DebugDefinition;
					if ( authored != null )
					{
						_debugDefinition.disableTutorials = authored.disableTutorials;
						_debugDefinition.debugSpawnId = authored.debugSpawnId;
					}
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

			CrosshairContextUI lookUi = interfacePrefab.GetComponentInChildren<CrosshairContextUI>( true );
			if ( lookUi == null )
			{
				if ( crosshair != null )
					lookUi = crosshair.gameObject.AddComponent<CrosshairContextUI>();
				else
					lookUi = interfacePrefab.AddComponent<CrosshairContextUI>();
			}
			lookUi.Setup();

			InteractionContextUI contextUi = interfacePrefab.GetComponentInChildren<InteractionContextUI>( true );
			if ( contextUi == null )
				contextUi = interfacePrefab.AddComponent<InteractionContextUI>();
			contextUi.Setup();

			BuildableHoverUI buildableHover = ResolveUniqueInterfaceChild<BuildableHoverUI>( interfacePrefab, "BuildableHoverPopup" );
			if ( buildableHover != null )
				buildableHover.Setup();
			else
				Debug.LogWarning( "GameMode: BuildableHoverUI missing on Interface prefab." );

			InteractionProgressRingUI progressRing = interfacePrefab.GetComponentInChildren<InteractionProgressRingUI>( true );
			if ( progressRing != null )
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

			PauseMenuUI pauseMenu = interfacePrefab.GetComponentInChildren<PauseMenuUI>( true );
			if ( pauseMenu != null )
				pauseMenu.Setup();

			MapUI mapUi = ResolveUniqueInterfaceChild<MapUI>( interfacePrefab, "Map" );
			if ( mapUi != null )
				mapUi.Setup();
			else
				Debug.LogWarning( "GameMode: MapUI missing on Interface prefab." );

			PouchSummaryUI pouchSummary = interfacePrefab.GetComponentInChildren<PouchSummaryUI>( true );
			if ( pouchSummary != null )
				pouchSummary.Setup();

			PouchBarUI pouchBar = interfacePrefab.GetComponentInChildren<PouchBarUI>( true );
			if ( pouchBar == null )
			{
				Transform existingBar = interfacePrefab.transform.Find( "PouchBar" );
				GameObject barGo = existingBar != null
					? existingBar.gameObject
					: new GameObject( "PouchBar", typeof( RectTransform ), typeof( CanvasGroup ), typeof( PouchBarUI ) );
				if ( existingBar == null )
					barGo.transform.SetParent( interfacePrefab.transform, false );
				pouchBar = barGo.GetComponent<PouchBarUI>();
				if ( pouchBar == null )
					pouchBar = barGo.AddComponent<PouchBarUI>();
			}
			pouchBar.Setup();

			PouchFullHintUI pouchFullHint = interfacePrefab.GetComponentInChildren<PouchFullHintUI>( true );
			if ( pouchFullHint == null )
			{
				GameObject hintGo = new GameObject( "PouchFullHint", typeof( RectTransform ), typeof( CanvasGroup ), typeof( PouchFullHintUI ) );
				hintGo.transform.SetParent( interfacePrefab.transform, false );
				pouchFullHint = hintGo.GetComponent<PouchFullHintUI>();
			}
			pouchFullHint.Setup();

			TutorialPopupUI tutorialPopup = interfacePrefab.GetComponentInChildren<TutorialPopupUI>( true );
			if ( tutorialPopup != null )
				tutorialPopup.Setup();
			else
				Debug.LogWarning( "GameMode: TutorialPopupUI missing on Interface prefab." );

			ToastStackUI toastStack = interfacePrefab.GetComponentInChildren<ToastStackUI>( true );
			if ( toastStack != null )
				toastStack.Setup();
			else
				Debug.LogWarning( "GameMode: ToastStackUI missing on Interface prefab." );

			DiscoveryToastUI discoveryToast = interfacePrefab.GetComponentInChildren<DiscoveryToastUI>( true );
			if ( discoveryToast != null )
				discoveryToast.Setup();
			else
				Debug.LogWarning( "GameMode: DiscoveryToastUI missing on Interface prefab." );

			UnlockRewardToastUI unlockToast = interfacePrefab.GetComponentInChildren<UnlockRewardToastUI>( true );
			if ( unlockToast != null )
				unlockToast.Setup();
			else
				Debug.LogWarning( "GameMode: UnlockRewardToastUI missing on Interface prefab." );

			TutorialManager tutorials = TutorialManager.EnsureExists();
			tutorials.StartCatalog( tutorialPopup );

			WorldEventSystem worldEvents = WorldEventSystem.EnsureExists();
			worldEvents.StartCatalog();
			DebugSpawnRegistry.ApplySkipIntroIfSelected();

			ObjectiveSystem objectives = ObjectiveSystem.EnsureExists();
			objectives.StartCatalog();
			DebugSpawnRegistry.ApplyPostTutorialIfSelected();

			QuestObjectiveOutline.EnsureExists();
		}

		if ( GetComponent<DebugOverlay>() == null )
			gameObject.AddComponent<DebugOverlay>();

		loaded = true;
	}

	/// <summary>
	/// Finds a unique named HUD child on Interface. Never creates a second sibling when one already exists.
	/// Extra duplicates (if any) are destroyed so play mode cannot keep stacking them.
	/// </summary>
	static T ResolveUniqueInterfaceChild<T>( GameObject interfaceRoot, string childName ) where T : Component
	{
		if ( interfaceRoot == null || string.IsNullOrEmpty( childName ) )
			return null;

		Transform keep = null;
		for ( int i = 0; i < interfaceRoot.transform.childCount; i++ )
		{
			Transform child = interfaceRoot.transform.GetChild( i );
			if ( child == null || child.name != childName )
				continue;
			if ( keep == null )
				keep = child;
			else
				Destroy( child.gameObject );
		}

		if ( keep != null )
		{
			T onNamed = keep.GetComponent<T>();
			if ( onNamed != null )
				return onNamed;
		}

		return interfaceRoot.GetComponentInChildren<T>( true );
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
			cameraController.CaptureBaseLocalPosition();
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