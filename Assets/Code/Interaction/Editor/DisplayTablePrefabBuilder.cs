using System.IO;

using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Builds Gem/Coin/Mixed display table prefabs under Assets/Addressables/Tables/
/// and registers them with Addressables.
/// </summary>
public static class DisplayTablePrefabBuilder
{
	const string TablesFolder = "Assets/Addressables/Tables";
	const string GemPrefabPath = TablesFolder + "/GemDisplayTable.prefab";
	const string CoinPrefabPath = TablesFolder + "/CoinDisplayTable.prefab";
	const string MixedPrefabPath = TablesFolder + "/MixedDisplayTable.prefab";
	const string ConstellationPrefabPath = TablesFolder + "/GemConstellation.prefab";
	const string ArtifactPresentationPrefabPath = TablesFolder + "/ArtifactPresentationTable.prefab";
	const string TableMeshPrefab = "Assets/ThirdParty/CobraGamesAssets/Stylized_Dungeon_Props_Pack/Prefabs/Furniture/Table_Small.prefab";
	const string ConstellationShaderFolder = "Assets/Materials/Shaders/Constellation";
	const string ConstellationLineMaterialPath = ConstellationShaderFolder + "/M_ConstellationLine.mat";
	const string ConstellationLineShaderName = "DragonLoot/ConstellationLine";
	const string ArtifactSlotShaderFolder = "Assets/Materials/Shaders/ArtifactSlot";
	const string ArtifactSlotIndicatorMaterialPath = ArtifactSlotShaderFolder + "/M_ArtifactSlotIndicator.mat";
	const string ArtifactSlotIndicatorShaderName = "DragonLoot/ArtifactSlotIndicator";

	[MenuItem( "DragonLoot/Prefabs/Build Display Table Prefabs" )]
	public static void BuildFromMenu()
	{
		BuildAll( force: true );
	}

	/// <summary>Unity batchmode entry: -executeMethod DisplayTablePrefabBuilder.BuildFromCommandLine</summary>
	public static void BuildFromCommandLine()
	{
		BuildAll( force: true );
	}

	[InitializeOnLoadMethod]
	static void EnsurePrefabsExist()
	{
		EditorApplication.delayCall += () =>
		{
			EnsureConstellationLineMaterialAssigned();

			if ( File.Exists( GemPrefabPath ) && File.Exists( CoinPrefabPath ) && File.Exists( MixedPrefabPath ) && File.Exists( ConstellationPrefabPath ) && File.Exists( ArtifactPresentationPrefabPath ) )
				return;

			BuildAll( force: false );
		};
	}

