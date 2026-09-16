/// <summary>
/// Optional intensity + braking signal for looping feedbacks (e.g. minecart move rumble).
/// </summary>
public interface IFeedbackIntensity
{
	/// <summary>0 = idle, 1 = full speed / intensity.</summary>
	float FeedbackIntensity { get; }

	/// <summary>True while actively braking; loop SFX may duck.</summary>
	bool FeedbackBraking { get; }
}
