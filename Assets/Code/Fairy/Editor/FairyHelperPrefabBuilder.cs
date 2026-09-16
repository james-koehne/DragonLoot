#if UNITY_EDITOR
using System.IO;

using FeedbackSystem;

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds / patches the Addressable FairyHelper prefab juice children + gameplay animator.
/// Does not wipe an existing Fairy model on the prefab.
/// </summary>
public static class FairyHelperPrefabBuilder
{
	const string CompanionsFolder = "Assets/Addressables/Companions";
	const string PrefabPath = CompanionsFolder + "/FairyHelper.prefab";
	const string ControllerPath = CompanionsFolder + "/FairyHelper.controller";
	const string FairyFbxPath = "Assets/ThirdParty/Fairy/Model/Fairy.fbx";
	const string Address = "Companions/FairyHelper";

	[MenuItem( DragonLootMenus.CompanionsBuildFairyHelper )]
	public static void BuildFromMenu()
	{
		Build( force: true );
	}

	/// <summary>Public entry used by asset postprocessor to patch juice onto the existing model prefab.</summary>
	public static void EnsureJuiceOnPrefab()
	{
		if ( !File.Exists( PrefabPath ) )
			return;
		PatchExistingPrefabJuice();
		AddressableEditorUtil.TryRegister( PrefabPath, Address );
	}

	[InitializeOnLoadMethod]
	static void EnsurePrefabExists()
	{
		EditorApplication.delayCall += () =>
		{
			if ( !File.Exists( PrefabPath ) )
			{
				Build( force: false );
				return;
			}

			PatchExistingPrefabJuice();
			if ( !File.Exists( ControllerPath ) )
				EnsureController( force: false );
		};
	}

	public static void BuildFromCommandLine()
	{
		Build( force: true );
	}

	static void Build( bool force )
	{
		AddressableEditorUtil.EnsureFolder( "Assets/Addressables" );
		AddressableEditorUtil.EnsureFolder( CompanionsFolder );

		AnimatorController controller = EnsureController( force );
		if ( !File.Exists( PrefabPath ) )
			BuildMinimalPrefab( controller );
		else
			PatchExistingPrefabJuice( controller );

		AddressableEditorUtil.TryRegister( PrefabPath, Address );
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log( "FairyHelper prefab ready at " + PrefabPath + " (" + Address + ")" );
	}

