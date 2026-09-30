#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps EnvironmentLit surface/blend render state in sync with URP Lit conventions
/// (<c>_Surface</c>, <c>_Blend</c>, queue, keywords) while drawing the default inspector.
/// </summary>
public class EnvironmentLitShaderGUI : ShaderGUI
{
	public override void OnGUI( MaterialEditor materialEditor, MaterialProperty[] properties )
	{
		if ( materialEditor == null )
			return;

		EditorGUI.BeginChangeCheck();
		materialEditor.PropertiesDefaultGUI( properties );
		if ( !EditorGUI.EndChangeCheck() )
			return;

		foreach ( Object target in materialEditor.targets )
		{
			Material material = target as Material;
			if ( material == null )
				continue;

			SyncSurfaceState( material );
			EditorUtility.SetDirty( material );
		}
	}

	public override void AssignNewShaderToMaterial( Material material, Shader oldShader, Shader newShader )
	{
		base.AssignNewShaderToMaterial( material, oldShader, newShader );
		if ( material != null )
			SyncSurfaceState( material );
	}

	public override void ValidateMaterial( Material material )
	{
		base.ValidateMaterial( material );
		if ( material != null )
			SyncSurfaceState( material );
	}

	static void SyncSurfaceState( Material material )
	{
		if ( material == null )
			return;

		// Property names match URP Lit (_Surface, _Blend, _SrcBlend, _ZWrite, ...).
		BaseShaderGUI.SetupMaterialBlendMode( material );
	}
}
#endif
