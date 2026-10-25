/// <summary>
/// Proximity dig objectives for individual gold piles (<c>obj_pile_*</c>).
/// Quiet HUD behaviour: no new-objective toast, no show SFX ceremony, no quest outlines.
/// </summary>
public static class GoldPileObjectiveHandler
{
	public const string IdPrefix = "obj_pile_";

	public static bool IsGoldPileObjective( ObjectiveDefinition definition )
	{
		if ( definition == null || string.IsNullOrEmpty( definition.id ) )
			return false;
		return IsGoldPileObjectiveId( definition.id );
	}

	public static bool IsGoldPileObjectiveId( string objectiveId )
	{
		if ( string.IsNullOrEmpty( objectiveId ) )
			return false;
		return objectiveId.StartsWith( IdPrefix, System.StringComparison.Ordinal );
	}

	/// <summary>Gold pile digs never fire the "New objective" toast.</summary>
	public static bool ShouldAnnounceOnHud( ObjectiveDefinition definition )
	{
		return !IsGoldPileObjective( definition );
	}

	/// <summary>Gold pile digs do not contribute quest outline roots.</summary>
	public static bool ShouldContributeOutline( ObjectiveDefinition definition )
	{
		return !IsGoldPileObjective( definition );
	}

	/// <summary>
	/// True when every top-level HUD row is a gold pile dig (or there are no story rows).
	/// Used so the Tasks panel can appear/update without show SFX.
	/// </summary>
	public static bool IsQuietHud( TutorialHudChangedEvent evt )
	{
		if ( evt.Cleared || evt.Rows == null || evt.Rows.Length == 0 )
			return false;

		bool anyTopLevel = false;
		for ( int i = 0; i < evt.Rows.Length; i++ )
		{
			TutorialHudRow row = evt.Rows[ i ];
			if ( row.Indent != 0 )
				continue;
			anyTopLevel = true;
			if ( !IsGoldPileObjectiveId( row.ObjectiveId ) )
				return false;
		}

		return anyTopLevel;
	}

	/// <summary>Objective key that ignores gold pile digs so they do not trigger show feedback.</summary>
	public static string BuildStoryObjectiveKey( TutorialHudChangedEvent evt )
	{
		if ( evt.Cleared || evt.Rows == null || evt.Rows.Length == 0 )
			return string.Empty;

		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		for ( int i = 0; i < evt.Rows.Length; i++ )
		{
			TutorialHudRow row = evt.Rows[ i ];
			if ( row.Indent != 0 )
				continue;
			if ( IsGoldPileObjectiveId( row.ObjectiveId ) )
				continue;
			if ( sb.Length > 0 )
				sb.Append( '|' );
			sb.Append( row.ObjectiveId );
		}

		return sb.ToString();
	}
}
