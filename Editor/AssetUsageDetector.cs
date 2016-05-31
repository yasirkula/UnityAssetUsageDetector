// Asset Usage Detector
// Original code by: Suleyman Yasir KULA (yasirkula@yahoo.com)
// Finds objects that refer to selected asset (or GameObject)
// 
// Limitations:
// - GUIText materials can not be searched (could not find a property that does not leak material)
// - Textures in Flare's can not be searched (no property to access the texture?)
// - "static" variables are not searched
// - also "transform", "rectTransform", "gameObject" and "attachedRigidbody" properties in components
//   are not searched (to yield more relevant results)
// - found a bug? Let me know on Unity forums!

using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public enum AssetType { Texture, MultipleSprite, Material, Script, Shader, Animation, GameObject, Other };
public enum Phase { Setup, Processing, Complete };

// Custom class to hold the results for a single scene (or Assets folder)
public class SceneObjectReferences
{
	public string scenePath;
	public List<Object> references;
	
	public SceneObjectReferences()
	{
		scenePath = null; // Project View (Assets)
		references = new List<Object>();
	}

	public SceneObjectReferences( string scenePath )
	{
		this.scenePath = scenePath;
		references = new List<Object>();
	}

	// Draw the results found for this scene
	public void DrawOnGUI()
	{
		Color c = GUI.color;
		GUI.color = Color.cyan;

		if( scenePath == null )
			GUILayout.Box( "Project View (Assets)", AssetUsageDetector.boxGUIStyle, GUILayout.ExpandWidth( true ), GUILayout.Height( 40 ) );
		else if( GUILayout.Button( scenePath, AssetUsageDetector.boxGUIStyle, GUILayout.ExpandWidth( true ), GUILayout.Height( 40 ) ) )
		{
			// If scene name is clicked, highlight it on Project view
			EditorGUIUtility.PingObject( AssetDatabase.LoadAssetAtPath<SceneAsset>( scenePath ) );
			Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>( scenePath );
		}

		GUI.color = Color.yellow;

		for( int i = 0; i < references.Count; i++ )
		{
			GUILayout.Space( 5 );

			if( GUILayout.Button( references[i].ToString(), AssetUsageDetector.boxGUIStyle, GUILayout.ExpandWidth( true ) ) )
			{
				// If a reference is clicked, highlight it (either on Hierarchy view or Project view)
				EditorGUIUtility.PingObject( references[i] );
				Selection.activeObject = references[i];
			}
		}

		GUI.color = c;

		GUILayout.Space( 10 );
	}
}

public class AssetUsageDetector : EditorWindow
{
	private Object assetToSearch;
    private List<Sprite> assetToSearchMultipleSprite; // Stores each sprite of the asset if it is a multiple sprite
	private AssetType assetType;
	private System.Type assetClass; // Class of the object (like GameObject, Material, custom MonoBehaviour etc.)

	private Phase currentPhase = Phase.Setup;

	private List<SceneObjectReferences> searchResult = new List<SceneObjectReferences>(); // Overall search results
	private SceneObjectReferences currentSceneReferences; // Results for the scene currently being searched

	private Dictionary<System.Type, FieldInfo[]> typeToVariables; // An optimization to fetch & filter fields of a class only once
	private Dictionary<System.Type, PropertyInfo[]> typeToProperties; // An optimization to fetch & filter properties of a class only once
    private Dictionary<AnimationClip, bool> searchedAnimationClips; // An optimization to search keyframes of animation clips only once

	private bool searchInOpenScenes = true; // Scenes currently open in Hierarchy view
	private bool searchInScenesInBuild = false; // Scenes in build (ticked in Build Settings)
	private bool searchInAllScenes = false; // All scenes (including scenes that are not in build)
	private bool searchInAssetsFolder = false; // Assets in Project view
	
	private bool stopAtFirstOccurrence = false; // Stop as soon as a reference is found
	private bool restoreInitialSceneSetup = true; // Close the additively loaded scenes that were not part of the initial scene setup

	private string errorMessage = "";

	private Color32 ORANGE_COLOR = new Color32( 235, 185, 140, 255 );
	public static GUIStyle boxGUIStyle; // GUIStyle used to draw the results of the search
	private Vector2 scrollPosition = Vector2.zero;

	// Initial scene setup (which scenes were open and/or loaded)
	private SceneSetup[] sceneInitialSetup; 

	// Fetch public, protected and private non-static variables (fields) from objects
	// Fetch only public non-static properties from objects
	private const BindingFlags FIELD_MODIFIERS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private const BindingFlags PROPERTY_MODIFIERS = BindingFlags.Instance | BindingFlags.Public;

