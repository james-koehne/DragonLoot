using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space quest marker that tracks a world position.
/// Hides within <see cref="hideWithinMeters"/> and only returns after
/// the player has stayed beyond <see cref="showBeyondMeters"/> for a dwell.
/// Wire the marker RectTransform on the Interface prefab.
/// </summary>
public class QuestWorldMarker : MonoBehaviour
{
	[SerializeField] RectTransform marker;
	[SerializeField] CanvasGroup group;
	[SerializeField] Text distance;

	[SerializeField] [Tooltip( "Fade the marker out once the player is this close." )]
	float hideWithinMeters = 3f;
	[SerializeField] [Tooltip( "Marker stays hidden until the player is at least this far away." )]
	float showBeyondMeters = 10f;
	[SerializeField] [Tooltip( "How long the player must remain beyond show distance before the marker fades back in." )]
	float showAfterAwaySeconds = 2.5f;
	[SerializeField] [Tooltip( "Seconds to fade the marker in or out." )]
	float fadeDuration = 0.4f;

	bool _subscribed;
	bool _hasTarget;
	Vector3 _worldPos;
	bool _proximityHidden;
	float _awayTimer;
	float _alpha;

	public void Setup()
	{
		if ( distance == null && marker != null )
			distance = marker.GetComponentInChildren<Text>( true );

		Subscribe();
		_alpha = 0f;
		ApplyAlpha( 0f );
	}

	void OnEnable()
	{
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

	void LateUpdate()
	{
		bool inFront = false;
		if ( _hasTarget && marker != null )
			inFront = UpdateScreenPosition();

		UpdateProximity();

		bool wantVisible = _hasTarget && inFront && !_proximityHidden;
		float targetFade = wantVisible ? 1f : 0f;
		float duration = fadeDuration > 0.01f ? fadeDuration : 0.01f;
		_alpha = Mathf.MoveTowards( _alpha, targetFade, Time.deltaTime / duration );
		ApplyAlpha( Mathf.SmoothStep( 0f, 1f, _alpha ) );
	}

	bool UpdateScreenPosition()
	{
		Camera cam = null;
		if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
			cam = GameMode.Instance.cameraController.GetComponentInChildren<Camera>();
		if ( cam == null )
			cam = Camera.main;
		if ( cam == null )
			return false;

		Vector3 screen = cam.WorldToScreenPoint( _worldPos );
		if ( screen.z <= 0f )
			return false;

		marker.position = screen;
		UpdateDistance();
		return true;
	}

	void UpdateProximity()
	{
		if ( !_hasTarget )
		{
			_proximityHidden = false;
			_awayTimer = 0f;
			return;
		}

		float dist = QuestHudDistance.HorizontalTo( _worldPos );
		if ( dist <= hideWithinMeters )
		{
			_proximityHidden = true;
			_awayTimer = 0f;
			return;
		}

		if ( !_proximityHidden )
			return;

		if ( dist < showBeyondMeters )
		{
			_awayTimer = 0f;
			return;
		}

		_awayTimer += Time.deltaTime;
		if ( _awayTimer >= showAfterAwaySeconds )
			_proximityHidden = false;
	}

	void UpdateDistance()
	{
		if ( distance == null )
			return;

		distance.text = QuestHudDistance.Format( QuestHudDistance.HorizontalTo( _worldPos ) );
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<QuestHudChangedEvent>( OnHudChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<QuestHudChangedEvent>( OnHudChanged );
		_subscribed = false;
	}

	void OnHudChanged( QuestHudChangedEvent evt )
	{
		_hasTarget = evt.HasMarker && !evt.CatalogComplete;
		_worldPos = evt.MarkerWorldPosition;
		_awayTimer = 0f;

		if ( !_hasTarget )
		{
			_proximityHidden = false;
			return;
		}

		_proximityHidden = QuestHudDistance.HorizontalTo( _worldPos ) <= hideWithinMeters;
	}

	void ApplyAlpha( float alpha )
	{
		if ( group != null )
			group.alpha = alpha;
		else if ( marker != null )
			marker.gameObject.SetActive( alpha > 0.01f );
	}
}
