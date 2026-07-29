#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

/// <summary>
/// Loads Milanote board TASK elements and builds hierarchy via Category:/Feature: keywords.
/// API path assigns Feature→Category and Task→Feature using location.parentId ancestry.
/// </summary>
public static class HierarchyParser
{
	const string CurrentPhaseMarker = "Current Phase";

	public static async Task<ProjectBoard> ParseAsync(
		MilanoteApiClient client,
		string rootBoardId,
		CancellationToken cancellationToken = default )
	{
		if ( client == null )
			throw new ArgumentNullException( nameof( client ) );
		if ( string.IsNullOrWhiteSpace( rootBoardId ) )
			throw new MilanoteApiException( "Board ID is not configured." );

		var project = new ProjectBoard
		{
			RootBoardId = rootBoardId.Trim()
		};

		MilanoteBoardResponse rootResponse = await client.GetBoardByIdAsync( project.RootBoardId, cancellationToken )
			.ConfigureAwait( false );

		MilanoteParsedElement rootElement = GetBoardElement( rootResponse, project.RootBoardId );
		project.RootTitle = rootElement != null && !string.IsNullOrWhiteSpace( rootElement.Title )
			? rootElement.Title.Trim()
			: project.RootBoardId;

		Dictionary<string, MilanoteParsedElement> byId = IndexElements( rootResponse );
		AppendElementTypeDiagnostics( project, byId );

		List<MilanoteParsedElement> phaseTasks = CollectCurrentPhaseTasks( byId, project );
		AppendKeywordDiagnostics( project, phaseTasks );
		BuildBoardFromParentIds( project, byId, phaseTasks );

		if ( CountFeatures( project ) == 0 )
			TryParseEmbeddedMarkdownChecklists( project, byId );

		if ( CountFeatures( project ) == 0 )
		{
			project.Warnings.Add(
				"No features generated. Author Milanote todos with 'Category:' and 'Feature:' prefixes "
				+ "(see .cursor/dragon-loot*.md export for the expected format)." );
		}

		return project;
	}

