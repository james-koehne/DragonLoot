using UnityEngine;

public class DebugWorldEventsSection : DebugOverlaySection
{
	public string Title => "World Events";

	public void Draw()
	{
		WorldEventSystem system = WorldEventSystem.Instance;
		if ( system == null )
			system = WorldEventSystem.EnsureExists();

		if ( system == null )
		{
			GUILayout.Label( "No WorldEventSystem" );
			return;
		}

		WorldEventCatalogDefinition catalog = system.Catalog;
		int count = catalog != null ? catalog.Count : 0;
		GUILayout.Label( "Catalog events: " + count );
		GUILayout.Label( "Game started: " + system.GameStarted );

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		int fired = 0;
		if ( save != null )
		{
			save.EnsureWorldEventProgress();
			fired = save.firedWorldEventIds != null ? save.firedWorldEventIds.Count : 0;
		}
		GUILayout.Label( "Fired (save): " + fired );

		GUILayout.Space( 6f );
		if ( GUILayout.Button( "Reset Fired Events" ) )
			system.DebugResetFiredEvents();

		GUILayout.Space( 4f );
		GUILayout.Label( "Fire intro:" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Welcome" ) )
			system.DebugFireEvent( "intro_welcome" );
		if ( GUILayout.Button( "Hallway" ) )
			system.DebugFireEvent( "intro_hallway" );
		if ( GUILayout.Button( "Ledge" ) )
			system.DebugFireEvent( "intro_ledge" );
		GUILayout.EndHorizontal();

		if ( GUILayout.Button( "Start Catalog" ) )
			system.StartCatalog();
	}
}
