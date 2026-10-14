using System;

using UnityEngine;

/// <summary>
/// One junk input → resource outputs for the recycling machine.
/// </summary>
[CreateAssetMenu( fileName = "RecyclingRecipe", menuName = "Definitions/RecyclingRecipeDefinition" )]
public class RecyclingRecipeDefinition : ScriptableObject
{
	[Tooltip( "Junk treasure this recipe consumes." )]
	public TreasureDefinition input;

	[Tooltip( "Resources spawned when the input is shredded." )]
	public RecyclingOutputEntry[] outputs;

	[Tooltip( "Play wood chew VFX/SFX when this recipe is processed." )]
	public bool chewWood = true;

	[Tooltip( "Play metal chew VFX/SFX when this recipe is processed." )]
	public bool chewMetal;
}

[Serializable]
public class RecyclingOutputEntry
{
	public TreasureDefinition resource;

	[Min( 1 )]
	public int count = 1;
}
