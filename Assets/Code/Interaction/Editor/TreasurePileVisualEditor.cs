#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( TreasurePileVisual ) )]
public class TreasurePileVisualEditor : Editor
{
	int _previewRevision = -1;

	void OnEnable()
	{
		Undo.undoRedoPerformed += OnUndoRedo;
		TreasurePileVisual visual = target as TreasurePileVisual;
		if ( visual != null && !Application.isPlaying )
			RebuildPreview( visual );
	}

	void OnDisable()
	{
		Undo.undoRedoPerformed -= OnUndoRedo;
	}

	void OnUndoRedo()
	{
		TreasurePileVisual visual = target as TreasurePileVisual;
		if ( visual != null && !Application.isPlaying )
			RebuildPreview( visual );
	}

	public override void OnInspectorGUI()
	{
		serializedObject.Update();
		DrawDefaultInspector();

		TreasurePileVisual visual = ( TreasurePileVisual )target;
		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Artifact Latent Bake", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Curated loot: drop an artifact/chest/key *Visual prefab so it overlaps the mound "
			+ "(or parent it under _AuthoredLoot). Assign Treasure Definition on TreasurePileAuthoredItem. "
			+ "Move with gizmos — the mesh stays visible. Then Bake Latents For This Pile. "
			+ "Bake fills definition gems/artifacts around your piece, only where Treasure surface paint is below. "
			+ "Also bakes GPU coin seats (TreasurePileCoinSeatBake) so play-mode bind skips placement probes. "
			+ "Assign TreasurePileLatentBakeSettings to change placement (e.g. near-surface), then rebake. "
			+ "Coins/gems are not curated.",
			MessageType.Info );

		int authoredCount = visual.CountAuthoredItems();
		EditorGUILayout.LabelField( "Curated authored props", authoredCount.ToString() );

		int sharedBakeUsers = CountOtherScenePilesUsingBake( visual.LatentBake, visual );
		if ( !Application.isPlaying && sharedBakeUsers > 0 )
		{
			EditorGUILayout.HelpBox(
				$"This bake asset is shared with {sharedBakeUsers} other pile{( sharedBakeUsers == 1 ? "" : "s" )}. "
				+ "Each pile needs its own bake — Bake Latents will create a unique asset for this pile.",
				MessageType.Warning );
		}

		int sharedCoinBakeUsers = CountOtherScenePilesUsingCoinSeatBake( visual.CoinSeatBake, visual );
		if ( !Application.isPlaying && sharedCoinBakeUsers > 0 )
		{
			EditorGUILayout.HelpBox(
				$"This coin-seat bake is shared with {sharedCoinBakeUsers} other pile{( sharedCoinBakeUsers == 1 ? "" : "s" )}. "
				+ "Each pile needs its own coin bake — Bake Latents will create a unique asset for this pile.",
				MessageType.Warning );
		}

		string staleReason = visual.GetLatentBakeStaleReason();
		if ( !Application.isPlaying && staleReason != null )
		{
			EditorGUILayout.HelpBox(
				$"Latent bake is stale ({staleReason}) — rebake to update fill.",
				MessageType.Warning );
		}

		string coinStaleReason = visual.GetCoinSeatBakeStaleReason();
		if ( !Application.isPlaying && coinStaleReason != null )
		{
			EditorGUILayout.HelpBox(
				$"Coin seat bake is stale ({coinStaleReason}) — rebake to update seats.",
				MessageType.Warning );
		}

		if ( visual.CoinSeatBake != null )
			EditorGUILayout.LabelField( "Coin seats baked", visual.CoinSeatBake.SeatCount.ToString() );

		if ( GUILayout.Button( "Bake Latents For This Pile" ) )
		{
			BakeLatentsForVisual( visual );
			serializedObject.Update();
		}

		if ( GUILayout.Button( "Bake Latents For All Piles In Scene" ) )
		{
			BakeLatentsForAllPilesInScene();
			serializedObject.Update();
		}

