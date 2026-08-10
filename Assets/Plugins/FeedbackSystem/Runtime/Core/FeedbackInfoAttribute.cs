using System;

namespace FeedbackSystem
{
	[AttributeUsage( AttributeTargets.Class, Inherited = false )]
	public sealed class FeedbackInfoAttribute : Attribute
	{
		public readonly string Path;
		public readonly string Category;
		public readonly string DisplayName;

		public FeedbackInfoAttribute( string path )
		{
			if ( string.IsNullOrEmpty( path ) )
			{
				Path = "Misc/Feedback";
				Category = "Misc";
				DisplayName = "Feedback";
				return;
			}

			Path = path;
			int slash = path.LastIndexOf( '/' );
			if ( slash <= 0 || slash >= path.Length - 1 )
			{
				Category = "Misc";
				DisplayName = path;
				return;
			}

			Category = path.Substring( 0, slash );
			DisplayName = path.Substring( slash + 1 );
		}
	}
}
