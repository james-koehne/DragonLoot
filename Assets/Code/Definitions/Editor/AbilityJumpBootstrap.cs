#if UNITY_EDITOR
using UnityEditor;

using UnityEngine;

/// <summary>
/// Ensures passive Jump ability exists and is registered on <see cref="AbilityCatalogDefinition"/>.
/// </summary>
public static class AbilityJumpBootstrap
{
	const string AbilitiesFolder = "Assets/Definitions/Abilities";
	const string JumpPath = "Assets/Definitions/Abilities/Ability_Jump.asset";
	const string CatalogPath = "Assets/Definitions/AbilityCatalogDefinition.asset";
	const string JumpId = "jump";

	[InitializeOnLoadMethod]
	static void QueueEnsure()
	{
		EditorApplication.delayCall += EnsureJumpAbility;
	}

	[MenuItem( DragonLootMenus.DefinitionsEnsureJumpAbility )]
	static void MenuEnsureJumpAbility()
	{
		EnsureJumpAbility();
		Debug.Log( "Ability_Jump ensured and registered on AbilityCatalogDefinition." );
	}

	static void EnsureJumpAbility()
	{
		AddressableEditorUtil.EnsureFolder( AbilitiesFolder );

		AbilityDefinition jump = AssetDatabase.LoadAssetAtPath<AbilityDefinition>( JumpPath );
		if ( jump == null )
		{
			jump = ScriptableObject.CreateInstance<AbilityDefinition>();
			jump.name = "Ability_Jump";
			jump.id = JumpId;
			jump.displayName = "Jump";
			jump.description = "Leap into the air. No fall damage.";
			jump.cooldown = 0f;
			jump.enabled = true;
			jump.isPassive = true;
			AssetDatabase.CreateAsset( jump, JumpPath );
			AssetDatabase.SaveAssets();
		}
		else
		{
			bool dirty = false;
			if ( jump.id != JumpId )
			{
				jump.id = JumpId;
				dirty = true;
			}
			if ( string.IsNullOrEmpty( jump.displayName ) )
			{
				jump.displayName = "Jump";
				dirty = true;
			}
			if ( !jump.isPassive )
			{
				jump.isPassive = true;
				dirty = true;
			}
			if ( dirty )
			{
				EditorUtility.SetDirty( jump );
				AssetDatabase.SaveAssets();
			}
		}

		AbilityCatalogDefinition catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalogDefinition>( CatalogPath );
		if ( catalog == null )
			return;

		if ( catalog.abilities == null )
			catalog.abilities = new System.Collections.Generic.List<AbilityDefinition>();

		for ( int i = 0; i < catalog.abilities.Count; i++ )
		{
			AbilityDefinition entry = catalog.abilities[ i ];
			if ( entry == jump || ( entry != null && entry.id == JumpId ) )
				return;
		}

		catalog.abilities.Add( jump );
		EditorUtility.SetDirty( catalog );
		AssetDatabase.SaveAssets();
	}
}
#endif
