using UnityEngine;

public static class RectTransformExtentions
{
	public static void SetLeft( this RectTransform rt, float left )
	{
		rt.offsetMin = new Vector2( left, rt.offsetMin.y );
	}

	public static void SetRight( this RectTransform rt, float right )
	{
		rt.offsetMax = new Vector2( -right, rt.offsetMax.y );
	}

	public static void SetTop( this RectTransform rt, float top )
	{
		rt.offsetMax = new Vector2( rt.offsetMax.x, -top );
	}

	public static void SetBottom( this RectTransform rt, float bottom )
	{
		rt.offsetMin = new Vector2( rt.offsetMin.x, bottom );
	}

	public static void SetAnchoredPositionX( this RectTransform rt, float x )
	{
		rt.anchoredPosition = new Vector2( x, rt.anchoredPosition.y );
	}

	public static void SetAnchoredPositionY( this RectTransform rt, float y )
	{
		rt.anchoredPosition = new Vector2( rt.anchoredPosition.x, y );
	}

	public static void SetSize( this RectTransform rt, Vector2 size )
	{
		Vector2 oldSize = rt.rect.size;
		Vector2 delta = size - oldSize;
		rt.offsetMin -= new Vector2( delta.x * rt.pivot.x, delta.y * rt.pivot.y );
		rt.offsetMax += new Vector2( delta.x * ( 1f - rt.pivot.x ), delta.y * ( 1f - rt.pivot.y ) );
	}

	public static void SetWidth( this RectTransform rt, float width )
	{
		rt.SetSize( new Vector2( width, rt.rect.size.y ) );
	}

	public static void SetHeight( this RectTransform rt, float height )
	{
		rt.SetSize( new Vector2( rt.rect.size.x, height ) );
	}

	public static void SetAnchors( this RectTransform rt, Vector2 min, Vector2 max )
	{
		rt.anchorMin = min;
		rt.anchorMax = max;
	}

	public static void SetAnchorCenter( this RectTransform rt )
	{
		rt.anchorMin = rt.anchorMax = new Vector2( 0.5f, 0.5f );
	}

	public static void SetAnchorStretchHorizontal( this RectTransform rt )
	{
		rt.anchorMin = new Vector2( 0f, rt.anchorMin.y );
		rt.anchorMax = new Vector2( 1f, rt.anchorMax.y );
	}

	public static void SetAnchorStretchVertical( this RectTransform rt )
	{
		rt.anchorMin = new Vector2( rt.anchorMin.x, 0f );
		rt.anchorMax = new Vector2( rt.anchorMax.x, 1f );
	}

	public static void ResetOffsets( this RectTransform rt )
	{
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
	}

	public static void ResetAnchorsAndOffsets( this RectTransform rt )
	{
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
	}

	public static void SetPivot( this RectTransform rt, Vector2 pivot )
	{
		Vector2 delta = pivot - rt.pivot;
		Vector3 deltaPos = new Vector3( delta.x * rt.rect.width, delta.y * rt.rect.height );
		rt.pivot = pivot;
		rt.localPosition += deltaPos;
	}

	public static void CopyFrom( this RectTransform rt, RectTransform source )
	{
		rt.anchorMin = source.anchorMin;
		rt.anchorMax = source.anchorMax;
		rt.pivot = source.pivot;
		rt.offsetMin = source.offsetMin;
		rt.offsetMax = source.offsetMax;
		rt.localScale = source.localScale;
		rt.localRotation = source.localRotation;
	}
}