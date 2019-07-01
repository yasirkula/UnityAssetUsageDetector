// Asset Usage Detector - by Suleyman Yasir KULA (yasirkula@gmail.com)

using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using Object = UnityEngine.Object;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace AssetUsageDetectorNamespace
{
	public enum Phase { Setup, Processing, Complete };
	public enum PathDrawingMode { Full, ShortRelevantParts, Shortest };

	public class AssetUsageDetector : EditorWindow
	{
		#region Helper Classes		
		[Serializable]
		private class SubAssetToSearch
		{
			public Object subAsset;
			public bool shouldSearch;

			public SubAssetToSearch( Object subAsset, bool shouldSearch )
			{
				this.subAsset = subAsset;
				this.shouldSearch = shouldSearch;
			}
		}

		[Serializable]
		private class CacheEntry
		{
			public string[] dependencies;
			public string hash;
			public bool hasSelfDependency;

			[NonSerialized]
			public bool verified;

			public CacheEntry( string path )
			{
				Verify( path );
			}

			public void Verify( string path )
			{
				string hash = AssetDatabase.GetAssetDependencyHash( path ).ToString();
				if( this.hash != hash )
				{
					this.hash = hash;

					dependencies = AssetDatabase.GetDependencies( path, false );
					hasSelfDependency = false;

					for( int i = 0; i < dependencies.Length; i++ )
					{
						if( dependencies[i] == path )
						{
							int newSize = dependencies.Length - 1;
							if( i < newSize )
								dependencies[i] = dependencies[newSize];

							Array.Resize( ref dependencies, newSize );
							hasSelfDependency = true;

							break;
						}
					}
				}

				verified = true;
			}
		}
		#endregion

		private const float PLAY_MODE_REFRESH_INTERVAL = 1f; // Interval to refresh the editor window in play mode
		private const string DEPENDENCY_CACHE_PATH = "Library/AssetUsageDetector.cache"; // Path of the cache file

		private List<Object> objectsToSearch = new List<Object>() { null };
		private List<SubAssetToSearch> subAssetsToSearch = new List<SubAssetToSearch>();

		private HashSet<Object> objectsToSearchSet; // A set that contains the searched asset(s) and their sub-assets (if any)

		private HashSet<Object> sceneObjectsToSearchSet; // Scene object(s) in objectsToSearchSet
		private HashSet<string> sceneObjectsToSearchScenesSet; // sceneObjectsToSearchSet's scene(s)

		private HashSet<Object> assetsToSearchSet; // Project asset(s) in objectsToSearchSet
		private HashSet<string> assetsToSearchPathsSet; // objectsToSearchSet's path(s)

		private Phase currentPhase = Phase.Setup;
		private PathDrawingMode pathDrawingMode = PathDrawingMode.ShortRelevantParts;

		private List<ReferenceHolder> searchResult = new List<ReferenceHolder>(); // Overall search results
		private ReferenceHolder currentReferenceHolder; // Results for the currently searched scene

		private Dictionary<Type, VariableGetterHolder[]> typeToVariables; // An optimization to fetch & filter fields and properties of a class only once
		private Dictionary<string, ReferenceNode> searchedObjects; // An optimization to search an object only once (key is a hash of the searched object)
		private Dictionary<string, CacheEntry> assetDependencyCache; // An optimization to fetch the dependencies of an asset only once (key is the path of the asset)

		private Stack<object> callStack; // Stack of SearchObject function parameters to avoid infinite loops (which happens when same object is passed as parameter to function)

		private bool searchMaterialAssets;
		private bool searchPrefabConnections;
		private bool searchMonoBehavioursForScript;
		private bool searchRenderers;
		private bool searchMaterialsForShader;
		private bool searchMaterialsForTexture;

		private bool searchSerializableVariablesOnly;

		private bool searchInOpenScenes = true; // Scenes currently open in Hierarchy view
		private bool searchInScenesInBuild = true; // Scenes in build
		private bool searchInScenesInBuildTickedOnly = true; // Scenes in build (ticked only or not)
		private bool searchInAllScenes = true; // All scenes (including scenes that are not in build)
		private bool searchInAssetsFolder = true; // Assets in Project view

		private bool showSubAssetsFoldout = true; // Whether or not sub-assets included in search should be shown

		private int searchDepthLimit = 4; // Depth limit for recursively searching variables of objects
		private int currentDepth = 0;

		private bool restoreInitialSceneSetup = true; // Close the additively loaded scenes that were not part of the initial scene setup
		private SceneSetup[] initialSceneSetup; // Initial scene setup (which scenes were open and/or loaded)

		private string errorMessage = string.Empty;

		// Fetch public, protected and private non-static fields and properties from objects by default
		private BindingFlags fieldModifiers = BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic;
		private BindingFlags propertyModifiers = BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic;

		private int prevSearchDepthLimit;
		private BindingFlags prevFieldModifiers;
		private BindingFlags prevPropertyModifiers;

		public static string tooltip = null;
		public static bool showTooltips = false;

		private double nextPlayModeRefreshTime = 0f;

		private Vector2 scrollPosition = Vector2.zero;

		private int searchCount; // Number of searched objects
		private double searchStartTime;

		private List<ReferenceNode> nodesPool = new List<ReferenceNode>( 32 );
		private List<VariableGetterHolder> validVariables = new List<VariableGetterHolder>( 32 );

		// Add "Asset Usage Detector" menu item to the Window menu
		[MenuItem( "Window/Asset Usage Detector" )]
		private static void Init()
		{
			AssetUsageDetector window = GetWindow<AssetUsageDetector>();
			window.titleContent = new GUIContent( "Asset Usage Detector" );

			window.Show();
		}

		private void OnDisable()
		{
			SaveCache();
		}

		private void OnDestroy()
		{
			if( currentPhase == Phase.Complete && !EditorApplication.isPlaying && initialSceneSetup != null )
			{
				if( EditorUtility.DisplayDialog( "Scenes", "Restore initial scene setup?", "Yes", "Leave it as is" ) )
					RestoreInitialSceneSetup();
			}
		}

		private void Update()
		{
			// Refresh the window at a regular interval in play mode to update the tooltip
			if( EditorApplication.isPlaying && currentPhase == Phase.Complete && showTooltips && EditorApplication.timeSinceStartup >= nextPlayModeRefreshTime )
			{
				nextPlayModeRefreshTime = EditorApplication.timeSinceStartup + PLAY_MODE_REFRESH_INTERVAL; ;
				Repaint();
			}
		}

		private void OnGUI()
		{
			// Make the window scrollable
			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition, Utilities.GL_EXPAND_WIDTH, Utilities.GL_EXPAND_HEIGHT );

			GUILayout.BeginVertical();

			GUILayout.Space( 10 );

			// Show the error message, if it is not empty
			if( errorMessage.Length > 0 )
				EditorGUILayout.HelpBox( errorMessage, MessageType.Error );

			GUILayout.Space( 10 );

			if( currentPhase == Phase.Processing )
			{
				// If we are stuck at this phase, then we have encountered an exception
				GUILayout.Label( ". . . Something went wrong, check console . . ." );

				restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup (Recommended)", restoreInitialSceneSetup );

				if( GUILayout.Button( "RETURN", Utilities.GL_HEIGHT_30 ) )
				{
					if( !restoreInitialSceneSetup || RestoreInitialSceneSetup() )
					{
						errorMessage = string.Empty;
						currentPhase = Phase.Setup;
					}
				}
			}
			else if( currentPhase == Phase.Setup )
			{
				if( ExposeSearchedAssets() )
					OnSearchedAssetsChanged();

				if( subAssetsToSearch.Count > 0 )
				{
					GUILayout.BeginHorizontal();

					// 0-> all toggles off, 1-> mixed, 2-> all toggles on
					bool toggleAllSubAssets = subAssetsToSearch[0].shouldSearch;
					bool mixedToggle = false;
					for( int i = 1; i < subAssetsToSearch.Count; i++ )
					{
						if( subAssetsToSearch[i].shouldSearch != toggleAllSubAssets )
						{
							mixedToggle = true;
							break;
						}
					}

					if( mixedToggle )
						EditorGUI.showMixedValue = true;

					GUI.changed = false;
					toggleAllSubAssets = EditorGUILayout.Toggle( toggleAllSubAssets, Utilities.GL_WIDTH_25 );
					if( GUI.changed )
					{
						for( int i = 0; i < subAssetsToSearch.Count; i++ )
							subAssetsToSearch[i].shouldSearch = toggleAllSubAssets;
					}

					EditorGUI.showMixedValue = false;

					showSubAssetsFoldout = EditorGUILayout.Foldout( showSubAssetsFoldout, "Include sub-assets in search:", true );

					GUILayout.EndHorizontal();

					if( showSubAssetsFoldout )
					{
						for( int i = 0; i < subAssetsToSearch.Count; i++ )
						{
							GUILayout.BeginHorizontal();

							subAssetsToSearch[i].shouldSearch = EditorGUILayout.Toggle( subAssetsToSearch[i].shouldSearch, Utilities.GL_WIDTH_25 );

							GUI.enabled = false;
							EditorGUILayout.ObjectField( string.Empty, subAssetsToSearch[i].subAsset, typeof( Object ), true );
							GUI.enabled = true;

							GUILayout.EndHorizontal();
						}
					}
				}

				GUILayout.Space( 10 );

				GUILayout.Box( "SEARCH IN", Utilities.GL_EXPAND_WIDTH );

				searchInAssetsFolder = EditorGUILayout.ToggleLeft( "Project view (Assets folder)", searchInAssetsFolder );

				GUILayout.Space( 10 );

				if( EditorApplication.isPlaying )
				{
					searchInAllScenes = false;
					searchInScenesInBuild = false;
				}
				else if( searchInAllScenes )
					GUI.enabled = false;

				searchInOpenScenes = EditorGUILayout.ToggleLeft( "Currently open (loaded) scene(s)", searchInOpenScenes );

				if( EditorApplication.isPlaying )
					GUI.enabled = false;

				searchInScenesInBuild = EditorGUILayout.ToggleLeft( "Scenes in Build Settings", searchInScenesInBuild );

				if( searchInScenesInBuild )
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space( 35 );

					searchInScenesInBuildTickedOnly = EditorGUILayout.ToggleLeft( "Ticked only", searchInScenesInBuildTickedOnly, Utilities.GL_WIDTH_100 );
					searchInScenesInBuildTickedOnly = !EditorGUILayout.ToggleLeft( "All", !searchInScenesInBuildTickedOnly, Utilities.GL_WIDTH_100 );

					GUILayout.EndHorizontal();
				}

				if( !EditorApplication.isPlaying )
					GUI.enabled = true;

				searchInAllScenes = EditorGUILayout.ToggleLeft( "All scenes in the project", searchInAllScenes );

				GUI.enabled = true;

				GUILayout.Space( 10 );

				GUILayout.Box( "SEARCH SETTINGS", Utilities.GL_EXPAND_WIDTH );

				GUILayout.BeginHorizontal();

				GUILayout.Label( new GUIContent( "> Search depth: " + searchDepthLimit, "Depth limit for recursively searching variables of objects" ), Utilities.GL_WIDTH_250 );

				searchDepthLimit = (int) GUILayout.HorizontalSlider( searchDepthLimit, 0, 4 );

				GUILayout.EndHorizontal();

				GUILayout.Label( "> Search variables:" );

				GUILayout.BeginHorizontal();

				GUILayout.Space( 35 );

				if( EditorGUILayout.ToggleLeft( "Public", ( fieldModifiers & BindingFlags.Public ) == BindingFlags.Public, Utilities.GL_WIDTH_100 ) )
					fieldModifiers |= BindingFlags.Public;
				else
					fieldModifiers &= ~BindingFlags.Public;

				if( EditorGUILayout.ToggleLeft( "Non-public", ( fieldModifiers & BindingFlags.NonPublic ) == BindingFlags.NonPublic, Utilities.GL_WIDTH_100 ) )
					fieldModifiers |= BindingFlags.NonPublic;
				else
					fieldModifiers &= ~BindingFlags.NonPublic;

				GUILayout.EndHorizontal();

				GUILayout.Label( "> Search properties (can be slow):" );

				GUILayout.BeginHorizontal();

				GUILayout.Space( 35 );

				if( EditorGUILayout.ToggleLeft( "Public", ( propertyModifiers & BindingFlags.Public ) == BindingFlags.Public, Utilities.GL_WIDTH_100 ) )
					propertyModifiers |= BindingFlags.Public;
				else
					propertyModifiers &= ~BindingFlags.Public;

				if( EditorGUILayout.ToggleLeft( "Non-public", ( propertyModifiers & BindingFlags.NonPublic ) == BindingFlags.NonPublic, Utilities.GL_WIDTH_100 ) )
					propertyModifiers |= BindingFlags.NonPublic;
				else
					propertyModifiers &= ~BindingFlags.NonPublic;

				GUILayout.EndHorizontal();

				GUILayout.Space( 10 );

				// Don't let the user press the GO button without any valid search location
				if( !searchInAllScenes && !searchInOpenScenes && !searchInScenesInBuild && !searchInAssetsFolder )
					GUI.enabled = false;

				if( GUILayout.Button( "GO!", Utilities.GL_HEIGHT_30 ) )
				{
					if( AreSearchedAssetsEmpty() )
					{
						errorMessage = "ADD AN ASSET TO THE LIST FIRST!";
					}
					else if( !EditorApplication.isPlaying && !AreScenesSaved() )
					{
						// Don't start the search if at least one scene is currently dirty (not saved)
						errorMessage = "SAVE OPEN SCENES FIRST!";
					}
					else
					{
						errorMessage = string.Empty;
						currentPhase = Phase.Processing;

						if( !EditorApplication.isPlaying )
							initialSceneSetup = EditorSceneManager.GetSceneManagerSetup(); // Get the scenes that are open right now
						else
							initialSceneSetup = null;

						// Start searching
						ExecuteQuery();
					}
				}
			}
			else if( currentPhase == Phase.Complete )
			{
				// Draw the results of the search
				GUI.enabled = false;

				ExposeSearchedAssets();

				GUILayout.Space( 10 );
				GUI.enabled = true;

				restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup after search is reset (Recommended)", restoreInitialSceneSetup );

				if( GUILayout.Button( "Reset Search", Utilities.GL_HEIGHT_30 ) )
				{
					if( !restoreInitialSceneSetup || RestoreInitialSceneSetup() )
					{
						errorMessage = string.Empty;
						currentPhase = Phase.Setup;

						if( searchResult != null )
							searchResult.Clear();
					}
				}

				Color c = GUI.color;
				GUI.color = Color.green;
				GUILayout.Box( "Don't forget to save scene(s) if you made any changes!", Utilities.GL_EXPAND_WIDTH );
				GUI.color = c;

				GUILayout.Space( 10 );

				if( searchResult.Count == 0 )
				{
					GUILayout.Box( "No results found...", Utilities.GL_EXPAND_WIDTH );
				}
				else
				{
					GUILayout.BeginHorizontal();

					// Select all the references after filtering them (select only the GameObject's)
					if( GUILayout.Button( "Select All\n(GameObject-wise)", Utilities.GL_HEIGHT_35 ) )
					{
						HashSet<GameObject> uniqueGameObjects = new HashSet<GameObject>();
						for( int i = 0; i < searchResult.Count; i++ )
							searchResult[i].AddGameObjectsTo( uniqueGameObjects );

						if( uniqueGameObjects.Count > 0 )
						{
							GameObject[] objects = new GameObject[uniqueGameObjects.Count];
							uniqueGameObjects.CopyTo( objects );
							Selection.objects = objects;
						}
					}

					// Select all the references without filtering them
					if( GUILayout.Button( "Select All\n(Object-wise)", Utilities.GL_HEIGHT_35 ) )
					{
						HashSet<Object> uniqueObjects = new HashSet<Object>();
						for( int i = 0; i < searchResult.Count; i++ )
							searchResult[i].AddObjectsTo( uniqueObjects );

						if( uniqueObjects.Count > 0 )
						{
							Object[] objects = new Object[uniqueObjects.Count];
							uniqueObjects.CopyTo( objects );
							Selection.objects = objects;
						}
					}

					GUILayout.EndHorizontal();

					GUILayout.Space( 10 );

					showTooltips = EditorGUILayout.ToggleLeft( "Show tooltips", showTooltips );

					GUILayout.Space( 10 );

					GUILayout.Label( "Path drawing mode:" );

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Full: Draw the complete paths to the references (can be slow with too many references)", pathDrawingMode == PathDrawingMode.Full ) )
						pathDrawingMode = PathDrawingMode.Full;

					GUILayout.EndHorizontal();

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Shorter: Draw only the most relevant unique parts of the complete paths that start with a UnityEngine.Object", pathDrawingMode == PathDrawingMode.ShortRelevantParts ) )
						pathDrawingMode = PathDrawingMode.ShortRelevantParts;

					GUILayout.EndHorizontal();

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Shortest: Draw only the last two nodes of complete paths that are unique", pathDrawingMode == PathDrawingMode.Shortest ) )
						pathDrawingMode = PathDrawingMode.Shortest;

					GUILayout.EndHorizontal();

					// Tooltip gets its value in ReferenceHolder.DrawOnGUI function
					tooltip = null;
					for( int i = 0; i < searchResult.Count; i++ )
						searchResult[i].DrawOnGUI( pathDrawingMode );

					if( tooltip != null )
					{
						// Show tooltip at mouse position
						Vector2 mousePos = Event.current.mousePosition;
						Vector2 size = Utilities.TooltipGUIStyle.CalcSize( new GUIContent( tooltip ) );

						GUI.Box( new Rect( new Vector2( mousePos.x - size.x * 0.5f, mousePos.y - size.y ), size ), tooltip, Utilities.TooltipGUIStyle );
					}
				}
			}

			GUILayout.Space( 10 );

			GUILayout.EndVertical();

			EditorGUILayout.EndScrollView();
		}

		// Exposes searchedAssets as an array field on GUI
		private bool ExposeSearchedAssets()
		{
			bool hasChanged = false;
			Event ev = Event.current;

			GUILayout.BeginHorizontal();

			GUILayout.Label( "Asset(s):" );

			if( GUI.enabled )
			{
				// Handle drag & drop references to array
				// Credit: https://answers.unity.com/answers/657877/view.html
				if( ( ev.type == EventType.DragPerform || ev.type == EventType.DragUpdated ) &&
					GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
					if( ev.type == EventType.DragPerform )
					{
						DragAndDrop.AcceptDrag();

						Object[] draggedObjects = DragAndDrop.objectReferences;
						if( draggedObjects.Length > 0 )
						{
							for( int i = 0; i < draggedObjects.Length; i++ )
							{
								if( draggedObjects[i] != null && !draggedObjects[i].Equals( null ) )
								{
									hasChanged = true;
									objectsToSearch.Add( draggedObjects[i] );
								}
							}
						}
					}

					ev.Use();
				}

				if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
					objectsToSearch.Insert( 0, null );
			}

			GUILayout.EndHorizontal();

			for( int i = 0; i < objectsToSearch.Count; i++ )
			{
				GUI.changed = false;
				GUILayout.BeginHorizontal();

				Object prevAssetToSearch = objectsToSearch[i];
				objectsToSearch[i] = EditorGUILayout.ObjectField( "", objectsToSearch[i], typeof( Object ), true );

				if( GUI.changed && prevAssetToSearch != objectsToSearch[i] )
					hasChanged = true;

				if( GUI.enabled )
				{
					if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
						objectsToSearch.Insert( i + 1, null );

					if( GUILayout.Button( "-", Utilities.GL_WIDTH_25 ) )
					{
						if( objectsToSearch[i] != null && !objectsToSearch[i].Equals( null ) )
							hasChanged = true;

						objectsToSearch.RemoveAt( i-- );
					}
				}

				GUILayout.EndHorizontal();
			}

			return hasChanged;
		}

		// Called when the value of assetsToSearch has changed in editor window
		private void OnSearchedAssetsChanged()
		{
			errorMessage = string.Empty;
			subAssetsToSearch.Clear();

			if( AreSearchedAssetsEmpty() )
				return;

			MonoScript[] monoScriptsInProject = null;
			HashSet<Object> currentSubAssets = new HashSet<Object>();
			for( int i = 0; i < objectsToSearch.Count; i++ )
			{
				Object assetToSearch = objectsToSearch[i];
				if( assetToSearch == null || assetToSearch.Equals( null ) )
					continue;

				if( !assetToSearch.IsAsset() || !AssetDatabase.IsMainAsset( assetToSearch ) || assetToSearch is SceneAsset )
					continue;

				if( assetToSearch is DefaultAsset && AssetDatabase.IsValidFolder( AssetDatabase.GetAssetPath( assetToSearch ) ) )
					Debug.Log( assetToSearch );

				// Find sub-assets of the searched asset(s) (if any)
				Object[] assets = AssetDatabase.LoadAllAssetsAtPath( AssetDatabase.GetAssetPath( assetToSearch ) );
				for( int j = 0; j < assets.Length; j++ )
				{
					Object asset = assets[j];
					if( asset == null || asset.Equals( null ) || asset is Component )
						continue;

					if( currentSubAssets.Contains( asset ) )
						continue;

					if( asset != assetToSearch )
					{
						subAssetsToSearch.Add( new SubAssetToSearch( asset, true ) );
						currentSubAssets.Add( asset );
					}

					// MonoScripts are a special case such that other MonoScript objects
					// that extend this MonoScript are also considered a sub-asset
					if( asset is MonoScript )
					{
						Type monoScriptType = ( (MonoScript) asset ).GetClass();
						if( monoScriptType == null || ( !monoScriptType.IsInterface && !typeof( Component ).IsAssignableFrom( monoScriptType ) ) )
							continue;

						// Find all MonoScript objects in the project
						if( monoScriptsInProject == null )
						{
							string[] monoScriptGuids = AssetDatabase.FindAssets( "t:MonoScript" );
							monoScriptsInProject = new MonoScript[monoScriptGuids.Length];
							for( int k = 0; k < monoScriptGuids.Length; k++ )
								monoScriptsInProject[k] = AssetDatabase.LoadAssetAtPath<MonoScript>( AssetDatabase.GUIDToAssetPath( monoScriptGuids[k] ) );
						}

						// Add any MonoScript objects that extend this MonoScript as a sub-asset
						for( int k = 0; k < monoScriptsInProject.Length; k++ )
						{
							Type otherMonoScriptType = monoScriptsInProject[k].GetClass();
							if( otherMonoScriptType == null || monoScriptType == otherMonoScriptType || !monoScriptType.IsAssignableFrom( otherMonoScriptType ) )
								continue;

							if( !currentSubAssets.Contains( monoScriptsInProject[k] ) )
							{
								subAssetsToSearch.Add( new SubAssetToSearch( monoScriptsInProject[k], false ) );
								currentSubAssets.Add( monoScriptsInProject[k] );
							}
						}
					}
				}
			}
		}

		private bool AreSearchedAssetsEmpty()
		{
			for( int i = 0; i < objectsToSearch.Count; i++ )
			{
				if( objectsToSearch[i] != null && !objectsToSearch[i].Equals( null ) )
					return false;
			}

			return true;
		}

		// Search for references!
		private void ExecuteQuery()
		{
			searchCount = 0;
			searchStartTime = EditorApplication.timeSinceStartup;

			// Initialize commonly used variables
			if( searchResult == null )
				searchResult = new List<ReferenceHolder>( 16 );
			else
				searchResult.Clear();

			if( typeToVariables == null )
				typeToVariables = new Dictionary<Type, VariableGetterHolder[]>( 4096 );
			else if( searchDepthLimit != prevSearchDepthLimit || prevFieldModifiers != fieldModifiers || prevPropertyModifiers != propertyModifiers )
				typeToVariables.Clear();

			if( searchedObjects == null )
				searchedObjects = new Dictionary<string, ReferenceNode>( 32768 );
			else
				searchedObjects.Clear();

			if( callStack == null )
				callStack = new Stack<object>( 64 );
			else
				callStack.Clear();

			if( objectsToSearchSet == null )
				objectsToSearchSet = new HashSet<Object>();
			else
				objectsToSearchSet.Clear();

			if( sceneObjectsToSearchSet == null )
				sceneObjectsToSearchSet = new HashSet<Object>();
			else
				sceneObjectsToSearchSet.Clear();

			if( sceneObjectsToSearchScenesSet == null )
				sceneObjectsToSearchScenesSet = new HashSet<string>();
			else
				sceneObjectsToSearchScenesSet.Clear();

			if( assetsToSearchSet == null )
				assetsToSearchSet = new HashSet<Object>();
			else
				assetsToSearchSet.Clear();

			if( assetsToSearchPathsSet == null )
				assetsToSearchPathsSet = new HashSet<string>();
			else
				assetsToSearchPathsSet.Clear();

			if( assetDependencyCache == null )
			{
				LoadCache();
				searchStartTime = EditorApplication.timeSinceStartup;
			}
			else
			{
				foreach( var cacheEntry in assetDependencyCache.Values )
					cacheEntry.verified = false;
			}

			prevSearchDepthLimit = searchDepthLimit;
			prevFieldModifiers = fieldModifiers;
			prevPropertyModifiers = propertyModifiers;

			searchMaterialAssets = false;
			searchPrefabConnections = false;
			searchMonoBehavioursForScript = false;
			searchRenderers = false;
			searchMaterialsForShader = false;
			searchMaterialsForTexture = false;

			// Store the searched asset(s) and their sub-assets (if any) in a set
			try
			{
				// Temporarily add main searched asset(s) to sub-assets list to avoid duplicate code
				for( int i = 0; i < objectsToSearch.Count; i++ )
					subAssetsToSearch.Add( new SubAssetToSearch( objectsToSearch[i], true ) );

				for( int i = 0; i < subAssetsToSearch.Count; i++ )
				{
					if( subAssetsToSearch[i].shouldSearch )
					{
						Object obj = subAssetsToSearch[i].subAsset;
						if( obj == null || obj.Equals( null ) )
							continue;

						if( obj is SceneAsset )
							continue;

						objectsToSearchSet.Add( obj );

						bool isAsset = obj.IsAsset();
						if( isAsset )
						{
							assetsToSearchSet.Add( obj );

							string assetPath = AssetDatabase.GetAssetPath( obj );
							if( !string.IsNullOrEmpty( assetPath ) )
								assetsToSearchPathsSet.Add( assetPath );
						}
						else
						{
							sceneObjectsToSearchSet.Add( obj );

							if( obj is GameObject )
								sceneObjectsToSearchScenesSet.Add( ( (GameObject) obj ).scene.path );
							else if( obj is Component )
								sceneObjectsToSearchScenesSet.Add( ( (Component) obj ).gameObject.scene.path );
						}

						if( obj is GameObject )
						{
							// If searched asset is a GameObject, include its components in the search
							Component[] components = ( (GameObject) obj ).GetComponents<Component>();
							for( int j = 0; j < components.Length; j++ )
							{
								if( components[j] == null || components[j].Equals( null ) )
									continue;

								objectsToSearchSet.Add( components[j] );

								if( isAsset )
									assetsToSearchSet.Add( components[j] );
								else
									sceneObjectsToSearchSet.Add( components[j] );
							}
						}
					}
				}
			}
			finally
			{
				subAssetsToSearch.RemoveRange( subAssetsToSearch.Count - objectsToSearch.Count, objectsToSearch.Count );
			}

			foreach( Object obj in objectsToSearchSet )
			{
				// Initialize the nodes of searched asset(s)
				searchedObjects.Add( obj.Hash(), PopReferenceNode( obj ) );

				if( obj is Texture )
				{
					searchMaterialAssets = true;
					searchRenderers = true;
					searchMaterialsForTexture = true;
				}
				else if( obj is Material )
				{
					searchRenderers = true;
				}
				else if( obj is MonoScript )
				{
					searchMonoBehavioursForScript = true;
				}
				else if( obj is Shader )
				{
					searchMaterialAssets = true;
					searchRenderers = true;
					searchMaterialsForShader = true;
				}
				else if( obj is GameObject )
				{
					searchPrefabConnections = true;
				}
			}

			// Find the scenes to search for references
			HashSet<string> scenesToSearch = new HashSet<string>();
			if( searchInAllScenes )
			{
				// Get all scenes from the Assets folder
				string[] sceneGuids = AssetDatabase.FindAssets( "t:SceneAsset" );
				for( int i = 0; i < sceneGuids.Length; i++ )
					scenesToSearch.Add( AssetDatabase.GUIDToAssetPath( sceneGuids[i] ) );
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
					// Get all scenes in build settings
					EditorBuildSettingsScene[] scenesTemp = EditorBuildSettings.scenes;
					for( int i = 0; i < scenesTemp.Length; i++ )
					{
						if( !searchInScenesInBuildTickedOnly || scenesTemp[i].enabled )
							scenesToSearch.Add( scenesTemp[i].path );
					}
				}
			}

			// By default, search only serializable variables for references
			searchSerializableVariablesOnly = true;

			// Don't search assets if searched object is a scene object as assets can't hold references to scene objects
			if( searchInAssetsFolder && assetsToSearchSet.Count > 0 )
			{
				currentReferenceHolder = new ReferenceHolder( "Project View (Assets)", false );

				// Search through all the prefabs and imported models in the project
				string[] pathsToAssets = AssetDatabase.FindAssets( "t:GameObject" );
				for( int i = 0; i < pathsToAssets.Length; i++ )
					SearchGameObjectRecursively( AssetDatabase.LoadAssetAtPath<GameObject>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) ) );

				// Search through all the scriptable objects in the project
				pathsToAssets = AssetDatabase.FindAssets( "t:ScriptableObject" );
				for( int i = 0; i < pathsToAssets.Length; i++ )
					BeginSearchObject( AssetDatabase.LoadAssetAtPath<ScriptableObject>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) ) );

				// If a searched asset is shader or texture, search through all the materials in the project
				if( searchMaterialAssets )
				{
					pathsToAssets = AssetDatabase.FindAssets( "t:Material" );
					for( int i = 0; i < pathsToAssets.Length; i++ )
						BeginSearchObject( AssetDatabase.LoadAssetAtPath<Material>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) ) );
				}

				// Search through all the animation clips in the project
				pathsToAssets = AssetDatabase.FindAssets( "t:AnimationClip" );
				for( int i = 0; i < pathsToAssets.Length; i++ )
					BeginSearchObject( AssetDatabase.LoadAssetAtPath<AnimationClip>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) ) );

				// Search through all the animator controllers in the project
				pathsToAssets = AssetDatabase.FindAssets( "t:RuntimeAnimatorController" );
				for( int i = 0; i < pathsToAssets.Length; i++ )
					BeginSearchObject( AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>( AssetDatabase.GUIDToAssetPath( pathsToAssets[i] ) ) );

				// If a reference is found in the Project view, save the results
				if( currentReferenceHolder.NumberOfReferences > 0 )
					searchResult.Add( currentReferenceHolder );
			}

			// Search non-serializable variables for references only if we are currently searching a scene and the editor is in play mode
			if( EditorApplication.isPlaying )
				searchSerializableVariablesOnly = false;

			foreach( string scenePath in scenesToSearch )
			{
				// Search scene for references
				if( !string.IsNullOrEmpty( scenePath ) )
					SearchScene( scenePath );
			}

			// Search through all the GameObjects under the DontDestroyOnLoad scene (if exists)
			if( EditorApplication.isPlaying )
			{
				currentReferenceHolder = new ReferenceHolder( "DontDestroyOnLoad", false );

				GameObject[] rootGameObjects = GetDontDestroyOnLoadObjects();
				for( int i = 0; i < rootGameObjects.Length; i++ )
					SearchGameObjectRecursively( rootGameObjects[i] );

				if( currentReferenceHolder.NumberOfReferences > 0 )
					searchResult.Add( currentReferenceHolder );
			}

			for( int i = 0; i < searchResult.Count; i++ )
				searchResult[i].InitializeNodes();

			// Log some c00l stuff to console
			Debug.Log( "Searched " + searchCount + " objects in " + ( EditorApplication.timeSinceStartup - searchStartTime ).ToString( "F2" ) + " seconds" );

			// Search is complete!
			currentPhase = Phase.Complete;
		}

		// Search a scene for references
		private void SearchScene( string scenePath )
		{
			Scene scene = EditorSceneManager.GetSceneByPath( scenePath );

			bool canContainSceneObjectReference = scene.isLoaded && ( !EditorSceneManager.preventCrossSceneReferences || sceneObjectsToSearchScenesSet.Contains( scenePath ) );
			if( !canContainSceneObjectReference )
			{
				bool canContainAssetReference = AssetHasAnyReferenceTo( scenePath, assetsToSearchPathsSet );
				if( !canContainAssetReference )
					return;
			}

			if( !EditorApplication.isPlaying )
				scene = EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Additive );

			currentReferenceHolder = new ReferenceHolder( scenePath, true );

			// Search through all the GameObjects in the scene
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			for( int i = 0; i < rootGameObjects.Length; i++ )
				SearchGameObjectRecursively( rootGameObjects[i] );

			// If no references are found in the scene and if the scene is not part of the initial scene setup, close it
			if( currentReferenceHolder.NumberOfReferences == 0 )
			{
				if( !EditorApplication.isPlaying )
				{
					bool sceneIsOneOfInitials = false;
					for( int i = 0; i < initialSceneSetup.Length; i++ )
					{
						if( initialSceneSetup[i].path == scenePath )
						{
							if( !initialSceneSetup[i].isLoaded )
								EditorSceneManager.CloseScene( scene, false );

							sceneIsOneOfInitials = true;
							break;
						}
					}

					if( !sceneIsOneOfInitials )
						EditorSceneManager.CloseScene( scene, true );
				}
			}
			else
			{
				// Some references are found in this scene, save the results
				searchResult.Add( currentReferenceHolder );
			}
		}

		// Search a GameObject and its children for references recursively
		private void SearchGameObjectRecursively( GameObject go )
		{
			BeginSearchObject( go );

			Transform tr = go.transform;
			for( int i = 0; i < tr.childCount; i++ )
				SearchGameObjectRecursively( tr.GetChild( i ).gameObject );
		}

		// Begin searching a root object (like a GameObject or an asset)
		private void BeginSearchObject( Object obj )
		{
			if( objectsToSearchSet.Contains( obj ) )
			{
				// Rare case: if searched object is a scene GameObject, search its components for references 
				// instead of completely ignoring the GameObject
				if( obj is GameObject && !obj.IsAsset() )
				{
					ReferenceNode referenceNode = PopReferenceNode( obj );
					Component[] components = ( (GameObject) obj ).GetComponents<Component>();
					for( int i = 0; i < components.Length; i++ )
					{
						ReferenceNode componentNode = SearchComponent( components[i] );
						if( componentNode == null )
							continue;

						if( componentNode.NumberOfOutgoingLinks > 0 )
							referenceNode.AddLinkTo( componentNode );
						else
							PoolReferenceNode( componentNode );
					}

					if( referenceNode.NumberOfOutgoingLinks > 0 )
						currentReferenceHolder.AddReference( referenceNode );
					else
						PoolReferenceNode( referenceNode );
				}

				return;
			}

			ReferenceNode searchResult = SearchObject( obj );
			if( searchResult != null )
				currentReferenceHolder.AddReference( searchResult );
		}

		// Search an object for references
		private ReferenceNode SearchObject( object obj )
		{
			if( obj == null || obj.Equals( null ) )
				return null;

			// Avoid recursion (which leads to stackoverflow exception) using a stack
			if( callStack.Contains( obj ) )
				return null;

			// Hashing does not work well with structs all the time, don't cache search results for structs
			string objHash = null;
			if( !( obj is ValueType ) )
			{
				objHash = obj.Hash();

				// If object was searched before, return the cached result
				ReferenceNode cachedResult;
				if( searchedObjects.TryGetValue( objHash, out cachedResult ) )
					return cachedResult;
			}

			searchCount++;

			ReferenceNode result;
			Object unityObject = obj as Object;
			if( unityObject != null )
			{
				// If we hit a searched asset
				if( objectsToSearchSet.Contains( unityObject ) )
				{
					result = PopReferenceNode( unityObject );
					searchedObjects.Add( objHash, result );

					return result;
				}

				if( unityObject.IsAsset() )
				{
					// If the Object is an asset, search it in detail only if its dependencies contain at least one of the searched asset(s)
					if( !AssetHasAnyReferenceTo( AssetDatabase.GetAssetPath( unityObject ), assetsToSearchPathsSet ) )
					{
						searchedObjects.Add( objHash, null );
						return null;
					}
				}

				callStack.Push( unityObject );

				// Search the Object in detail
				if( unityObject is GameObject )
					result = SearchGameObject( (GameObject) unityObject );
				else if( unityObject is Component )
					result = SearchComponent( (Component) unityObject );
				else if( unityObject is Material )
					result = SearchMaterial( (Material) unityObject );
				else if( unityObject is RuntimeAnimatorController )
					result = SearchAnimatorController( (RuntimeAnimatorController) unityObject );
				else if( unityObject is AnimationClip )
					result = SearchAnimationClip( (AnimationClip) unityObject );
				else
				{
					result = PopReferenceNode( unityObject );
					SearchFieldsAndPropertiesOf( result );
				}

				callStack.Pop();
			}
			else
			{
				// Comply with the recursive search limit
				if( currentDepth >= searchDepthLimit )
					return null;

				callStack.Push( obj );
				currentDepth++;

				result = PopReferenceNode( obj );
				SearchFieldsAndPropertiesOf( result );

				currentDepth--;
				callStack.Pop();
			}

			if( result != null && result.NumberOfOutgoingLinks == 0 )
			{
				PoolReferenceNode( result );
				result = null;
			}

			// Cache the search result if we are skimming through a class (not a struct; i.e. objHash != null)
			// and if the object is a UnityEngine.Object (if not, cache the result only if we have actually found something
			// or we are at the root of the search; i.e. currentDepth == 0)
			if( ( result != null || unityObject != null || currentDepth == 0 ) && objHash != null )
				searchedObjects.Add( objHash, result );

			return result;
		}

		// Search through components of this GameObject in detail
		private ReferenceNode SearchGameObject( GameObject go )
		{
			ReferenceNode referenceNode = PopReferenceNode( go );

			// Check if this GameObject's prefab is one of the selected assets
			if( searchPrefabConnections )
			{
#if UNITY_2018_3_OR_NEWER
				Object prefab = PrefabUtility.GetCorrespondingObjectFromSource( go );
				if( objectsToSearchSet.Contains( prefab ) && go == PrefabUtility.GetNearestPrefabInstanceRoot( go ) )
#else
				Object prefab = PrefabUtility.GetPrefabParent( go );
				if( objectsToSearchSet.Contains( prefab ) && go == PrefabUtility.FindRootGameObjectWithSameParentPrefab( go ) )
#endif
					referenceNode.AddLinkTo( GetReferenceNode( prefab ), "Prefab object" );
			}

			// Search through all the components of the object
			Component[] components = go.GetComponents<Component>();
			for( int i = 0; i < components.Length; i++ )
			{
				ReferenceNode result = SearchObject( components[i] );
				if( result != null )
					referenceNode.AddLinkTo( result );
			}

			return referenceNode;
		}

		// Check if the asset is used in this component
		private ReferenceNode SearchComponent( Component component )
		{
			// Ignore Transform component (no object field to search for)
			if( component is Transform )
				return null;

			ReferenceNode referenceNode = PopReferenceNode( component );

			if( searchMonoBehavioursForScript && component is MonoBehaviour )
			{
				// If a searched asset is script, check if this component is an instance of it
				MonoScript script = MonoScript.FromMonoBehaviour( (MonoBehaviour) component );
				if( objectsToSearchSet.Contains( script ) )
					referenceNode.AddLinkTo( GetReferenceNode( script ) );
			}

			if( searchRenderers && component is Renderer )
			{
				// If an asset is a shader, texture or material, and this component is a Renderer,
				// search it for references
				Material[] materials = ( (Renderer) component ).sharedMaterials;
				for( int j = 0; j < materials.Length; j++ )
					referenceNode.AddLinkTo( SearchObject( materials[j] ) );
			}

			if( component is Animation )
			{
				// If this component is an Animation, search its animation clips for references
				foreach( AnimationState anim in (Animation) component )
					referenceNode.AddLinkTo( SearchObject( anim.clip ) );
			}

			if( component is Animator )
			{
				// If this component is an Animator, search its animation clips for references
				referenceNode.AddLinkTo( SearchObject( ( (Animator) component ).runtimeAnimatorController ) );
			}

			SearchFieldsAndPropertiesOf( referenceNode );

			return referenceNode;
		}

		// Check if the asset is used in this material
		private ReferenceNode SearchMaterial( Material material )
		{
			ReferenceNode referenceNode = PopReferenceNode( material );

			if( searchMaterialsForShader && objectsToSearchSet.Contains( material.shader ) )
				referenceNode.AddLinkTo( GetReferenceNode( material.shader ), "Shader" );

			if( searchMaterialsForTexture )
			{
				// Search through all the textures attached to this material
				// Credit: http://answers.unity3d.com/answers/1116025/view.html
				Shader shader = material.shader;
				int shaderPropertyCount = ShaderUtil.GetPropertyCount( shader );
				for( int k = 0; k < shaderPropertyCount; k++ )
				{
					if( ShaderUtil.GetPropertyType( shader, k ) == ShaderUtil.ShaderPropertyType.TexEnv )
					{
						string propertyName = ShaderUtil.GetPropertyName( shader, k );
						Texture assignedTexture = material.GetTexture( propertyName );
						if( objectsToSearchSet.Contains( assignedTexture ) )
							referenceNode.AddLinkTo( GetReferenceNode( assignedTexture ), "Shader property: " + propertyName );
					}
				}
			}

			return referenceNode;
		}

		// Check if the asset is used in this animator controller
		private ReferenceNode SearchAnimatorController( RuntimeAnimatorController controller )
		{
			ReferenceNode referenceNode = PopReferenceNode( controller );

			AnimationClip[] animClips = controller.animationClips;
			for( int j = 0; j < animClips.Length; j++ )
				referenceNode.AddLinkTo( SearchObject( animClips[j] ) );

			return referenceNode;
		}

		// Check if the asset is used in this animation clip (and its keyframes)
		private ReferenceNode SearchAnimationClip( AnimationClip clip )
		{
			ReferenceNode referenceNode = PopReferenceNode( clip );

			// Get all curves from animation clip
			EditorCurveBinding[] objectCurves = AnimationUtility.GetObjectReferenceCurveBindings( clip );
			for( int i = 0; i < objectCurves.Length; i++ )
			{
				// Search through all the keyframes in this curve
				ObjectReferenceKeyframe[] keyframes = AnimationUtility.GetObjectReferenceCurve( clip, objectCurves[i] );
				for( int j = 0; j < keyframes.Length; j++ )
					referenceNode.AddLinkTo( SearchObject( keyframes[j].value ), "Keyframe: " + keyframes[j].time );
			}

			return referenceNode;
		}

		// Search through field and properties of an object for references
		private void SearchFieldsAndPropertiesOf( ReferenceNode referenceNode )
		{
			// Get filtered variables for this object
			VariableGetterHolder[] variables = GetFilteredVariablesForType( referenceNode.nodeObject.GetType() );
			for( int i = 0; i < variables.Length; i++ )
			{
				// When possible, don't search non-serializable variables
				if( searchSerializableVariablesOnly && !variables[i].isSerializable )
					continue;

				try
				{
					object variableValue = variables[i].Get( referenceNode.nodeObject );
					if( variableValue == null )
						continue;

					if( !( variableValue is IEnumerable ) || variableValue is Transform )
						referenceNode.AddLinkTo( SearchObject( variableValue ), ( variables[i].isProperty ? "Property: " : "Variable: " ) + variables[i].name );
					else
					{
						// If the field is IEnumerable (possibly an array or collection), search through members of it
						// Note that Transform IEnumerable (children of the transform) is not iterated
						foreach( object arrayItem in (IEnumerable) variableValue )
							referenceNode.AddLinkTo( SearchObject( arrayItem ), ( variables[i].isProperty ? "Property (IEnumerable): " : "Variable (IEnumerable): " ) + variables[i].name );
					}
				}
				catch( UnassignedReferenceException )
				{ }
				catch( MissingReferenceException )
				{ }
			}
		}

		// Get filtered variables for a type
		private VariableGetterHolder[] GetFilteredVariablesForType( Type type )
		{
			VariableGetterHolder[] result;
			if( typeToVariables.TryGetValue( type, out result ) )
				return result;

			// This is the first time this type of object is seen, filter and cache its variables
			// Variable filtering process:
			// 1- skip Obsolete variables
			// 2- skip primitive types, enums and strings
			// 3- skip common Unity types that can't hold any references (e.g. Vector3, Rect, Color, Quaternion)

			validVariables.Clear();

			// Filter the fields
			if( fieldModifiers != ( BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
			{
				Type currType = type;
				while( currType != typeof( object ) )
				{
					FieldInfo[] fields = currType.GetFields( fieldModifiers );
					for( int i = 0; i < fields.Length; i++ )
					{
						// Skip obsolete fields
						if( Attribute.IsDefined( fields[i], typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						Type fieldType = fields[i].FieldType;
						if( fieldType.IsPrimitive || fieldType == typeof( string ) || fieldType.IsEnum )
							continue;

						if( fieldType.IsPrimitiveUnityType() )
							continue;

						VariableGetVal getter = fields[i].CreateGetter( type );
						if( getter != null )
							validVariables.Add( new VariableGetterHolder( fields[i], getter, fields[i].IsSerializable() ) );
					}

					currType = currType.BaseType;
				}
			}

			if( propertyModifiers != ( BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
			{
				Type currType = type;
				while( currType != typeof( object ) )
				{
					PropertyInfo[] properties = currType.GetProperties( propertyModifiers );
					for( int i = 0; i < properties.Length; i++ )
					{
						// Skip obsolete properties
						if( Attribute.IsDefined( properties[i], typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						Type propertyType = properties[i].PropertyType;
						if( propertyType.IsPrimitive || propertyType == typeof( string ) || propertyType.IsEnum )
							continue;

						if( propertyType.IsPrimitiveUnityType() )
							continue;

						// Additional filtering for properties:
						// 1- Ignore "gameObject", "transform", "rectTransform" and "attachedRigidbody" properties of Component's to get more useful results
						// 2- Ignore "canvasRenderer" and "canvas" properties of Graphic components
						// 3 & 4- Prevent accessing properties of Unity that instantiate an existing resource (causing leak)
						string propertyName = properties[i].Name;
						if( typeof( Component ).IsAssignableFrom( currType ) && ( propertyName.Equals( "gameObject" ) ||
							propertyName.Equals( "transform" ) || propertyName.Equals( "attachedRigidbody" ) || propertyName.Equals( "rectTransform" ) ) )
							continue;
						else if( typeof( UnityEngine.UI.Graphic ).IsAssignableFrom( currType ) &&
							( propertyName.Equals( "canvasRenderer" ) || propertyName.Equals( "canvas" ) ) )
							continue;
						else if( typeof( MeshFilter ).IsAssignableFrom( currType ) && propertyName.Equals( "mesh" ) )
							continue;
						else if( typeof( Renderer ).IsAssignableFrom( currType ) &&
							( propertyName.Equals( "sharedMaterial" ) || propertyName.Equals( "sharedMaterials" ) ) )
							continue;
						else if( ( propertyName.Equals( "material" ) || propertyName.Equals( "materials" ) ) &&
							( typeof( Renderer ).IsAssignableFrom( currType ) || typeof( Collider ).IsAssignableFrom( currType ) ||
							typeof( Collider2D ).IsAssignableFrom( currType )
#pragma warning disable 0618
							|| typeof( GUIText ).IsAssignableFrom( currType ) ) )
#pragma warning restore 0618
							continue;
						else
						{
							VariableGetVal getter = properties[i].CreateGetter();
							if( getter != null )
								validVariables.Add( new VariableGetterHolder( properties[i], getter, properties[i].IsSerializable() ) );
						}
					}

					currType = currType.BaseType;
				}
			}

			result = validVariables.ToArray();

			// Cache the filtered fields
			typeToVariables.Add( type, result );

			return result;
		}

		// Check if the asset at specified path depends on any of the references
		private bool AssetHasAnyReferenceTo( string assetPath, HashSet<string> referencePaths )
		{
			CacheEntry cacheEntry;
			if( !assetDependencyCache.TryGetValue( assetPath, out cacheEntry ) )
			{
				cacheEntry = new CacheEntry( assetPath );
				assetDependencyCache[assetPath] = cacheEntry;
			}
			else if( !cacheEntry.verified )
				cacheEntry.Verify( assetPath );

			if( cacheEntry.hasSelfDependency && referencePaths.Contains( assetPath ) )
				return true;

			string[] dependencies = cacheEntry.dependencies;
			for( int i = 0; i < dependencies.Length; i++ )
			{
				if( referencePaths.Contains( dependencies[i] ) )
					return true;
			}

			for( int i = 0; i < dependencies.Length; i++ )
			{
				if( AssetHasAnyReferenceTo( dependencies[i], referencePaths ) )
					return true;
			}

			return false;
		}

		// Get reference node for object
		private ReferenceNode GetReferenceNode( object nodeObject, string hash = null )
		{
			if( hash == null )
				hash = nodeObject.Hash();

			ReferenceNode result;
			if( !searchedObjects.TryGetValue( hash, out result ) || result == null )
			{
				result = PopReferenceNode( nodeObject );
				searchedObjects[hash] = result;
			}

			return result;
		}

		// Fetch a reference node from pool
		private ReferenceNode PopReferenceNode( object nodeObject )
		{
			ReferenceNode node;
			if( nodesPool.Count == 0 )
				node = new ReferenceNode();
			else
			{
				int index = nodesPool.Count - 1;
				node = nodesPool[index];
				nodesPool.RemoveAt( index );
			}

			node.nodeObject = nodeObject;
			return node;
		}

		// Pool a reference node
		private void PoolReferenceNode( ReferenceNode node )
		{
			node.Clear();
			nodesPool.Add( node );
		}

		// Retrieve the game objects listed under the DontDestroyOnLoad scene
		private GameObject[] GetDontDestroyOnLoadObjects()
		{
			GameObject temp = null;
			try
			{
				temp = new GameObject();
				Object.DontDestroyOnLoad( temp );
				Scene dontDestroyOnLoad = temp.scene;
				Object.DestroyImmediate( temp );
				temp = null;

				return dontDestroyOnLoad.GetRootGameObjects();
			}
			finally
			{
				if( temp != null )
					Object.DestroyImmediate( temp );
			}
		}

		// Check if all open scenes are saved (not dirty)
		private bool AreScenesSaved()
		{
			for( int i = 0; i < EditorSceneManager.loadedSceneCount; i++ )
			{
				Scene scene = EditorSceneManager.GetSceneAt( i );
				if( scene.isDirty || string.IsNullOrEmpty( scene.path ) )
					return false;
			}

			return true;
		}

		// Close the scenes that were not part of the initial scene setup
		private bool RestoreInitialSceneSetup()
		{
			if( EditorApplication.isPlaying || initialSceneSetup == null )
				return true;

			if( !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() )
				return false;

			SceneSetup[] sceneFinalSetup = EditorSceneManager.GetSceneManagerSetup();
			for( int i = 0; i < sceneFinalSetup.Length; i++ )
			{
				bool sceneIsOneOfInitials = false;
				for( int j = 0; j < initialSceneSetup.Length; j++ )
				{
					if( sceneFinalSetup[i].path == initialSceneSetup[j].path )
					{
						sceneIsOneOfInitials = true;
						break;
					}
				}

				if( !sceneIsOneOfInitials )
					EditorSceneManager.CloseScene( EditorSceneManager.GetSceneByPath( sceneFinalSetup[i].path ), true );
			}

			for( int i = 0; i < initialSceneSetup.Length; i++ )
			{
				if( !initialSceneSetup[i].isLoaded )
					EditorSceneManager.CloseScene( EditorSceneManager.GetSceneByPath( initialSceneSetup[i].path ), false );
			}

			return true;
		}

		private void SaveCache()
		{
			if( assetDependencyCache == null )
				return;

			string cachePath = Application.dataPath + "/../" + DEPENDENCY_CACHE_PATH;
			using( FileStream stream = new FileStream( cachePath, FileMode.Create ) )
			{
				try
				{
					new BinaryFormatter().Serialize( stream, assetDependencyCache );
				}
				catch( Exception e )
				{
					Debug.LogException( e );
				}
			}
		}

		private void LoadCache()
		{
			string cachePath = Application.dataPath + "/../" + DEPENDENCY_CACHE_PATH;
			if( File.Exists( cachePath ) )
			{
				using( FileStream stream = new FileStream( cachePath, FileMode.Open, FileAccess.Read ) )
				{
					try
					{
						assetDependencyCache = (Dictionary<string, CacheEntry>) new BinaryFormatter().Deserialize( stream );
					}
					catch( Exception e )
					{
						Debug.LogException( e );
					}
				}
			}

			// Generate cache for all assets for the first time
			if( assetDependencyCache == null )
			{
				assetDependencyCache = new Dictionary<string, CacheEntry>( 1024 * 8 );

				string[] allAssets = AssetDatabase.GetAllAssetPaths();
				if( allAssets.Length > 0 )
				{
					string title = "Please wait...";
					string message = "Generating cache for the first time";

					HashSet<string> temp = new HashSet<string>();
					EditorUtility.DisplayProgressBar( title, message, 0f );

					for( int i = 0; i < allAssets.Length; i++ )
					{
						AssetHasAnyReferenceTo( allAssets[i], temp );
						EditorUtility.DisplayProgressBar( title, message, (float) i / allAssets.Length );
					}

					EditorUtility.ClearProgressBar();
				}
			}
		}
	}
}