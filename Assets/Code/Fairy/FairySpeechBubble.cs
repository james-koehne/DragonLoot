using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-space speech bubble that tracks a fairy like a world label: fixed UI text size,
/// soft follow above the fairy, whole-panel clamp on-screen, and a short aiming tail.
/// The Overlay canvas stays fullscreen; a child FollowRoot is what moves.
/// </summary>
[DisallowMultipleComponent]
public class FairySpeechBubble : MonoBehaviour
{
	const float MinTailLength = 14f;
	const float DefaultTailWidth = 22f;
	const float TipVisibilityFadeSpeed = 8f;
	const string FollowRootName = "FollowRoot";

	[SerializeField]
	Canvas canvas;

	[SerializeField]
	CanvasGroup canvasGroup;

	[SerializeField]
	Text body;

	[SerializeField]
	Text continuePrompt;

	[SerializeField]
	[Tooltip( "Shown under the speech line while awaiting ContextualInteract (E)." )]
	string continuePromptText = "E to continue";

	[SerializeField]
	[Min( 0.1f )]
	[Tooltip( "Sine speed for continue-prompt glow pulse while awaiting E." )]
	float continuePromptPulseSpeed = 5.5f;

	[SerializeField]
	[Tooltip( "Glow color around the continue prompt (alpha is pulsed)." )]
	Color continuePromptGlowColor = new Color( 1f, 0.95f, 0.55f, 1f );

	[SerializeField]
	[Min( 0f )]
	[Tooltip( "Fixed outline glow radius around the continue prompt." )]
	float continuePromptGlowDistance = 3.5f;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Glow alpha at the dimmest part of the pulse." )]
	float continuePromptGlowMinAlpha = 0.25f;

	[SerializeField]
	[Range( 0f, 1f )]
	[Tooltip( "Glow alpha at the brightest part of the pulse." )]
	float continuePromptGlowMaxAlpha = 1f;

	[SerializeField]
	RectTransform bubbleRoot;

	[SerializeField]
	RectTransform tail;

	[SerializeField]
	RectTransform followRoot;

	[SerializeField]
	[Min( 0.5f )]
	float defaultHoldSeconds = 3.5f;

	[SerializeField]
	[Min( 0f )]
	float fadeSeconds = 0.2f;

	[SerializeField]
	Vector3 worldOffset = new Vector3( 0f, 0.7f, 0f );

	[SerializeField]
	[Min( 0f )]
	float screenPadding = 28f;

	[SerializeField]
	[Min( 0f )]
	float tipClearance = 12f;

	[SerializeField]
	[Min( 0.01f )]
	[Tooltip( "SmoothDamp time for floating toward the preferred screen position." )]
	float screenFollowSmoothTime = 0.1f;

	[SerializeField]
	[Min( 16f )]
	[Tooltip( "Max tail length in reference pixels (before canvas scale)." )]
	float maxTailLength = 100f;

	[SerializeField]
	[Tooltip( "Fade the bubble when the fairy is behind the camera." )]
	bool hideWhenBehind = true;

	[SerializeField]
	float maxPreferredWidth = 280f;

	readonly Queue<string> _queue = new Queue<string>( 4 );

	RectTransform _canvasRect;
	Transform _follow;
	float _lineEndsAt;
	float _fadeOutAt = -1f;
	bool _showing;
	bool _awaitingContinue;
	float _tailBaseWidth = DefaultTailWidth;
	Vector2 _smoothedScreen;
	Vector2 _smoothVelocity;
	bool _hasSmoothedScreen;
	float _tipVisibility = 1f;
	bool _tipValid;
	Vector2 _lastTipScreen;
	bool _detached;
	Color _continuePromptBaseColor = new Color( 1f, 1f, 1f, 1f );
	bool _hasContinuePromptBaseColor;
	Outline _continuePromptGlow;
	Outline _continuePromptGlowSoft;

