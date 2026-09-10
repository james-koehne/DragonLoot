#if UNITY_EDITOR
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor wizard to author a new treasure (any category): visual Addressable + TreasureDefinition + extras.
/// Menu: DragonLoot → Treasure → Create Treasure
/// </summary>
public class TreasureCreationWindow : EditorWindow
{
	TreasureCreationPipeline.Request _request = new TreasureCreationPipeline.Request();
	TreasureDefinition _duplicateFrom;
	TreasureDefinition _lastDuplicateFrom;
	TreasureCategory _lastCategory = TreasureCategory.Artifact;
	bool _displayNameDirty;
	bool _pathsDirty;

	Vector2 _scroll;
	bool _foldStats = true;
	bool _foldPhysics = true;
	bool _foldVisuals = true;
	bool _foldCleaning = true;
	bool _foldStacking = true;
	bool _foldAudio = true;
	bool _foldCategory = true;
	bool _foldOutput = true;

	Editor _previewEditor;
	Object _previewTarget;

	enum CoinStackAs
	{
		Gold,
		Silver,
		Copper,
		Custom
	}

	CoinStackAs _coinStackAs = CoinStackAs.Gold;
	List<ChestContentEntry> _chestContents = new List<ChestContentEntry>();

	[MenuItem( DragonLootMenus.TreasureCreate )]
	public static void Open()
	{
		TreasureCreationWindow window = GetWindow<TreasureCreationWindow>( "Create Treasure" );
		window.minSize = new Vector2( 420f, 520f );
		window.Show();
	}

	void OnEnable()
	{
		if ( string.IsNullOrEmpty( _request.Id ) )
		{
			_request.Category = TreasureCategory.Artifact;
			TreasureCreationPipeline.ApplyCategoryDefaults( _request );
			_lastCategory = _request.Category;
			RefreshDefaultPaths();
		}
	}

	void OnDisable()
	{
		DestroyPreviewEditor();
	}

	void OnGUI()
	{
		_scroll = EditorGUILayout.BeginScrollView( _scroll );

		DrawHeader();
		DrawIdentity();
		DrawSourceVisual();
		DrawCategoryExtras();
		DrawStats();
		DrawPhysics();
		DrawVisuals();
		DrawCleaning();
		DrawStacking();
		DrawAudio();
		DrawOutput();
		DrawCreateButton();

		EditorGUILayout.EndScrollView();
	}

	void DrawHeader()
	{
		EditorGUILayout.LabelField( "Treasure Creation", EditorStyles.boldLabel );
		EditorGUILayout.HelpBox(
			"Creates a visual Addressable prefab and TreasureDefinition. "
			+ "Does not wire piles, museum slots, or quests — assign those references manually.",
			MessageType.Info );

		EditorGUI.BeginChangeCheck();
		_duplicateFrom = (TreasureDefinition)EditorGUILayout.ObjectField(
			"Duplicate From",
			_duplicateFrom,
			typeof( TreasureDefinition ),
			false );
		if ( EditorGUI.EndChangeCheck() && _duplicateFrom != null && _duplicateFrom != _lastDuplicateFrom )
		{
			TreasureCreationPipeline.CopyFromDefinition( _request, _duplicateFrom );
			_lastDuplicateFrom = _duplicateFrom;
			_lastCategory = _request.Category;
			_displayNameDirty = true;
			_pathsDirty = false;
			RefreshDefaultPaths();
			SyncChestContentsFromRequest();
			if ( _request.Category == TreasureCategory.Coin )
				_coinStackAs = ResolveCoinStackAs( _request.Variant );
		}

		EditorGUI.BeginChangeCheck();
		_request.Category = (TreasureCategory)EditorGUILayout.EnumPopup( "Category", _request.Category );
		if ( EditorGUI.EndChangeCheck() && _request.Category != _lastCategory )
		{
			bool wasDuplicating = _duplicateFrom != null;
			_lastCategory = _request.Category;
			if ( !wasDuplicating )
				TreasureCreationPipeline.ApplyCategoryDefaults( _request );
			_pathsDirty = false;
			RefreshDefaultPaths();
			if ( _request.Category == TreasureCategory.Coin )
				_coinStackAs = ResolveCoinStackAs( _request.Variant );
		}
	}

