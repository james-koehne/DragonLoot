using UnityEngine;

public class DebugToastsSection : DebugOverlaySection
{
	public string Title => "Toasts";

	public void Draw()
	{
		bool discoveryReady = DiscoveryToastUI.Instance != null;
		bool unlockReady = UnlockRewardToastUI.Instance != null;
		ToastStackUI stack = ToastStackUI.Instance;
		bool stackReady = stack != null;

		GUILayout.Label( "Discovery: " + ( discoveryReady ? ( DiscoveryToastUI.Instance.IsBusy ? "busy" : "ready" ) : "missing" ) );
		GUILayout.Label( "Unlock: " + ( unlockReady ? ( UnlockRewardToastUI.Instance.IsBusy ? "busy" : "ready" ) : "missing" ) );
		GUILayout.Label( "Stack: " + ( stackReady ? ( stack.VisibleCount + " visible / " + stack.QueuedCount + " queued" ) : "missing" ) );

		GUILayout.Space( 6f );
		GUILayout.Label( "By Tier" );
		GUI.enabled = discoveryReady;
		if ( GUILayout.Button( "Discovery (first pickup)" ) )
			DiscoveryToastUI.NotifyMessage( "New Discovery: Debug Coin", ToastStackUI.ToastTier.Discovery );
		if ( GUILayout.Button( "Completion (constellation / display)" ) )
			DiscoveryToastUI.NotifyMessage( "Constellation Complete: Debug Gem", ToastStackUI.ToastTier.Completion );
		GUI.enabled = unlockReady;
		if ( GUILayout.Button( "Unlock (ability)" ) )
			PlayUnlockGlide();
		if ( GUILayout.Button( "Unlock (upgrade)" ) )
			PlayUnlockSwiftHands();
		if ( GUILayout.Button( "Milestone (island complete)" ) )
			UnlockRewardToastUI.NotifyMessage( "Island complete — platforms activated", null, ToastStackUI.ToastTier.Milestone );
		GUI.enabled = true;

		GUILayout.Space( 6f );
		GUILayout.Label( "Legacy / Sequence" );
		GUI.enabled = discoveryReady;
		if ( GUILayout.Button( "Play Discovery Message" ) )
			DiscoveryToastUI.NotifyMessage( "Debug: Discovery Toast" );
		GUI.enabled = unlockReady;
		if ( GUILayout.Button( "Play Unlock Message" ) )
			UnlockRewardToastUI.NotifyMessage( "Unlocked: Debug Reward" );
		GUI.enabled = discoveryReady && unlockReady;
		if ( GUILayout.Button( "Play Discovery then Unlock" ) )
		{
			DiscoveryToastUI.NotifyMessage( "Constellation Complete: Debug Gem", ToastStackUI.ToastTier.Completion );
			PlayUnlockGlide();
		}
		if ( GUILayout.Button( "Play 3 Stacked Discovery" ) )
		{
			DiscoveryToastUI.NotifyMessage( "Debug: Discovery 1" );
			DiscoveryToastUI.NotifyMessage( "Debug: Discovery 2" );
			DiscoveryToastUI.NotifyMessage( "Debug: Discovery 3" );
		}
		if ( GUILayout.Button( "Play Mixed Stack (4 tiers, queues 1)" ) )
		{
			DiscoveryToastUI.NotifyMessage( "New Discovery: Debug Coin", ToastStackUI.ToastTier.Discovery );
			DiscoveryToastUI.NotifyMessage( "Display Complete: Coins", ToastStackUI.ToastTier.Completion );
			PlayUnlockGlide();
			UnlockRewardToastUI.NotifyMessage( "Island complete — platforms activated", null, ToastStackUI.ToastTier.Milestone );
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

	static void PlayUnlockSwiftHands()
	{
		UpgradeDefinition upgrade = ResolveSwiftHandsUpgrade();
		if ( upgrade != null )
		{
			UnlockRewardToastUI.NotifyUnlock( upgrade );
			return;
		}

		UnlockRewardToastUI.NotifyMessage( "Unlocked: Swift Hands", null, ToastStackUI.ToastTier.Unlock, "Dig and pick up treasure 1.5x faster." );
	}

	static UpgradeDefinition ResolveSwiftHandsUpgrade()
	{
		UpgradeSystem system = UpgradeSystem.Instance;
		if ( system == null )
			return null;
		if ( system.TryGetDefinition( UpgradeDefinition.DigPickupSpeedId, out UpgradeDefinition definition ) )
			return definition;
		return null;
	}

	static Sprite ResolveGlideIcon()
	{
		AbilityDefinition glide = ResolveGlideAbility();
		return glide != null ? glide.icon : null;
	}
}