	public bool IsShowing => _showing || _queue.Count > 0;
	public bool IsAwaitingContinue => _awaitingContinue;

	void Awake()
	{
		if ( canvas == null )
			canvas = GetComponent<Canvas>();
		_canvasRect = transform as RectTransform;
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

		EnsureFollowRoot();
		EnsureContinuePrompt();
		ConfigureOverlayCanvas();
		SetAwaitingContinue( false );
		SetVisible( false );
	}

	void Start()
	{
		if ( _follow == null && transform.parent != null )
			_follow = transform.parent;
		EnsureDetachedOverlay();
		EnsureFollowRoot();
		ConfigureOverlayCanvas();
	}

	void LateUpdate()
	{
		if ( _showing )
			UpdateScreenPosition();

		TickContinuePromptPulse();
		TickSpeech();
	}

	public void BindFollow( Transform follow )
	{
		if ( follow == null || follow == transform )
			_follow = transform.parent != null ? transform.parent : null;
		else
			_follow = follow;
		EnsureDetachedOverlay();
		EnsureFollowRoot();
		ConfigureOverlayCanvas();
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
		_hasSmoothedScreen = false;
		_smoothVelocity = Vector2.zero;
		_tipVisibility = 1f;
		SetAwaitingContinue( false );
		SetVisible( false );
	}

	/// <summary>
	/// Shows or hides the continue hint and pauses auto-fade while awaiting player input.
	/// </summary>
	public void SetAwaitingContinue( bool awaiting )
	{
		_awaitingContinue = awaiting;
		EnsureContinuePrompt();
		if ( continuePrompt != null )
		{
			if ( !string.IsNullOrEmpty( continuePromptText ) )
				continuePrompt.text = continuePromptText;
			EnsureContinuePromptGlow();
			if ( awaiting )
				CacheContinuePromptBaseColor();
			else
				RestoreContinuePromptBaseColor();
			continuePrompt.gameObject.SetActive( awaiting );
			SetContinuePromptGlowEnabled( awaiting );
		}

		if ( awaiting && _showing )
			_fadeOutAt = -1f;

		if ( awaiting && bubbleRoot != null )
			LayoutRebuilder.ForceRebuildLayoutImmediate( bubbleRoot );
	}

	void CacheContinuePromptBaseColor()
	{
		if ( continuePrompt == null )
			return;

		_continuePromptBaseColor = continuePrompt.color;
		_continuePromptBaseColor.a = 1f;
		_hasContinuePromptBaseColor = true;
	}

	void RestoreContinuePromptBaseColor()
	{
		if ( continuePrompt == null || !_hasContinuePromptBaseColor )
			return;

		continuePrompt.color = _continuePromptBaseColor;
		ApplyContinuePromptGlow( 0f );
	}

	void TickContinuePromptPulse()
	{
		if ( !_awaitingContinue || continuePrompt == null || !continuePrompt.gameObject.activeInHierarchy )
			return;

		if ( !_hasContinuePromptBaseColor )
			CacheContinuePromptBaseColor();
		EnsureContinuePromptGlow();

		float pulse = 0.5f + 0.5f * Mathf.Sin( Time.unscaledTime * continuePromptPulseSpeed );
		ApplyContinuePromptGlow( pulse );

		Color textColor = _continuePromptBaseColor;
		textColor.a = 1f;
		continuePrompt.color = textColor;
	}

	void ApplyContinuePromptGlow( float pulse )
	{
		float minA = Mathf.Min( continuePromptGlowMinAlpha, continuePromptGlowMaxAlpha );
		float maxA = Mathf.Max( continuePromptGlowMinAlpha, continuePromptGlowMaxAlpha );
		float alpha = Mathf.Lerp( minA, maxA, pulse );
		float distance = Mathf.Max( 0f, continuePromptGlowDistance );

		Color glow = continuePromptGlowColor;
		glow.a = alpha;
		if ( _continuePromptGlow != null )
		{
			_continuePromptGlow.effectColor = glow;
			_continuePromptGlow.effectDistance = new Vector2( distance, distance );
		}

		Color soft = continuePromptGlowColor;
		soft.a = alpha * 0.55f;
		if ( _continuePromptGlowSoft != null )
		{
			_continuePromptGlowSoft.effectColor = soft;
			float softDist = distance * 1.75f;
			_continuePromptGlowSoft.effectDistance = new Vector2( softDist, softDist );
		}
	}

