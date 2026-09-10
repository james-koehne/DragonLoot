using UnityEngine;

/// <summary>
/// Named player spawn for the debug overlay. Place empties in the Level; do not reuse
/// <see cref="EventSpawnPoint"/> (those ids are for world-event Addressable spawns).
/// </summary>
[DisallowMultipleComponent]
public class DebugSpawnPoint : MonoBehaviour
{
	[SerializeField]
	string id;

	[SerializeField]
	string displayName;

	[SerializeField]
	[Tooltip( "Skip intro world events this session and snap lanterns/fog when spawning here." )]
	bool skipIntro = true;

	public string Id => id;

	public string DisplayName
	{
		get
		{
			if ( !string.IsNullOrEmpty( displayName ) )
				return displayName;
			if ( !string.IsNullOrEmpty( id ) )
				return id;
			return gameObject.name;
		}
	}

	public bool SkipIntro => skipIntro;

	public Transform SpawnTransform => transform;

	void OnEnable()
	{
		DebugSpawnRegistry.Register( this );
	}

	void OnDisable()
	{
		DebugSpawnRegistry.Unregister( this );
	}

	public void SetId( string value )
	{
		id = value;
		if ( isActiveAndEnabled )
			DebugSpawnRegistry.Register( this );
	}
}
