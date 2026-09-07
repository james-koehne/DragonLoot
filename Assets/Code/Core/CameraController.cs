using UnityEngine;

public class CameraController : MonoBehaviour
{
	Vector3 _baseLocalPosition;
	Quaternion _baseLocalRotation;

	public FirstPersonCameraController FirstPerson { get; private set; }

	public void Setup()
	{
		FirstPerson = GetComponentInChildren<FirstPersonCameraController>();
		CaptureBaseLocalPose();
	}

	public void CaptureBaseLocalPosition()
	{
		CaptureBaseLocalPose();
	}

	public void CaptureBaseLocalPose()
	{
		_baseLocalPosition = transform.localPosition;
		_baseLocalRotation = transform.localRotation;
	}

	public void SetLocalPositionZOffset( float zOffset )
	{
		Vector3 position = _baseLocalPosition;
		position.z += zOffset;
		transform.localPosition = position;
	}

	public void SetWorldPose( Vector3 worldPosition, Quaternion worldRotation )
	{
		transform.SetPositionAndRotation( worldPosition, worldRotation );
	}

	public void ResetLocalPose()
	{
		transform.localPosition = _baseLocalPosition;
		transform.localRotation = _baseLocalRotation;
	}

	public void ResetLocalPosition()
	{
		ResetLocalPose();
	}
}
