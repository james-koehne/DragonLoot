/// <summary>
/// Streaming lifecycle for a gold-pile loot chunk.
/// Unloaded is only used when the pile is released; distance never unloads.
/// </summary>
public enum GoldPileChunkStreamState : byte
{
	Unloaded = 0,
	Loaded = 1,
	Visible = 2,
	Rendered = 3
}
