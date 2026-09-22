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

	[Header( "Attention" )]
	[SerializeField] CanvasGroup attentionGroup;
	[SerializeField] Transform attentionDiamond;
	[SerializeField] Transform attentionGlow;

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
	string _objectiveFormatted = string.Empty;
	readonly UiCountChunkPop _countPop = new UiCountChunkPop();

	public void Setup()
	{
		BindLayout();
		ResolveFeedbackRefs();
		ResolveAttentionRefs();
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

		SetUnscaled( showFeedback );
		SetUnscaled( hideFeedback );
		SetUnscaled( subObjectiveCompleteFeedback );
		SetUnscaled( objectiveCompleteFeedback );
	}

	void ResolveAttentionRefs()
	{
		if ( attentionGroup == null )
		{
			Transform existing = transform.Find( "Attention" );
			if ( existing != null )
				attentionGroup = existing.GetComponent<CanvasGroup>();
		}

		if ( attentionDiamond == null )
		{
			Transform existing = transform.Find( "Attention/Diamond" );
			if ( existing != null )
				attentionDiamond = existing;
		}

		if ( attentionGlow == null )
		{
			Transform existing = transform.Find( "Attention/Glow" );
			if ( existing != null )
				attentionGlow = existing;
		}
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
		StopCountChunkPop( restore: true );
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
		BindLayout();
		string previousText = FormatRows( _lastHud );
		string nextText = FormatRows( evt );
		StoreHud( evt, asPending: false );

		bool wasShown = IsShown;
		_wantVisible = !evt.Cleared && HasRows( evt );
		string newKey = BuildObjectiveKey( evt );
		bool sameObjective = !string.IsNullOrEmpty( newKey ) && newKey == _shownObjectiveKey;

		if ( title != null )
			title.text = string.IsNullOrEmpty( evt.Title ) ? "Nearby" : evt.Title;
		_objectiveFormatted = nextText;
		if ( objective != null )
			objective.text = nextText;

		RefreshLayout();

		if ( IsShown && playShow && ( !wasShown || !sameObjective ) )
			PlayShowFeedback();
		else if ( !IsShown )
			HideImmediate();
		else if ( wasShown && sameObjective && !_playingCompleteSequence )
			PlayTaskCountPop( previousText, nextText );

		_shownObjectiveKey = newKey;
	}

	void BindLayout()
	{
		if ( _layoutReady )
			return;

		_panel = transform as RectTransform;

		VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>();
		if ( layout != null )
		{
			layout.padding = new RectOffset( Mathf.RoundToInt( padding.x ), Mathf.RoundToInt( padding.x ), Mathf.RoundToInt( padding.y ), Mathf.RoundToInt( padding.y ) );
			layout.spacing = textSpacing;
		}

		_layoutReady = true;
		CaptureRestPose();
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
		ResetAttentionChrome();
		StopCountChunkPop( restore: true );
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

	void PlayTaskCountPop( string previousText, string nextText )
	{
		if ( !_countPop.Begin( previousText, nextText ) )
			return;
		int fontSize = objective != null ? objective.fontSize : 14;
		Color restColor = objective != null ? objective.color : Color.white;
		ApplyCountChunkDisplay( _countPop.CurrentDisplay( fontSize, restColor ) );
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
		_objectiveFormatted = FormatRows( _lastHud );
		if ( objective != null )
			objective.text = _objectiveFormatted;
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
		StopCountChunkPop( restore: true );
		if ( hideFeedback != null )
			hideFeedback.Stop();
		ResetAttentionChrome();

		if ( objectiveCompleteFeedback != null )
			objectiveCompleteFeedback.Play();

		yield return WaitUnscaled( ResolveFeedbackDuration( objectiveCompleteFeedback, 0.85f ) );

		bool pendingHasRows = _hasPendingHud && !_pendingHud.Cleared && HasRows( _pendingHud );
		if ( pendingHasRows )
		{
			_playingCompleteSequence = false;
			_completeSequenceRoutine = null;
			ApplyHud( _pendingHud, playShow: true );
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
			ApplyHud( _pendingHud, playShow: true );
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
		StopCountChunkPop( restore: false );
		RestoreRestPose();
		RestoreObjectiveTransform();
		ResetAttentionChrome();
		if ( group != null )
			group.alpha = 0f;
	}

	void RestoreObjectiveTransform()
	{
		if ( objective == null )
			return;
		objective.transform.localScale = Vector3.one;
	}

	void ResetAttentionChrome()
	{
		if ( attentionGroup != null )
			attentionGroup.alpha = 0f;
		if ( attentionDiamond != null )
		{
			attentionDiamond.localRotation = Quaternion.identity;
			attentionDiamond.localScale = Vector3.one;
		}
		if ( attentionGlow != null )
			attentionGlow.localScale = Vector3.one;
	}

	void ApplyCountChunkDisplay( string display )
	{
		if ( objective != null )
		{
			objective.supportRichText = true;
			objective.text = display ?? string.Empty;
		}
		RefreshLayout();
	}

	void StopCountChunkPop( bool restore )
	{
		bool wasActive = _countPop.Active;
		_countPop.Stop();
		if ( restore && wasActive )
			ApplyCountChunkDisplay( _objectiveFormatted );
	}

	void Update()
	{
		TickCountChunkPop();
	}

	void TickCountChunkPop()
	{
		if ( !_countPop.Active )
			return;

		int fontSize = objective != null ? objective.fontSize : 14;
		Color restColor = objective != null ? objective.color : Color.white;
		string display;
		bool running = _countPop.Tick( Time.unscaledDeltaTime, fontSize, restColor, out display );
		ApplyCountChunkDisplay( display );
		if ( !running )
			ApplyCountChunkDisplay( _objectiveFormatted );
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

	int ResolveContextualFontSize()
	{
		int baseSize = objective != null ? objective.fontSize : 14;
		return Mathf.Max( 1, Mathf.RoundToInt( baseSize * ( contextualSizePercent / 100f ) ) );
	}
}
