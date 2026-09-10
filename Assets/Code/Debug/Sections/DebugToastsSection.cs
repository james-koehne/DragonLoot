using UnityEngine;

public class DebugToastsSection : DebugOverlaySection
{
	public string Title => "Toasts";

	public void Draw()
	{
		bool discoveryReady = DiscoveryToastUI.Instance != null;
		bool unlockReady = UnlockRewardToastUI.Instance != null;

		GUILayout.Label( "Discovery: " + ( discoveryReady ? ( DiscoveryToastUI.Instance.IsBusy ? "busy" : "ready" ) : "missing" ) );
		GUILayout.Label( "Unlock: " + ( unlockReady ? ( UnlockRewardToastUI.Instance.IsBusy ? "busy" : "ready" ) : "missing" ) );

		GUILayout.Space( 6f );
		GUILayout.Label( "DiscoveryToast" );
		GUI.enabled = discoveryReady;
		if ( GUILayout.Button( "Play Discovery Message" ) )
			DiscoveryToastUI.NotifyMessage( "Debug: Discovery Toast" );
		if ( GUILayout.Button( "Play Constellation Complete" ) )
			DiscoveryToastUI.NotifyMessage( "Constellation Complete: Debug Gem" );
		GUI.enabled = true;

		GUILayout.Space( 6f );
		GUILayout.Label( "UnlockRewardToast" );
		GUI.enabled = unlockReady;
		if ( GUILayout.Button( "Play Unlock Message" ) )
			UnlockRewardToastUI.NotifyMessage( "Unlocked: Debug Reward" );
		if ( GUILayout.Button( "Play Unlock Ability (Glide)" ) )
			PlayUnlockGlide();
		GUI.enabled = true;

		GUILayout.Space( 6f );
		GUILayout.Label( "Sequence" );
		GUI.enabled = discoveryReady && unlockReady;
		if ( GUILayout.Button( "Play Discovery then Unlock" ) )
		{
			DiscoveryToastUI.NotifyMessage( "Constellation Complete: Debug Gem" );
			UnlockRewardToastUI.NotifyMessage( "Unlocked: Double Jump", ResolveGlideIcon() );
		}
		GUI.enabled = true;
	}

	static void PlayUnlockGlide()
	{
		AbilityDefinition glide = ResolveGlideAbility();
		if ( glide != null )
		{
			UnlockRewardToastUI.NotifyUnlock( glide );
			return;
		}

		UnlockRewardToastUI.NotifyMessage( "Unlocked: Double Jump", ResolveGlideIcon() );
	}

	static AbilityDefinition ResolveGlideAbility()
	{
		AbilitySystem system = AbilitySystem.Instance;
		if ( system == null )
			return null;
		if ( system.TryGetDefinition( PlayerController.GlideAbilityId, out AbilityDefinition definition ) )
			return definition;
		return null;
	}

	static Sprite ResolveGlideIcon()
	{
		AbilityDefinition glide = ResolveGlideAbility();
		return glide != null ? glide.icon : null;
	}
}
