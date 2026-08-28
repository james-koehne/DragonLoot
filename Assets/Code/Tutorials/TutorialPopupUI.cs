using System;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Corner contextual tutorial popup. Non-blocking; advances on timer or dismiss.
/// Uses unscaled time so pause-menu replay works.
/// </summary>
public class TutorialPopupUI : MonoBehaviour
{
	[SerializeField] CanvasGroup group;
	[SerializeField] Text titleText;
	[SerializeField] Text bodyText;
	[SerializeField] Text hintText;
	[SerializeField] Text stepText;
	[SerializeField] Button dismissButton;

	Action _onAdvanced;
	float _hideAt = -1f;
	bool _visible;
	bool _ready;

	public bool IsVisible => _visible;

	public void Setup()
	{
		EnsureRuntimeVisuals();

		if ( dismissButton != null )
		{
			dismissButton.onClick.RemoveListener( OnDismissClicked );
			dismissButton.onClick.AddListener( OnDismissClicked );
		}

		HideImmediate();
		_ready = true;
	}

	void Update()
	{
		if ( !_ready || !_visible )
			return;

		if ( _hideAt > 0f && Time.unscaledTime >= _hideAt )
			Advance();
	}

	public void Show( string title, string body, string hint, int stepIndex, int stepCount, Action onAdvanced )
	{
		EnsureRuntimeVisuals();
		transform.SetAsLastSibling();
		_onAdvanced = onAdvanced;
		_visible = true;

		if ( titleText != null )
			titleText.text = title ?? string.Empty;
		if ( bodyText != null )
			bodyText.text = body ?? string.Empty;
		if ( hintText != null )
		{
			hintText.text = hint ?? string.Empty;
			hintText.gameObject.SetActive( !string.IsNullOrEmpty( hint ) );
		}

		if ( stepText != null )
		{
			bool multi = stepCount > 1;
			stepText.gameObject.SetActive( multi );
			if ( multi )
				stepText.text = stepIndex.ToString() + " / " + stepCount.ToString();
		}

		if ( group != null )
		{
			group.alpha = 1f;
			group.blocksRaycasts = true;
			group.interactable = true;
		}

		float hold = EstimateReadSeconds( body ) + EstimateReadSeconds( hint ) * 0.35f;
		_hideAt = Time.unscaledTime + hold;
	}

	public void Hide()
	{
		_onAdvanced = null;
		HideImmediate();
	}

	void HideImmediate()
	{
		_visible = false;
		_hideAt = -1f;
		if ( group != null )
		{
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}
	}

	void OnDismissClicked()
	{
		Advance();
	}

	void Advance()
	{
		if ( !_visible )
			return;

		Action done = _onAdvanced;
		_onAdvanced = null;
		HideImmediate();
		if ( done != null )
			done();
	}

	void EnsureRuntimeVisuals()
	{
		if ( group != null && titleText != null && bodyText != null )
			return;

		RectTransform rootRect = GetComponent<RectTransform>();
		if ( rootRect == null )
			rootRect = gameObject.AddComponent<RectTransform>();
		rootRect.anchorMin = new Vector2( 1f, 0f );
		rootRect.anchorMax = new Vector2( 1f, 0f );
		rootRect.pivot = new Vector2( 1f, 0f );
		rootRect.anchoredPosition = new Vector2( -36f, 36f );
		rootRect.sizeDelta = new Vector2( 520f, 280f );

		if ( group == null )
			group = gameObject.GetComponent<CanvasGroup>();
		if ( group == null )
			group = gameObject.AddComponent<CanvasGroup>();

		Image bg = gameObject.GetComponent<Image>();
		if ( bg == null )
			bg = gameObject.AddComponent<Image>();
		bg.color = new Color( 0.07f, 0.08f, 0.11f, 0.92f );
		bg.raycastTarget = true;

		Font font = ResolveFont();
		if ( titleText == null )
			titleText = EnsureText( "Title", new Vector2( 0f, 96f ), new Vector2( 480f, 40f ), 30, TextAnchor.MiddleLeft, font );
		if ( bodyText == null )
			bodyText = EnsureText( "Body", new Vector2( 0f, 10f ), new Vector2( 480f, 120f ), 24, TextAnchor.UpperLeft, font );
		if ( hintText == null )
		{
			hintText = EnsureText( "Hint", new Vector2( 0f, -70f ), new Vector2( 480f, 36f ), 22, TextAnchor.MiddleLeft, font );
			hintText.color = new Color( 0.85f, 0.9f, 1f, 0.95f );
		}
		if ( stepText == null )
		{
			stepText = EnsureText( "Step", new Vector2( 180f, 96f ), new Vector2( 120f, 36f ), 22, TextAnchor.MiddleRight, font );
			stepText.color = new Color( 1f, 1f, 1f, 0.65f );
		}
		if ( dismissButton == null )
			dismissButton = EnsureButton( "DismissButton", "OK", new Vector2( 170f, -110f ), font );
	}

	Text EnsureText( string name, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, Font font )
	{
		Transform existing = transform.Find( name );
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = pos;
		rect.sizeDelta = size;

		Text text = go.GetComponent<Text>();
		if ( text == null )
			text = go.AddComponent<Text>();
		text.font = font;
		text.fontSize = fontSize;
		text.fontStyle = FontStyle.Bold;
		text.alignment = align;
		text.color = Color.white;
		text.raycastTarget = false;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		return text;
	}

	Button EnsureButton( string name, string label, Vector2 pos, Font font )
	{
		Transform existing = transform.Find( name );
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( Button ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = pos;
		rect.sizeDelta = new Vector2( 140f, 48f );

		Image image = go.GetComponent<Image>();
		if ( image == null )
			image = go.AddComponent<Image>();
		image.color = new Color( 0.22f, 0.28f, 0.38f, 1f );

		Button button = go.GetComponent<Button>();
		if ( button == null )
			button = go.AddComponent<Button>();

		Transform labelT = go.transform.Find( "Label" );
		GameObject labelGo = labelT != null
			? labelT.gameObject
			: new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		if ( labelT == null )
			labelGo.transform.SetParent( go.transform, false );

		RectTransform labelRect = labelGo.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;

		Text text = labelGo.GetComponent<Text>();
		if ( text == null )
			text = labelGo.AddComponent<Text>();
		text.font = font;
		text.fontSize = 26;
		text.fontStyle = FontStyle.Bold;
		text.alignment = TextAnchor.MiddleCenter;
		text.color = Color.white;
		text.raycastTarget = false;
		text.text = label;
		return button;
	}

	static Font ResolveFont()
	{
		Font font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		return font;
	}

	static float EstimateReadSeconds( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return 1.75f;
		return Mathf.Clamp( text.Length / 16f, 2.25f, 10f );
	}
}
