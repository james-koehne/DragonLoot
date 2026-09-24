using UnityEngine;

[CreateAssetMenu( fileName = "CompleteGlowPreset", menuName = "Definitions/Graphics/Complete Glow Preset" )]
public class CompleteGlowPreset : ScriptableObject
{
	[SerializeField]
	CompleteGlowSettings _settings = new CompleteGlowSettings();

	public CompleteGlowSettings Settings
	{
		get { return _settings; }
	}

	public void ApplyTo( CompleteGlowSettings target )
	{
		if ( target == null || _settings == null )
			return;
		target.CopyFrom( _settings );
	}
}
