using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top/bottom cinematic letterbox bars on the Interface canvas.
/// Scene setup: auto-created on Interface at runtime if missing.
/// </summary>
public class CinematicLetterboxUI : MonoBehaviour
{
	static CinematicLetterboxUI _instance;

	[SerializeField] RectTransform _topBar;
	[SerializeField] RectTransform _bottomBar;

	public static CinematicLetterboxUI Instance => _instance;

	public static CinematicLetterboxUI EnsureExists()
	{
		if ( _instance != null )
			return _instance;

		if ( GameMode.Instance == null || GameMode.Instance.InterfaceController == null )
			return null;

		Transform parent = GameMode.Instance.InterfaceController.transform;
		CinematicLetterboxUI existing = parent.GetComponentInChildren<CinematicLetterboxUI>( true );
		if ( existing != null )
			return existing;

		GameObject letterboxObj = new GameObject( "CinematicLetterbox", typeof( RectTransform ), typeof( CinematicLetterboxUI ) );
		letterboxObj.transform.SetParent( parent, false );
		RectTransform root = letterboxObj.GetComponent<RectTransform>();
		root.anchorMin = Vector2.zero;
		root.anchorMax = Vector2.one;
		root.offsetMin = Vector2.zero;
		root.offsetMax = Vector2.zero;
		root.SetAsLastSibling();
		return letterboxObj.GetComponent<CinematicLetterboxUI>();
	}

	void Awake()
	{
		_instance = this;
		EnsureBars();
		SetLetterboxAmount( 0f );
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}

	public void SetLetterboxAmount( float normalizedHeight )
	{
		EnsureBars();
		float height = Mathf.Clamp01( normalizedHeight );
		SetBarHeight( _topBar, height, true );
		SetBarHeight( _bottomBar, height, false );
	}

	void EnsureBars()
	{
		if ( _topBar == null )
			_topBar = CreateBar( "TopBar", true );
		if ( _bottomBar == null )
			_bottomBar = CreateBar( "BottomBar", false );
	}

	RectTransform CreateBar( string name, bool top )
	{
		GameObject barObj = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		barObj.transform.SetParent( transform, false );

		RectTransform rect = barObj.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0f, top ? 1f : 0f );
		rect.anchorMax = new Vector2( 1f, top ? 1f : 0f );
		rect.pivot = new Vector2( 0.5f, top ? 1f : 0f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2( 0f, 0f );

		Image image = barObj.GetComponent<Image>();
		image.color = Color.black;
		image.raycastTarget = false;

		return rect;
	}

	static void SetBarHeight( RectTransform bar, float normalizedHeight, bool top )
	{
		if ( bar == null )
			return;

		float height = Mathf.Clamp01( normalizedHeight );
		bar.anchorMin = new Vector2( 0f, top ? 1f - height : 0f );
		bar.anchorMax = new Vector2( 1f, top ? 1f : height );
		bar.offsetMin = Vector2.zero;
		bar.offsetMax = Vector2.zero;
	}
}