	void DrawIdentity()
	{
		EditorGUILayout.Space( 6f );
		EditorGUILayout.LabelField( "Identity", EditorStyles.boldLabel );

		EditorGUI.BeginChangeCheck();
		string id = EditorGUILayout.TextField( "Id", _request.Id );
		if ( EditorGUI.EndChangeCheck() )
		{
			_request.Id = TreasureCreationPipeline.SanitizeId( id );
			if ( !_displayNameDirty )
				_request.DisplayName = SplitCamel( _request.Id );
			if ( !_pathsDirty )
				RefreshDefaultPaths();
		}

		EditorGUI.BeginChangeCheck();
		_request.DisplayName = EditorGUILayout.TextField( "Display Name", _request.DisplayName );
		if ( EditorGUI.EndChangeCheck() )
			_displayNameDirty = true;

		if ( _request.Category == TreasureCategory.Coin )
		{
			EditorGUI.BeginChangeCheck();
			_coinStackAs = (CoinStackAs)EditorGUILayout.EnumPopup( "Stack As", _coinStackAs );
			if ( EditorGUI.EndChangeCheck() && _coinStackAs != CoinStackAs.Custom )
				_request.Variant = _coinStackAs.ToString();

			if ( _coinStackAs == CoinStackAs.Custom )
				_request.Variant = EditorGUILayout.TextField( "Variant", _request.Variant );

			bool known = IsKnownCoinVariant( _request.Variant );
			if ( !known )
			{
				EditorGUILayout.HelpBox(
					"CoinStackVisualDefinition only matches variants containing Gold, Silver, or Copper. "
					+ "Unknown variants fall back to the gold stack material.",
					MessageType.Warning );
			}
		}
		else
		{
			_request.Variant = EditorGUILayout.TextField( "Variant", _request.Variant );
		}
	}

	void DrawSourceVisual()
	{
		EditorGUILayout.Space( 6f );
		EditorGUILayout.LabelField( "Source Visual", EditorStyles.boldLabel );

		_request.VisualMode = (TreasureCreationPipeline.VisualSourceMode)EditorGUILayout.EnumPopup(
			"Mode",
			_request.VisualMode );

		Object previewObj = null;
		switch ( _request.VisualMode )
		{
			case TreasureCreationPipeline.VisualSourceMode.GenerateFromPrefab:
				_request.SourcePrefab = (GameObject)EditorGUILayout.ObjectField(
					"Source Prefab",
					_request.SourcePrefab,
					typeof( GameObject ),
					false );
				previewObj = _request.SourcePrefab;
				break;
			case TreasureCreationPipeline.VisualSourceMode.GenerateFromMesh:
				_request.SourceMesh = (Mesh)EditorGUILayout.ObjectField(
					"Source Mesh",
					_request.SourceMesh,
					typeof( Mesh ),
					false );
				previewObj = _request.SourceMesh;
				EditorGUILayout.LabelField( "Materials (optional)" );
				int matCount = _request.SourceMaterials != null ? _request.SourceMaterials.Length : 0;
				int newCount = Mathf.Max( 0, EditorGUILayout.IntField( "Count", matCount ) );
				if ( newCount != matCount )
				{
					var next = new Material[ newCount ];
					for ( int i = 0; i < newCount && _request.SourceMaterials != null && i < _request.SourceMaterials.Length; i++ )
						next[ i ] = _request.SourceMaterials[ i ];
					_request.SourceMaterials = next;
				}

				if ( _request.SourceMaterials != null )
				{
					for ( int i = 0; i < _request.SourceMaterials.Length; i++ )
					{
						_request.SourceMaterials[ i ] = (Material)EditorGUILayout.ObjectField(
							"Mat " + i,
							_request.SourceMaterials[ i ],
							typeof( Material ),
							false );
					}
				}
				break;
			case TreasureCreationPipeline.VisualSourceMode.UseExistingVisual:
				_request.ExistingVisualPrefab = (GameObject)EditorGUILayout.ObjectField(
					"Existing Visual",
					_request.ExistingVisualPrefab,
					typeof( GameObject ),
					false );
				previewObj = _request.ExistingVisualPrefab;
				break;
		}

		DrawPreview( previewObj );
	}

	void DrawPreview( Object target )
	{
		if ( target == null )
		{
			DestroyPreviewEditor();
			return;
		}

		if ( _previewTarget != target )
		{
			DestroyPreviewEditor();
			_previewTarget = target;
			_previewEditor = Editor.CreateEditor( target );
		}

		if ( _previewEditor != null )
		{
			Rect rect = GUILayoutUtility.GetRect( 128f, 128f );
			_previewEditor.OnPreviewGUI( rect, EditorStyles.helpBox );
		}
	}

