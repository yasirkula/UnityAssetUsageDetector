// Asset Usage Detector - by Suleyman Yasir KULA (yasirkula@gmail.com)

using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Reflection;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace.Extras
{
	public enum Phase { Setup, Processing, Complete };

	public class AssetUsageDetectorWindow : EditorWindow
	{
		private const float PLAY_MODE_REFRESH_INTERVAL = 1f; // Interval to refresh the editor window in play mode

		private const string PREFS_SEARCH_SCENES = "AUD_SceneSearch";
		private const string PREFS_SEARCH_ASSETS = "AUD_AssetsSearch";
		private const string PREFS_SEARCH_DEPTH_LIMIT = "AUD_Depth";
		private const string PREFS_SEARCH_FIELDS = "AUD_Fields";
		private const string PREFS_SEARCH_PROPERTIES = "AUD_Properties";
		private const string PREFS_PATH_DRAWING_MODE = "AUD_PathDrawing";
		private const string PREFS_SHOW_TOOLTIPS = "AUD_Tooltips";

		private AssetUsageDetector core = new AssetUsageDetector();
		private SearchResult searchResult; // Overall search results

		private List<ObjectToSearch> objectsToSearch = new List<ObjectToSearch>() { new ObjectToSearch( null ) };

		private Phase currentPhase = Phase.Setup;

		private bool searchInOpenScenes = true; // Scenes currently open in Hierarchy view
		private bool searchInScenesInBuild = true; // Scenes in build
		private bool searchInScenesInBuildTickedOnly = true; // Scenes in build (ticked only or not)
		private bool searchInAllScenes = true; // All scenes (including scenes that are not in build)
		private bool searchInAssetsFolder = true; // Assets in Project view

		private List<Object> searchInAssetsSubset = new List<Object>() { null }; // If not empty, only these assets are searched for references

		private int searchDepthLimit = 4; // Depth limit for recursively searching variables of objects

		private bool restoreInitialSceneSetup = true; // Close the additively loaded scenes that were not part of the initial scene setup

		private string errorMessage = string.Empty;
		private bool noAssetDatabaseChanges = false;

		private BindingFlags fieldModifiers, propertyModifiers;

		private SearchResultDrawParameters searchResultDrawParameters = new SearchResultDrawParameters( PathDrawingMode.ShortRelevantParts, false );

		private double nextPlayModeRefreshTime = 0f;

		private Vector2 scrollPosition = Vector2.zero;

		// Add "Asset Usage Detector" menu item to the Window menu
		[MenuItem( "Window/Asset Usage Detector" )]
		private static void Init()
		{
			AssetUsageDetectorWindow window = GetWindow<AssetUsageDetectorWindow>();
			window.titleContent = new GUIContent( "Asset Usage Detector" );
			window.minSize = new Vector2( 325f, 220f );

			window.LoadPrefs();
			window.Show();
		}

		private void OnDestroy()
		{
			if( core != null )
				core.SaveCache();

			SavePrefs();

			if( searchResult != null && currentPhase == Phase.Complete && !EditorApplication.isPlaying && searchResult.IsSceneSetupDifferentThanCurrentSetup() )
			{
				if( EditorUtility.DisplayDialog( "Scenes", "Restore initial scene setup?", "Yes", "Leave it as is" ) )
					searchResult.RestoreInitialSceneSetup();
			}
		}

		private void SavePrefs()
		{
			SceneSearchMode sceneSearchMode = SceneSearchMode.None;
			if( searchInOpenScenes )
				sceneSearchMode |= SceneSearchMode.OpenScenes;
			if( searchInScenesInBuild )
				sceneSearchMode |= searchInScenesInBuildTickedOnly ? SceneSearchMode.ScenesInBuildSettingsTickedOnly : SceneSearchMode.ScenesInBuildSettingsAll;
			if( searchInAllScenes )
				sceneSearchMode |= SceneSearchMode.AllScenes;

			EditorPrefs.SetInt( PREFS_SEARCH_SCENES, (int) sceneSearchMode );
			EditorPrefs.SetBool( PREFS_SEARCH_ASSETS, searchInAssetsFolder );
			EditorPrefs.SetInt( PREFS_SEARCH_DEPTH_LIMIT, searchDepthLimit );
			EditorPrefs.SetInt( PREFS_SEARCH_FIELDS, (int) fieldModifiers );
			EditorPrefs.SetInt( PREFS_SEARCH_PROPERTIES, (int) propertyModifiers );
			EditorPrefs.SetInt( PREFS_PATH_DRAWING_MODE, (int) searchResultDrawParameters.pathDrawingMode );
			EditorPrefs.SetBool( PREFS_SHOW_TOOLTIPS, searchResultDrawParameters.showTooltips );
		}

		private void LoadPrefs()
		{
			SceneSearchMode sceneSearchMode = (SceneSearchMode) EditorPrefs.GetInt( PREFS_SEARCH_SCENES, (int) ( SceneSearchMode.OpenScenes | SceneSearchMode.ScenesInBuildSettingsTickedOnly | SceneSearchMode.AllScenes ) );
			searchInOpenScenes = ( sceneSearchMode & SceneSearchMode.OpenScenes ) == SceneSearchMode.OpenScenes;
			searchInScenesInBuild = ( sceneSearchMode & SceneSearchMode.ScenesInBuildSettingsAll ) == SceneSearchMode.ScenesInBuildSettingsAll || ( sceneSearchMode & SceneSearchMode.ScenesInBuildSettingsTickedOnly ) == SceneSearchMode.ScenesInBuildSettingsTickedOnly;
			searchInScenesInBuildTickedOnly = ( sceneSearchMode & SceneSearchMode.ScenesInBuildSettingsAll ) != SceneSearchMode.ScenesInBuildSettingsAll;
			searchInAllScenes = ( sceneSearchMode & SceneSearchMode.AllScenes ) == SceneSearchMode.AllScenes;

			searchInAssetsFolder = EditorPrefs.GetBool( PREFS_SEARCH_ASSETS, true );
			searchDepthLimit = EditorPrefs.GetInt( PREFS_SEARCH_DEPTH_LIMIT, 4 );

			// Fetch public, protected and private non-static fields and properties from objects by default
			fieldModifiers = (BindingFlags) EditorPrefs.GetInt( PREFS_SEARCH_FIELDS, (int) ( BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic ) );
			propertyModifiers = (BindingFlags) EditorPrefs.GetInt( PREFS_SEARCH_PROPERTIES, (int) ( BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic ) );

			try
			{
				searchResultDrawParameters.pathDrawingMode = (PathDrawingMode) EditorPrefs.GetInt( PREFS_PATH_DRAWING_MODE, (int) PathDrawingMode.ShortRelevantParts );
			}
			catch
			{
				searchResultDrawParameters.pathDrawingMode = PathDrawingMode.ShortRelevantParts;
			}

			searchResultDrawParameters.showTooltips = EditorPrefs.GetBool( PREFS_SHOW_TOOLTIPS, false );
		}

		private void Update()
		{
			// Refresh the window at a regular interval in play mode to update the tooltip
			if( EditorApplication.isPlaying && currentPhase == Phase.Complete && searchResultDrawParameters.showTooltips && EditorApplication.timeSinceStartup >= nextPlayModeRefreshTime )
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
					ReturnToSetupPhase( restoreInitialSceneSetup );
			}
			else if( currentPhase == Phase.Setup )
			{
				if( ExposeSearchedObjects() )
					errorMessage = string.Empty;

				GUILayout.Space( 10 );

				GUILayout.Box( "SEARCH IN", Utilities.GL_EXPAND_WIDTH );

				searchInAssetsFolder = EditorGUILayout.ToggleLeft( "Project view (Assets folder)", searchInAssetsFolder );

				if( searchInAssetsFolder )
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space( 35 );
					GUILayout.BeginVertical();

					ExposeAssetsToSearch();

					GUILayout.EndVertical();
					GUILayout.EndHorizontal();
				}

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

				noAssetDatabaseChanges = EditorGUILayout.ToggleLeft( "I haven't modified any assets/scenes since the last search (faster search)", noAssetDatabaseChanges );

				if( GUILayout.Button( "GO!", Utilities.GL_HEIGHT_30 ) )
				{
					if( objectsToSearch.IsEmpty() )
						errorMessage = "ADD AN ASSET TO THE LIST FIRST!";
					else if( !EditorApplication.isPlaying && !AreScenesSaved() )
					{
						// Don't start the search if at least one scene is currently dirty (not saved)
						errorMessage = "SAVE OPEN SCENES FIRST!";
					}
					else
					{
						errorMessage = string.Empty;
						currentPhase = Phase.Processing;

						SceneSearchMode sceneSearchMode = SceneSearchMode.None;
						if( searchInAllScenes )
							sceneSearchMode = SceneSearchMode.AllScenes;
						else
						{
							if( searchInOpenScenes )
								sceneSearchMode = SceneSearchMode.OpenScenes;

							if( searchInScenesInBuild )
							{
								if( searchInScenesInBuildTickedOnly )
									sceneSearchMode |= SceneSearchMode.ScenesInBuildSettingsTickedOnly;
								else
									sceneSearchMode |= SceneSearchMode.ScenesInBuildSettingsAll;
							}
						}

						// Start searching
						searchResult = core.Run( new AssetUsageDetector.Parameters()
						{
							objectsToSearch = !objectsToSearch.IsEmpty() ? new ObjectToSearchEnumerator( objectsToSearch ) : null,
							searchInScenes = sceneSearchMode,
							searchInAssetsFolder = searchInAssetsFolder,
							searchInAssetsSubset = !searchInAssetsSubset.IsEmpty() ? searchInAssetsSubset : null,
							fieldModifiers = fieldModifiers,
							propertyModifiers = propertyModifiers,
							searchDepthLimit = searchDepthLimit,
							noAssetDatabaseChanges = noAssetDatabaseChanges
						} );

						currentPhase = Phase.Complete;
					}
				}
			}
			else if( currentPhase == Phase.Complete )
			{
				// Draw the results of the search
				GUI.enabled = false;

				ExposeSearchedObjects();

				GUILayout.Space( 10 );
				GUI.enabled = true;

				restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup after search is reset (Recommended)", restoreInitialSceneSetup );

				if( GUILayout.Button( "Reset Search", Utilities.GL_HEIGHT_30 ) )
					ReturnToSetupPhase( restoreInitialSceneSetup );

				if( searchResult == null )
				{
					EditorGUILayout.HelpBox( "ERROR: searchResult is null", MessageType.Error );
					return;
				}
				else if( !searchResult.SearchCompletedSuccessfully )
					EditorGUILayout.HelpBox( "ERROR: search was interrupted, check the logs for more info", MessageType.Error );

				Color c = GUI.color;
				GUI.color = Color.green;
				GUILayout.Box( "Don't forget to save scene(s) if you made any changes!", Utilities.GL_EXPAND_WIDTH );
				GUI.color = c;

				GUILayout.Space( 10 );

				if( searchResult.NumberOfGroups == 0 )
					GUILayout.Box( "No results found...", Utilities.GL_EXPAND_WIDTH );
				else
				{
					GUILayout.BeginHorizontal();

					// Select all the references after filtering them (select only the GameObject's)
					if( GUILayout.Button( "Select All\n(GameObject-wise)", Utilities.GL_HEIGHT_35 ) )
					{
						GameObject[] objects = searchResult.SelectAllAsGameObjects();
						if( objects != null && objects.Length > 0 )
							Selection.objects = objects;
					}

					// Select all the references without filtering them
					if( GUILayout.Button( "Select All\n(Object-wise)", Utilities.GL_HEIGHT_35 ) )
					{
						Object[] objects = searchResult.SelectAllAsObjects();
						if( objects != null && objects.Length > 0 )
							Selection.objects = objects;
					}

					GUILayout.EndHorizontal();

					GUILayout.Space( 10 );

					searchResultDrawParameters.showTooltips = EditorGUILayout.ToggleLeft( "Show tooltips", searchResultDrawParameters.showTooltips );

					GUILayout.Space( 10 );

					GUILayout.Label( "Path drawing mode:" );

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Full: Draw the complete paths to the references (can be slow with too many references)", searchResultDrawParameters.pathDrawingMode == PathDrawingMode.Full ) )
						searchResultDrawParameters.pathDrawingMode = PathDrawingMode.Full;

					GUILayout.EndHorizontal();

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Shorter: Draw only the most relevant unique parts of the complete paths that start with a UnityEngine.Object", searchResultDrawParameters.pathDrawingMode == PathDrawingMode.ShortRelevantParts ) )
						searchResultDrawParameters.pathDrawingMode = PathDrawingMode.ShortRelevantParts;

					GUILayout.EndHorizontal();

					GUILayout.BeginHorizontal();

					GUILayout.Space( 35 );

					if( EditorGUILayout.ToggleLeft( "Shortest: Draw only the last two nodes of complete paths that are unique", searchResultDrawParameters.pathDrawingMode == PathDrawingMode.Shortest ) )
						searchResultDrawParameters.pathDrawingMode = PathDrawingMode.Shortest;

					GUILayout.EndHorizontal();

					searchResult.DrawOnGUI( searchResultDrawParameters );
				}
			}

			GUILayout.Space( 10 );

			GUILayout.EndVertical();

			EditorGUILayout.EndScrollView();
		}

		// Exposes objectsToSearch as an array field on GUI
		private bool ExposeSearchedObjects()
		{
			bool hasChanged = false;
			bool guiEnabled = GUI.enabled;

			Event ev = Event.current;

			GUILayout.BeginHorizontal();

			GUILayout.Label( "Asset(s):" );

			if( guiEnabled )
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
									objectsToSearch.Add( new ObjectToSearch( draggedObjects[i] ) );
								}
							}
						}
					}

					ev.Use();
				}

				if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
					objectsToSearch.Insert( 0, new ObjectToSearch( null ) );
			}

			GUILayout.EndHorizontal();

			for( int i = 0; i < objectsToSearch.Count; i++ )
			{
				ObjectToSearch objToSearch = objectsToSearch[i];

				GUI.changed = false;
				GUILayout.BeginHorizontal();

				Object prevAssetToSearch = objToSearch.obj;
				objToSearch.obj = EditorGUILayout.ObjectField( "", objToSearch.obj, typeof( Object ), true );

				if( GUI.changed && prevAssetToSearch != objToSearch.obj )
				{
					hasChanged = true;
					objToSearch.RefreshSubAssets();
				}

				if( guiEnabled )
				{
					if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
						objectsToSearch.Insert( i + 1, new ObjectToSearch( null ) );

					if( GUILayout.Button( "-", Utilities.GL_WIDTH_25 ) )
					{
						if( objToSearch != null && !objToSearch.Equals( null ) )
							hasChanged = true;

						objectsToSearch.RemoveAt( i-- );
					}
				}

				GUILayout.EndHorizontal();

				List<ObjectToSearch.SubAsset> subAssetsToSearch = objToSearch.subAssets;
				if( subAssetsToSearch.Count > 0 )
				{
					GUILayout.BeginHorizontal();

					// 0-> all toggles off, 1-> mixed, 2-> all toggles on
					bool toggleAllSubAssets = subAssetsToSearch[0].shouldSearch;
					bool mixedToggle = false;
					for( int j = 1; j < subAssetsToSearch.Count; j++ )
					{
						if( subAssetsToSearch[j].shouldSearch != toggleAllSubAssets )
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
						for( int j = 0; j < subAssetsToSearch.Count; j++ )
							subAssetsToSearch[j].shouldSearch = toggleAllSubAssets;
					}

					EditorGUI.showMixedValue = false;

					objToSearch.showSubAssetsFoldout = EditorGUILayout.Foldout( objToSearch.showSubAssetsFoldout, "Include sub-assets in search:", true );

					GUILayout.EndHorizontal();

					if( objToSearch.showSubAssetsFoldout )
					{
						for( int j = 0; j < subAssetsToSearch.Count; j++ )
						{
							GUILayout.BeginHorizontal();

							subAssetsToSearch[j].shouldSearch = EditorGUILayout.Toggle( subAssetsToSearch[j].shouldSearch, Utilities.GL_WIDTH_25 );

							GUI.enabled = false;
							EditorGUILayout.ObjectField( string.Empty, subAssetsToSearch[j].subAsset, typeof( Object ), true );
							GUI.enabled = guiEnabled;

							GUILayout.EndHorizontal();
						}
					}
				}
			}

			return hasChanged;
		}

		// Exposes searchInAssetsSubset as an array field on GUI
		private void ExposeAssetsToSearch()
		{
			Event ev = Event.current;

			GUILayout.BeginHorizontal();

			GUILayout.Label( "Search in following asset(s) only:" );

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
								searchInAssetsSubset.Add( draggedObjects[i] );
						}
					}
				}

				ev.Use();
			}

			if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
				searchInAssetsSubset.Insert( 0, null );

			GUILayout.EndHorizontal();

			for( int i = 0; i < searchInAssetsSubset.Count; i++ )
			{
				GUILayout.BeginHorizontal();

				searchInAssetsSubset[i] = EditorGUILayout.ObjectField( "", searchInAssetsSubset[i], typeof( Object ), false );

				if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
					searchInAssetsSubset.Insert( i + 1, null );

				if( GUILayout.Button( "-", Utilities.GL_WIDTH_25 ) )
					searchInAssetsSubset.RemoveAt( i-- );

				GUILayout.EndHorizontal();
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

		private void ReturnToSetupPhase( bool restoreInitialSceneSetup )
		{
			if( searchResult != null && restoreInitialSceneSetup && !EditorApplication.isPlaying && !searchResult.RestoreInitialSceneSetup() )
				return;

			searchResult = null;

			errorMessage = string.Empty;
			currentPhase = Phase.Setup;
		}
	}
}