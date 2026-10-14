public sealed class GameSaveWorldTreasureContributor : IGameSaveContributor
{
	public string Key => "worldTreasure";

	public void Capture( GameSaveData data )
	{
		if ( data == null )
			return;
		if ( data.world == null )
			data.world = new GameSaveWorldData();

		WorldTreasureStreamer.CaptureInto( data.world );
	}

	public void Apply( GameSaveData data )
	{
		if ( data == null || data.world == null )
			return;

		WorldTreasureStreamer.ApplyFrom( data.world );
	}
}
