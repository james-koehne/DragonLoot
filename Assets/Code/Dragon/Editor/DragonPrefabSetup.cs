#if UNITY_EDITOR
using FeedbackSystem;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Wires Looking / Idle_Looking and Casting / Combat_Ground_Spell_* on ElderDragon_Controller,
/// and DragonController on ElderDragon_Bk.
/// </summary>
public static class DragonPrefabSetup
{
	const string ControllerPath = "Assets/StylizedDragonPack/Meshes/ElderDragon_Controller.controller";
	const string PrefabPath = "Assets/Prefabs/Dragon/ElderDragon_Bk.prefab";
	const string FireBreathRootName = "FireBreathRoot";
	const string FireBreathVfxPrefabPath = "Assets/Prefabs/VFX/DragonFireBreath.prefab";
	const string FbxPath = "Assets/StylizedDragonPack/Meshes/ElderDragon.fbx";
	const string IdleClipName = "Idle_Ground";
	const string LookingStateName = "Idle_Looking";
	const string IdleStateName = "Idle_Ground";
	const string SpellStartStateName = "Combat_Ground_Spell_Start";
	const string SpellLoopStateName = "Combat_Ground_Spell_Loop";
	const string SpellEndStateName = "Combat_Ground_Spell_End";
	const string SpellStartClipName = "Combat_Ground_Spell_Start";
	const string SpellLoopClipName = "Combat_Ground_Spell_Loop";
	const string SpellEndClipName = "Combat_Ground_Spell_End";
	const float TransitionDuration = 0.25f;
	const float SpellTransitionDuration = 0.15f;
	const float DefaultLookTriggerRadius = 8f;

	[InitializeOnLoadMethod]
	static void AutoSetupWhenMissing()
	{
		EditorApplication.delayCall += () =>
		{
			if ( EditorApplication.isPlayingOrWillChangePlaymode )
				return;
			if ( IsPrefabConfigured() && IsSpellConfigured() )
				return;
			SetupLookAt();
		};
	}

	[MenuItem( DragonLootMenus.DragonSetupLookAt )]
	public static void SetupFromMenu()
	{
		SetupLookAt();
	}

	[MenuItem( DragonLootMenus.DragonSetupSpellCast )]
	public static void SetupSpellFromMenu()
	{
		SetupLookAt();
	}

	public static void SetupLookAt()
	{
		AnimatorController controller = ConfigureController();
		if ( controller == null )
		{
			Debug.LogError( "DragonPrefabSetup: failed to configure animator controller at " + ControllerPath );
			return;
		}

		if ( !ConfigurePrefab() )
		{
			Debug.LogError( "DragonPrefabSetup: failed to configure prefab at " + PrefabPath );
			return;
		}

		AssetDatabase.SaveAssets();
		Debug.Log( "DragonPrefabSetup: Looking / Casting spell states wired; DragonController on ElderDragon_Bk." );
	}

