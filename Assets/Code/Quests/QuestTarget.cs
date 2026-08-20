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
	string areaId;

	[SerializeField]
	QuestMarkerAnchor markerAnchor;

	public string Id => id;

	public string AreaId => areaId;

	public Transform MarkerTransform
	{
		get
		{
			if ( markerAnchor != null )
				return markerAnchor.MarkerTransform;
			return transform;
		}
	}

	/// <summary>
	/// Host transform for quest outlines. Scene targets are often marker children with no meshes.
	/// Walks to the nearest ancestor with meshes, but never expands to a broad area root.
	/// </summary>
	public Transform ResolveOutlineRoot()
	{
		Transform current = transform;
		if ( HoverOutlineTargetUtility.HasEnabledMeshRenderers( current.gameObject ) )
			return current;

		Transform parent = current.parent;
		while ( parent != null )
		{
			if ( IsBroadAreaRoot( parent ) )
				return current;

			if ( HoverOutlineTargetUtility.HasEnabledMeshRenderers( parent.gameObject ) )
				return parent;

			current = parent;
			parent = parent.parent;
		}

		return transform;
	}

	static bool IsBroadAreaRoot( Transform candidate )
	{
		return candidate != null && candidate.name == "StartingArea";
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

	public void SetAreaId( string value )
	{
		areaId = value;
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
