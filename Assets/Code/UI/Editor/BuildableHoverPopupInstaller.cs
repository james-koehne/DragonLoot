#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot authoring for the blueprint BuildableHoverPopup prefab and nesting on Interface.
/// Menu-driven only — no InitializeOnLoad. Idempotent: always leaves exactly one nested instance.
/// </summary>
public static class BuildableHoverPopupInstaller
{
	const string PopupPrefabPath = "Assets/Prefabs/UI/BuildableHoverPopup.prefab";
	const string InterfacePrefabPath = "Assets/Addressables/Interface.prefab";
	const string PopupChildName = "BuildableHoverPopup";
	const string FontPath = "Assets/Fonts/NotoSerif.ttf";

	static readonly Color PlateColor = new Color( 0.039f, 0.086f, 0.125f, 0.85f );
	static readonly Color BorderColor = new Color( 0.369f, 0.784f, 1f, 0.75f );
	static readonly Color TitleColor = new Color( 0.369f, 0.784f, 1f, 1f );
	static readonly Color BodyColor = new Color( 0.55f, 0.82f, 0.95f, 0.82f );
	static readonly Color CostColor = new Color( 0.45f, 0.7f, 0.82f, 0.7f );
	static readonly Color CornerColor = new Color( 0.369f, 0.784f, 1f, 0.9f );

	const float PopupWidth = 400f;
	const float PopupHeight = 150f;

	[MenuItem( DragonLootMenus.UiInstallBuildableHover )]
	public static void InstallFromMenu()
	{
		Install();
		Debug.Log( "BuildableHoverPopup installed and nested on Interface prefab." );
	}

