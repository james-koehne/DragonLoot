using UnityEngine;

/// <summary>
/// Shared horizontal slot-grid math and scene gizmos for mixed / typed display tables.
/// </summary>
public static class DisplayTableSlotLayout
{
	public static int SlotCount( int rows, int columns )
	{
		return Mathf.Max( 1, rows ) * Mathf.Max( 1, columns );
	}

	/// <summary>
	/// Slot centers on a single horizontal plane (local XZ). Row/column index never changes base Y.
	/// </summary>
	public static Vector3 GetSlotLocalPosition(
		int index,
		int rows,
		int columns,
		float slotSpacing,
		float margin )
	{
		rows = Mathf.Max( 1, rows );
		columns = Mathf.Max( 1, columns );
		slotSpacing = Mathf.Max( 0.01f, slotSpacing );
		margin = Mathf.Max( 0f, margin );

		int row = index / columns;
		int col = index % columns;

		float width = ( columns - 1 ) * slotSpacing;
		float depth = ( rows - 1 ) * slotSpacing;
		float startX = -width * 0.5f;
		float startZ = depth * 0.5f;

		float x = startX + col * slotSpacing;
		float z = startZ - row * slotSpacing;
		return new Vector3( x, margin, z );
	}

	public static void DrawLayoutGizmos(
		Transform area,
		int rows,
		int columns,
		float slotSpacing,
		float margin,
		Color slotColor,
		Color gridColor )
	{
		if ( area == null )
			return;

		rows = Mathf.Max( 1, rows );
		columns = Mathf.Max( 1, columns );
		slotSpacing = Mathf.Max( 0.01f, slotSpacing );

		int count = rows * columns;
		float markerRadius = Mathf.Clamp( slotSpacing * 0.18f, 0.02f, 0.08f );

		Gizmos.color = slotColor;
		for ( int i = 0; i < count; i++ )
		{
			Vector3 local = GetSlotLocalPosition( i, rows, columns, slotSpacing, margin );
			Vector3 world = area.TransformPoint( local );
			Gizmos.DrawWireSphere( world, markerRadius );
			Gizmos.DrawLine( world, world + area.up * ( markerRadius * 2f ) );
		}

		// Grid lines across rows / columns.
		Gizmos.color = gridColor;
		for ( int r = 0; r < rows; r++ )
		{
			Vector3 a = area.TransformPoint( GetSlotLocalPosition( r * columns, rows, columns, slotSpacing, margin ) );
			Vector3 b = area.TransformPoint( GetSlotLocalPosition( r * columns + ( columns - 1 ), rows, columns, slotSpacing, margin ) );
			Gizmos.DrawLine( a, b );
		}

		for ( int c = 0; c < columns; c++ )
		{
			Vector3 a = area.TransformPoint( GetSlotLocalPosition( c, rows, columns, slotSpacing, margin ) );
			Vector3 b = area.TransformPoint( GetSlotLocalPosition( ( rows - 1 ) * columns + c, rows, columns, slotSpacing, margin ) );
			Gizmos.DrawLine( a, b );
		}
	}
}
