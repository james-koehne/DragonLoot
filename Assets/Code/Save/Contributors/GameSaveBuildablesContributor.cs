using System.Collections.Generic;

public sealed class GameSaveBuildablesContributor : IGameSaveContributor
{
	public string Key => "buildables";

	public void Capture( GameSaveData data )
	{
		if ( data == null )
			return;
		if ( data.world == null )
			data.world = new GameSaveWorldData();

		List<GameSaveBuildableEntry> list = data.world.buildables;
		if ( list == null )
		{
			list = new List<GameSaveBuildableEntry>();
			data.world.buildables = list;
		}
		else
			list.Clear();

		IReadOnlyList<BuildableObject> active = BuildableObject.Active;
		for ( int i = 0; i < active.Count; i++ )
		{
			BuildableObject buildable = active[ i ];
			if ( buildable == null )
				continue;

			string id = buildable.ResolvePersistenceId();
			if ( string.IsNullOrEmpty( id ) )
				continue;

			list.Add( new GameSaveBuildableEntry
			{
				persistenceId = id,
				built = buildable.IsBuilt
			} );
		}
	}

	public void Apply( GameSaveData data )
	{
		if ( data == null || data.world == null || data.world.buildables == null )
			return;

		Dictionary<string, bool> builtById = new Dictionary<string, bool>();
		List<GameSaveBuildableEntry> entries = data.world.buildables;
		for ( int i = 0; i < entries.Count; i++ )
		{
			GameSaveBuildableEntry entry = entries[ i ];
			if ( entry == null || string.IsNullOrEmpty( entry.persistenceId ) )
				continue;
			builtById[ entry.persistenceId ] = entry.built;
		}

		IReadOnlyList<BuildableObject> active = BuildableObject.Active;
		for ( int i = 0; i < active.Count; i++ )
		{
			BuildableObject buildable = active[ i ];
			if ( buildable == null )
				continue;

			string id = buildable.ResolvePersistenceId();
			if ( string.IsNullOrEmpty( id ) )
				continue;
			if ( !builtById.TryGetValue( id, out bool built ) )
				continue;

			buildable.ApplySavedBuiltState( built );
		}
	}
}