	void DestroyPreviewEditor()
	{
		if ( _previewEditor != null )
			DestroyImmediate( _previewEditor );
		_previewEditor = null;
		_previewTarget = null;
	}

	void DrawCategoryExtras()
	{
		_foldCategory = EditorGUILayout.Foldout( _foldCategory, "Category Options", true );
		if ( !_foldCategory )
			return;

		EditorGUI.indentLevel++;
		switch ( _request.Category )
		{
			case TreasureCategory.Key:
				_request.KeyType = (KeyType)EditorGUILayout.EnumPopup( "Key Type", _request.KeyType );
				_request.IsSkeletonKey = EditorGUILayout.Toggle( "Skeleton Key", _request.IsSkeletonKey );
				break;

			case TreasureCategory.Chest:
			case TreasureCategory.Container:
				_request.ChestType = EditorGUILayout.TextField(
					_request.Category == TreasureCategory.Container ? "Container Type" : "Chest Type",
					_request.ChestType );
				_request.KeyType = (KeyType)EditorGUILayout.EnumPopup( "Required Key", _request.KeyType );
				_request.ChestStartsLocked = EditorGUILayout.Toggle( "Starts Locked", _request.ChestStartsLocked );
				_request.ChestDestroyOnOpen = EditorGUILayout.Toggle(
					"Destroy On Open (barrel / crate)",
					_request.ChestDestroyOnOpen );
				_request.ChestLockpickDuration = EditorGUILayout.FloatField(
					"Lockpick Duration",
					_request.ChestLockpickDuration );
				DrawChestContents();
				break;

			case TreasureCategory.Gem:
				_request.GemMaterial = (Material)EditorGUILayout.ObjectField(
					"Gem Material",
					_request.GemMaterial,
					typeof( Material ),
					false );
				_request.CloneGemMaterial = EditorGUILayout.Toggle(
					"Clone Material Asset",
					_request.CloneGemMaterial );
				break;

			case TreasureCategory.Artifact:
			case TreasureCategory.Crown:
			case TreasureCategory.Goblet:
			case TreasureCategory.Helmet:
				_request.ConvertArtifactMaterials = EditorGUILayout.Toggle(
					"Convert To DragonLoot/Artifact",
					_request.ConvertArtifactMaterials );
				break;
		}

		EditorGUI.indentLevel--;
	}

	void DrawChestContents()
	{
		EditorGUILayout.LabelField( "Contents" );
		int removeIndex = -1;
		for ( int i = 0; i < _chestContents.Count; i++ )
		{
			EditorGUILayout.BeginHorizontal();
			ChestContentEntry entry = _chestContents[ i ];
			entry.treasure = (TreasureDefinition)EditorGUILayout.ObjectField(
				entry.treasure,
				typeof( TreasureDefinition ),
				false );
			entry.count = EditorGUILayout.IntField( entry.count, GUILayout.Width( 48f ) );
			_chestContents[ i ] = entry;
			if ( GUILayout.Button( "X", GUILayout.Width( 22f ) ) )
				removeIndex = i;
			EditorGUILayout.EndHorizontal();
		}

		if ( removeIndex >= 0 )
			_chestContents.RemoveAt( removeIndex );

		if ( GUILayout.Button( "Add Content Entry" ) )
			_chestContents.Add( new ChestContentEntry { count = 1 } );

		_request.ChestContents = _chestContents.ToArray();
	}

	void DrawStats()
	{
		_foldStats = EditorGUILayout.Foldout( _foldStats, "Stats", true );
		if ( !_foldStats )
			return;

		EditorGUI.indentLevel++;
		_request.Value = EditorGUILayout.IntField( "Value", _request.Value );
		_request.Weight = EditorGUILayout.IntField( "Weight", _request.Weight );
		_request.ExclusiveCarry = EditorGUILayout.Toggle( "Exclusive Carry", _request.ExclusiveCarry );
		_request.UsesHeavyThrow = EditorGUILayout.Toggle( "Uses Heavy Throw", _request.UsesHeavyThrow );
		_request.CannotThrow = EditorGUILayout.Toggle( "Cannot Throw", _request.CannotThrow );
		_request.PlaceOnGroundOrArtifactSlotOnly = EditorGUILayout.Toggle(
			"Ground / Artifact Slot Only",
			_request.PlaceOnGroundOrArtifactSlotOnly );
		_request.ThrowForceScale = EditorGUILayout.FloatField( "Throw Force Scale", _request.ThrowForceScale );
		_request.ThrowUpBiasScale = EditorGUILayout.FloatField( "Throw Up Bias Scale", _request.ThrowUpBiasScale );
		EditorGUI.indentLevel--;
	}

