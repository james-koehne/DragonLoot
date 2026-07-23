#if STEAM_BUILD

using Steamworks;

using TMPro;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Settings;

/// <summary>
/// Steam virtual (gamepad) keyboard support. Use the static API to show the keyboard for any
/// TMP_InputField, or add this component and assign a field to bind on select.
/// </summary>
public class SteamKeyboard : MonoBehaviour
{
	private static Callback<GamepadTextInputDismissed_t> s_callback;
	private static Callback<GameOverlayActivated_t> s_overlayCallback;
	private static Callback<FloatingGamepadTextInputDismissed_t> s_floatingCallback;
	private static TMP_InputField s_pendingTarget;
	private static bool s_usedFloatingKeyboard;

	[SerializeField] private TMP_InputField inputField;
	[SerializeField] private string description = "Enter text";
	[SerializeField] private int maxChars = 64;
	[SerializeField] private string descriptionLocalizationKey;

	private void OnEnable()
	{
		if ( inputField != null )
			inputField.onSelect.AddListener( OnFieldSelected );
	}

	private void OnDisable()
	{
		if ( inputField != null )
			inputField.onSelect.RemoveListener( OnFieldSelected );
		if ( s_pendingTarget == inputField )
			s_pendingTarget = null;
	}

	private void OnFieldSelected( string _ )
	{
		ShowForField( inputField, GetDescription(), maxChars );
	}

	private string GetDescription()
	{
		if ( !string.IsNullOrEmpty( descriptionLocalizationKey ) )
		{
			string localized = LocalizationSettings.StringDatabase.GetLocalizedString( descriptionLocalizationKey );
			if ( !string.IsNullOrEmpty( localized ) )
				return localized;
		}
		return description;
	}

	/// <summary>
	/// Show Steam virtual keyboard and write the result into the given field when dismissed.
	/// No-op if Steam is not enabled or not initialized.
	/// </summary>
	public static void ShowForField( TMP_InputField field, string description, int maxChars = 64 )
	{
		if ( field == null )
			return;
		if ( GameInstance.gameDef == null || !GameInstance.gameDef.SteamEnabled
			|| SteamManager.Instance == null || !SteamManager.Instance.Initialized )
			return;

		if ( string.IsNullOrEmpty( description ) )
			description = "Enter text";

		EnsureCallbacks();

		bool shown = false;
		s_usedFloatingKeyboard = false;

		EnsureFloatingCallback();
		GetTextFieldScreenRect( field, out int x, out int y, out int w, out int h );
		shown = SteamUtils.ShowFloatingGamepadTextInput(
			EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeSingleLine,
			x, y, w, h );
		if ( shown )
			s_usedFloatingKeyboard = true;

		if ( !shown )
		{
			shown = SteamUtils.ShowGamepadTextInput(
				EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
				EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine,
				description,
				(uint)maxChars,
				field.text ?? string.Empty );
		}

		if ( shown )
		{
			s_pendingTarget = field;
			if ( !s_usedFloatingKeyboard )
				field.DeactivateInputField();
			if ( GameMode.Instance != null && GameMode.Instance.InputController != null )
				GameMode.Instance.InputController.SetInputEnabled( false );
		}
	}

	private static void GetTextFieldScreenRect( TMP_InputField field, out int x, out int y, out int w, out int h )
	{
		RectTransform rt = field.GetComponent<RectTransform>();
		Canvas canvas = field.GetComponentInParent<Canvas>();
		Camera cam = canvas != null ? canvas.worldCamera : null;
		if ( cam == null )
			cam = Camera.main;
		Vector3[] corners = new Vector3[ 4 ];
		rt.GetWorldCorners( corners );
		if ( cam != null )
		{
			Vector2 min = RectTransformUtility.WorldToScreenPoint( cam, corners[ 0 ] );
			Vector2 max = RectTransformUtility.WorldToScreenPoint( cam, corners[ 2 ] );
			x = (int)min.x;
			y = (int)( Screen.height - max.y );
			w = Mathf.Max( 1, (int)( max.x - min.x ) );
			h = Mathf.Max( 1, (int)( max.y - min.y ) );
		}
		else
		{
			x = Screen.width / 4;
			y = Screen.height / 2;
			w = Screen.width / 2;
			h = 40;
		}
	}

	private static void EnsureFloatingCallback()
	{
		if ( s_floatingCallback == null )
			s_floatingCallback = Callback<FloatingGamepadTextInputDismissed_t>.Create( OnFloatingDismissed );
	}

	private static void OnFloatingDismissed( FloatingGamepadTextInputDismissed_t callback )
	{
		if ( GameMode.Instance != null && GameMode.Instance.InputController != null )
			GameMode.Instance.InputController.SetInputEnabled( true );
		s_usedFloatingKeyboard = false;
		if ( s_pendingTarget != null )
		{
			TMP_InputField target = s_pendingTarget;
			s_pendingTarget = null;
			DeselectPendingTarget( target );
		}
	}

	/// <summary>
	/// Call once at startup when Steam is enabled so overlay open/close disables game input.
	/// </summary>
	public static void EnsureOverlayCallback()
	{
		if ( s_overlayCallback == null )
			s_overlayCallback = Callback<GameOverlayActivated_t>.Create( OnOverlayActivated );
	}

	private static void EnsureCallbacks()
	{
		EnsureOverlayCallback();
		if ( s_callback == null )
			s_callback = Callback<GamepadTextInputDismissed_t>.Create( OnDismissed );
	}

	private static void OnOverlayActivated( GameOverlayActivated_t callback )
	{
		bool overlayOpen = callback.m_bActive != 0;
		if ( GameMode.Instance != null && GameMode.Instance.InputController != null )
			GameMode.Instance.InputController.SetInputEnabled( !overlayOpen );
		if ( !overlayOpen )
			DeselectPendingTarget();
	}

	private static void OnDismissed( GamepadTextInputDismissed_t callback )
	{
		if ( GameMode.Instance != null && GameMode.Instance.InputController != null )
			GameMode.Instance.InputController.SetInputEnabled( true );

		if ( s_pendingTarget == null )
			return;

		TMP_InputField target = s_pendingTarget;
		s_pendingTarget = null;

		if ( !s_usedFloatingKeyboard && callback.m_bSubmitted && SteamUtils.GetEnteredGamepadTextInput( out string text, callback.m_unSubmittedText + 1 ) )
			target.text = text ?? string.Empty;

		DeselectPendingTarget( target );
	}

	private static void DeselectPendingTarget( TMP_InputField target = null )
	{
		TMP_InputField field = target ?? s_pendingTarget;
		if ( field != null )
		{
			field.DeactivateInputField();
			if ( EventSystem.current != null )
				EventSystem.current.SetSelectedGameObject( null );
		}
		if ( target == null )
			s_pendingTarget = null;
	}
}

#else

using TMPro;

using UnityEngine;

/// <summary>Stub when <c>STEAM_BUILD</c> is not defined (Stove-only builds).</summary>
public class SteamKeyboard : MonoBehaviour
{
	public static void ShowForField( TMP_InputField field, string description, int maxChars = 64 )
	{
	}

	public static void EnsureOverlayCallback()
	{
	}
}

#endif
