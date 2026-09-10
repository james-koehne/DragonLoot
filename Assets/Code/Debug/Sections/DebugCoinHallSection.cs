using System.Collections.Generic;

using UnityEngine;

public class DebugCoinHallSection : DebugOverlaySection
{
	int _forceLevel;

	public string Title => "Coin Hall";

	public void Draw()
	{
		IReadOnlyList<CoinHallProgression> halls = CoinHallProgression.ActiveHalls;
		int count = halls != null ? halls.Count : 0;
		GUILayout.Label( "Active halls: " + count );

		if ( halls == null || count == 0 )
		{
			GUILayout.Label( "(Add CoinHallProgression to the level)" );
			return;
		}

		for ( int i = 0; i < count; i++ )
		{
			CoinHallProgression hall = halls[ i ];
			if ( hall == null )
				continue;

			GUILayout.Space( 4f );
			GUILayout.Label( hall.HallId + " — level " + hall.CurrentLevel + "/" + Mathf.Max( 0, hall.LevelCount - 1 ) );
			string levelId = hall.CurrentLevelId;
			if ( !string.IsNullOrEmpty( levelId ) )
				GUILayout.Label( "  id: " + levelId );

			GUILayout.BeginHorizontal();
			GUILayout.Label( "Force", GUILayout.Width( 40f ) );
			string raw = GUILayout.TextField( _forceLevel.ToString(), GUILayout.Width( 40f ) );
			int parsed;
			if ( int.TryParse( raw, out parsed ) )
				_forceLevel = parsed;

			if ( GUILayout.Button( "Go" ) )
				hall.ForceLevel( _forceLevel );

			if ( GUILayout.Button( "Reset" ) )
			{
				hall.ResetHall();
				_forceLevel = 0;
			}

			GUILayout.EndHorizontal();
		}
	}
}