	// Add menu named "Asset Usage Detector" to the Util menu
	[MenuItem( "Util/Asset Usage Detector" )]
	static void Init()
	{
		// Get existing open window or if none, make a new one
		AssetUsageDetector window = (AssetUsageDetector) EditorWindow.GetWindow( typeof( AssetUsageDetector ) );
		window.titleContent = new GUIContent( "Asset Usage" );
		
		boxGUIStyle = new GUIStyle( EditorStyles.helpBox );
		boxGUIStyle.alignment = TextAnchor.MiddleCenter;
		boxGUIStyle.font = EditorStyles.label.font;

		window.Show();
	}

	void OnGUI()
	{
		// Make the window scrollable
		scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition, GUILayout.ExpandWidth( true ), GUILayout.ExpandHeight( true ) );

		GUILayout.BeginVertical();

		GUILayout.Space( 10 );

		// Show the error message, if it is not empty
		if( errorMessage.Length > 0 )
		{
			Color c = GUI.color;
			GUI.color = ORANGE_COLOR;
			GUILayout.Box( errorMessage, GUILayout.ExpandWidth( true ) );
			GUI.color = c;
		}

		GUILayout.Space( 10 );

		if( currentPhase == Phase.Processing )
		{
			// Search is in progress
			GUILayout.Label( ". . . . . . ." );
		}
		else if( currentPhase == Phase.Setup )
		{
			assetToSearch = EditorGUILayout.ObjectField( "Asset: ", assetToSearch, typeof( Object ), true );

			GUILayout.Space( 10 );

			GUILayout.Box( "SCENES TO SEARCH", GUILayout.ExpandWidth( true ) );

			if( searchInAllScenes )
				GUI.enabled = false;

			searchInOpenScenes = EditorGUILayout.ToggleLeft( "Currently open (loaded) scene(s)", searchInOpenScenes );
			searchInScenesInBuild = EditorGUILayout.ToggleLeft( "Scenes in Build Settings (ticked)", searchInScenesInBuild );
			GUI.enabled = true;
			searchInAllScenes = EditorGUILayout.ToggleLeft( "All scenes in project (including scenes not in build)", searchInAllScenes );

			GUILayout.Space( 10 );

			GUILayout.Box( "OTHER FILTER(S)", GUILayout.ExpandWidth( true ) );

			searchInAssetsFolder = EditorGUILayout.ToggleLeft( "Also search in Project view (Assets folder)", searchInAssetsFolder );

			GUILayout.Space( 10 );

			GUILayout.Box( "SETTINGS", GUILayout.ExpandWidth( true ) );

			stopAtFirstOccurrence = EditorGUILayout.ToggleLeft( "Stop searching at first occurrence", stopAtFirstOccurrence );
			restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup after search is reset (Recommended)", restoreInitialSceneSetup );

			GUILayout.Space( 10 );

			// Don't let the user press the GO button without any search filters
			if( !searchInAllScenes && !searchInOpenScenes && !searchInScenesInBuild && !searchInAssetsFolder )
				GUI.enabled = false;

			if( GUILayout.Button( "GO!", GUILayout.Height( 30 ) ) )
			{
				if( assetToSearch == null )
				{
					errorMessage = "SELECT AN ASSET FIRST!";
				}
				else if( !AreScenesSaved() )
				{
					// Don't start the search if at least one scene is currently dirty (not saved)
					errorMessage = "SAVE OPEN SCENES FIRST!";
				}
				else
				{
					Debug.Log( "Searching..." );

					errorMessage = "";
					currentPhase = Phase.Processing;

					// Get the scenes that are open right now
					sceneInitialSetup = EditorSceneManager.GetSceneManagerSetup();

					// Start searching
					ExecuteQuery();
					
					return;
				}
			}
		}
		else if( currentPhase == Phase.Complete )
		{
			// Draw the results of the search
			GUI.enabled = false;

			assetToSearch = EditorGUILayout.ObjectField( "Asset: ", assetToSearch, typeof( Object ), true );

			GUILayout.Space( 10 );
			GUI.enabled = true;

			if( GUILayout.Button( "Reset Search", GUILayout.Height( 30 ) ) )
			{
				if( !restoreInitialSceneSetup || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
				{
					errorMessage = "";
					currentPhase = Phase.Setup;

					if( restoreInitialSceneSetup )
						RestoreInitialSceneSetup();
				}
			}
			
			Color c = GUI.color;
			GUI.color = Color.green;
			GUILayout.Box( "Don't forget to save scene(s) if you made any changes!", GUILayout.ExpandWidth( true ) );
			GUI.color = c;

			GUILayout.Space( 10 );

			if( searchResult.Count == 0 )
			{
				GUILayout.Box( "No results found...", GUILayout.ExpandWidth( true ) );
			}
			else
			{
                GUILayout.BeginHorizontal();

                // Select all the references without filtering them (i.e. without
                // getting gameObject's of components)
                if( GUILayout.Button( "Select All\n(Component-wise)", GUILayout.Height( 35 ) ) )
                {
                    // Find the number of references first
                    int referenceCount = 0;
                    for( int i = 0; i < searchResult.Count; i++ )
                    {
                        // Ping the first element of the references (either in Project view
                        // or Hierarchy view) to force both views to scroll to a proper position
                        // (setting Selection.objects does not scroll automatically)
                        EditorGUIUtility.PingObject( searchResult[i].references[0] );

                        referenceCount += searchResult[i].references.Count;
                    }

                    Object[] allReferences = new Object[referenceCount];
                    int currIndex = 0;
                    for( int i = 0; i < searchResult.Count; i++ )
                    {
                        for( int j = 0; j < searchResult[i].references.Count; j++ )
                        {
                            allReferences[currIndex] = searchResult[i].references[j];
                            currIndex++;
                        }
                    }
                    
                    Selection.objects = allReferences;
                }

                // Select all the references after filtering them (i.e. do not select components
                // but their gameObject's)
                if( GUILayout.Button( "Select All\n(GameObject-wise)", GUILayout.Height( 35 ) ) )
                {
                    HashSet<GameObject> uniqueGameObjects = new HashSet<GameObject>();
                    List<Object> resultList = new List<Object>();
                    for( int i = 0; i < searchResult.Count; i++ )
                    {
                        // Ping the first element of the references (either in Project view
                        // or Hierarchy view) to force both views to scroll to a proper position
                        // (setting Selection.objects does not scroll automatically)
                        EditorGUIUtility.PingObject( searchResult[i].references[0] );

                        for( int j = 0; j < searchResult[i].references.Count; j++ )
                        {
                            Component currReferenceAsComponent = searchResult[i].references[j] as Component;
                            if( currReferenceAsComponent != null )
                            {
                                if( !uniqueGameObjects.Contains( currReferenceAsComponent.gameObject ) )
                                {
                                    uniqueGameObjects.Add( currReferenceAsComponent.gameObject );
                                    resultList.Add( currReferenceAsComponent.gameObject );
                                }
                            }
                            else
                            {
                                resultList.Add( searchResult[i].references[j] );
                            }
                        }
                    }

                    Selection.activeObject = resultList[0];
                    Selection.objects = resultList.ToArray();
                }

                GUILayout.EndHorizontal();

                GUILayout.Space( 10 );

                for( int i = 0; i < searchResult.Count; i++ )
				{
					searchResult[i].DrawOnGUI();
				}
			}
		}

		GUILayout.Space( 10 );

		GUILayout.EndVertical();

		EditorGUILayout.EndScrollView();
	}