	void SetContinuePromptGlowEnabled( bool enabled )
	{
		if ( _continuePromptGlow != null )
			_continuePromptGlow.enabled = enabled;
		if ( _continuePromptGlowSoft != null )
			_continuePromptGlowSoft.enabled = enabled;
	}

	void EnsureContinuePromptGlow()
	{
		if ( continuePrompt == null )
			return;

		Outline[] outlines = continuePrompt.GetComponents<Outline>();
		if ( outlines != null && outlines.Length > 0 )
			_continuePromptGlow = outlines[ 0 ];
		if ( outlines != null && outlines.Length > 1 )
			_continuePromptGlowSoft = outlines[ 1 ];

		if ( _continuePromptGlow == null )
		{
			_continuePromptGlow = continuePrompt.gameObject.AddComponent<Outline>();
			_continuePromptGlow.useGraphicAlpha = true;
		}

		if ( _continuePromptGlowSoft == null )
		{
			_continuePromptGlowSoft = continuePrompt.gameObject.AddComponent<Outline>();
			_continuePromptGlowSoft.useGraphicAlpha = true;
		}

		ApplyContinuePromptGlow( 1f );
	}

	void EnsureContinuePrompt()
	{
		if ( continuePrompt != null )
		{
			EnsureContinuePromptGlow();
			return;
		}

		if ( bubbleRoot == null )
			return;

		Transform existing = bubbleRoot.Find( "ContinuePrompt" );
		if ( existing != null )
		{
			continuePrompt = existing.GetComponent<Text>();
			if ( continuePrompt != null )
			{
				EnsureContinuePromptGlow();
				continuePrompt.gameObject.SetActive( false );
				return;
			}
		}

		GameObject go = new GameObject( "ContinuePrompt", typeof( RectTransform ) );
		go.layer = bubbleRoot.gameObject.layer;
		RectTransform rect = go.GetComponent<RectTransform>();
		rect.SetParent( bubbleRoot, false );
		rect.localScale = Vector3.one;
		rect.localRotation = Quaternion.identity;

		continuePrompt = go.AddComponent<Text>();
		continuePrompt.raycastTarget = false;
		continuePrompt.alignment = TextAnchor.MiddleCenter;
		continuePrompt.horizontalOverflow = HorizontalWrapMode.Overflow;
		continuePrompt.verticalOverflow = VerticalWrapMode.Overflow;
		continuePrompt.fontSize = body != null ? Mathf.Max( 18, body.fontSize - 4 ) : 20;
		continuePrompt.color = new Color( 1f, 1f, 1f, 1f );
		continuePrompt.fontStyle = FontStyle.Bold;
		continuePrompt.text = string.IsNullOrEmpty( continuePromptText ) ? "E to continue" : continuePromptText;
		if ( body != null && body.font != null )
			continuePrompt.font = body.font;

		ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		LayoutElement layout = go.AddComponent<LayoutElement>();
		layout.minHeight = 22f;

		VerticalLayoutGroup group = bubbleRoot.GetComponent<VerticalLayoutGroup>();
		if ( group != null && group.spacing < 4f )
			group.spacing = 6f;

		EnsureContinuePromptGlow();
		go.SetActive( false );
	}

	/// <summary>
	/// Overlay + CanvasScaler must stay fullscreen. Nesting under the fairy breaks positioning,
	/// so detach to a root Overlay and only move <see cref="followRoot"/>.
	/// </summary>
	void EnsureDetachedOverlay()
	{
		if ( _detached )
			return;
		if ( transform.parent != null )
			transform.SetParent( null, false );
		_detached = true;
		ConfigureOverlayCanvas();
		Canvas.ForceUpdateCanvases();
	}

