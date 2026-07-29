using System.Collections.Generic;

using UnityEngine;

public static class HoverOutlineTargetUtility
{
	static readonly List<Renderer> Buffer = new List<Renderer>( 8 );

	public static bool ShouldHighlight( TreasureItem item )
	{
		if ( item == null || item.Definition == null )
			return false;

		return item.Definition.category != TreasureCategory.Coin;
	}

	public static IReadOnlyList<Renderer> CollectRenderers( TreasureItem item )
	{
		Buffer.Clear();
		if ( item == null )
			return Buffer;

		MeshRenderer[] renderers = item.GetComponentsInChildren<MeshRenderer>( true );
		for ( int i = 0; i < renderers.Length; i++ )
		{
			MeshRenderer renderer = renderers[ i ];
			if ( renderer == null || !renderer.enabled || renderer.sharedMaterial == null )
				continue;

			Buffer.Add( renderer );
		}

		return Buffer;
	}
}
