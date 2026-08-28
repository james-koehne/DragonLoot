using UnityEngine;

public class DebugTutorialsSection : DebugOverlaySection
{
	public string Title => "Tutorials";

	public void Draw()
	{
		TutorialManager system = TutorialManager.Instance;
		if ( system == null )
			system = TutorialManager.EnsureExists();

		if ( system == null )
		{
			GUILayout.Label( "No TutorialManager" );
			return;
		}

		TutorialCatalogDefinition catalog = system.Catalog;
		int count = catalog != null ? catalog.Count : 0;
		GUILayout.Label( "Catalog tutorials: " + count );
		GUILayout.Label( "Playing: " + system.IsSequencePlaying );
		GUILayout.Label( "Last shown: " + ( string.IsNullOrEmpty( system.LastShownTutorialId ) ? "(none)" : system.LastShownTutorialId ) );

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		int discovered = 0;
		int completed = 0;
		if ( save != null )
		{
			save.EnsureTutorialProgress();
			discovered = save.discoveredTutorialIds != null ? save.discoveredTutorialIds.Count : 0;
			completed = save.completedTutorialIds != null ? save.completedTutorialIds.Count : 0;
		}
		GUILayout.Label( "Discovered: " + discovered + "  Completed: " + completed );

		GUILayout.Space( 6f );
		if ( GUILayout.Button( "Reset Tutorial Progress" ) )
			system.DebugResetProgress();

		GUILayout.Space( 4f );
		GUILayout.Label( "Force show:" );
		if ( catalog != null && catalog.tutorials != null )
		{
			for ( int i = 0; i < catalog.tutorials.Count; i++ )
			{
				TutorialDefinition def = catalog.tutorials[ i ];
				if ( def == null || string.IsNullOrEmpty( def.id ) )
					continue;
				string label = string.IsNullOrEmpty( def.title ) ? def.id : def.title;
				if ( GUILayout.Button( label ) )
					system.DebugForceShow( def.id );
			}
		}
	}
}
