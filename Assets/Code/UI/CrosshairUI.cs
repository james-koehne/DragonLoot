using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
	static CrosshairUI _instance;

	[Header( "Colours" )]
	public Color idleColor = Color.white;
	public Color focusedColor = new Color( 1f, 0.85f, 0.2f, 1f );

	[Header( "Layout" )]
	public float size = 8f;
	public float contextualRingSize = 20f;

	[SerializeField] CanvasGroup _canvasGroup;

	Image _image;
	Image _contextualRing;
	bool _visible;

	public static CrosshairUI Instance => _instance;

	void Awake()
	{
		_instance = this;
		EnsureCanvasGroup();
	}

	void OnDestroy()
	{
		if ( _instance == this )
			_instance = null;
	}

	public void Setup()
	{
		if ( _instance == null )
			_instance = this;

		EnsureImage();
		EnsureContextualRing();
		EnsureCanvasGroup();
		SetVisible( true );
		SetAlpha( 1f );
		ApplyState( false, false );
	}

	public void SetVisible( bool visible )
	{
		_visible = visible;
		EnsureImage();
		EnsureContextualRing();
		if ( _image != null )
			_image.enabled = visible;
		if ( _contextualRing != null && !visible )
			_contextualRing.enabled = false;
	}

	public void SetAlpha( float alpha )
	{
		EnsureCanvasGroup();
		if ( _canvasGroup != null )
		{
			_canvasGroup.alpha = Mathf.Clamp01( alpha );
			_canvasGroup.blocksRaycasts = false;
			_canvasGroup.interactable = false;
		}
	}

	void Update()
	{
		if ( !_visible )
			return;

		bool focused = false;
		bool contextual = false;
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
		{
			PlayerInteraction interaction = GameMode.Instance.Player.Interaction;
			if ( interaction != null )
			{
				focused = interaction.HasInteractableFocus;
				contextual = interaction.HasContextualInteractFocus;
			}
		}

		ApplyState( focused, contextual );
	}

	void EnsureImage()
	{
		if ( _image != null )
			return;

		Transform named = transform.Find( "Dot" );
		if ( named == null )
			named = transform.Find( "Crosshair" );
		if ( named != null )
			_image = named.GetComponent<Image>();
		if ( _image != null )
			return;

		Image[] images = GetComponentsInChildren<Image>( true );
		for ( int i = 0; i < images.Length; i++ )
		{
			string n = images[ i ].gameObject.name;
			if ( n == "ContextualRing" || n == "ProgressRing" || n == "MovementIcon" )
				continue;
			_image = images[ i ];
			break;
		}

		if ( _image != null )
			return;

		GameObject crosshairObj = new GameObject( "Dot", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
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

	void EnsureContextualRing()
	{
		if ( _contextualRing != null )
			return;

		Transform existing = transform.Find( "ContextualRing" );
		if ( existing != null )
			_contextualRing = existing.GetComponent<Image>();

		if ( _contextualRing == null )
		{
			GameObject ringGo = new GameObject( "ContextualRing", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			ringGo.transform.SetParent( transform, false );
			_contextualRing = ringGo.GetComponent<Image>();
		}

		RectTransform rect = _contextualRing.rectTransform;
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2( contextualRingSize, contextualRingSize );
		rect.SetAsFirstSibling();

		_contextualRing.raycastTarget = false;
		_contextualRing.color = focusedColor;
		if ( _contextualRing.sprite == null )
			_contextualRing.sprite = CreateRingSprite();
		_contextualRing.enabled = false;
	}

	void ApplyState( bool focused, bool contextual )
	{
		EnsureImage();
		EnsureContextualRing();
		if ( _image == null )
			return;

		Color color = focused ? focusedColor : idleColor;
		_image.color = color;

		if ( _contextualRing == null )
			return;

		_contextualRing.rectTransform.sizeDelta = new Vector2( contextualRingSize, contextualRingSize );
		_contextualRing.color = focusedColor;
		_contextualRing.enabled = _visible && contextual;
	}

	void EnsureCanvasGroup()
	{
		if ( _canvasGroup != null )
			return;

		_canvasGroup = GetComponent<CanvasGroup>();
		if ( _canvasGroup == null )
			_canvasGroup = gameObject.AddComponent<CanvasGroup>();

		_canvasGroup.blocksRaycasts = false;
		_canvasGroup.interactable = false;
	}

	static Sprite CreateRingSprite()
	{
		const int texSize = 64;
		const float outer = 0.48f;
		const float inner = 0.34f;
		Texture2D tex = new Texture2D( texSize, texSize, TextureFormat.RGBA32, false );
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;

		Color clear = new Color( 0f, 0f, 0f, 0f );
		Color solid = Color.white;
		float center = ( texSize - 1 ) * 0.5f;
		for ( int y = 0; y < texSize; y++ )
		{
			for ( int x = 0; x < texSize; x++ )
			{
				float dx = ( x - center ) / texSize;
				float dy = ( y - center ) / texSize;
				float r = Mathf.Sqrt( dx * dx + dy * dy );
				tex.SetPixel( x, y, r <= outer && r >= inner ? solid : clear );
			}
		}

		tex.Apply( false, false );
		return Sprite.Create( tex, new Rect( 0f, 0f, texSize, texSize ), new Vector2( 0.5f, 0.5f ), 100f );
	}
}
