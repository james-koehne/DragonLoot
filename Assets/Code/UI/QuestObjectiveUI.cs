using System.Text;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Current quest title + hierarchical objective list. Wire references on the Interface prefab.
/// </summary>
public class QuestObjectiveUI : MonoBehaviour
{
	[SerializeField] CanvasGroup group;
	[SerializeField] Text title;
	[SerializeField] Text objective;

	bool _subscribed;

	public void Setup()
	{
		Subscribe();
		if ( group != null )
			group.alpha = 0f;
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void OnDestroy()
	{
		Unsubscribe();
	}

	void Subscribe()
	{
		if ( _subscribed )
			return;
		EventBus.Subscribe<QuestHudChangedEvent>( OnHudChanged );
		_subscribed = true;
	}

	void Unsubscribe()
	{
		if ( !_subscribed )
			return;
		EventBus.Unsubscribe<QuestHudChangedEvent>( OnHudChanged );
		_subscribed = false;
	}

	void OnHudChanged( QuestHudChangedEvent evt )
	{
		bool show = !evt.CatalogComplete && HasRows( evt );
		if ( group != null )
			group.alpha = show ? 1f : 0f;

		if ( title != null )
			title.text = string.IsNullOrEmpty( evt.QuestTitle ) ? "Quest" : evt.QuestTitle;
		if ( objective != null )
			objective.text = FormatRows( evt );
	}

	static bool HasRows( QuestHudChangedEvent evt )
	{
		if ( evt.Rows != null && evt.Rows.Length > 0 )
			return true;
		return !string.IsNullOrEmpty( evt.ObjectiveText );
	}

	static string FormatRows( QuestHudChangedEvent evt )
	{
		if ( evt.Rows == null || evt.Rows.Length == 0 )
			return evt.ObjectiveText ?? string.Empty;

		StringBuilder sb = new StringBuilder();
		for ( int i = 0; i < evt.Rows.Length; i++ )
		{
			QuestHudRow row = evt.Rows[ i ];
			if ( i > 0 )
				sb.Append( '\n' );
			for ( int n = 0; n < row.Indent; n++ )
				sb.Append( "  " );
			if ( row.Complete )
				sb.Append( "<color=#9ad89a>✓ " );
			else
				sb.Append( "• " );
			sb.Append( row.Text ?? string.Empty );
			if ( row.Optional )
				sb.Append( " (optional)" );
			if ( row.Complete )
				sb.Append( "</color>" );
		}

		return sb.ToString();
	}
}
