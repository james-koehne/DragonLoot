public interface IGameMode
{
	public void HandleDisconnection();
	public void LeaveCurrentMatch( bool sendCloseMatch = true );
}