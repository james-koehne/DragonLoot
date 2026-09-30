#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires <see cref="ToastStackUI"/> hierarchy into Interface.prefab.
/// Root RectTransform is only set on first create so designer positioning is preserved.
/// </summary>
public static class ToastStackBootstrap
{
	const string InterfacePrefabPath = "Assets/Addressables/Interface.prefab";
	const string FontPath = "Assets/Fonts/NotoSerif.ttf";
	const string DiscoveryIconPath = "Assets/Textures/Icons/icon_coin.png";
	const string CompletionIconPath = "Assets/Textures/Icons/icon_star_TEMP_DELETE.png";
	const string UnlockIconPath = "Assets/Textures/Icons/icon_star_TEMP_DELETE.png";
	const string MilestoneIconPath = "Assets/Textures/Icons/icon_quest.png";

	static readonly Color DiscoveryBackdrop = new Color( 0.05f, 0.04f, 0.02f, 0.72f );
	static readonly Color GoldOutline = new Color( 0.92f, 0.78f, 0.32f, 0.85f );
	static readonly Color CoinAccent = new Color( 0.95f, 0.78f, 0.28f, 1f );
	static readonly Color GoldKicker = new Color( 1f, 0.88f, 0.45f, 1f );
	static readonly Color DescriptionColor = new Color( 0.78f, 0.74f, 0.62f, 0.82f );

	static bool _ranThisDomain;

