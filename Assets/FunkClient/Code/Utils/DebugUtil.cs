using UnityEngine;

public static class DebugUtil
{
	public static void Log( Color color, string message )
	{
		string hexColor = ColorUtility.ToHtmlStringRGB( color );
		Debug.Log( $"<color=#{hexColor}>{message}</color>" );
	}

	public static void LogWarning( Color color, string message )
	{
		string hexColor = ColorUtility.ToHtmlStringRGB( color );
		Debug.LogWarning( $"<color=#{hexColor}>{message}</color>" );
	}

	public static void LogError( Color color, string message )
	{
		string hexColor = ColorUtility.ToHtmlStringRGB( color );
		Debug.LogError( $"<color=#{hexColor}>{message}</color>" );
	}
}
