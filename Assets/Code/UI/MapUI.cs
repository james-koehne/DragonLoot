using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Full-screen map overlay. M toggles; Escape closes. Pauses like the escape menu.
/// Open anim uses unscaled time (zoom + fade) so it works at timescale 0.
/// </summary>
public sealed class MapUI : MonoBehaviour
{
	static MapUI s_instance;

	[SerializeField] CanvasGroup group;
	[SerializeField] RectTransform panel;
	[SerializeField] RawImage mapImage;
	[SerializeField] RectTransform playerDot;
	[SerializeField] RectTransform aimCone;
	[SerializeField] Transform labelsRoot;

	MapDefinition _definition;
	bool _ready;
	bool _open;
	float _timeScaleBeforePause = 1f;
	float _animElapsed;
	float _animDuration = 0.22f;
	float _animFromScale = 0.92f;
	bool _animatingOpen;
	bool _animatingClose;

	readonly List<MapLabelMarker> _labelSources = new List<MapLabelMarker>( 32 );
	readonly List<MapLabelWidget> _labelWidgets = new List<MapLabelWidget>( 32 );
	readonly List<TutorialMapMarker> _tutorialMarkerSources = new List<TutorialMapMarker>( 8 );
	readonly List<TempMarkerWidget> _tutorialMarkerWidgets = new List<TempMarkerWidget>( 8 );
	Font _font;
	Texture2D _coneTexture;
	Texture2D _dotTexture;
	Sprite _dotSprite;

	struct MapLabelWidget
	{
		public RectTransform Root;
		public Image Glow;
		public Image Icon;
		public Text Text;
	}

	struct TempMarkerWidget
	{
		public RectTransform Root;
		public Image Glow;
		public Image Pin;
		public Text Text;
	}

	MapDefinition Definition => RuntimeDefinition.Resolve( ref _definition );

	public static bool IsOpen => s_instance != null && s_instance._open;

	public static void DebugSetOpen( bool open )
	{
		if ( s_instance == null )
			return;

		if ( open )
		{
			if ( !s_instance._open && !s_instance._animatingOpen )
				s_instance.BeginOpen();
		}
		else if ( s_instance._open || s_instance._animatingOpen )
		{
			s_instance.BeginClose();
		}
	}

	public void Setup()
	{
		EnsureHierarchy();
		SetOpenImmediate( false, applyGameplay: false );
		_ready = true;
	}

	void Awake()
	{
		s_instance = this;
	}

	void OnDestroy()
	{
		if ( s_instance == this )
			s_instance = null;

		if ( _coneTexture != null )
		{
			Destroy( _coneTexture );
			_coneTexture = null;
		}

		if ( _dotTexture != null )
		{
			Destroy( _dotTexture );
			_dotTexture = null;
		}

		_dotSprite = null;

		if ( _open )
			SetOpenImmediate( false, applyGameplay: true );
	}

	void Update()
	{
		if ( !_ready )
			return;

		HandleInput();
		TickAnimation();

		if ( _open || _animatingClose )
			RefreshOverlays();
	}

	void HandleInput()
	{
		Keyboard keyboard = Keyboard.current;
		if ( keyboard == null )
			return;

		if ( DebugOverlay.IsOpen )
		{
			// Still allow closing the map while the debug panel is open.
			if ( ( _open || _animatingOpen )
				&& ( keyboard.mKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame ) )
			{
				BeginClose();
			}
			return;
		}

		if ( keyboard.mKey.wasPressedThisFrame )
		{
			if ( PauseMenuUI.IsOpen )
				return;

			if ( _open || _animatingOpen )
				BeginClose();
			else if ( !_animatingClose )
				BeginOpen();
			return;
		}

		if ( keyboard.escapeKey.wasPressedThisFrame && ( _open || _animatingOpen ) )
			BeginClose();
	}

	void BeginOpen()
	{
		MapSystem map = MapSystem.EnsureExists();
		map.RequestImmediateBake();

		MapDefinition def = Definition;
		if ( def != null )
		{
			def.EnsureDefaults();
			_animDuration = def.openDuration;
			_animFromScale = def.openStartScale;
		}

		_open = true;
		_animatingOpen = true;
		_animatingClose = false;
		_animElapsed = 0f;

		if ( group != null )
		{
			group.alpha = 0f;
			group.interactable = true;
			group.blocksRaycasts = true;
		}

		if ( panel != null )
			panel.localScale = Vector3.one * _animFromScale;

		ApplyPause( true );
		BindMapTexture();
		RefreshOverlays();
		EventBus.Publish( new MapOpenedEvent() );
	}