		EditorGUILayout.Space( 8f );
		EditorGUILayout.LabelField( "Pile Sculpt (Edit Mode)", EditorStyles.boldLabel );

		if ( Application.isPlaying )
		{
			EditorGUILayout.HelpBox( "Sculpt tools are available in Edit Mode only.", MessageType.Info );
			serializedObject.ApplyModifiedProperties();
			return;
		}

		EditorGUILayout.HelpBox(
			"Sculpt uses the Scene Tools Gold Pile context (does not steal Move/Rotate).\n"
			+ "Click Sculpt Pile, or open the tool-context menu in the Scene view Tools overlay.",
			MessageType.Info );

		if ( GUILayout.Button( "Sculpt Pile" ) )
			GoldPileSculptToolContext.ActivateAndRestoreBrush();

		DrawDerivedLayoutStats( visual );

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Rebuild Preview" ) )
		{
			RebuildPreview( visual );
			EditorUtility.SetDirty( visual );
		}

		if ( GUILayout.Button( "Reset To Empty" ) )
		{
			Undo.RecordObject( visual, "Reset Gold Pile Empty" );
			visual.ResetAuthoredToEmpty();
			RebuildPreview( visual );
			EditorUtility.SetDirty( visual );
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Fit Bounds" ) )
		{
			Undo.RecordObject( visual, "Fit Gold Pile Bounds" );
			visual.FitBounds();
			RebuildPreview( visual );
			EditorUtility.SetDirty( visual );
		}

		if ( GUILayout.Button( "Apply Settle Pass" ) )
		{
			Undo.RecordObject( visual, "Settle Gold Pile" );
			GoldPileSculptToolContext.Painter.ApplyOneShot(
				visual,
				GoldPileEditorBrushMode.Settle,
				GoldPileSculptSettings.SettleItersStrokeEnd );
			_previewRevision = visual.AuthoredRevision;
		}
		EditorGUILayout.EndHorizontal();

		if ( GUILayout.Button( "Clear Authored Height" ) )
		{
			Undo.RecordObject( visual, "Clear Authored Gold Pile Height" );
			visual.ClearAuthoredHeight();
			RebuildPreview( visual );
			EditorUtility.SetDirty( visual );
		}

		EditorGUILayout.LabelField(
			visual.HasAuthoredHeight
				? $"Authored: {visual.AuthoredResolutionX}×{visual.AuthoredResolutionZ}  "
					+ $"{visual.AuthoredWorldSizeX:0.###}m×{visual.AuthoredWorldSizeZ:0.###}m  rev {visual.AuthoredRevision}"
				: "Authored: (none — preview starts empty)" );

		serializedObject.ApplyModifiedProperties();
	}

	static void DrawDerivedLayoutStats( TreasurePileVisual visual )
	{
		GoldPileHeightfield hf = visual.Heightfield;
		if ( hf != null && hf.IsInitialized )
		{
			EditorGUILayout.LabelField(
				$"Layout: {hf.ResolutionX}×{hf.ResolutionZ}  "
					+ $"{hf.WorldSizeX:0.###}m×{hf.WorldSizeZ:0.###}m  "
					+ $"cell {hf.CellSize:0.####}m  maxH {hf.MaxHeight:0.###}m" );
			return;
		}

		if ( visual.HasAuthoredHeight )
		{
			EditorGUILayout.LabelField(
				$"Layout (authored): {visual.AuthoredResolutionX}×{visual.AuthoredResolutionZ}  "
					+ $"{visual.AuthoredWorldSizeX:0.###}m×{visual.AuthoredWorldSizeZ:0.###}m  "
					+ $"maxH {visual.AuthoredMaxHeight:0.###}m" );
		}
	}

	void RebuildPreview( TreasurePileVisual visual )
	{
		visual.EnsureEditorPreview();
		_previewRevision = visual.AuthoredRevision;
		GoldPileSculptToolContext.Painter.SyncPreviewRevision( visual );
		SceneView.RepaintAll();
	}

