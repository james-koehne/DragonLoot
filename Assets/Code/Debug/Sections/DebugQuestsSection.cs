using UnityEngine;

public class DebugQuestsSection : DebugOverlaySection
{
	public string Title => "Quests";

	public void Draw()
	{
		QuestSystem system = QuestSystem.Instance;
		if ( system == null )
			system = QuestSystem.EnsureExists();

		if ( system == null )
		{
			GUILayout.Label( "No QuestSystem" );
			return;
		}

		QuestDefinition active = system.ActiveQuest;
		GUILayout.Label( "Active: " + ( active != null ? active.id : "(none)" ) );
		GUILayout.Label( "Step: " + system.ActiveStepIndex );
		GUILayout.Label( "Catalog complete: " + system.CatalogComplete );

		GUILayout.Space( 6f );
		if ( GUILayout.Button( "Complete Active Step" ) )
			system.DebugCompleteActiveStep();

		if ( GUILayout.Button( "Reset Quest Progress" ) )
			system.DebugResetProgress();

		GUILayout.Space( 4f );
		GUILayout.Label( "Skip to quest:" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "0" ) )
			system.DebugSkipToQuest( 0 );
		if ( GUILayout.Button( "1" ) )
			system.DebugSkipToQuest( 1 );
		if ( GUILayout.Button( "2" ) )
			system.DebugSkipToQuest( 2 );
		if ( GUILayout.Button( "3" ) )
			system.DebugSkipToQuest( 3 );
		if ( GUILayout.Button( "4" ) )
			system.DebugSkipToQuest( 4 );
		GUILayout.EndHorizontal();

		if ( GUILayout.Button( "Start / Resume Catalog" ) )
			system.StartOrResumeCatalog();
	}
}
