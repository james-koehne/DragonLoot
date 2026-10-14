using System.Text;

using UnityEngine;

public static class GameSavePersistenceIds
{
	public static string Resolve( Component component )
	{
		if ( component == null )
			return string.Empty;

		QuestTarget target = component.GetComponent<QuestTarget>();
		if ( target == null )
			target = component.GetComponentInParent<QuestTarget>();
		if ( target != null && !string.IsNullOrEmpty( target.Id ) )
			return target.Id;

		BuildableObject buildable = component.GetComponent<BuildableObject>();
		if ( buildable == null )
			buildable = component.GetComponentInParent<BuildableObject>();
		if ( buildable != null )
		{
			string buildableId = buildable.ResolvePersistenceId();
			if ( !string.IsNullOrEmpty( buildableId ) )
				return buildableId;
		}

		return BuildHierarchyPath( component.transform );
	}

	public static string BuildHierarchyPath( Transform t )
	{
		if ( t == null )
			return string.Empty;

		StringBuilder sb = new StringBuilder( 64 );
		while ( t != null )
		{
			if ( sb.Length > 0 )
				sb.Insert( 0, '/' );
			sb.Insert( 0, t.name );
			t = t.parent;
		}

		return sb.ToString();
	}
}