	static bool IsPrefabConfigured()
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		if ( prefab == null )
			return false;
		return prefab.GetComponent<DragonController>() != null;
	}

	static bool IsSpellConfigured()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>( ControllerPath );
		if ( controller == null )
			return false;

		bool hasCasting = false;
		AnimatorControllerParameter[] parameters = controller.parameters;
		for ( int i = 0; i < parameters.Length; i++ )
		{
			if ( parameters[ i ].name == DragonController.CastingParam
				&& parameters[ i ].type == AnimatorControllerParameterType.Bool )
			{
				hasCasting = true;
				break;
			}
		}

		if ( !hasCasting )
			return false;

		AnimatorStateMachine rootSm = controller.layers[ 0 ].stateMachine;
		return FindState( rootSm, SpellStartStateName ) != null
			&& FindState( rootSm, SpellLoopStateName ) != null
			&& FindState( rootSm, SpellEndStateName ) != null;
	}

	static AnimatorController ConfigureController()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>( ControllerPath );
		if ( controller == null )
			return null;

		EnsureLookingParameter( controller );
		EnsureCastingParameter( controller );

		AnimationClip idleClip = FindClip( IdleClipName );
		if ( idleClip == null )
		{
			Debug.LogError( "DragonPrefabSetup: missing clip '" + IdleClipName + "' in " + FbxPath );
			return controller;
		}

		AnimatorStateMachine rootSm = controller.layers[ 0 ].stateMachine;
		AnimatorState idleState = FindState( rootSm, IdleStateName );
		AnimatorState lookingState = FindState( rootSm, LookingStateName );
		if ( lookingState == null )
			lookingState = rootSm.AddState( LookingStateName );

		lookingState.motion = idleClip;

		if ( idleState != null )
		{
			EnsureLookingTransition( idleState, lookingState, looking: true );
			EnsureLookingTransition( lookingState, idleState, looking: false );
			rootSm.defaultState = idleState;
		}

		ConfigureSpellStates( rootSm, idleState, lookingState );

		EditorUtility.SetDirty( controller );
		return controller;
	}

	static void ConfigureSpellStates( AnimatorStateMachine rootSm, AnimatorState idleState, AnimatorState lookingState )
	{
		AnimationClip startClip = FindClip( SpellStartClipName );
		AnimationClip loopClip = FindClip( SpellLoopClipName );
		AnimationClip endClip = FindClip( SpellEndClipName );
		if ( startClip == null || loopClip == null || endClip == null )
		{
			Debug.LogError( "DragonPrefabSetup: missing Combat_Ground_Spell_* clips in " + FbxPath );
			return;
		}

		AnimatorState startState = FindState( rootSm, SpellStartStateName );
		if ( startState == null )
			startState = rootSm.AddState( SpellStartStateName );
		startState.motion = startClip;

		AnimatorState loopState = FindState( rootSm, SpellLoopStateName );
		if ( loopState == null )
			loopState = rootSm.AddState( SpellLoopStateName );
		loopState.motion = loopClip;

		AnimatorState endState = FindState( rootSm, SpellEndStateName );
		if ( endState == null )
			endState = rootSm.AddState( SpellEndStateName );
		endState.motion = endClip;

		if ( idleState != null )
			EnsureCastingTransition( idleState, startState, casting: true, hasExitTime: false );
		if ( lookingState != null )
			EnsureCastingTransition( lookingState, startState, casting: true, hasExitTime: false );

		EnsureExitTimeTransition( startState, loopState, SpellTransitionDuration );
		EnsureCastingTransition( loopState, endState, casting: false, hasExitTime: false );
		if ( idleState != null )
			EnsureExitTimeTransition( endState, idleState, SpellTransitionDuration );
	}

	static void EnsureLookingParameter( AnimatorController controller )
	{
		AnimatorControllerParameter[] parameters = controller.parameters;
		for ( int i = 0; i < parameters.Length; i++ )
		{
			if ( parameters[ i ].name == DragonController.LookingParam
				&& parameters[ i ].type == AnimatorControllerParameterType.Bool )
				return;
		}

		controller.AddParameter( DragonController.LookingParam, AnimatorControllerParameterType.Bool );
	}

	static void EnsureCastingParameter( AnimatorController controller )
	{
		AnimatorControllerParameter[] parameters = controller.parameters;
		for ( int i = 0; i < parameters.Length; i++ )
		{
			if ( parameters[ i ].name == DragonController.CastingParam
				&& parameters[ i ].type == AnimatorControllerParameterType.Bool )
				return;
		}

		controller.AddParameter( DragonController.CastingParam, AnimatorControllerParameterType.Bool );
	}

	static void EnsureLookingTransition( AnimatorState from, AnimatorState to, bool looking )
	{
		AnimatorStateTransition[] transitions = from.transitions;
		for ( int i = 0; i < transitions.Length; i++ )
		{
			AnimatorStateTransition existing = transitions[ i ];
			if ( existing.destinationState != to )
				continue;

			AnimatorCondition[] conditions = existing.conditions;
			bool hasLooking = false;
			bool hasNotCasting = false;
			for ( int c = 0; c < conditions.Length; c++ )
			{
				if ( conditions[ c ].parameter == DragonController.LookingParam )
				{
					bool isIf = conditions[ c ].mode == AnimatorConditionMode.If;
					bool isIfNot = conditions[ c ].mode == AnimatorConditionMode.IfNot;
					if ( ( looking && isIf ) || ( !looking && isIfNot ) )
						hasLooking = true;
				}

				if ( conditions[ c ].parameter == DragonController.CastingParam
					&& conditions[ c ].mode == AnimatorConditionMode.IfNot )
					hasNotCasting = true;
			}

			if ( hasLooking )
			{
				existing.hasExitTime = false;
				existing.duration = TransitionDuration;
				existing.hasFixedDuration = true;
				if ( !hasNotCasting )
					existing.AddCondition( AnimatorConditionMode.IfNot, 0f, DragonController.CastingParam );
				return;
			}
		}

		AnimatorStateTransition transition = from.AddTransition( to );
		transition.hasExitTime = false;
		transition.duration = TransitionDuration;
		transition.hasFixedDuration = true;
		transition.AddCondition(
			looking ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
			0f,
			DragonController.LookingParam );
		transition.AddCondition( AnimatorConditionMode.IfNot, 0f, DragonController.CastingParam );
	}

	static void EnsureCastingTransition( AnimatorState from, AnimatorState to, bool casting, bool hasExitTime )
	{
		AnimatorStateTransition[] transitions = from.transitions;
		for ( int i = 0; i < transitions.Length; i++ )
		{
			AnimatorStateTransition existing = transitions[ i ];
			if ( existing.destinationState != to )
				continue;

			AnimatorCondition[] conditions = existing.conditions;
			for ( int c = 0; c < conditions.Length; c++ )
			{
				if ( conditions[ c ].parameter != DragonController.CastingParam )
					continue;

				bool isIf = conditions[ c ].mode == AnimatorConditionMode.If;
				bool isIfNot = conditions[ c ].mode == AnimatorConditionMode.IfNot;
				if ( ( casting && isIf ) || ( !casting && isIfNot ) )
				{
					existing.hasExitTime = hasExitTime;
					existing.duration = SpellTransitionDuration;
					existing.hasFixedDuration = true;
					return;
				}
			}
		}

		AnimatorStateTransition transition = from.AddTransition( to );
		transition.hasExitTime = hasExitTime;
		transition.duration = SpellTransitionDuration;
		transition.hasFixedDuration = true;
		transition.AddCondition(
			casting ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
			0f,
			DragonController.CastingParam );
	}

	static void EnsureExitTimeTransition( AnimatorState from, AnimatorState to, float duration )
	{
		AnimatorStateTransition[] transitions = from.transitions;
		for ( int i = 0; i < transitions.Length; i++ )
		{
			AnimatorStateTransition existing = transitions[ i ];
			if ( existing.destinationState != to )
				continue;

			existing.hasExitTime = true;
			existing.exitTime = 0.95f;
			existing.duration = duration;
			existing.hasFixedDuration = true;
			return;
		}

		AnimatorStateTransition transition = from.AddTransition( to );
		transition.hasExitTime = true;
		transition.exitTime = 0.95f;
		transition.duration = duration;
		transition.hasFixedDuration = true;
	}

	static AnimatorState FindState( AnimatorStateMachine sm, string name )
	{
		ChildAnimatorState[] children = sm.states;
		for ( int i = 0; i < children.Length; i++ )
		{
			AnimatorState state = children[ i ].state;
			if ( state != null && state.name == name )
				return state;
		}

		return null;
	}

	static AnimationClip FindClip( string clipName )
	{
		Object[] assets = AssetDatabase.LoadAllAssetsAtPath( FbxPath );
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

		return null;
	}

	static bool ConfigurePrefab()
	{
		GameObject root = PrefabUtility.LoadPrefabContents( PrefabPath );
		if ( root == null )
			return false;

		try
		{
			DragonController dragon = root.GetComponent<DragonController>();
			if ( dragon == null )
				dragon = root.AddComponent<DragonController>();

			Animator animator = root.GetComponent<Animator>();
			Transform spine01 = FindDeep( root.transform, "spine_01" );
			Transform spine02 = FindDeep( root.transform, "spine_02" );
			Transform neck01 = FindDeep( root.transform, "neck_01" );
			Transform neck02 = FindDeep( root.transform, "neck_02" );
			Transform neck03 = FindDeep( root.transform, "neck_03" );
			Transform head = FindDeep( root.transform, "head" );
			Transform fireBreathRoot = EnsureFireBreathRoot( head );
			DragonFireBreathVFX fireBreath = EnsureFireBreathVfx( fireBreathRoot );

			dragon.EditorAssign( animator, spine01, spine02, neck01, neck02, neck03, head );
			dragon.EditorAssignFireBreath( fireBreathRoot, fireBreath );

			SerializedObject so = new SerializedObject( dragon );
			so.Update();
			so.FindProperty( "_animator" ).objectReferenceValue = animator;
			so.FindProperty( "_spine01" ).objectReferenceValue = spine01;
			so.FindProperty( "_spine02" ).objectReferenceValue = spine02;
			so.FindProperty( "_neck01" ).objectReferenceValue = neck01;
			so.FindProperty( "_neck02" ).objectReferenceValue = neck02;
			so.FindProperty( "_neck03" ).objectReferenceValue = neck03;
			so.FindProperty( "_head" ).objectReferenceValue = head;
			SerializedProperty fireBreathRootProp = so.FindProperty( "_fireBreathRoot" );
			if ( fireBreathRootProp != null )
				fireBreathRootProp.objectReferenceValue = fireBreathRoot;
			SerializedProperty fireBreathProp = so.FindProperty( "_fireBreath" );
			if ( fireBreathProp != null )
				fireBreathProp.objectReferenceValue = fireBreath;
			so.FindProperty( "_playerEyeHeight" ).floatValue = 1.6f;
			so.FindProperty( "_aimSmoothTime" ).floatValue = 0.35f;
			so.FindProperty( "_maxHeadDegreesPerSecond" ).floatValue = 180f;
			so.FindProperty( "_lookingAnimSpeed" ).floatValue = 0.2f;
			so.FindProperty( "_headLookOriginOffset" ).floatValue = 0.25f;
			so.FindProperty( "_headWeight" ).floatValue = 1f;
			so.FindProperty( "_headMaxYaw" ).floatValue = 80f;
			so.FindProperty( "_headMaxPitch" ).floatValue = 45f;
			so.FindProperty( "_arcPower" ).floatValue = 1.25f;
			so.FindProperty( "_arcStrength" ).floatValue = 0.65f;
			WriteFloatArray( so, "_supportWeights", new float[] { 0.55f, 0.7f, 0.85f, 1f, 1f } );
			WriteFloatArray( so, "_supportMaxYaw", new float[] { 28f, 32f, 40f, 48f, 55f } );
			WriteFloatArray( so, "_supportMaxPitch", new float[] { 16f, 20f, 26f, 30f, 34f } );
			EnsureDefaultLookTrigger( root, dragon );
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static void WriteFloatArray( SerializedObject so, string propertyName, float[] values )
	{
		SerializedProperty prop = so.FindProperty( propertyName );
		if ( prop == null || !prop.isArray )
			return;

		prop.arraySize = values.Length;
		for ( int i = 0; i < values.Length; i++ )
			prop.GetArrayElementAtIndex( i ).floatValue = values[ i ];
	}

	static void EnsureDefaultLookTrigger( GameObject root, DragonController dragon )
	{
		if ( root == null )
			return;

		DragonLookTrigger[] existing = root.GetComponentsInChildren<DragonLookTrigger>( true );
		if ( existing != null && existing.Length > 0 )
		{
			for ( int i = 0; i < existing.Length; i++ )
			{
				if ( existing[ i ] != null )
					existing[ i ].EditorAssign( dragon );
			}
			return;
		}

		Transform group = root.transform.Find( "LookTriggers" );
		if ( group == null )
		{
			GameObject groupGo = new GameObject( "LookTriggers" );
			group = groupGo.transform;
			group.SetParent( root.transform, false );
			group.localPosition = Vector3.zero;
			group.localRotation = Quaternion.identity;
			group.localScale = Vector3.one;
		}

		GameObject sphereGo = new GameObject( "LookTrigger_Near" );
		sphereGo.transform.SetParent( group, false );
		sphereGo.transform.localPosition = Vector3.zero;
		sphereGo.transform.localRotation = Quaternion.identity;
		sphereGo.transform.localScale = Vector3.one;

		SphereCollider sphere = sphereGo.AddComponent<SphereCollider>();
		sphere.isTrigger = true;
		sphere.radius = DefaultLookTriggerRadius;
		sphere.center = Vector3.zero;

		DragonLookTrigger trigger = sphereGo.AddComponent<DragonLookTrigger>();
		trigger.EditorAssign( dragon );
	}

	static Transform EnsureFireBreathRoot( Transform head )
	{
		if ( head == null )
			return null;

		Transform existing = FindDeep( head, FireBreathRootName );
		if ( existing != null )
			return existing;

		GameObject rootGo = new GameObject( FireBreathRootName );
		Transform root = rootGo.transform;
		root.SetParent( head, false );
		root.localPosition = new Vector3( 0f, -0.05f, 0.4f );
		root.localRotation = Quaternion.identity;
		root.localScale = Vector3.one;
		return root;
	}

	static DragonFireBreathVFX EnsureFireBreathVfx( Transform fireBreathRoot )
	{
		if ( fireBreathRoot == null )
			return null;

		DragonFireBreathVFX existing = fireBreathRoot.GetComponentInChildren<DragonFireBreathVFX>( true );
		if ( existing != null )
		{
			WireFireBreathAimOrigin( existing, fireBreathRoot );
			return existing;
		}

		GameObject vfxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>( FireBreathVfxPrefabPath );
		if ( vfxPrefab == null )
		{
			Debug.LogWarning( "DragonPrefabSetup: missing fire breath prefab at " + FireBreathVfxPrefabPath );
			return null;
		}

		GameObject instance = PrefabUtility.InstantiatePrefab( vfxPrefab, fireBreathRoot ) as GameObject;
		if ( instance == null )
			return null;

		instance.name = "DragonFireBreath";
		instance.SetActive( false );
		instance.transform.localPosition = Vector3.zero;
		instance.transform.localRotation = Quaternion.identity;
		instance.transform.localScale = Vector3.one;

		DragonFireBreathVFX vfx = instance.GetComponent<DragonFireBreathVFX>();
		WireFireBreathAimOrigin( vfx, fireBreathRoot );
		return vfx;
	}

	static void WireFireBreathAimOrigin( DragonFireBreathVFX vfx, Transform fireBreathRoot )
	{
		if ( vfx == null || fireBreathRoot == null )
			return;

		Feedbacks breathStart = null;
		Transform feedbackTf = fireBreathRoot.Find( "Feedbacks_BreathStart" );
		if ( feedbackTf != null )
			breathStart = feedbackTf.GetComponent<Feedbacks>();
		if ( breathStart == null )
			breathStart = fireBreathRoot.GetComponentInChildren<Feedbacks>( true );

		SerializedObject so = new SerializedObject( vfx );
		so.Update();
		SerializedProperty aimOrigin = so.FindProperty( "_aimOrigin" );
		if ( aimOrigin != null )
			aimOrigin.objectReferenceValue = fireBreathRoot;
		SerializedProperty onBreathStart = so.FindProperty( "_onBreathStart" );
		if ( onBreathStart != null && breathStart != null )
			onBreathStart.objectReferenceValue = breathStart;
		so.ApplyModifiedPropertiesWithoutUndo();
	}

	static Transform FindDeep( Transform root, string name )
	{
		if ( root == null )
			return null;
		if ( root.name == name )
			return root;

		for ( int i = 0; i < root.childCount; i++ )
		{
			Transform found = FindDeep( root.GetChild( i ), name );
			if ( found != null )
				return found;
		}

		return null;
	}
}
#endif
