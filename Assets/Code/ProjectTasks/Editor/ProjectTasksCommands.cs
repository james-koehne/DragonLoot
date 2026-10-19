#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;

/// <summary>
/// Builds Cursor clipboard commands for sort / discover / plan using absolute workspace paths.
/// </summary>
public static class ProjectTasksCommands
{
	public static bool TryCopySortInboxCommand( string rootAbsolute, out string message )
	{
		string root = RequireRoot( rootAbsolute, out message );
		if ( root == null )
			return false;

		string prompt = BuildSortInboxCommand( root );
		EditorGuiUtilityCopy( prompt );
		message = "Sort Inbox command copied (" + root + ").";
		return true;
	}

	public static bool TryCopyDiscoverFeaturesCommand( string rootAbsolute, string unityProjectRoot, out string message )
	{
		string root = RequireRoot( rootAbsolute, out message );
		if ( root == null )
			return false;

		string projectRoot = string.IsNullOrWhiteSpace( unityProjectRoot )
			? Directory.GetCurrentDirectory()
			: ProjectTasksSettings.NormalizeAbsolutePath( unityProjectRoot );

		string prompt = BuildDiscoverFeaturesCommand( root, projectRoot );
		EditorGuiUtilityCopy( prompt );
		message = "Discover Features command copied.";
		return true;
	}

	public static bool TryCopyPlanFeatureCommand( string rootAbsolute, string featureAbsolutePath, out string message )
	{
		string root = RequireRoot( rootAbsolute, out message );
		if ( root == null )
			return false;

		if ( string.IsNullOrWhiteSpace( featureAbsolutePath ) || !File.Exists( featureAbsolutePath ) )
		{
			message = "Select a feature file first.";
			return false;
		}

		string featurePath = Path.GetFullPath( featureAbsolutePath );
		string prompt = BuildPlanFeatureCommand( root, featurePath );
		EditorGuiUtilityCopy( prompt );
		message = "Plan Feature command copied for " + Path.GetFileName( featurePath ) + ".";
		return true;
	}

	public static string BuildSortInboxCommand( string rootAbsolute )
	{
		string root = Path.GetFullPath( rootAbsolute );
		string ai = Path.Combine( root, ProjectTasksTemplates.AiInstructionsFileName );
		string project = Path.Combine( root, ProjectTasksTemplates.ProjectFileName );
		string inbox = Path.Combine( root, ProjectTasksTemplates.InboxFolderName );
		string features = Path.Combine( root, ProjectTasksTemplates.FeaturesFolderName );

		var sb = new StringBuilder();
		sb.AppendLine( "Sort the Project Tasks inbox. This is organisation only — do not implement tasks or write detailed implementation plans." );
		sb.AppendLine();
		sb.AppendLine( "Workspace root (absolute):" );
		sb.AppendLine( root );
		sb.AppendLine();
		sb.AppendLine( "1. Read:" );
		sb.AppendLine( "   - " + ai );
		sb.AppendLine( "   - " + project );
		sb.AppendLine( "2. Read all Markdown task files in:" );
		sb.AppendLine( "   - " + inbox );
		sb.AppendLine( "3. Categorise each task into the appropriate feature file under:" );
		sb.AppendLine( "   - " + features );
		sb.AppendLine( "4. Respect any explicit Feature assignment on the inbox task." );
		sb.AppendLine( "5. For Feature: Auto, determine the best existing feature (or create a new feature file when needed)." );
		sb.AppendLine( "6. Identify likely duplicates, ambiguous tasks, and conflicting requirements." );
		sb.AppendLine( "7. Preserve original task intent and stable task IDs (TASK-…)." );
		sb.AppendLine( "8. Move sorted tasks out of Inbox/ into the feature file's ## Outstanding Tasks section, then delete the inbox file." );
		sb.AppendLine( "9. Update files safely — never discard existing Markdown content; never silently overwrite unrelated sections." );
		sb.AppendLine( "10. Report a concise summary of changes, unresolved ambiguities, and decisions needing human input." );
		sb.AppendLine();
		sb.AppendLine( "Follow AI_INSTRUCTIONS.md strictly. Do not implement gameplay code in this step." );
		return sb.ToString().TrimEnd() + "\n";
	}