	/// <summary>
	/// Builds Category → Feature → Tasks using parentId links and Milanote position.score/index order.
	/// </summary>
	static void BuildBoardFromParentIds(
		ProjectBoard project,
		Dictionary<string, MilanoteParsedElement> byId,
		List<MilanoteParsedElement> tasks )
	{
		if ( tasks == null || tasks.Count == 0 )
			return;

		var categoryByTaskId = new Dictionary<string, ProjectCategory>( StringComparer.Ordinal );
		var featureByTaskId = new Dictionary<string, ProjectFeature>( StringComparer.Ordinal );
		var rawByFeature = new Dictionary<ProjectFeature, List<ProjectTaskItem>>();
		var categoryTaskIds = new HashSet<string>( StringComparer.Ordinal );
		var featureTaskIds = new HashSet<string>( StringComparer.Ordinal );
		var phaseTaskIds = new HashSet<string>( StringComparer.Ordinal );

		for ( int i = 0; i < tasks.Count; i++ )
		{
			if ( tasks[i] != null && !string.IsNullOrEmpty( tasks[i].Id ) )
				phaseTaskIds.Add( tasks[i].Id );
		}

		List<MilanoteParsedElement> ordered = new List<MilanoteParsedElement>( tasks );
		ordered.Sort( CompareByPosition );

		for ( int i = 0; i < ordered.Count; i++ )
		{
			MilanoteParsedElement task = ordered[i];
			string text = GetTaskText( task );
			if ( string.IsNullOrEmpty( text ) )
				continue;

			string categoryName;
			if ( !KeywordHierarchyParser.TryStripPrefix( text, KeywordHierarchyParser.CategoryPrefix, out categoryName ) )
				continue;

			ProjectCategory category = KeywordHierarchyParser.GetOrCreateCategory(
				project, task.Id, categoryName, task.PositionScore, task.PositionIndex );
			categoryByTaskId[task.Id] = category;
			categoryTaskIds.Add( task.Id );
		}

		for ( int i = 0; i < ordered.Count; i++ )
		{
			MilanoteParsedElement task = ordered[i];
			string text = GetTaskText( task );
			if ( string.IsNullOrEmpty( text ) )
				continue;

			string featureName;
			if ( !KeywordHierarchyParser.TryStripPrefix( text, KeywordHierarchyParser.FeaturePrefix, out featureName ) )
				continue;

			ProjectCategory category = FindAncestorCategory( task, byId, categoryByTaskId );
			if ( category == null )
			{
				category = KeywordHierarchyParser.GetOrCreateCategory(
					project, "uncategorized", KeywordHierarchyParser.UncategorizedName );
				project.Warnings.Add(
					"Feature '" + featureName + "' has no Category: ancestor via parentId; using Uncategorized." );
			}

			ProjectFeature feature = KeywordHierarchyParser.GetOrCreateNamedFeature(
				category,
				task.Id,
				featureName,
				task.IsComplete,
				task.PositionScore,
				task.PositionIndex );
			featureByTaskId[task.Id] = feature;
			featureTaskIds.Add( task.Id );
			if ( !rawByFeature.ContainsKey( feature ) )
				rawByFeature[feature] = new List<ProjectTaskItem>();
		}

		var skipIds = new HashSet<string>( categoryTaskIds, StringComparer.Ordinal );
		foreach ( string id in featureTaskIds )
			skipIds.Add( id );

		foreach ( KeyValuePair<string, ProjectFeature> pair in featureByTaskId )
		{
			List<ProjectTaskItem> raw = CollectTreeOrderedTasks(
				pair.Key, byId, phaseTaskIds, skipIds );
			rawByFeature[pair.Value] = raw;
		}

		foreach ( KeyValuePair<string, ProjectCategory> pair in categoryByTaskId )
		{
			List<ProjectTaskItem> loose = CollectTreeOrderedTasks(
				pair.Key, byId, phaseTaskIds, skipIds );
			if ( loose.Count == 0 )
				continue;

			ProjectFeature bugs = KeywordHierarchyParser.GetOrCreateBugsFeature( pair.Value );
			List<ProjectTaskItem> existing = GetRawList( rawByFeature, bugs );
			for ( int i = 0; i < loose.Count; i++ )
				existing.Add( loose[i] );
		}

		List<ProjectTaskItem> orphanLoose = new List<ProjectTaskItem>();
		for ( int i = 0; i < ordered.Count; i++ )
		{
			MilanoteParsedElement task = ordered[i];
			if ( skipIds.Contains( task.Id ) )
				continue;
			if ( FindAncestorFeature( task, byId, featureByTaskId ) != null )
				continue;
			if ( FindAncestorCategory( task, byId, categoryByTaskId ) != null )
				continue;

			string text = GetTaskText( task );
			if ( string.IsNullOrEmpty( text ) )
				continue;
			orphanLoose.Add( ToTaskItem( task ) );
		}

		if ( orphanLoose.Count > 0 )
		{
			ProjectCategory overallBugsCat = KeywordHierarchyParser.GetOrCreateCategory(
				project, "cat-bugs", KeywordHierarchyParser.BugsName );
			ProjectFeature overallBugs = KeywordHierarchyParser.GetOrCreateBugsFeature( overallBugsCat );
			List<ProjectTaskItem> existing = GetRawList( rawByFeature, overallBugs );
			for ( int i = 0; i < orphanLoose.Count; i++ )
				existing.Add( orphanLoose[i] );
		}

		foreach ( KeyValuePair<ProjectFeature, List<ProjectTaskItem>> pair in rawByFeature )
		{
			TaskPrefixParser.Categorise( pair.Key, pair.Value );
			pair.Key.Status = FeatureStatusDeriver.Derive( pair.Key );
		}

		KeywordHierarchyParser.SortBoard( project );

		project.Warnings.Add(
			"ParentId hierarchy: categories=" + categoryTaskIds.Count
			+ ", features=" + featureTaskIds.Count
			+ ", assigned task rows=" + CountAssigned( rawByFeature ) + "." );
	}

	static List<ProjectTaskItem> CollectTreeOrderedTasks(
		string rootId,
		Dictionary<string, MilanoteParsedElement> byId,
		HashSet<string> phaseTaskIds,
		HashSet<string> skipIds )
	{
		var result = new List<ProjectTaskItem>();
		AppendTreeChildren( rootId, byId, phaseTaskIds, skipIds, result );
		return result;
	}

