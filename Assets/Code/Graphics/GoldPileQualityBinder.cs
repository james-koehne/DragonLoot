using UnityEngine;

/// <summary>
/// Applies <see cref="GoldPileQuality"/> to this renderer's Gold Pile materials on enable
/// and when Unity quality level changes.
/// </summary>
[DisallowMultipleComponent]
public class GoldPileQualityBinder : MonoBehaviour
{
	[SerializeField]
	Renderer targetRenderer;

	void Awake()
	{
		if (targetRenderer == null)
			targetRenderer = GetComponent<Renderer>();
	}

	void OnEnable()
	{
		Apply();
#if UNITY_2022_2_OR_NEWER
		QualitySettings.activeQualityLevelChanged += OnQualityChanged;
#endif
	}

	void OnDisable()
	{
#if UNITY_2022_2_OR_NEWER
		QualitySettings.activeQualityLevelChanged -= OnQualityChanged;
#endif
	}

#if UNITY_2022_2_OR_NEWER
	void OnQualityChanged(int previous, int current)
	{
		Apply();
	}
#endif

	public void Apply()
	{
		GoldPileQuality.Apply(targetRenderer);
	}
}
