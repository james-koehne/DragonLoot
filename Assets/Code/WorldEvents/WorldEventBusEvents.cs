using UnityEngine;

/// <summary>Player entered a <see cref="QuestVolume"/> / <see cref="EventVolume"/>.</summary>
public struct VolumeEnteredEvent
{
	public string VolumeId;
	public QuestVolume Volume;
}

/// <summary>A one-shot world event finished firing.</summary>
public struct WorldEventFiredEvent
{
	public string Id;
	public string[] Tags;
}

/// <summary>Show or clear dragon dialogue line.</summary>
public struct DragonDialogueChangedEvent
{
	public bool Visible;
	public string Speaker;
	public string Text;
	public bool CanSkip;
}

public struct TutorialHudRow
{
	public string ObjectiveId;
	public string Text;
	public int Indent;
	public bool Optional;
	public bool Complete;
	public bool ContextualFocus;
}

/// <summary>Contextual / tutorial objective HUD + marker changed.</summary>
public struct TutorialHudChangedEvent
{
	public string Title;
	public string ObjectiveText;
	public TutorialHudRow[] Rows;
	public bool HasMarker;
	public Vector3 MarkerWorldPosition;
	public bool Cleared;
}

/// <summary>Coin sorter successfully emitted at least one coin.</summary>
public struct CoinSorterUsedEvent
{
	public CoinSortingStation Station;
	public TreasureDefinition Coin;
}

static class TutorialHudDistance
{
	public static string Format( float meters )
	{
		if ( meters < 10f )
			return meters.ToString( "0.0" ) + "m";
		return Mathf.RoundToInt( meters ).ToString() + "m";
	}

	public static float HorizontalTo( Vector3 worldPos )
	{
		Vector3 origin;
		if ( GameMode.Instance != null && GameMode.Instance.Player != null )
			origin = GameMode.Instance.Player.transform.position;
		else if ( GameMode.Instance != null && GameMode.Instance.cameraController != null )
			origin = GameMode.Instance.cameraController.transform.position;
		else if ( Camera.main != null )
			origin = Camera.main.transform.position;
		else
			return 0f;

		Vector3 delta = worldPos - origin;
		delta.y = 0f;
		return delta.magnitude;
	}
}
