#if UNITY_EDITOR
using System.Collections.Generic;

using FeedbackSystem;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures QuestObjective completion and show Feedbacks children exist on the Interface prefab.
/// </summary>
public static class QuestObjectiveFeedbackPostprocessor
{
	const string InterfacePrefabPath = "Assets/Addressables/Interface.prefab";
	const string SubClipPath = "Assets/Audio/SFX/Rewarding/ObjectiveComplete_01_TEMP_DELETE.wav";
	const string CompleteClipPath = "Assets/Audio/SFX/Rewarding/ObjectiveComplete_02_TEMP_DELETE.wav";

	static bool _ranThisDomain;

	[InitializeOnLoadMethod]
	static void QueuePatch()
	{
		if ( _ranThisDomain )
			return;
		_ranThisDomain = true;
		EditorApplication.delayCall += EnsureFeedbacksOnPrefab;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureObjectiveFeedbacks )]
	static void MenuEnsureFeedbacks()
	{
		EnsureFeedbacksOnPrefab();
		Debug.Log( "QuestObjective show/complete Feedbacks ensured on Interface prefab." );
	}

	public static void EnsureFeedbacksOnPrefab()
	{
		GameObject root = PrefabUtility.LoadPrefabContents( InterfacePrefabPath );
		if ( root == null )
			return;

		try
		{
			Transform quest = FindDeepChild( root.transform, "QuestObjective" );
			if ( quest == null )
			{
				Debug.LogWarning( "QuestObjectiveFeedbackPostprocessor: QuestObjective missing on Interface prefab." );
				return;
			}

			QuestObjectiveUI ui = quest.GetComponent<QuestObjectiveUI>();
			if ( ui == null )
			{
				Debug.LogWarning( "QuestObjectiveFeedbackPostprocessor: QuestObjectiveUI missing." );
				return;
			}

			Feedbacks showFeedback = EnsureShowChild( quest );
			Feedbacks hideFeedback = EnsureHideChild( quest );
			Feedbacks subFeedback = EnsureSfxChild( quest, "SubObjectiveCompleteFeedbacks", SubClipPath );
			Feedbacks completeFeedback = EnsureSfxChild( quest, "ObjectiveCompleteFeedbacks", CompleteClipPath );
			EnsureCompleteVisuals( quest, subFeedback, completeFeedback );

			SerializedObject so = new SerializedObject( ui );
			so.FindProperty( "showFeedback" ).objectReferenceValue = showFeedback;
			so.FindProperty( "hideFeedback" ).objectReferenceValue = hideFeedback;
			so.FindProperty( "subObjectiveCompleteFeedback" ).objectReferenceValue = subFeedback;
			so.FindProperty( "objectiveCompleteFeedback" ).objectReferenceValue = completeFeedback;
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset( root, InterfacePrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static Feedbacks EnsureShowChild( Transform parent )
	{
		const string childName = "ShowObjectiveFeedbacks";
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();
		feedbacks.UseUnscaledTime = true;

		if ( feedbacks.FeedbackList != null && feedbacks.FeedbackList.Count > 0 )
			return feedbacks;

		CanvasGroup group = parent.GetComponent<CanvasGroup>();
		RectTransform rect = parent as RectTransform;

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

		feedbacks.AddFeedback( parallel );
		EditorUtility.SetDirty( feedbacks );
		return feedbacks;
	}

	static Feedbacks EnsureHideChild( Transform parent )
	{
		const string childName = "HideObjectiveFeedbacks";
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();
		feedbacks.UseUnscaledTime = true;

		if ( feedbacks.FeedbackList != null && feedbacks.FeedbackList.Count > 0 )
			return feedbacks;

		CanvasGroup group = parent.GetComponent<CanvasGroup>();
		RectTransform rect = parent as RectTransform;

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
		slide.Curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
		parallel.Feedbacks.Add( slide );

		feedbacks.AddFeedback( parallel );
		EditorUtility.SetDirty( feedbacks );
		return feedbacks;
	}

	static void EnsureCompleteVisuals( Transform quest, Feedbacks subFeedback, Feedbacks completeFeedback )
	{
		RectTransform rect = quest as RectTransform;
		WrapWithPunch( subFeedback, rect, new Vector3( 0.035f, 0.045f, 0f ), 0.22f, 0f );
		WrapWithPunch( completeFeedback, rect, new Vector3( 0.08f, 0.1f, 0f ), 0.4f, 0.35f );
	}

	static void WrapWithPunch( Feedbacks feedbacks, RectTransform rect, Vector3 punchAmount, float punchDuration, float holdAfter )
	{
		if ( feedbacks == null || ContainsPunch( feedbacks.FeedbackList ) )
			return;

		UiPunchScaleFeedback punch = new UiPunchScaleFeedback();
		punch.Target = rect;
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
		}
		else
		{
			feedbacks.AddFeedback( parallel );
		}

		EditorUtility.SetDirty( feedbacks );
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

	static Feedbacks EnsureSfxChild( Transform parent, string childName, string clipPath )
	{
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();
		feedbacks.UseUnscaledTime = true;

		if ( feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
		{
			AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>( clipPath );
			PlaySFXFeedback sfx = new PlaySFXFeedback
			{
				Clip = clip,
				VolumeMin = 0.85f,
				VolumeMax = 1f,
				PitchMin = 0.98f,
				PitchMax = 1.02f,
				SpatialBlend = 0f
			};
			feedbacks.AddFeedback( sfx );
			EditorUtility.SetDirty( feedbacks );
		}

		return feedbacks;
	}

	static Transform FindDeepChild( Transform parent, string name )
	{
		if ( parent.name == name )
			return parent;

		for ( int i = 0; i < parent.childCount; i++ )
		{
			Transform found = FindDeepChild( parent.GetChild( i ), name );
			if ( found != null )
				return found;
		}

		return null;
	}
}
#endif
