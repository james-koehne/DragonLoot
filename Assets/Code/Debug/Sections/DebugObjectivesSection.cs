using UnityEngine;

public class DebugObjectivesSection : DebugOverlaySection
{
	static string s_filter = string.Empty;

	public string Title => "Objectives";

	public void Draw()
	{
		ObjectiveSystem system = ObjectiveSystem.Instance;
		if ( system == null )
			system = ObjectiveSystem.EnsureExists();

		if ( system == null )
		{
			GUILayout.Label( "No ObjectiveSystem" );
			return;
		}

		ObjectiveCatalogDefinition catalog = system.Catalog;
		int count = catalog != null ? catalog.Count : 0;
		GUILayout.Label( "Catalog objectives: " + count );

		ProfileSaveData save = ProfileManager.Instance != null ? ProfileManager.Instance.ProfileSaveData : null;
		int completed = 0;
		int completedSubs = 0;
		if ( save != null )
		{
			save.EnsureObjectiveProgress();
			completed = save.completedObjectiveIds != null ? save.completedObjectiveIds.Count : 0;
			completedSubs = save.completedObjectiveSubIds != null ? save.completedObjectiveSubIds.Count : 0;
		}

		GUILayout.Label( "Completed: " + completed + "  Sub keys: " + completedSubs );

		GUILayout.Space( 6f );
		if ( GUILayout.Button( "Reset Objective Progress" ) )
			system.DebugResetProgress();

		GUILayout.Space( 4f );
		GUILayout.Label( "Force complete:" );
		DebugOverlayGui.DrawSearchField( ref s_filter );
		string filter = s_filter != null ? s_filter.Trim() : string.Empty;
		if ( catalog != null && catalog.objectives != null )
		{
			for ( int i = 0; i < catalog.objectives.Count; i++ )
			{
				ObjectiveDefinition def = catalog.objectives[ i ];
				if ( def == null || string.IsNullOrEmpty( def.id ) )
					continue;

				string label = def.ResolveTitle();
				if ( !string.IsNullOrEmpty( filter ) &&
				     label.IndexOf( filter, System.StringComparison.OrdinalIgnoreCase ) < 0 &&
				     def.id.IndexOf( filter, System.StringComparison.OrdinalIgnoreCase ) < 0 )
					continue;

				bool done = system.IsCompleted( def.id );
				GUILayout.BeginHorizontal();
				GUILayout.Label( ( done ? "[done] " : "" ) + label, GUILayout.ExpandWidth( true ) );
				if ( GUILayout.Button( "Complete", GUILayout.Width( 70f ) ) )
					system.DebugCompleteObjective( def.id );
				GUILayout.EndHorizontal();

				if ( def.subs == null )
					continue;

				for ( int s = 0; s < def.subs.Length; s++ )
				{
					ObjectiveSubDefinition sub = def.subs[ s ];
					if ( sub == null || string.IsNullOrEmpty( sub.id ) )
						continue;

					string subLabel = string.IsNullOrEmpty( sub.label ) ? sub.id : sub.label;
					bool subDone = system.IsSubCompleted( def.id, sub.id );
					if ( ObjectiveProgress.HasCountProgress( sub.completeType ) )
					{
						int current;
						int required;
						if ( ObjectiveProgress.TryGetCounts( def, sub, out current, out required ) && required > 0 )
							subLabel = subLabel + ": " + current + "/" + required;
					}

					GUILayout.BeginHorizontal();
					GUILayout.Space( 12f );
					GUILayout.Label( ( subDone ? "[x] " : "[ ] " ) + subLabel, GUILayout.ExpandWidth( true ) );
					if ( GUILayout.Button( "Sub", GUILayout.Width( 50f ) ) )
						system.DebugCompleteSub( def.id, sub.id );
					GUILayout.EndHorizontal();
				}
			}
		}
	}
}