	static void BakeLatentsForVisual( TreasurePileVisual visual )
	{
		LatentSceneBakeResult result = TryBakeLatentsForVisual( visual, saveAssets: true );
		if ( !result.Ran )
		{
			EditorUtility.DisplayDialog( "Bake Latents", result.Error, "OK" );
			return;
		}

		string coinLine = result.CoinBakeRan
			? $"\nWrote {result.CoinSeatCount} coin seats to {result.CoinBakeName}."
			: "\n(No coin contents — skipped coin seat bake.)";

		if ( !result.Complete )
		{
			string perType = string.IsNullOrEmpty( result.PerType ) ? "" : "\n" + result.PerType;
			EditorUtility.DisplayDialog(
				"Bake Latents",
				$"Wrote {result.PoseCount} of {result.Expected} remainder poses to {result.BakeName} for pile '{result.PileName}' "
				+ $"(+ {result.Authored} curated authored).\n"
				+ $"Missing {Mathf.Max( 0, result.Expected - result.Placed )} — all treasure needs to spawn.{perType}"
				+ coinLine
				+ "\nCheck Treasure surface paint under the sculpted footprint, neighborhood radius, and loot ground height.",
				"OK" );
			return;
		}

		EditorUtility.DisplayDialog(
			"Bake Latents",
			$"Wrote {result.PoseCount} remainder poses to {result.BakeName} for pile '{result.PileName}' "
			+ $"(+ {result.Authored} curated authored)."
			+ coinLine,
			"OK" );
	}

	static void BakeLatentsForAllPilesInScene()
	{
		TreasurePileVisual[] piles = Object.FindObjectsByType<TreasurePileVisual>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );

		int sceneCount = 0;
		for ( int i = 0; i < piles.Length; i++ )
		{
			if ( IsScenePile( piles[ i ] ) )
				sceneCount++;
		}

		if ( sceneCount <= 0 )
		{
			EditorUtility.DisplayDialog( "Bake Latents", "No treasure piles in the open scene(s).", "OK" );
			return;
		}

		if ( !EditorUtility.DisplayDialog(
			"Bake Latents",
			$"Bake latents for {sceneCount} pile{( sceneCount == 1 ? "" : "s" )} in the open scene(s)?",
			"Bake All",
			"Cancel" ) )
			return;

		int baked = 0;
		int incomplete = 0;
		int failed = 0;
		System.Text.StringBuilder missing = new System.Text.StringBuilder();
		try
		{
			int done = 0;
			for ( int i = 0; i < piles.Length; i++ )
			{
				TreasurePileVisual visual = piles[ i ];
				if ( !IsScenePile( visual ) )
					continue;

				done++;
				EditorUtility.DisplayProgressBar(
					"Bake Latents",
					visual.name,
					done / ( float )sceneCount );

				LatentSceneBakeResult result = TryBakeLatentsForVisual( visual, saveAssets: false );
				if ( !result.Ran )
				{
					failed++;
					if ( missing.Length > 0 )
						missing.Append( '\n' );
					missing.Append( visual.name ).Append( ": " ).Append( result.Error );
					continue;
				}

				baked++;
				if ( !result.Complete )
				{
					incomplete++;
					if ( missing.Length > 0 )
						missing.Append( '\n' );
					missing.Append( visual.name )
						.Append( ": " )
						.Append( result.Placed )
						.Append( '/' )
						.Append( result.Expected )
						.Append( " remainder" );
				}
			}
		}
		finally
		{
			EditorUtility.ClearProgressBar();
			AssetDatabase.SaveAssets();
		}

