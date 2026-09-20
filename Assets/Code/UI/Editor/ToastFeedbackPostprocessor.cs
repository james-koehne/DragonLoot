#if UNITY_EDITOR
using System.Collections.Generic;

using FeedbackSystem;

using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ensures Discovery/Unlock toast Feedbacks on the Interface prefab include Completion + Milestone
/// chains with stronger juice (punch, color flash, distinct harps).
/// </summary>
public static class ToastFeedbackPostprocessor
{
	const string InterfacePrefabPath = "Assets/Addressables/Interface.prefab";
	const string DiscoveryHarpPath = "Assets/Audio/SFX/Rewarding/MUSIC_EFFECT_Solo_Harp_Positive_04_stereo.wav";
	const string CompletionHarpPath = "Assets/Audio/SFX/Rewarding/MUSIC_EFFECT_Solo_Harp_Positive_10_stereo.wav";
	const string UnlockHarpPath = "Assets/Audio/SFX/Rewarding/MUSIC_EFFECT_Solo_Harp_Positive_14_stereo.wav";
	const string MilestoneHarpPath = "Assets/Audio/SFX/Rewarding/MUSIC_EFFECT_Solo_Harp_Positive_16_stereo.wav";
	const string VersionMarker = "ToastFeedbacksV2";

	static readonly Color GoldFlash = new Color( 1f, 0.9f, 0.45f, 1f );

	static bool _ranThisDomain;

	[InitializeOnLoadMethod]
	static void QueuePatch()
	{
		if ( _ranThisDomain )
			return;
		_ranThisDomain = true;
		EditorApplication.delayCall += EnsureFeedbacksOnPrefab;
	}