	/// <summary>Batchmode entry: Unity -batchmode -executeMethod BuildableHoverPopupInstaller.InstallBatch</summary>
	public static void InstallBatch()
	{
		Install();
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	public static void Install()
	{
		EnsureFolder( "Assets/Prefabs" );
		EnsureFolder( "Assets/Prefabs/UI" );

		GameObject popupPrefab = CreateOrUpdatePopupPrefab();
		NestPrefabOnInterface( popupPrefab );
	}

	static GameObject CreateOrUpdatePopupPrefab()
	{
		GameObject root = new GameObject( PopupChildName, typeof( RectTransform ), typeof( CanvasGroup ), typeof( BuildableHoverUI ) );
		try
		{
			BuildPopupHierarchy( root );
			return PrefabUtility.SaveAsPrefabAsset( root, PopupPrefabPath );
		}
		finally
		{
			Object.DestroyImmediate( root );
		}
	}

	/// <summary>
	/// Ensures exactly one nested <see cref="PopupPrefabPath"/> instance under Interface.
	/// Patches open Prefab Mode contents when Interface is staged, so saves cannot stack duplicates.
	/// </summary>
	static void NestPrefabOnInterface( GameObject popupPrefab )
	{
		if ( popupPrefab == null )
		{
			Debug.LogError( "BuildableHoverPopupInstaller: popup prefab missing." );
			return;
		}

		PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
		if ( stage != null && stage.assetPath == InterfacePrefabPath && stage.prefabContentsRoot != null )
		{
			EnsureSingleNestedInstance( stage.prefabContentsRoot, popupPrefab );
			EditorSceneManager.MarkSceneDirty( stage.scene );
			PrefabUtility.SaveAsPrefabAsset( stage.prefabContentsRoot, InterfacePrefabPath );
			return;
		}

		GameObject interfaceRoot = PrefabUtility.LoadPrefabContents( InterfacePrefabPath );
		if ( interfaceRoot == null )
		{
			Debug.LogWarning( "BuildableHoverPopupInstaller: Interface prefab missing at " + InterfacePrefabPath );
			return;
		}

		try
		{
			EnsureSingleNestedInstance( interfaceRoot, popupPrefab );
			PrefabUtility.SaveAsPrefabAsset( interfaceRoot, InterfacePrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( interfaceRoot );
		}
	}

	static void EnsureSingleNestedInstance( GameObject interfaceRoot, GameObject popupPrefab )
	{
		DestroyAllNamedDirectChildren( interfaceRoot.transform, PopupChildName );

		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab( popupPrefab, interfaceRoot.transform );
		instance.name = PopupChildName;

		RectTransform rect = instance.GetComponent<RectTransform>();
		if ( rect == null )
			return;

		rect.anchorMin = new Vector2( 0.5f, 0.5f );
		rect.anchorMax = new Vector2( 0.5f, 0.5f );
		rect.pivot = new Vector2( 0.5f, 1f );
		rect.anchoredPosition = new Vector2( 0f, -56f );
		rect.sizeDelta = new Vector2( PopupWidth, PopupHeight );
		rect.localScale = Vector3.one;
		rect.localRotation = Quaternion.identity;
	}

	static void DestroyAllNamedDirectChildren( Transform parent, string childName )
	{
		var toDestroy = new List<GameObject>();
		for ( int i = 0; i < parent.childCount; i++ )
		{
			Transform child = parent.GetChild( i );
			if ( child != null && child.name == childName )
				toDestroy.Add( child.gameObject );
		}

		for ( int i = 0; i < toDestroy.Count; i++ )
			Object.DestroyImmediate( toDestroy[ i ] );
	}

	static void BuildPopupHierarchy( GameObject root )
	{
		RectTransform rootRect = root.GetComponent<RectTransform>();
		rootRect.anchorMin = new Vector2( 0.5f, 0.5f );
		rootRect.anchorMax = new Vector2( 0.5f, 0.5f );
		rootRect.pivot = new Vector2( 0.5f, 1f );
		rootRect.anchoredPosition = new Vector2( 0f, -56f );
		rootRect.sizeDelta = new Vector2( PopupWidth, PopupHeight );
		rootRect.localScale = Vector3.one;
		rootRect.localRotation = Quaternion.identity;

		CanvasGroup group = root.GetComponent<CanvasGroup>();
		group.alpha = 0f;
		group.blocksRaycasts = false;
		group.interactable = false;

		RectTransform panel = CreateChild( root.transform, "Panel" );
		StretchFill( panel );
		Image plate = panel.gameObject.AddComponent<Image>();
		plate.color = PlateColor;
		plate.raycastTarget = false;
		Outline outline = panel.gameObject.AddComponent<Outline>();
		outline.effectColor = BorderColor;
		outline.effectDistance = new Vector2( 1.5f, 1.5f );
		outline.useGraphicAlpha = true;

		CreateCorner( panel, "CornerTL", new Vector2( 0f, 1f ), new Vector2( 0f, 1f ), new Vector2( 8f, -8f ) );
		CreateCorner( panel, "CornerTR", new Vector2( 1f, 1f ), new Vector2( 1f, 1f ), new Vector2( -8f, -8f ) );
		CreateCorner( panel, "CornerBL", new Vector2( 0f, 0f ), new Vector2( 0f, 0f ), new Vector2( 8f, 8f ) );
		CreateCorner( panel, "CornerBR", new Vector2( 1f, 0f ), new Vector2( 1f, 0f ), new Vector2( -8f, 8f ) );

		Text nameText = CreateLabel( panel, "Name", 22, FontStyle.Bold, TitleColor );
		RectTransform nameRect = nameText.rectTransform;
		nameRect.anchorMin = new Vector2( 0f, 1f );
		nameRect.anchorMax = new Vector2( 1f, 1f );
		nameRect.pivot = new Vector2( 0.5f, 1f );
		nameRect.anchoredPosition = new Vector2( 0f, -14f );
		nameRect.sizeDelta = new Vector2( -36f, 28f );
		nameText.alignment = TextAnchor.UpperLeft;
		nameText.text = "BUILDABLE";

		Text descriptionText = CreateLabel( panel, "Description", 15, FontStyle.Normal, BodyColor );
		RectTransform descRect = descriptionText.rectTransform;
		descRect.anchorMin = new Vector2( 0f, 0f );
		descRect.anchorMax = new Vector2( 1f, 1f );
		descRect.pivot = new Vector2( 0.5f, 0.5f );
		descRect.anchoredPosition = new Vector2( 0f, -5f );
		descRect.sizeDelta = new Vector2( -36f, -82f );
		descriptionText.alignment = TextAnchor.UpperLeft;
		descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
		descriptionText.verticalOverflow = VerticalWrapMode.Truncate;
		descriptionText.text = string.Empty;
		descriptionText.gameObject.SetActive( false );

		Text costText = CreateLabel( panel, "Cost", 14, FontStyle.Bold, CostColor );
		RectTransform costRect = costText.rectTransform;
		costRect.anchorMin = new Vector2( 0f, 0f );
		costRect.anchorMax = new Vector2( 1f, 0f );
		costRect.pivot = new Vector2( 0.5f, 0f );
		costRect.anchoredPosition = new Vector2( 0f, 12f );
		costRect.sizeDelta = new Vector2( -36f, 22f );
		costText.alignment = TextAnchor.LowerLeft;
		costText.text = "COST  Free";

		SerializedObject so = new SerializedObject( root.GetComponent<BuildableHoverUI>() );
		so.FindProperty( "_group" ).objectReferenceValue = group;
		so.FindProperty( "_nameText" ).objectReferenceValue = nameText;
		so.FindProperty( "_descriptionText" ).objectReferenceValue = descriptionText;
		so.FindProperty( "_costText" ).objectReferenceValue = costText;
		so.ApplyModifiedPropertiesWithoutUndo();
	}

	static RectTransform CreateChild( Transform parent, string childName )
	{
		GameObject go = new GameObject( childName, typeof( RectTransform ) );
		go.transform.SetParent( parent, false );
		return go.GetComponent<RectTransform>();
	}

	static void StretchFill( RectTransform rect )
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		rect.pivot = new Vector2( 0.5f, 0.5f );
	}

	static void CreateCorner( RectTransform parent, string childName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos )
	{
		RectTransform corner = CreateChild( parent, childName );
		corner.anchorMin = anchorMin;
		corner.anchorMax = anchorMax;
		corner.pivot = new Vector2( 0.5f, 0.5f );
		corner.anchoredPosition = anchoredPos;
		corner.sizeDelta = new Vector2( 10f, 10f );
		Image image = corner.gameObject.AddComponent<Image>();
		image.color = CornerColor;
		image.raycastTarget = false;
	}

	static Text CreateLabel( RectTransform parent, string childName, int fontSize, FontStyle style, Color color )
	{
		GameObject go = new GameObject( childName, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		go.transform.SetParent( parent, false );
		Text text = go.GetComponent<Text>();
		Font font = AssetDatabase.LoadAssetAtPath<Font>( FontPath );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		text.font = font;
		text.fontSize = fontSize;
		text.fontStyle = style;
		text.color = color;
		text.raycastTarget = false;
		text.supportRichText = false;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		return text;
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
#endif