	void EnsureFollowRoot()
	{
		if ( followRoot != null )
		{
			SetupFollowRoot( followRoot );
			return;
		}

		Transform existing = transform.Find( FollowRootName );
		if ( existing != null )
		{
			followRoot = existing as RectTransform;
			SetupFollowRoot( followRoot );
			return;
		}

		GameObject go = new GameObject( FollowRootName, typeof( RectTransform ) );
		go.layer = gameObject.layer;
		followRoot = go.GetComponent<RectTransform>();
		followRoot.SetParent( transform, false );
		SetupFollowRoot( followRoot );

		if ( bubbleRoot != null && bubbleRoot.parent != followRoot )
			bubbleRoot.SetParent( followRoot, false );
		if ( tail != null && tail.parent != followRoot )
			tail.SetParent( followRoot, false );
	}

	void SetupFollowRoot( RectTransform root )
	{
		root.anchorMin = new Vector2( 0.5f, 0.5f );
		root.anchorMax = new Vector2( 0.5f, 0.5f );
		root.pivot = new Vector2( 0.5f, 0.5f );
		root.localScale = Vector3.one;
		root.localRotation = Quaternion.identity;
		root.sizeDelta = Vector2.zero;

		if ( bubbleRoot != null )
		{
			if ( bubbleRoot.parent != root )
				bubbleRoot.SetParent( root, false );
			bubbleRoot.anchorMin = new Vector2( 0.5f, 0.5f );
			bubbleRoot.anchorMax = new Vector2( 0.5f, 0.5f );
			bubbleRoot.pivot = new Vector2( 0.5f, 0.5f );
			bubbleRoot.anchoredPosition = Vector2.zero;
		}

		if ( tail != null )
		{
			if ( tail.parent != root )
				tail.SetParent( root, false );
			_tailBaseWidth = Mathf.Max( 8f, Mathf.Abs( tail.sizeDelta.x ) );
			tail.pivot = new Vector2( 0.5f, 1f );
			tail.anchorMin = new Vector2( 0.5f, 0.5f );
			tail.anchorMax = new Vector2( 0.5f, 0.5f );
		}
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

		// Fullscreen root — CanvasScaler owns this; do not move it.
		if ( _canvasRect != null )
		{
			_canvasRect.anchorMin = Vector2.zero;
			_canvasRect.anchorMax = Vector2.one;
			_canvasRect.pivot = new Vector2( 0.5f, 0.5f );
			_canvasRect.offsetMin = Vector2.zero;
			_canvasRect.offsetMax = Vector2.zero;
			_canvasRect.localScale = Vector3.one;
			_canvasRect.localRotation = Quaternion.identity;
		}
	}

