#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

static class LightFlickerBatchTools
{
	[MenuItem( DragonLootMenus.GraphicsLightFlickerAddToSelection, priority = 212 )]
	static void AddToSelection()
	{
		GameObject[] selection = Selection.gameObjects;
		if ( selection == null || selection.Length == 0 )
			return;

		int added = 0;
		for ( int i = 0; i < selection.Length; i++ )
		{
			GameObject go = selection[ i ];
			if ( go == null )
				continue;

			Light light = go.GetComponent<Light>();
			if ( light == null )
				light = go.GetComponentInChildren<Light>( true );
			if ( light == null )
				continue;

			LightFlicker flicker = light.GetComponent<LightFlicker>();
			if ( flicker == null )
				flicker = light.gameObject.GetComponent<LightFlicker>();
			if ( flicker == null )
			{
				Undo.AddComponent<LightFlicker>( light.gameObject );
				flicker = light.GetComponent<LightFlicker>();
				added++;
			}

			if ( flicker == null )
				continue;

			Undo.RecordObject( flicker, "Add Light Flicker" );
			SerializedObject so = new SerializedObject( flicker );
			SerializedProperty lightProp = so.FindProperty( "_light" );
			if ( lightProp != null )
			{
				lightProp.objectReferenceValue = light;
				so.ApplyModifiedProperties();
			}

			flicker.RecaptureLightBase();
			flicker.RefreshEditorPreview();
		}

		Debug.Log( "LightFlickerBatchTools: configured " + added + " new LightFlicker component(s)." );
	}

	[MenuItem( DragonLootMenus.GraphicsLightFlickerAddToSelection, validate = true )]
	static bool ValidateAddToSelection()
	{
		GameObject[] selection = Selection.gameObjects;
		if ( selection == null || selection.Length == 0 )
			return false;

		for ( int i = 0; i < selection.Length; i++ )
		{
			GameObject go = selection[ i ];
			if ( go == null )
				continue;

			Light light = go.GetComponent<Light>();
			if ( light == null )
				light = go.GetComponentInChildren<Light>( true );
			if ( light != null )
				return true;
		}

		return false;
	}

	[MenuItem( DragonLootMenus.GraphicsLightFlickerRecaptureSelection, priority = 230 )]
	static void RecaptureBasesInSelection()
	{
		LightFlicker[] flickers = CollectFlickersInSelection();
		for ( int i = 0; i < flickers.Length; i++ )
		{
			Undo.RecordObject( flickers[ i ], "Recapture Light Base" );
			flickers[ i ].RecaptureLightBase();
			flickers[ i ].RefreshEditorPreview();
		}

		Debug.Log( "LightFlickerBatchTools: recaptured " + flickers.Length + " LightFlicker base(s)." );
	}

	[MenuItem( DragonLootMenus.GraphicsLightFlickerRecaptureSelection, validate = true )]
	static bool ValidateRecaptureBasesInSelection() => CollectFlickersInSelection().Length > 0;

	[MenuItem( DragonLootMenus.GraphicsLightFlickerRefreshPreviews, priority = 231 )]
	static void RefreshEditPreviewsInOpenScenes()
	{
		LightFlickerEditorPreviewGuard.RefreshAllEditPreviews();
		Debug.Log( "LightFlickerBatchTools: refreshed edit-mode previews in open scenes." );
	}

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Lantern", priority = 220 )]
	static void ApplyPresetLantern() => ApplyPresetToSelection( LightFlickerPresetKind.Lantern );

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Flame", priority = 221 )]
	static void ApplyPresetFlame() => ApplyPresetToSelection( LightFlickerPresetKind.Flame );

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Torch", priority = 222 )]
	static void ApplyPresetTorch() => ApplyPresetToSelection( LightFlickerPresetKind.Torch );

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Candle", priority = 223 )]
	static void ApplyPresetCandle() => ApplyPresetToSelection( LightFlickerPresetKind.Candle );

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Campfire", priority = 224 )]
	static void ApplyPresetCampfire() => ApplyPresetToSelection( LightFlickerPresetKind.Campfire );

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Magical", priority = 225 )]
	static void ApplyPresetMagical() => ApplyPresetToSelection( LightFlickerPresetKind.Magical );

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Ember", priority = 226 )]
	static void ApplyPresetEmber() => ApplyPresetToSelection( LightFlickerPresetKind.Ember );

	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Lantern", validate = true )]
	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Flame", validate = true )]
	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Torch", validate = true )]
	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Candle", validate = true )]
	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Campfire", validate = true )]
	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Magical", validate = true )]
	[MenuItem( DragonLootMenus.GraphicsLightFlickerApplyPreset + "/Ember", validate = true )]
	static bool ValidateApplyPreset() => CollectFlickersInSelection().Length > 0;

	static void ApplyPresetToSelection( LightFlickerPresetKind kind )
	{
		LightFlicker[] flickers = CollectFlickersInSelection();
		for ( int i = 0; i < flickers.Length; i++ )
		{
			Undo.RecordObject( flickers[ i ], "Apply Light Flicker Preset" );
			flickers[ i ].SetPreset( kind );
			flickers[ i ].RefreshEditorPreview();
		}

		Debug.Log( "LightFlickerBatchTools: applied preset " + kind + " to " + flickers.Length + " LightFlicker(s)." );
	}

	static LightFlicker[] CollectFlickersInSelection()
	{
		GameObject[] selection = Selection.gameObjects;
		if ( selection == null || selection.Length == 0 )
			return System.Array.Empty<LightFlicker>();

		System.Collections.Generic.List<LightFlicker> flickers = new System.Collections.Generic.List<LightFlicker>();
		for ( int i = 0; i < selection.Length; i++ )
		{
			GameObject go = selection[ i ];
			if ( go == null )
				continue;

			LightFlicker onRoot = go.GetComponent<LightFlicker>();
			if ( onRoot != null && !flickers.Contains( onRoot ) )
				flickers.Add( onRoot );

			LightFlicker[] found = go.GetComponentsInChildren<LightFlicker>( true );
			for ( int f = 0; f < found.Length; f++ )
			{
				if ( found[ f ] != null && !flickers.Contains( found[ f ] ) )
					flickers.Add( found[ f ] );
			}
		}

		return flickers.ToArray();
	}
}
#endif
