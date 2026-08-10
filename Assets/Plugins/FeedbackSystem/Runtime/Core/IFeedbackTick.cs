namespace FeedbackSystem
{
	public interface IFeedbackTick
	{
		bool Tick( float deltaTime );

		void Cancel();
	}
}
