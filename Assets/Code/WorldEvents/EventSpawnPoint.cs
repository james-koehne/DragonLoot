using UnityEngine;

/// <summary>
/// Named spawn transform for <see cref="WorldEventActionType.SpawnAddressable"/>.
/// </summary>
[DisallowMultipleComponent]
public class EventSpawnPoint : MonoBehaviour
{
	[SerializeField]
	string id;

	public string Id => id;

	public Transform SpawnTransform => transform;

	void OnEnable()
	{
		EventTargetRegistry.Register( this );
	}

	void OnDisable()
	{
		EventTargetRegistry.Unregister( this );
	}

	public void SetId( string value )
	{
		id = value;
		if ( isActiveAndEnabled )
			EventTargetRegistry.Register( this );
	}
}
