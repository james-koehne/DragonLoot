using UnityEngine;

public class DebugInteractionSection : DebugOverlaySection
{
	public string Title => "Interaction";

	public void Draw()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		PlayerInteraction interaction = player != null ? player.Interaction : null;
		if ( interaction == null )
		{
			GUILayout.Label( "No interaction" );
			return;
		}

		if ( interaction.Current != null )
			GUILayout.Label( $"Focus: {interaction.Current.GetType().Name}" );
		else
			GUILayout.Label( "Focus: (none)" );

		float range = interaction.InteractRange;
		GUILayout.Label( $"Interact Range: {range:0.00}" );
		float nextRange = GUILayout.HorizontalSlider( range, 0.5f, 30f );
		if ( !Mathf.Approximately( nextRange, range ) )
			interaction.SetInteractRange( nextRange );

		float throwForce = interaction.ThrowForce;
		GUILayout.Label( $"Throw Force: {throwForce:0.00}" );
		float nextThrow = GUILayout.HorizontalSlider( throwForce, 0f, 30f );
		if ( !Mathf.Approximately( nextThrow, throwForce ) )
			interaction.SetThrowForce( nextThrow );

		float soft = interaction.SoftThrowSpeedScale;
		GUILayout.Label( $"Soft Throw Scale: {soft:0.00}" );
		float nextSoft = GUILayout.HorizontalSlider( soft, 0f, 1f );
		if ( !Mathf.Approximately( nextSoft, soft ) )
			interaction.SetSoftThrowSpeedScale( nextSoft );

		float repeatInterval = interaction.ThrowPlaceRepeatInterval;
		GUILayout.Label( $"Throw/Place Repeat: {repeatInterval:0.00}s" );
		float nextRepeat = GUILayout.HorizontalSlider( repeatInterval, 0.05f, 2f );
		if ( !Mathf.Approximately( nextRepeat, repeatInterval ) )
			interaction.SetThrowPlaceRepeatInterval( nextRepeat );

		float pickupRepeat = interaction.PickupRepeatInterval;
		GUILayout.Label( $"Pickup Repeat: {pickupRepeat:0.00}s" );
		float nextPickupRepeat = GUILayout.HorizontalSlider( pickupRepeat, 0.05f, 2f );
		if ( !Mathf.Approximately( nextPickupRepeat, pickupRepeat ) )
			interaction.SetPickupRepeatInterval( nextPickupRepeat );

		PlayerCarry carry = player != null ? player.Carry : null;
		if ( carry != null && carry.TryPeekActive( out TreasureItem held ) && held != null )
		{
			Vector3 releaseVel = interaction.GetReleaseVelocity( held );
			GUILayout.Label( $"Held throw speed: {releaseVel.magnitude:0.00}" );
		}

		if ( carry != null && carry.Count > 0 )
		{
			float progress = interaction.SecondaryThrowPlaceRepeatProgress;
			GUILayout.Label( $"Throw/Place Repeat Progress: {progress * 100f:0}%" );
		}

		if ( interaction.Current != null )
		{
			float pickupProgress = interaction.PrimaryPickupRepeatProgress;
			GUILayout.Label( $"Pickup Repeat Progress: {pickupProgress * 100f:0}%" );
		}
	}
}
