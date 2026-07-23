using UnityEngine;
using UnityEditor;
using System;
using System.IO;

[InitializeOnLoad]
public static class FunkScreenshot
{
	[MenuItem( "Funk/Take Screenshot %#F10" )]
	public static void TakeScreenshot()
	{
		string folder = Path.Combine( Application.dataPath, "../Screenshots" );
		if ( !Directory.Exists( folder ) )
			Directory.CreateDirectory( folder );

		string fileName = $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
		string fullPath = Path.Combine( folder, fileName );

		ScreenCapture.CaptureScreenshot( fullPath );
		Debug.Log( $"Screenshot saved to: {fullPath}" );
	}
}
