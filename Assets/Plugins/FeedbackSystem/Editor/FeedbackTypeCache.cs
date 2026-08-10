using System;
using System.Collections.Generic;

using UnityEditor;

using FeedbackSystem;

namespace FeedbackSystem.Editor
{
	public static class FeedbackTypeCache
	{
		public sealed class Entry
		{
			public Type Type;
			public string Path;
			public string Category;
			public string DisplayName;
		}

		static Entry[] _entries;

		public static Entry[] GetEntries()
		{
			if ( _entries != null )
				return _entries;

			TypeCache.TypeCollection types = TypeCache.GetTypesDerivedFrom<Feedback>();
			List<Entry> list = new List<Entry>( types.Count );
			for ( int i = 0; i < types.Count; i++ )
			{
				Type type = types[i];
				if ( type.IsAbstract || type.IsGenericTypeDefinition )
					continue;

				Entry entry = new Entry();
				entry.Type = type;
				FeedbackInfoAttribute info = GetInfo( type );
				if ( info != null )
				{
					entry.Path = info.Path;
					entry.Category = info.Category;
					entry.DisplayName = info.DisplayName;
				}
				else
				{
					entry.DisplayName = Nicify( type.Name );
					entry.Category = "Misc";
					entry.Path = entry.Category + "/" + entry.DisplayName;
				}

				list.Add( entry );
			}

			list.Sort( CompareEntries );
			_entries = list.ToArray();
			return _entries;
		}

		public static string GetDisplayName( Type type )
		{
			if ( type == null )
				return "Feedback";

			Entry[] entries = GetEntries();
			for ( int i = 0; i < entries.Length; i++ )
			{
				if ( entries[i].Type == type )
					return entries[i].DisplayName;
			}

			FeedbackInfoAttribute info = GetInfo( type );
			if ( info != null )
				return info.DisplayName;

			return Nicify( type.Name );
		}

		static FeedbackInfoAttribute GetInfo( Type type )
		{
			object[] attributes = type.GetCustomAttributes( typeof( FeedbackInfoAttribute ), false );
			if ( attributes == null || attributes.Length == 0 )
				return null;

			return attributes[0] as FeedbackInfoAttribute;
		}

		static int CompareEntries( Entry a, Entry b )
		{
			int category = string.CompareOrdinal( a.Category, b.Category );
			if ( category != 0 )
				return category;

			return string.CompareOrdinal( a.DisplayName, b.DisplayName );
		}

		static string Nicify( string typeName )
		{
			if ( string.IsNullOrEmpty( typeName ) )
				return "Feedback";

			if ( typeName.EndsWith( "Feedback", StringComparison.Ordinal ) && typeName.Length > 8 )
				typeName = typeName.Substring( 0, typeName.Length - 8 );

			return ObjectNames.NicifyVariableName( typeName );
		}
	}
}
