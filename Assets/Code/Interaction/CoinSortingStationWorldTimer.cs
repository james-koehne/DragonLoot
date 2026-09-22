using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space remaining-time label on the coin sorter. Colored from the energy gauge.
/// Authored on the station prefab above the energy-gauge well.
/// </summary>
public class CoinSortingStationWorldTimer : MonoBehaviour
{
	[SerializeField]
	CoinSortingStation station;

	[SerializeField]
	Canvas canvas;

	[SerializeField]
	CanvasGroup canvasGroup;

	[SerializeField]
	Text label;

	string _lastText;
	Color _lastColor;
	bool _visible;

	public void BindStation( CoinSortingStation owner )
	{
		station = owner;
		EnsureUi();
	}

	void Awake()
	{
		if ( station == null )
			station = GetComponentInParent<CoinSortingStation>();
		EnsureUi();
	}

	void LateUpdate()
	{
		EnsureUi();
		if ( label == null )
			return;

		RefreshDisplay();
	}

	void EnsureUi()
	{
		if ( canvas == null )
			canvas = GetComponent<Canvas>();
		if ( canvas == null )
			canvas = gameObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.WorldSpace;
		canvas.overrideSorting = true;
		if ( canvas.sortingOrder < 40 )
			canvas.sortingOrder = 40;

		if ( canvasGroup == null )
			canvasGroup = GetComponent<CanvasGroup>();
		if ( canvasGroup == null )
			canvasGroup = gameObject.AddComponent<CanvasGroup>();
		canvasGroup.blocksRaycasts = false;
		canvasGroup.interactable = false;
		canvasGroup.ignoreParentGroups = true;

		if ( label == null )
		{
			Transform existingLabel = transform.Find( "Label" );
			if ( existingLabel != null )
				label = existingLabel.GetComponent<Text>();
		}

		if ( label == null )
			label = CreateLabel();

		if ( label.GetComponent<Outline>() == null )
		{
			Outline outline = label.gameObject.AddComponent<Outline>();
			outline.effectColor = new Color( 0f, 0f, 0f, 0.85f );
			outline.effectDistance = new Vector2( 1.2f, -1.2f );
			outline.useGraphicAlpha = true;
		}

		label.raycastTarget = false;
		label.supportRichText = false;
	}

	Text CreateLabel()
	{
		GameObject labelGo = new GameObject( "Label", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		labelGo.transform.SetParent( transform, false );
		RectTransform labelRect = labelGo.GetComponent<RectTransform>();
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = Vector2.zero;
		labelRect.offsetMax = Vector2.zero;

		Text text = labelGo.GetComponent<Text>();
		text.font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( text.font == null )
			text.font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		text.fontSize = 20;
		text.fontStyle = FontStyle.Bold;
		text.alignment = TextAnchor.MiddleCenter;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		return text;
	}

	void RefreshDisplay()
	{
		if ( station == null || !station.TryGetWorldTimerDisplay( out float seconds, out Color color ) )
		{
			SetVisible( false );
			return;
		}

		string text = seconds.ToString( "0.0" ) + "s";
		if ( _lastText != text )
		{
			label.text = text;
			_lastText = text;
		}

		if ( _lastColor != color )
		{
			label.color = color;
			_lastColor = color;
		}

		SetVisible( true );
	}

	void SetVisible( bool visible )
	{
		if ( _visible == visible && canvasGroup != null )
			return;

		_visible = visible;
		if ( canvasGroup != null )
			canvasGroup.alpha = visible ? 1f : 0f;
		else if ( canvas != null )
			canvas.enabled = visible;
	}

#if UNITY_EDITOR
	public void EditorBind( Canvas canvasRef, CanvasGroup groupRef, Text labelRef )
	{
		canvas = canvasRef;
		canvasGroup = groupRef;
		label = labelRef;
	}
#endif
}
