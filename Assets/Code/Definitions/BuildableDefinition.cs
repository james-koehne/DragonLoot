using UnityEngine;

[CreateAssetMenu( fileName = "BuildableDefinition", menuName = "Definitions/BuildableDefinition" )]
public class BuildableDefinition : ScriptableObject
{
	[Header( "Identity" )]
	public string id;

	public string displayName;

	[TextArea( 2, 4 )]
	public string description;

	public Sprite icon;

	[Header( "Cost" )]
	[Tooltip( "Build cost in currency. 0 displays as Free. Spend logic is not implemented yet." )]
	[Min( 0 )]
	public int cost;

	public string ResolveDisplayName()
	{
		if ( !string.IsNullOrEmpty( displayName ) )
			return displayName;
		if ( !string.IsNullOrEmpty( id ) )
			return id;
		return name;
	}

	public string ResolveDescription()
	{
		return description ?? string.Empty;
	}

	public int ResolveCost()
	{
		return Mathf.Max( 0, cost );
	}

	void OnValidate()
	{
		cost = Mathf.Max( 0, cost );
	}
}
