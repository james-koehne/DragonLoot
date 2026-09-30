using UnityEngine;

[CreateAssetMenu( fileName = "MinecartStationDefinition", menuName = "Definitions/MinecartStationDefinition" )]
public class MinecartStationDefinition : ScriptableObject
{
	[Tooltip( "If no track is assigned, bind the nearest track within this radius on play." )]
	[Min( 0.5f )]
	public float trackSnapRadius = 6f;

	[Tooltip( "Seconds between each single-item transfer while docked." )]
	[Min( 0.05f )]
	public float transferInterval = 0.25f;

	[Tooltip( "Leave the dock after this many seconds with no successful transfer." )]
	[Min( 0.5f )]
	public float inactivitySeconds = 10f;

	void OnValidate()
	{
		trackSnapRadius = Mathf.Max( 0.5f, trackSnapRadius );
		transferInterval = Mathf.Max( 0.05f, transferInterval );
		inactivitySeconds = Mathf.Max( 0.5f, inactivitySeconds );
	}
}
