using Nakama;

using UnityEngine.Events;

public interface IInterfaceController
{
	public void HideError();
	public void ShowError( string errorMsg, UnityAction handleOnClick = null, bool showCloseButton = true, bool playFx = true );
	public void OnNotificationsUpdated();
}