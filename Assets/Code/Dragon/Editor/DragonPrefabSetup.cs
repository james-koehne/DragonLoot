#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Wires Looking / Idle_Looking on ElderDragon_Controller and DragonController on ElderDragon_Bk.
/// </summary>
public static class DragonPrefabSetup
{
	const string ControllerPath = "Assets/StylizedDragonPack/Meshes/ElderDragon_Controller.controller";
	const string PrefabPath = "Assets/StylizedDragonPack/Prefabs/URP/ElderDragon/ElderDragon_Bk.prefab";
	const string FbxPath = "Assets/StylizedDragonPack/Meshes/ElderDragon.fbx";
	const string IdleClipName = "Idle_Ground";
	const string LookingStateName = "Idle_Looking";
	const string IdleStateName = "Idle_Ground";
	const float TransitionDuration = 0.25f;
	const float DefaultAutoLookRange = 50f;

	[InitializeOnLoadMethod]
	static void AutoSetupWhenMissing()
	{
		EditorApplication.delayCall += () =>
		{
			if ( EditorApplication.isPlayingOrWillChangePlaymode )
				return;
			if ( IsPrefabConfigured() )
				return;
			SetupLookAt();
		};
	}

	[MenuItem( DragonLootMenus.DragonSetupLookAt )]
	public static void SetupFromMenu()
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
		Debug.Log( "DragonPrefabSetup: Looking / Idle_Looking wired; DragonController on ElderDragon_Bk." );
	}

	static bool IsPrefabConfigured()
	{
		GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>( PrefabPath );
		if ( prefab == null )
			return false;
		return prefab.GetComponent<DragonController>() != null;
	}

	static AnimatorController ConfigureController()
	{
		AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>( ControllerPath );
		if ( controller == null )
			return null;

		EnsureLookingParameter( controller );

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
			EnsureTransition( idleState, lookingState, looking: true );
			EnsureTransition( lookingState, idleState, looking: false );
			rootSm.defaultState = idleState;
		}

		EditorUtility.SetDirty( controller );
		return controller;
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

	static void EnsureTransition( AnimatorState from, AnimatorState to, bool looking )
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
				if ( conditions[ c ].parameter != DragonController.LookingParam )
					continue;

				bool isIf = conditions[ c ].mode == AnimatorConditionMode.If;
				bool isIfNot = conditions[ c ].mode == AnimatorConditionMode.IfNot;
				if ( ( looking && isIf ) || ( !looking && isIfNot ) )
				{
					existing.hasExitTime = false;
					existing.duration = TransitionDuration;
					existing.hasFixedDuration = true;
					return;
				}
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

			dragon.EditorAssign( animator, spine01, spine02, neck01, neck02, neck03, head, DefaultAutoLookRange );

			SerializedObject so = new SerializedObject( dragon );
			so.Update();
			so.FindProperty( "_animator" ).objectReferenceValue = animator;
			so.FindProperty( "_spine01" ).objectReferenceValue = spine01;
			so.FindProperty( "_spine02" ).objectReferenceValue = spine02;
			so.FindProperty( "_neck01" ).objectReferenceValue = neck01;
			so.FindProperty( "_neck02" ).objectReferenceValue = neck02;
			so.FindProperty( "_neck03" ).objectReferenceValue = neck03;
			so.FindProperty( "_head" ).objectReferenceValue = head;
			so.FindProperty( "_autoLookRange" ).floatValue = DefaultAutoLookRange;
			so.ApplyModifiedPropertiesWithoutUndo();

			PrefabUtility.SaveAsPrefabAsset( root, PrefabPath );
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
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
