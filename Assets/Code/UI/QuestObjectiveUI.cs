using System.Text;

using FeedbackSystem;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nearby objective list. Wire references on the Interface prefab.
/// Driven by <see cref="TutorialHudChangedEvent"/> from <see cref="ObjectiveSystem"/>.
/// </summary>
public class QuestObjectiveUI : MonoBehaviour
{
	[SerializeField] CanvasGroup group;
	[SerializeField] Text title;
	[SerializeField] Text objective;
	[SerializeField] Vector2 padding = new Vector2( 22f, 22f );
	[SerializeField] float textSpacing = 8f;
	[SerializeField] [Range( 100, 150 )] int contextualSizePercent = 118;

	[Header( "Feedbacks" )]
	[SerializeField] Feedbacks subObjectiveCompleteFeedback;
	[SerializeField] Feedbacks objectiveCompleteFeedback;

	RectTransform _panel;
	bool _layoutReady;
	bool _subscribed;
	bool _wantVisible;
	bool _cinematicHidden;

	public void Setup()
	{
		EnsureLayout();
		ResolveFeedbackRefs();
		Subscribe();
		_wantVisible = false;
		ApplyVisibility();
	}

	public void SetCinematicHidden( bool hidden )
	{
		_cinematicHidden = hidden;
		ApplyVisibility();
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		Unsubscribe();
	}

	void ResolveFeedbackRefs()
	{
		if ( subObjectiveCompleteFeedback == null )
		{
			Transform existing = transform.Find( "SubObjectiveCompleteFeedbacks" );
			if ( existing != null )
				subObjectiveCompleteFeedback = existing.GetComponent<Feedbacks>();
		}

		if ( objectiveCompleteFeedback == null )
		{
			Transform existing = transform.Find( "ObjectiveCompleteFeedbacks" );
			if ( existing != null )
				objectiveCompleteFeedback = existing.GetComponent<Feedbacks>();
		}
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<TutorialHudChangedEvent>( OnHudChanged );
		EventBus.Subscribe<ObjectiveSubCompletedEvent>( OnSubCompleted );
		EventBus.Subscribe<ObjectiveCompletedEvent>( OnObjectiveCompleted );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<TutorialHudChangedEvent>( OnHudChanged );
		EventBus.Unsubscribe<ObjectiveSubCompletedEvent>( OnSubCompleted );
		EventBus.Unsubscribe<ObjectiveCompletedEvent>( OnObjectiveCompleted );
		_subscribed = false;
	}

	void OnSubCompleted( ObjectiveSubCompletedEvent evt )
	{
		if ( subObjectiveCompleteFeedback != null )
			subObjectiveCompleteFeedback.Play();
	}

	void OnObjectiveCompleted( ObjectiveCompletedEvent evt )
	{
		if ( objectiveCompleteFeedback != null )
			objectiveCompleteFeedback.Play();
	}

	void OnHudChanged( TutorialHudChangedEvent evt )
	{
		EnsureLayout();

		_wantVisible = !evt.Cleared && HasRows( evt );
		ApplyVisibility();

		if ( title != null )
			title.text = string.IsNullOrEmpty( evt.Title ) ? "Nearby" : evt.Title;
		if ( objective != null )
			objective.text = FormatRows( evt );

		RefreshLayout();
	}

	void EnsureLayout()
	{
		if ( _layoutReady )
			return;

		_panel = transform as RectTransform;

		VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>();
		if ( layout == null )
			layout = gameObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset( Mathf.RoundToInt( padding.x ), Mathf.RoundToInt( padding.x ), Mathf.RoundToInt( padding.y ), Mathf.RoundToInt( padding.y ) );
		layout.spacing = textSpacing;
		layout.childAlignment = TextAnchor.UpperLeft;
		layout.childControlHeight = true;
		layout.childControlWidth = true;
		layout.childForceExpandHeight = false;
		layout.childForceExpandWidth = false;

		ContentSizeFitter fitter = GetComponent<ContentSizeFitter>();
		if ( fitter == null )
			fitter = gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		ConfigureTextLayout( title );
		ConfigureTextLayout( objective );

		_layoutReady = true;
	}

	static void ConfigureTextLayout( Text text )
	{
		if ( text == null )
			return;

		RectTransform rect = text.rectTransform;
		rect.anchorMin = new Vector2( 0f, 1f );
		rect.anchorMax = new Vector2( 0f, 1f );
		rect.pivot = new Vector2( 0f, 1f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = Vector2.zero;

		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.supportRichText = true;

		ContentSizeFitter fitter = text.GetComponent<ContentSizeFitter>();
		if ( fitter == null )
			fitter = text.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	void ApplyVisibility()
	{
		if ( group != null )
			group.alpha = _wantVisible && !_cinematicHidden ? 1f : 0f;
	}

	void RefreshLayout()
	{
		if ( _panel == null )
			return;
		LayoutRebuilder.ForceRebuildLayoutImmediate( _panel );
	}

	static bool HasRows( TutorialHudChangedEvent evt )
	{
		if ( evt.Rows != null && evt.Rows.Length > 0 )
			return true;
		return !string.IsNullOrEmpty( evt.ObjectiveText );
	}

	string FormatRows( TutorialHudChangedEvent evt )
	{
		if ( evt.Rows == null || evt.Rows.Length == 0 )
			return evt.ObjectiveText ?? string.Empty;

		int contextualFontSize = ResolveContextualFontSize();
		StringBuilder sb = new StringBuilder();
		for ( int i = 0; i < evt.Rows.Length; i++ )
		{
			TutorialHudRow row = evt.Rows[ i ];
			if ( i > 0 )
				sb.Append( '\n' );
			for ( int n = 0; n < row.Indent; n++ )
				sb.Append( "  " );

			bool isReward = !string.IsNullOrEmpty( row.Text ) && row.Text.StartsWith( "Reward:" );
			if ( isReward )
				sb.Append( "<color=#f0d878>" );

			if ( row.Complete )
				sb.Append( "<color=#9ad89a>" );

			if ( row.ContextualFocus )
			{
				sb.Append( "<size=" ).Append( contextualFontSize ).Append( "><b>" );
			}

			if ( isReward )
				sb.Append( "★ " );
			else
				sb.Append( row.Complete ? "✓ " : "• " );
			sb.Append( row.Text ?? string.Empty );
			if ( row.Optional )
				sb.Append( " (optional)" );

			if ( row.ContextualFocus )
				sb.Append( "</b></size>" );

			if ( row.Complete )
				sb.Append( "</color>" );

			if ( isReward )
				sb.Append( "</color>" );
		}

		return sb.ToString();
	}

	int ResolveContextualFontSize()
	{
		int baseSize = objective != null ? objective.fontSize : 14;
		return Mathf.Max( 1, Mathf.RoundToInt( baseSize * ( contextualSizePercent / 100f ) ) );
	}
}