	static void AppendTreeChildren(
		string parentId,
		Dictionary<string, MilanoteParsedElement> byId,
		HashSet<string> phaseTaskIds,
		HashSet<string> skipIds,
		List<ProjectTaskItem> result )
	{
		var children = new List<MilanoteParsedElement>();
		foreach ( KeyValuePair<string, MilanoteParsedElement> pair in byId )
		{
			MilanoteParsedElement element = pair.Value;
			if ( element == null || !phaseTaskIds.Contains( element.Id ) )
				continue;
			if ( !string.Equals( element.ElementType, "TASK", StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( !string.Equals( element.ParentId, parentId, StringComparison.Ordinal ) )
				continue;
			if ( skipIds.Contains( element.Id ) )
				continue;
			children.Add( element );
		}

		children.Sort( CompareByPosition );
		for ( int i = 0; i < children.Count; i++ )
		{
			MilanoteParsedElement child = children[i];
			string text = GetTaskText( child );
			if ( !string.IsNullOrEmpty( text ) )
				result.Add( ToTaskItem( child ) );
			AppendTreeChildren( child.Id, byId, phaseTaskIds, skipIds, result );
		}
	}

	static ProjectTaskItem ToTaskItem( MilanoteParsedElement task )
	{
		return new ProjectTaskItem
		{
			Id = task.Id,
			ParentId = task.ParentId,
			Text = GetTaskText( task ),
			IsComplete = task.IsComplete,
			SortScore = task.PositionScore,
			SortIndex = task.PositionIndex
		};
	}

	static int CountAssigned( Dictionary<ProjectFeature, List<ProjectTaskItem>> rawByFeature )
	{
		int count = 0;
		foreach ( KeyValuePair<ProjectFeature, List<ProjectTaskItem>> pair in rawByFeature )
			count += pair.Value.Count;
		return count;
	}

	static List<ProjectTaskItem> GetRawList(
		Dictionary<ProjectFeature, List<ProjectTaskItem>> map,
		ProjectFeature feature )
	{
		List<ProjectTaskItem> list;
		if ( !map.TryGetValue( feature, out list ) || list == null )
		{
			list = new List<ProjectTaskItem>();
			map[feature] = list;
		}

		return list;
	}

	static ProjectCategory FindAncestorCategory(
		MilanoteParsedElement task,
		Dictionary<string, MilanoteParsedElement> byId,
		Dictionary<string, ProjectCategory> categoryByTaskId )
	{
		string parentId = task != null ? task.ParentId : null;
		var guard = new HashSet<string>( StringComparer.Ordinal );
		while ( !string.IsNullOrEmpty( parentId ) && guard.Add( parentId ) )
		{
			ProjectCategory category;
			if ( categoryByTaskId.TryGetValue( parentId, out category ) )
				return category;

			MilanoteParsedElement parent;
			if ( !byId.TryGetValue( parentId, out parent ) )
				return null;
			parentId = parent.ParentId;
		}

		return null;
	}

	static ProjectFeature FindAncestorFeature(
		MilanoteParsedElement task,
		Dictionary<string, MilanoteParsedElement> byId,
		Dictionary<string, ProjectFeature> featureByTaskId )
	{
		string parentId = task != null ? task.ParentId : null;
		var guard = new HashSet<string>( StringComparer.Ordinal );
		while ( !string.IsNullOrEmpty( parentId ) && guard.Add( parentId ) )
		{
			ProjectFeature feature;
			if ( featureByTaskId.TryGetValue( parentId, out feature ) )
				return feature;

			MilanoteParsedElement parent;
			if ( !byId.TryGetValue( parentId, out parent ) )
				return null;
			parentId = parent.ParentId;
		}

		return null;
	}

	static string GetTaskText( MilanoteParsedElement task )
	{
		if ( task == null )
			return null;
		string text = task.TextContent;
		if ( string.IsNullOrWhiteSpace( text ) )
			text = task.Title;
		return string.IsNullOrWhiteSpace( text ) ? null : text.Trim();
	}

	static void AppendKeywordDiagnostics( ProjectBoard project, IReadOnlyList<MilanoteParsedElement> tasks )
	{
		int categoryCount = 0;
		int featureCount = 0;
		var samples = new List<string>();

		for ( int i = 0; i < tasks.Count; i++ )
		{
			string text = GetTaskText( tasks[i] );
			if ( string.IsNullOrEmpty( text ) )
				continue;

			if ( text.StartsWith( KeywordHierarchyParser.CategoryPrefix, StringComparison.OrdinalIgnoreCase ) )
				categoryCount++;
			else if ( text.StartsWith( KeywordHierarchyParser.FeaturePrefix, StringComparison.OrdinalIgnoreCase ) )
				featureCount++;

			if ( samples.Count < 12 )
				samples.Add( Truncate( text, 80 ) );
		}

		project.Warnings.Add(
			"API todo items=" + tasks.Count
			+ ", Category:=" + categoryCount
			+ ", Feature:=" + featureCount + "." );

		if ( categoryCount == 0 && tasks.Count > 0 )
		{
			project.Warnings.Add(
				"No 'Category:' prefixes found in API task text. "
				+ "Expected items like 'Category: Player' / 'Feature: Throwing'." );
			project.Warnings.Add( "Sample API task texts: " + string.Join( " | ", samples ) );
		}
	}

	static string Truncate( string text, int maxLen )
	{
		if ( string.IsNullOrEmpty( text ) || text.Length <= maxLen )
			return text;
		return text.Substring( 0, maxLen ) + "…";
	}

	static List<MilanoteParsedElement> CollectCurrentPhaseTasks(
		Dictionary<string, MilanoteParsedElement> byId,
		ProjectBoard project )
	{
		var tasks = new List<MilanoteParsedElement>();
		List<MilanoteParsedElement> taskLists = GetAllTaskLists( byId );
		taskLists.Sort( CompareByPosition );

		if ( taskLists.Count == 0 )
		{
			List<MilanoteParsedElement> allTasks = GetAllTasks( byId );
			allTasks.Sort( CompareByPosition );
			if ( allTasks.Count > 0 )
				project.Warnings.Add( "No TASK_LIST found; parsing " + allTasks.Count + " TASK elements." );
			return allTasks;
		}

		var included = new List<MilanoteParsedElement>();
		int skipped = 0;
		for ( int i = 0; i < taskLists.Count; i++ )
		{
			MilanoteParsedElement taskList = taskLists[i];
			string label = string.IsNullOrWhiteSpace( taskList.Title ) ? taskList.Id : taskList.Title.Trim();
			if ( label.IndexOf( CurrentPhaseMarker, StringComparison.OrdinalIgnoreCase ) < 0 )
			{
				skipped++;
				project.Warnings.Add( "Skipped TASK_LIST (not Current Phase): '" + label + "'." );
				continue;
			}

			included.Add( taskList );
		}

		if ( included.Count == 0 )
		{
			project.Warnings.Add(
				"No TASK_LIST titles contain '" + CurrentPhaseMarker + "' ("
				+ taskLists.Count + " list(s) skipped). No API todos imported." );
			return tasks;
		}

		for ( int i = 0; i < included.Count; i++ )
		{
			MilanoteParsedElement taskList = included[i];
			List<MilanoteParsedElement> ordered = CollectOrderedTasksForList( byId, taskList.Id );
			string label = string.IsNullOrWhiteSpace( taskList.Title ) ? taskList.Id : taskList.Title.Trim();
			project.Warnings.Add( "TASK_LIST '" + label + "' → " + ordered.Count + " tasks." );
			tasks.AddRange( ordered );
		}

		if ( skipped > 0 )
			project.Warnings.Add( "Current Phase filter: included " + included.Count + ", skipped " + skipped + "." );

		return tasks;
	}

	static void TryParseEmbeddedMarkdownChecklists(
		ProjectBoard project,
		Dictionary<string, MilanoteParsedElement> byId )
	{
		var combined = new System.Text.StringBuilder();
		int docs = 0;
		foreach ( KeyValuePair<string, MilanoteParsedElement> pair in byId )
		{
			MilanoteParsedElement element = pair.Value;
			string type = element.ElementType ?? string.Empty;
			if ( !string.Equals( type, "DOCUMENT", StringComparison.OrdinalIgnoreCase )
			     && !string.Equals( type, "CARD", StringComparison.OrdinalIgnoreCase )
			     && !string.Equals( type, "NOTE", StringComparison.OrdinalIgnoreCase ) )
				continue;

			if ( string.IsNullOrWhiteSpace( element.TextContent ) )
				continue;

			docs++;
			combined.Append( element.TextContent ).Append( '\n' );
		}

		if ( docs == 0 || combined.Length == 0 )
			return;

		project.Warnings.Add( "Trying embedded markdown checklists from " + docs + " document/card elements." );
		ProjectBoard fromMarkdown = MarkdownExportParser.Parse(
			combined.ToString(), project.RootTitle, requireCurrentPhase: false );
		MergeBoard( project, fromMarkdown );
	}

	static void MergeBoard( ProjectBoard target, ProjectBoard source )
	{
		if ( source == null )
			return;

		for ( int i = 0; i < source.Warnings.Count; i++ )
			target.Warnings.Add( source.Warnings[i] );

		for ( int c = 0; c < source.Categories.Count; c++ )
		{
			ProjectCategory srcCat = source.Categories[c];
			ProjectCategory dstCat = null;
			for ( int i = 0; i < target.Categories.Count; i++ )
			{
				if ( string.Equals( target.Categories[i].Name, srcCat.Name, StringComparison.OrdinalIgnoreCase ) )
				{
					dstCat = target.Categories[i];
					break;
				}
			}

			if ( dstCat == null )
			{
				dstCat = new ProjectCategory { Id = srcCat.Id, Name = srcCat.Name };
				target.Categories.Add( dstCat );
			}

			for ( int f = 0; f < srcCat.Features.Count; f++ )
			{
				ProjectFeature feature = srcCat.Features[f];
				feature.CategoryName = dstCat.Name;
				dstCat.Features.Add( feature );
			}
		}
	}

	static List<MilanoteParsedElement> CollectOrderedTasksForList(
		Dictionary<string, MilanoteParsedElement> byId,
		string taskListId )
	{
		var tasks = new List<MilanoteParsedElement>();
		foreach ( KeyValuePair<string, MilanoteParsedElement> pair in byId )
		{
			MilanoteParsedElement element = pair.Value;
			if ( !string.Equals( element.ElementType, "TASK", StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( BelongsToAncestor( element, taskListId, byId ) )
				tasks.Add( element );
		}

		tasks.Sort( CompareByPosition );
		return tasks;
	}

	static bool BelongsToAncestor(
		MilanoteParsedElement task,
		string ancestorId,
		Dictionary<string, MilanoteParsedElement> byId )
	{
		string parentId = task.ParentId;
		var guard = new HashSet<string>( StringComparer.Ordinal );
		while ( !string.IsNullOrEmpty( parentId ) && guard.Add( parentId ) )
		{
			if ( string.Equals( parentId, ancestorId, StringComparison.Ordinal ) )
				return true;

			MilanoteParsedElement parent;
			if ( !byId.TryGetValue( parentId, out parent ) )
				return false;

			parentId = parent.ParentId;
		}

		return false;
	}

	static List<MilanoteParsedElement> GetAllTasks( Dictionary<string, MilanoteParsedElement> byId )
	{
		var tasks = new List<MilanoteParsedElement>();
		foreach ( KeyValuePair<string, MilanoteParsedElement> pair in byId )
		{
			if ( string.Equals( pair.Value.ElementType, "TASK", StringComparison.OrdinalIgnoreCase ) )
				tasks.Add( pair.Value );
		}

		return tasks;
	}

	static List<MilanoteParsedElement> GetAllTaskLists( Dictionary<string, MilanoteParsedElement> byId )
	{
		var lists = new List<MilanoteParsedElement>();
		foreach ( KeyValuePair<string, MilanoteParsedElement> pair in byId )
		{
			if ( string.Equals( pair.Value.ElementType, "TASK_LIST", StringComparison.OrdinalIgnoreCase ) )
				lists.Add( pair.Value );
		}

		return lists;
	}

	static Dictionary<string, MilanoteParsedElement> IndexElements( MilanoteBoardResponse response )
	{
		var byId = new Dictionary<string, MilanoteParsedElement>( StringComparer.Ordinal );
		if ( response == null || response.Elements == null )
			return byId;

		foreach ( KeyValuePair<string, JObject> pair in response.Elements )
		{
			MilanoteParsedElement element = MilanoteDtoParser.ParseElement( pair.Value );
			if ( element == null || string.IsNullOrEmpty( element.Id ) )
				continue;
			byId[element.Id] = element;
		}

		return byId;
	}

	static MilanoteParsedElement GetBoardElement( MilanoteBoardResponse response, string boardId )
	{
		if ( response == null || response.Elements == null )
			return null;
		JObject json;
		if ( !response.Elements.TryGetValue( boardId, out json ) )
			return null;
		return MilanoteDtoParser.ParseElement( json );
	}

	static void AppendElementTypeDiagnostics( ProjectBoard project, Dictionary<string, MilanoteParsedElement> byId )
	{
		var counts = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		foreach ( KeyValuePair<string, MilanoteParsedElement> pair in byId )
		{
			string type = string.IsNullOrEmpty( pair.Value.ElementType ) ? "(unknown)" : pair.Value.ElementType;
			int count;
			counts.TryGetValue( type, out count );
			counts[type] = count + 1;
		}

		var parts = new List<string>();
		foreach ( KeyValuePair<string, int> pair in counts )
			parts.Add( pair.Key + "=" + pair.Value );

		parts.Sort( StringComparer.OrdinalIgnoreCase );
		project.Warnings.Add( "Board elements (" + byId.Count + "): " + string.Join( ", ", parts ) );
	}

	static int CountFeatures( ProjectBoard project )
	{
		int count = 0;
		for ( int i = 0; i < project.Categories.Count; i++ )
			count += project.Categories[i].Features.Count;
		return count;
	}

	static int CompareByPosition( MilanoteParsedElement a, MilanoteParsedElement b )
	{
		return KeywordHierarchyParser.CompareSortOrder(
			a.PositionScore,
			a.PositionIndex,
			a.Id,
			b.PositionScore,
			b.PositionIndex,
			b.Id );
	}
}
#endif
