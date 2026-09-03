#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor( typeof( TreasurePileDefinition ) )]
public class TreasurePileDefinitionEditor : Editor
{
	public override void OnInspectorGUI()
	{
		TreasurePileDefinition definition = (TreasurePileDefinition)target;

		EditorGUILayout.LabelField( "Contents", EditorStyles.boldLabel );
		DrawContentsSummary( definition );

		if ( GUILayout.Button( "Open Contents Editor" ) )
			TreasurePileContentsWindow.Open( definition );

		EditorGUILayout.Space( 8f );
		DrawDefaultInspector();
	}

	static void DrawContentsSummary( TreasurePileDefinition definition )
	{
		int coinUnits = definition.TotalCoinUnits();
		int treasureUnits = definition.TotalTreasureUnits();
		long totalValue = SumValue( definition.coinContents ) + SumValue( definition.treasureContents );

		EditorGUILayout.LabelField( "Coin mix weight", coinUnits.ToString( "N0" ) );
		EditorGUILayout.LabelField( "Treasure units", treasureUnits.ToString( "N0" ) );
		EditorGUILayout.LabelField( "Total value", totalValue.ToString( "N0" ) );

		int coinRows = definition.coinContents != null ? definition.coinContents.Length : 0;
		int treasureRows = definition.treasureContents != null ? definition.treasureContents.Length : 0;
		EditorGUILayout.LabelField( "Rows", $"coins {coinRows}  treasure {treasureRows}" );

		EditorGUILayout.HelpBox(
			"Use Pile Contents for bulk scale, mix edits, and catalog add. Coin counts are mix weights, not drawn GPU seats.",
			MessageType.None );
	}

	static long SumValue( TreasurePileEntry[] entries )
	{
		if ( entries == null )
			return 0;

		long total = 0;
		for ( int i = 0; i < entries.Length; i++ )
		{
			TreasurePileEntry entry = entries[ i ];
			if ( entry.treasure != null && entry.count > 0 )
				total += ( long )entry.treasure.value * entry.count;
		}

		return total;
	}
}
#endif