	void UpdateScreenPosition()
	{
		if ( followRoot == null )
			EnsureFollowRoot();
		if ( followRoot == null )
			return;

		Transform follow = _follow;
		if ( follow == null || follow == transform )
			return;

		Camera cam = ResolveCamera();
		if ( cam == null )
			return;

		Vector2 tipScreen;
		_tipValid = TryResolveTipScreen( follow, cam, out tipScreen );
		if ( _tipValid )
			_lastTipScreen = tipScreen;
		else
			tipScreen = _lastTipScreen;

		float targetVis = ( !hideWhenBehind || _tipValid ) ? 1f : 0f;
		_tipVisibility = Mathf.MoveTowards( _tipVisibility, targetVis, TipVisibilityFadeSpeed * Time.unscaledDeltaTime );

		if ( !_tipValid && _tipVisibility <= 0.01f )
		{
			ApplyDisplayAlpha( 0f );
			if ( tail != null )
				tail.gameObject.SetActive( false );
			return;
		}

		if ( tail != null && !tail.gameObject.activeSelf )
			tail.gameObject.SetActive( true );

		Vector2 panelSize = GetPanelPixelSize();
		float scale = canvas != null ? Mathf.Max( 0.0001f, canvas.scaleFactor ) : 1f;
		float clearance = tipClearance * scale;

		Vector2 preferred = new Vector2( tipScreen.x, tipScreen.y + clearance + panelSize.y * 0.5f );
		Vector2 logicalScreen = ClampPanelCenter( preferred, panelSize );

		if ( !_hasSmoothedScreen )
		{
			_smoothedScreen = logicalScreen;
			_smoothVelocity = Vector2.zero;
			_hasSmoothedScreen = true;
		}
		else
		{
			float smooth = Mathf.Max( 0.01f, screenFollowSmoothTime );
			_smoothedScreen = Vector2.SmoothDamp(
				_smoothedScreen,
				logicalScreen,
				ref _smoothVelocity,
				smooth,
				Mathf.Infinity,
				Time.unscaledDeltaTime );
		}

		SetFollowRootScreenPosition( _smoothedScreen );
		UpdateTailAim( tipScreen, _smoothedScreen, panelSize, scale );

		if ( _fadeOutAt <= 0f )
			ApplyDisplayAlpha( _tipVisibility );
	}

	void SetFollowRootScreenPosition( Vector2 screenPoint )
	{
		if ( followRoot == null )
			return;

		// Overlay RectTransforms use screen pixels for world position. Prefer this over
		// ScreenPointToLocalPointInRectangle, which collapses to center when the root
		// canvas rect is still 0-sized (common while nested under a 3D fairy).
		followRoot.position = new Vector3( screenPoint.x, screenPoint.y, 0f );
	}

	bool TryResolveTipScreen( Transform follow, Camera cam, out Vector2 tipScreen )
	{
		tipScreen = Vector2.zero;
		Vector3 world = follow.position + worldOffset;
		Vector3 screen = cam.WorldToScreenPoint( world );

		if ( screen.z <= 0.05f )
			return false;

		tipScreen = new Vector2( screen.x, screen.y );

		float margin = 80f;
		if ( tipScreen.x < -margin || tipScreen.x > Screen.width + margin
			|| tipScreen.y < -margin || tipScreen.y > Screen.height + margin )
			return false;

		return true;
	}

	Vector2 ClampPanelCenter( Vector2 center, Vector2 panelSize )
	{
		float pad = Mathf.Max( 0f, screenPadding );
		float halfW = Mathf.Min( panelSize.x * 0.5f, Screen.width * 0.35f );
		float halfH = Mathf.Min( panelSize.y * 0.5f, Screen.height * 0.35f );
		float minX = pad + halfW;
		float maxX = Screen.width - pad - halfW;
		float minY = pad + halfH;
		float maxY = Screen.height - pad - halfH;

		if ( maxX < minX )
			center.x = Mathf.Clamp( center.x, pad, Screen.width - pad );
		else
			center.x = Mathf.Clamp( center.x, minX, maxX );

		if ( maxY < minY )
			center.y = Mathf.Clamp( center.y, pad, Screen.height - pad );
		else
			center.y = Mathf.Clamp( center.y, minY, maxY );

		return center;
	}

