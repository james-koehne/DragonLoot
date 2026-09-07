using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// Profile-backed first-time treasure discovery (by <see cref="TreasureDefinition.id"/>).
/// Falls back to a session set when the profile is unavailable so toasts still fire.
/// </summary>
public static class TreasureDiscoveryProgress
{
	static readonly HashSet<string> s_sessionDiscovered = new HashSet<string>();

	public static bool IsDiscovered( string treasureId )
	{
		if ( string.IsNullOrEmpty( treasureId ) )
			return false;

		if ( s_sessionDiscovered.Contains( treasureId ) )
			return true;

		ProfileSaveData save = GetSave();
		if ( save == null )
			return false;

		save.EnsureTreasureDiscoveryProgress();
		return save.discoveredTreasureIds.Contains( treasureId );
	}

	public static bool IsDiscovered( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;

		string id = definition.id;
		if ( string.IsNullOrEmpty( id ) )
			id = definition.name;
		return IsDiscovered( id );
	}

	/// <summary>
	/// Marks <paramref name="definition"/> as discovered. Returns true only on the first mark.
	/// Uses <see cref="TreasureDefinition.id"/>, falling back to the asset name if id is empty.
	/// </summary>
	public static bool TryMarkDiscovered( TreasureDefinition definition )
	{
		if ( definition == null )
			return false;

		string id = definition.id;
		if ( string.IsNullOrEmpty( id ) )
			id = definition.name;
		if ( string.IsNullOrEmpty( id ) )
			return false;

		if ( s_sessionDiscovered.Contains( id ) )
			return false;

		ProfileSaveData save = GetSave();
		if ( save != null )
		{
			save.EnsureTreasureDiscoveryProgress();
			if ( save.discoveredTreasureIds.Contains( id ) )
			{
				s_sessionDiscovered.Add( id );
				return false;
			}

			save.discoveredTreasureIds.Add( id );
			Persist( save );
		}

		s_sessionDiscovered.Add( id );
		return true;
	}

	static ProfileSaveData GetSave()
	{
		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
			return null;
		return manager.ProfileSaveData;
	}

	static void Persist( ProfileSaveData save )
	{
		if ( save == null )
			return;

		ProfileManager manager = ProfileManager.Instance;
		if ( manager == null )
			return;

		manager.SaveCurrentStatsToProfile();
	}
}
