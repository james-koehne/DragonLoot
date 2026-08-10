using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Default unload receiver: extracts all cart cargo and drops it into the treasure surface / world.
/// </summary>
public class MinecartDropWorldUnloadReceiver : MonoBehaviour, IMinecartUnloadReceiver
{
	static readonly List<TreasureItem> CargoBuffer = new List<TreasureItem>( 64 );

	[Tooltip( "World origin for dropped items. Defaults to this transform." )]
	[SerializeField]
	Transform spout;

	[SerializeField]
	[Min( 0f )]
	float scatterRadius = -1f;

	public bool TryUnload( MinecartInteractable cart )
	{
		if ( cart == null || cart.ItemCount <= 0 )
			return false;

		Transform dropRoot = spout != null ? spout : transform;
		float scatter = scatterRadius >= 0f ? scatterRadius : cart.UnloadScatterRadius;

		cart.ExtractAllCargo( CargoBuffer );
		if ( CargoBuffer.Count == 0 )
			return false;

		for ( int i = 0; i < CargoBuffer.Count; i++ )
		{
			TreasureItem item = CargoBuffer[ i ];
			if ( item == null )
				continue;

			Vector3 offset = Random.insideUnitSphere * scatter;
			offset.y = Mathf.Abs( offset.y ) * 0.25f;
			Vector3 pos = dropRoot.position + offset;
			Quaternion rot = dropRoot.rotation;
			if ( TreasureItem.UsesSurfaceSimulation( item.Definition ) )
				item.EnterSurface( pos, rot, Vector3.zero );
			else
				item.EnterPhysics( pos, rot, Vector3.zero );
		}

		CargoBuffer.Clear();
		return true;
	}
}
