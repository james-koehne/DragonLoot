using System.Collections.Generic;

using UnityEditor;

using UnityEngine;

/// <summary>
/// Bakes colour-agnostic imperfect coin chunk meshes (XZ offsets only) into
/// <see cref="CoinStackVisualDefinition"/>. XZ range comes from the definition's
/// imperfect bake min/max radius fractions.
/// </summary>
public static class CoinStackImperfectBaker
{
	const string DefinitionPath = "Assets/Definitions/CoinStackVisualDefinition.asset";
	const string OutputFolder = "Assets/Art/CoinStack/Imperfect";
	static readonly int[] ChunkSizes = { 8, 16, 32 };

	[MenuItem( "DragonLoot/Coin Stack/Rebuild Imperfect Chunks" )]
	public static void RebuildImperfectChunks()
	{
		CoinStackVisualDefinition def = AssetDatabase.LoadAssetAtPath<CoinStackVisualDefinition>( DefinitionPath );
		if ( def == null )
		{
			Debug.LogError( "CoinStackImperfectBaker: missing " + DefinitionPath );
			return;
		}

		if ( def.stackMesh == null )
		{
			Debug.LogError( "CoinStackImperfectBaker: CoinStackVisualDefinition.stackMesh is not assigned." );
			return;
		}

		EnsureFolder( OutputFolder );

		def.GetMeshReferenceSize( out float refDiameter, out float refHeight );
		float step = Mathf.Max( 0.0001f, refHeight );
		CoinStackImperfectLayout.ResolveXzRadiiMesh( def, out float minRadius, out float maxRadius );
		int variantCount = Mathf.Max( 1, def.imperfectVariantCount );

		DeleteExistingChunkMeshes( OutputFolder );

		List<CoinStackImperfectChunkEntry> entries = new List<CoinStackImperfectChunkEntry>( ChunkSizes.Length * variantCount );
		for ( int s = 0; s < ChunkSizes.Length; s++ )
		{
			int coinCount = ChunkSizes[ s ];
			for ( int seedIndex = 0; seedIndex < variantCount; seedIndex++ )
			{
				CoinStackImperfectChunkEntry entry = BakeChunk(
					def.stackMesh,
					coinCount,
					seedIndex,
					variantCount,
					step,
					minRadius,
					maxRadius,
					OutputFolder );
				if ( entry != null )
					entries.Add( entry );
			}
		}

		def.imperfectChunks = entries;
		EditorUtility.SetDirty( def );
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		Debug.Log(
			"CoinStackImperfectBaker: rebuilt " + entries.Count
			+ " imperfect chunks under " + OutputFolder
			+ " (XZ radius mesh units " + minRadius.ToString( "F4" )
			+ "–" + maxRadius.ToString( "F4" )
			+ ", diameter " + refDiameter.ToString( "F3" ) + ")" );
	}

	static CoinStackImperfectChunkEntry BakeChunk(
		Mesh sourceMesh,
		int coinCount,
		int seedIndex,
		int variantCount,
		float step,
		float minRadius,
		float maxRadius,
		string outputFolder )
	{
		Vector2[] xzOffsets = new Vector2[ coinCount ];
		CombineInstance[] combines = new CombineInstance[ coinCount ];
		float yBias = -sourceMesh.bounds.min.y;

		for ( int i = 0; i < coinCount; i++ )
		{
			// 8 = one leaf; 16 = two consecutive leaves; 32 = four — matches runtime LeafSeedIndex packing.
			Vector2 xz = CoinStackImperfectLayout.SampleHierarchicalChunkXz(
				seedIndex,
				i,
				coinCount,
				variantCount,
				minRadius,
				maxRadius );
			xzOffsets[ i ] = xz;

			Mesh coinMesh = Object.Instantiate( sourceMesh );
			coinMesh.name = "CoinBake_" + i;
			WriteCoinIndexUv2( coinMesh, i );

			combines[ i ] = new CombineInstance
			{
				mesh = coinMesh,
				transform = Matrix4x4.TRS(
					new Vector3( xz.x, i * step + yBias, xz.y ),
					Quaternion.identity,
					Vector3.one )
			};
		}

		Mesh combined = new Mesh
		{
			name = "ImperfectChunk_" + coinCount + "_s" + seedIndex
		};
		combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
		combined.CombineMeshes( combines, true, true, false );

		for ( int i = 0; i < combines.Length; i++ )
		{
			if ( combines[ i ].mesh != null )
				Object.DestroyImmediate( combines[ i ].mesh );
		}

		string assetPath = outputFolder + "/" + combined.name + ".asset";
		AssetDatabase.CreateAsset( combined, assetPath );

		return new CoinStackImperfectChunkEntry
		{
			coinCount = coinCount,
			seedIndex = seedIndex,
			mesh = combined,
			height = coinCount * step,
			localXzOffsets = xzOffsets
		};
	}

	static void WriteCoinIndexUv2( Mesh mesh, int coinIndex )
	{
		int vertCount = mesh.vertexCount;
		Vector2[] uv2 = new Vector2[ vertCount ];
		Vector2 value = new Vector2( coinIndex, 0f );
		for ( int i = 0; i < vertCount; i++ )
			uv2[ i ] = value;
		mesh.uv2 = uv2;
	}

	static void EnsureFolder( string folder )
	{
		if ( AssetDatabase.IsValidFolder( folder ) )
			return;

		string[] parts = folder.Split( '/' );
		string current = parts[ 0 ];
		for ( int i = 1; i < parts.Length; i++ )
		{
			string next = current + "/" + parts[ i ];
			if ( !AssetDatabase.IsValidFolder( next ) )
				AssetDatabase.CreateFolder( current, parts[ i ] );
			current = next;
		}
	}

	static void DeleteExistingChunkMeshes( string folder )
	{
		if ( !AssetDatabase.IsValidFolder( folder ) )
			return;

		string[] guids = AssetDatabase.FindAssets( "t:Mesh", new[] { folder } );
		for ( int i = 0; i < guids.Length; i++ )
		{
			string path = AssetDatabase.GUIDToAssetPath( guids[ i ] );
			if ( string.IsNullOrEmpty( path ) )
				continue;
			AssetDatabase.DeleteAsset( path );
		}
	}
}