	static void EnsureConstellationLineMaterialAssigned()
	{
		if ( !File.Exists( ConstellationPrefabPath ) )
			return;

		// Skip rewrite when a material is already wired on the prefab.
		string prefabText = File.ReadAllText( ConstellationPrefabPath );
		if ( !prefabText.Contains( "lineMaterial: {fileID: 0}" ) )
			return;

		Material lineMat = EnsureConstellationLineMaterial();
		if ( lineMat == null )
			return;

		GameObject prefabRoot = PrefabUtility.LoadPrefabContents( ConstellationPrefabPath );
		if ( prefabRoot == null )
			return;

		try
		{
			GemConstellationLineVisual lineVisual = prefabRoot.GetComponentInChildren<GemConstellationLineVisual>( true );
			if ( lineVisual == null )
				return;

			SerializedObject lineSo = new SerializedObject( lineVisual );
			SerializedProperty lineMatProp = lineSo.FindProperty( "lineMaterial" );
			if ( lineMatProp == null || lineMatProp.objectReferenceValue != null )
				return;

			lineMatProp.objectReferenceValue = lineMat;
			lineSo.ApplyModifiedPropertiesWithoutUndo();
			PrefabUtility.SaveAsPrefabAsset( prefabRoot, ConstellationPrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( prefabRoot );
		}
	}

	static void BuildAll( bool force )
	{
		EnsureFolder( "Assets/Addressables" );
		EnsureFolder( TablesFolder );

		if ( force || !File.Exists( GemPrefabPath ) )
			BuildTablePrefab( GemPrefabPath, "GemDisplayTable", typeof( GemDisplayTableInteractable ), LayoutKind.Gem );

		if ( force || !File.Exists( CoinPrefabPath ) )
			BuildTablePrefab( CoinPrefabPath, "CoinDisplayTable", typeof( CoinDisplayTableInteractable ), LayoutKind.Coin );

		if ( force || !File.Exists( MixedPrefabPath ) )
			BuildTablePrefab( MixedPrefabPath, "MixedDisplayTable", typeof( MixedDisplayTableInteractable ), LayoutKind.Mixed );

		if ( force || !File.Exists( ConstellationPrefabPath ) )
			BuildConstellationPrefab( ConstellationPrefabPath );

		if ( force || !File.Exists( ArtifactPresentationPrefabPath ) )
			BuildArtifactPresentationPrefab( ArtifactPresentationPrefabPath );

		RegisterAddressable( GemPrefabPath, "Tables/GemDisplayTable" );
		RegisterAddressable( CoinPrefabPath, "Tables/CoinDisplayTable" );
		RegisterAddressable( MixedPrefabPath, "Tables/MixedDisplayTable" );
		RegisterAddressable( ConstellationPrefabPath, "Tables/GemConstellation" );
		RegisterAddressable( ArtifactPresentationPrefabPath, "Tables/ArtifactPresentationTable" );

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log( "Display table prefabs ready under " + TablesFolder );
	}

	enum LayoutKind
	{
		Gem,
		Coin,
		Mixed
	}

	static void BuildTablePrefab( string path, string rootName, System.Type interactableType, LayoutKind layout )
	{
		GameObject root = new GameObject( rootName );

		BoxCollider box = root.AddComponent<BoxCollider>();
		box.center = new Vector3( 0f, 0.45f, 0f );
		box.size = new Vector3( 1.2f, 0.9f, 0.7f );

		Component interactable = root.AddComponent( interactableType );

		GameObject displayAreaGo = new GameObject( "DisplayArea" );
		displayAreaGo.transform.SetParent( root.transform, false );
		displayAreaGo.transform.localPosition = new Vector3( 0f, 0.92f, 0f );

		SerializedObject so = new SerializedObject( interactable );
		SerializedProperty displayAreaProp = so.FindProperty( "displayArea" );
		if ( displayAreaProp != null )
			displayAreaProp.objectReferenceValue = displayAreaGo.transform;

		SerializedProperty rowsProp = so.FindProperty( "rows" );
		SerializedProperty columnsProp = so.FindProperty( "columns" );
		SerializedProperty spacingProp = so.FindProperty( "slotSpacing" );
		SerializedProperty marginProp = so.FindProperty( "margin" );

		switch ( layout )
		{
			case LayoutKind.Gem:
				if ( rowsProp != null )
					rowsProp.intValue = 3;
				if ( columnsProp != null )
					columnsProp.intValue = 4;
				if ( spacingProp != null )
					spacingProp.floatValue = 0.2f;
				break;
			case LayoutKind.Coin:
				if ( rowsProp != null )
					rowsProp.intValue = 4;
				if ( columnsProp != null )
					columnsProp.intValue = 6;
				if ( spacingProp != null )
					spacingProp.floatValue = 0.12f;
				break;
			default:
				if ( rowsProp != null )
					rowsProp.intValue = 3;
				if ( columnsProp != null )
					columnsProp.intValue = 4;
				if ( spacingProp != null )
					spacingProp.floatValue = 0.18f;
				break;
		}

		if ( marginProp != null )
			marginProp.floatValue = 0.02f;

		so.ApplyModifiedPropertiesWithoutUndo();

		GameObject visualSource = AssetDatabase.LoadAssetAtPath<GameObject>( TableMeshPrefab );
		if ( visualSource != null )
		{
			GameObject visual = ( GameObject )PrefabUtility.InstantiatePrefab( visualSource );
			visual.name = "Visual";
			visual.transform.SetParent( root.transform, false );
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
		}
		else
		{
			GameObject fallback = GameObject.CreatePrimitive( PrimitiveType.Cube );
			fallback.name = "Visual";
			fallback.transform.SetParent( root.transform, false );
			fallback.transform.localPosition = new Vector3( 0f, 0.4f, 0f );
			fallback.transform.localScale = new Vector3( 1.2f, 0.8f, 0.7f );
			Object.DestroyImmediate( fallback.GetComponent<Collider>() );
		}

		PrefabUtility.SaveAsPrefabAsset( root, path );
		Object.DestroyImmediate( root );
	}

	static void BuildConstellationPrefab( string path )
	{
		GameObject root = new GameObject( "GemConstellation" );

		BoxCollider box = root.AddComponent<BoxCollider>();
		box.center = new Vector3( 0f, 0.75f, 0f );
		box.size = new Vector3( 1.4f, 1.5f, 0.25f );

		GemConstellationInteractable interactable = root.AddComponent<GemConstellationInteractable>();

		GameObject slotsRoot = new GameObject( "Slots" );
		slotsRoot.transform.SetParent( root.transform, false );

		GameObject linesRoot = new GameObject( "Lines" );
		linesRoot.transform.SetParent( root.transform, false );
		GemConstellationLineVisual lineVisual = linesRoot.AddComponent<GemConstellationLineVisual>();

		Vector3[] sampleLocalPositions =
		{
			new Vector3( -0.35f, 0.95f, 0.02f ),
			new Vector3( 0f, 1.15f, 0.02f ),
			new Vector3( 0.35f, 0.95f, 0.02f ),
			new Vector3( -0.2f, 0.55f, 0.02f ),
			new Vector3( 0.2f, 0.55f, 0.02f ),
			new Vector3( 0f, 0.25f, 0.02f )
		};

		Transform[] anchors = new Transform[ sampleLocalPositions.Length ];
		for ( int i = 0; i < sampleLocalPositions.Length; i++ )
		{
			GameObject slotGo = new GameObject( "Slot_" + i );
			slotGo.transform.SetParent( slotsRoot.transform, false );
			slotGo.transform.localPosition = sampleLocalPositions[ i ];
			anchors[ i ] = slotGo.transform;
		}

		GameObject visual = GameObject.CreatePrimitive( PrimitiveType.Cube );
		visual.name = "Visual";
		visual.transform.SetParent( root.transform, false );
		visual.transform.localPosition = new Vector3( 0f, 0.75f, 0f );
		visual.transform.localScale = new Vector3( 1.4f, 1.5f, 0.12f );
		Object.DestroyImmediate( visual.GetComponent<Collider>() );

		SerializedObject so = new SerializedObject( interactable );
		SerializedProperty slotsProp = so.FindProperty( "slots" );
		if ( slotsProp != null )
		{
			slotsProp.arraySize = anchors.Length;
			for ( int i = 0; i < anchors.Length; i++ )
			{
				SerializedProperty element = slotsProp.GetArrayElementAtIndex( i );
				SerializedProperty anchorProp = element.FindPropertyRelative( "anchor" );
				if ( anchorProp != null )
					anchorProp.objectReferenceValue = anchors[ i ];
			}
		}

		SerializedProperty lineVisualProp = so.FindProperty( "lineVisual" );
		if ( lineVisualProp != null )
			lineVisualProp.objectReferenceValue = lineVisual;

		so.ApplyModifiedPropertiesWithoutUndo();

		SerializedObject lineSo = new SerializedObject( lineVisual );
		SerializedProperty constellationProp = lineSo.FindProperty( "constellation" );
		if ( constellationProp != null )
			constellationProp.objectReferenceValue = interactable;

		Material lineMat = EnsureConstellationLineMaterial();
		SerializedProperty lineMatProp = lineSo.FindProperty( "lineMaterial" );
		if ( lineMatProp != null && lineMat != null )
			lineMatProp.objectReferenceValue = lineMat;

		lineSo.ApplyModifiedPropertiesWithoutUndo();

		PrefabUtility.SaveAsPrefabAsset( root, path );
		Object.DestroyImmediate( root );
	}

	static void BuildArtifactPresentationPrefab( string path )
	{
		GameObject root = new GameObject( "ArtifactPresentationTable" );

		BoxCollider box = root.AddComponent<BoxCollider>();
		box.center = new Vector3( 0f, 0.45f, 0f );
		box.size = new Vector3( 1.2f, 0.9f, 0.7f );

		ArtifactPresentationTableInteractable interactable = root.AddComponent<ArtifactPresentationTableInteractable>();
		ArtifactPresentationSlotIndicators indicators = root.AddComponent<ArtifactPresentationSlotIndicators>();

		GameObject slotsRoot = new GameObject( "Slots" );
		slotsRoot.transform.SetParent( root.transform, false );

		Vector3[] sampleLocalPositions =
		{
			new Vector3( -0.28f, 0.92f, -0.12f ),
			new Vector3( 0f, 0.92f, 0.12f ),
			new Vector3( 0.28f, 0.92f, -0.12f ),
			new Vector3( 0f, 0.92f, -0.22f )
		};

		Transform[] anchors = new Transform[ sampleLocalPositions.Length ];
		for ( int i = 0; i < sampleLocalPositions.Length; i++ )
		{
			GameObject slotGo = new GameObject( "Slot_" + i );
			slotGo.transform.SetParent( slotsRoot.transform, false );
			slotGo.transform.localPosition = sampleLocalPositions[ i ];
			anchors[ i ] = slotGo.transform;

			BoxCollider slotBox = slotGo.AddComponent<BoxCollider>();
			slotBox.center = Vector3.zero;
			slotBox.size = new Vector3( 0.22f, 0.06f, 0.22f );
			ArtifactPresentationSlotVolume volume = slotGo.AddComponent<ArtifactPresentationSlotVolume>();
			volume.Configure( interactable, i );
		}

		GameObject visualSource = AssetDatabase.LoadAssetAtPath<GameObject>( TableMeshPrefab );
		if ( visualSource != null )
		{
			GameObject visual = ( GameObject )PrefabUtility.InstantiatePrefab( visualSource );
			visual.name = "Visual";
			visual.transform.SetParent( root.transform, false );
			visual.transform.localPosition = Vector3.zero;
			visual.transform.localRotation = Quaternion.identity;
			visual.transform.localScale = Vector3.one;
		}
		else
		{
			GameObject fallback = GameObject.CreatePrimitive( PrimitiveType.Cube );
			fallback.name = "Visual";
			fallback.transform.SetParent( root.transform, false );
			fallback.transform.localPosition = new Vector3( 0f, 0.4f, 0f );
			fallback.transform.localScale = new Vector3( 1.2f, 0.8f, 0.7f );
			Object.DestroyImmediate( fallback.GetComponent<Collider>() );
		}

		Material indicatorMat = EnsureArtifactSlotIndicatorMaterial();

		SerializedObject tableSo = new SerializedObject( interactable );
		SerializedProperty slotsProp = tableSo.FindProperty( "slots" );
		if ( slotsProp != null )
		{
			slotsProp.arraySize = anchors.Length;
			for ( int i = 0; i < anchors.Length; i++ )
			{
				SerializedProperty element = slotsProp.GetArrayElementAtIndex( i );
				SerializedProperty anchorProp = element.FindPropertyRelative( "anchor" );
				if ( anchorProp != null )
					anchorProp.objectReferenceValue = anchors[ i ];
			}
		}

		SerializedProperty indicatorsProp = tableSo.FindProperty( "slotIndicators" );
		if ( indicatorsProp != null )
			indicatorsProp.objectReferenceValue = indicators;

		tableSo.ApplyModifiedPropertiesWithoutUndo();

		SerializedObject indicatorsSo = new SerializedObject( indicators );
		SerializedProperty indicatorMatProp = indicatorsSo.FindProperty( "indicatorMaterial" );
		if ( indicatorMatProp != null && indicatorMat != null )
			indicatorMatProp.objectReferenceValue = indicatorMat;

		indicatorsSo.ApplyModifiedPropertiesWithoutUndo();

		PrefabUtility.SaveAsPrefabAsset( root, path );
		Object.DestroyImmediate( root );
	}

	static Material EnsureArtifactSlotIndicatorMaterial()
	{
		Material existing = AssetDatabase.LoadAssetAtPath<Material>( ArtifactSlotIndicatorMaterialPath );
		if ( existing != null )
			return existing;

		Shader shader = Shader.Find( ArtifactSlotIndicatorShaderName );
		if ( shader == null )
		{
			Debug.LogWarning( "Artifact slot indicator shader missing: " + ArtifactSlotIndicatorShaderName );
			return null;
		}

		EnsureFolder( ArtifactSlotShaderFolder );
		Material mat = new Material( shader );
		mat.name = "M_ArtifactSlotIndicator";
		mat.SetColor( "_TintColor", new Color( 0.35f, 0.95f, 1.4f, 0.55f ) );
		mat.SetColor( "_RimColor", new Color( 0.2f, 1.2f, 1.8f, 1f ) );
		mat.SetColor( "_CoreColor", new Color( 0.08f, 0.35f, 0.5f, 1f ) );
		AssetDatabase.CreateAsset( mat, ArtifactSlotIndicatorMaterialPath );
		AssetDatabase.SaveAssets();
		return mat;
	}

	static Material EnsureConstellationLineMaterial()
	{
		Material existing = AssetDatabase.LoadAssetAtPath<Material>( ConstellationLineMaterialPath );
		if ( existing != null )
			return existing;

		Shader shader = Shader.Find( ConstellationLineShaderName );
		if ( shader == null )
		{
			Debug.LogWarning( "Constellation line shader missing: " + ConstellationLineShaderName );
			return null;
		}

		EnsureFolder( ConstellationShaderFolder );
		Material mat = new Material( shader );
		mat.name = "M_ConstellationLine";
		mat.SetColor( "_Color", new Color( 0.45f, 1.1f, 1.8f, 1f ) );
		mat.SetColor( "_CoreColor", new Color( 1.6f, 2.2f, 3f, 1f ) );
		mat.SetColor( "_PulseColor", new Color( 2f, 1.2f, 3.2f, 1f ) );
		AssetDatabase.CreateAsset( mat, ConstellationLineMaterialPath );
		AssetDatabase.SaveAssets();
		return mat;
	}

	static void RegisterAddressable( string assetPath, string address )
	{
		AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
		if ( settings == null )
		{
			Debug.LogWarning( "AddressableAssetSettings missing; skipped registering " + assetPath );
			return;
		}

		string guid = AssetDatabase.AssetPathToGUID( assetPath );
		if ( string.IsNullOrEmpty( guid ) )
			return;

		AddressableAssetGroup group = settings.DefaultGroup;
		AddressableAssetEntry entry = settings.FindAssetEntry( guid );
		if ( entry == null )
			entry = settings.CreateOrMoveEntry( guid, group, readOnly: false, postEvent: false );

		entry.SetAddress( address );
		settings.SetDirty( AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true );
	}

	static void EnsureFolder( string path )
	{
		if ( AssetDatabase.IsValidFolder( path ) )
			return;

		string parent = Path.GetDirectoryName( path ).Replace( '\\', '/' );
		string name = Path.GetFileName( path );
		if ( !AssetDatabase.IsValidFolder( parent ) )
			EnsureFolder( parent );

		AssetDatabase.CreateFolder( parent, name );
	}
}
