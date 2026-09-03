using System;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Corner contextual tutorial popup. Non-blocking; advances on timer or dismiss.
/// Uses unscaled time so pause-menu replay works.
/// Wire references on the Interface prefab.
/// </summary>
public class TutorialPopupUI : MonoBehaviour
{
	[SerializeField] CanvasGroup group;
	[SerializeField] Text titleText;
	[SerializeField] Text bodyText;
	[SerializeField] Text hintText;
	[SerializeField] Text stepText;
	[SerializeField] Button dismissButton;

	Action _onAdvanced;
	float _hideAt = -1f;
	bool _visible;
	bool _ready;

	public bool IsVisible => _visible;

	public void Setup()
	{
		if ( dismissButton != null )
		{
			dismissButton.onClick.RemoveListener( OnDismissClicked );
			dismissButton.onClick.AddListener( OnDismissClicked );
		}

		HideImmediate();
		_ready = true;
	}

	void Update()
	{
		if ( !_ready || !_visible )
			return;

		if ( _hideAt > 0f && Time.unscaledTime >= _hideAt )
			Advance();
	}

	public void Show( string title, string body, string hint, int stepIndex, int stepCount, Action onAdvanced )
	{
		transform.SetAsLastSibling();
		_onAdvanced = onAdvanced;
		_visible = true;

		if ( titleText != null )
			titleText.text = title ?? string.Empty;
		if ( bodyText != null )
			bodyText.text = body ?? string.Empty;
		if ( hintText != null )
		{
			hintText.text = hint ?? string.Empty;
			hintText.gameObject.SetActive( !string.IsNullOrEmpty( hint ) );
		}

		if ( stepText != null )
		{
			bool multi = stepCount > 1;
			stepText.gameObject.SetActive( multi );
			if ( multi )
				stepText.text = stepIndex.ToString() + " / " + stepCount.ToString();
		}

		if ( group != null )
		{
			group.alpha = 1f;
			group.blocksRaycasts = true;
			group.interactable = true;
		}

		float hold = EstimateReadSeconds( body ) + EstimateReadSeconds( hint ) * 0.35f;
		_hideAt = Time.unscaledTime + hold;
	}

	public void Hide()
	{
		_onAdvanced = null;
		HideImmediate();
	}

	void HideImmediate()
	{
		_visible = false;
		_hideAt = -1f;
		if ( group != null )
		{
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
		}
	}

	void OnDismissClicked()
	{
		Advance();
	}

	void Advance()
	{
		if ( !_visible )
			return;

		Action done = _onAdvanced;
		_onAdvanced = null;
		HideImmediate();
		if ( done != null )
			done();
	}

	static float EstimateReadSeconds( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return 1.75f;
		return Mathf.Clamp( text.Length / 16f, 2.25f, 10f );
	}
}
