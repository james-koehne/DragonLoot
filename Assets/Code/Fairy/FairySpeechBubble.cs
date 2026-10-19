using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space speech bubble that auto-sizes to text, tracks a world follow target,
/// and clamps itself inside the player's viewport.
/// </summary>
[DisallowMultipleComponent]
public class FairySpeechBubble : MonoBehaviour
{
	[SerializeField]
	Canvas canvas;

	[SerializeField]
	CanvasGroup canvasGroup;

	[SerializeField]
	Text body;

	[SerializeField]
	RectTransform bubbleRoot;

	[SerializeField]
	RectTransform tail;

	[SerializeField]
	[Min( 0.5f )]
	float defaultHoldSeconds = 3.5f;

	[SerializeField]
	[Min( 0f )]
	float fadeSeconds = 0.2f;

	[SerializeField]
	Vector3 worldOffset = new Vector3( 0f, 0.55f, 0f );

	[SerializeField]
	[Min( 0f )]
	float screenPadding = 24f;

	[SerializeField]
	Vector2 screenOffset = new Vector2( 0f, 36f );

	[SerializeField]
	float maxPreferredWidth = 280f;

	readonly Queue<string> _queue = new Queue<string>( 4 );

	RectTransform _rect;
	Transform _follow;
	float _lineEndsAt;
	float _fadeOutAt = -1f;
	bool _showing;

	public bool IsShowing => _showing || _queue.Count > 0;

	void Awake()
	{
		_rect = transform as RectTransform;
		if ( canvas == null )
			canvas = GetComponent<Canvas>();
		if ( canvasGroup == null && canvas != null )
			canvasGroup = canvas.GetComponent<CanvasGroup>();
		if ( body == null )
			body = GetComponentInChildren<Text>( true );
		if ( bubbleRoot == null )
		{
			Transform found = transform.Find( "BubbleRoot" );
			if ( found != null )
				bubbleRoot = found as RectTransform;
		}
		if ( tail == null )
		{
			Transform found = transform.Find( "Tail" );
			if ( found == null && bubbleRoot != null )
				found = bubbleRoot.Find( "Tail" );
			if ( found != null )
				tail = found as RectTransform;
		}

		ConfigureOverlayCanvas();
		SetVisible( false );
	}

	void Start()
	{
		if ( _follow == null && transform.parent != null )
			_follow = transform.parent;
	}

	void LateUpdate()
	{
		if ( _showing )
			UpdateScreenPosition();

		TickSpeech();
	}

	public void BindFollow( Transform follow )
	{
		if ( follow == null || follow == transform )
			_follow = transform.parent != null ? transform.parent : null;
		else
			_follow = follow;
		if ( _showing )
			UpdateScreenPosition();
	}

	public void EditorBind( Canvas canvasRef, CanvasGroup groupRef, Text bodyRef )
	{
		canvas = canvasRef;
		canvasGroup = groupRef;
		body = bodyRef;
	}

	public void Say( string line )
	{
		if ( string.IsNullOrEmpty( line ) )
			return;

		_queue.Enqueue( line );
		if ( !_showing )
			ShowNext();
	}

	public void Say( params string[] lines )
	{
		if ( lines == null || lines.Length == 0 )
			return;

		for ( int i = 0; i < lines.Length; i++ )
		{
			if ( !string.IsNullOrEmpty( lines[ i ] ) )
				_queue.Enqueue( lines[ i ] );
		}

		if ( !_showing )
			ShowNext();
	}

	public void Clear()
	{
		_queue.Clear();
		_showing = false;
		_fadeOutAt = -1f;
		SetVisible( false );
	}

	void ConfigureOverlayCanvas()
	{
		if ( canvas != null )
		{
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.overrideSorting = true;
			if ( canvas.sortingOrder < 50 )
				canvas.sortingOrder = 50;
		}

		if ( _rect != null )
		{
			_rect.localScale = Vector3.one;
			_rect.localRotation = Quaternion.identity;
			_rect.pivot = new Vector2( 0.5f, 0f );
		}
	}

	void UpdateScreenPosition()
	{
		if ( _rect == null )
			_rect = transform as RectTransform;
		if ( _rect == null )
			return;

		Transform follow = _follow;
		if ( follow == null || follow == transform )
			follow = transform.parent;
		if ( follow == null )
			return;

		Camera cam = ResolveCamera();
		if ( cam == null )
			return;

		Vector3 world = follow.position + worldOffset;
		Vector3 screen = cam.WorldToScreenPoint( world );
		if ( screen.z <= 0f )
		{
			screen.x = Screen.width - screen.x;
			screen.y = Screen.height - screen.y;
		}

		screen.x += screenOffset.x;
		screen.y += screenOffset.y;

		Vector2 half = EstimateHalfSizePixels();
		float pad = Mathf.Max( 0f, screenPadding );
		// Pivot is bottom-center, so clamp using half-width and full height above the pivot.
		float minX = pad + half.x;
		float maxX = Screen.width - pad - half.x;
		float minY = pad;
		float maxY = Screen.height - pad - half.y * 2f;

		if ( maxX < minX )
			screen.x = Screen.width * 0.5f;
		else
			screen.x = Mathf.Clamp( screen.x, minX, maxX );

		if ( maxY < minY )
			screen.y = Screen.height * 0.5f;
		else
			screen.y = Mathf.Clamp( screen.y, minY, maxY );

		_rect.position = new Vector3( screen.x, screen.y, 0f );
	}

