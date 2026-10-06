using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Blueprint-styled hover card for the aimed buildable ghost in build mode.
/// Prefab-wired only — no runtime hierarchy creation.
/// </summary>
public class BuildableHoverUI : MonoBehaviour
{
	[SerializeField] CanvasGroup _group;
	[SerializeField] Text _nameText;
	[SerializeField] Text _descriptionText;
	[SerializeField] Text _costText;

	bool _ready;
	BuildableObject _shown;

	public void Setup()
	{
		_ready = true;
		_shown = null;
		SetVisible( false );
		ClearLabels();
	}

	void Update()
	{
		if ( !_ready )
			return;

		Refresh();
	}

	void Refresh()
	{
		if ( GameMode.Instance == null || GameMode.Instance.Player == null )
		{
			Hide();
			return;
		}

		PlayerBuildMode buildMode = GameMode.Instance.Player.BuildMode;
		if ( buildMode == null || !buildMode.IsActive )
		{
			Hide();
			return;
		}

		BuildableObject aimed = buildMode.AimedBuildable;
		if ( aimed == null )
		{
			Hide();
			return;
		}

		if ( aimed != _shown )
		{
			_shown = aimed;
			ApplyLabels( aimed );
		}

		SetVisible( true );
	}

	void ApplyLabels( BuildableObject buildable )
	{
		if ( _nameText != null )
			_nameText.text = buildable.ResolveDisplayName();

		if ( _descriptionText != null )
		{
			string description = buildable.ResolveDescription();
			_descriptionText.text = description;
			_descriptionText.gameObject.SetActive( !string.IsNullOrEmpty( description ) );
		}

		if ( _costText != null )
		{
			int cost = buildable.ResolveCost();
			_costText.text = cost <= 0 ? "COST  Free" : "COST  " + cost.ToString();
		}
	}

	void ClearLabels()
	{
		if ( _nameText != null )
			_nameText.text = string.Empty;
		if ( _descriptionText != null )
		{
			_descriptionText.text = string.Empty;
			_descriptionText.gameObject.SetActive( false );
		}
		if ( _costText != null )
			_costText.text = string.Empty;
	}

	void Hide()
	{
		_shown = null;
		SetVisible( false );
	}

	void SetVisible( bool visible )
	{
		if ( _group == null )
			return;

		_group.alpha = visible ? 1f : 0f;
		_group.blocksRaycasts = false;
		_group.interactable = false;
	}
}
