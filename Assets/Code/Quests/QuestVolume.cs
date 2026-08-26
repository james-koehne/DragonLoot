using UnityEngine;

/// <summary>
/// Trigger volume that publishes <see cref="VolumeEnteredEvent"/> when the player enters.
/// Scene volumes are authored as this component.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( Collider ) )]
public class QuestVolume : MonoBehaviour
{
	[SerializeField]
	string id;

	[SerializeField]
	bool triggerOnce;

	bool _triggered;

	public string Id => id;

	void Reset()
	{
		Collider col = GetComponent<Collider>();
		if ( col != null )
			col.isTrigger = true;
	}

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

	public void ResetTriggered()
	{
		_triggered = false;
	}

	void OnTriggerEnter( Collider other )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;

		if ( triggerOnce && _triggered )
			return;

		if ( !IsPlayer( other ) )
			return;

		_triggered = true;
		EventBus.Publish( new VolumeEnteredEvent
		{
			VolumeId = id,
			Volume = this
		} );
	}

	static bool IsPlayer( Collider other )
	{
		if ( other == null )
			return false;

		PlayerController player = other.GetComponentInParent<PlayerController>();
		return player != null;
	}
}

/// <summary>Alias name for world-event volumes. Prefer <see cref="QuestVolume"/> in scenes.</summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( Collider ) )]
public class EventVolume : QuestVolume
{
}
