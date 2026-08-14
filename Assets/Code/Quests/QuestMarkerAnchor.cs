using UnityEngine;

/// <summary>
/// Optional marker height offset for compass / world pip.
/// </summary>
[DisallowMultipleComponent]
public class QuestMarkerAnchor : MonoBehaviour
{
	[SerializeField]
	Vector3 worldOffset = new Vector3( 0f, 2f, 0f );

	[SerializeField]
	Transform overrideTransform;

	Transform _runtimeAnchor;

	public Transform MarkerTransform
	{
		get
		{
			if ( overrideTransform != null )
				return overrideTransform;

			EnsureRuntimeAnchor();
			return _runtimeAnchor;
		}
	}

	void EnsureRuntimeAnchor()
	{
		if ( _runtimeAnchor != null )
			return;

		GameObject go = new GameObject( "QuestMarkerAnchorPoint" );
		_runtimeAnchor = go.transform;
		_runtimeAnchor.SetParent( transform, false );
		_runtimeAnchor.localPosition = worldOffset;
		_runtimeAnchor.localRotation = Quaternion.identity;
	}

	void OnValidate()
	{
		if ( _runtimeAnchor != null )
			_runtimeAnchor.localPosition = worldOffset;
	}
}
