using UnityEngine;

public class DebugQuestsSection : DebugOverlaySection
{
	public string Title => "Quests";

	public void Draw()
	{
		if ( !QuestSystem.Enabled )
		{
			GUILayout.Label( "Quests disabled (QuestSystem.Enabled = false)" );
			return;
		}

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
		GUILayout.Label( "Objective: " + system.ActiveStepIndex );
		GUILayout.Label( "Catalog complete: " + system.CatalogComplete );

		if ( active != null && active.subquests != null )
		{
			GUILayout.Label( "Subquests: " + active.subquests.Length );
			for ( int i = 0; i < active.subquests.Length; i++ )
			{
				QuestDefinition sub = active.subquests[ i ];
				if ( sub == null )
					continue;
				GUILayout.Label( "  " + sub.id );
			}
		}

		GUILayout.Space( 6f );
		if ( GUILayout.Button( "Complete Active Objective" ) )
			system.DebugCompleteActiveStep();

		if ( GUILayout.Button( "Reset Quest Progress" ) )
			system.DebugResetProgress();

		GUILayout.Space( 4f );
		GUILayout.Label( "Skip to quest:" );
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Starting" ) )
			system.DebugSkipToQuest( 0 );
		if ( GUILayout.Button( "Main" ) )
			system.DebugSkipToQuest( 1 );
		GUILayout.EndHorizontal();

		if ( GUILayout.Button( "Start / Resume Catalog" ) )
			system.StartOrResumeCatalog();
	}
}