	void BeginClose()
	{
		if ( !_open && !_animatingOpen )
			return;

		_animatingOpen = false;
		_animatingClose = true;
		_animElapsed = 0f;
		MapDefinition def = Definition;
		if ( def != null )
			_animDuration = def.openDuration;
	}

	void SetOpenImmediate( bool open, bool applyGameplay )
	{
		_open = open;
		_animatingOpen = false;
		_animatingClose = false;
		_animElapsed = 0f;

		if ( group != null )
		{
			group.alpha = open ? 1f : 0f;
			group.interactable = open;
			group.blocksRaycasts = open;
		}

		if ( panel != null )
			panel.localScale = Vector3.one;

		if ( applyGameplay )
			ApplyPause( open );
	}

	void TickAnimation()
	{
		if ( !_animatingOpen && !_animatingClose )
			return;

		float duration = Mathf.Max( 0.05f, _animDuration );
		_animElapsed += Time.unscaledDeltaTime;
		float t = Mathf.Clamp01( _animElapsed / duration );
		float eased = 1f - ( 1f - t ) * ( 1f - t );

		if ( _animatingOpen )
		{
			if ( group != null )
				group.alpha = eased;
			if ( panel != null )
				panel.localScale = Vector3.one * Mathf.Lerp( _animFromScale, 1f, eased );

			if ( t >= 1f )
			{
				_animatingOpen = false;
				if ( group != null )
					group.alpha = 1f;
				if ( panel != null )
					panel.localScale = Vector3.one;
			}
			return;
		}

		float closeAlpha = 1f - eased;
		if ( group != null )
			group.alpha = closeAlpha;
		if ( panel != null )
			panel.localScale = Vector3.one * Mathf.Lerp( 1f, _animFromScale, eased );

		if ( t >= 1f )
		{
			_animatingClose = false;
			_open = false;
			if ( group != null )
			{
				group.alpha = 0f;
				group.interactable = false;
				group.blocksRaycasts = false;
			}
			if ( panel != null )
				panel.localScale = Vector3.one;
			ApplyPause( false );
		}
	}

	void ApplyPause( bool paused )
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;

