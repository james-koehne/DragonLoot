using System;
using System.Collections.Generic;
using System.Text;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Corner contextual tutorial popup: explanation + multi-task checkboxes.
/// Non-blocking, gameplay-driven; no click required.
/// Wire Feedbacks on the Interface prefab (show / hide / task complete / tutorial complete).
/// </summary>
public class TutorialPopupUI : MonoBehaviour
{
	const float ContentPadX = 20f;
	const float ContentPadTop = 18f;
	const float ContentPadBottom = 18f;
	const float ContentGap = 10f;
	const float MinPopupHeight = 140f;
	const float DefaultPopupWidth = 520f;
	const float MinimizedHeight = 48f;
	const float MinimizedGap = 8f;
	const float StackGap = 10f;
	const float MinimizedIndent = 16f;
	const float CycleControlWidth = 168f;
	const float CycleControlHeight = 48f;
	const float CycleControlGap = 14f;

	[SerializeField] CanvasGroup group;
	[SerializeField] Text titleText;
	[SerializeField] Text bodyText;
	[SerializeField] Text hintText;
	[SerializeField] Text tasksText;
	[SerializeField] Text stepText;
	[SerializeField] Text cycleHintText;
	[SerializeField] Button dismissButton;
	[SerializeField] Feedbacks showFeedback;
	[SerializeField] Feedbacks hideFeedback;
	[SerializeField] Feedbacks taskCompleteFeedback;
	[SerializeField] Feedbacks tutorialCompleteFeedback;
	[SerializeField] Feedbacks switchFeedback;
	[SerializeField] CanvasGroup completeGlowGroup;

	bool _visible;
	bool _ready;
	string _bodyBase = string.Empty;
	string _tasksFormatted = string.Empty;
	Vector3 _restScale = Vector3.one;
	Vector2 _restAnchored;
	bool _restAnchoredCaptured;
	float _popupWidth = DefaultPopupWidth;
	float _activeCardHeight;
	bool _cycleVisible;
	RectTransform _backgroundRect;
	RectTransform _glowRect;
	RectTransform _activeCardRect;
	CanvasGroup _activeCardGroup;
	Vector2 _activeCardRestAnchored;
	bool _activeCardRestCaptured;
	RectTransform _minimizedRoot;
	RectTransform _cycleRoot;
	Text _cycleBinding;
	Text _cycleLabel;
	readonly List<MinimizedRow> _minimizedRows = new List<MinimizedRow>();
	readonly List<string> _minimizedTitles = new List<string>();

	class MinimizedRow
	{
		public GameObject go;
		public RectTransform rect;
		public Text title;
	}

	public bool IsVisible => _visible;

	public float TaskCompleteFeedbackDuration => ResolveFeedbackDuration( taskCompleteFeedback, 0.35f );

	public float TutorialCompleteFeedbackDuration => ResolveFeedbackDuration( tutorialCompleteFeedback, 0.9f );

	public float HideFeedbackDuration => ResolveFeedbackDuration( hideFeedback, 0.25f );

	public void Setup()
	{
		if ( dismissButton != null )
		{
			dismissButton.onClick.RemoveAllListeners();
			dismissButton.gameObject.SetActive( false );
			dismissButton.interactable = false;
		}

		if ( stepText != null )
			stepText.gameObject.SetActive( false );

		if ( group == null )
			group = GetComponent<CanvasGroup>();

		EnsureUnscaledFeedbacks();

		_restScale = transform.localScale;
		if ( _restScale.sqrMagnitude < 0.0001f )
			_restScale = Vector3.one;

		RectTransform rect = transform as RectTransform;
		if ( rect != null )
		{
			_restAnchored = rect.anchoredPosition;
			_restAnchoredCaptured = true;
			if ( rect.rect.width > 1f )
				_popupWidth = rect.rect.width;
		}

		ConfigureTextOverflow( titleText );
		ConfigureTextOverflow( bodyText );
		ConfigureTextOverflow( hintText );
		ConfigureTextOverflow( tasksText );
		ConfigureTextOverflow( stepText );
		CacheCardChrome();
		EnsureActiveCard();
		EnsureSwitchFeedback();
		HideLegacyCycleHint();
		EnsureMinimizedRoot();
		EnsureCycleControl();

		HideImmediate();
		_ready = true;
	}

