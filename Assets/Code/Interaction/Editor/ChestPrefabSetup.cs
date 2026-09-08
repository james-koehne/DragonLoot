#if UNITY_EDITOR
using FeedbackSystem;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires <see cref="ChestInteractable"/> + OnOpenFeedbacks (lid rotate) on chest visual prefabs.
/// </summary>
public static class ChestPrefabSetup
{
	const string IronPath = "Assets/Addressables/Treasure/Chests/IronChest.prefab";
	const string GoldPath = "Assets/Addressables/Treasure/Chests/GoldChest.prefab";
	const string BarrelPath = "Assets/Addressables/Treasure/Artifacts/Barrel_01Visual.prefab";
	const string Crate1Path = "Assets/Addressables/Treasure/Artifacts/Crate_01Visual.prefab";
	const string Crate2Path = "Assets/Addressables/Treasure/Artifacts/Crate_02Visual.prefab";
	const string IronDefPath = "Assets/Definitions/Treasure/Chests/ChestDefs/IronChestDefinition.asset";
	const string GoldDefPath = "Assets/Definitions/Treasure/Chests/ChestDefs/GoldChestDefinition.asset";
	const string BarrelDefPath = "Assets/Definitions/Treasure/Chests/ChestDefs/BarrelChestDefinition.asset";
	const string CrateDefPath = "Assets/Definitions/Treasure/Chests/ChestDefs/CrateChestDefinition.asset";
	const string OpenFeedbackChildName = "OnOpenFeedbacks";

	[MenuItem( DragonLootMenus.Root + "/Treasure/Setup Chest Prefab Feedbacks" )]
	public static void MenuSetupChestPrefabs()
	{
		int count = 0;
		if ( SetupPrefab( IronPath, IronDefPath ) )
			count++;
		if ( SetupPrefab( GoldPath, GoldDefPath ) )
			count++;
		if ( SetupSmashPrefab( BarrelPath, BarrelDefPath ) )
			count++;
		if ( SetupSmashPrefab( Crate1Path, CrateDefPath ) )
			count++;
		if ( SetupSmashPrefab( Crate2Path, CrateDefPath ) )
			count++;

		AssetDatabase.SaveAssets();
		Debug.Log( "ChestPrefabSetup: configured " + count + " chest prefab(s)." );
	}

