using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Lookup of recycling recipes by input treasure. Asset name must be
/// <c>RecyclingRecipeCatalog</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "RecyclingRecipeCatalog", menuName = "Definitions/RecyclingRecipeCatalog" )]
public class RecyclingRecipeCatalog : ScriptableObject
{
	public List<RecyclingRecipeDefinition> recipes = new List<RecyclingRecipeDefinition>();

	Dictionary<TreasureDefinition, RecyclingRecipeDefinition> _byInput;

	public void InvalidateCache()
	{
		_byInput = null;
	}

	public bool TryGetRecipe( TreasureDefinition input, out RecyclingRecipeDefinition recipe )
	{
		recipe = null;
		if ( input == null )
			return false;

		EnsureCache();
		return _byInput.TryGetValue( input, out recipe ) && recipe != null;
	}

	public bool HasRecipe( TreasureDefinition input )
	{
		return TryGetRecipe( input, out _ );
	}

	void EnsureCache()
	{
		if ( _byInput != null )
			return;

		_byInput = new Dictionary<TreasureDefinition, RecyclingRecipeDefinition>( 16 );
		if ( recipes == null )
			return;

		for ( int i = 0; i < recipes.Count; i++ )
		{
			RecyclingRecipeDefinition recipe = recipes[ i ];
			if ( recipe == null || recipe.input == null )
				continue;
			_byInput[ recipe.input ] = recipe;
		}
	}

	void OnValidate()
	{
		InvalidateCache();
	}
}