	void CacheCardChrome()
	{
		Transform background = transform.Find( "Background" );
		if ( background != null )
			_backgroundRect = background as RectTransform;

		if ( completeGlowGroup != null )
			_glowRect = completeGlowGroup.transform as RectTransform;

		PinChromeToTop( _backgroundRect, 0f, 0f );
		PinChromeToTop( _glowRect, 20f, 10f );
	}

	void EnsureActiveCard()
	{
		if ( _activeCardRect != null )
			return;

		Transform existing = transform.Find( "ActiveCard" );
		GameObject go = existing != null ? existing.gameObject : new GameObject( "ActiveCard", typeof( RectTransform ), typeof( CanvasGroup ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		_activeCardRect = go.GetComponent<RectTransform>();
		_activeCardRect.anchorMin = new Vector2( 0f, 1f );
		_activeCardRect.anchorMax = new Vector2( 1f, 1f );
		_activeCardRect.pivot = new Vector2( 0.5f, 1f );
		_activeCardRect.anchoredPosition = Vector2.zero;
		_activeCardRect.sizeDelta = new Vector2( 0f, MinPopupHeight );
		_activeCardRestAnchored = _activeCardRect.anchoredPosition;
		_activeCardRestCaptured = true;

		_activeCardGroup = go.GetComponent<CanvasGroup>();
		if ( _activeCardGroup == null )
			_activeCardGroup = go.AddComponent<CanvasGroup>();
		_activeCardGroup.blocksRaycasts = false;
		_activeCardGroup.interactable = false;

		ReparentToActiveCard( "Background" );
		ReparentToActiveCard( "CompleteGlow" );
		ReparentToActiveCard( "Title" );
		ReparentToActiveCard( "Body" );
		ReparentToActiveCard( "Tasks" );
		ReparentToActiveCard( "Hint" );
		ReparentToActiveCard( "Step" );
		if ( dismissButton != null )
			dismissButton.transform.SetParent( _activeCardRect, false );

		if ( _backgroundRect != null )
		{
			_backgroundRect.anchorMin = Vector2.zero;
			_backgroundRect.anchorMax = Vector2.one;
			_backgroundRect.pivot = new Vector2( 0.5f, 0.5f );
			_backgroundRect.anchoredPosition = Vector2.zero;
			_backgroundRect.offsetMin = Vector2.zero;
			_backgroundRect.offsetMax = Vector2.zero;
		}

		if ( _glowRect != null )
			PinChromeToTop( _glowRect, 20f, 10f );

		_activeCardRect.SetSiblingIndex( 0 );
		RetargetCardFeedbacks();
	}

	void ReparentToActiveCard( string childName )
	{
		Transform child = transform.Find( childName );
		if ( child == null || _activeCardRect == null )
			return;
		child.SetParent( _activeCardRect, false );
	}

	void RetargetCardFeedbacks()
	{
		if ( _activeCardRect == null || _activeCardGroup == null )
			return;

		RectTransform rootRect = transform as RectTransform;
		RetargetFeedbacks( showFeedback, rootRect, group, _activeCardRect, _activeCardGroup );
		RetargetFeedbacks( hideFeedback, rootRect, group, _activeCardRect, _activeCardGroup );
		RetargetFeedbacks( tutorialCompleteFeedback, rootRect, group, _activeCardRect, _activeCardGroup );
		RetargetFeedbacks( taskCompleteFeedback, rootRect, group, _activeCardRect, _activeCardGroup );
		RetargetFeedbacks( switchFeedback, rootRect, group, _activeCardRect, _activeCardGroup );
	}

	static void RetargetFeedbacks( Feedbacks feedbacks, RectTransform oldRect, CanvasGroup oldGroup, RectTransform newRect, CanvasGroup newGroup )
	{
		if ( feedbacks == null || feedbacks.FeedbackList == null )
			return;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
			RetargetFeedback( feedbacks.FeedbackList[ i ], oldRect, oldGroup, newRect, newGroup );
	}

	static void RetargetFeedback( Feedback feedback, RectTransform oldRect, CanvasGroup oldGroup, RectTransform newRect, CanvasGroup newGroup )
	{
		if ( feedback == null )
			return;

		ParallelFeedback parallel = feedback as ParallelFeedback;
		if ( parallel != null && parallel.Feedbacks != null )
		{
			for ( int i = 0; i < parallel.Feedbacks.Count; i++ )
				RetargetFeedback( parallel.Feedbacks[ i ], oldRect, oldGroup, newRect, newGroup );
			return;
		}

		SequenceFeedback sequence = feedback as SequenceFeedback;
		if ( sequence != null && sequence.Feedbacks != null )
		{
			for ( int i = 0; i < sequence.Feedbacks.Count; i++ )
				RetargetFeedback( sequence.Feedbacks[ i ], oldRect, oldGroup, newRect, newGroup );
			return;
		}

		CanvasGroupFadeFeedback fade = feedback as CanvasGroupFadeFeedback;
		if ( fade != null && fade.Target == oldGroup )
			fade.Target = newGroup;

		UiPunchScaleFeedback punch = feedback as UiPunchScaleFeedback;
		if ( punch != null && punch.Target == oldRect )
			punch.Target = newRect;

		UiAnchoredSlideFeedback slide = feedback as UiAnchoredSlideFeedback;
		if ( slide != null && slide.Target == oldRect )
			slide.Target = newRect;
	}

	void EnsureSwitchFeedback()
	{
		if ( switchFeedback != null && switchFeedback.FeedbackList != null && switchFeedback.FeedbackList.Count > 0 )
		{
			SetUnscaled( switchFeedback );
			return;
		}

		Transform existing = transform.Find( "SwitchFeedbacks" );
		GameObject go = existing != null ? existing.gameObject : new GameObject( "SwitchFeedbacks", typeof( RectTransform ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		if ( switchFeedback == null )
			switchFeedback = go.GetComponent<Feedbacks>();
		if ( switchFeedback == null )
			switchFeedback = go.AddComponent<Feedbacks>();
		switchFeedback.UseUnscaledTime = true;

		if ( switchFeedback.FeedbackList != null && switchFeedback.FeedbackList.Count > 0 )
			return;

		RectTransform card = _activeCardRect != null ? _activeCardRect : transform as RectTransform;
		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks = new List<Feedback>();

		UiPunchScaleFeedback punch = new UiPunchScaleFeedback();
		punch.Target = card;
		punch.Punch = new Vector3( 0.05f, 0.06f, 0f );
		punch.Duration = 0.28f;
		punch.UseUnscaledTime = true;
		punch.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f ),
			new Keyframe( 0.3f, 1f ),
			new Keyframe( 1f, 0f ) );
		parallel.Feedbacks.Add( punch );

		UiAnchoredSlideFeedback slide = new UiAnchoredSlideFeedback();
		slide.Target = card;
		slide.FromOffset = new Vector2( 16f, 10f );
		slide.ToOffset = Vector2.zero;
		slide.Duration = 0.22f;
		slide.UseUnscaledTime = true;
		slide.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f, 0f, 2.2f ),
			new Keyframe( 0.65f, 1.04f ),
			new Keyframe( 1f, 1f ) );
		parallel.Feedbacks.Add( slide );

		switchFeedback.AddFeedback( parallel );
	}