		if ( paused )
		{
			_timeScaleBeforePause = Time.timeScale;
			if ( _timeScaleBeforePause <= 0.001f )
				_timeScaleBeforePause = 1f;
			Time.timeScale = 0f;

			if ( player != null )
				player.SetGameplayInputEnabled( false );
			else
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible = true;
			}
		}
		else
		{
			Time.timeScale = _timeScaleBeforePause;

			if ( PauseMenuUI.IsOpen || DebugOverlay.IsOpen )
				return;

			if ( player != null )
				player.SetGameplayInputEnabled( true );
			else
			{
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
		}
	}

	void BindMapTexture()
	{
		MapSystem map = MapSystem.Instance;
		if ( map == null || mapImage == null )
			return;

		mapImage.texture = map.DisplayTexture;
		mapImage.color = Color.white;
	}

	void RefreshOverlays()
	{
		MapSystem map = MapSystem.Instance;
		MapDefinition def = Definition;
		if ( map == null || !map.HasBounds || panel == null )
			return;

		if ( def != null )
			def.EnsureDefaults();

		BindMapTexture();
		RefreshPlayerMarker( map, def );
		RefreshLabels( map, def );
		RefreshTutorialMarkers( map, def );
	}

	void RefreshPlayerMarker( MapSystem map, MapDefinition def )
	{
		PlayerController player = GameMode.Instance != null ? GameMode.Instance.Player : null;
		if ( player == null || playerDot == null || aimCone == null )
			return;

		if ( !map.TryWorldToUv( player.transform.position, out Vector2 uv ) )
		{
			playerDot.gameObject.SetActive( false );
			aimCone.gameObject.SetActive( false );
			return;
		}

		playerDot.gameObject.SetActive( true );
		aimCone.gameObject.SetActive( true );

		Vector2 panelPos = UvToPanelLocal( uv );
		playerDot.anchoredPosition = panelPos;
		aimCone.anchoredPosition = panelPos;

		float yaw = Mathf.Atan2( player.transform.forward.x, player.transform.forward.z ) * Mathf.Rad2Deg;
		// UI +Y is up (world +Z). Rotate so cone points along planar facing.
		aimCone.localEulerAngles = new Vector3( 0f, 0f, -yaw );

		float dotSize = def != null ? def.playerDotSize : 14f;
		playerDot.sizeDelta = new Vector2( dotSize, dotSize );
		Image dotImage = playerDot.GetComponent<Image>();
		if ( dotImage != null && def != null )
			dotImage.color = def.playerDotColor;

		float coneLen = def != null ? def.aimConeLength : 48f;
		float halfAngle = def != null ? def.aimConeHalfAngle : 28f;
		float coneWidth = 2f * coneLen * Mathf.Tan( halfAngle * Mathf.Deg2Rad );
		if ( def != null )
			coneWidth = Mathf.Max( coneWidth, def.aimConeBaseWidth * 0.5f );
		aimCone.sizeDelta = new Vector2( coneWidth, coneLen );
		aimCone.pivot = new Vector2( 0.5f, 0f );
		Image coneImage = aimCone.GetComponent<Image>();
		if ( coneImage != null && def != null )
			coneImage.color = def.aimConeColor;
	}

	void RefreshLabels( MapSystem map, MapDefinition def )
	{
		if ( labelsRoot == null )
			return;

		map.CollectLabels( _labelSources );
		EnsureLabelWidgetCount( _labelSources.Count, def );

		float iconSize = def != null ? def.labelIconSize : 28f;
		float gap = def != null ? def.labelIconTextGap : 4f;

		for ( int i = 0; i < _labelWidgets.Count; i++ )
		{
			MapLabelWidget widget = _labelWidgets[ i ];
			if ( widget.Root == null )
				continue;

			if ( i >= _labelSources.Count )
			{
				widget.Root.gameObject.SetActive( false );
				continue;
			}

			MapLabelMarker marker = _labelSources[ i ];
			if ( marker == null || !map.TryWorldToUv( marker.WorldPosition, out Vector2 uv ) )
			{
				widget.Root.gameObject.SetActive( false );
				continue;
			}

			bool highlighted = MapOverlayRegistrar.IsLabelHighlighted( marker.Label );
			if ( !highlighted && !map.IsDiscoveredAtWorld( marker.WorldPosition ) )
			{
				widget.Root.gameObject.SetActive( false );
				continue;
			}

			widget.Root.gameObject.SetActive( true );
			widget.Root.anchoredPosition = UvToPanelLocal( uv );

			float pulse = highlighted
				? 0.5f + 0.5f * Mathf.Sin( Time.unscaledTime * 5.5f )
				: 0f;
			widget.Root.localScale = highlighted
				? Vector3.one * ( 1f + 0.1f * pulse )
				: Vector3.one;

			bool hasIcon = marker.Icon != null && widget.Icon != null;
			if ( widget.Glow != null )
			{
				widget.Glow.gameObject.SetActive( highlighted );
				if ( highlighted )
				{
					if ( widget.Glow.sprite == null )
						widget.Glow.sprite = EnsureDotSprite();
					float glowSize = ( hasIcon ? iconSize : 36f ) * ( 1.6f + 0.45f * pulse );
					widget.Glow.rectTransform.sizeDelta = new Vector2( glowSize, glowSize );
					float glowY = hasIcon ? gap * 0.5f + iconSize * 0.5f : 0f;
					widget.Glow.rectTransform.anchoredPosition = new Vector2( 0f, glowY );
					widget.Glow.color = new Color( 1f, 0.85f, 0.25f, 0.25f + 0.45f * pulse );
				}
			}

			if ( widget.Icon != null )
			{
				widget.Icon.gameObject.SetActive( hasIcon );
				if ( hasIcon )
				{
					widget.Icon.sprite = marker.Icon;
					widget.Icon.rectTransform.sizeDelta = new Vector2( iconSize, iconSize );
					widget.Icon.rectTransform.anchoredPosition = new Vector2( 0f, gap * 0.5f + iconSize * 0.5f );
					widget.Icon.color = highlighted
						? Color.Lerp( Color.white, new Color( 1f, 0.92f, 0.4f, 1f ), pulse )
						: Color.white;
				}
			}

			if ( widget.Text != null )
			{
				widget.Text.text = highlighted ? "★ " + marker.Label : marker.Label;
				float textY = hasIcon ? -( gap * 0.5f + widget.Text.preferredHeight * 0.25f ) : 0f;
				widget.Text.rectTransform.anchoredPosition = new Vector2( 0f, textY );
				if ( def != null )
				{
					widget.Text.fontSize = highlighted ? def.labelFontSize + 4 : def.labelFontSize;
					Color baseHi = new Color( 1f, 0.92f, 0.35f, 1f );
					Color brightHi = new Color( 1f, 1f, 0.75f, 1f );
					widget.Text.color = highlighted
						? Color.Lerp( baseHi, brightHi, pulse )
						: def.labelColor;
				}
			}
		}
	}

	void RefreshTutorialMarkers( MapSystem map, MapDefinition def )
	{
		if ( labelsRoot == null )
			return;

		MapOverlayRegistrar.CollectActiveTutorialMarkers( _tutorialMarkerSources );
		EnsureTempMarkerWidgetCount( _tutorialMarkerSources.Count, def );

		const float pinSize = 22f;
		for ( int i = 0; i < _tutorialMarkerWidgets.Count; i++ )
		{
			TempMarkerWidget widget = _tutorialMarkerWidgets[ i ];
			if ( widget.Root == null )
				continue;

			if ( i >= _tutorialMarkerSources.Count )
			{
				widget.Root.gameObject.SetActive( false );
				continue;
			}

			TutorialMapMarker marker = _tutorialMarkerSources[ i ];
			if ( marker == null || !map.TryWorldToUv( marker.WorldPosition, out Vector2 uv ) )
			{
				widget.Root.gameObject.SetActive( false );
				continue;
			}

			widget.Root.gameObject.SetActive( true );
			widget.Root.anchoredPosition = UvToPanelLocal( uv );

			float pulse = 0.5f + 0.5f * Mathf.Sin( Time.unscaledTime * 5.5f );
			widget.Root.localScale = Vector3.one * ( 1f + 0.12f * pulse );

			Color baseHi = new Color( 0.35f, 0.9f, 1f, 1f );
			Color brightHi = new Color( 0.85f, 1f, 1f, 1f );
			Color pinColor = Color.Lerp( baseHi, brightHi, pulse );

			if ( widget.Glow != null )
			{
				if ( widget.Glow.sprite == null )
					widget.Glow.sprite = EnsureDotSprite();
				float glowSize = pinSize * ( 2.1f + 0.55f * pulse );
				widget.Glow.rectTransform.sizeDelta = new Vector2( glowSize, glowSize );
				widget.Glow.color = new Color( 0.3f, 0.85f, 1f, 0.22f + 0.4f * pulse );
			}

			if ( widget.Pin != null )
			{
				if ( widget.Pin.sprite == null )
					widget.Pin.sprite = EnsureDotSprite();
				widget.Pin.rectTransform.sizeDelta = new Vector2( pinSize, pinSize );
				widget.Pin.color = pinColor;
			}

			if ( widget.Text != null )
			{
				bool hasLabel = !string.IsNullOrEmpty( marker.MapLabel );
				widget.Text.gameObject.SetActive( hasLabel );
				if ( hasLabel )
				{
					widget.Text.text = marker.MapLabel;
					widget.Text.fontSize = def != null ? def.labelFontSize : 22;
					widget.Text.color = pinColor;
					widget.Text.rectTransform.anchoredPosition = new Vector2( 0f, -( pinSize * 0.5f + 10f ) );
				}
			}
		}
	}

	Vector2 UvToPanelLocal( Vector2 uv )
	{
		Rect rect = panel.rect;
		return new Vector2( ( uv.x - 0.5f ) * rect.width, ( uv.y - 0.5f ) * rect.height );
	}

	void EnsureLabelWidgetCount( int count, MapDefinition def )
	{
		while ( _labelWidgets.Count < count )
		{
			GameObject go = new GameObject( "MapLabel", typeof( RectTransform ) );
			go.transform.SetParent( labelsRoot, false );
			RectTransform root = go.GetComponent<RectTransform>();
			root.anchorMin = new Vector2( 0.5f, 0.5f );
			root.anchorMax = new Vector2( 0.5f, 0.5f );
			root.pivot = new Vector2( 0.5f, 0.5f );
			root.sizeDelta = new Vector2( 280f, 80f );

			GameObject glowGo = new GameObject( "Glow", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			glowGo.transform.SetParent( go.transform, false );
			RectTransform glowRect = glowGo.GetComponent<RectTransform>();
			glowRect.anchorMin = new Vector2( 0.5f, 0.5f );
			glowRect.anchorMax = new Vector2( 0.5f, 0.5f );
			glowRect.pivot = new Vector2( 0.5f, 0.5f );
			glowRect.sizeDelta = new Vector2( 48f, 48f );
			Image glow = glowGo.GetComponent<Image>();
			glow.sprite = EnsureDotSprite();
			glow.raycastTarget = false;
			glow.color = new Color( 1f, 0.85f, 0.25f, 0.4f );
			glowGo.SetActive( false );

			GameObject iconGo = new GameObject( "Icon", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			iconGo.transform.SetParent( go.transform, false );
			RectTransform iconRect = iconGo.GetComponent<RectTransform>();
			iconRect.anchorMin = new Vector2( 0.5f, 0.5f );
			iconRect.anchorMax = new Vector2( 0.5f, 0.5f );
			iconRect.pivot = new Vector2( 0.5f, 0.5f );
			float iconSize = def != null ? def.labelIconSize : 28f;
			iconRect.sizeDelta = new Vector2( iconSize, iconSize );
			Image icon = iconGo.GetComponent<Image>();
			icon.raycastTarget = false;
			icon.preserveAspect = true;
			iconGo.SetActive( false );

			GameObject textGo = new GameObject( "Text", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
			textGo.transform.SetParent( go.transform, false );
			RectTransform textRect = textGo.GetComponent<RectTransform>();
			textRect.anchorMin = new Vector2( 0.5f, 0.5f );
			textRect.anchorMax = new Vector2( 0.5f, 0.5f );
			textRect.pivot = new Vector2( 0.5f, 0.5f );
			textRect.sizeDelta = new Vector2( 280f, 40f );

			Text text = textGo.GetComponent<Text>();
			text.font = ResolveFont();
			text.fontSize = def != null ? def.labelFontSize : 22;
			text.fontStyle = FontStyle.Bold;
			text.alignment = TextAnchor.MiddleCenter;
			text.color = def != null ? def.labelColor : Color.white;
			text.raycastTarget = false;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;

			_labelWidgets.Add( new MapLabelWidget
			{
				Root = root,
				Glow = glow,
				Icon = icon,
				Text = text
			} );
		}
	}

	void EnsureTempMarkerWidgetCount( int count, MapDefinition def )
	{
		while ( _tutorialMarkerWidgets.Count < count )
		{
			GameObject go = new GameObject( "TutorialMapPin", typeof( RectTransform ) );
			go.transform.SetParent( labelsRoot, false );
			RectTransform root = go.GetComponent<RectTransform>();
			root.anchorMin = new Vector2( 0.5f, 0.5f );
			root.anchorMax = new Vector2( 0.5f, 0.5f );
			root.pivot = new Vector2( 0.5f, 0.5f );
			root.sizeDelta = new Vector2( 160f, 80f );

			GameObject glowGo = new GameObject( "Glow", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			glowGo.transform.SetParent( go.transform, false );
			RectTransform glowRect = glowGo.GetComponent<RectTransform>();
			glowRect.anchorMin = new Vector2( 0.5f, 0.5f );
			glowRect.anchorMax = new Vector2( 0.5f, 0.5f );
			glowRect.pivot = new Vector2( 0.5f, 0.5f );
			glowRect.sizeDelta = new Vector2( 48f, 48f );
			Image glow = glowGo.GetComponent<Image>();
			glow.sprite = EnsureDotSprite();
			glow.raycastTarget = false;

			GameObject pinGo = new GameObject( "Pin", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			pinGo.transform.SetParent( go.transform, false );
			RectTransform pinRect = pinGo.GetComponent<RectTransform>();
			pinRect.anchorMin = new Vector2( 0.5f, 0.5f );
			pinRect.anchorMax = new Vector2( 0.5f, 0.5f );
			pinRect.pivot = new Vector2( 0.5f, 0.5f );
			pinRect.sizeDelta = new Vector2( 22f, 22f );
			Image pin = pinGo.GetComponent<Image>();
			pin.sprite = EnsureDotSprite();
			pin.raycastTarget = false;

			GameObject textGo = new GameObject( "Text", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
			textGo.transform.SetParent( go.transform, false );
			RectTransform textRect = textGo.GetComponent<RectTransform>();
			textRect.anchorMin = new Vector2( 0.5f, 0.5f );
			textRect.anchorMax = new Vector2( 0.5f, 0.5f );
			textRect.pivot = new Vector2( 0.5f, 0.5f );
			textRect.sizeDelta = new Vector2( 160f, 36f );

			Text text = textGo.GetComponent<Text>();
			text.font = ResolveFont();
			text.fontSize = def != null ? def.labelFontSize : 22;
			text.fontStyle = FontStyle.Bold;
			text.alignment = TextAnchor.MiddleCenter;
			text.color = new Color( 0.35f, 0.9f, 1f, 1f );
			text.raycastTarget = false;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;

			_tutorialMarkerWidgets.Add( new TempMarkerWidget
			{
				Root = root,
				Glow = glow,
				Pin = pin,
				Text = text
			} );
		}
	}

	void EnsureHierarchy()
	{
		if ( group == null )
			group = GetComponent<CanvasGroup>();
		if ( group == null )
			group = gameObject.AddComponent<CanvasGroup>();

		RectTransform rootRect = GetComponent<RectTransform>();
		if ( rootRect != null )
		{
			rootRect.anchorMin = Vector2.zero;
			rootRect.anchorMax = Vector2.one;
			rootRect.offsetMin = Vector2.zero;
			rootRect.offsetMax = Vector2.zero;
		}

		if ( panel == null )
		{
			Transform existing = transform.Find( "Panel" );
			GameObject panelGo = existing != null
				? existing.gameObject
				: new GameObject( "Panel", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			if ( existing == null )
				panelGo.transform.SetParent( transform, false );
			panel = panelGo.GetComponent<RectTransform>();
		}

		panel.anchorMin = new Vector2( 0.5f, 0.5f );
		panel.anchorMax = new Vector2( 0.5f, 0.5f );
		panel.pivot = new Vector2( 0.5f, 0.5f );
		panel.sizeDelta = new Vector2( 920f, 920f );
		panel.anchoredPosition = Vector2.zero;

		Image panelBg = panel.GetComponent<Image>();
		if ( panelBg == null )
			panelBg = panel.gameObject.AddComponent<Image>();
		panelBg.color = new Color( 0.06f, 0.07f, 0.09f, 0.98f );
		panelBg.raycastTarget = true;

		if ( mapImage == null )
		{
			Transform existing = panel.Find( "MapImage" );
			GameObject mapGo = existing != null
				? existing.gameObject
				: new GameObject( "MapImage", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( RawImage ) );
			if ( existing == null )
				mapGo.transform.SetParent( panel, false );
			mapImage = mapGo.GetComponent<RawImage>();
		}

		RectTransform mapRect = mapImage.rectTransform;
		mapRect.anchorMin = Vector2.zero;
		mapRect.anchorMax = Vector2.one;
		mapRect.offsetMin = new Vector2( 24f, 24f );
		mapRect.offsetMax = new Vector2( -24f, -24f );
		mapImage.raycastTarget = false;
		mapImage.color = Color.white;

		if ( labelsRoot == null )
		{
			Transform existing = panel.Find( "Labels" );
			GameObject labelsGo = existing != null
				? existing.gameObject
				: new GameObject( "Labels", typeof( RectTransform ) );
			if ( existing == null )
				labelsGo.transform.SetParent( panel, false );
			labelsRoot = labelsGo.transform;
		}

		RectTransform labelsRect = labelsRoot as RectTransform;
		if ( labelsRect != null )
		{
			labelsRect.anchorMin = Vector2.zero;
			labelsRect.anchorMax = Vector2.one;
			labelsRect.offsetMin = new Vector2( 24f, 24f );
			labelsRect.offsetMax = new Vector2( -24f, -24f );
		}

		if ( aimCone == null )
		{
			Transform existing = panel.Find( "AimCone" );
			GameObject coneGo = existing != null
				? existing.gameObject
				: new GameObject( "AimCone", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			if ( existing == null )
				coneGo.transform.SetParent( panel, false );
			aimCone = coneGo.GetComponent<RectTransform>();
		}

		Image coneImage = aimCone.GetComponent<Image>();
		if ( coneImage == null )
			coneImage = aimCone.gameObject.AddComponent<Image>();
		coneImage.sprite = EnsureConeSprite();
		coneImage.type = Image.Type.Simple;
		coneImage.preserveAspect = true;
		coneImage.raycastTarget = false;
		aimCone.anchorMin = new Vector2( 0.5f, 0.5f );
		aimCone.anchorMax = new Vector2( 0.5f, 0.5f );
		aimCone.pivot = new Vector2( 0.5f, 0f );
		aimCone.sizeDelta = new Vector2( 36f, 48f );

		if ( playerDot == null )
		{
			Transform existing = panel.Find( "PlayerDot" );
			GameObject dotGo = existing != null
				? existing.gameObject
				: new GameObject( "PlayerDot", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
			if ( existing == null )
				dotGo.transform.SetParent( panel, false );
			playerDot = dotGo.GetComponent<RectTransform>();
		}

		Image dotImage = playerDot.GetComponent<Image>();
		if ( dotImage == null )
			dotImage = playerDot.gameObject.AddComponent<Image>();
		dotImage.sprite = EnsureDotSprite();
		dotImage.color = Color.white;
		dotImage.raycastTarget = false;
		playerDot.anchorMin = new Vector2( 0.5f, 0.5f );
		playerDot.anchorMax = new Vector2( 0.5f, 0.5f );
		playerDot.pivot = new Vector2( 0.5f, 0.5f );
		playerDot.sizeDelta = new Vector2( 14f, 14f );

		Image dim = GetComponent<Image>();
		if ( dim == null )
			dim = gameObject.AddComponent<Image>();
		dim.color = new Color( 0f, 0f, 0f, 0.72f );
		dim.raycastTarget = true;
	}

	Sprite EnsureConeSprite()
	{
		Image coneImage = aimCone != null ? aimCone.GetComponent<Image>() : null;
		if ( coneImage != null && coneImage.sprite != null )
			return coneImage.sprite;

		const int w = 64;
		const int h = 64;
		if ( _coneTexture == null )
		{
			_coneTexture = new Texture2D( w, h, TextureFormat.RGBA32, false );
			_coneTexture.name = "MapAimCone";
			_coneTexture.filterMode = FilterMode.Bilinear;
			_coneTexture.wrapMode = TextureWrapMode.Clamp;
			Color32[] pixels = new Color32[ w * h ];
			for ( int y = 0; y < h; y++ )
			{
				float v = y / ( float )( h - 1 );
				float halfWidth = Mathf.Lerp( 0.5f, 0.02f, v );
				for ( int x = 0; x < w; x++ )
				{
					float u = ( x + 0.5f ) / w - 0.5f;
					bool inside = Mathf.Abs( u ) <= halfWidth * 0.5f + 0.01f && v > 0.02f;
					pixels[ y * w + x ] = inside
						? new Color32( 255, 255, 255, 255 )
						: new Color32( 255, 255, 255, 0 );
				}
			}

			_coneTexture.SetPixels32( pixels );
			_coneTexture.Apply( false, false );
		}

		return Sprite.Create( _coneTexture, new Rect( 0f, 0f, w, h ), new Vector2( 0.5f, 0f ), 64f );
	}

	Sprite EnsureDotSprite()
	{
		Image dotImage = playerDot != null ? playerDot.GetComponent<Image>() : null;
		if ( dotImage != null && dotImage.sprite != null && _dotSprite == null )
			return dotImage.sprite;
		if ( _dotSprite != null )
			return _dotSprite;

		const int size = 32;
		_dotTexture = new Texture2D( size, size, TextureFormat.RGBA32, false );
		_dotTexture.name = "MapPlayerDot";
		_dotTexture.filterMode = FilterMode.Bilinear;
		_dotTexture.wrapMode = TextureWrapMode.Clamp;
		Color32[] pixels = new Color32[ size * size ];
		float center = ( size - 1 ) * 0.5f;
		float radius = size * 0.45f;
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				float dx = x - center;
				float dy = y - center;
				float dist = Mathf.Sqrt( dx * dx + dy * dy );
				byte a = dist <= radius ? ( byte )255 : ( byte )0;
				pixels[ y * size + x ] = new Color32( 255, 255, 255, a );
			}
		}

		_dotTexture.SetPixels32( pixels );
		_dotTexture.Apply( false, false );
		_dotSprite = Sprite.Create( _dotTexture, new Rect( 0f, 0f, size, size ), new Vector2( 0.5f, 0.5f ), 32f );
		return _dotSprite;
	}

	Font ResolveFont()
	{
		if ( _font != null )
			return _font;

		_font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( _font == null )
			_font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		return _font;
	}
}