	[InitializeOnLoadMethod]
	static void QueuePatch()
	{
		if ( _ranThisDomain )
			return;
		_ranThisDomain = true;
		// Double delay so we run after other Interface writers (e.g. QuestObjectiveFeedbackPostprocessor)
		// that LoadPrefabContents + SaveAsPrefabAsset and would otherwise stomp ToastStack.
		EditorApplication.delayCall += () => EditorApplication.delayCall += EnsureOnPrefab;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureToastStack )]
	static void MenuEnsure()
	{
		EnsureOnPrefab( forceRebuildSlots: false );
		Debug.Log( "ToastStack ensured on Interface prefab." );
	}

	/// <summary>Entry point for Unity -batchmode -executeMethod ToastStackBootstrap.EnsureOnPrefabBatch.</summary>
	public static void EnsureOnPrefabBatch()
	{
		EnsureOnPrefab( forceRebuildSlots: false );
	}

	public static void EnsureOnPrefab()
	{
		EnsureOnPrefab( forceRebuildSlots: false );
	}

	public static void EnsureOnPrefab( bool forceRebuildSlots )
	{
		var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
		if ( stage != null && stage.assetPath == InterfacePrefabPath && stage.prefabContentsRoot != null )
		{
			if ( ApplyToRoot( stage.prefabContentsRoot, forceRebuildSlots ) )
			{
				UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty( stage.scene );
				PrefabUtility.SaveAsPrefabAsset( stage.prefabContentsRoot, InterfacePrefabPath );
				Debug.Log( "ToastStackBootstrap: patched open Interface Prefab Mode contents and saved asset." );
			}
			return;
		}

		GameObject root = PrefabUtility.LoadPrefabContents( InterfacePrefabPath );
		if ( root == null )
		{
			Debug.LogWarning( "ToastStackBootstrap: Interface prefab missing at " + InterfacePrefabPath );
			return;
		}

		try
		{
			if ( ApplyToRoot( root, forceRebuildSlots ) )
				PrefabUtility.SaveAsPrefabAsset( root, InterfacePrefabPath );
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents( root );
		}
	}

	static bool ApplyToRoot( GameObject root, bool forceRebuildSlots )
	{
		bool dirty = false;
		ToastStackUI stack = root.GetComponentInChildren<ToastStackUI>( true );

		if ( stack == null )
		{
			Transform existing = root.transform.Find( "ToastStack" );
			GameObject go = existing != null ? existing.gameObject : new GameObject( "ToastStack", typeof( RectTransform ) );
			if ( existing == null )
				go.transform.SetParent( root.transform, false );

			stack = go.GetComponent<ToastStackUI>();
			if ( stack == null )
				stack = go.AddComponent<ToastStackUI>();

			ApplyDefaultRootRect( go.GetComponent<RectTransform>() );
			dirty = true;
		}

		if ( SyncLayerRecursive( stack.gameObject, root.layer ) )
			dirty = true;

		SerializedObject so = new SerializedObject( stack );
		SerializedProperty slotsProp = so.FindProperty( "slots" );
		bool slotsWired = AreSlotsWired( slotsProp );

		if ( !slotsWired || forceRebuildSlots )
		{
			if ( forceRebuildSlots )
				ClearExistingItems( stack.transform );

			ToastStackUI.ToastSlot[] bindings = new ToastStackUI.ToastSlot[ ToastStackUI.MaxVisible ];
			for ( int i = 0; i < ToastStackUI.MaxVisible; i++ )
				bindings[ i ] = CreateOrRefreshItem( stack.transform, i );

			slotsProp.arraySize = ToastStackUI.MaxVisible;
			for ( int i = 0; i < ToastStackUI.MaxVisible; i++ )
				WriteSlot( slotsProp.GetArrayElementAtIndex( i ), bindings[ i ] );

			Font font = AssetDatabase.LoadAssetAtPath<Font>( FontPath );
			if ( font == null && bindings[ 0 ] != null && bindings[ 0 ].Label != null )
				font = bindings[ 0 ].Label.font;
			if ( font != null )
				so.FindProperty( "labelFont" ).objectReferenceValue = font;

			dirty = true;
		}

		if ( AssignFallbackIcons( so ) )
			dirty = true;

		if ( dirty )
			so.ApplyModifiedPropertiesWithoutUndo();

		return dirty;
	}

	static bool AssignFallbackIcons( SerializedObject so )
	{
		bool dirty = false;
		dirty |= AssignSpriteIfEmpty( so, "discoveryFallbackIcon", DiscoveryIconPath );
		dirty |= AssignSpriteIfEmpty( so, "completionFallbackIcon", CompletionIconPath );
		dirty |= AssignSpriteIfEmpty( so, "unlockFallbackIcon", UnlockIconPath );
		dirty |= AssignSpriteIfEmpty( so, "milestoneFallbackIcon", MilestoneIconPath );
		return dirty;
	}

	static bool AssignSpriteIfEmpty( SerializedObject so, string propertyName, string spritePath )
	{
		SerializedProperty prop = so.FindProperty( propertyName );
		if ( prop == null )
			return false;
		if ( prop.objectReferenceValue != null )
			return false;

		Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>( spritePath );
		if ( sprite == null )
			return false;

		prop.objectReferenceValue = sprite;
		return true;
	}

	static bool SyncLayerRecursive( GameObject go, int layer )
	{
		bool changed = false;
		if ( go.layer != layer )
		{
			go.layer = layer;
			changed = true;
		}

		Transform transform = go.transform;
		for ( int i = 0; i < transform.childCount; i++ )
		{
			if ( SyncLayerRecursive( transform.GetChild( i ).gameObject, layer ) )
				changed = true;
		}

		return changed;
	}

	static bool AreSlotsWired( SerializedProperty slotsProp )
	{
		if ( slotsProp == null || !slotsProp.isArray || slotsProp.arraySize < ToastStackUI.MaxVisible )
			return false;

		for ( int i = 0; i < ToastStackUI.MaxVisible; i++ )
		{
			SerializedProperty element = slotsProp.GetArrayElementAtIndex( i );
			if ( element.FindPropertyRelative( "Slot" ).objectReferenceValue == null )
				return false;
			if ( element.FindPropertyRelative( "Content" ).objectReferenceValue == null )
				return false;
			if ( element.FindPropertyRelative( "Label" ).objectReferenceValue == null )
				return false;
		}

		return true;
	}

	static void WriteSlot( SerializedProperty element, ToastStackUI.ToastSlot binding )
	{
		element.FindPropertyRelative( "Slot" ).objectReferenceValue = binding.Slot;
		element.FindPropertyRelative( "Content" ).objectReferenceValue = binding.Content;
		element.FindPropertyRelative( "Group" ).objectReferenceValue = binding.Group;
		element.FindPropertyRelative( "Backdrop" ).objectReferenceValue = binding.Backdrop;
		element.FindPropertyRelative( "Outline" ).objectReferenceValue = binding.Outline;
		element.FindPropertyRelative( "Accent" ).objectReferenceValue = binding.Accent;
		element.FindPropertyRelative( "Icon" ).objectReferenceValue = binding.Icon;
		element.FindPropertyRelative( "Kicker" ).objectReferenceValue = binding.Kicker;
		element.FindPropertyRelative( "Label" ).objectReferenceValue = binding.Label;
		element.FindPropertyRelative( "Description" ).objectReferenceValue = binding.Description;
	}

	static void ClearExistingItems( Transform stack )
	{
		for ( int i = stack.childCount - 1; i >= 0; i-- )
		{
			Transform child = stack.GetChild( i );
			if ( child.name.StartsWith( "ToastItem_" ) )
				Object.DestroyImmediate( child.gameObject );
		}
	}

	static void ApplyDefaultRootRect( RectTransform root )
	{
		float height = ToastStackUI.MaxVisible * ToastStackUI.MaxSlotHeight() + ( ToastStackUI.MaxVisible - 1 ) * ToastStackUI.Spacing;
		root.anchorMin = new Vector2( 0.5f, 1f );
		root.anchorMax = new Vector2( 0.5f, 1f );
		root.pivot = new Vector2( 0.5f, 1f );
		root.anchoredPosition = new Vector2( 0f, -ToastStackUI.TopInset );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastStackUI.ToastWidth );
		root.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
		root.localScale = Vector3.one;
		root.localRotation = Quaternion.identity;
	}

	static ToastStackUI.ToastSlot CreateOrRefreshItem( Transform parent, int index )
	{
		string itemName = "ToastItem_" + index;
		Transform existing = parent.Find( itemName );
		GameObject slotGo = existing != null ? existing.gameObject : new GameObject( itemName, typeof( RectTransform ) );
		if ( existing == null )
			slotGo.transform.SetParent( parent, false );

		float height = ToastStackUI.HeightForTier( ToastStackUI.ToastTier.Discovery );
		RectTransform slot = slotGo.GetComponent<RectTransform>();
		slot.anchorMin = new Vector2( 0.5f, 1f );
		slot.anchorMax = new Vector2( 0.5f, 1f );
		slot.pivot = new Vector2( 0.5f, 1f );
		slot.anchoredPosition = new Vector2( 0f, -index * ( height + ToastStackUI.Spacing ) );
		slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastStackUI.ToastWidth );
		slot.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );
		slot.localScale = Vector3.one;
		slot.localRotation = Quaternion.identity;

		Transform contentTf = slot.Find( "Content" );
		GameObject contentGo = contentTf != null
			? contentTf.gameObject
			: new GameObject( "Content", typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ), typeof( CanvasGroup ), typeof( Outline ) );
		if ( contentTf == null )
			contentGo.transform.SetParent( slot, false );

		RectTransform content = contentGo.GetComponent<RectTransform>();
		content.anchorMin = new Vector2( 0.5f, 1f );
		content.anchorMax = new Vector2( 0.5f, 1f );
		content.pivot = new Vector2( 0.5f, 1f );
		content.anchoredPosition = Vector2.zero;
		content.SetSizeWithCurrentAnchors( RectTransform.Axis.Horizontal, ToastStackUI.ToastWidth );
		content.SetSizeWithCurrentAnchors( RectTransform.Axis.Vertical, height );

		Image backdrop = contentGo.GetComponent<Image>();
		if ( backdrop == null )
			backdrop = contentGo.AddComponent<Image>();
		backdrop.color = DiscoveryBackdrop;
		backdrop.raycastTarget = false;

		Outline outline = contentGo.GetComponent<Outline>();
		if ( outline == null )
			outline = contentGo.AddComponent<Outline>();
		outline.effectColor = GoldOutline;
		outline.effectDistance = new Vector2( 2f, 2f );
		outline.useGraphicAlpha = true;

		CanvasGroup group = contentGo.GetComponent<CanvasGroup>();
		if ( group == null )
			group = contentGo.AddComponent<CanvasGroup>();
		group.blocksRaycasts = false;
		group.interactable = false;
		group.alpha = 0f;

		Image accent = EnsureImageChild( content, "Accent", new Vector2( 6f, 0f ), CoinAccent, stretchVertical: true );
		Image icon = EnsureImageChild( content, "Icon", new Vector2( 48f, 48f ), Color.white, stretchVertical: false );
		icon.preserveAspect = true;
		icon.enabled = false;
		icon.gameObject.SetActive( false );

		RectTransform iconRect = icon.transform as RectTransform;
		iconRect.anchorMin = new Vector2( 0f, 0.5f );
		iconRect.anchorMax = new Vector2( 0f, 0.5f );
		iconRect.pivot = new Vector2( 0f, 0.5f );
		iconRect.anchoredPosition = new Vector2( 18f, 0f );

		Text kicker = EnsureTextChild( content, "Kicker", 16, FontStyle.Bold, GoldKicker );
		kicker.gameObject.SetActive( false );
		Text label = EnsureTextChild( content, "Label", 26, FontStyle.Bold, Color.white );
		Text description = EnsureTextChild( content, "Description", 16, FontStyle.Normal, DescriptionColor );
		description.horizontalOverflow = HorizontalWrapMode.Wrap;
		description.verticalOverflow = VerticalWrapMode.Truncate;
		description.gameObject.SetActive( false );

		return new ToastStackUI.ToastSlot
		{
			Slot = slot,
			Content = content,
			Group = group,
			Backdrop = backdrop,
			Outline = outline,
			Accent = accent,
			Icon = icon,
			Kicker = kicker,
			Label = label,
			Description = description
		};
	}

	static Image EnsureImageChild( RectTransform parent, string childName, Vector2 size, Color color, bool stretchVertical )
	{
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Image ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Image image = go.GetComponent<Image>();
		if ( image == null )
			image = go.AddComponent<Image>();
		image.color = color;
		image.raycastTarget = false;

		RectTransform rect = go.GetComponent<RectTransform>();
		if ( stretchVertical )
		{
			rect.anchorMin = new Vector2( 0f, 0f );
			rect.anchorMax = new Vector2( 0f, 1f );
			rect.pivot = new Vector2( 0f, 0.5f );
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = new Vector2( size.x, 0f );
		}
		else
		{
			rect.anchorMin = new Vector2( 0f, 0.5f );
			rect.anchorMax = new Vector2( 0f, 0.5f );
			rect.pivot = new Vector2( 0f, 0.5f );
			rect.sizeDelta = size;
		}

		return image;
	}

	static Text EnsureTextChild( RectTransform parent, string childName, int fontSize, FontStyle style, Color color )
	{
		Transform existing = parent.Find( childName );
		GameObject go = existing != null ? existing.gameObject : new GameObject( childName, typeof( RectTransform ), typeof( CanvasRenderer ), typeof( Text ) );
		if ( existing == null )
			go.transform.SetParent( parent, false );

		Text text = go.GetComponent<Text>();
		if ( text == null )
			text = go.AddComponent<Text>();

		Font font = AssetDatabase.LoadAssetAtPath<Font>( FontPath );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "LegacyRuntime.ttf" );
		if ( font == null )
			font = Resources.GetBuiltinResource<Font>( "Arial.ttf" );

		text.font = font;
		text.fontSize = fontSize;
		text.fontStyle = style;
		text.color = color;
		text.horizontalOverflow = HorizontalWrapMode.Overflow;
		text.verticalOverflow = VerticalWrapMode.Overflow;
		text.raycastTarget = false;
		text.supportRichText = false;
		text.alignment = TextAnchor.MiddleLeft;

		RectTransform rect = text.rectTransform;
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = new Vector2( 22f, 8f );
		rect.offsetMax = new Vector2( -16f, -8f );

		return text;
	}
}
#endif

