using System;
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
	[SerializeField] CanvasGroup completeGlowGroup;

	bool _visible;
	bool _ready;
	string _bodyBase = string.Empty;
	string _tasksFormatted = string.Empty;
	Vector3 _restScale = Vector3.one;
	Vector2 _restAnchored;
	bool _restAnchoredCaptured;
	float _popupWidth = DefaultPopupWidth;

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
		EnsureCycleHint();
		ConfigureTextOverflow( cycleHintText );

		HideImmediate();
		_ready = true;
	}

	void EnsureCycleHint()
	{
		if ( cycleHintText != null )
			return;

		Transform existing = transform.Find( "CycleHint" );
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject( "CycleHint", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		RectTransform rt = go.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2( 0.5f, 1f );
		rt.anchorMax = new Vector2( 0.5f, 1f );
		rt.pivot = new Vector2( 0.5f, 1f );
		rt.sizeDelta = new Vector2( 480f, 28f );

		cycleHintText = go.GetComponent<Text>();
		if ( cycleHintText == null )
			cycleHintText = go.AddComponent<Text>();
		cycleHintText.font = titleText != null ? titleText.font : Resources.GetBuiltinResource<Font>( "Arial.ttf" );
		cycleHintText.fontSize = 18;
		cycleHintText.fontStyle = FontStyle.Bold;
		cycleHintText.alignment = TextAnchor.MiddleRight;
		cycleHintText.color = new Color( 0.75f, 0.82f, 0.95f, 0.9f );
		cycleHintText.raycastTarget = false;
		cycleHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
		cycleHintText.verticalOverflow = VerticalWrapMode.Overflow;
		go.SetActive( false );
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
	}

	static void SetUnscaled( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.UseUnscaledTime = true;
	}

	public void Show( string title, string body, string hint, string tasksFormatted )
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
			group.blocksRaycasts = false;
			group.interactable = false;
		}

		if ( showFeedback != null )
			showFeedback.Play();
		else if ( group != null )
			group.alpha = 1f;
	}

	public void SetCycleHint( bool visible, string text )
	{
		if ( cycleHintText == null )
			return;

		cycleHintText.text = text ?? string.Empty;
		cycleHintText.gameObject.SetActive( visible && !string.IsNullOrEmpty( text ) );
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

	public void Hide()
	{
		if ( !_visible )
		{
			HideImmediate();
			return;
		}

		_visible = false;
		if ( showFeedback != null )
			showFeedback.Stop();
		if ( taskCompleteFeedback != null )
			taskCompleteFeedback.Stop();
		if ( tutorialCompleteFeedback != null )
			tutorialCompleteFeedback.Stop();

		transform.localScale = _restScale;
		transform.localRotation = Quaternion.identity;
		RestoreRestAnchored();
		RestoreTasksTransform();

		if ( hideFeedback != null )
		{
			hideFeedback.Play();
			return;
		}

		HideImmediate();
	}

	public void HideImmediate()
	{
		_visible = false;
		StopAllFeedbacksExcept( null );

		transform.localScale = _restScale;
		transform.localRotation = Quaternion.identity;
		RestoreRestAnchored();
		RestoreTasksTransform();
		SetGlowAlpha( 0f );

		if ( group != null )
		{
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}
	}

	void StopAllFeedbacksExcept( Feedbacks keep )
	{
		StopIfNot( showFeedback, keep );
		StopIfNot( hideFeedback, keep );
		StopIfNot( taskCompleteFeedback, keep );
		StopIfNot( tutorialCompleteFeedback, keep );
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
		y = PlaceTextBlock( cycleHintText, contentWidth, y, ContentGap );

		float height = Mathf.Max( MinPopupHeight, -y + ContentPadBottom );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
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
