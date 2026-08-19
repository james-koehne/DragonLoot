using UnityEngine;

/// <summary>
/// Trigger volume that publishes <see cref="DoorUnlockedEvent"/> when the player enters.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent( typeof( Collider ) )]
public class DoorUnlockEmitter : MonoBehaviour
{
	[SerializeField]
	string doorId;

	[SerializeField]
	bool triggerOnce;

	bool _triggered;

	public string DoorId => doorId;

	void Reset()
	{
		Collider col = GetComponent<Collider>();
		if ( col != null )
			col.isTrigger = true;
	}

	public void SetDoorId( string value )
	{
		doorId = value;
	}

	public void ResetTriggered()
	{
		_triggered = false;
	}

	void OnTriggerEnter( Collider other )
	{
		if ( string.IsNullOrEmpty( doorId ) )
			return;

		if ( triggerOnce && _triggered )
			return;

		if ( !IsPlayer( other ) )
			return;

		_triggered = true;
		EventBus.Publish( new DoorUnlockedEvent { DoorId = doorId } );
	}

	static bool IsPlayer( Collider other )
	{
		if ( other == null )
			return false;

		PlayerController player = other.GetComponentInParent<PlayerController>();
		return player != null;
	}
}
