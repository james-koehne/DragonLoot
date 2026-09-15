using System.Collections.Generic;

using UnityEngine;

public class DebugFloatingPlatformsSection : DebugOverlaySection
{
	public string Title => "Floating Platforms";

	public void Draw()
	{
		IReadOnlyList<FloatingPlatformGroup> groups = FloatingPlatformGroup.ActiveGroups;
		int count = groups != null ? groups.Count : 0;
		GUILayout.Label( "Active groups: " + count );

		if ( groups == null || count == 0 )
		{
			GUILayout.Label( "(Add FloatingPlatformGroup to the level)" );
			return;
		}

		for ( int i = 0; i < count; i++ )
		{
			FloatingPlatformGroup group = groups[ i ];
			if ( group == null )
				continue;

			GUILayout.Space( 4f );
			string id = string.IsNullOrEmpty( group.GroupId ) ? "(no id)" : group.GroupId;
			GUILayout.Label( id + " — " + ( group.IsActivated ? "UP" : "SUNK" ) );
			GUILayout.Label( "  " + group.TriggerType + ": " + ( string.IsNullOrEmpty( group.TriggerId ) ? "-" : group.TriggerId ) );

			int platformCount = group.Platforms != null ? group.Platforms.Length : 0;
			GUILayout.Label( "  platforms: " + platformCount );

			GUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Activate" ) )
				group.Activate();
			if ( GUILayout.Button( "Force" ) )
				group.ForceActivate();
			if ( GUILayout.Button( "Reset" ) )
				group.ResetToSunk();
			GUILayout.EndHorizontal();
		}
	}
}
