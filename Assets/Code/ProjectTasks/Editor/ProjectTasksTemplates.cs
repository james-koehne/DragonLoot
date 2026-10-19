#if UNITY_EDITOR
/// <summary>
/// Default Markdown templates written only when target files do not already exist.
/// </summary>
public static class ProjectTasksTemplates
{
	public const string AiInstructionsFileName = "AI_INSTRUCTIONS.md";
	public const string ProjectFileName = "PROJECT.md";
	public const string InboxFolderName = "Inbox";
	public const string FeaturesFolderName = "Features";
	public const string ArchiveFolderName = "Archive";

	public static string AiInstructionsMarkdown =>
@"# AI Instructions — Project Tasks

Markdown under this workspace root is the single source of truth for project tasks.
Unity captures tasks. Cursor organises, plans, and archives. Humans confirm gameplay logic and review implementation quality.

## Roles

- **Unity Editor:** capture tasks to `Inbox/`, configure the root path, browse files, copy Cursor commands.
- **Cursor:** sort the inbox, discover features, plan outstanding work, archive completed tasks.
- **Developer:** confirm gameplay/architecture decisions, playtest, and review quality.

## Sorting (Inbox → Features)

1. Read `AI_INSTRUCTIONS.md` and `PROJECT.md`.
2. Read every Markdown file in `Inbox/`.
3. Categorise each task into the appropriate `Features/*.md` file.
4. Respect an explicit `Feature:` value on the inbox task. Use judgment only when Feature is `Auto`.
5. Create a missing feature file when required (use the feature stub structure).
6. Preserve stable task IDs and original intent/wording.
7. Move sorted tasks into the feature's `## Outstanding Tasks` section, then remove the inbox file.
8. Flag likely duplicates, ambiguous tasks, and conflicting requirements for human input.
9. Never discard existing feature content. Append or update safely.
10. Do **not** implement tasks or write detailed implementation plans during sorting.
11. Report a concise summary: moved tasks, new features, ambiguities, decisions needing humans.

## Feature files

- One file per logical feature boundary (gameplay system or shared infrastructure), e.g. `Player.md`, `Minecarts.md`.
- Prefer meaningful boundaries over one file per class/prefab.
- Keep `## Summary` and `## References` concise.
- Do not invent systems without evidence from the project.

## Planning

When asked to work on a feature (e.g. `Work on this feature: Player.md`):

1. Read `AI_INSTRUCTIONS.md`, `PROJECT.md`, and the feature file.
2. Consult related feature files and docs only as needed.
3. Inspect implementation files selectively — do not recursively open everything.
4. Cover **all** outstanding tasks for that feature, kept practically scoped.
5. Identify dependencies, risks, reuse opportunities, and which work needs human input vs AI.
6. Explain intended behaviour, assumptions, and important decisions.
7. **Stop and obtain explicit human confirmation** of the proposed logic and approach before finalising the plan for implementation.
8. Resolve routine technical details independently; do not silently make significant gameplay or architectural decisions.
9. Incorporate feedback and revise before proceeding.

## Task completion and archiving

When a task is completed:

1. Write a concise completion summary.
2. Move the task to `Archive/` as `Archive/<TASK-ID>.md`, preserving ID, original description, and completion notes.
3. Remove it from the feature's `## Outstanding Tasks`.
4. Do not include archived tasks in future active planning.

## Verification (human playtest)

Each feature file has a `## Verification` checklist for human confirmation in Unity.

Format:

```markdown
## Verification

- [ ] Concise playable step to confirm behaviour X
- [x] Already confirmed by the developer
```

Rules:

1. **When starting work on a feature:** remove all completed items (`- [x]`) from that feature's `## Verification` section. Leave any still-pending (`- [ ]`) items.
2. **After implementing:** append new unchecked (`- [ ]`) verification steps the developer must playtest/confirm. Keep them concise, observable, and specific.
3. Do not tick verification items yourself — only the human does that in the Project Tasks window.
4. Create the `## Verification` section if it is missing when adding items.

## Safety and style

- Never silently overwrite existing Markdown content.
- Preserve task IDs forever across Inbox → Feature → Archive.
- Avoid unnecessary documentation and duplicated information.
- Prefer updating existing sections over creating parallel copies.
- Ambiguity → ask or flag; do not invent requirements.
- `.cursor/milanote/` is historical only — not an active task source.
";

	public static string ProjectMarkdown =>
@"# Project — Dragon Loot

Concise project context for task sorting and planning. Feature details live in `Features/` — do not duplicate them here.

## What this project is

Dragon Loot is a Unity game about collecting, sorting, and presenting treasure (coins, gems, artifacts) with player interaction, world systems, and juice.

## Development conventions

- Prefer Addressables for spawning GameObjects (not raw prefab Instantiate) where the project already does.
- Never use `FindObjectOfType`.
- Never use null propagation.
- Never use `UnityEngine.Input` (use the project's input approach).
- Avoid `System.Threading.Tasks` for gameplay unless explicitly required.
- Keep method calls on one line when they fit.
- Do not edit `.unity` scene files unless explicitly asked; prefer prefabs / definitions.
- Runtime FX/SFX/juice: use **FeedbackSystem** (`Assets/Plugins/FeedbackSystem/`) — expose `Feedbacks` and call `Play()`.

## Useful locations

- Gameplay code: `Assets/Code/`
- Cursor rules: `.cursor/rules/`
- Historical Milanote exports (inactive): `.cursor/milanote/`

## Constraints

- Keep plans practical and scoped.
- Markdown in this Project Tasks workspace is authoritative for outstanding work.
- Humans confirm gameplay logic before implementation plans are final.
";

	public static string FeatureStubMarkdown( string featureName )
	{
		string name = string.IsNullOrWhiteSpace( featureName ) ? "Feature" : featureName.Trim();
		string colour = ProjectTasksMarkdown.ColourToHex( ProjectTasksMarkdown.ColorFromFeatureName( name ) );
		return
@"# " + name + @"

- Colour: " + colour + @"

## Summary

Brief description of this feature / system boundary.

## References

- (scripts, scenes, prefabs, docs)

## Outstanding Tasks

## Verification

## Notes

";
	}
}
#endif
