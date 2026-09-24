#if UNITY_EDITOR
using System.Collections.Generic;

using FeedbackSystem;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ensures QuestObjective completion and show Feedbacks children exist on the Interface prefab.
/// </summary>
public static class QuestObjectiveFeedbackPostprocessor
{
	const string InterfacePrefabPath = "Assets/Addressables/Interface.prefab";
	const string SubClipPath = "Assets/Audio/SFX/Rewarding/ObjectiveComplete_01_TEMP_DELETE.wav";
	const string CompleteClipPath = "Assets/Audio/SFX/Rewarding/ObjectiveComplete_02_TEMP_DELETE.wav";
	const string ShowClipPath = "Assets/Audio/SFX/Rewarding/MUSIC_EFFECT_Solo_Harp_Positive_01_stereo.wav";
	const string DiamondSpritePath = "Assets/Textures/Icons/icon_quest.png";
	const string GlowSpritePath = "Assets/Textures/Circle.png";

	static readonly Color QuestCyan = new Color( 0.2f, 0.95f, 1f, 1f );

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

			AttentionChrome attention = EnsureAttentionOverlay( quest );
			EnsureTitleDiamondRow( quest, attention );
			Feedbacks showFeedback = EnsureShowChild( quest, attention );
			Feedbacks hideFeedback = EnsureHideChild( quest );
			Feedbacks subFeedback = EnsureSfxChild( quest, "SubObjectiveCompleteFeedbacks", SubClipPath );
			Feedbacks completeFeedback = EnsureSfxChild( quest, "ObjectiveCompleteFeedbacks", CompleteClipPath );
			EnsureCompleteVisuals( quest, subFeedback, completeFeedback );

