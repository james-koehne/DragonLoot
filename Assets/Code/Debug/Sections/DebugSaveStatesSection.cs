using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

public class DebugSaveStatesSection : DebugOverlaySection
{
	readonly DebugOverlay _host;
	static string s_newSlotName = "checkpoint";
	static string s_filter = string.Empty;
	static Vector2 s_scroll;

	public DebugSaveStatesSection( DebugOverlay host )
	{
		_host = host;
	}

	public string Title => "Save States";

	public void Draw()
	{
		GUILayout.BeginHorizontal();
		if ( GUILayout.Button( "Quick Save (F5)" ) )
			QuickSave();
		if ( GUILayout.Button( "Quick Load (F9)" ) )
			_ = QuickLoadAsync( _host );
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		s_newSlotName = GUILayout.TextField( s_newSlotName ?? string.Empty );
		if ( GUILayout.Button( "Save As", GUILayout.Width( 72f ) ) )
		{
			string label = string.IsNullOrWhiteSpace( s_newSlotName ) ? "checkpoint" : s_newSlotName.Trim();
			string slotId = GameSavePaths.SanitizeSlotId( label );
			if ( GameSaveSystem.TryWriteSlot( slotId, label, out string error ) )
				DebugOverlay.SetLastAction( "Saved slot: " + slotId );
			else
				DebugOverlay.SetLastAction( "Save failed: " + error );
		}
		GUILayout.EndHorizontal();

		List<GameSaveSlotInfo> slots = GameSaveSystem.ListSlots();
		if ( slots.Count > 6 )
			DebugOverlayGui.DrawSearchField( ref s_filter, "Filter" );

		if ( slots.Count == 0 )
		{
			GUILayout.Label( "No save slots yet" );
			return;
		}

		string filter = s_filter != null ? s_filter.Trim() : string.Empty;
		s_scroll = GUILayout.BeginScrollView( s_scroll, GUILayout.MaxHeight( 220f ) );
		for ( int i = 0; i < slots.Count; i++ )
		{
			GameSaveSlotInfo slot = slots[ i ];
			if ( slot == null || string.IsNullOrEmpty( slot.slotId ) )
				continue;

			if ( !string.IsNullOrEmpty( filter ) &&
			     slot.slotId.IndexOf( filter, System.StringComparison.OrdinalIgnoreCase ) < 0 &&
			     ( slot.label == null || slot.label.IndexOf( filter, System.StringComparison.OrdinalIgnoreCase ) < 0 ) )
				continue;

			GUILayout.BeginVertical( GUI.skin.box );
			GUILayout.Label( slot.label + "  [" + slot.slotId + "]" );
			if ( !string.IsNullOrEmpty( slot.savedAtUtc ) )
				GUILayout.Label( slot.savedAtUtc );

			GUILayout.BeginHorizontal();
			if ( GUILayout.Button( "Load" ) )
			{
				DebugOverlay.SetLastAction( "Loading: " + slot.slotId );
				if ( _host != null )
					_host.LoadSaveSlot( slot.slotId );
				else
					_ = GameSaveSystem.RequestLoad( slot.slotId );
			}

			if ( GUILayout.Button( "Overwrite" ) )
			{
				if ( GameSaveSystem.TryWriteSlot( slot.slotId, slot.label, out string overwriteError ) )
					DebugOverlay.SetLastAction( "Overwrote: " + slot.slotId );
				else
					DebugOverlay.SetLastAction( "Overwrite failed: " + overwriteError );
			}

			if ( GUILayout.Button( "Delete" ) )
			{
				if ( GameSaveSystem.TryDeleteSlot( slot.slotId, out string deleteError ) )
					DebugOverlay.SetLastAction( "Deleted: " + slot.slotId );
				else
					DebugOverlay.SetLastAction( "Delete failed: " + deleteError );
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}
		GUILayout.EndScrollView();
	}

	public static void QuickSave()
	{
		if ( GameSaveSystem.TryQuickSave( out string error ) )
			DebugOverlay.SetLastAction( "Quick Save ok" );
		else
			DebugOverlay.SetLastAction( "Quick Save failed: " + error );
	}

	public static async Task QuickLoadAsync( DebugOverlay host = null )
	{
		if ( !GameSaveSystem.TryReadSlot( GameSavePaths.QuickSaveSlotId, out _, out string error ) )
		{
			DebugOverlay.SetLastAction( "Quick Load failed: " + error );
			return;
		}

		DebugOverlay.SetLastAction( "Quick Loading..." );
		if ( host != null )
			host.LoadSaveSlot( GameSavePaths.QuickSaveSlotId );
		else
			await GameSaveSystem.RequestLoad( GameSavePaths.QuickSaveSlotId );
	}
}
