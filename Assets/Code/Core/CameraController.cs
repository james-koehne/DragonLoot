using UnityEngine;

public class CameraController : MonoBehaviour
{
	Vector3 _baseLocalPosition;

	public FirstPersonCameraController FirstPerson { get; private set; }

	public void Setup()
	{
		FirstPerson = GetComponentInChildren<FirstPersonCameraController>();
		CaptureBaseLocalPosition();
	}

	public void CaptureBaseLocalPosition()
	{
		_baseLocalPosition = transform.localPosition;
	}

	public void SetLocalPositionZOffset( float zOffset )
	{
		Vector3 position = _baseLocalPosition;
		position.z += zOffset;
		transform.localPosition = position;
	}

	public void ResetLocalPosition()
	{
		transform.localPosition = _baseLocalPosition;
	}
}
