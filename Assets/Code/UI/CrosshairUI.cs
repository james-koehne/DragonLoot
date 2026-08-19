using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
	[Header( "Colours" )]
	public Color idleColor = Color.white;
	public Color focusedColor = new Color( 1f, 0.85f, 0.2f, 1f );

	[Header( "Layout" )]
	public float size = 8f;

	Image _image;
	bool _visible;

	public void Setup()
	{
		EnsureImage();
		SetVisible( true );
		ApplyColor( false );
	}

	public void SetVisible( bool visible )
	{
		_visible = visible;
		EnsureImage();
		if ( _image != null )
			_image.enabled = visible;
	}

	void Update()
	{
		if ( !_visible )
			return;

		bool focused = false;
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
		{
			PlayerInteraction interaction = GameMode.Instance.Player.Interaction;
			if ( interaction != null )
				focused = interaction.HasInteractableFocus;
		}

		ApplyColor( focused );
	}

	void EnsureImage()
	{
		if ( _image != null )
			return;

		_image = GetComponentInChildren<Image>( true );
		if ( _image != null )
			return;

		GameObject crosshairObj = new GameObject( "Crosshair", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		crosshairObj.transform.SetParent( transform, false );

		RectTransform rect = crosshairObj.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2( size, size );

		_image = crosshairObj.GetComponent<Image>();
		_image.raycastTarget = false;
		_image.color = idleColor;
	}

	void ApplyColor( bool focused )
	{
		EnsureImage();
		if ( _image == null )
			return;

		_image.color = focused ? focusedColor : idleColor;
	}
}
