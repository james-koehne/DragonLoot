using System;
using System.Collections;
using System.Collections.Generic;
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
	[SerializeField] Feedbacks showFeedback;
	[SerializeField] Feedbacks hideFeedback;
	[SerializeField] Feedbacks subObjectiveCompleteFeedback;
	[SerializeField] Feedbacks objectiveCompleteFeedback;
	[SerializeField] Feedbacks taskCountPopFeedback;

	RectTransform _panel;
	Vector2 _restAnchored;
	Vector3 _restScale = Vector3.one;
	bool _layoutReady;
	bool _subscribed;
	bool _wantVisible;
	bool _cinematicHidden;
	string _shownObjectiveKey;
	TutorialHudChangedEvent _lastHud;
	TutorialHudChangedEvent _pendingHud;
	bool _hasPendingHud;
	bool _playingCompleteSequence;
	Coroutine _completeSequenceRoutine;

	public void Setup()
	{
		EnsureLayout();
		ResolveFeedbackRefs();
		EnsureShowFeedback();
		EnsureHideFeedback();
		EnsureCompleteFeedback();
		EnsureCountPopFeedback();
		Subscribe();
		_wantVisible = false;
		_shownObjectiveKey = null;
		_hasPendingHud = false;
		CancelCompleteSequence();
		HideImmediate();
	}

	public void SetCinematicHidden( bool hidden )
	{
		_cinematicHidden = hidden;
		if ( hidden )
		{
			CancelCompleteSequence();
			HideImmediate();
			return;
		}

		if ( _hasPendingHud )
			ApplyHud( _pendingHud, playShow: true );
		else if ( _wantVisible )
			PlayShowFeedback();
	}

	bool IsShown => _wantVisible && !_cinematicHidden;

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		CancelCompleteSequence();
		Unsubscribe();
	}

	void OnDestroy()
	{
		CancelCompleteSequence();
		Unsubscribe();
	}

	void ResolveFeedbackRefs()
	{
		if ( showFeedback == null )
		{
			Transform existing = transform.Find( "ShowObjectiveFeedbacks" );
			if ( existing != null )
				showFeedback = existing.GetComponent<Feedbacks>();
		}

		if ( hideFeedback == null )
		{
			Transform existing = transform.Find( "HideObjectiveFeedbacks" );
			if ( existing != null )
				hideFeedback = existing.GetComponent<Feedbacks>();
		}

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

		if ( taskCountPopFeedback == null )
		{
			Transform existing = transform.Find( "TaskCountPopFeedbacks" );
			if ( existing != null )
				taskCountPopFeedback = existing.GetComponent<Feedbacks>();
		}

		SetUnscaled( showFeedback );
		SetUnscaled( hideFeedback );
		SetUnscaled( subObjectiveCompleteFeedback );
		SetUnscaled( objectiveCompleteFeedback );
		SetUnscaled( taskCountPopFeedback );
	}

	static void SetUnscaled( Feedbacks feedbacks )
	{
		if ( feedbacks != null )
			feedbacks.UseUnscaledTime = true;
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
		if ( taskCountPopFeedback != null )
			taskCountPopFeedback.Stop();
		RestoreObjectiveTransform();
		PaintRowComplete( evt.ObjectiveId, evt.ObjectiveId + "/" + evt.SubId );
		if ( _playingCompleteSequence )
			return;
		if ( subObjectiveCompleteFeedback != null )
			subObjectiveCompleteFeedback.Play();
	}

	void OnObjectiveCompleted( ObjectiveCompletedEvent evt )
	{
		if ( _cinematicHidden )
			return;

		PaintObjectiveComplete( evt.ObjectiveId );
		BeginCompleteSequence();
	}

	void OnHudChanged( TutorialHudChangedEvent evt )
	{
		if ( _playingCompleteSequence )
		{
			StoreHud( evt, asPending: true );
			return;
		}

		ApplyHud( evt, playShow: true );
	}

	void ApplyHud( TutorialHudChangedEvent evt, bool playShow )
	{
		EnsureLayout();
		bool countsIncreased = CountsIncreased( _lastHud, evt );
		StoreHud( evt, asPending: false );

		bool wasShown = IsShown;
		_wantVisible = !evt.Cleared && HasRows( evt );
		string newKey = BuildObjectiveKey( evt );
		bool sameObjective = !string.IsNullOrEmpty( newKey ) && newKey == _shownObjectiveKey;

		if ( title != null )
			title.text = string.IsNullOrEmpty( evt.Title ) ? "Nearby" : evt.Title;
		if ( objective != null )
			objective.text = FormatRows( evt );

		RefreshLayout();

		if ( IsShown && playShow && ( !wasShown || !sameObjective ) )
			PlayShowFeedback();
		else if ( !IsShown )
			HideImmediate();
		else if ( wasShown && sameObjective && countsIncreased && !_playingCompleteSequence )
			PlayTaskCountPop();

		_shownObjectiveKey = newKey;
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
		CaptureRestPose();
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

	void CaptureRestPose()
	{
		if ( _panel == null )
			return;
		_restAnchored = _panel.anchoredPosition;
		_restScale = _panel.localScale;
		if ( _restScale.sqrMagnitude < 0.0001f )
			_restScale = Vector3.one;
	}

	void RestoreRestPose()
	{
		if ( _panel == null )
			return;
		_panel.anchoredPosition = _restAnchored;
		_panel.localScale = _restScale;
	}

	void PlayShowFeedback()
	{
		RestoreRestPose();
		RestoreObjectiveTransform();
		if ( taskCountPopFeedback != null )
			taskCountPopFeedback.Stop();
		if ( showFeedback != null )
		{
			if ( group != null )
				group.alpha = 0f;
			showFeedback.Play();
			return;
		}

		if ( group != null )
			group.alpha = 1f;
	}

	void PlayTaskCountPop()
	{
		EnsureCountPopFeedback();
		if ( taskCountPopFeedback != null )
			taskCountPopFeedback.Play();
	}

	void EnsureShowFeedback()
	{
		if ( showFeedback != null && showFeedback.FeedbackList != null && showFeedback.FeedbackList.Count > 0 )
		{
			SetUnscaled( showFeedback );
			return;
		}

		Transform existing = transform.Find( "ShowObjectiveFeedbacks" );
		GameObject go = existing != null ? existing.gameObject : new GameObject( "ShowObjectiveFeedbacks" );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		if ( showFeedback == null )
			showFeedback = go.GetComponent<Feedbacks>();
		if ( showFeedback == null )
			showFeedback = go.AddComponent<Feedbacks>();
		showFeedback.UseUnscaledTime = true;

		if ( showFeedback.FeedbackList != null && showFeedback.FeedbackList.Count > 0 )
			return;

		RectTransform rect = transform as RectTransform;
		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks = new List<Feedback>();

		CanvasGroupFadeFeedback fade = new CanvasGroupFadeFeedback();
		fade.Target = group;
		fade.From = 0f;
		fade.To = 1f;
		fade.Duration = 0.22f;
		fade.UseUnscaledTime = true;
		fade.Curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
		parallel.Feedbacks.Add( fade );

		UiPunchScaleFeedback punch = new UiPunchScaleFeedback();
		punch.Target = rect;
		punch.Punch = new Vector3( 0.04f, 0.06f, 0f );
		punch.Duration = 0.28f;
		punch.UseUnscaledTime = true;
		punch.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f ),
			new Keyframe( 0.3f, 1f ),
			new Keyframe( 1f, 0f ) );
		parallel.Feedbacks.Add( punch );

		UiAnchoredSlideFeedback slide = new UiAnchoredSlideFeedback();
		slide.Target = rect;
		slide.FromOffset = new Vector2( -28f, 0f );
		slide.ToOffset = Vector2.zero;
		slide.Duration = 0.24f;
		slide.UseUnscaledTime = true;
		slide.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f, 0f, 2.2f ),
			new Keyframe( 0.65f, 1.04f ),
			new Keyframe( 1f, 1f ) );
		parallel.Feedbacks.Add( slide );

		showFeedback.AddFeedback( parallel );
	}

	void EnsureHideFeedback()
	{
		if ( hideFeedback != null && hideFeedback.FeedbackList != null && hideFeedback.FeedbackList.Count > 0 )
		{
			SetUnscaled( hideFeedback );
			return;
		}

		hideFeedback = EnsureFeedbacksChild( "HideObjectiveFeedbacks" );
		if ( hideFeedback.FeedbackList != null && hideFeedback.FeedbackList.Count > 0 )
			return;

		RectTransform rect = transform as RectTransform;
		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks = new List<Feedback>();

		CanvasGroupFadeFeedback fade = new CanvasGroupFadeFeedback();
		fade.Target = group;
		fade.From = 1f;
		fade.To = 0f;
		fade.Duration = 0.22f;
		fade.UseUnscaledTime = true;
		fade.CaptureCurrentAsFrom = true;
		fade.Curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
		parallel.Feedbacks.Add( fade );

		UiAnchoredSlideFeedback slide = new UiAnchoredSlideFeedback();
		slide.Target = rect;
		slide.FromOffset = Vector2.zero;
		slide.ToOffset = new Vector2( -28f, 0f );
		slide.Duration = 0.22f;
		slide.UseUnscaledTime = true;
		slide.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f, 0f, 0f ),
			new Keyframe( 1f, 1f, 1.8f, 0f ) );
		parallel.Feedbacks.Add( slide );

		hideFeedback.AddFeedback( parallel );
	}

	void EnsureCompleteFeedback()
	{
		objectiveCompleteFeedback = EnsureFeedbacksChild( "ObjectiveCompleteFeedbacks", objectiveCompleteFeedback );
		SetUnscaled( objectiveCompleteFeedback );
		EnsurePunchOnFeedback( objectiveCompleteFeedback, new Vector3( 0.08f, 0.1f, 0f ), 0.4f, 0.35f );

		subObjectiveCompleteFeedback = EnsureFeedbacksChild( "SubObjectiveCompleteFeedbacks", subObjectiveCompleteFeedback );
		SetUnscaled( subObjectiveCompleteFeedback );
		EnsurePunchOnFeedback( subObjectiveCompleteFeedback, new Vector3( 0.035f, 0.045f, 0f ), 0.22f, 0f );
	}

	void EnsureCountPopFeedback()
	{
		if ( taskCountPopFeedback != null && taskCountPopFeedback.FeedbackList != null && taskCountPopFeedback.FeedbackList.Count > 0 )
		{
			SetUnscaled( taskCountPopFeedback );
			RetargetCountPop();
			return;
		}

		Transform existing = transform.Find( "TaskCountPopFeedbacks" );
		GameObject go = existing != null ? existing.gameObject : new GameObject( "TaskCountPopFeedbacks", typeof( RectTransform ) );
		if ( existing == null )
			go.transform.SetParent( transform, false );

		if ( taskCountPopFeedback == null )
			taskCountPopFeedback = go.GetComponent<Feedbacks>();
		if ( taskCountPopFeedback == null )
			taskCountPopFeedback = go.AddComponent<Feedbacks>();
		taskCountPopFeedback.UseUnscaledTime = true;

		if ( taskCountPopFeedback.FeedbackList != null && taskCountPopFeedback.FeedbackList.Count > 0 )
		{
			RetargetCountPop();
			return;
		}

		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks = new List<Feedback>();
		parallel.Feedbacks.Add( CreateCountPopPunch() );
		parallel.Feedbacks.Add( CreateCountPopColor() );
		taskCountPopFeedback.AddFeedback( parallel );
		RetargetCountPop();
	}

	UiPunchScaleFeedback CreateCountPopPunch()
	{
		UiPunchScaleFeedback punch = new UiPunchScaleFeedback();
		punch.Punch = new Vector3( 0.06f, 0.14f, 0f );
		punch.Duration = 0.16f;
		punch.UseUnscaledTime = true;
		punch.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f ),
			new Keyframe( 0.28f, 1f ),
			new Keyframe( 1f, 0f ) );
		return punch;
	}

	UiGraphicColorPunchFeedback CreateCountPopColor()
	{
		UiGraphicColorPunchFeedback color = new UiGraphicColorPunchFeedback();
		color.PunchColor = new Color( 1f, 0.92f, 0.55f, 1f );
		color.Duration = 0.16f;
		color.UseUnscaledTime = true;
		color.CaptureRestOnPlay = true;
		color.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f ),
			new Keyframe( 0.3f, 1f ),
			new Keyframe( 1f, 0f ) );
		return color;
	}

	void RetargetCountPop()
	{
		if ( taskCountPopFeedback == null || taskCountPopFeedback.FeedbackList == null )
			return;

		RectTransform objectiveRect = objective != null ? objective.rectTransform : null;
		for ( int i = 0; i < taskCountPopFeedback.FeedbackList.Count; i++ )
			RetargetCountPopRecursive( taskCountPopFeedback.FeedbackList[ i ], objectiveRect, objective );
	}

	static void RetargetCountPopRecursive( Feedback feedback, RectTransform targetRect, Graphic targetGraphic )
	{
		if ( feedback == null )
			return;

		ParallelFeedback parallel = feedback as ParallelFeedback;
		if ( parallel != null && parallel.Feedbacks != null )
		{
			for ( int i = 0; i < parallel.Feedbacks.Count; i++ )
				RetargetCountPopRecursive( parallel.Feedbacks[ i ], targetRect, targetGraphic );
			return;
		}

		SequenceFeedback sequence = feedback as SequenceFeedback;
		if ( sequence != null && sequence.Feedbacks != null )
		{
			for ( int i = 0; i < sequence.Feedbacks.Count; i++ )
				RetargetCountPopRecursive( sequence.Feedbacks[ i ], targetRect, targetGraphic );
			return;
		}

		UiPunchScaleFeedback punch = feedback as UiPunchScaleFeedback;
		if ( punch != null )
			punch.Target = targetRect;

		UiGraphicColorPunchFeedback color = feedback as UiGraphicColorPunchFeedback;
		if ( color != null )
			color.Target = targetGraphic;
	}

	Feedbacks EnsureFeedbacksChild( string childName )
	{
		return EnsureFeedbacksChild( childName, null );
	}

	Feedbacks EnsureFeedbacksChild( string childName, Feedbacks existing )
	{
		if ( existing != null )
		{
			SetUnscaled( existing );
			return existing;
		}

		Transform child = transform.Find( childName );
		GameObject go = child != null ? child.gameObject : new GameObject( childName );
		if ( child == null )
			go.transform.SetParent( transform, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();
		feedbacks.UseUnscaledTime = true;
		return feedbacks;
	}

	void EnsurePunchOnFeedback( Feedbacks feedbacks, Vector3 punchAmount, float punchDuration, float holdAfter )
	{
		if ( feedbacks == null )
			return;
		if ( ContainsPunch( feedbacks.FeedbackList ) )
			return;

		UiPunchScaleFeedback punch = new UiPunchScaleFeedback();
		punch.Target = transform as RectTransform;
		punch.Punch = punchAmount;
		punch.Duration = punchDuration;
		punch.UseUnscaledTime = true;
		punch.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f ),
			new Keyframe( 0.28f, 1f ),
			new Keyframe( 1f, 0f ) );

		List<Feedback> existing = new List<Feedback>();
		if ( feedbacks.FeedbackList != null )
		{
			for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
			{
				Feedback feedback = feedbacks.FeedbackList[ i ];
				if ( feedback != null )
					existing.Add( feedback );
			}
		}

		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks = new List<Feedback>();
		parallel.Feedbacks.Add( punch );
		for ( int i = 0; i < existing.Count; i++ )
			parallel.Feedbacks.Add( existing[ i ] );

		feedbacks.FeedbackList.Clear();
		if ( holdAfter > 0f )
		{
			SequenceFeedback sequence = new SequenceFeedback();
			sequence.Feedbacks = new List<Feedback>();
			sequence.Feedbacks.Add( parallel );
			DelayFeedback delay = new DelayFeedback();
			delay.Duration = holdAfter;
			sequence.Feedbacks.Add( delay );
			feedbacks.AddFeedback( sequence );
			return;
		}

		feedbacks.AddFeedback( parallel );
	}

	static bool ContainsPunch( List<Feedback> list )
	{
		if ( list == null )
			return false;

		for ( int i = 0; i < list.Count; i++ )
		{
			Feedback feedback = list[ i ];
			if ( feedback is UiPunchScaleFeedback )
				return true;

			ParallelFeedback parallel = feedback as ParallelFeedback;
			if ( parallel != null && ContainsPunch( parallel.Feedbacks ) )
				return true;

			SequenceFeedback sequence = feedback as SequenceFeedback;
			if ( sequence != null && ContainsPunch( sequence.Feedbacks ) )
				return true;
		}

		return false;
	}

	void StoreHud( TutorialHudChangedEvent evt, bool asPending )
	{
		TutorialHudChangedEvent copy = evt;
		if ( evt.Rows != null )
		{
			TutorialHudRow[] rows = new TutorialHudRow[ evt.Rows.Length ];
			Array.Copy( evt.Rows, rows, evt.Rows.Length );
			copy.Rows = rows;
		}

		if ( asPending )
		{
			_pendingHud = copy;
			_hasPendingHud = true;
			return;
		}

		_lastHud = copy;
		_hasPendingHud = false;
	}

	void PaintObjectiveComplete( string objectiveId )
	{
		PaintRowComplete( objectiveId, null );
	}

	void PaintRowComplete( string objectiveId, string rowId )
	{
		if ( _lastHud.Rows == null || _lastHud.Rows.Length == 0 )
			return;

		bool changed = false;
		for ( int i = 0; i < _lastHud.Rows.Length; i++ )
		{
			TutorialHudRow row = _lastHud.Rows[ i ];
			if ( !RowMatches( row, objectiveId, rowId ) )
				continue;
			if ( row.Complete )
				continue;
			row.Complete = true;
			_lastHud.Rows[ i ] = row;
			changed = true;
		}

		if ( !changed )
			return;

		_wantVisible = true;
		if ( objective != null )
			objective.text = FormatRows( _lastHud );
		RefreshLayout();
		if ( group != null && group.alpha < 1f )
			group.alpha = 1f;
	}

	static bool RowMatches( TutorialHudRow row, string objectiveId, string rowId )
	{
		if ( string.IsNullOrEmpty( row.ObjectiveId ) || string.IsNullOrEmpty( objectiveId ) )
			return false;
		if ( !string.IsNullOrEmpty( rowId ) )
			return row.ObjectiveId == rowId;
		return row.ObjectiveId == objectiveId || row.ObjectiveId.StartsWith( objectiveId + "/" );
	}

	void BeginCompleteSequence()
	{
		if ( _playingCompleteSequence )
			return;

		_playingCompleteSequence = true;
		if ( _completeSequenceRoutine != null )
			StopCoroutine( _completeSequenceRoutine );
		_completeSequenceRoutine = StartCoroutine( CompleteThenHideRoutine() );
	}

	void CancelCompleteSequence()
	{
		if ( _completeSequenceRoutine != null )
		{
			StopCoroutine( _completeSequenceRoutine );
			_completeSequenceRoutine = null;
		}

		_playingCompleteSequence = false;
	}

	IEnumerator CompleteThenHideRoutine()
	{
		RestoreRestPose();
		RestoreObjectiveTransform();
		if ( showFeedback != null )
			showFeedback.Stop();
		if ( subObjectiveCompleteFeedback != null )
			subObjectiveCompleteFeedback.Stop();
		if ( taskCountPopFeedback != null )
			taskCountPopFeedback.Stop();
		if ( hideFeedback != null )
			hideFeedback.Stop();

		if ( objectiveCompleteFeedback != null )
			objectiveCompleteFeedback.Play();

		yield return WaitUnscaled( ResolveFeedbackDuration( objectiveCompleteFeedback, 0.85f ) );

		bool pendingHasRows = _hasPendingHud && !_pendingHud.Cleared && HasRows( _pendingHud );
		if ( pendingHasRows )
		{
			_playingCompleteSequence = false;
			_completeSequenceRoutine = null;
			ApplyHud( _pendingHud, playShow: false );
			yield break;
		}

		if ( hideFeedback != null )
			hideFeedback.Play();
		else if ( group != null )
			group.alpha = 0f;

		yield return WaitUnscaled( ResolveFeedbackDuration( hideFeedback, 0.22f ) );

		_playingCompleteSequence = false;
		_completeSequenceRoutine = null;
		HideImmediate();
		if ( _hasPendingHud )
			ApplyHud( _pendingHud, playShow: false );
	}

	static IEnumerator WaitUnscaled( float seconds )
	{
		float remaining = Mathf.Max( 0f, seconds );
		while ( remaining > 0f )
		{
			remaining -= Time.unscaledDeltaTime;
			yield return null;
		}
	}

	static float ResolveFeedbackDuration( Feedbacks feedbacks, float fallback )
	{
		if ( feedbacks == null || feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
			return fallback;

		float max = MaxHold( feedbacks.FeedbackList );
		return max > 0.05f ? max : fallback;
	}

	static float MaxHold( List<Feedback> list )
	{
		if ( list == null )
			return 0f;

		float max = 0f;
		for ( int i = 0; i < list.Count; i++ )
		{
			Feedback feedback = list[ i ];
			if ( feedback == null || !feedback.Enabled )
				continue;

			float hold = feedback.GetHoldDuration();
			PlaySFXFeedback sfx = feedback as PlaySFXFeedback;
			if ( sfx != null && sfx.Clip != null )
				hold = Mathf.Max( hold, sfx.Clip.length );

			ParallelFeedback parallel = feedback as ParallelFeedback;
			if ( parallel != null )
				hold = Mathf.Max( hold, MaxHold( parallel.Feedbacks ) );

			SequenceFeedback sequence = feedback as SequenceFeedback;
			if ( sequence != null )
				hold = Mathf.Max( hold, MaxHold( sequence.Feedbacks ) );

			if ( hold > max )
				max = hold;
		}

		return max;
	}

	void HideImmediate()
	{
		if ( showFeedback != null )
			showFeedback.Stop();
		if ( hideFeedback != null )
			hideFeedback.Stop();
		if ( subObjectiveCompleteFeedback != null )
			subObjectiveCompleteFeedback.Stop();
		if ( objectiveCompleteFeedback != null )
			objectiveCompleteFeedback.Stop();
		if ( taskCountPopFeedback != null )
			taskCountPopFeedback.Stop();
		RestoreRestPose();
		RestoreObjectiveTransform();
		if ( group != null )
			group.alpha = 0f;
	}

	void RestoreObjectiveTransform()
	{
		if ( objective == null )
			return;
		objective.transform.localScale = Vector3.one;
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
		bool wroteRow = false;
		for ( int i = 0; i < evt.Rows.Length; i++ )
		{
			TutorialHudRow row = evt.Rows[ i ];
			if ( wroteRow )
				sb.Append( '\n' );
			if ( row.IsReward && wroteRow )
				sb.Append( '\n' );
			for ( int n = 0; n < row.Indent; n++ )
				sb.Append( "  " );

			bool isReward = row.IsReward || ( !string.IsNullOrEmpty( row.Text ) && row.Text.StartsWith( "Reward:" ) );
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

			wroteRow = true;
		}

		return sb.ToString();
	}

	static string BuildObjectiveKey( TutorialHudChangedEvent evt )
	{
		if ( evt.Cleared || evt.Rows == null || evt.Rows.Length == 0 )
			return string.Empty;

		StringBuilder sb = new StringBuilder();
		for ( int i = 0; i < evt.Rows.Length; i++ )
		{
			TutorialHudRow row = evt.Rows[ i ];
			if ( row.Indent != 0 )
				continue;
			if ( sb.Length > 0 )
				sb.Append( '|' );
			sb.Append( row.ObjectiveId );
		}

		return sb.ToString();
	}

	static bool CountsIncreased( TutorialHudChangedEvent previous, TutorialHudChangedEvent next )
	{
		if ( previous.Rows == null || next.Rows == null )
			return false;

		for ( int i = 0; i < next.Rows.Length; i++ )
		{
			TutorialHudRow nextRow = next.Rows[ i ];
			if ( nextRow.Indent <= 0 || nextRow.Complete || nextRow.IsReward )
				continue;
			if ( string.IsNullOrEmpty( nextRow.ObjectiveId ) )
				continue;

			int nextCount;
			if ( !TryReadProgressCount( nextRow.Text, out nextCount ) )
				continue;

			int previousCount = 0;
			bool found = false;
			for ( int p = 0; p < previous.Rows.Length; p++ )
			{
				TutorialHudRow previousRow = previous.Rows[ p ];
				if ( previousRow.ObjectiveId != nextRow.ObjectiveId )
					continue;
				found = true;
				TryReadProgressCount( previousRow.Text, out previousCount );
				break;
			}

			if ( found && nextCount > previousCount )
				return true;
		}

		return false;
	}

	static bool TryReadProgressCount( string text, out int current )
	{
		current = 0;
		if ( string.IsNullOrEmpty( text ) )
			return false;

		int slash = text.LastIndexOf( '/' );
		if ( slash <= 0 )
			return false;

		int start = slash - 1;
		while ( start >= 0 && char.IsDigit( text[ start ] ) )
			start--;
		start++;
		if ( start >= slash )
			return false;

		return int.TryParse( text.Substring( start, slash - start ), out current );
	}

	int ResolveContextualFontSize()
	{
		int baseSize = objective != null ? objective.fontSize : 14;
		return Mathf.Max( 1, Mathf.RoundToInt( baseSize * ( contextualSizePercent / 100f ) ) );
	}
}
