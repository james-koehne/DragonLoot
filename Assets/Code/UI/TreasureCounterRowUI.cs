using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in the treasure counter HUD: icon, name, Sorted / Total, completion state.
/// </summary>
public class TreasureCounterRowUI : MonoBehaviour
{
	Image _icon;
	Text _nameLabel;
	Text _countLabel;
	Image _checkmark;
	Image _background;
	Outline _flashOutline;

	Color _idleBg = new Color( 0f, 0f, 0f, 0.45f );
	Color _completeBg = new Color( 0.12f, 0.35f, 0.18f, 0.65f );
	Color _flashColor = new Color( 1f, 0.92f, 0.35f, 1f );

	float _flashUntil;
	string _boundId;

	public string BoundId => _boundId;

	public void Build()
	{
		RectTransform root = gameObject.GetComponent<RectTransform>();
		if ( root == null )
			root = gameObject.AddComponent<RectTransform>();

		root.sizeDelta = new Vector2( 280f, 44f );

		LayoutElement layoutElement = gameObject.GetComponent<LayoutElement>();
		if ( layoutElement == null )
			layoutElement = gameObject.AddComponent<LayoutElement>();
		layoutElement.minHeight = 44f;
		layoutElement.preferredHeight = 44f;
		layoutElement.flexibleWidth = 1f;

		_background = gameObject.GetComponent<Image>();
		if ( _background == null )
			_background = gameObject.AddComponent<Image>();
		_background.color = _idleBg;
		_background.raycastTarget = false;

		_flashOutline = gameObject.GetComponent<Outline>();
		if ( _flashOutline == null )
			_flashOutline = gameObject.AddComponent<Outline>();
		_flashOutline.effectColor = _flashColor;
		_flashOutline.effectDistance = new Vector2( 2f, 2f );
		_flashOutline.enabled = false;

		_icon = CreateChildImage( "Icon", new Vector2( 4f, 4f ), new Vector2( 36f, 36f ) );
		_checkmark = CreateChildImage( "Check", new Vector2( 248f, 8f ), new Vector2( 24f, 24f ) );
		_checkmark.color = new Color( 0.45f, 1f, 0.55f, 1f );
		_checkmark.enabled = false;
		_checkmark.gameObject.SetActive( false );

		_nameLabel = CreateChildText( "Name", new Vector2( 44f, 2f ), new Vector2( 140f, 40f ), TextAnchor.MiddleLeft, 20 );
		_countLabel = CreateChildText( "Count", new Vector2( 180f, 2f ), new Vector2( 64f, 40f ), TextAnchor.MiddleRight, 20 );
	}

	public void Bind( TreasureCounterEntry entry, bool flash )
	{
		if ( entry == null )
			return;

		_boundId = entry.Id;

		if ( _icon != null )
		{
			_icon.sprite = entry.Icon;
			_icon.enabled = entry.Icon != null;
			_icon.color = entry.Icon != null ? Color.white : new Color( 1f, 1f, 1f, 0.15f );
		}

		if ( _nameLabel != null )
			_nameLabel.text = entry.DisplayName;

		if ( _countLabel != null )
			_countLabel.text = entry.Sorted + " / " + entry.Total;

		bool completed = entry.Completed;
		if ( _background != null )
			_background.color = completed ? _completeBg : _idleBg;

		if ( _checkmark != null )
		{
			_checkmark.gameObject.SetActive( completed );
			_checkmark.enabled = completed;
		}

		if ( flash && completed )
		{
			_flashUntil = Time.unscaledTime + 0.85f;
			if ( _flashOutline != null )
				_flashOutline.enabled = true;
		}
	}

	void Update()
	{
		if ( _flashOutline == null || !_flashOutline.enabled )
			return;

		if ( Time.unscaledTime >= _flashUntil )
			_flashOutline.enabled = false;
	}

	Image CreateChildImage( string name, Vector2 anchoredPos, Vector2 size )
	{
		GameObject go = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		go.transform.SetParent( transform, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0f, 0.5f );
		rect.anchorMax = new Vector2( 0f, 0.5f );
		rect.pivot = new Vector2( 0f, 0.5f );
		rect.anchoredPosition = new Vector2( anchoredPos.x, 0f );
		rect.sizeDelta = size;

		Image image = go.GetComponent<Image>();
		image.raycastTarget = false;
		return image;
	}

	Text CreateChildText( string name, Vector2 anchoredPos, Vector2 size, TextAnchor align, int fontSize )
	{
		GameObject go = new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		go.transform.SetParent( transform, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0f, 0.5f );
		rect.anchorMax = new Vector2( 0f, 0.5f );
		rect.pivot = new Vector2( 0f, 0.5f );
		rect.anchoredPosition = new Vector2( anchoredPos.x, 0f );
		rect.sizeDelta = size;

		Text text = go.GetComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( text.font == null )
			text.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		text.fontSize = fontSize;
		text.alignment = align;
		text.color = Color.white;
		text.raycastTarget = false;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		return text;
	}
}
