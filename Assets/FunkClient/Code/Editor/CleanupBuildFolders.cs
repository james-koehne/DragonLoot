using System.IO;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public class CleanupBuildFolders : IPostprocessBuildWithReport
{
	public bool cleanupBuildFolders = true;
	public int callbackOrder => 0;

	public void OnPostprocessBuild( BuildReport report )
	{
		if ( !cleanupBuildFolders )
			return;

		string buildDir = Path.GetDirectoryName( report.summary.outputPath );

		foreach ( var dir in Directory.GetDirectories( buildDir ) )
		{
			if ( dir.Contains( "DoNotShip" ) || dir.Contains( "DontShip" ) )
			{
				Directory.Delete( dir, true );
				UnityEngine.Debug.Log( $"Deleted debug folder: {dir}" );
			}
		}
	}
}