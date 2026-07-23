using UnityEngine;

public class CameraController : MonoBehaviour
{
	public FirstPersonCameraController FirstPerson { get; private set; }

	public void Setup()
	{
		FirstPerson = GetComponentInChildren<FirstPersonCameraController>();
	}
}