	void DrawPhysics()
	{
		_foldPhysics = EditorGUILayout.Foldout( _foldPhysics, "Physics", true );
		if ( !_foldPhysics )
			return;

		EditorGUI.indentLevel++;
		_request.RigidbodyMass = EditorGUILayout.FloatField( "Mass", _request.RigidbodyMass );
		_request.Drag = EditorGUILayout.FloatField( "Drag", _request.Drag );
		_request.AngularDrag = EditorGUILayout.FloatField( "Angular Drag", _request.AngularDrag );
		_request.SleepThreshold = EditorGUILayout.FloatField( "Sleep Threshold", _request.SleepThreshold );
		_request.AutoToppleStrength = EditorGUILayout.FloatField( "Auto Topple", _request.AutoToppleStrength );
		_request.MaxUprightAngle = EditorGUILayout.Slider( "Max Upright Angle", _request.MaxUprightAngle, 0f, 90f );
		_request.PhysicsStabilizationDelay = EditorGUILayout.FloatField(
			"Stabilization Delay",
			_request.PhysicsStabilizationDelay );
		_request.PickupRadius = EditorGUILayout.FloatField( "Pickup Radius", _request.PickupRadius );
		_request.CollideWithPlayerOnPile = EditorGUILayout.Toggle(
			"Collide With Player On Pile",
			_request.CollideWithPlayerOnPile );
		EditorGUI.indentLevel--;
	}

	void DrawVisuals()
	{
		_foldVisuals = EditorGUILayout.Foldout( _foldVisuals, "Visuals", true );
		if ( !_foldVisuals )
			return;

		EditorGUI.indentLevel++;
		_request.Icon = (Sprite)EditorGUILayout.ObjectField( "Icon", _request.Icon, typeof( Sprite ), false );
		_request.MeshOverride = (Mesh)EditorGUILayout.ObjectField(
			"Mesh Override",
			_request.MeshOverride,
			typeof( Mesh ),
			false );
		_request.MaterialOverride = (Material)EditorGUILayout.ObjectField(
			"Material Override",
			_request.MaterialOverride,
			typeof( Material ),
			false );
		_request.WorldScale = EditorGUILayout.Vector3Field( "World Scale", _request.WorldScale );
		_request.HeldScale = EditorGUILayout.Vector3Field( "Held Scale", _request.HeldScale );
		_request.ActiveHeldLocalEuler = EditorGUILayout.Vector3Field(
			"Active Held Euler",
			_request.ActiveHeldLocalEuler );
		_request.HeldStackLocalEuler = EditorGUILayout.Vector3Field(
			"Held Stack Euler",
			_request.HeldStackLocalEuler );
		EditorGUI.indentLevel--;
	}

	void DrawCleaning()
	{
		_foldCleaning = EditorGUILayout.Foldout( _foldCleaning, "Cleaning", true );
		if ( !_foldCleaning )
			return;

		EditorGUI.indentLevel++;
		_request.CleaningRequirement = (TreasureCleaningRequirement)EditorGUILayout.EnumPopup(
			"Requirement",
			_request.CleaningRequirement );
		EditorGUI.indentLevel--;
	}

	void DrawStacking()
	{
		_foldStacking = EditorGUILayout.Foldout( _foldStacking, "Stacking", true );
		if ( !_foldStacking )
			return;

		EditorGUI.indentLevel++;
		_request.CanStack = EditorGUILayout.Toggle( "Can Stack", _request.CanStack );
		_request.CoinThickness = EditorGUILayout.FloatField( "Coin Thickness", _request.CoinThickness );
		_request.CartGridSize = EditorGUILayout.Vector2IntField( "Cart Grid Size", _request.CartGridSize );
		EditorGUI.indentLevel--;
	}

