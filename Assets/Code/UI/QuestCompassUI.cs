using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top NESW compass strip with tutorial marker pip + distance.
/// Wire references on the Interface prefab.
/// </summary>
public class QuestCompassUI : MonoBehaviour
{
	const float HalfWidth = 440f;

	[SerializeField] CanvasGroup group;
	[SerializeField] RectTransform pip;
	[SerializeField] Text distance;
	[SerializeField] Text labelN;
	[SerializeField] Text labelE;
	[SerializeField] Text labelS;
	[SerializeField] Text labelW;

	bool _subscribed;
	bool _hasMarker;
	Vector3 _markerWorld;

	public void Setup()
	{
		Subscribe();
		AttachDistanceToPip();
		if ( group != null )
			group.alpha = 0f;
		if ( pip != null )
			pip.gameObject.SetActive( false );
	}

	void AttachDistanceToPip()
	{
		if ( distance == null || pip == null )
			return;

		RectTransform distRect = distance.rectTransform;
		if ( distRect.parent == pip )
			return;

		distRect.SetParent( pip, false );
		distRect.anchorMin = new Vector2( 0.5f, 0.5f );
		distRect.anchorMax = new Vector2( 0.5f, 0.5f );
		distRect.pivot = new Vector2( 0.5f, 1f );
		distRect.anchoredPosition = new Vector2( 0f, -10f );
		distRect.sizeDelta = new Vector2( 128f, 36f );
	}

	void OnEnable()
	{
		AttachDistanceToPip();
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		Unsubscribe();
	}

	void Update()
	{
		if ( !_hasMarker || group == null || group.alpha <= 0f )
			return;

		UpdateCompass();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<TutorialHudChangedEvent>( OnHudChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<TutorialHudChangedEvent>( OnHudChanged );
		_subscribed = false;
	}

	void OnHudChanged( TutorialHudChangedEvent evt )
	{
		_hasMarker = evt.HasMarker && !evt.Cleared;
		_markerWorld = evt.MarkerWorldPosition;
		if ( group != null )
			group.alpha = _hasMarker ? 1f : 0f;
		if ( pip != null )
			pip.gameObject.SetActive( _hasMarker );
	}

	void UpdateCompass()
	{
		Transform cam = null;
		if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
			cam = GameMode.Instance.cameraController.transform;
		else if ( Camera.main != null )
			cam = Camera.main.transform;

		if ( cam == null )
			return;

		Vector3 flatForward = cam.forward;
		flatForward.y = 0f;
		if ( flatForward.sqrMagnitude < 0.0001f )
			flatForward = Vector3.forward;
		flatForward.Normalize();

		float yaw = Mathf.Atan2( flatForward.x, flatForward.z ) * Mathf.Rad2Deg;

		PlaceCardinal( labelN, 0f - yaw );
		PlaceCardinal( labelE, 90f - yaw );
		PlaceCardinal( labelS, 180f - yaw );
		PlaceCardinal( labelW, -90f - yaw );

		Vector3 toMarker = _markerWorld - cam.position;
		toMarker.y = 0f;
		if ( distance != null )
			distance.text = TutorialHudDistance.Format( TutorialHudDistance.HorizontalTo( _markerWorld ) );

		if ( toMarker.sqrMagnitude < 0.01f )
		{
			if ( pip != null )
				pip.anchoredPosition = Vector2.zero;
			return;
		}

		toMarker.Normalize();
		float bearing = Mathf.Atan2( toMarker.x, toMarker.z ) * Mathf.Rad2Deg;
		float relative = Mathf.DeltaAngle( yaw, bearing );
		float x = Mathf.Clamp( relative / 90f, -1f, 1f ) * HalfWidth;
		if ( pip != null )
			pip.anchoredPosition = new Vector2( x, 0f );
	}

	void PlaceCardinal( Text label, float relativeDegrees )
	{
		if ( label == null )
			return;

		float wrapped = Mathf.DeltaAngle( 0f, relativeDegrees );
		float x = Mathf.Clamp( wrapped / 90f, -1.2f, 1.2f ) * HalfWidth;
		label.rectTransform.anchoredPosition = new Vector2( x, 0f );
		Color c = label.color;
		c.a = Mathf.Abs( wrapped ) > 100f ? 0.15f : 0.9f;
		label.color = c;
	}
}