			SerializedObject so = new SerializedObject( ui );
			so.FindProperty( "showFeedback" ).objectReferenceValue = showFeedback;
			so.FindProperty( "hideFeedback" ).objectReferenceValue = hideFeedback;
			so.FindProperty( "subObjectiveCompleteFeedback" ).objectReferenceValue = subFeedback;
			so.FindProperty( "objectiveCompleteFeedback" ).objectReferenceValue = completeFeedback;
			so.FindProperty( "attentionGroup" ).objectReferenceValue = attention.Group;
			so.FindProperty( "attentionDiamond" ).objectReferenceValue = attention.Diamond;
			SerializedProperty glowProp = so.FindProperty( "attentionGlow" );
			if ( glowProp != null )
				glowProp.objectReferenceValue = attention.GlowImage != null ? attention.GlowImage.transform : null;
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset( root, InterfacePrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	struct AttentionChrome
	{
		public CanvasGroup Group;
		public Transform Diamond;
		public Image DiamondImage;
		public Image GlowImage;
	}

	static AttentionChrome EnsureAttentionOverlay( Transform quest )
	{
		AttentionChrome chrome = new AttentionChrome();

		Transform attention = quest.Find( "Attention" );
		if ( attention == null )
			attention = quest.Find( "TitleRow/Attention" );
		GameObject attentionGo = attention != null ? attention.gameObject : new GameObject( "Attention", typeof( RectTransform ), typeof( CanvasGroup ), typeof( LayoutElement ) );
		if ( attention == null )
			attentionGo.transform.SetParent( quest, false );

		RectTransform attentionRect = attentionGo.GetComponent<RectTransform>();
		attentionRect.anchorMin = Vector2.zero;
		attentionRect.anchorMax = Vector2.one;
		attentionRect.pivot = new Vector2( 0.5f, 0.5f );
		attentionRect.anchoredPosition = Vector2.zero;
		attentionRect.offsetMin = Vector2.zero;
		attentionRect.offsetMax = Vector2.zero;
		attentionRect.SetSiblingIndex( 0 );

		LayoutElement layoutElement = attentionGo.GetComponent<LayoutElement>();
		if ( layoutElement == null )
			layoutElement = attentionGo.AddComponent<LayoutElement>();
		layoutElement.ignoreLayout = true;

		chrome.Group = attentionGo.GetComponent<CanvasGroup>();
		if ( chrome.Group == null )
			chrome.Group = attentionGo.AddComponent<CanvasGroup>();
		chrome.Group.alpha = 0f;
		chrome.Group.interactable = false;
		chrome.Group.blocksRaycasts = false;

		chrome.GlowImage = EnsureImageChild( attentionGo.transform, "Glow", GlowSpritePath, new Vector2( 40f, 40f ), new Color( QuestCyan.r, QuestCyan.g, QuestCyan.b, 0.4f ) );
		chrome.DiamondImage = EnsureImageChild( attentionGo.transform, "Diamond", DiamondSpritePath, new Vector2( 22f, 22f ), QuestCyan );
		chrome.Diamond = chrome.DiamondImage != null ? chrome.DiamondImage.transform : null;

		if ( chrome.GlowImage != null )
			chrome.GlowImage.transform.SetSiblingIndex( 0 );
		if ( chrome.Diamond != null )
			chrome.Diamond.SetSiblingIndex( 1 );

		return chrome;
	}

	static void EnsureTitleDiamondRow( Transform quest, AttentionChrome attention )
	{
		if ( quest == null || attention.Group == null )
			return;

		QuestObjectiveUI ui = quest.GetComponent<QuestObjectiveUI>();
		Text title = null;
		if ( ui != null )
		{
			SerializedObject so = new SerializedObject( ui );
			title = so.FindProperty( "title" ).objectReferenceValue as Text;
		}

		if ( title == null )
			return;

		Transform titleRow = quest.Find( "TitleRow" );
		if ( titleRow == null )
		{
			GameObject rowGo = new GameObject( "TitleRow", typeof( RectTransform ), typeof( HorizontalLayoutGroup ), typeof( LayoutElement ) );
			titleRow = rowGo.transform;
			titleRow.SetParent( quest, false );
			titleRow.SetSiblingIndex( title.rectTransform.GetSiblingIndex() );
		}

		HorizontalLayoutGroup rowLayout = titleRow.GetComponent<HorizontalLayoutGroup>();
		if ( rowLayout == null )
			rowLayout = titleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
		rowLayout.spacing = 8f;
		rowLayout.childAlignment = TextAnchor.MiddleLeft;
		rowLayout.childControlWidth = true;
		rowLayout.childControlHeight = true;
		rowLayout.childForceExpandWidth = false;
		rowLayout.childForceExpandHeight = false;

		LayoutElement rowElement = titleRow.GetComponent<LayoutElement>();
		if ( rowElement == null )
			rowElement = titleRow.gameObject.AddComponent<LayoutElement>();
		rowElement.flexibleWidth = 1f;

		Transform attentionTransform = attention.Group.transform;
		if ( attentionTransform.parent != titleRow )
			attentionTransform.SetParent( titleRow, false );
		attentionTransform.SetSiblingIndex( 0 );

		RectTransform titleRect = title.rectTransform;
		if ( titleRect.parent != titleRow )
			titleRect.SetParent( titleRow, false );
		titleRect.SetSiblingIndex( 1 );

		LayoutElement attentionLayout = attentionTransform.GetComponent<LayoutElement>();
		if ( attentionLayout == null )
			attentionLayout = attentionTransform.gameObject.AddComponent<LayoutElement>();
		attentionLayout.ignoreLayout = false;
		attentionLayout.preferredWidth = 22f;
		attentionLayout.preferredHeight = 22f;
		attentionLayout.minWidth = 22f;
		attentionLayout.minHeight = 22f;
		attentionLayout.flexibleWidth = 0f;

		RectTransform attentionRect = attentionTransform as RectTransform;
		if ( attentionRect != null )
		{
			attentionRect.anchorMin = new Vector2( 0f, 0.5f );
			attentionRect.anchorMax = new Vector2( 0f, 0.5f );
			attentionRect.pivot = new Vector2( 0.5f, 0.5f );
			attentionRect.sizeDelta = new Vector2( 22f, 22f );
			attentionRect.anchoredPosition = Vector2.zero;
		}

		PinCenteredChild( attention.Diamond, 22f );
		PinCenteredChild( attention.GlowImage != null ? attention.GlowImage.transform : null, 40f );

		LayoutElement titleLayout = title.GetComponent<LayoutElement>();
		if ( titleLayout == null )
			titleLayout = title.gameObject.AddComponent<LayoutElement>();
		titleLayout.flexibleWidth = 1f;
		titleLayout.minHeight = 22f;
	}

	static void PinCenteredChild( Transform child, float size )
	{
		if ( child == null )
			return;

		RectTransform rect = child as RectTransform;
		if ( rect == null )
			return;

		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2( size, size );
		rect.localRotation = Quaternion.identity;
		rect.localScale = Vector3.one;
	}

	static Image EnsureImageChild( Transform parent, string childName, string spritePath, Vector2 size, Color color )
	{
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 0.5f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = size;
		rect.localRotation = Quaternion.identity;
		rect.localScale = Vector3.one;

		Image image = go.GetComponent<Image>();
		if ( image == null )
			image = go.AddComponent<Image>();
		image.raycastTarget = false;
		image.preserveAspect = true;
		image.color = color;

		Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>( spritePath );
		if ( sprite != null )
			image.sprite = sprite;

		return image;
	}

	static Feedbacks EnsureShowChild( Transform parent, AttentionChrome attention )
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

		if ( feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
		{
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
			punch.Punch = new Vector3( 0.08f, 0.1f, 0f );
			punch.Duration = 0.34f;
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
		}

		InjectShowAttention( feedbacks, parent, attention );
		EditorUtility.SetDirty( feedbacks );
		return feedbacks;
	}

	static void InjectShowAttention( Feedbacks feedbacks, Transform quest, AttentionChrome attention )
	{
		if ( feedbacks == null || feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0 )
			return;

		ParallelFeedback parallel = FindOrCreateShowParallel( feedbacks );
		if ( parallel.Feedbacks == null )
			parallel.Feedbacks = new List<Feedback>();

		if ( !ContainsSfx( parallel.Feedbacks ) )
		{
			AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>( ShowClipPath );
			parallel.Feedbacks.Add( new PlaySFXFeedback
			{
				Clip = clip,
				VolumeMin = 0.85f,
				VolumeMax = 1f,
				PitchMin = 0.98f,
				PitchMax = 1.02f,
				SpatialBlend = 0f
			} );
		}

		Text title = null;
		Text objective = null;
		QuestObjectiveUI ui = quest.GetComponent<QuestObjectiveUI>();
		if ( ui != null )
		{
			SerializedObject so = new SerializedObject( ui );
			title = so.FindProperty( "title" ).objectReferenceValue as Text;
			objective = so.FindProperty( "objective" ).objectReferenceValue as Text;
		}

		if ( title != null && !ContainsTitleColorPunch( parallel.Feedbacks, title ) )
		{
			UiGraphicColorPunchFeedback colorPunch = new UiGraphicColorPunchFeedback();
			colorPunch.Target = title;
			colorPunch.PunchColor = QuestCyan;
			colorPunch.Duration = 0.45f;
			colorPunch.UseUnscaledTime = true;
			colorPunch.CaptureRestOnPlay = true;
			colorPunch.Curve = new AnimationCurve(
				new Keyframe( 0f, 0f ),
				new Keyframe( 0.3f, 1f ),
				new Keyframe( 1f, 0f ) );
			parallel.Feedbacks.Add( colorPunch );
		}

		if ( objective != null && !ContainsObjectivePunch( parallel.Feedbacks, objective.transform ) )
		{
			UiPunchScaleFeedback objectivePunch = new UiPunchScaleFeedback();
			objectivePunch.Target = objective.transform;
			objectivePunch.Punch = new Vector3( 0.1f, 0.14f, 0f );
			objectivePunch.Duration = 0.38f;
			objectivePunch.UseUnscaledTime = true;
			objectivePunch.Curve = new AnimationCurve(
				new Keyframe( 0f, 0f ),
				new Keyframe( 0.28f, 1f ),
				new Keyframe( 1f, 0f ) );
			parallel.Feedbacks.Add( objectivePunch );
		}

		BumpPanelPunch( parallel.Feedbacks, quest as RectTransform );

		if ( attention.Group != null && !ContainsAttentionFadeIn( parallel.Feedbacks, attention.Group ) )
		{
			CanvasGroupFadeFeedback fadeIn = new CanvasGroupFadeFeedback();
			fadeIn.Target = attention.Group;
			fadeIn.From = 0f;
			fadeIn.To = 1f;
			fadeIn.Duration = 0.18f;
			fadeIn.UseUnscaledTime = true;
			fadeIn.Curve = AnimationCurve.EaseInOut( 0f, 0f, 1f, 1f );
			parallel.Feedbacks.Add( fadeIn );
		}
		else
		{
			for ( int i = 0; i < parallel.Feedbacks.Count; i++ )
			{
				CanvasGroupFadeFeedback fade = parallel.Feedbacks[ i ] as CanvasGroupFadeFeedback;
				if ( fade == null || fade.Target != attention.Group )
					continue;
				fade.To = 1f;
			}
		}

		EnsureDiamondSpin( parallel.Feedbacks, attention.Diamond );
		EnsureGlowPulse( parallel.Feedbacks, attention.GlowImage != null ? attention.GlowImage.transform : null );
		RemoveAttentionFadeOut( parallel.Feedbacks, attention.Group );
	}

	static void BumpPanelPunch( List<Feedback> list, RectTransform panel )
	{
		if ( list == null || panel == null )
			return;

		for ( int i = 0; i < list.Count; i++ )
		{
			UiPunchScaleFeedback punch = list[ i ] as UiPunchScaleFeedback;
			if ( punch == null || punch.Target != panel )
				continue;
			if ( punch.Punch.x < 0.07f )
				punch.Punch = new Vector3( 0.08f, 0.1f, 0f );
			if ( punch.Duration < 0.32f )
				punch.Duration = 0.34f;
			return;
		}
	}

	static void EnsureDiamondSpin( List<Feedback> list, Transform diamond )
	{
		if ( diamond == null || list == null )
			return;

		UiSpinRotationFeedback existing = FindSpin( list, diamond );
		if ( existing != null )
		{
			existing.DegreesPerSecond = 160f;
			existing.Duration = 8f;
			existing.UseUnscaledTime = true;
			existing.RestoreOnComplete = true;
			return;
		}

		UiSpinRotationFeedback spin = new UiSpinRotationFeedback();
		spin.Target = diamond;
		spin.DegreesPerSecond = 160f;
		spin.Duration = 8f;
		spin.UseUnscaledTime = true;
		spin.RestoreOnComplete = true;
		list.Add( spin );

		if ( !ContainsPunchOn( list, diamond ) )
		{
			UiPunchScaleFeedback diamondPunch = new UiPunchScaleFeedback();
			diamondPunch.Target = diamond;
			diamondPunch.Punch = new Vector3( 0.18f, 0.18f, 0f );
			diamondPunch.Duration = 0.35f;
			diamondPunch.UseUnscaledTime = true;
			diamondPunch.Curve = new AnimationCurve(
				new Keyframe( 0f, 0f ),
				new Keyframe( 0.35f, 1f ),
				new Keyframe( 1f, 0f ) );
			list.Add( diamondPunch );
		}
	}

	static void EnsureGlowPulse( List<Feedback> list, Transform glow )
	{
		if ( glow == null || list == null )
			return;

		UiPulseScaleFeedback existing = FindPulse( list, glow );
		if ( existing != null )
		{
			existing.Amplitude = new Vector3( 0.35f, 0.35f, 0f );
			existing.CyclesPerSecond = 1.15f;
			existing.Duration = 8f;
			existing.UseUnscaledTime = true;
			existing.RestoreOnComplete = true;
			return;
		}

		UiPulseScaleFeedback pulse = new UiPulseScaleFeedback();
		pulse.Target = glow;
		pulse.Amplitude = new Vector3( 0.35f, 0.35f, 0f );
		pulse.CyclesPerSecond = 1.15f;
		pulse.Duration = 8f;
		pulse.UseUnscaledTime = true;
		pulse.RestoreOnComplete = true;
		list.Add( pulse );
	}

	static void RemoveAttentionFadeOut( List<Feedback> list, CanvasGroup group )
	{
		if ( group == null || list == null )
			return;

		for ( int i = list.Count - 1; i >= 0; i-- )
		{
			SequenceFeedback sequence = list[ i ] as SequenceFeedback;
			if ( sequence == null || sequence.Feedbacks == null )
				continue;

			bool isAttentionFadeOut = false;
			for ( int j = 0; j < sequence.Feedbacks.Count; j++ )
			{
				CanvasGroupFadeFeedback fade = sequence.Feedbacks[ j ] as CanvasGroupFadeFeedback;
				if ( fade != null && fade.Target == group && fade.To <= 0.01f )
				{
					isAttentionFadeOut = true;
					break;
				}
			}

			if ( isAttentionFadeOut )
				list.RemoveAt( i );
		}
	}

	static ParallelFeedback FindOrCreateShowParallel( Feedbacks feedbacks )
	{
		for ( int i = 0; i < feedbacks.FeedbackList.Count; i++ )
		{
			ParallelFeedback parallel = feedbacks.FeedbackList[ i ] as ParallelFeedback;
			if ( parallel != null )
				return parallel;
		}

		ParallelFeedback created = new ParallelFeedback();
		created.Feedbacks = new List<Feedback>();
		feedbacks.AddFeedback( created );
		return created;
	}

	static bool ContainsSfx( List<Feedback> list )
	{
		if ( list == null )
			return false;
		for ( int i = 0; i < list.Count; i++ )
		{
			if ( list[ i ] is PlaySFXFeedback )
				return true;
		}
		return false;
	}

	static bool ContainsTitleColorPunch( List<Feedback> list, Text title )
	{
		if ( list == null )
			return false;
		for ( int i = 0; i < list.Count; i++ )
		{
			UiGraphicColorPunchFeedback punch = list[ i ] as UiGraphicColorPunchFeedback;
			if ( punch != null && punch.Target == title )
				return true;
		}
		return false;
	}

	static bool ContainsAttentionFadeIn( List<Feedback> list, CanvasGroup group )
	{
		if ( list == null )
			return false;
		for ( int i = 0; i < list.Count; i++ )
		{
			CanvasGroupFadeFeedback fade = list[ i ] as CanvasGroupFadeFeedback;
			if ( fade != null && fade.Target == group && fade.To > 0.5f )
				return true;
		}
		return false;
	}

	static bool ContainsObjectivePunch( List<Feedback> list, Transform objective )
	{
		return ContainsPunchOn( list, objective );
	}

	static bool ContainsPunchOn( List<Feedback> list, Transform target )
	{
		if ( list == null || target == null )
			return false;
		for ( int i = 0; i < list.Count; i++ )
		{
			UiPunchScaleFeedback punch = list[ i ] as UiPunchScaleFeedback;
			if ( punch != null && punch.Target == target )
				return true;
		}
		return false;
	}

	static UiSpinRotationFeedback FindSpin( List<Feedback> list, Transform diamond )
	{
		if ( list == null || diamond == null )
			return null;
		for ( int i = 0; i < list.Count; i++ )
		{
			UiSpinRotationFeedback spin = list[ i ] as UiSpinRotationFeedback;
			if ( spin != null && spin.Target == diamond )
				return spin;
		}
		return null;
	}

	static UiPulseScaleFeedback FindPulse( List<Feedback> list, Transform glow )
	{
		if ( list == null || glow == null )
			return null;
		for ( int i = 0; i < list.Count; i++ )
		{
			UiPulseScaleFeedback pulse = list[ i ] as UiPulseScaleFeedback;
			if ( pulse != null && pulse.Target == glow )
				return pulse;
		}
		return null;
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
