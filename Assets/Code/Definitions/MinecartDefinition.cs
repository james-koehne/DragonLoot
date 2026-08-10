using UnityEngine;

[CreateAssetMenu( fileName = "MinecartDefinition", menuName = "Definitions/MinecartDefinition" )]
public class MinecartDefinition : ScriptableObject
{
	[Header( "Weight" )]
	[Tooltip( "Maximum total TreasureDefinition.weight the cart may hold." )]
	[Min( 1 )]
	public int maxWeight = 40;

	[Header( "Push" )]
	[Tooltip( "Planar push speed when the cart is empty." )]
	[Min( 0.1f )]
	public float emptyPushSpeed = 3.5f;

	[Tooltip( "Planar push speed when the cart is at max weight." )]
	[Min( 0.05f )]
	public float fullPushSpeed = 1.25f;

	[Tooltip( "Max distance from player to cart center to keep a push session alive." )]
	[Min( 0.5f )]
	public float pushAttachRadius = 2.5f;

	[Header( "Cargo Grid" )]
	[Min( 1 )]
	public int gridColumns = 4;

	[Min( 1 )]
	public int gridRows = 3;

	[Tooltip( "Local-space distance between adjacent cell centers." )]
	[Min( 0.05f )]
	public float cellSpacing = 0.22f;

	[Tooltip( "0 = unlimited vertical stack height per footprint for canStack treasure." )]
	[Min( 0 )]
	public int maxStackPerCell = 0;

	[Header( "Unload" )]
	[Tooltip( "Default radius used by unload points to detect a nearby cart." )]
	[Min( 0.5f )]
	public float unloadDetectRadius = 3f;

	[Tooltip( "World scatter radius when dumping cargo at an unload spout." )]
	[Min( 0f )]
	public float unloadScatterRadius = 0.35f;

	void OnValidate()
	{
		maxWeight = Mathf.Max( 1, maxWeight );
		emptyPushSpeed = Mathf.Max( 0.1f, emptyPushSpeed );
		fullPushSpeed = Mathf.Max( 0.05f, fullPushSpeed );
		pushAttachRadius = Mathf.Max( 0.5f, pushAttachRadius );
		gridColumns = Mathf.Max( 1, gridColumns );
		gridRows = Mathf.Max( 1, gridRows );
		cellSpacing = Mathf.Max( 0.05f, cellSpacing );
		maxStackPerCell = Mathf.Max( 0, maxStackPerCell );
		unloadDetectRadius = Mathf.Max( 0.5f, unloadDetectRadius );
		unloadScatterRadius = Mathf.Max( 0f, unloadScatterRadius );
	}
}
