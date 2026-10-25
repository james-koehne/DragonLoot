/// <summary>
/// When Island 4 artifact presentation completes, auto-builds the Island 4 coin sorter
/// (<c>island4_coin_sorter</c>) with player-style hammer SFX. Tutorial_Island4CoinSorter
/// listens for that buildable complete.
/// </summary>
public static class Island4CoinSorterUnlock
{
	public const string ObjectiveId = "obj_island_4_artifacts";
	public const string ArtifactSubId = "present_artifacts";
	public const string BuildablePersistenceId = "island4_coin_sorter";

	public static void TryUnlockFromSub( string objectiveId, string subId )
	{
		if ( objectiveId != ObjectiveId || subId != ArtifactSubId )
			return;

		TryUnlockSorter( scriptedBuild: true );
	}

	public static void CatchUp( ObjectiveSystem objectives )
	{
		if ( objectives == null )
			return;
		if ( !objectives.IsSubCompleted( ObjectiveId, ArtifactSubId ) )
			return;

		TryUnlockSorter( scriptedBuild: false );
	}

	static void TryUnlockSorter( bool scriptedBuild )
	{
		if ( scriptedBuild && TryBeginScriptedBuild() )
			return;

		if ( BuildableObject.TryCompleteByPersistenceId( BuildablePersistenceId ) )
			return;

		// Already built from save — still arm the sorter tutorial context.
		if ( !IsBuildableBuilt( BuildablePersistenceId ) )
			return;

		TutorialManager tutorials = TutorialManager.Instance;
		if ( tutorials != null )
			tutorials.NotifyBuildableAlreadyComplete( BuildablePersistenceId );
	}

	static bool TryBeginScriptedBuild()
	{
		if ( !BuildableObject.TryFindByPersistenceId( BuildablePersistenceId, out BuildableObject buildable ) )
			return false;
		if ( buildable.IsBuilt || buildable.IsCompleting )
			return false;

		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
			return false;

		PlayerBuildMode buildMode = GameMode.Instance.Player.BuildMode;
		if ( buildMode == null )
			return false;

		return buildMode.TryBeginScriptedBuild( buildable );
	}

	static bool IsBuildableBuilt( string persistenceId )
	{
		if ( !BuildableObject.TryFindByPersistenceId( persistenceId, out BuildableObject buildable ) )
			return false;
		return buildable.IsBuilt;
	}
}
