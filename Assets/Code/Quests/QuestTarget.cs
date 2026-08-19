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
	/// </summary>
	public Transform ResolveOutlineRoot()
	{
		Transform self = transform;
		if ( HoverOutlineTargetUtility.HasEnabledMeshRenderers( self.gameObject ) )
			return self;

		Transform parent = self.parent;
		if ( parent != null )
			return parent;

		return self;
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