		string extra = missing.Length > 0 ? "\n\n" + missing : "";
		EditorUtility.DisplayDialog(
			"Bake Latents",
			$"Baked {baked}/{sceneCount} piles ({incomplete} incomplete, {failed} failed).{extra}",
			"OK" );
	}

	static bool IsScenePile( TreasurePileVisual visual )
	{
		if ( visual == null )
			return false;
		if ( PrefabUtility.IsPartOfPrefabAsset( visual ) )
			return false;
		return visual.gameObject.scene.IsValid();
	}

	static int CountOtherScenePilesUsingBake( TreasurePileLatentBake bake, TreasurePileVisual except )
	{
		if ( bake == null )
			return 0;

		TreasurePileVisual[] piles = Object.FindObjectsByType<TreasurePileVisual>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );
		int count = 0;
		for ( int i = 0; i < piles.Length; i++ )
		{
			TreasurePileVisual visual = piles[ i ];
			if ( visual == except || !IsScenePile( visual ) )
				continue;
			if ( visual.LatentBake == bake )
				count++;
		}

		return count;
	}

	static TreasurePileLatentBake EnsureUniqueLatentBakeForVisual( TreasurePileVisual visual )
	{
		if ( visual == null )
			return null;

		TreasurePileLatentBake bake = visual.LatentBake;
		if ( bake != null && CountOtherScenePilesUsingBake( bake, visual ) == 0 )
			return bake;

		string scenePath = visual.gameObject.scene.path;
		string folder = "Assets";
		if ( !string.IsNullOrEmpty( scenePath ) )
		{
			string sceneDir = System.IO.Path.GetDirectoryName( scenePath );
			if ( !string.IsNullOrEmpty( sceneDir ) )
				folder = sceneDir.Replace( '\\', '/' );
		}

		string safeName = visual.name.Replace( '/', '_' ).Replace( '\\', '_' );
		string path = AssetDatabase.GenerateUniqueAssetPath( $"{folder}/{safeName}_LatentBake.asset" );
		bake = ScriptableObject.CreateInstance<TreasurePileLatentBake>();
		AssetDatabase.CreateAsset( bake, path );
		Undo.RecordObject( visual, "Assign Treasure Pile Latent Bake" );
		visual.SetLatentBake( bake );
		EditorUtility.SetDirty( visual );
		return bake;
	}

	struct LatentSceneBakeResult
	{
		public bool Ran;
		public bool Complete;
		public string Error;
		public string PileName;
		public string BakeName;
		public int PoseCount;
		public int Expected;
		public int Placed;
		public int Authored;
		public string PerType;
		public string CoinBakeName;
		public int CoinSeatCount;
		public bool CoinBakeRan;
	}

	static int CountOtherScenePilesUsingCoinSeatBake( TreasurePileCoinSeatBake bake, TreasurePileVisual except )
	{
		if ( bake == null )
			return 0;

		TreasurePileVisual[] piles = Object.FindObjectsByType<TreasurePileVisual>(
			FindObjectsInactive.Include,
			FindObjectsSortMode.None );
		int count = 0;
		for ( int i = 0; i < piles.Length; i++ )
		{
			TreasurePileVisual visual = piles[ i ];
			if ( visual == except || !IsScenePile( visual ) )
				continue;
			if ( visual.CoinSeatBake == bake )
				count++;
		}

		return count;
	}

	static TreasurePileCoinSeatBake EnsureUniqueCoinSeatBakeForVisual( TreasurePileVisual visual )
	{
		if ( visual == null )
			return null;

		TreasurePileCoinSeatBake bake = visual.CoinSeatBake;
		if ( bake != null && CountOtherScenePilesUsingCoinSeatBake( bake, visual ) == 0 )
			return bake;

		string scenePath = visual.gameObject.scene.path;
		string folder = "Assets";
		if ( !string.IsNullOrEmpty( scenePath ) )
		{
			string sceneDir = System.IO.Path.GetDirectoryName( scenePath );
			if ( !string.IsNullOrEmpty( sceneDir ) )
				folder = sceneDir.Replace( '\\', '/' );
		}

		string safeName = visual.name.Replace( '/', '_' ).Replace( '\\', '_' );
		string path = AssetDatabase.GenerateUniqueAssetPath( $"{folder}/{safeName}_CoinSeatBake.asset" );
		bake = ScriptableObject.CreateInstance<TreasurePileCoinSeatBake>();
		AssetDatabase.CreateAsset( bake, path );
		Undo.RecordObject( visual, "Assign Treasure Pile Coin Seat Bake" );
		visual.SetCoinSeatBake( bake );
		EditorUtility.SetDirty( visual );
		return bake;
	}

	static LatentSceneBakeResult TryBakeLatentsForVisual( TreasurePileVisual visual, bool saveAssets )
	{
		LatentSceneBakeResult result = default;
		if ( visual == null )
		{
			result.Error = "No treasure pile.";
			return result;
		}

		result.PileName = visual.name;
		visual.EnsureEditorPreview();
		TreasurePileDefinition definition = visual.ResolveDefinitionForEditor();
		if ( definition == null )
		{
			result.Error = "No TreasurePileDefinition on this visual / interactable.";
			return result;
		}

		if ( visual.Heightfield == null || !visual.Heightfield.IsInitialized )
		{
			result.Error = "Heightfield is not initialized.";
			return result;
		}

		TreasurePileLatentBake bake = EnsureUniqueLatentBakeForVisual( visual );
		if ( bake == null )
		{
			result.Error = "Could not create a unique latent bake asset.";
			return result;
		}

		GoldPileArtifactProps props = visual.ArtifactProps;
		if ( props == null )
		{
			props = visual.GetComponent<GoldPileArtifactProps>();
			if ( props == null )
				props = visual.gameObject.AddComponent<GoldPileArtifactProps>();
		}

		Undo.RecordObject( bake, "Bake Treasure Pile Latents" );
		GoldPileArtifactProps.LatentFillReport report = props.BakeLatentsInto(
			visual,
			definition,
			visual.Heightfield,
			visual.transform,
			visual.LootInstances,
			visual.LootLayoutSeed,
			bake );
		EditorUtility.SetDirty( bake );
		EditorUtility.SetDirty( visual );

		result.Ran = true;
		result.Complete = report.Complete;
		result.BakeName = bake.name;
		result.PoseCount = bake.PoseCount;
		result.Expected = report.Expected;
		result.Placed = report.Placed;
		result.Authored = visual.CountAuthoredItems();
		result.PerType = report.PerType;

		bool hasCoinContents = definition.coinContents != null && definition.coinContents.Length > 0;
		if ( hasCoinContents )
		{
			TreasurePileCoinSeatBake coinBake = EnsureUniqueCoinSeatBakeForVisual( visual );
			GoldPileLootInstances loot = visual.LootInstances;
			if ( loot == null )
			{
				loot = visual.GetComponent<GoldPileLootInstances>();
				if ( loot == null )
					loot = visual.gameObject.AddComponent<GoldPileLootInstances>();
			}

			if ( coinBake != null && loot != null )
			{
				Undo.RecordObject( coinBake, "Bake Treasure Pile Coin Seats" );
				try
				{
					GoldPileLootInstances.CoinSeatFillReport coinReport = loot.BakeCoinSeatsInto(
						visual,
						definition,
						visual.Heightfield,
						visual.transform,
						visual.LootLayoutSeed,
						coinBake,
						( t, label ) =>
						{
							EditorUtility.DisplayProgressBar(
								"Bake Coin Seats",
								string.IsNullOrEmpty( label ) ? visual.name : $"{visual.name}: {label}",
								Mathf.Clamp01( t ) );
						} );
					EditorUtility.SetDirty( coinBake );
					EditorUtility.SetDirty( visual );
					result.CoinBakeRan = true;
					result.CoinBakeName = coinBake.name;
					result.CoinSeatCount = coinReport.SeatCount;
				}
				finally
				{
					EditorUtility.ClearProgressBar();
				}
			}
		}

		if ( saveAssets )
			AssetDatabase.SaveAssets();
		RefreshLatentBakePreview( visual );

		return result;
	}

	static void RefreshLatentBakePreview( TreasurePileVisual visual )
	{
		if ( visual == null || Application.isPlaying )
			return;

		ClearLatentBakePreview( visual );
		TreasurePileLatentBake bake = visual.LatentBake;
		if ( bake == null || bake.poses == null || bake.poses.Length == 0 )
			return;

		Transform root = EnsureLatentBakePreviewRoot( visual );
		int neighborhood = 1;
		TreasurePileDefinition definition = visual.ResolveDefinitionForEditor();
		if ( definition != null )
			neighborhood = definition.ResolveLatentSurfaceNeighborhoodCells();

		for ( int i = 0; i < bake.poses.Length; i++ )
		{
			TreasurePileLatentBake.Pose pose = bake.poses[ i ];
			if ( pose.definition == null )
				continue;
			if ( !GoldPileTreasurePlacement.HasTreasureSurfaceBelow( visual.transform, pose.localPos, neighborhood ) )
				continue;

			Vector3 worldPos = visual.transform.TransformPoint( pose.localPos );
			Quaternion worldRot = visual.transform.rotation * pose.localRot;
			TreasureItem item = TreasureItemFactory.SpawnSync( pose.definition, worldPos, worldRot, root );
			if ( item == null )
				continue;

			ConfigureBakePreviewItem( item, pose.scale );
		}

		SceneView.RepaintAll();
	}

	static Transform EnsureLatentBakePreviewRoot( TreasurePileVisual visual )
	{
		Transform existing = visual.FindLatentBakePreviewRoot();
		if ( existing != null )
		{
			SetHideAndDontSaveRecursive( existing );
			return existing;
		}

		GameObject go = new GameObject( TreasurePileVisual.LatentBakePreviewRootName );
		go.transform.SetParent( visual.transform, false );
		go.transform.localPosition = Vector3.zero;
		go.transform.localRotation = Quaternion.identity;
		go.transform.localScale = Vector3.one;
		SetHideAndDontSaveRecursive( go.transform );
		return go.transform;
	}

	static void ClearLatentBakePreview( TreasurePileVisual visual )
	{
		Transform root = visual != null ? visual.FindLatentBakePreviewRoot() : null;
		if ( root == null )
			return;

		for ( int i = root.childCount - 1; i >= 0; i-- )
		{
			Transform child = root.GetChild( i );
			if ( child == null )
				continue;

			TreasureItem item = child.GetComponent<TreasureItem>();
			GameObject go = child.gameObject;
			if ( item != null )
			{
				bool viaAddressables = item.ReleasedViaAddressables;
				item.OnDespawned();
				if ( viaAddressables )
					UnityEngine.AddressableAssets.Addressables.ReleaseInstance( go );
				else
					Object.DestroyImmediate( go );
			}
			else
				Object.DestroyImmediate( go );
		}
	}

	static void ConfigureBakePreviewItem( TreasureItem item, float scale )
	{
		if ( item == null )
			return;

		GameObject go = item.gameObject;
		SetHideAndDontSaveRecursive( go.transform );

		Rigidbody body = item.Body;
		if ( body != null )
		{
			body.isKinematic = true;
			body.detectCollisions = false;
		}

		Collider[] cols = go.GetComponentsInChildren<Collider>( true );
		for ( int i = 0; i < cols.Length; i++ )
		{
			if ( cols[ i ] != null )
				cols[ i ].enabled = false;
		}

		Behaviour[] behaviours = go.GetComponentsInChildren<Behaviour>( true );
		for ( int i = 0; i < behaviours.Length; i++ )
		{
			Behaviour b = behaviours[ i ];
			if ( b == null || b is TreasureItem )
				continue;
			b.enabled = false;
		}

		if ( scale > 0.01f )
			go.transform.localScale = Vector3.one * scale;
	}

	static void SetHideAndDontSaveRecursive( Transform root )
	{
		if ( root == null )
			return;

		root.gameObject.hideFlags = HideFlags.HideAndDontSave;
		for ( int i = 0; i < root.childCount; i++ )
			SetHideAndDontSaveRecursive( root.GetChild( i ) );
	}
}
#endif
