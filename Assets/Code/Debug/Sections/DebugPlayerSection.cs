using UnityEngine;

public class DebugPlayerSection : DebugOverlaySection
{
	public string Title => "Player";

	public void Draw()
	{
		PlayerController player = DebugOverlay.GetPlayer();
		if ( player == null )
		{
			GUILayout.Label( "No player" );
			return;
		}

		GUILayout.Label( $"Grounded: {player.IsGrounded}" );
		GUILayout.Label( $"Ground angle: {player.GroundAngle:0.0}°" );
		GUILayout.Label( $"Speed: {player.PlanarSpeed:0.00}" );
		bool climbingEnabled = GUILayout.Toggle( player.IsClimbingEnabled, "Climbing Enabled" );
		if ( climbingEnabled != player.IsClimbingEnabled )
			player.SetClimbingEnabled( climbingEnabled );

		GUILayout.Label( $"Climbing: {player.IsClimbing}" );
		GUILayout.Label( $"Sliding: {player.IsSliding}" );
		if ( player.IsSlideExitBoostActive )
			GUILayout.Label( $"Exit boost: {player.SlideExitBoostSpeed:0.00} u/s" );
		if ( !player.IsSliding && player.SlideEnterChargeProgress > 0f )
			GUILayout.Label( $"Slide charge: {player.SlideEnterChargeProgress * 100f:0}%" );
		GUILayout.Label( $"State: {player.MovementState}" );

		float walk = player.CurrentWalkSpeed;
		GUILayout.Label( $"Walk Speed: {walk:0.00}" );
		float nextWalk = GUILayout.HorizontalSlider( walk, 0.5f, 20f );
		if ( !Mathf.Approximately( nextWalk, walk ) )
			player.SetWalkSpeed( nextWalk );

		float jump = player.CurrentJumpForce;
		GUILayout.Label( $"Jump Force: {jump:0.00}" );
		float nextJump = GUILayout.HorizontalSlider( jump, 0f, 20f );
		if ( !Mathf.Approximately( nextJump, jump ) )
			player.SetJumpForce( nextJump );

		float gravity = player.CurrentGravity;
		GUILayout.Label( $"Gravity: {gravity:0.00}" );
		float nextGravity = GUILayout.HorizontalSlider( gravity, -80f, -1f );
		if ( !Mathf.Approximately( nextGravity, gravity ) )
			player.SetGravity( nextGravity );
	}
}