	public static string BuildDiscoverFeaturesCommand( string rootAbsolute, string unityProjectRoot )
	{
		string root = Path.GetFullPath( rootAbsolute );
		string projectRoot = Path.GetFullPath( unityProjectRoot );
		string ai = Path.Combine( root, ProjectTasksTemplates.AiInstructionsFileName );
		string project = Path.Combine( root, ProjectTasksTemplates.ProjectFileName );
		string features = Path.Combine( root, ProjectTasksTemplates.FeaturesFolderName );

		var sb = new StringBuilder();
		sb.AppendLine( "Initialise the Project Tasks Features/ directory from the existing Unity project." );
		sb.AppendLine( "This is a lightweight discovery pass — not a comprehensive codebase audit. Do not create implementation plans or task lists." );
		sb.AppendLine();
		sb.AppendLine( "Project Tasks root (absolute):" );
		sb.AppendLine( root );
		sb.AppendLine( "Unity project root (absolute):" );
		sb.AppendLine( projectRoot );
		sb.AppendLine();
		sb.AppendLine( "1. Read if they exist:" );
		sb.AppendLine( "   - " + ai );
		sb.AppendLine( "   - " + project );
		sb.AppendLine( "2. Inspect the repository with lightweight methods first: directory listings, file names, existing documentation, Unity scene/prefab names, assembly definitions, and other project metadata." );
		sb.AppendLine( "3. Exclude irrelevant directories such as Library/, Temp/, Obj/, Logs/, Builds/, generated files, and third-party packages where appropriate." );
		sb.AppendLine( "4. Infer major gameplay systems, features, and shared infrastructure from structure and docs." );
		sb.AppendLine( "5. Use targeted searches or inspect selected files only when needed to distinguish features or resolve important uncertainty. Do not recursively open every file." );
		sb.AppendLine( "6. Create a concise, logically grouped initial set of Markdown files in:" );
		sb.AppendLine( "   - " + features );
		sb.AppendLine( "7. Prefer meaningful gameplay/system boundaries (e.g. Player, Inventory, Minecarts, Coin Sorting Machine, UI, Save System, Audio) where evidence supports them." );
		sb.AppendLine( "8. Avoid a feature file for every class, prefab, or minor subsystem." );
		sb.AppendLine( "9. Avoid duplicating the same system across multiple feature files." );
		sb.AppendLine( "10. For each feature: brief Summary plus, where identifiable, References to important scripts/scenes/prefabs/docs." );
		sb.AppendLine( "11. Distinguish confirmed features from uncertain inferences. Do not invent gameplay systems without supporting evidence." );
		sb.AppendLine( "12. Keep feature files concise. Do not create Outstanding Tasks lists during discovery." );
		sb.AppendLine( "13. Preserve existing feature files and content. Update or add safely without overwriting existing work." );
		sb.AppendLine();
		sb.AppendLine( "Produce a summary of features created, existing files reused, and uncertain categorisations." );
		return sb.ToString().TrimEnd() + "\n";
	}

	public static string BuildPlanFeatureCommand( string rootAbsolute, string featureAbsolutePath )
	{
		string root = Path.GetFullPath( rootAbsolute );
		string featurePath = Path.GetFullPath( featureAbsolutePath );
		string ai = Path.Combine( root, ProjectTasksTemplates.AiInstructionsFileName );
		string project = Path.Combine( root, ProjectTasksTemplates.ProjectFileName );
		string featureName = Path.GetFileName( featurePath );

		var sb = new StringBuilder();
		sb.AppendLine( "Work on this feature: " + featureName );
		sb.AppendLine();
		sb.AppendLine( "Feature file (absolute):" );
		sb.AppendLine( featurePath );
		sb.AppendLine( "Project Tasks root (absolute):" );
		sb.AppendLine( root );
		sb.AppendLine();
		sb.AppendLine( "1. Read:" );
		sb.AppendLine( "   - " + ai );
		sb.AppendLine( "   - " + project );
		sb.AppendLine( "   - " + featurePath );
		sb.AppendLine( "2. At the start of work: clear completed verification items from this feature's ## Verification section (remove all `- [x]` lines). Leave pending `- [ ]` items." );
		sb.AppendLine( "3. Identify all outstanding tasks in the feature file." );
		sb.AppendLine( "4. Consult related feature files and project documentation as necessary." );
		sb.AppendLine( "5. Inspect relevant implementation files selectively to understand existing behaviour and architecture." );
		sb.AppendLine( "6. Identify dependencies, technical risks, and opportunities to reuse existing systems." );
		sb.AppendLine( "7. Develop an implementation plan covering all outstanding tasks for this feature, practical in scope, and mark which tasks need human input vs AI implementation." );
		sb.AppendLine( "8. Explain your understanding of intended behaviour and logic; highlight important assumptions and decisions." );
		sb.AppendLine( "9. ALWAYS stop and obtain explicit human confirmation of the proposed logic and approach before finalising the plan for implementation." );
		sb.AppendLine( "10. Incorporate feedback and revise the plan before proceeding." );
		sb.AppendLine( "11. After implementing: append concise unchecked `- [ ]` playtest steps to ## Verification for the human to confirm in Unity. Create the section if missing. Do not tick them yourself." );
		sb.AppendLine();
		sb.AppendLine( "Minimise unnecessary questions. Resolve routine technical details independently, but do not silently make significant gameplay or architectural decisions." );
		sb.AppendLine( "Do not include Archive/ tasks in active planning." );
		sb.AppendLine( "Prefer Plan mode for this request." );
		return sb.ToString().TrimEnd() + "\n";
	}

	static string RequireRoot( string rootAbsolute, out string message )
	{
		message = string.Empty;
		string root = ProjectTasksSettings.NormalizeAbsolutePath( rootAbsolute );
		if ( string.IsNullOrEmpty( root ) || !Directory.Exists( root ) )
		{
			message = "Configure and initialise a valid Project Tasks root first.";
			return null;
		}

		return root;
	}

	static void EditorGuiUtilityCopy( string text )
	{
		EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
	}
}
#endif
