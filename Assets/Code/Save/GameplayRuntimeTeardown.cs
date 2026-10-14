using UnityEngine;

/// <summary>
/// Destroys gameplay DontDestroyOnLoad hosts so <see cref="GameInstance.RestartGame"/> boots clean.
/// </summary>
public static class GameplayRuntimeTeardown
{
	public static void Teardown()
	{
		DestroyHost( TutorialManager.Instance != null ? TutorialManager.Instance.gameObject : null );
		DestroyHost( WorldEventSystem.Instance != null ? WorldEventSystem.Instance.gameObject : null );
		DestroyHost( ObjectiveSystem.Instance != null ? ObjectiveSystem.Instance.gameObject : null );
		DestroyHost( FindByName( "QuestObjectiveOutline" ) );
		DestroyHost( MusicAmbienceSystem.Instance != null ? MusicAmbienceSystem.Instance.gameObject : null );
		WorldTreasureStreamer.ClearStaticInstance();
		DestroyHost( FindByName( "LooseTreasureManager" ) );
		DestroyHost( FindByName( "TreasureProximitySleep" ) );
		DestroyHost( FindByName( "TreasureMotionHost" ) );

		UpgradeSystem.ClearInstance();
		AbilitySystem.ClearInstance();
		TreasureItemFactory.ClearPoolRoot();
	}

	static void DestroyHost( GameObject go )
	{
		if ( go == null )
			return;
		Object.Destroy( go );
	}

	static GameObject FindByName( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return null;

		GameObject[] roots = Resources.FindObjectsOfTypeAll<GameObject>();
		if ( roots == null )
			return null;

		for ( int i = 0; i < roots.Length; i++ )
		{
			GameObject go = roots[ i ];
			if ( go == null || go.name != name )
				continue;
			if ( go.hideFlags != HideFlags.None )
				continue;
			if ( go.transform.parent != null )
				continue;
			return go;
		}

		return null;
	}
}