	void DrawAudio()
	{
		_foldAudio = EditorGUILayout.Foldout( _foldAudio, "Audio", true );
		if ( !_foldAudio )
			return;

		EditorGUI.indentLevel++;
		_request.PickupClips = DrawClipArray( "Pickup Clips", _request.PickupClips );
		_request.PickupVolumeMin = EditorGUILayout.Slider( "Pickup Vol Min", _request.PickupVolumeMin, 0f, 1f );
		_request.PickupVolumeMax = EditorGUILayout.Slider( "Pickup Vol Max", _request.PickupVolumeMax, 0f, 1f );
		_request.PickupPitchMin = EditorGUILayout.Slider( "Pickup Pitch Min", _request.PickupPitchMin, -3f, 3f );
		_request.PickupPitchMax = EditorGUILayout.Slider( "Pickup Pitch Max", _request.PickupPitchMax, -3f, 3f );

		_request.PlaceClips = DrawClipArray( "Place Clips", _request.PlaceClips );
		_request.PlaceVolumeMin = EditorGUILayout.Slider( "Place Vol Min", _request.PlaceVolumeMin, 0f, 1f );
		_request.PlaceVolumeMax = EditorGUILayout.Slider( "Place Vol Max", _request.PlaceVolumeMax, 0f, 1f );
		_request.PlacePitchMin = EditorGUILayout.Slider( "Place Pitch Min", _request.PlacePitchMin, -3f, 3f );
		_request.PlacePitchMax = EditorGUILayout.Slider( "Place Pitch Max", _request.PlacePitchMax, -3f, 3f );
		EditorGUI.indentLevel--;
	}

	static AudioClip[] DrawClipArray( string label, AudioClip[] clips )
	{
		int count = clips != null ? clips.Length : 0;
		int newCount = Mathf.Max( 0, EditorGUILayout.IntField( label + " Count", count ) );
		if ( newCount != count )
		{
			var next = new AudioClip[ newCount ];
			for ( int i = 0; i < newCount && clips != null && i < clips.Length; i++ )
				next[ i ] = clips[ i ];
			clips = next;
		}

		if ( clips != null )
		{
			for ( int i = 0; i < clips.Length; i++ )
			{
				clips[ i ] = (AudioClip)EditorGUILayout.ObjectField(
					"  [" + i + "]",
					clips[ i ],
					typeof( AudioClip ),
					false );
			}
		}

		return clips;
	}

	void DrawOutput()
	{
		_foldOutput = EditorGUILayout.Foldout( _foldOutput, "Output Paths", true );
		if ( !_foldOutput )
			return;

		EditorGUI.indentLevel++;
		EditorGUI.BeginChangeCheck();
		_request.VisualPathOverride = EditorGUILayout.TextField( "Visual Path", ResolveVisualPath() );
		_request.DefinitionPathOverride = EditorGUILayout.TextField( "Definition Path", ResolveDefinitionPath() );
		if ( TreasureCreationPipeline.UsesChestDefinition( _request.Category ) )
		{
			_request.ChestDefinitionPathOverride = EditorGUILayout.TextField(
				"Chest Def Path",
				ResolveChestDefinitionPath() );
		}

		if ( EditorGUI.EndChangeCheck() )
			_pathsDirty = true;

		bool conflict = AssetExists( ResolveVisualPath() ) || AssetExists( ResolveDefinitionPath() );
		if ( conflict )
		{
			EditorGUILayout.HelpBox(
				"One or more output assets already exist. Create will ask to overwrite.",
				MessageType.Warning );
		}

		EditorGUI.indentLevel--;
	}

	void DrawCreateButton()
	{
		EditorGUILayout.Space( 12f );
		string validation = TreasureCreationPipeline.Validate( _request );
		using ( new EditorGUI.DisabledScope( validation != null ) )
		{
			if ( GUILayout.Button( "Create Treasure", GUILayout.Height( 32f ) ) )
				RunCreate();
		}

		if ( validation != null )
			EditorGUILayout.HelpBox( validation, MessageType.Error );
	}

	void RunCreate()
	{
		_request.VisualPathOverride = ResolveVisualPath();
		_request.DefinitionPathOverride = ResolveDefinitionPath();
		if ( TreasureCreationPipeline.UsesChestDefinition( _request.Category ) )
			_request.ChestDefinitionPathOverride = ResolveChestDefinitionPath();
		_request.ChestContents = _chestContents.ToArray();

		bool needsOverwrite = AssetExists( _request.VisualPathOverride )
			|| AssetExists( _request.DefinitionPathOverride );
		if ( needsOverwrite )
		{
			bool ok = EditorUtility.DisplayDialog(
				"Overwrite Treasure Assets?",
				"Assets already exist at:\n"
				+ _request.VisualPathOverride + "\n"
				+ _request.DefinitionPathOverride
				+ "\n\nOverwrite?",
				"Overwrite",
				"Cancel" );
			if ( !ok )
				return;
		}

		TreasureCreationPipeline.Result result = TreasureCreationPipeline.CreateOrUpdate(
			_request,
			overwriteConfirmed: true );
		if ( !result.Success )
		{
			EditorUtility.DisplayDialog( "Create Treasure Failed", result.Error, "OK" );
			return;
		}

		Debug.Log( "TreasureCreationWindow: " + result.Summary );
		EditorUtility.DisplayDialog( "Treasure Created", result.Summary, "OK" );
		if ( result.Definition != null )
		{
			EditorGUIUtility.PingObject( result.Definition );
			Selection.activeObject = result.Definition;
		}
	}

