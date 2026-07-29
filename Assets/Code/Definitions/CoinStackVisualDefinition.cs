using UnityEngine;

/// <summary>
/// Shared visuals for coin columns (owned stacks, hand, ground, tables).
/// Asset name must be <c>CoinStackVisualDefinition</c> for <see cref="GameInstance.GetDefinition{T}"/>.
/// </summary>
[CreateAssetMenu( fileName = "CoinStackVisualDefinition", menuName = "Definitions/CoinStackVisualDefinition" )]
public class CoinStackVisualDefinition : ScriptableObject
{
	public const int MaxTypeSlots = 8;

	[Header( "Mesh" )]
	[Tooltip( "Single-coin mesh stretched into a stack (e.g. CopperCoin.fbx shared by all metals)." )]
	public Mesh stackMesh;

	[Tooltip( "Optional override for mesh diameter in local units. 0 = use mesh bounds." )]
	[Min( 0f )]
	public float meshReferenceDiameter = 0f;

	[Tooltip( "Optional override for single-coin thickness in local units. 0 = use mesh bounds." )]
	[Min( 0f )]
	public float meshReferenceHeight = 0f;

	[Header( "Materials" )]
	public Material goldStackMaterial;
	public Material silverStackMaterial;
	public Material copperStackMaterial;

	[Tooltip( "Multi-type cylinder material (Texture2DArrays + per-band type map)." )]
	public Material multiStackMaterial;

	[Header( "Multi Type Indices" )]
	[Tooltip( "Ordered type slice indices: Gold=0, Silver=1, Copper=2 by default." )]
	[Range( 0, MaxTypeSlots - 1 )]
	public int goldTypeIndex = 0;

	[Range( 0, MaxTypeSlots - 1 )]
	public int silverTypeIndex = 1;

	[Range( 0, MaxTypeSlots - 1 )]
	public int copperTypeIndex = 2;

	[Tooltip( "How many type slices are filled in the Texture2DArrays." )]
	[Range( 1, MaxTypeSlots )]
	public int typeCount = 3;

	[Header( "Layout" )]
	[Min( 0.01f )]
	public float diameterScale = 1f;

	[Tooltip( "How quickly stack height eases when count changes. 0 = snap." )]
	[Min( 0f )]
	public float heightLerpSpeed = 18f;

	[Tooltip( "Minimum settled coins before bulk meshes are replaced by the stack visual." )]
	[Min( 1 )]
	public int minCountForCylinder = 2;

	[Tooltip( "Max coins in a player-made ground stack. 0 = unlimited." )]
	[Min( 0 )]
	public int groundMaxStackHeight = 1000;

	public Material ResolveMaterial( TreasureDefinition treasure )
	{
		string variant = treasure != null ? treasure.variant : null;
		if ( !string.IsNullOrEmpty( variant ) )
		{
			if ( variant.IndexOf( "silver", System.StringComparison.OrdinalIgnoreCase ) >= 0 && silverStackMaterial != null )
				return silverStackMaterial;
			if ( variant.IndexOf( "copper", System.StringComparison.OrdinalIgnoreCase ) >= 0 && copperStackMaterial != null )
				return copperStackMaterial;
			if ( variant.IndexOf( "gold", System.StringComparison.OrdinalIgnoreCase ) >= 0 && goldStackMaterial != null )
				return goldStackMaterial;
		}

		if ( goldStackMaterial != null )
			return goldStackMaterial;
		if ( silverStackMaterial != null )
			return silverStackMaterial;
		return copperStackMaterial;
	}

	/// <summary>
	/// Texture2DArray slice index for <paramref name="treasure"/>. Unknown variants fall back to gold.
	/// </summary>
	public int ResolveTypeIndex( TreasureDefinition treasure )
	{
		int count = typeCount > 0 ? typeCount : 3;
		int maxIndex = Mathf.Max( 0, Mathf.Min( count, MaxTypeSlots ) - 1 );
		string variant = treasure != null ? treasure.variant : null;
		int index = goldTypeIndex;
		if ( !string.IsNullOrEmpty( variant ) )
		{
			if ( variant.IndexOf( "silver", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
				index = silverTypeIndex;
			else if ( variant.IndexOf( "copper", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
				index = copperTypeIndex;
			else if ( variant.IndexOf( "gold", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
				index = goldTypeIndex;
		}

		return Mathf.Clamp( index, 0, maxIndex );
	}

	public void GetMeshReferenceSize( out float diameter, out float height )
	{
		diameter = meshReferenceDiameter;
		height = meshReferenceHeight;

		if ( ( diameter > 0.0001f && height > 0.0001f ) || stackMesh == null )
		{
			if ( diameter <= 0.0001f )
				diameter = 1f;
			if ( height <= 0.0001f )
				height = 0.1f;
			return;
		}

		Bounds bounds = stackMesh.bounds;
		if ( diameter <= 0.0001f )
			diameter = Mathf.Max( bounds.size.x, bounds.size.z );
		if ( height <= 0.0001f )
			height = bounds.size.y;

		if ( diameter <= 0.0001f )
			diameter = 1f;
		if ( height <= 0.0001f )
			height = 0.1f;
	}

	public void GetMeshBoundsY( out float minY, out float sizeY )
	{
		if ( stackMesh != null )
		{
			Bounds bounds = stackMesh.bounds;
			minY = bounds.min.y;
			sizeY = Mathf.Max( 0.0001f, bounds.size.y );
			return;
		}

		GetMeshReferenceSize( out _, out float height );
		minY = -height * 0.5f;
		sizeY = height;
	}
}
