using UnityEngine;

/// <summary>
/// Tags a scene interactable / station as a quest target by string id.
/// </summary>
[DisallowMultipleComponent]
public class QuestTarget : MonoBehaviour
{
	[SerializeField]
	string id;

	[SerializeField]
	QuestMarkerAnchor markerAnchor;

	public string Id => id;

	public Transform MarkerTransform
	{
		get
		{
			if ( markerAnchor != null )
				return markerAnchor.MarkerTransform;
			return transform;
		}
	}

	void Awake()
	{
		if ( markerAnchor == null )
			markerAnchor = GetComponent<QuestMarkerAnchor>();
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

#if UNITY_EDITOR
	void OnValidate()
	{
		if ( markerAnchor == null )
			markerAnchor = GetComponent<QuestMarkerAnchor>();
	}
#endif
}