	public static bool SetupPrefab( string prefabPath, string chestDefinitionPath )
	{
		GameObject root = PrefabUtility.LoadPrefabContents( prefabPath );
		if ( root == null )
		{
			Debug.LogError( "ChestPrefabSetup: missing prefab at " + prefabPath );
			return false;
		}

		try
		{
			TreasureItem item = root.GetComponent<TreasureItem>();
			if ( item == null )
				item = root.AddComponent<TreasureItem>();

			ChestInteractable chest = root.GetComponent<ChestInteractable>();
			if ( chest == null )
				chest = root.AddComponent<ChestInteractable>();

			ChestDefinition def = AssetDatabase.LoadAssetAtPath<ChestDefinition>( chestDefinitionPath );
			if ( def != null )
			{
				SerializedObject so = new SerializedObject( chest );
				so.FindProperty( "chestDefinition" ).objectReferenceValue = def;
				so.ApplyModifiedPropertiesWithoutUndo();
			}

			Feedbacks openFeedback = EnsureOpenFeedbacks( root, out Transform lid );
			chest.EditorSetOpenFeedback( openFeedback );

			if ( lid == null )
				Debug.LogWarning( "ChestPrefabSetup: no lid candidate on '" + prefabPath + "'. Assign Rotate Transform Target in Inspector." );
			else
				Debug.Log( "ChestPrefabSetup: '" + prefabPath + "' lid -> " + lid.name );

			PrefabUtility.SaveAsPrefabAsset( root, prefabPath );
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	public static bool SetupSmashPrefab( string prefabPath, string chestDefinitionPath )
	{
		GameObject root = PrefabUtility.LoadPrefabContents( prefabPath );
		if ( root == null )
		{
			Debug.LogError( "ChestPrefabSetup: missing smash prefab at " + prefabPath );
			return false;
		}

		try
		{
			TreasureItem item = root.GetComponent<TreasureItem>();
			if ( item == null )
				item = root.AddComponent<TreasureItem>();

			ChestInteractable chest = root.GetComponent<ChestInteractable>();
			if ( chest == null )
				chest = root.AddComponent<ChestInteractable>();

			ChestDefinition def = AssetDatabase.LoadAssetAtPath<ChestDefinition>( chestDefinitionPath );
			if ( def != null )
			{
				SerializedObject so = new SerializedObject( chest );
				so.FindProperty( "chestDefinition" ).objectReferenceValue = def;
				so.ApplyModifiedPropertiesWithoutUndo();
			}

			Feedbacks openFeedback = EnsureSmashFeedbacks( root );
			chest.EditorSetOpenFeedback( openFeedback );
			Debug.Log( "ChestPrefabSetup: smash feedbacks on '" + prefabPath + "'" );

			PrefabUtility.SaveAsPrefabAsset( root, prefabPath );
			return true;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static Feedbacks EnsureOpenFeedbacks( GameObject root, out Transform lid )
	{
		lid = FindLikelyLid( root.transform );

		Transform existing = root.transform.Find( OpenFeedbackChildName );
		GameObject host = existing != null ? existing.gameObject : new GameObject( OpenFeedbackChildName );
		if ( existing == null )
			host.transform.SetParent( root.transform, false );

		Feedbacks feedbacks = host.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = host.AddComponent<Feedbacks>();

		feedbacks.FeedbackList.Clear();

		RotateTransformFeedback rotate = new RotateTransformFeedback();
		rotate.Target = lid;
		rotate.TargetEuler = new Vector3( -115f, 0f, 0f );
		rotate.Duration = 0.28f;
		rotate.WorldSpace = false;
		// Ease-out burst: quick open then settle.
		rotate.Curve = new AnimationCurve(
			new Keyframe( 0f, 0f, 0f, 3f ),
			new Keyframe( 1f, 1f, 0f, 0f ) );

		ShakeTransformFeedback shake = new ShakeTransformFeedback();
		shake.Target = root.transform;
		shake.Duration = 0.22f;
		shake.Strength = 0.035f;

		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks.Add( rotate );
		parallel.Feedbacks.Add( shake );
		feedbacks.AddFeedback( parallel );

		EditorUtility.SetDirty( feedbacks );
		return feedbacks;
	}

	static Feedbacks EnsureSmashFeedbacks( GameObject root )
	{
		Transform existing = root.transform.Find( OpenFeedbackChildName );
		GameObject host = existing != null ? existing.gameObject : new GameObject( OpenFeedbackChildName );
		if ( existing == null )
			host.transform.SetParent( root.transform, false );

		Feedbacks feedbacks = host.GetComponent<Feedbacks>();
		if ( feedbacks == null )
			feedbacks = host.AddComponent<Feedbacks>();

		feedbacks.FeedbackList.Clear();

		PunchScaleFeedback punch = new PunchScaleFeedback();
		punch.Target = root.transform;
		punch.Punch = new Vector3( 0.18f, -0.28f, 0.18f );
		punch.Duration = 0.22f;

		ShakeTransformFeedback shake = new ShakeTransformFeedback();
		shake.Target = root.transform;
		shake.Duration = 0.22f;
		shake.Strength = 0.06f;

		ParallelFeedback parallel = new ParallelFeedback();
		parallel.Feedbacks.Add( punch );
		parallel.Feedbacks.Add( shake );
		feedbacks.AddFeedback( parallel );

		EditorUtility.SetDirty( feedbacks );
		return feedbacks;
	}

	/// <summary>
	/// Prefer a child mesh whose bounds sit above the other (typical lid), else highest Y transform.
	/// </summary>
	static Transform FindLikelyLid( Transform root )
	{
		Renderer[] renderers = root.GetComponentsInChildren<Renderer>( true );
		Transform best = null;
		float bestY = float.NegativeInfinity;

		for ( int i = 0; i < renderers.Length; i++ )
		{
			Renderer renderer = renderers[ i ];
			if ( renderer == null )
				continue;

			Transform t = renderer.transform;
			if ( t == root )
				continue;

			string n = t.name;
			if ( !string.IsNullOrEmpty( n )
				&& ( n.IndexOf( "lid", System.StringComparison.OrdinalIgnoreCase ) >= 0
					|| n.IndexOf( "top", System.StringComparison.OrdinalIgnoreCase ) >= 0
					|| n.IndexOf( "cover", System.StringComparison.OrdinalIgnoreCase ) >= 0 ) )
				return t;

			float y = renderer.bounds.center.y;
			if ( y > bestY )
			{
				bestY = y;
				best = t;
			}
		}

		// Prefer chest_0113 over chest_0111 when bounds are similar (pack naming).
		if ( best != null )
		{
			Transform named = FindChildByNameContains( root, "0113" );
			if ( named != null )
				return named;
		}

		return best;
	}

	static Transform FindChildByNameContains( Transform root, string token )
	{
		Transform[] all = root.GetComponentsInChildren<Transform>( true );
		for ( int i = 0; i < all.Length; i++ )
		{
			Transform t = all[ i ];
			if ( t == null || t == root )
				continue;
			if ( t.name.IndexOf( token, System.StringComparison.OrdinalIgnoreCase ) >= 0 )
				return t;
		}

		return null;
	}
}
#endif