	// Search for references!
	private void ExecuteQuery()
	{
		searchResult = new List<SceneObjectReferences>();
		typeToVariables = new Dictionary<System.Type, FieldInfo[]>();
		typeToProperties = new Dictionary<System.Type, PropertyInfo[]>();
        searchedAnimationClips = new Dictionary<AnimationClip, bool>();

        assetType = AssetType.Other;

        // Special case: if asset is a Sprite, but its Texture2D file is given, grab the correct asset
        // (if it is a multiple sprite, store all sprites in a list)
        if( assetToSearch is Texture2D )
        {
            string assetPath = AssetDatabase.GetAssetPath( assetToSearch );
            TextureImporter importSettings = (TextureImporter) TextureImporter.GetAtPath( assetPath );
            if( importSettings.spriteImportMode == SpriteImportMode.Multiple )
            {
                // If it is a multiple sprite asset, store all sprites in a list
                assetToSearchMultipleSprite = new List<Sprite>();

                Object[] sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath( assetPath );
                for( int i = 0; i < sprites.Length; i++ )
                {
                    if( sprites[i] is Sprite )
                    {
                        assetToSearchMultipleSprite.Add( (Sprite) sprites[i] );
                    }
                }
                
                // If there is, in fact, multiple sprites extracted,
                // set the asset type accordingly
                // Otherwise (only 1 sprite is extracted), simply change the
                // searched asset to that sprite
                if( assetToSearchMultipleSprite.Count > 1 )
                {
                    assetType = AssetType.MultipleSprite;
                    assetClass = typeof( Sprite );
                }
                else if( assetToSearchMultipleSprite.Count == 1 )
                {
                    assetToSearch = assetToSearchMultipleSprite[0];
                }
            }
            else if( importSettings.spriteImportMode != SpriteImportMode.None )
            {
                // If it is a single sprite, try to extract
                // the sprite from the asset
                Sprite spriteRepresentation = AssetDatabase.LoadAssetAtPath<Sprite>( assetPath );
                if( spriteRepresentation != null )
                    assetToSearch = spriteRepresentation;
            }
        }

        if( assetType != AssetType.MultipleSprite )
        {
            if( assetToSearch is Texture )
                assetType = AssetType.Texture;
            else if( assetToSearch is Material )
                assetType = AssetType.Material;
            else if( assetToSearch is MonoScript )
                assetType = AssetType.Script;
            else if( assetToSearch is Shader )
                assetType = AssetType.Shader;
            else if( assetToSearch is AnimationClip )
                assetType = AssetType.Animation;
            else if( assetToSearch is GameObject )
                assetType = AssetType.GameObject;
            else
                assetType = AssetType.Other;

            assetClass = assetToSearch.GetType();
        }

        Debug.Log( "Asset type: " + assetType );

		// Find the scenes to search for references
		HashSet<string> scenesToSearch = new HashSet<string>();
		if( searchInAllScenes )
		{
			// Get all scenes from the Assets folder
			string[] scenesTemp = AssetDatabase.FindAssets( "t:SceneAsset" );
			for( int i = 0; i < scenesTemp.Length; i++ )
			{
				scenesToSearch.Add( AssetDatabase.GUIDToAssetPath( scenesTemp[i] ) );
			}
		}
		else
		{
			if( searchInOpenScenes )
			{
				// Get all open (and loaded) scenes
				for( int i = 0; i < EditorSceneManager.loadedSceneCount; i++ )
				{
					Scene scene = EditorSceneManager.GetSceneAt( i );
					if( scene.IsValid() )
						scenesToSearch.Add( scene.path );
				}
			}

			if( searchInScenesInBuild )
			{
				// Get all scenes in build settings (ticked)
				EditorBuildSettingsScene[] scenesTemp = EditorBuildSettings.scenes;
				for( int i = 0; i < scenesTemp.Length; i++ )
				{
					if( scenesTemp[i].enabled )
					{
						scenesToSearch.Add( scenesTemp[i].path );
					}
				}
			}
		}
		
		if( searchInAssetsFolder )
		{
			currentSceneReferences = new SceneObjectReferences();

			// Search through all prefabs and imported models
			string[] pathsToAssets = AssetDatabase.FindAssets( "t:GameObject" );
			for( int i = 0; i < pathsToAssets.Length; i++ )
			{
				CheckGameObjectForAssetRecursive( AssetDatabase.LoadAssetAtPath<GameObject>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) ) );

				if( stopAtFirstOccurrence && currentSceneReferences.references.Count > 0 )
					break;
			}