	[MenuItem( "Dragon Loot/UI/Ensure Toast Feedbacks" )]
	public static void EnsureFeedbacksOnPrefab()
	{
		GameObject root = PrefabUtility.LoadPrefabContents( InterfacePrefabPath );
		if ( root == null )
			return;

		try
		{
			Transform discovery = FindDeepChild( root.transform, "DiscoveryToast" );
			Transform unlock = FindDeepChild( root.transform, "UnlockRewardToast" );
			if ( discovery == null || unlock == null )
			{
				Debug.LogWarning( "ToastFeedbackPostprocessor: DiscoveryToast or UnlockRewardToast missing on Interface prefab." );
				return;
			}

			bool alreadyPatched = discovery.Find( VersionMarker ) != null && unlock.Find( VersionMarker ) != null;
			bool dirty = false;
			dirty |= PatchDiscovery( discovery, force: !alreadyPatched );
			dirty |= PatchUnlock( unlock, force: !alreadyPatched );

			if ( dirty )
			{
				EnsureVersionMarker( discovery );
				EnsureVersionMarker( unlock );
				PrefabUtility.SaveAsPrefabAsset( root, InterfacePrefabPath );
				Debug.Log( "ToastFeedbackPostprocessor: Updated toast feedbacks on Interface prefab." );
			}
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static void EnsureVersionMarker( Transform parent )
	{
		if ( parent.Find( VersionMarker ) != null )
			return;
		GameObject marker = new GameObject( VersionMarker );
		marker.transform.SetParent( parent, false );
		marker.hideFlags = HideFlags.HideInHierarchy;
	}

	static bool PatchDiscovery( Transform discovery, bool force )
	{
		DiscoveryToastUI ui = discovery.GetComponent<DiscoveryToastUI>();
		if ( ui == null )
			return false;

		EnsureAccent( discovery, "Accent" );
		Image pouchIcon = FindChildImage( discovery, "PouchIcon" );

		Feedbacks show = EnsureChildFeedbacks( discovery, "ShowFeedbacks" );
		Feedbacks hide = EnsureChildFeedbacks( discovery, "HideFeedbacks" );
		Feedbacks completionShow = EnsureChildFeedbacks( discovery, "CompletionShowFeedbacks" );
		Feedbacks completionHide = EnsureChildFeedbacks( discovery, "CompletionHideFeedbacks" );

		bool dirty = false;
		if ( force || IsEmpty( show ) )
			dirty |= ConfigureShowChain( show, discovery, pouchIcon, DiscoveryHarpPath, punch: 0.16f, slideY: 28f, slideDur: 0.28f, volume: 0.5f, includeIconPunch: false, includeRotation: false );
		if ( force || IsEmpty( hide ) )
			dirty |= ConfigureHideChain( hide, discovery, punch: -0.04f, fadeDur: 0.22f );
		if ( force || IsEmpty( completionShow ) )
			dirty |= ConfigureShowChain( completionShow, discovery, pouchIcon, CompletionHarpPath, punch: 0.22f, slideY: 36f, slideDur: 0.34f, volume: 0.62f, includeIconPunch: true, includeRotation: false );
		if ( force || IsEmpty( completionHide ) )
			dirty |= ConfigureHideChain( completionHide, discovery, punch: -0.05f, fadeDur: 0.26f );

		SerializedObject so = new SerializedObject( ui );
		dirty |= Assign( so, "showFeedback", show );
		dirty |= Assign( so, "hideFeedback", hide );
		dirty |= Assign( so, "completionShowFeedback", completionShow );
		dirty |= Assign( so, "completionHideFeedback", completionHide );
		if ( dirty )
			so.ApplyModifiedPropertiesWithoutUndo();

		return dirty;
	}

	static bool PatchUnlock( Transform unlock, bool force )
	{
		UnlockRewardToastUI ui = unlock.GetComponent<UnlockRewardToastUI>();
		if ( ui == null )
			return false;

		EnsureAccent( unlock, "Accent" );
		Image rewardIcon = FindChildImage( unlock, "RewardIcon" );
		Image backdrop = unlock.GetComponent<Image>();

		Feedbacks show = EnsureChildFeedbacks( unlock, "ShowFeedbacks" );
		Feedbacks hide = EnsureChildFeedbacks( unlock, "HideFeedbacks" );
		Feedbacks milestoneShow = EnsureChildFeedbacks( unlock, "MilestoneShowFeedbacks" );
		Feedbacks milestoneHide = EnsureChildFeedbacks( unlock, "MilestoneHideFeedbacks" );

		bool dirty = false;
		if ( force || IsEmpty( show ) )
			dirty |= ConfigureShowChain( show, unlock, rewardIcon, UnlockHarpPath, punch: 0.28f, slideY: 32f, slideDur: 0.32f, volume: 0.6f, includeIconPunch: true, includeRotation: true, colorTarget: backdrop );
		if ( force || IsEmpty( hide ) )
			dirty |= ConfigureHideChain( hide, unlock, punch: -0.05f, fadeDur: 0.24f );
		if ( force || IsEmpty( milestoneShow ) )
			dirty |= ConfigureShowChain( milestoneShow, unlock, rewardIcon, MilestoneHarpPath, punch: 0.36f, slideY: 42f, slideDur: 0.4f, volume: 0.7f, includeIconPunch: true, includeRotation: true, colorTarget: backdrop, rotationZ: 5f );
		if ( force || IsEmpty( milestoneHide ) )
			dirty |= ConfigureHideChain( milestoneHide, unlock, punch: -0.06f, fadeDur: 0.3f );

		SerializedObject so = new SerializedObject( ui );
		dirty |= Assign( so, "showFeedback", show );
		dirty |= Assign( so, "hideFeedback", hide );
		dirty |= Assign( so, "milestoneShowFeedback", milestoneShow );
		dirty |= Assign( so, "milestoneHideFeedback", milestoneHide );
		if ( dirty )
			so.ApplyModifiedPropertiesWithoutUndo();

		return dirty;
	}

	static bool IsEmpty( Feedbacks feedbacks )
	{
		return feedbacks == null || feedbacks.FeedbackList == null || feedbacks.FeedbackList.Count == 0;
	}

	static bool ConfigureShowChain(
		Feedbacks feedbacks,
		Transform toastRoot,
		Image icon,
		string clipPath,
		float punch,
		float slideY,
		float slideDur,
		float volume,
		bool includeIconPunch,
		bool includeRotation,
		Image colorTarget = null,
		float rotationZ = 3.5f )
	{
		if ( feedbacks == null )
			return false;

		feedbacks.UseUnscaledTime = true;
		feedbacks.FeedbackList.Clear();

		CanvasGroup group = toastRoot.GetComponent<CanvasGroup>();
		RectTransform rect = toastRoot as RectTransform;

		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks = new List<Feedback>();

		parallel.Feedbacks.Add( new CanvasGroupFadeFeedback
		{
			Target = group,
			From = 0f,
			To = 1f,
			Duration = Mathf.Min( 0.22f, slideDur * 0.75f ),
			Curve = AnimationCurve.Linear( 0f, 0f, 1f, 1f ),
			UseUnscaledTime = true,
			CaptureCurrentAsFrom = false
		} );

		parallel.Feedbacks.Add( new UiAnchoredSlideFeedback
		{
			Target = rect,
			FromOffset = new Vector2( 0f, slideY ),
			ToOffset = Vector2.zero,
			Duration = slideDur,
			Curve = OvershootCurve(),
			UseUnscaledTime = true
		} );

		parallel.Feedbacks.Add( new UiPunchScaleFeedback
		{
			Target = rect,
			Punch = new Vector3( punch, punch, 0f ),
			Duration = Mathf.Max( 0.3f, slideDur ),
			Curve = PunchCurve(),
			UseUnscaledTime = true
		} );

		if ( includeRotation )
		{
			parallel.Feedbacks.Add( new UiPunchRotationFeedback
			{
				Target = rect,
				PunchEuler = new Vector3( 0f, 0f, rotationZ ),
				Duration = 0.28f,
				Curve = PunchCurve(),
				UseUnscaledTime = true
			} );
		}

		Image accent = FindChildImage( toastRoot, "Accent" );
		Graphic flashTarget = colorTarget != null ? (Graphic)colorTarget : accent;
		if ( flashTarget != null )
		{
			parallel.Feedbacks.Add( new UiGraphicColorPunchFeedback
			{
				Target = flashTarget,
				PunchColor = GoldFlash,
				Duration = 0.32f,
				Curve = PunchCurve(),
				UseUnscaledTime = true,
				CaptureRestOnPlay = true
			} );
		}

		if ( includeIconPunch && icon != null )
		{
			parallel.Feedbacks.Add( new UiPunchScaleFeedback
			{
				Target = icon.transform,
				Punch = new Vector3( 0.2f, 0.2f, 0f ),
				Duration = 0.3f,
				Curve = PunchCurve(),
				UseUnscaledTime = true
			} );
		}

		AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>( clipPath );
		if ( clip != null )
		{
			parallel.Feedbacks.Add( new PlaySFXFeedback
			{
				Clip = clip,
				VolumeMin = volume,
				VolumeMax = volume,
				PitchMin = 0.95f,
				PitchMax = 1.05f,
				SpatialBlend = 0f
			} );
		}

		feedbacks.AddFeedback( parallel );
		EditorUtility.SetDirty( feedbacks );
		return true;
	}

	static bool ConfigureHideChain( Feedbacks feedbacks, Transform toastRoot, float punch, float fadeDur )
	{
		if ( feedbacks == null )
			return false;

		feedbacks.UseUnscaledTime = true;
		feedbacks.FeedbackList.Clear();

		CanvasGroup group = toastRoot.GetComponent<CanvasGroup>();
		RectTransform rect = toastRoot as RectTransform;

		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks = new List<Feedback>();

		parallel.Feedbacks.Add( new CanvasGroupFadeFeedback
		{
			Target = group,
			From = 1f,
			To = 0f,
			Duration = fadeDur,
			Curve = AnimationCurve.Linear( 0f, 0f, 1f, 1f ),
			UseUnscaledTime = true,
			CaptureCurrentAsFrom = true
		} );

		parallel.Feedbacks.Add( new UiPunchScaleFeedback
		{
			Target = rect,
			Punch = new Vector3( punch, punch, 0f ),
			Duration = fadeDur,
			Curve = PunchCurve(),
			UseUnscaledTime = true
		} );

		feedbacks.AddFeedback( parallel );
		EditorUtility.SetDirty( feedbacks );
		return true;
	}

	static Feedbacks EnsureChildFeedbacks( Transform parent, string childName )
	{
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Feedbacks feedbacks = go.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = go.AddComponent<Feedbacks>();
		return feedbacks;
	}

	static Image EnsureAccent( Transform parent, string childName )
	{
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Image image = go.GetComponent<Image>();
		if ( image == null )
			image = go.AddComponent<Image>();

		RectTransform rect = go.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2( 0f, 0f );
		rect.anchorMax = new Vector2( 0f, 1f );
		rect.pivot = new Vector2( 0f, 0.5f );
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2( 6f, 0f );

		image.color = new Color( 0.95f, 0.78f, 0.28f, 1f );
		image.raycastTarget = false;
		go.SetActive( false );
		return image;
	}

	static Image FindChildImage( Transform parent, string childName )
	{
		Transform existing = parent.Find( childName );
		if ( existing == null )
			return null;
		return existing.GetComponent<Image>();
	}

	static bool Assign( SerializedObject so, string propertyName, Object value )
	{
		SerializedProperty prop = so.FindProperty( propertyName );
		if ( prop == null )
			return false;
		if ( prop.objectReferenceValue == value )
			return false;
		prop.objectReferenceValue = value;
		return true;
	}

	static AnimationCurve OvershootCurve()
	{
		return new AnimationCurve(
			new Keyframe( 0f, 0f, 0f, 2.4f ),
			new Keyframe( 0.7f, 1.05f, 0f, 0f ),
			new Keyframe( 1f, 1f, 0f, 0f ) );
	}

	static AnimationCurve PunchCurve()
	{
		return new AnimationCurve(
			new Keyframe( 0f, 0f ),
			new Keyframe( 0.35f, 1f ),
			new Keyframe( 1f, 0f ) );
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
