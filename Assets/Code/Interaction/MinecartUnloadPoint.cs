using UnityEngine;

/// <summary>
/// Interactable dump station: one Interact empties a nearby minecart via <see cref="IMinecartUnloadReceiver"/>.
/// Scene setup: collider + this component; add <see cref="MinecartDropWorldUnloadReceiver"/> (default) or workshop stub.
/// </summary>
[RequireComponent( typeof( Collider ) )]
public class MinecartUnloadPoint : InteractableBase
{
	static readonly Collider[] OverlapBuffer = new Collider[ 32 ];

	[SerializeField]
	MinecartDefinition definition;

	[Tooltip( "Override detect radius. Negative uses MinecartDefinition.unloadDetectRadius." )]
	[SerializeField]
	float detectRadiusOverride = -1f;

	[Tooltip( "Receiver used on Interact. Defaults to DropWorld on this object." )]
	[SerializeField]
	MonoBehaviour unloadReceiver;

	void Reset()
	{
		SetInteractionName( "Unload Minecart" );
	}

	void Awake()
	{
		if ( string.IsNullOrEmpty( InteractionName ) || InteractionName == "Interactable" )
			SetInteractionName( "Unload Minecart" );

		EnsureDefaultReceiver();
	}

	void EnsureDefaultReceiver()
	{
		if ( unloadReceiver != null )
			return;

		MinecartDropWorldUnloadReceiver drop = GetComponent<MinecartDropWorldUnloadReceiver>();
		if ( drop == null )
			drop = gameObject.AddComponent<MinecartDropWorldUnloadReceiver>();

		unloadReceiver = drop;
	}

	float DetectRadius
	{
		get
		{
			if ( detectRadiusOverride >= 0f )
				return detectRadiusOverride;

			if ( definition != null )
				return definition.unloadDetectRadius;

			return 3f;
		}
	}

	public override bool CanInteract( PlayerController player )
	{
		if ( !IsAvailable || player == null )
			return false;

		return TryFindLoadedCart( out _ );
	}

	public override void Interact( PlayerController player )
	{
		if ( !TryFindLoadedCart( out MinecartInteractable cart ) )
			return;

		IMinecartUnloadReceiver receiver = ResolveReceiver();
		if ( receiver == null )
			return;

		receiver.TryUnload( cart );
	}

	IMinecartUnloadReceiver ResolveReceiver()
	{
		EnsureDefaultReceiver();
		return unloadReceiver as IMinecartUnloadReceiver;
	}

	bool TryFindLoadedCart( out MinecartInteractable cart )
	{
		cart = null;
		float radius = DetectRadius;
		int count = Physics.OverlapSphereNonAlloc( transform.position, radius, OverlapBuffer );
		float bestDist = float.MaxValue;

		for ( int i = 0; i < count; i++ )
		{
			Collider col = OverlapBuffer[ i ];
			if ( col == null )
				continue;

			MinecartInteractable candidate = col.GetComponentInParent<MinecartInteractable>();
			if ( candidate == null || candidate.ItemCount <= 0 )
				continue;

			float dist = ( candidate.transform.position - transform.position ).sqrMagnitude;
			if ( dist >= bestDist )
				continue;

			bestDist = dist;
			cart = candidate;
		}

		return cart != null;
	}
}