			if( !stopAtFirstOccurrence || currentSceneReferences.references.Count == 0 )
			{
				// If asset is shader or texture, search through all materials in the project
				if( assetType == AssetType.Shader || assetType == AssetType.Texture )
				{
					pathsToAssets = AssetDatabase.FindAssets( "t:Material" );
					for( int i = 0; i < pathsToAssets.Length; i++ )
					{
						Material mat = AssetDatabase.LoadAssetAtPath<Material>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) );
						if( CheckMaterialForAsset( mat ) )
						{
							currentSceneReferences.references.Add( mat );

							if( stopAtFirstOccurrence )
								break;
						}
					}
				}
			}

            // Search through all AnimatorController's and AnimationClip's in the project
            if( !stopAtFirstOccurrence || currentSceneReferences.references.Count == 0 )
			{
                if( assetType != AssetType.Animation )
                {
                    // Search through animation clip keyframes for references to searched asset
                    pathsToAssets = AssetDatabase.FindAssets( "t:AnimationClip" );
                    for( int i = 0; i < pathsToAssets.Length; i++ )
                    {
                        AnimationClip animClip = AssetDatabase.LoadAssetAtPath<AnimationClip>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) );
                        if( CheckAnimationForAsset( animClip ) )
                        {
                            currentSceneReferences.references.Add( animClip );

                            if( stopAtFirstOccurrence )
                                break;
                        }
                    }
                }

                if( !stopAtFirstOccurrence || currentSceneReferences.references.Count == 0 )
                {
                    // Search through all animator controllers
                    pathsToAssets = AssetDatabase.FindAssets( "t:AnimatorController" );
                    for( int i = 0; i < pathsToAssets.Length; i++ )
                    {
                        AnimatorController animController = AssetDatabase.LoadAssetAtPath<AnimatorController>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) );
                        AnimationClip[] animClips = animController.animationClips;
                        bool foundAsset = false;
                        for( int j = 0; j < animClips.Length; j++ )
                        {
                            if( CheckAnimationForAsset( animClips[j] ) )
                            {
                                foundAsset = true;
                                break;
                            }
                        }

                        if( foundAsset )
                        {
                            currentSceneReferences.references.Add( animController );

                            if( stopAtFirstOccurrence )
                                break;
                        }
                    }
                }
            }

			// If a reference is found in the Project view, save the result(s)
			if( currentSceneReferences.references.Count > 0 )
			{
				searchResult.Add( currentSceneReferences );
			}
		}

		foreach( string scenePath in scenesToSearch )
		{
			if( scenePath != null )
			{
				// Open the scene additively (to access its objects)
				Scene scene = EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Additive );
				currentSceneReferences = new SceneObjectReferences( scenePath );

				// Search through all GameObjects in the scene
				GameObject[] rootGameObjects = scene.GetRootGameObjects();
				for( int i = 0; i < rootGameObjects.Length; i++ )
				{
					CheckGameObjectForAssetRecursive( rootGameObjects[i] );

					if( stopAtFirstOccurrence && currentSceneReferences.references.Count > 0 )
						break;
				}

				// If no reference is found in this scene
				// and the scene is not part of the initial scene setup,
				// close it
				if( currentSceneReferences.references.Count == 0 )
				{
					bool sceneIsOneOfInitials = false;
					for( int i = 0; i < sceneInitialSetup.Length; i++ )
					{
						if( sceneInitialSetup[i].path == scenePath )
						{
							if( !sceneInitialSetup[i].isLoaded )
								EditorSceneManager.CloseScene( scene, false );

							sceneIsOneOfInitials = true;
							break;
						}
					}

					if( !sceneIsOneOfInitials )
						EditorSceneManager.CloseScene( scene, true );
				}
				else
				{
					// Some references are found in this scene,
					// save the results
					searchResult.Add( currentSceneReferences );

					if( stopAtFirstOccurrence )
						break;
				}
			}
		}

		// Search is complete!
		currentPhase = Phase.Complete;
	}

	// Search through components of this GameObject in detail
	// (including fields, properties, arrays, ArrayList's and generic List's in scripts)
	// and then search through its children recursively
	private void CheckGameObjectForAssetRecursive( GameObject go )
	{
        if( stopAtFirstOccurrence && currentSceneReferences.references.Count > 0 )
            return;

        // Check if this GameObject's prefab is the selected asset
        if( assetType == AssetType.GameObject && PrefabUtility.GetPrefabParent( go ) == assetToSearch )
		{
			currentSceneReferences.references.Add( go );

			if( stopAtFirstOccurrence )
				return;
		}
		else
		{
			Component[] components = go.GetComponents<Component>();

			// Search through all the components of the object
			for( int i = 0; i < components.Length; i++ )
			{
				Component component = components[i];

				// Ignore Transform component (no object field to search for)
				if( component is Transform )
					continue;

				if( assetType == AssetType.Script && component is MonoBehaviour )
				{
					// If selected asset is a script, check if this component is an instance of it

					if( MonoScript.FromMonoBehaviour( (MonoBehaviour) component ) == assetToSearch )
					{
						currentSceneReferences.references.Add( component );

						if( stopAtFirstOccurrence )
							return;
						else
							continue;
					}
				}
				else if( ( assetType == AssetType.Shader || assetType == AssetType.Texture || assetType == AssetType.Material ) && component is Renderer )
				{
					// If selected asset is a shader, texture or material, and this component is a Renderer,
					// perform a special search (CheckMaterialForAsset) for references

					Material[] materials = ( (Renderer) component ).sharedMaterials;

					bool foundAsset = false;
					for( int j = 0; j < materials.Length; j++ )
					{
						if( CheckMaterialForAsset( materials[j] ) )
						{
							foundAsset = true;
							break;
						}
					}

					if( foundAsset )
					{
						currentSceneReferences.references.Add( component );

						if( stopAtFirstOccurrence )
							return;
						else
							continue;
					}
				}
				else if( component is Animation || component is Animator )
				{
                    // If this component is an Animation or Animator, perform a special search for references
                    // in its animation clips (and keyframes in these animations)

                    bool foundAsset = false;
					if( component is Animation )
					{
						foreach( AnimationState anim in (Animation) component )
						{
							if( CheckAnimationForAsset( anim.clip ) )
							{
								foundAsset = true;
								break;
							}
						}
					}
					else if( component is Animator )
					{
						RuntimeAnimatorController animController = ( (Animator) component ).runtimeAnimatorController;
						if( animController != null )
						{
							AnimationClip[] animClips = animController.animationClips;
							for( int j = 0; j < animClips.Length; j++ )
							{
								if( CheckAnimationForAsset( animClips[j] ) )
								{
									foundAsset = true;
									break;
								}
							}
						}
					}

					if( foundAsset )
					{
						currentSceneReferences.references.Add( component );

						if( stopAtFirstOccurrence )
							return;
						else
							continue;
					}
				}

				// None of the above methods yielded a result for this component,
				// Perform a search through fields, properties, arrays, ArrayList's and
				// generic List's of this component

				FieldInfo[] variables;
				if( !typeToVariables.TryGetValue( component.GetType(), out variables ) )
				{
					// If it is the first time this type of object is seen,
					// filter and cache its fields
					System.Type componentType = component.GetType();
					variables = componentType.GetFields( FIELD_MODIFIERS );
					
					// Filter the fields
					if( variables.Length > 0 )
					{
						int invalidVariables = 0;
						List<FieldInfo> validVariables = new List<FieldInfo>();
						for( int j = 0; j < variables.Length; j++ )
						{
							// Field filtering process:
							// 1- allow field types that are subclass of UnityEngine.Object and superclass of searched asset's type
							// 2- allow fields that extend UnityEngine.Component (but only if asset type is GameObject) to find out whether the component
							// stored in that field is a component of the searched asset
							// 3- allow ArrayList collections
							// 4- allow arrays whose elements' type is superclass of the searched asset's type
							// 5- allow arrays whose elements extend UnityEngine.Component and the asset type is GameObject (similar to 2)
							// 6- allow generic List collections whose generic type is superclass of the searched asset's type
							// 6- allow generic List collections whose generic type extends UnityEngine.Component
							// and the asset type is GameObject (similar to 2)
							System.Type variableType = variables[j].FieldType;
							if( ( IsTypeDerivedFrom( variableType, typeof( Object ) ) && IsTypeDerivedFrom( assetClass, variableType ) ) ||
								( IsTypeDerivedFrom( variableType, typeof( Component ) ) && assetType == AssetType.GameObject ) ||
								IsTypeDerivedFrom( variableType, typeof( ArrayList ) ) ||
								( variableType.IsArray && ( IsTypeDerivedFrom( assetClass, variableType.GetElementType() ) ||
									( IsTypeDerivedFrom( variableType.GetElementType(), typeof( Component ) ) && assetType == AssetType.GameObject ) ) ) ||
								( variableType.IsGenericType && ( IsTypeDerivedFrom( assetClass, variableType.GetGenericArguments()[0] ) ||
									( IsTypeDerivedFrom( variableType.GetGenericArguments()[0], typeof( Component ) ) && assetType == AssetType.GameObject ) ) ) )
							{
								validVariables.Add( variables[j] );
							}
							else
							{
								invalidVariables++;
							}
						}

						if( invalidVariables > 0 )
						{
							variables = validVariables.ToArray();
						}
					}

					// Cache the filtered fields
					typeToVariables.Add( componentType, variables );
				}
					
				// Search through all the filtered fields
				for( int j = 0; j < variables.Length; j++ )
				{
					object variableValue = variables[j].GetValue( component );
					if( CheckVariableValueForAsset( variableValue ) )
					{
						currentSceneReferences.references.Add( component );

						if( stopAtFirstOccurrence )
							return;
						else
							break;
					}
					else if( variableValue is IEnumerable )
					{
						// If the field is IEnumerable (possibly an array or collection),
						// search through members of it (not recursive)
						bool assetFoundInArray = false;
						try
						{
							foreach( object arrayItem in (IEnumerable) variableValue )
							{
								if( CheckVariableValueForAsset( arrayItem ) )
								{
									currentSceneReferences.references.Add( component );

									if( stopAtFirstOccurrence )
										return;
									else
									{
										assetFoundInArray = true;
										break;
									}
								}
							}
						}
						catch( UnassignedReferenceException )
						{ }
						catch( MissingReferenceException )
						{ }

						if( assetFoundInArray )
						{
							break;
						}
					}
				}
				
				PropertyInfo[] properties;
				if( !typeToProperties.TryGetValue( component.GetType(), out properties ) )
				{
					// If it is the first time this type of object is seen,
					// filter and cache its properties
					System.Type componentType = component.GetType();
					properties = componentType.GetProperties( PROPERTY_MODIFIERS );
					
					// Filter the properties
					if( properties.Length > 0 )
					{
						int invalidProperties = 0;
						List<PropertyInfo> validProperties = new List<PropertyInfo>();
						for( int j = 0; j < properties.Length; j++ )
						{
							// Property filtering process:
							// 1- allow property types that are subclass of UnityEngine.Object and superclass of searched asset's type
							// 2- allow properties that extend UnityEngine.Component (but only if asset type is GameObject) to find out whether the component
							// stored in that property is a component of the searched asset
							// 3- allow ArrayList collections
							// 4- allow arrays whose elements' type is superclass of the searched asset's type
							// 5- allow arrays whose elements extend UnityEngine.Component and the asset type is GameObject (similar to 2)
							// 6- allow generic List collections whose generic type is superclass of the searched asset's type
							// 6- allow generic List collections whose generic type extends UnityEngine.Component
							// and the asset type is GameObject (similar to 2)
							System.Type propertyType = properties[j].PropertyType;
							if( ( IsTypeDerivedFrom( propertyType, typeof( Object ) ) && IsTypeDerivedFrom( assetClass, propertyType ) ) ||
								( IsTypeDerivedFrom( propertyType, typeof( Component ) ) && assetType == AssetType.GameObject ) ||
								IsTypeDerivedFrom( propertyType, typeof( ArrayList ) ) ||
								( propertyType.IsArray && ( IsTypeDerivedFrom( assetClass, propertyType.GetElementType() ) || 
									( IsTypeDerivedFrom( propertyType.GetElementType(), typeof( Component ) ) && assetType == AssetType.GameObject ) ) ) ||
								( propertyType.IsGenericType && ( IsTypeDerivedFrom( assetClass, propertyType.GetGenericArguments()[0] ) ||
									( IsTypeDerivedFrom( propertyType.GetGenericArguments()[0], typeof( Component ) ) && assetType == AssetType.GameObject ) ) ) )
							{
								// Additional filtering for properties:
								// 1- Ignore "gameObject", "transform", "rectTransform" and "attachedRigidbody" properties to reduce repetition
								// and get more relevant results
								// 2- Ignore "canvasRenderer" and "canvas" properties of Graphic components
								// 3 & 4- Prevent accessing properties of Unity that instantiate an existing resource (causing leak)
								if( properties[j].Name.Equals( "gameObject" ) || properties[j].Name.Equals( "transform" ) || 
									properties[j].Name.Equals( "attachedRigidbody" ) || properties[j].Name.Equals( "rectTransform" ) )
									invalidProperties++;
								else if( ( properties[j].Name.Equals( "canvasRenderer" ) || properties[j].Name.Equals( "canvas" ) ) &&
										   IsTypeDerivedFrom( componentType, typeof( UnityEngine.UI.Graphic ) ) )
									invalidProperties++;
								else if( properties[j].Name.Equals( "mesh" ) && IsTypeDerivedFrom( componentType, typeof( MeshFilter ) ) )
									invalidProperties++;
								else if( ( properties[j].Name.Equals( "material" ) || properties[j].Name.Equals( "materials" ) ) &&
									( IsTypeDerivedFrom( componentType, typeof( Renderer ) ) || IsTypeDerivedFrom( componentType, typeof( Collider ) ) ||
									IsTypeDerivedFrom( componentType, typeof( Collider2D ) ) || IsTypeDerivedFrom( componentType, typeof( GUIText ) ) ) )
									invalidProperties++;
								else
									validProperties.Add( properties[j] );
							}
							else
							{
								invalidProperties++;
							}
						}

						if( invalidProperties > 0 )
						{
							properties = validProperties.ToArray();
						}
					}

					// Cache the filtered properties
					typeToProperties.Add( componentType, properties );
				}

				// Search through all the filtered properties
				for( int j = 0; j < properties.Length; j++ )
				{
					try
					{
						object propertyValue = properties[j].GetValue( component, null );

						if( CheckVariableValueForAsset( propertyValue ) )
						{
							currentSceneReferences.references.Add( component );
							
							if( stopAtFirstOccurrence )
								return;
							else
								break;
						}
						else if( propertyValue is IEnumerable )
						{
							// If the property is IEnumerable (possibly an array or collection),
							// search through members of it (not recursive)
							bool assetFoundInArray = false;
							try
							{
								foreach( object arrayItem in (IEnumerable) propertyValue )
								{
									if( CheckVariableValueForAsset( arrayItem ) )
									{
										currentSceneReferences.references.Add( component );
										
										if( stopAtFirstOccurrence )
											return;
										else
										{
											assetFoundInArray = true;
											break;
										}
									}
								}
							}
							catch( UnassignedReferenceException )
							{ }
							catch( MissingReferenceException )
							{ }

							if( assetFoundInArray )
							{
								break;
							}
						}
					}
					catch( System.Exception )
					{ }
				}
			}
		}

		// Search the children GameObject's recursively
		Transform tr = go.transform;
		for( int i = 0; i < tr.childCount; i++ )
		{
			CheckGameObjectForAssetRecursive( tr.GetChild( i ).gameObject );
		}
	}

	// Chech if asset is used in this material
	private bool CheckMaterialForAsset( Material material )
	{
		if( material == null )
			return false;

		if( material == assetToSearch )
			return true;

		if( assetType == AssetType.Shader )
		{
			if( material.shader == assetToSearch )
				return true;
		}
		else if( assetType == AssetType.Texture )
		{
			// Search through all the textures attached to this material
			// Credit to: jobesu on unity answers
			Shader shader = material.shader;
			int shaderPropertyCount = ShaderUtil.GetPropertyCount( shader );
			for( int k = 0; k < shaderPropertyCount; k++ )
			{
				if( ShaderUtil.GetPropertyType( shader, k ) == ShaderUtil.ShaderPropertyType.TexEnv )
				{
					if( material.GetTexture( ShaderUtil.GetPropertyName( shader, k ) ) == assetToSearch )
					{
						return true;
					}
				}
			}
		}

		return false;
	}

    // Check if asset is used in this animation clip (and its keyframes)
    private bool CheckAnimationForAsset( AnimationClip clip )
    {
        // If this AnimationClip is already searched, return the cached result
        bool result;
        if( !searchedAnimationClips.TryGetValue( clip, out result ) )
        {
            if( clip == assetToSearch )
            {
                searchedAnimationClips.Add( clip, true );
                return true;
            }

            // Don't search for animation clip references inside an animation clip's keyframes!
            if( assetType == AssetType.Animation )
            {
                searchedAnimationClips.Add( clip, false );
                return false;
            }

            // Get all curves from animation clip
            EditorCurveBinding[] objectCurves = AnimationUtility.GetObjectReferenceCurveBindings( clip );
            for( int i = 0; i < objectCurves.Length; i++ )
            {
                // Search through all the keyframes in this curve
                ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve( clip, objectCurves[i] );
                Object objectAtKeyframe;
                for( int j = 0; j < keyframes.Length; j++ )
                {
                    objectAtKeyframe = keyframes[j].value;
                    if( assetType == AssetType.MultipleSprite )
                    {
                        for( int k = 0; k < assetToSearchMultipleSprite.Count; k++ )
                        {
                            if( objectAtKeyframe == assetToSearchMultipleSprite[k] )
                            {
                                searchedAnimationClips.Add( clip, true );
                                return true;
                            }
                        }
                    }
                    else
                    {
                        if( objectAtKeyframe == assetToSearch )
                        {
                            searchedAnimationClips.Add( clip, true );
                            return true;
                        }
                        else if( objectAtKeyframe is Component && assetType == AssetType.GameObject )
                        {
                            // If keyframe value is a component, and selected asset is a GameObject,
                            // check if it is a component of the selected asset
                            if( ( (Component) objectAtKeyframe ).gameObject == assetToSearch )
                            {
                                searchedAnimationClips.Add( clip, true );
                                return true;
                            }
                        }
                    }
                }
            }

            searchedAnimationClips.Add( clip, false );
            return false;
        }

        return result;
    }

	// Check if this variable is a refence to the asset
	private bool CheckVariableValueForAsset( object variableValue )
	{
		if( variableValue == null )
			return false;

        if( assetType == AssetType.MultipleSprite )
        {
            for( int i = 0; i < assetToSearchMultipleSprite.Count; i++ )
            {
                if( variableValue == assetToSearchMultipleSprite[i] )
                    return true;
            }
        }
        else
        {
            if( variableValue == assetToSearch )
            {
                return true;
            }
            else if( variableValue is Component && assetType == AssetType.GameObject )
            {
                // If variable is a component, and selected asset is a GameObject,
                // check if it is a component of the selected asset
                try
                {
                    if( ( (Component) variableValue ).gameObject == assetToSearch )
                    {
                        return true;
                    }
                }
                catch( UnassignedReferenceException )
                { }
                catch( MissingReferenceException )
                { }
            }
        }

		return false;
	}

	// Check if all open scenes are saved (not dirty)
	private bool AreScenesSaved()
	{
		for( int i = 0; i < EditorSceneManager.loadedSceneCount; i++ )
		{
			Scene scene = EditorSceneManager.GetSceneAt( i );
			if( scene.isDirty || scene.path == null || scene.path.Length == 0 )
				return false;
		}

		return true;
	}

	// Check if "child" is a subclass of "parent" (or if their types match)
	private bool IsTypeDerivedFrom( System.Type child, System.Type parent )
	{
		if( child.IsSubclassOf( parent ) || child == parent )
			return true;

		return false;
	}

	// Close the scenes that were not part of the initial scene setup
	private void RestoreInitialSceneSetup()
	{
		if( sceneInitialSetup == null )
			return;

		SceneSetup[] sceneFinalSetup = EditorSceneManager.GetSceneManagerSetup();
		for( int i = 0; i < sceneFinalSetup.Length; i++ )
		{
			bool sceneIsOneOfInitials = false;
			for( int j = 0; j < sceneInitialSetup.Length; j++ )
			{
				if( sceneFinalSetup[i].path == sceneInitialSetup[j].path )
				{
					sceneIsOneOfInitials = true;
					break;
				}
			}

			if( !sceneIsOneOfInitials )
			{
				EditorSceneManager.CloseScene( EditorSceneManager.GetSceneByPath( sceneFinalSetup[i].path ), true );
			}
		}

		for( int i = 0; i < sceneInitialSetup.Length; i++ )
		{
			if( !sceneInitialSetup[i].isLoaded )
				EditorSceneManager.CloseScene( EditorSceneManager.GetSceneByPath( sceneInitialSetup[i].path ), false );
		}
	}

	// Restore the initial scene setup when the window is closed
	void OnDestroy()
	{
		if( restoreInitialSceneSetup )
			RestoreInitialSceneSetup();
	}
}