	void RefreshDefaultPaths()
	{
		string id = TreasureCreationPipeline.SanitizeId( _request.Id );
		if ( string.IsNullOrEmpty( id ) )
			id = "NewTreasure";
		_request.VisualPathOverride = TreasureCreationPipeline.DefaultVisualPath( id, _request.Category );
		_request.DefinitionPathOverride = TreasureCreationPipeline.DefaultDefinitionPath( id, _request.Category );
		_request.ChestDefinitionPathOverride = TreasureCreationPipeline.DefaultChestDefinitionPath( id );
	}

	string ResolveVisualPath()
	{
		if ( !string.IsNullOrEmpty( _request.VisualPathOverride ) )
			return _request.VisualPathOverride;
		string id = TreasureCreationPipeline.SanitizeId( _request.Id );
		if ( string.IsNullOrEmpty( id ) )
			id = "NewTreasure";
		return TreasureCreationPipeline.DefaultVisualPath( id, _request.Category );
	}

	string ResolveDefinitionPath()
	{
		if ( !string.IsNullOrEmpty( _request.DefinitionPathOverride ) )
			return _request.DefinitionPathOverride;
		string id = TreasureCreationPipeline.SanitizeId( _request.Id );
		if ( string.IsNullOrEmpty( id ) )
			id = "NewTreasure";
		return TreasureCreationPipeline.DefaultDefinitionPath( id, _request.Category );
	}

	string ResolveChestDefinitionPath()
	{
		if ( !string.IsNullOrEmpty( _request.ChestDefinitionPathOverride ) )
			return _request.ChestDefinitionPathOverride;
		string id = TreasureCreationPipeline.SanitizeId( _request.Id );
		if ( string.IsNullOrEmpty( id ) )
			id = "NewTreasure";
		return TreasureCreationPipeline.DefaultChestDefinitionPath( id );
	}

	void SyncChestContentsFromRequest()
	{
		_chestContents.Clear();
		if ( _request.ChestContents == null )
			return;
		for ( int i = 0; i < _request.ChestContents.Length; i++ )
			_chestContents.Add( _request.ChestContents[ i ] );
	}

	static bool AssetExists( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return false;
		return AssetDatabase.LoadMainAssetAtPath( path ) != null;
	}

	static bool IsKnownCoinVariant( string variant )
	{
		if ( string.IsNullOrEmpty( variant ) )
			return false;
		return variant.IndexOf( "gold", System.StringComparison.OrdinalIgnoreCase ) >= 0
			|| variant.IndexOf( "silver", System.StringComparison.OrdinalIgnoreCase ) >= 0
			|| variant.IndexOf( "copper", System.StringComparison.OrdinalIgnoreCase ) >= 0;
	}

	static CoinStackAs ResolveCoinStackAs( string variant )
	{
		if ( string.IsNullOrEmpty( variant ) )
			return CoinStackAs.Gold;
		if ( variant.IndexOf( "silver", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return CoinStackAs.Silver;
		if ( variant.IndexOf( "copper", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return CoinStackAs.Copper;
		if ( variant.IndexOf( "gold", System.StringComparison.OrdinalIgnoreCase ) >= 0 )
			return CoinStackAs.Gold;
		return CoinStackAs.Custom;
	}

	static string SplitCamel( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return id;

		var chars = new System.Text.StringBuilder( id.Length + 4 );
		for ( int i = 0; i < id.Length; i++ )
		{
			char c = id[ i ];
			if ( i > 0 && char.IsUpper( c ) && ( char.IsLower( id[ i - 1 ] ) || ( i + 1 < id.Length && char.IsLower( id[ i + 1 ] ) ) ) )
				chars.Append( ' ' );
			if ( c == '_' )
			{
				chars.Append( ' ' );
				continue;
			}

			chars.Append( c );
		}

		return chars.ToString().Trim();
	}
}
#endif
