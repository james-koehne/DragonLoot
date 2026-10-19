using UnityEngine;

/// <summary>
/// Gates interactables / placement targets until required objectives are complete.
/// Put one on a machine root; children resolve it via <see cref="IsUnlockedFor"/>.
/// </summary>
public class ObjectiveUnlockGate : MonoBehaviour
{
	[SerializeField]
	[Tooltip( "All listed objective ids must be completed before this gate unlocks. Empty = always unlocked." )]
	string[] requiredObjectiveIds;

	public bool IsUnlocked
	{
		get
		{
			if ( requiredObjectiveIds == null || requiredObjectiveIds.Length == 0 )
				return true;

			ObjectiveSystem objectives = ObjectiveSystem.Instance;
			if ( objectives == null )
				return false;

			for ( int i = 0; i < requiredObjectiveIds.Length; i++ )
			{
				string id = requiredObjectiveIds[ i ];
				if ( string.IsNullOrEmpty( id ) )
					continue;
				if ( !objectives.IsCompleted( id ) )
					return false;
			}

			return true;
		}
	}

	/// <summary>
	/// True when no gate exists in the parent hierarchy, or the nearest gate is unlocked.
	/// </summary>
	public static bool IsUnlockedFor( Component component )
	{
		if ( component == null )
			return true;

		ObjectiveUnlockGate gate = component.GetComponentInParent<ObjectiveUnlockGate>();
		if ( gate == null )
			return true;
		return gate.IsUnlocked;
	}
}
