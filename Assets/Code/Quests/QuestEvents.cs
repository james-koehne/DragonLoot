using UnityEngine;

/// <summary>Player entered a quest trigger volume.</summary>
public struct QuestVolumeEnteredEvent
{
	public string VolumeId;
	public QuestVolume Volume;
}

/// <summary>Coin sorter successfully emitted at least one coin.</summary>
public struct CoinSorterUsedEvent
{
	public CoinSortingStation Station;
	public TreasureDefinition Coin;
}

public struct QuestHudRow
{
	public string Text;
	public int Indent;
	public bool Optional;
	public bool Complete;
}

/// <summary>Active quest / objective / marker changed.</summary>
public struct QuestHudChangedEvent
{
	public string QuestId;
	public string QuestTitle;
	public string StepId;
	public string ObjectiveText;
	public QuestHudRow[] Rows;
	public bool HasMarker;
	public Vector3 MarkerWorldPosition;
	public bool CatalogComplete;
}

/// <summary>Show or clear dragon dialogue line.</summary>
public struct QuestDialogueChangedEvent
{
	public bool Visible;
	public string Speaker;
	public string Text;
	public bool CanSkip;
}

/// <summary>Quest advanced (step or quest completed).</summary>
public struct QuestProgressChangedEvent
{
	public string ActiveQuestId;
	public int ActiveStepIndex;
	public bool CatalogComplete;
}

static class QuestHudDistance
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