	Vector2 EstimateHalfSizePixels()
	{
		RectTransform sizeSource = bubbleRoot != null ? bubbleRoot : _rect;
		if ( sizeSource == null )
			return new Vector2( 80f, 40f );

		Vector2 size = sizeSource.rect.size;
		if ( size.x < 1f || size.y < 1f )
			size = sizeSource.sizeDelta;

		float tailH = 0f;
		if ( tail != null )
			tailH = Mathf.Abs( tail.rect.height > 1f ? tail.rect.height : tail.sizeDelta.y );

		float scale = 1f;
		if ( canvas != null )
			scale = canvas.scaleFactor;

		return new Vector2(
			Mathf.Max( 24f, size.x * scale * 0.5f ),
			Mathf.Max( 16f, ( size.y + tailH ) * scale * 0.5f ) );
	}

	static Camera ResolveCamera()
	{
		if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
		{
			Camera cam = GameMode.Instance.cameraController.GetComponentInChildren<Camera>();
			if ( cam != null )
				return cam;
		}

		return Camera.main;
	}

	void TickSpeech()
	{
		if ( !_showing )
			return;

		if ( _fadeOutAt > 0f )
		{
			float t = fadeSeconds <= 0.001f
				? 1f
				: Mathf.Clamp01( 1f - ( _fadeOutAt - Time.unscaledTime ) / fadeSeconds );
			if ( canvasGroup != null )
				canvasGroup.alpha = 1f - t;
			if ( Time.unscaledTime >= _fadeOutAt )
			{
				_fadeOutAt = -1f;
				ShowNext();
			}
			return;
		}

		if ( Time.unscaledTime < _lineEndsAt )
			return;

		BeginFadeOut();
	}

	void BeginFadeOut()
	{
		if ( fadeSeconds <= 0.001f )
		{
			ShowNext();
			return;
		}

		_fadeOutAt = Time.unscaledTime + fadeSeconds;
	}

	void ShowNext()
	{
		_fadeOutAt = -1f;
		if ( _queue.Count == 0 )
		{
			_showing = false;
			SetVisible( false );
			return;
		}

		string line = _queue.Dequeue();
		ApplyLine( line );
		_lineEndsAt = Time.unscaledTime + EstimateHoldSeconds( line );
		_showing = true;
		SetVisible( true );
		if ( canvasGroup != null )
			canvasGroup.alpha = 1f;
		UpdateScreenPosition();
	}

	void ApplyLine( string line )
	{
		if ( body == null )
			return;

		body.text = line;
		body.horizontalOverflow = HorizontalWrapMode.Wrap;
		body.verticalOverflow = VerticalWrapMode.Overflow;

		RectTransform bodyRect = body.rectTransform;
		ContentSizeFitter bodyFitter = body.GetComponent<ContentSizeFitter>();
		LayoutElement bodyLayout = body.GetComponent<LayoutElement>();

		if ( bodyFitter != null )
		{
			bodyFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		}
		if ( bodyLayout != null )
		{
			bodyLayout.preferredWidth = -1f;
			bodyLayout.minWidth = -1f;
		}

		Canvas.ForceUpdateCanvases();
		LayoutRebuilder.ForceRebuildLayoutImmediate( bodyRect );

		float preferred = LayoutUtility.GetPreferredWidth( bodyRect );
		float width = maxPreferredWidth > 1f ? Mathf.Min( preferred, maxPreferredWidth ) : preferred;
		width = Mathf.Max( 48f, width );

		if ( bodyLayout != null )
			bodyLayout.preferredWidth = width;
		if ( bodyFitter != null )
		{
			bodyFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		}
		bodyRect.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, width );

		Canvas.ForceUpdateCanvases();
		if ( bubbleRoot != null )
			LayoutRebuilder.ForceRebuildLayoutImmediate( bubbleRoot );
		if ( _rect != null )
			LayoutRebuilder.ForceRebuildLayoutImmediate( _rect );

		SyncRootToBubble();
	}

	void SyncRootToBubble()
	{
		if ( _rect == null || bubbleRoot == null )
			return;

		Vector2 size = bubbleRoot.rect.size;
		if ( size.x < 1f || size.y < 1f )
			size = bubbleRoot.sizeDelta;

		float tailH = 0f;
		if ( tail != null )
			tailH = Mathf.Abs( tail.sizeDelta.y );

		_rect.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, size.x );
		_rect.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, size.y + tailH );
	}

	float EstimateHoldSeconds( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return defaultHoldSeconds;
		return Mathf.Max( defaultHoldSeconds, Mathf.Clamp( text.Length / 12f, 2.5f, 8f ) );
	}

	void SetVisible( bool visible )
	{
		if ( canvasGroup != null )
		{
			canvasGroup.alpha = visible ? 1f : 0f;
			canvasGroup.blocksRaycasts = false;
			canvasGroup.interactable = false;
			return;
		}

		if ( canvas != null )
			canvas.enabled = visible;
	}
}
