using UnityEditor;

using UnityEngine;

/// <summary>
/// Rebuilds <c>TA_CoinStack_Albedo</c> / Normal / Mask Texture2DArrays from the
/// gold, silver, and copper stack materials on <see cref="CoinStackVisualDefinition"/>.
/// Slice indices match the definition's gold/silver/copper type indices.
/// </summary>
public static class CoinStackTextureArrayBaker
{
	const string DefinitionPath = "Assets/Definitions/CoinStackVisualDefinition.asset";
	const string OutputFolder = "Assets/Materials/Shaders/Coin";
	const string AlbedoPath = OutputFolder + "/TA_CoinStack_Albedo.asset";
	const string NormalPath = OutputFolder + "/TA_CoinStack_Normal.asset";
	const string MaskPath = OutputFolder + "/TA_CoinStack_Mask.asset";

	const string BaseMapProp = "_BaseMap";
	const string BumpMapProp = "_BumpMap";
	const string MaskMapProp = "_MetallicGlossMap";
	const string AlbedoArrayProp = "_BaseMapArray";
	const string BumpArrayProp = "_BumpMapArray";
	const string MaskArrayProp = "_MetallicGlossMapArray";

	[MenuItem( DragonLootMenus.CoinStackRebuildTextureArrays )]
	public static void RebuildTextureArrays()
	{
		CoinStackVisualDefinition def = AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>( DefinitionPath );
		if ( def == null )
		{
			Debug.LogError( "CoinStackTextureArrayBaker: missing " + DefinitionPath );
			return;
		}

		if ( def.goldStackMaterial == null || def.silverStackMaterial == null || def.copperStackMaterial == null )
		{
			Debug.LogError( "CoinStackTextureArrayBaker: gold/silver/copper stack materials must be assigned on CoinStackVisualDefinition." );
			return;
		}

		int depth = def.typeCount > 0
			? Mathf.Clamp( def.typeCount, 1, CoinStackVisualDefinition.MaxTypeSlots )
			: 3;

		Texture2D[] albedoSlices = new Texture2D[ depth ];
		Texture2D[] normalSlices = new Texture2D[ depth ];
		Texture2D[] maskSlices = new Texture2D[ depth ];
		if ( !TryCollectSlices( def, depth, albedoSlices, normalSlices, maskSlices ) )
			return;

		int size = ResolveArraySize( albedoSlices, normalSlices, maskSlices );
		if ( size < 4 )
		{
			Debug.LogError( "CoinStackTextureArrayBaker: could not resolve a valid array size from stack textures." );
			return;
		}

		Texture2DArray albedo = BuildArray( albedoSlices, size, linear: false );
		Texture2DArray normal = BuildArray( normalSlices, size, linear: true );
		Texture2DArray mask = BuildArray( maskSlices, size, linear: true );
		if ( albedo == null || normal == null || mask == null )
		{
			DestroyTemp( albedo );
			DestroyTemp( normal );
			DestroyTemp( mask );
			Debug.LogError( "CoinStackTextureArrayBaker: failed to build one or more texture arrays." );
			return;
		}

		albedo = WriteArrayAsset( albedo, AlbedoPath, "TA_CoinStack_Albedo" );
		normal = WriteArrayAsset( normal, NormalPath, "TA_CoinStack_Normal" );
		mask = WriteArrayAsset( mask, MaskPath, "TA_CoinStack_Mask" );

		Material multi = def.multiStackMaterial;
		if ( multi != null )
		{
			if ( multi.HasProperty( AlbedoArrayProp ) )
				multi.SetTexture( AlbedoArrayProp, albedo );
			if ( multi.HasProperty( BumpArrayProp ) )
				multi.SetTexture( BumpArrayProp, normal );
			if ( multi.HasProperty( MaskArrayProp ) )
				multi.SetTexture( MaskArrayProp, mask );
			if ( multi.HasProperty( "_TypeCount" ) )
				multi.SetFloat( "_TypeCount", depth );
			EditorUtility.SetDirty( multi );
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log(
			"CoinStackTextureArrayBaker: rebuilt " + size + "x" + size + " x " + depth
			+ " arrays (Gold=" + def.goldTypeIndex
			+ " Silver=" + def.silverTypeIndex
			+ " Copper=" + def.copperTypeIndex
			+ ") from stack materials into " + OutputFolder );
	}

	static bool TryCollectSlices(
		CoinStackVisualDefinition def,
		int depth,
		Texture2D[] albedo,
		Texture2D[] normal,
		Texture2D[] mask )
	{
		if ( !TryAssignType( def.goldStackMaterial, "gold", def.goldTypeIndex, depth, albedo, normal, mask ) )
			return false;
		if ( !TryAssignType( def.silverStackMaterial, "silver", def.silverTypeIndex, depth, albedo, normal, mask ) )
			return false;
		if ( !TryAssignType( def.copperStackMaterial, "copper", def.copperTypeIndex, depth, albedo, normal, mask ) )
			return false;

		Texture2D albedoFill = FirstNonNull( albedo );
		Texture2D normalFill = FirstNonNull( normal );
		Texture2D maskFill = FirstNonNull( mask );
		for ( int i = 0; i < depth; i++ )
		{
			if ( albedo[ i ] == null )
				albedo[ i ] = albedoFill;
			if ( normal[ i ] == null )
				normal[ i ] = normalFill;
			if ( mask[ i ] == null )
				mask[ i ] = maskFill;
			if ( albedo[ i ] == null || normal[ i ] == null || mask[ i ] == null )
			{
				Debug.LogError( "CoinStackTextureArrayBaker: missing textures for array slice " + i + "." );
				return false;
			}
		}

		return true;
	}

	static bool TryAssignType(
		Material material,
		string typeName,
		int typeIndex,
		int depth,
		Texture2D[] albedo,
		Texture2D[] normal,
		Texture2D[] mask )
	{
		int index = Mathf.Clamp( typeIndex, 0, depth - 1 );
		Texture2D albedoTex = GetTexture2D( material, BaseMapProp );
		Texture2D normalTex = GetTexture2D( material, BumpMapProp );
		Texture2D maskTex = GetTexture2D( material, MaskMapProp );
		if ( albedoTex == null || normalTex == null || maskTex == null )
		{
			Debug.LogError(
				"CoinStackTextureArrayBaker: " + typeName
				+ " stack material is missing _BaseMap, _BumpMap, or _MetallicGlossMap." );
			return false;
		}

		albedo[ index ] = albedoTex;
		normal[ index ] = normalTex;
		mask[ index ] = maskTex;
		return true;
	}

	static Texture2D GetTexture2D( Material material, string property )
	{
		if ( material == null || !material.HasProperty( property ) )
			return null;

		Texture tex = material.GetTexture( property );
		return tex as Texture2D;
	}

	static Texture2D FirstNonNull( Texture2D[] slices )
	{
		for ( int i = 0; i < slices.Length; i++ )
		{
			if ( slices[ i ] != null )
				return slices[ i ];
		}

		return null;
	}

	static int ResolveArraySize( params Texture2D[][] groups )
	{
		int size = 0;
		for ( int g = 0; g < groups.Length; g++ )
		{
			Texture2D[] slices = groups[ g ];
			for ( int i = 0; i < slices.Length; i++ )
			{
				Texture2D slice = slices[ i ];
				if ( slice == null )
					continue;
				size = Mathf.Max( size, slice.width );
				size = Mathf.Max( size, slice.height );
			}
		}

		if ( size < 4 )
			return 0;

		size = Mathf.ClosestPowerOfTwo( size );
		return Mathf.Max( 4, size );
	}

	static Texture2DArray BuildArray( Texture2D[] slices, int size, bool linear )
	{
		if ( slices == null || slices.Length == 0 )
			return null;

		Texture2DArray array = new Texture2DArray( size, size, slices.Length, TextureFormat.RGBA32, true, linear );
		array.filterMode = FilterMode.Bilinear;
		array.wrapMode = TextureWrapMode.Repeat;
		array.anisoLevel = 1;
		array.ignoreMipmapLimit = true;

		for ( int i = 0; i < slices.Length; i++ )
		{
			Texture2D readable = BlitToReadable( slices[ i ], size, linear );
			if ( readable == null )
			{
				DestroyTemp( array );
				return null;
			}

			array.SetPixels( readable.GetPixels(), i, 0 );
			Object.DestroyImmediate( readable );
		}

		array.Apply( true, false );
		return array;
	}

	static Texture2D BlitToReadable( Texture source, int size, bool linear )
	{
		if ( source == null )
			return null;

		RenderTextureReadWrite readWrite = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
		RenderTexture rt = RenderTexture.GetTemporary( size, size, 0, RenderTextureFormat.ARGB32, readWrite );
		RenderTexture previous = RenderTexture.active;
		Graphics.Blit( source, rt );
		RenderTexture.active = rt;

		Texture2D readable = new Texture2D( size, size, TextureFormat.RGBA32, false, linear );
		readable.ReadPixels( new Rect( 0f, 0f, size, size ), 0, 0, false );
		readable.Apply( false, false );

		RenderTexture.active = previous;
		RenderTexture.ReleaseTemporary( rt );
		return readable;
	}

	static Texture2DArray WriteArrayAsset( Texture2DArray built, string path, string name )
	{
		built.name = name;
		Texture2DArray existing = AssetDatabase.LoadAssetAtPath<Texture2DArray>( path );
		if ( existing != null )
		{
			EditorUtility.CopySerialized( built, existing );
			existing.name = name;
			Object.DestroyImmediate( built );
			EditorUtility.SetDirty( existing );
			return existing;
		}

		AssetDatabase.CreateAsset( built, path );
		return built;
	}

	static void DestroyTemp( Object obj )
	{
		if ( obj != null )
			Object.DestroyImmediate( obj );
	}
}