	static void PinChromeToTop( RectTransform rect, float extraSize, float extraUp )
	{
		if ( rect == null )
			return;

		rect.anchorMin = new Vector2( 0f, 1f );
		rect.anchorMax = new Vector2( 1f, 1f );
		rect.pivot = new Vector2( 0.5f, 1f );
		rect.anchoredPosition = new Vector2( 0f, extraUp );
		rect.sizeDelta = new Vector2( extraSize, MinPopupHeight + extraSize );
	}

	void HideLegacyCycleHint()
	{
		if ( cycleHintText == null )
			return;
		cycleHintText.gameObject.SetActive( false );
	}

	void EnsureMinimizedRoot()
	{
		if ( _minimizedRoot != null )
			return;

		Transform existing = transform.Find( "MinimizedList" );
		GameObject go = existing != null ? existing.gameObject : new GameObject( "MinimizedList", typeof( RectTransform ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		_minimizedRoot = go.GetComponent<RectTransform>();
		_minimizedRoot.anchorMin = new Vector2( 1f, 1f );
		_minimizedRoot.anchorMax = new Vector2( 1f, 1f );
		_minimizedRoot.pivot = new Vector2( 1f, 1f );
		_minimizedRoot.anchoredPosition = Vector2.zero;
		go.SetActive( false );
	}

	void EnsureCycleControl()
	{
		if ( _cycleRoot != null )
			return;

		Transform existing = transform.Find( "CycleControl" );
		GameObject go = existing != null ? existing.gameObject : new GameObject( "CycleControl", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		_cycleRoot = go.GetComponent<RectTransform>();
		_cycleRoot.anchorMin = new Vector2( 1f, 1f );
		_cycleRoot.anchorMax = new Vector2( 1f, 1f );
		_cycleRoot.pivot = new Vector2( 1f, 1f );
		_cycleRoot.sizeDelta = new Vector2( CycleControlWidth, CycleControlHeight );

		Image background = go.GetComponent<Image>();
		if ( background == null )
			background = go.AddComponent<Image>();
		background.color = new Color( 0.12f, 0.16f, 0.24f, 0.92f );
		background.raycastTarget = false;

		_cycleBinding = FindOrCreateText( go.transform, "Binding", "[Tab]", 20 );
		_cycleBinding.alignment = TextAnchor.MiddleCenter;
		_cycleBinding.color = new Color( 0.82f, 0.88f, 1f, 0.95f );
		RectTransform bindRect = _cycleBinding.rectTransform;
		bindRect.anchorMin = new Vector2( 0f, 0.5f );
		bindRect.anchorMax = new Vector2( 1f, 1f );
		bindRect.offsetMin = new Vector2( 8f, 0f );
		bindRect.offsetMax = new Vector2( -8f, -4f );

		_cycleLabel = FindOrCreateText( go.transform, "Label", "Switch", 16 );
		_cycleLabel.alignment = TextAnchor.MiddleCenter;
		_cycleLabel.color = new Color( 0.75f, 0.82f, 0.95f, 0.9f );
		RectTransform labelRect = _cycleLabel.rectTransform;
		labelRect.anchorMin = new Vector2( 0f, 0f );
		labelRect.anchorMax = new Vector2( 1f, 0.5f );
		labelRect.offsetMin = new Vector2( 8f, 4f );
		labelRect.offsetMax = new Vector2( -8f, 0f );

		go.SetActive( false );
	}

	Text FindOrCreateText( Transform parent, string name, string value, int fontSize )
	{
		Transform existing = parent.Find( name );
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject( name, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Text text = go.GetComponent<Text>();
		if ( text == null )
			text = go.AddComponent<Text>();
		text.font = titleText != null ? titleText.font : Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		text.fontSize = fontSize;
		text.fontStyle = FontStyle.Bold;
		text.color = Color.white;
		text.raycastTarget = false;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.supportRichText = false;
		text.text = value;
		return text;
	}

	static void ConfigureTextOverflow( Text text )
	{
		if ( text == null )
			return;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.resizeTextForBestFit = false;
	}

	void EnsureUnscaledFeedbacks()
	{
		SetUnscaled( showFeedback );
		SetUnscaled( hideFeedback );
		SetUnscaled( taskCompleteFeedback );
		SetUnscaled( tutorialCompleteFeedback );
		SetUnscaled( switchFeedback );
	}

	static void SetUnscaled( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.UseUnscaledTime = true;
	}

	public void Show( string title, string body, string hint, string tasksFormatted )
	{
		Show( title, body, hint, tasksFormatted, animate: true );
	}

	public void Show( string title, string body, string hint, string tasksFormatted, bool animate )
	{
		if ( !_ready )
			Setup();

		transform.SetAsLastSibling();
		_visible = true;
		_bodyBase = body ?? string.Empty;
		_tasksFormatted = tasksFormatted ?? string.Empty;

		StopAllFeedbacksExcept( null );

		transform.localScale = _restScale;
		transform.localRotation = Quaternion.identity;
		RestoreRestAnchored();
		RestoreActiveCardRest();
		RestoreTasksTransform();
		SetGlowAlpha( 0f );

		if ( titleText != null )
			titleText.text = title ?? string.Empty;
		if ( hintText != null )
		{
			hintText.text = hint ?? string.Empty;
			hintText.gameObject.SetActive( !string.IsNullOrEmpty( hint ) );
		}

		RefreshBodyAndTasks();
		LayoutContent();

		if ( group != null )
		{
			group.alpha = 1f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}

		if ( _activeCardGroup != null )
		{
			_activeCardGroup.blocksRaycasts = false;
			_activeCardGroup.interactable = false;
		}

		if ( animate && showFeedback != null )
			showFeedback.Play();
		else
		{
			if ( _activeCardGroup != null )
				_activeCardGroup.alpha = 1f;
			else if ( group != null )
				group.alpha = 1f;
			PlaySwitch();
		}
	}

	public void SetCycleHint( bool visible, string binding, string label )
	{
		_cycleVisible = visible;
		if ( _cycleBinding != null )
			_cycleBinding.text = string.IsNullOrEmpty( binding ) ? "[Tab]" : binding;
		if ( _cycleLabel != null )
			_cycleLabel.text = string.IsNullOrEmpty( label ) ? "Switch" : label;
		if ( _visible )
			LayoutContent();
	}

	public void SetMinimizedTutorials( List<string> titles )
	{
		_minimizedTitles.Clear();
		if ( titles != null )
		{
			for ( int i = 0; i < titles.Count; i++ )
			{
				string title = titles[ i ];
				if ( !string.IsNullOrEmpty( title ) )
					_minimizedTitles.Add( title );
			}
		}

		if ( _visible )
			LayoutContent();
	}

	public void SetTasks( string tasksFormatted )
	{
		_tasksFormatted = tasksFormatted ?? string.Empty;
		RefreshBodyAndTasks();
		LayoutContent();
	}

	public void PlayTaskComplete()
	{
		if ( taskCompleteFeedback != null )
			taskCompleteFeedback.Play();
	}

	public void PlayTutorialComplete()
	{
		if ( tutorialCompleteFeedback != null )
			tutorialCompleteFeedback.Play();
	}

	public void PlaySwitch()
	{
		EnsureSwitchFeedback();
		if ( switchFeedback != null )
			switchFeedback.Play();
	}

	public void Hide()
	{
		if ( !_visible )
		{
			HideImmediate();
			return;
		}

		_visible = false;
		_minimizedTitles.Clear();
		_cycleVisible = false;
		HideStackExtras();
		if ( showFeedback != null )
			showFeedback.Stop();
		if ( taskCompleteFeedback != null )
			taskCompleteFeedback.Stop();
		if ( tutorialCompleteFeedback != null )
			tutorialCompleteFeedback.Stop();
		if ( switchFeedback != null )
			switchFeedback.Stop();

		transform.localScale = _restScale;
		transform.localRotation = Quaternion.identity;
		RestoreRestAnchored();
		RestoreActiveCardRest();
		RestoreTasksTransform();

		if ( hideFeedback != null )
		{
			hideFeedback.Play();
			return;
		}

		HideImmediate();
	}

	public void HideActiveCard()
	{
		if ( !_ready )
			Setup();

		if ( showFeedback != null )
			showFeedback.Stop();
		if ( taskCompleteFeedback != null )
			taskCompleteFeedback.Stop();
		if ( tutorialCompleteFeedback != null )
			tutorialCompleteFeedback.Stop();
		if ( switchFeedback != null )
			switchFeedback.Stop();

		RestoreActiveCardRest();
		RestoreTasksTransform();
		SetGlowAlpha( 0f );

		if ( hideFeedback != null )
		{
			hideFeedback.Play();
			return;
		}

		if ( _activeCardGroup != null )
			_activeCardGroup.alpha = 0f;
	}

	public void HideImmediate()
	{
		_visible = false;
		StopAllFeedbacksExcept( null );

		transform.localScale = _restScale;
		transform.localRotation = Quaternion.identity;
		RestoreRestAnchored();
		RestoreActiveCardRest();
		RestoreTasksTransform();
		SetGlowAlpha( 0f );
		HideStackExtras();
		_minimizedTitles.Clear();
		_cycleVisible = false;

		if ( _activeCardGroup != null )
			_activeCardGroup.alpha = 0f;

		if ( group != null )
		{
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}
	}

	void HideStackExtras()
	{
		if ( _minimizedRoot != null )
			_minimizedRoot.gameObject.SetActive( false );
		if ( _cycleRoot != null )
			_cycleRoot.gameObject.SetActive( false );
	}

	void StopAllFeedbacksExcept( Feedbacks keep )
	{
		StopIfNot( showFeedback, keep );
		StopIfNot( hideFeedback, keep );
		StopIfNot( taskCompleteFeedback, keep );
		StopIfNot( tutorialCompleteFeedback, keep );
		StopIfNot( switchFeedback, keep );
	}

	static void StopIfNot( Feedbacks feedbacks, Feedbacks keep )
	{
		if ( feedbacks != null && feedbacks != keep )
			feedbacks.Stop();
	}

	void SetGlowAlpha( float alpha )
	{
		if ( completeGlowGroup != null )
			completeGlowGroup.alpha = alpha;
	}

	void RestoreTasksTransform()
	{
		if ( tasksText == null )
			return;
		Transform t = tasksText.transform;
		t.localScale = Vector3.one;
		t.localRotation = Quaternion.identity;
	}

	void RestoreRestAnchored()
	{
		if ( !_restAnchoredCaptured )
			return;
		RectTransform rect = transform as RectTransform;
		if ( rect != null )
			rect.anchoredPosition = _restAnchored;
	}

	void RestoreActiveCardRest()
	{
		if ( !_activeCardRestCaptured || _activeCardRect == null )
			return;
		_activeCardRect.anchoredPosition = _activeCardRestAnchored;
		_activeCardRect.localScale = Vector3.one;
		_activeCardRect.localRotation = Quaternion.identity;
	}

	void RefreshBodyAndTasks()
	{
		if ( tasksText != null )
		{
			if ( bodyText != null )
				bodyText.text = _bodyBase;
			tasksText.text = _tasksFormatted;
			tasksText.supportRichText = true;
			tasksText.gameObject.SetActive( !string.IsNullOrEmpty( _tasksFormatted ) );
			return;
		}

		if ( bodyText == null )
			return;

		bodyText.supportRichText = true;
		if ( string.IsNullOrEmpty( _tasksFormatted ) )
			bodyText.text = _bodyBase;
		else if ( string.IsNullOrEmpty( _bodyBase ) )
			bodyText.text = _tasksFormatted;
		else
			bodyText.text = _bodyBase + "\n\n" + _tasksFormatted;
	}

	void LayoutContent()
	{
		RectTransform root = transform as RectTransform;
		if ( root == null )
			return;

		float width = _popupWidth > 1f ? _popupWidth : DefaultPopupWidth;
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, width );

		float contentWidth = Mathf.Max( 40f, width - ContentPadX * 2f );
		Canvas.ForceUpdateCanvases();

		float y = -ContentPadTop;
		y = PlaceTextBlock( titleText, contentWidth, y, ContentGap );
		y = PlaceTextBlock( bodyText, contentWidth, y, ContentGap );
		y = PlaceTextBlock( tasksText, contentWidth, y, ContentGap );
		y = PlaceTextBlock( hintText, contentWidth, y, ContentGap );
		y = PlaceTextBlock( stepText, contentWidth, y, ContentGap );

		_activeCardHeight = Mathf.Max( MinPopupHeight, -y + ContentPadBottom );
		SizeCardChrome( _activeCardHeight );

		float stackY = -_activeCardHeight;
		stackY = LayoutMinimized( width, stackY );
		stackY = LayoutCycleControl( stackY );

		float height = Mathf.Max( _activeCardHeight, -stackY );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
	}

	void SizeCardChrome( float cardHeight )
	{
		if ( _activeCardRect != null )
			_activeCardRect.sizeDelta = new Vector2( 0f, cardHeight );

		if ( _glowRect != null )
			_glowRect.sizeDelta = new Vector2( 20f, cardHeight + 20f );
	}

	float LayoutMinimized( float width, float yFromTop )
	{
		EnsureMinimizedRoot();
		int count = _visible ? _minimizedTitles.Count : 0;
		bool show = count > 0;
		_minimizedRoot.gameObject.SetActive( show );
		if ( !show )
		{
			for ( int i = 0; i < _minimizedRows.Count; i++ )
			{
				MinimizedRow row = _minimizedRows[ i ];
				if ( row != null && row.go != null )
					row.go.SetActive( false );
			}
			return yFromTop;
		}

		float rowWidth = Mathf.Max( 80f, width - MinimizedIndent );
		_minimizedRoot.anchoredPosition = new Vector2( 0f, yFromTop - StackGap );
		float innerY = 0f;
		for ( int i = 0; i < count; i++ )
		{
			MinimizedRow row = EnsureMinimizedRow( i );
			row.go.SetActive( true );
			row.title.text = _minimizedTitles[ i ];
			row.rect.anchorMin = new Vector2( 1f, 1f );
			row.rect.anchorMax = new Vector2( 1f, 1f );
			row.rect.pivot = new Vector2( 1f, 1f );
			row.rect.sizeDelta = new Vector2( rowWidth, MinimizedHeight );
			row.rect.anchoredPosition = new Vector2( 0f, innerY );
			innerY -= MinimizedHeight + MinimizedGap;
		}

		for ( int i = count; i < _minimizedRows.Count; i++ )
		{
			MinimizedRow row = _minimizedRows[ i ];
			if ( row != null && row.go != null )
				row.go.SetActive( false );
		}

		_minimizedRoot.sizeDelta = new Vector2( rowWidth, -innerY - MinimizedGap );
		return yFromTop - StackGap + innerY + MinimizedGap;
	}

	MinimizedRow EnsureMinimizedRow( int index )
	{
		while ( _minimizedRows.Count <= index )
			_minimizedRows.Add( CreateMinimizedRow( _minimizedRows.Count ) );
		return _minimizedRows[ index ];
	}

	MinimizedRow CreateMinimizedRow( int index )
	{
		GameObject go = new GameObject( "Minimized_" + index, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		go.transform.SetParent( _minimizedRoot, false );

		Image background = go.GetComponent<Image>();
		background.color = new Color( 0.07f, 0.08f, 0.11f, 0.88f );
		background.raycastTarget = false;

		Text title = FindOrCreateText( go.transform, "Title", string.Empty, 22 );
		title.horizontalOverflow = HorizontalWrapMode.Overflow;
		title.verticalOverflow = VerticalWrapMode.Overflow;
		RectTransform titleRect = title.rectTransform;
		titleRect.anchorMin = Vector2.zero;
		titleRect.anchorMax = Vector2.one;
		titleRect.offsetMin = new Vector2( 16f, 4f );
		titleRect.offsetMax = new Vector2( -16f, -4f );

		return new MinimizedRow
		{
			go = go,
			rect = go.GetComponent<RectTransform>(),
			title = title
		};
	}

	float LayoutCycleControl( float yFromTop )
	{
		EnsureCycleControl();
		bool show = _visible && _cycleVisible;
		_cycleRoot.gameObject.SetActive( show );
		if ( !show )
			return yFromTop;

		_cycleRoot.anchoredPosition = new Vector2( 0f, yFromTop - CycleControlGap );
		return yFromTop - CycleControlGap - CycleControlHeight;
	}

	float PlaceTextBlock( Text text, float contentWidth, float yFromTop, float gapAfter )
	{
		if ( text == null || !text.gameObject.activeInHierarchy )
			return yFromTop;

		RectTransform rt = text.rectTransform;
		rt.anchorMin = new Vector2( 0.5f, 1f );
		rt.anchorMax = new Vector2( 0.5f, 1f );
		rt.pivot = new Vector2( 0.5f, 1f );
		rt.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, contentWidth );

		Canvas.ForceUpdateCanvases();
		float preferred = text.preferredHeight;
		float height = Mathf.Max( preferred, text.fontSize * 1.1f );
		rt.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
		rt.anchoredPosition = new Vector2( 0f, yFromTop );

		return yFromTop - height - gapAfter;
	}

	static float ResolveFeedbackDuration( Feedbacks feedbacks, float fallback )
	{
		if ( feedbacks == null || feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
			return fallback;

		float max = 0f;
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			Feedback feedback = feedbacks.FeedbackList[ i ];
			if ( feedback == null || !feedback.Enabled )
				continue;
			float hold = feedback.GetHoldDuration();
			if ( hold > max )
				max = hold;
		}

		return max > 0.01f ? max : fallback;
	}

	public static string FormatTasks( TutorialTask[] tasks, Func<string, bool> isComplete )
	{
		if ( tasks == null || tasks.Length == 0 )
			return string.Empty;

		StringBuilder sb = new StringBuilder();
		for ( int i = 0; i < tasks.Length; i++ )
		{
			TutorialTask task = tasks[ i ];
			if ( task == null )
				continue;

			if ( sb.Length > 0 )
				sb.Append( '\n' );

			bool done = isComplete != null && !string.IsNullOrEmpty( task.id ) && isComplete( task.id );
			if ( done )
				sb.Append( "<color=#9ad89a>✓ " );
			else
				sb.Append( "• " );

			sb.Append( task.label ?? string.Empty );
			if ( done )
				sb.Append( "</color>" );
		}

		return sb.ToString();
	}
}