	void UpdateTailAim( Vector2 tipScreen, Vector2 panelCenterScreen, Vector2 panelSize, float scale )
	{
		if ( tail == null || followRoot == null || _canvasRect == null )
			return;

		Vector2 toTip = tipScreen - panelCenterScreen;
		float distance = toTip.magnitude;
		Vector2 dir = distance < 0.001f ? Vector2.down : toTip / distance;

		Vector2 half = panelSize * 0.5f;
		float tx = Mathf.Abs( dir.x ) > 0.0001f ? half.x / Mathf.Abs( dir.x ) : float.MaxValue;
		float ty = Mathf.Abs( dir.y ) > 0.0001f ? half.y / Mathf.Abs( dir.y ) : float.MaxValue;
		float edgeT = Mathf.Min( tx, ty );
		if ( float.IsInfinity( edgeT ) || edgeT > 10000f )
			edgeT = half.y;
		Vector2 baseScreen = panelCenterScreen + dir * edgeT;

		Vector2 towardTip = tipScreen - baseScreen;
		float tipDist = towardTip.magnitude;
		if ( tipDist > 0.01f )
			dir = towardTip / tipDist;
		else
			dir = Vector2.down;

		float maxLenPx = Mathf.Max( MinTailLength, maxTailLength ) * scale;
		float lengthPx = Mathf.Clamp( tipDist, MinTailLength * scale, maxLenPx );

		Vector2 baseLocal;
		Vector2 centerLocal;
		RectTransformUtility.ScreenPointToLocalPointInRectangle( followRoot, baseScreen, null, out baseLocal );
		RectTransformUtility.ScreenPointToLocalPointInRectangle( followRoot, panelCenterScreen, null, out centerLocal );

		float angle = Mathf.Atan2( dir.x, -dir.y ) * Mathf.Rad2Deg;
		tail.anchoredPosition = baseLocal;
		tail.localRotation = Quaternion.Euler( 0f, 0f, angle );
		tail.sizeDelta = new Vector2( _tailBaseWidth, lengthPx / Mathf.Max( 0.0001f, scale ) );
	}

	Vector2 GetPanelPixelSize()
	{
		RectTransform sizeSource = bubbleRoot != null ? bubbleRoot : followRoot;
		if ( sizeSource == null )
			return new Vector2( 160f, 64f );

		Vector2 size = sizeSource.rect.size;
		if ( size.x < 1f || size.y < 1f )
			size = sizeSource.sizeDelta;
		if ( size.x < 1f || size.y < 1f )
			size = new Vector2( 160f, 64f );

		float scale = canvas != null ? Mathf.Max( 0.0001f, canvas.scaleFactor ) : 1f;
		return new Vector2(
			Mathf.Clamp( size.x * scale, 48f, Screen.width * 0.7f ),
			Mathf.Clamp( size.y * scale, 32f, Screen.height * 0.5f ) );
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

		if ( _awaitingContinue )
		{
			_fadeOutAt = -1f;
			_lineEndsAt = Time.unscaledTime + 1f;
			return;
		}

		if ( _fadeOutAt > 0f )
		{
			float t = fadeSeconds <= 0.001f
				? 1f
				: Mathf.Clamp01( 1f - ( _fadeOutAt - Time.unscaledTime ) / fadeSeconds );
			ApplyDisplayAlpha( ( 1f - t ) * _tipVisibility );
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
			if ( !_awaitingContinue )
				SetVisible( false );
			return;
		}

		string line = _queue.Dequeue();
		ApplyLine( line );
		_lineEndsAt = Time.unscaledTime + EstimateHoldSeconds( line );
		_showing = true;
		_hasSmoothedScreen = false;
		_smoothVelocity = Vector2.zero;
		_tipVisibility = 1f;
		SetVisible( true );
		if ( _awaitingContinue && continuePrompt != null )
			continuePrompt.gameObject.SetActive( true );
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
		if ( followRoot != null && bubbleRoot != null )
		{
			Vector2 size = bubbleRoot.rect.size;
			if ( size.x < 1f || size.y < 1f )
				size = bubbleRoot.sizeDelta;
			followRoot.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, size.x );
			followRoot.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, size.y );
		}
	}

	float EstimateHoldSeconds( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return defaultHoldSeconds;
		return Mathf.Max( defaultHoldSeconds, Mathf.Clamp( text.Length / 12f, 2.5f, 8f ) );
	}

	void ApplyDisplayAlpha( float alpha )
	{
		if ( canvasGroup == null )
			return;
		canvasGroup.alpha = Mathf.Clamp01( alpha );
		canvasGroup.blocksRaycasts = false;
		canvasGroup.interactable = false;
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