	static void PatchExistingPrefabJuice()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>( ControllerPath );
		PatchExistingPrefabJuice( controller );
	}

	static void PatchExistingPrefabJuice( AnimatorController controller )
	{
		GameObject root = PrefabUtility.LoadPrefabContents( PrefabPath );
		if ( root == null )
			return;

		try
		{
			StripMissingScripts( root );

			FairyHelper helper = root.GetComponent<FairyHelper>();
			if ( helper == null )
				helper = root.AddComponent<FairyHelper>();

			FairyInteractable interactable = root.GetComponent<FairyInteractable>();
			if ( interactable == null )
				interactable = root.AddComponent<FairyInteractable>();

			if ( root.GetComponent<CapsuleCollider>() == null )
			{
				CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
				capsule.isTrigger = false;
				capsule.center = new Vector3( 0f, 0.35f, 0f );
				capsule.radius = 0.28f;
				capsule.height = 0.85f;
				capsule.direction = 1;
			}

			Rigidbody body = root.GetComponent<Rigidbody>();
			if ( body == null )
				body = root.AddComponent<Rigidbody>();
			body.isKinematic = true;
			body.useGravity = false;

			Animator animator = root.GetComponentInChildren<Animator>( true );
			if ( animator != null && controller != null && animator.runtimeAnimatorController == null )
				animator.runtimeAnimatorController = controller;

			Feedbacks farGlow = EnsureGlow( root.transform );
			Feedbacks onInteract = EnsureInteractFeedbacks( root.transform, animator );
			Feedbacks onLoop = EnsureLoopFeedbacks( root.transform );
			FairySpeechBubble bubble = EnsureSpeechBubble( root.transform );
			if ( bubble == null )
			{
				Debug.LogError( "FairyHelperPrefabBuilder: failed to create FairySpeechBubble on prefab." );
				return;
			}

			SerializedObject helperSo = new SerializedObject( helper );
			if ( animator != null )
				helperSo.FindProperty( "animator" ).objectReferenceValue = animator;
			helperSo.FindProperty( "speechBubble" ).objectReferenceValue = bubble;
			helperSo.FindProperty( "onInteractFeedback" ).objectReferenceValue = onInteract;
			helperSo.FindProperty( "onFarGlowFeedback" ).objectReferenceValue = farGlow;
			helperSo.FindProperty( "onLoopFeedback" ).objectReferenceValue = onLoop;
			helperSo.ApplyModifiedPropertiesWithoutUndo();

			SerializedObject interactSo = new SerializedObject( interactable );
			interactSo.FindProperty( "helper" ).objectReferenceValue = helper;
			interactSo.ApplyModifiedPropertiesWithoutUndo();

			StripMissingScripts( root );
			if ( HasMissingScripts( root ) )
			{
				Debug.LogWarning( "FairyHelperPrefabBuilder: skipping prefab save because missing scripts remain on " + PrefabPath );
				return;
			}

			PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static void StripMissingScripts( GameObject root )
	{
		Transform[] transforms = root.GetComponentsInChildren<Transform>( true );
		for ( int i = 0; i < transforms.Length; i++ )
			GameObjectUtility.RemoveMonoBehavioursWithMissingScript( transforms[ i ].gameObject );
	}

	static bool HasMissingScripts( GameObject root )
	{
		Transform[] transforms = root.GetComponentsInChildren<Transform>( true );
		for ( int i = 0; i < transforms.Length; i++ )
		{
			if ( GameObjectUtility.GetMonoBehavioursWithMissingScriptCount( transforms[ i ].gameObject ) > 0 )
				return true;
		}
		return false;
	}

	static AnimatorController EnsureController( bool force )
	{
		if ( !force )
		{
			AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>( ControllerPath );
			if ( existing != null )
				return existing;
		}

		if ( File.Exists( ControllerPath ) )
			AssetDatabase.DeleteAsset( ControllerPath );

		AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath( ControllerPath );
		controller.AddParameter( "IsMoving", AnimatorControllerParameterType.Bool );
		controller.AddParameter( "Talk", AnimatorControllerParameterType.Trigger );

		AnimationClip idleClip = FindClip( "Fairy_Idle" );
		AnimationClip flyClip = FindClip( "Fairy_Fly" );
		AnimationClip talkClip = FindClip( "Fairy_Idle_long" );

		AnimatorStateMachine rootSm = controller.layers[ 0 ].stateMachine;
		AnimatorState idle = rootSm.AddState( "Fairy_Idle" );
		AnimatorState fly = rootSm.AddState( "Fairy_Fly" );
		AnimatorState talk = rootSm.AddState( "Fairy_Idle_long" );

		idle.motion = idleClip;
		fly.motion = flyClip;
		talk.motion = talkClip;
		rootSm.defaultState = idle;

		AnimatorStateTransition idleToFly = idle.AddTransition( fly );
		idleToFly.hasExitTime = false;
		idleToFly.duration = 0.1f;
		idleToFly.AddCondition( AnimatorConditionMode.If, 0f, "IsMoving" );

		AnimatorStateTransition flyToIdle = fly.AddTransition( idle );
		flyToIdle.hasExitTime = false;
		flyToIdle.duration = 0.1f;
		flyToIdle.AddCondition( AnimatorConditionMode.IfNot, 0f, "IsMoving" );

		AnimatorStateTransition anyToTalk = rootSm.AddAnyStateTransition( talk );
		anyToTalk.hasExitTime = false;
		anyToTalk.duration = 0.05f;
		anyToTalk.canTransitionToSelf = false;
		anyToTalk.AddCondition( AnimatorConditionMode.If, 0f, "Talk" );

		AnimatorStateTransition talkToIdle = talk.AddTransition( idle );
		talkToIdle.hasExitTime = true;
		talkToIdle.exitTime = 0.9f;
		talkToIdle.duration = 0.15f;
		talkToIdle.hasFixedDuration = true;

		EditorUtility.SetDirty( controller );
		return controller;
	}

	static AnimationClip FindClip( string clipName )
	{
		Object[] assets = AssetDatabase.LoadAllAssetsAtPath( FairyFbxPath );
		if ( assets == null )
			return null;

		for ( int i = 0; i < assets.Length; i++ )
		{
			AnimationClip clip = assets[ i ] as AnimationClip;
			if ( clip == null )
				continue;
			if ( clip.name == clipName )
				return clip;
		}

		Debug.LogWarning( "FairyHelperPrefabBuilder: missing clip '" + clipName + "' in " + FairyFbxPath );
		return null;
	}

	static void BuildMinimalPrefab( AnimatorController controller )
	{
		GameObject root = new GameObject( "FairyHelper" );
		try
		{
			root.AddComponent<CapsuleCollider>();
			root.AddComponent<Rigidbody>();
			root.AddComponent<FairyHelper>();
			root.AddComponent<FairyInteractable>();

			GameObject visual = GameObject.CreatePrimitive( PrimitiveType.Cube );
			visual.name = "Visual";
			visual.transform.SetParent( root.transform, false );
			visual.transform.localPosition = new Vector3( 0f, 0.35f, 0f );
			visual.transform.localScale = new Vector3( 0.35f, 0.35f, 0.35f );
			Object.DestroyImmediate( visual.GetComponent<BoxCollider>() );

			Animator animator = visual.AddComponent<Animator>();
			animator.runtimeAnimatorController = controller;
			animator.applyRootMotion = false;

			PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
		}
		finally
		{
			Object.DestroyImmediate( root );
		}

		PatchExistingPrefabJuice( controller );
	}

	static Feedbacks EnsureGlow( Transform root )
	{
		Transform glow = root.Find( "Glow" );
		GameObject glowGo = glow != null ? glow.gameObject : new GameObject( "Glow" );
		if ( glow == null )
		{
			glowGo.transform.SetParent( root, false );
			glowGo.transform.localPosition = new Vector3( 0f, 0.35f, 0f );
		}

		ParticleSystem ps = glowGo.GetComponent<ParticleSystem>();
		if ( ps == null )
			ps = glowGo.AddComponent<ParticleSystem>();
		ConfigureGlowParticles( ps );

		Feedbacks farGlow = glowGo.GetComponent<Feedbacks>();
		if ( farGlow == null )
			farGlow = glowGo.AddComponent<Feedbacks>();

		bool hasPlayParticles = false;
		for ( int i = 0; i < farGlow.FeedbackList.Count; i++ )
		{
			PlayParticlesFeedback existing = farGlow.FeedbackList[ i ] as PlayParticlesFeedback;
			if ( existing == null )
				continue;
			hasPlayParticles = true;
			if ( existing.ParticleSystem == null )
				existing.ParticleSystem = ps;
		}

		if ( !hasPlayParticles )
		{
			PlayParticlesFeedback playParticles = new PlayParticlesFeedback();
			playParticles.ParticleSystem = ps;
			farGlow.AddFeedback( playParticles );
		}

		EditorUtility.SetDirty( farGlow );
		return farGlow;
	}

	static Feedbacks EnsureInteractFeedbacks( Transform root, Animator animator )
	{
		Transform interact = root.Find( "InteractFeedbacks" );
		GameObject interactGo = interact != null ? interact.gameObject : new GameObject( "InteractFeedbacks" );
		if ( interact == null )
			interactGo.transform.SetParent( root, false );

		Feedbacks onInteract = interactGo.GetComponent<Feedbacks>();
		if ( onInteract == null )
			onInteract = interactGo.AddComponent<Feedbacks>();

		if ( onInteract.FeedbackList.Count == 0 )
		{
			PunchScaleFeedback punch = new PunchScaleFeedback();
			punch.Target = root;
			punch.Punch = new Vector3( 0.2f, 0.2f, 0.2f );
			punch.Duration = 0.22f;

			if ( animator != null )
			{
				SetAnimatorTriggerFeedback talkTrigger = new SetAnimatorTriggerFeedback();
				talkTrigger.Animator = animator;
				talkTrigger.Trigger = "Talk";

				ParallelFeedback parallel = new ParallelFeedback();
				parallel.Feedbacks.Add( talkTrigger );
				parallel.Feedbacks.Add( punch );
				onInteract.AddFeedback( parallel );
			}
			else
			{
				onInteract.AddFeedback( punch );
			}
		}

		EditorUtility.SetDirty( onInteract );
		return onInteract;
	}

	static Feedbacks EnsureLoopFeedbacks( Transform root )
	{
		const string LoopClipPath = "Assets/Audio/SFX/Fairy/fairy_loop.mp3";
		AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>( LoopClipPath );
		if ( clip == null )
			Debug.LogWarning( "FairyHelperPrefabBuilder: missing loop clip at " + LoopClipPath );

		Transform loop = root.Find( "LoopFeedbacks" );
		GameObject loopGo = loop != null ? loop.gameObject : new GameObject( "LoopFeedbacks" );
		if ( loop == null )
		{
			loopGo.transform.SetParent( root, false );
			loopGo.transform.localPosition = Vector3.zero;
		}

		AudioSource source = loopGo.GetComponent<AudioSource>();
		if ( source == null )
			source = loopGo.AddComponent<AudioSource>();
		source.playOnAwake = false;
		source.loop = true;
		source.spatialBlend = 1f;
		source.minDistance = 1.5f;
		source.maxDistance = 18f;
		source.dopplerLevel = 0.15f;
		source.rolloffMode = AudioRolloffMode.Linear;
		if ( clip != null )
			source.clip = clip;

		Feedbacks onLoop = loopGo.GetComponent<Feedbacks>();
		if ( onLoop == null )
			onLoop = loopGo.AddComponent<Feedbacks>();

		AmbientLoopSfxFeedback ambient = null;
		for ( int i = 0; i < onLoop.FeedbackList.Count; i++ )
		{
			ambient = onLoop.FeedbackList[ i ] as AmbientLoopSfxFeedback;
			if ( ambient != null )
				break;
		}

		bool created = ambient == null;
		if ( created )
		{
			ambient = new AmbientLoopSfxFeedback();
			onLoop.AddFeedback( ambient );
			ambient.Volume = 0.55f;
			ambient.Pitch = 1f;
			ambient.FadeInSeconds = 0.35f;
			ambient.FadeOutSeconds = 0.45f;
			ambient.SpatialBlend = 1f;
			ambient.MinDistance = 1.5f;
			ambient.MaxDistance = 18f;
			ambient.DopplerLevel = 0.15f;
			ambient.RolloffMode = AudioRolloffMode.Linear;
		}

		if ( clip != null )
			ambient.Clip = clip;
		ambient.AudioSource = source;
		if ( ambient.SpatialBlend < 0.99f )
			ambient.SpatialBlend = 1f;

		EditorUtility.SetDirty( onLoop );
		EditorUtility.SetDirty( source );
		return onLoop;
	}

	static FairySpeechBubble EnsureSpeechBubble( Transform parent )
	{
		FairySpeechBubble existing = parent.GetComponentInChildren<FairySpeechBubble>( true );
		if ( existing != null )
		{
			WireSpeechBubbleRefs( existing );
			return existing;
		}

		Transform speech = parent.Find( "SpeechBubble" );
		GameObject bubbleGo;
		if ( speech != null )
		{
			bubbleGo = speech.gameObject;
			GameObjectUtility.RemoveMonoBehavioursWithMissingScript( bubbleGo );
		}
		else
		{
			bubbleGo = new GameObject( "SpeechBubble" );
			bubbleGo.transform.SetParent( parent, false );
			bubbleGo.transform.localPosition = new Vector3( 0f, 0.95f, 0f );
		}

		Canvas canvas = bubbleGo.GetComponent<Canvas>();
		if ( canvas == null )
			canvas = bubbleGo.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.WorldSpace;
		canvas.sortingOrder = 50;

		RectTransform canvasRect = bubbleGo.GetComponent<RectTransform>();
		if ( canvasRect != null )
		{
			canvasRect.sizeDelta = new Vector2( 220f, 70f );
			bubbleGo.transform.localScale = new Vector3( 0.01f, 0.01f, 0.01f );
		}

		CanvasGroup group = bubbleGo.GetComponent<CanvasGroup>();
		if ( group == null )
			group = bubbleGo.AddComponent<CanvasGroup>();
		group.alpha = 0f;
		group.blocksRaycasts = false;
		group.interactable = false;

		Image image = bubbleGo.GetComponentInChildren<Image>( true );
		if ( image == null )
		{
			GameObject bg = new GameObject( "Background" );
			bg.transform.SetParent( bubbleGo.transform, false );
			image = bg.AddComponent<Image>();
			image.color = new Color( 0.08f, 0.1f, 0.16f, 0.82f );
			RectTransform bgRect = bg.GetComponent<RectTransform>();
			bgRect.anchorMin = Vector2.zero;
			bgRect.anchorMax = Vector2.one;
			bgRect.offsetMin = Vector2.zero;
			bgRect.offsetMax = Vector2.zero;
		}

		Text text = bubbleGo.GetComponentInChildren<Text>( true );
		if ( text == null )
		{
			GameObject textGo = new GameObject( "Body" );
			textGo.transform.SetParent( bubbleGo.transform, false );
			text = textGo.AddComponent<Text>();
			Font font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
			if ( font == null )
				font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );
			text.font = font;
			text.fontSize = 28;
			text.alignment = TextAnchor.MiddleCenter;
			text.color = Color.white;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.text = string.Empty;
			RectTransform textRect = textGo.GetComponent<RectTransform>();
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = new Vector2( 10f, 8f );
			textRect.offsetMax = new Vector2( -10f, -8f );
		}

		FairySpeechBubble bubble = bubbleGo.GetComponent<FairySpeechBubble>();
		if ( bubble == null )
			bubble = bubbleGo.AddComponent<FairySpeechBubble>();
		WireSpeechBubbleRefs( bubble );
		return bubble;
	}

	static void WireSpeechBubbleRefs( FairySpeechBubble bubble )
	{
		if ( bubble == null )
			return;

		SerializedObject so = new SerializedObject( bubble );
		SerializedProperty canvasProp = so.FindProperty( "canvas" );
		SerializedProperty groupProp = so.FindProperty( "canvasGroup" );
		SerializedProperty bodyProp = so.FindProperty( "body" );

		if ( canvasProp.objectReferenceValue == null )
			canvasProp.objectReferenceValue = bubble.GetComponent<Canvas>();
		if ( groupProp.objectReferenceValue == null )
			groupProp.objectReferenceValue = bubble.GetComponent<CanvasGroup>();
		if ( bodyProp.objectReferenceValue == null )
			bodyProp.objectReferenceValue = bubble.GetComponentInChildren<Text>( true );

		so.ApplyModifiedPropertiesWithoutUndo();
	}

	static void ConfigureGlowParticles( ParticleSystem ps )
	{
		ps.Stop( true, ParticleSystemStopBehavior.StopEmittingAndClear );

		ParticleSystem.MainModule main = ps.main;
		main.loop = true;
		main.playOnAwake = false;
		main.startLifetime = 1.2f;
		main.startSize = 0.35f;
		main.startColor = new Color( 0.55f, 0.95f, 1.4f, 0.65f );
		main.maxParticles = 24;
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = 8f;

		ParticleSystem.ShapeModule shape = ps.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 0.15f;

		ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
		color.enabled = true;
		Gradient gradient = new Gradient();
		gradient.SetKeys(
			new[]
			{
				new GradientColorKey( new Color( 0.6f, 1f, 1.4f ), 0f ),
				new GradientColorKey( new Color( 0.3f, 0.7f, 1.2f ), 1f )
			},
			new[]
			{
				new GradientAlphaKey( 0f, 0f ),
				new GradientAlphaKey( 0.7f, 0.2f ),
				new GradientAlphaKey( 0f, 1f )
			} );
		color.color = gradient;

		ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
		if ( renderer != null )
		{
			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			Material mat = AssetDatabase.GetBuiltinExtraResource<Material>( "Default-Particle.mat" );
			if ( mat != null )
				renderer.sharedMaterial = mat;
		}
	}
}
#endif
