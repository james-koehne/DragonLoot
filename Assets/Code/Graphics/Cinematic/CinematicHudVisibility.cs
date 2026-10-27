using UnityEngine;

/// <summary>
/// Hides gameplay HUD during cinematics while leaving dialog UI (e.g. <see cref="DragonDialogueUI"/>) alone.
/// </summary>
public static class CinematicHudVisibility
{
	public static void SetGameplayHudHidden( bool hidden )
	{
		if ( CrosshairUI.Instance != null )
			CrosshairUI.Instance.SetCinematicHidden( hidden );

		if ( GameMode.Instance == null || GameMode.Instance.InterfaceController == null )
			return;

		Transform root = GameMode.Instance.InterfaceController.transform;
		QuestObjectiveUI objective = root.GetComponentInChildren<QuestObjectiveUI>( true );
		if ( objective != null )
			objective.SetCinematicHidden( hidden );

		TutorialPopupUI tutorial = root.GetComponentInChildren<TutorialPopupUI>( true );
		if ( tutorial != null )
			tutorial.SetCinematicHidden( hidden );

		QuestCompassUI compass = root.GetComponentInChildren<QuestCompassUI>( true );
		if ( compass != null )
			compass.SetCinematicHidden( hidden );

		QuestWorldMarker marker = root.GetComponentInChildren<QuestWorldMarker>( true );
		if ( marker != null )
			marker.SetCinematicHidden( hidden );

		PouchBarUI pouchBar = root.GetComponentInChildren<PouchBarUI>( true );
		if ( pouchBar != null )
			pouchBar.SetCinematicHidden( hidden );

		RightContextUI rightContext = root.GetComponentInChildren<RightContextUI>( true );
		if ( rightContext != null )
			rightContext.SetCinematicHidden( hidden );
	}
}
