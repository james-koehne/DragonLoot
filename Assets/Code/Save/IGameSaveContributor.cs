/// <summary>
/// Thin adapter that captures/applies one slice of <see cref="GameSaveData"/>.
/// </summary>
public interface IGameSaveContributor
{
	string Key { get; }

	void Capture( GameSaveData data );

	void Apply( GameSaveData data );
}
