using UnityEngine;

/// <summary>
/// Trigger volume that publishes <see cref="QuestVolumeEnteredEvent"/> when the player enters.
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
		QuestTargetRegistry.Register( this );
	}

	void OnDisable()
	{
		QuestTargetRegistry.Unregister( this );
	}

	public void SetId( string value )
	{
		id = value;
		if ( isActiveAndEnabled )
			QuestTargetRegistry.Register( this );
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
		EventBus.Publish( new QuestVolumeEnteredEvent
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
