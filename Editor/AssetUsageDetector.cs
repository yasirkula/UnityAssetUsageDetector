// Asset Usage Detector - by Suleyman Yasir KULA (yasirkula@yahoo.com)
// Finds references to an asset or Object
// 
// Note that static variables are not searched
// Found a bug? Let me know on Unity forums! 

using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	public enum Phase { Setup, Processing, Complete };

	// Delegate to get the value of a variable (either field or property)
	public delegate object VariableGetVal( object obj );

	#region Helper Classes
	// Custom class to hold the results for a single scene or Assets folder
	public class ReferenceHolder
	{
		// Custom struct to hold a single path to a reference
		public struct ReferencePath
		{
			public readonly ReferenceNode startNode;
			public readonly int[] pathLinksToFollow;

			public ReferencePath( ReferenceNode startNode, int[] pathIndices )
			{
				this.startNode = startNode;
				pathLinksToFollow = pathIndices;
			}
		}

		private string title;
		private bool clickable;
		private List<ReferenceNode> references;
		private List<ReferencePath> shortestPathsToReferences;

		public int NumberOfReferences { get { return references.Count; } }

		public ReferenceHolder( string title, bool clickable )
		{
			this.title = title;
			this.clickable = clickable;
			references = new List<ReferenceNode>();
			shortestPathsToReferences = null;
		}

		// Add a reference to the list
		public void AddReference( ReferenceNode node )
		{
			references.Add( node );
		}

		// Add all the Object's in this container to the set
		public void AddObjectsTo( HashSet<Object> objectsSet )
		{
			CalculateShortestPathsToReferences();

			for( int i = 0; i < shortestPathsToReferences.Count; i++ )
			{
				Object obj = shortestPathsToReferences[i].startNode.nodeObject as Object;
				if( obj != null )
					objectsSet.Add( obj );
			}
		}

		// Add all the GameObject's in this container to the set
		public void AddGameObjectsTo( HashSet<GameObject> gameObjectsSet )
		{
			CalculateShortestPathsToReferences();

			for( int i = 0; i < shortestPathsToReferences.Count; i++ )
			{
				Object obj = shortestPathsToReferences[i].startNode.nodeObject as Object;
				if( obj != null )
				{
					if( obj is GameObject )
						gameObjectsSet.Add( (GameObject) obj );
					else if( obj is Component )
						gameObjectsSet.Add( ( (Component) obj ).gameObject );
				}
			}
		}

		// Calculate shortest unique paths to the references
		public void CalculateShortestPathsToReferences()
		{
			if( shortestPathsToReferences != null )
				return;

			shortestPathsToReferences = new List<ReferencePath>( 32 );
			for( int i = 0; i < references.Count; i++ )
				references[i].CalculateShortestPaths( shortestPathsToReferences );
		}

		// Draw the results found for this container
		public void DrawOnGUI( bool drawFullPaths )
		{
			Color c = GUI.color;
			GUI.color = Color.cyan;

			if( GUILayout.Button( title, AssetUsageDetector.BoxGUIStyle, AssetUsageDetector.GL_EXPAND_WIDTH, AssetUsageDetector.GL_HEIGHT_40 ) && clickable )
			{
				// If the container (scene, usually) is clicked, highlight it on Project view
				EditorGUIUtility.PingObject( AssetDatabase.LoadAssetAtPath<SceneAsset>( title ) );
				Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>( title );
			}

			GUI.color = Color.yellow;

			if( drawFullPaths )
			{
				for( int i = 0; i < references.Count; i++ )
				{
					if( references[i].nodeObject == null )
						continue;

					GUILayout.Space( 5 );

					references[i].DrawOnGUIRecursively();
				}
			}
			else
			{
				if( shortestPathsToReferences == null )
					CalculateShortestPathsToReferences();

				for( int i = 0; i < shortestPathsToReferences.Count; i++ )
				{
					ReferencePath path = shortestPathsToReferences[i];
					if( path.startNode.nodeObject == null )
						continue;

					GUILayout.Space( 5 );

					GUILayout.BeginHorizontal();

					path.startNode.DrawOnGUI( null );

					ReferenceNode currentNode = path.startNode;
					for( int j = 0; j < path.pathLinksToFollow.Length; j++ )
					{
						ReferenceNode.Link link = currentNode[path.pathLinksToFollow[j]];
						link.targetNode.DrawOnGUI( link.description );
						currentNode = link.targetNode;
					}

					GUILayout.EndHorizontal();
				}
			}

			GUI.color = c;

			GUILayout.Space( 10 );
		}
	}

	// Custom class to hold an object in the path to a reference as a node
	public class ReferenceNode
	{
		public struct Link
		{
			public readonly ReferenceNode targetNode;
			public readonly string description;

			public Link( ReferenceNode targetNode, string description )
			{
				this.targetNode = targetNode;
				this.description = description;
			}
		}

		public object nodeObject;
		private readonly List<Link> links;

		public int NumberOfOutgoingLinks { get { return links.Count; } }
		public Link this[int index] { get { return links[index]; } }

		public ReferenceNode()
		{
			links = new List<Link>( 2 );
		}

		public ReferenceNode( object obj ) : this()
		{
			nodeObject = obj;
		}

		// Add a one-way connection to another node
		public void AddLinkTo( ReferenceNode nextNode, string description = null )
		{
			if( nextNode != null )
			{
				if( !string.IsNullOrEmpty( description ) )
					description = "[" + description + "]";

				links.Add( new Link( nextNode, description ) );
			}
		}

		// Clear this node so that it can be reused later
		public void Clear()
		{
			nodeObject = null;
			links.Clear();
		}

		// Calculate shortest unique paths that start with this node
		public void CalculateShortestPaths( List<ReferenceHolder.ReferencePath> currentPaths )
		{
			CalculateShortestPaths( currentPaths, new List<ReferenceNode>( 8 ), new List<int>( 8 ) { -1 }, 0 );
		}

		// Just some boring calculations to find the shortest unique paths recursively
		private void CalculateShortestPaths( List<ReferenceHolder.ReferencePath> shortestPaths, List<ReferenceNode> currentPath, List<int> currentPathIndices, int latestObjectIndexInPath )
		{
			if( nodeObject == null )
				return;

			int currentIndex = currentPath.Count;
			currentPath.Add( this );

			if( links.Count == 0 )
			{
				// Check if the path to the reference is unique (not discovered so far)
				bool isUnique = true;
				for( int i = 0; i < shortestPaths.Count; i++ )
				{
					if( shortestPaths[i].startNode == currentPath[latestObjectIndexInPath] && shortestPaths[i].pathLinksToFollow.Length == currentPathIndices.Count - latestObjectIndexInPath - 1 )
					{
						int j = latestObjectIndexInPath + 1;
						for( int k = 0; j < currentPathIndices.Count; j++, k++ )
						{
							if( shortestPaths[i].pathLinksToFollow[k] != currentPathIndices[j] )
								break;
						}

						if( j == currentPathIndices.Count )
						{
							isUnique = false;
							break;
						}
					}
				}

				// Don't allow duplicate shortest paths
				if( isUnique )
				{
					int[] pathIndices = new int[currentPathIndices.Count - latestObjectIndexInPath - 1];
					for( int i = latestObjectIndexInPath + 1, j = 0; i < currentPathIndices.Count; i++, j++ )
						pathIndices[j] = currentPathIndices[i];

					shortestPaths.Add( new ReferenceHolder.ReferencePath( currentPath[latestObjectIndexInPath], pathIndices ) );
				}
			}
			else
			{
				if( nodeObject is Object )
					latestObjectIndexInPath = currentIndex;

				for( int i = 0; i < links.Count; i++ )
				{
					currentPathIndices.Add( i );
					links[i].targetNode.CalculateShortestPaths( shortestPaths, currentPath, currentPathIndices, latestObjectIndexInPath );
					currentPathIndices.RemoveAt( currentIndex + 1 );
				}
			}

			currentPath.RemoveAt( currentIndex );
		}

		// Draw all the paths that start with this node on GUI recursively
		public void DrawOnGUIRecursively( string linkToPrevNodeDescription = null )
		{
			GUILayout.BeginHorizontal();

			DrawOnGUI( linkToPrevNodeDescription );

			if( links.Count > 0 )
			{
				GUILayout.BeginVertical();

				for( int i = 0; i < links.Count; i++ )
				{
					ReferenceNode next = links[i].targetNode;
					if( next.nodeObject != null )
						next.DrawOnGUIRecursively( links[i].description );
				}

				GUILayout.EndVertical();
			}

			GUILayout.EndHorizontal();
		}

		// Draw only this node on GUI
		public void DrawOnGUI( string linkToPrevNodeDescription )
		{
			string label = GetNodeContent( linkToPrevNodeDescription );
			if( GUILayout.Button( label, AssetUsageDetector.BoxGUIStyle, AssetUsageDetector.GL_EXPAND_HEIGHT ) )
			{
				// If a reference is clicked, highlight it (either on Hierarchy view or Project view)
				Object unityObject = nodeObject as Object;
				if( unityObject != null )
				{
					EditorGUIUtility.PingObject( unityObject );
					Selection.activeObject = unityObject;
				}
			}

			if( AssetUsageDetector.showTooltips && Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains( Event.current.mousePosition ) )
				AssetUsageDetector.tooltip = label;
		}

		// Get the string representation of this node
		private string GetNodeContent( string linkToPrevNodeDescription = null )
		{
			string result = string.Empty;
			if( !string.IsNullOrEmpty( linkToPrevNodeDescription ) )
				result = linkToPrevNodeDescription + "\n";

			Object unityObject = nodeObject as Object;
			if( unityObject != null )
				result += unityObject.name + " (" + unityObject.GetType() + ")";
			else
				result += nodeObject.GetType() + " object";

			return result;
		}
	}

	// Custom struct to hold a variable, its important properties and its getter function
	public struct VariableGetterHolder
	{
		public readonly string name;
		public readonly bool isProperty;
		public readonly bool isSerializable;
		private readonly VariableGetVal getter;

		public VariableGetterHolder( FieldInfo fieldInfo, VariableGetVal getter, bool isSerializable )
		{
			name = fieldInfo.Name;
			isProperty = false;
			this.isSerializable = isSerializable;
			this.getter = getter;
		}

		public VariableGetterHolder( PropertyInfo propertyInfo, VariableGetVal getter, bool isSerializable )
		{
			name = propertyInfo.Name;
			isProperty = true;
			this.isSerializable = isSerializable;
			this.getter = getter;
		}

		public object Get( object obj )
		{
			return getter( obj );
		}
	}

	// Credit: http://stackoverflow.com/questions/724143/how-do-i-create-a-delegate-for-a-net-property
	public interface IPropertyAccessor
	{
		object GetValue( object source );
	}

	// A wrapper class for properties to get their values more efficiently
	public class PropertyWrapper<TObject, TValue> : IPropertyAccessor where TObject : class
	{
		private readonly Func<TObject, TValue> getter;

		public PropertyWrapper( MethodInfo getterMethod )
		{
			getter = (Func<TObject, TValue>) Delegate.CreateDelegate( typeof( Func<TObject, TValue> ), getterMethod );
		}

		public object GetValue( object obj )
		{
			try
			{
				return getter( (TObject) obj );
			}
			catch
			{
				// Property getters may return various kinds of exceptions
				// if their backing fields are not initialized (yet)
				return null;
			}
		}
	}
	#endregion

	#region Extension Functions
	public static class AssetUsageDetectorExtensions
	{
		// Get a unique-ish string hash code for an object
		public static string Hash( this object obj )
		{
			if( obj is Object )
				return obj.GetHashCode()
					+ obj.GetType().Name +
					( (Object) obj ).name;

			return obj.GetHashCode() + obj.GetType().Name;
		}

		// Check if object depends on any of the references
		public static bool HasAnyReferenceTo( this Object obj, HashSet<Object> references )
		{
			Object[] dependencies = EditorUtility.CollectDependencies( new Object[] { obj } );
			for( int i = 0; i < dependencies.Length; i++ )
			{
				if( references.Contains( dependencies[i] ) )
					return true;
			}

			return false;
		}

		// Check if the field is serializable
		public static bool IsSerializable( this FieldInfo fieldInfo )
		{
			// see Serialization Rules: https://docs.unity3d.com/Manual/script-Serialization.html
			Type fieldType = fieldInfo.FieldType;
			if( fieldType.IsDerivedFrom( typeof( Object ) ) )
				return true;

			if( fieldType.IsArray )
			{
				if( fieldType.GetArrayRank() != 1 )
					return false;

				fieldType = fieldType.GetElementType();
			}
			else if( fieldType.IsGenericType )
			{
				if( fieldType.GetGenericTypeDefinition() != typeof( List<> ) )
					return false;

				fieldType = fieldType.GetGenericArguments()[0];
			}

			if( fieldType.IsGenericType || fieldInfo.IsInitOnly ||
			  ( ( !fieldInfo.IsPublic || fieldInfo.IsNotSerialized ) && !Attribute.IsDefined( fieldInfo, typeof( SerializeField ) ) ) )
				return false;

			if( Attribute.IsDefined( fieldType, typeof( SerializableAttribute ), false ) )
				return true;

			return false;
		}

		// Check if the type is a common Unity type (let's call them primitives)
		public static bool IsPrimitiveUnityType( this Type type )
		{
			return type.IsPrimitive || type == typeof( string ) || type == typeof( Vector3 ) || type == typeof( Vector2 ) || type == typeof( Rect ) ||
				type == typeof( Quaternion ) || type == typeof( Color ) || type == typeof( Color32 ) || type == typeof( LayerMask ) || type == typeof( GUIStyle ) ||
				type == typeof( Vector4 ) || type == typeof( Matrix4x4 ) || type == typeof( AnimationCurve ) || type == typeof( Gradient ) || type == typeof( RectOffset );
		}

		// Check if the property is serializable
		public static bool IsSerializable( this PropertyInfo propertyInfo )
		{
			// see Serialization Rules: https://docs.unity3d.com/Manual/script-Serialization.html
			Type propertyType = propertyInfo.PropertyType;
			if( propertyType.IsDerivedFrom( typeof( Object ) ) )
				return true;

			if( propertyType.IsArray )
			{
				if( propertyType.GetArrayRank() != 1 )
					return false;

				propertyType = propertyType.GetElementType();
			}
			else if( propertyType.IsGenericType )
			{
				if( propertyType.GetGenericTypeDefinition() != typeof( List<> ) )
					return false;

				propertyType = propertyType.GetGenericArguments()[0];
			}

			if( propertyType.IsGenericType )
				return false;

			return true;
		}

		// Credit: https://www.codeproject.com/Articles/14560/Fast-Dynamic-Property-Field-Accessors
		// Get <get> function for a field
		public static VariableGetVal CreateGetter( this FieldInfo fieldInfo, Type type )
		{
			// Commented the IL generator code below because it might actually be slower than simply using reflection
			//DynamicMethod dm = new DynamicMethod( "Get" + fieldInfo.Name, fieldInfo.FieldType, new Type[] { typeof( object ) }, type );
			//ILGenerator il = dm.GetILGenerator();
			//// Load the instance of the object (argument 0) onto the stack
			//il.Emit( OpCodes.Ldarg_0 );
			//// Load the value of the object's field (fi) onto the stack
			//il.Emit( OpCodes.Ldfld, fieldInfo );
			//// return the value on the top of the stack
			//il.Emit( OpCodes.Ret );

			//return (VariableGetVal) dm.CreateDelegate( typeof( VariableGetVal ) );

			return fieldInfo.GetValue;
		}

		// Get <get> function for a property
		public static VariableGetVal CreateGetter( this PropertyInfo propertyInfo )
		{
			// Ignore indexer properties
			if( propertyInfo.GetIndexParameters().Length > 0 )
				return null;

			MethodInfo mi = propertyInfo.GetGetMethod( true );
			if( mi != null )
			{
				Type GenType = typeof( PropertyWrapper<,> ).MakeGenericType( propertyInfo.DeclaringType, propertyInfo.PropertyType );
				return ( (IPropertyAccessor) Activator.CreateInstance( GenType, mi ) ).GetValue;
			}

			return null;
		}

		// Check if "child" is a subclass of "parent" (or if their types match)
		public static bool IsDerivedFrom( this Type child, Type parent )
		{
			if( child == parent || child.IsSubclassOf( parent ) )
				return true;

			return false;

		}
	}
	#endregion

	// Here we go..!
	public class AssetUsageDetector : EditorWindow
	{
		private Object assetToSearch;

		private HashSet<Object> assetsSet; // A set that contains the searched asset and its sub-assets (if any)
		private Type[] assetClasses; // Type's of the searched objects (like GameObject, Material, a custom MonoBehaviour etc.)

		private Phase currentPhase = Phase.Setup;

		private List<ReferenceHolder> searchResult = new List<ReferenceHolder>(); // Overall search results
		private ReferenceHolder currentReferenceHolder; // Results for the currently searched scene

		private Dictionary<Type, VariableGetterHolder[]> typeToVariables; // An optimization to fetch & filter fields and properties of a class only once
		private Dictionary<Type, bool> searchableTypes; // An optimization to search only certain types for references that can store the searched object(s) in their variables
		private Dictionary<string, ReferenceNode> searchedObjects; // An optimization to search an object only once (key is a hash of the searched object)

		private Stack<object> callStack; // Stack of SearchObject function parameters to avoid infinite loops (which happens when same object is passed as parameter to function)
		private Stack<Type> searchedTypesStack; // Stack of TypeCanContainReferences function parameters to avoid infinite loops

		private bool searchMaterialAssets;
		private bool searchGameObjectReferences;
		private bool searchMonoBehavioursForScript;
		private bool searchRenderers;
		private bool searchMaterialsForShader;
		private bool searchMaterialsForTexture;

		private bool searchSerializableVariablesOnly;

		private bool searchInOpenScenes = true; // Scenes currently open in Hierarchy view
		private bool searchInScenesInBuild = false; // Scenes in build
		private bool searchInScenesInBuildTickedOnly = true; // Scenes in build (ticked only or not)
		private bool searchInAllScenes = false; // All scenes (including scenes that are not in build)
		private bool searchInAssetsFolder = false; // Assets in Project view

		private bool includeSubAssetsInSearch = false; // Search sub-assets of a main asset as well
		private bool isSearchingAsset; // Whether we are searching for an asset's references or a scene object's references

		private int searchDepthLimit = 1; // Depth limit for recursively searching variables of objects
		private int currentDepth = 0;

		private bool showFullPathsToReferences = false; // Draw either the complete paths to the references or only the most relevant parts of the paths

		private bool restoreInitialSceneSetup = true; // Close the additively loaded scenes that were not part of the initial scene setup
		private SceneSetup[] initialSceneSetup; // Initial scene setup (which scenes were open and/or loaded)

		private string errorMessage = string.Empty;

		// Fetch public, protected and private non-static fields from objects by default
		// Don't fetch properties from objects by default
		private BindingFlags fieldModifiers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		private BindingFlags propertyModifiers = BindingFlags.Instance;

		private int prevSearchDepthLimit;
		private BindingFlags prevFieldModifiers;
		private BindingFlags prevPropertyModifiers;

		private static bool assembliesReloaded = true; // An optimization to reinitialize some cached variables only if assemblies are reloaded

		public static string tooltip = null;
		public static bool showTooltips = true;

		private const float PLAY_MODE_REFRESH_INTERVAL = 1f; // Interval to refresh the editor window in play mode
		private double nextPlayModeRefreshTime = 0f;

		public static GUILayoutOption GL_EXPAND_WIDTH = GUILayout.ExpandWidth( true );
		public static GUILayoutOption GL_EXPAND_HEIGHT = GUILayout.ExpandHeight( true );
		public static GUILayoutOption GL_WIDTH_100 = GUILayout.Width( 100 );
		public static GUILayoutOption GL_WIDTH_250 = GUILayout.Width( 250 );
		public static GUILayoutOption GL_HEIGHT_30 = GUILayout.Height( 30 );
		public static GUILayoutOption GL_HEIGHT_35 = GUILayout.Height( 35 );
		public static GUILayoutOption GL_HEIGHT_40 = GUILayout.Height( 40 );

		private static GUIStyle m_boxGUIStyle; // GUIStyle used to draw the results of the search
		public static GUIStyle BoxGUIStyle
		{
			get
			{
				if( m_boxGUIStyle == null )
				{
					m_boxGUIStyle = new GUIStyle( EditorStyles.helpBox )
					{
						alignment = TextAnchor.MiddleCenter,
						font = EditorStyles.label.font
					};
				}

				return m_boxGUIStyle;
			}
		}

		private GUIStyle m_tooltipGUIStyle; // GUIStyle used to draw the tooltip
		private GUIStyle TooltipGUIStyle
		{
			get
			{
				if( m_tooltipGUIStyle == null )
				{
					m_tooltipGUIStyle = new GUIStyle( EditorStyles.helpBox )
					{
						alignment = TextAnchor.MiddleCenter,
						font = EditorStyles.label.font
					};

					Texture2D backgroundTexture = new Texture2D( 1, 1 );
					backgroundTexture.SetPixel( 0, 0, new Color( 0.88f, 0.88f, 0.88f, 0.85f ) );
					backgroundTexture.Apply();

					backgroundTexture.hideFlags = HideFlags.HideAndDontSave;

					m_tooltipGUIStyle.normal.background = backgroundTexture;
					m_tooltipGUIStyle.normal.textColor = Color.black;
				}

				return m_tooltipGUIStyle;
			}
		}

		private Vector2 scrollPosition = Vector2.zero;

		private int searchCount; // Number of searched objects
		private double searchStartTime;

		private List<ReferenceNode> nodesPool = new List<ReferenceNode>( 32 );
		private List<VariableGetterHolder> validVariables = new List<VariableGetterHolder>( 32 );

		// This method is now disabled by default
		private bool experimentalMethod = false;

		// Add "Asset Usage Detector" menu item to the Window menu
		[MenuItem( "Window/Asset Usage Detector" )]
		static void Init()
		{
			// Get existing open window or if none, make a new one
			AssetUsageDetector window = GetWindow<AssetUsageDetector>();
			window.titleContent = new GUIContent( "Asset Usage Detector" );

			window.Show();
		}

		void Update()
		{
			// Refresh the window at a regular interval in play mode to update the tooltip
			if( EditorApplication.isPlaying && currentPhase == Phase.Complete && showTooltips && EditorApplication.timeSinceStartup >= nextPlayModeRefreshTime )
			{
				nextPlayModeRefreshTime = EditorApplication.timeSinceStartup + PLAY_MODE_REFRESH_INTERVAL; ;
				Repaint();
			}
		}

		void OnGUI()
		{
			// Make the window scrollable
			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition, GL_EXPAND_WIDTH, GL_EXPAND_HEIGHT );

			GUILayout.BeginVertical();

			GUILayout.Space( 10 );

			// Show the error message, if it is not empty
			if( errorMessage.Length > 0 )
				EditorGUILayout.HelpBox( errorMessage, MessageType.Error );

			GUILayout.Space( 10 );

			if( currentPhase == Phase.Processing )
			{
				// If we are stuck at this phase, then we have encountered an exception
				GUILayout.Label( ". . . something went wrong, check console . . ." );

				restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup (Recommended)", restoreInitialSceneSetup );

				if( GUILayout.Button( "RETURN", GL_HEIGHT_30 ) )
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
				assetToSearch = EditorGUILayout.ObjectField( "Asset: ", assetToSearch, typeof( Object ), true );

				if( assetToSearch != null && AssetDatabase.IsMainAsset( assetToSearch ) )
				{
					GUILayout.BeginHorizontal();

					includeSubAssetsInSearch = EditorGUILayout.ToggleLeft( "Include sub-assets in search (if any)", includeSubAssetsInSearch, GL_WIDTH_250 );
					if( !includeSubAssetsInSearch && assetToSearch is Texture && AssetDatabase.LoadAssetAtPath<Sprite>( AssetDatabase.GetAssetPath( assetToSearch ) ) != null )
						GUILayout.Label( "  <-- Recommended for sprites!", EditorStyles.boldLabel );

					GUILayout.EndHorizontal();
				}

				GUILayout.Space( 10 );

				GUILayout.Box( "SEARCH IN", GL_EXPAND_WIDTH );

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

					searchInScenesInBuildTickedOnly = EditorGUILayout.ToggleLeft( "Ticked only", searchInScenesInBuildTickedOnly, GL_WIDTH_100 );
					searchInScenesInBuildTickedOnly = !EditorGUILayout.ToggleLeft( "All", !searchInScenesInBuildTickedOnly, GL_WIDTH_100 );

					GUILayout.EndHorizontal();
				}

				if( !EditorApplication.isPlaying )
					GUI.enabled = true;

				searchInAllScenes = EditorGUILayout.ToggleLeft( "All scenes in the project", searchInAllScenes );

				GUI.enabled = true;

				GUILayout.Space( 10 );

				GUILayout.Box( "SEARCH SETTINGS", GL_EXPAND_WIDTH );

				GUILayout.BeginHorizontal();

				GUILayout.Label( new GUIContent( "> Search depth: " + searchDepthLimit, "Depth limit for recursively searching variables of objects" ), GL_WIDTH_250 );

				searchDepthLimit = (int) GUILayout.HorizontalSlider( searchDepthLimit, 0, 4 );

				GUILayout.EndHorizontal();

				GUILayout.Label( "> Search variables:" );

				GUILayout.BeginHorizontal();

				GUILayout.Space( 35 );

				if( EditorGUILayout.ToggleLeft( "Public", ( fieldModifiers & BindingFlags.Public ) == BindingFlags.Public, GL_WIDTH_100 ) )
					fieldModifiers |= BindingFlags.Public;
				else
					fieldModifiers &= ~BindingFlags.Public;

				if( EditorGUILayout.ToggleLeft( "Non-public", ( fieldModifiers & BindingFlags.NonPublic ) == BindingFlags.NonPublic, GL_WIDTH_100 ) )
					fieldModifiers |= BindingFlags.NonPublic;
				else
					fieldModifiers &= ~BindingFlags.NonPublic;

				GUILayout.EndHorizontal();

				GUILayout.Label( "> Search properties (can be slow):" );

				GUILayout.BeginHorizontal();

				GUILayout.Space( 35 );

				if( EditorGUILayout.ToggleLeft( "Public", ( propertyModifiers & BindingFlags.Public ) == BindingFlags.Public, GL_WIDTH_100 ) )
					propertyModifiers |= BindingFlags.Public;
				else
					propertyModifiers &= ~BindingFlags.Public;

				if( EditorGUILayout.ToggleLeft( "Non-public", ( propertyModifiers & BindingFlags.NonPublic ) == BindingFlags.NonPublic, GL_WIDTH_100 ) )
					propertyModifiers |= BindingFlags.NonPublic;
				else
					propertyModifiers &= ~BindingFlags.NonPublic;

				GUILayout.EndHorizontal();

				GUILayout.Space( 10 );

				// Disabled by default as it seems slower than the normal method
				//experimentalMethod = EditorGUILayout.ToggleLeft( "Experimental method (is it faster?)", experimentalMethod );

				// Don't let the user press the GO button without any valid search location
				if( !searchInAllScenes && !searchInOpenScenes && !searchInScenesInBuild && !searchInAssetsFolder )
					GUI.enabled = false;

				if( GUILayout.Button( "GO!", GL_HEIGHT_30 ) )
				{
					if( assetToSearch == null )
					{
						errorMessage = "SELECT AN ASSET FIRST!";
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

				assetToSearch = EditorGUILayout.ObjectField( "Asset(s): ", assetToSearch, typeof( Object ), true );

				GUILayout.Space( 10 );
				GUI.enabled = true;

				restoreInitialSceneSetup = EditorGUILayout.ToggleLeft( "Restore initial scene setup after search is reset (Recommended)", restoreInitialSceneSetup );

				if( GUILayout.Button( "Reset Search", GL_HEIGHT_30 ) )
				{
					if( !restoreInitialSceneSetup || RestoreInitialSceneSetup() )
					{
						errorMessage = string.Empty;
						currentPhase = Phase.Setup;
					}
				}

				Color c = GUI.color;
				GUI.color = Color.green;
				GUILayout.Box( "Don't forget to save scene(s) if you made any changes!", GL_EXPAND_WIDTH );
				GUI.color = c;

				GUILayout.Space( 10 );

				if( searchResult.Count == 0 )
				{
					GUILayout.Box( "No results found...", GL_EXPAND_WIDTH );
				}
				else
				{
					GUILayout.BeginHorizontal();

					// Select all the references after filtering them (select only the GameObject's)
					if( GUILayout.Button( "Select All\n(GameObject-wise)", GL_HEIGHT_35 ) )
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
					if( GUILayout.Button( "Select All\n(Object-wise)", GL_HEIGHT_35 ) )
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

					showFullPathsToReferences = EditorGUILayout.ToggleLeft( new GUIContent( "Show full paths to references (can be slow with too many references)", "If deselected, only the most relevant parts of the paths are drawn" ), showFullPathsToReferences );

					showTooltips = EditorGUILayout.ToggleLeft( "Show tooltips", showTooltips );

					GUILayout.Space( 10 );

					// Tooltip gets its value in ReferenceHolder.DrawOnGUI function
					tooltip = null;
					for( int i = 0; i < searchResult.Count; i++ )
						searchResult[i].DrawOnGUI( showFullPathsToReferences );

					Vector2 mousePos = Event.current.mousePosition;
					if( tooltip != null )
					{
						// Show tooltip at mouse position
						float width = tooltip.Length * 8;
						GUI.Box( new Rect( mousePos.x - width * 0.5f, mousePos.y - 40f, width, 40f ), tooltip, TooltipGUIStyle );
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
			searchCount = 0;
			searchStartTime = EditorApplication.timeSinceStartup;

			// Initialize commonly used variables
			if( searchResult == null )
				searchResult = new List<ReferenceHolder>( 16 );
			else
				searchResult.Clear();

			if( typeToVariables == null )
				typeToVariables = new Dictionary<Type, VariableGetterHolder[]>( 4096 );
			else if( assembliesReloaded || searchDepthLimit != prevSearchDepthLimit || prevFieldModifiers != fieldModifiers || prevPropertyModifiers != propertyModifiers )
				typeToVariables.Clear();

			if( searchableTypes == null )
				searchableTypes = new Dictionary<Type, bool>( 4096 );
			else
				searchableTypes.Clear();

			if( searchedObjects == null )
				searchedObjects = new Dictionary<string, ReferenceNode>( 32768 );
			else
				searchedObjects.Clear();

			if( callStack == null )
				callStack = new Stack<object>( 64 );
			else
				callStack.Clear();

			if( searchedTypesStack == null )
				searchedTypesStack = new Stack<Type>( 8 );

			if( assetsSet == null )
				assetsSet = new HashSet<Object>();
			else
				assetsSet.Clear();

			assembliesReloaded = false;
			prevSearchDepthLimit = searchDepthLimit;
			prevFieldModifiers = fieldModifiers;
			prevPropertyModifiers = propertyModifiers;

			HashSet<Type> allAssetClasses = new HashSet<Type>();

			searchMaterialAssets = false;
			searchGameObjectReferences = false;
			searchMonoBehavioursForScript = false;
			searchRenderers = false;
			searchMaterialsForShader = false;
			searchMaterialsForTexture = false;

			// Store the searched asset and its sub-assets (if any) in a set
			isSearchingAsset = !string.IsNullOrEmpty( AssetDatabase.GetAssetPath( assetToSearch ) );
			bool isMainAssetSearched = AssetDatabase.IsMainAsset( assetToSearch );
			if( isMainAssetSearched && includeSubAssetsInSearch && !( assetToSearch is SceneAsset ) )
			{
				Object[] assets = AssetDatabase.LoadAllAssetsAtPath( AssetDatabase.GetAssetPath( assetToSearch ) );
				for( int i = 0; i < assets.Length; i++ )
				{
					if( assets[i] != null )
					{
						assetsSet.Add( assets[i] );
						allAssetClasses.Add( assets[i].GetType() );

						if( assets[i] is MonoScript )
							allAssetClasses.Add( ( (MonoScript) assets[i] ).GetClass() );
					}
				}
			}
			else
			{
				assetsSet.Add( assetToSearch );
				allAssetClasses.Add( assetToSearch.GetType() );

				if( assetToSearch is MonoScript )
					allAssetClasses.Add( ( (MonoScript) assetToSearch ).GetClass() );
			}

			if( assetToSearch is GameObject )
			{
				// If searched asset is a GameObject, include its components in the search
				Component[] components;
				if( isMainAssetSearched && includeSubAssetsInSearch )
					components = ( (GameObject) assetToSearch ).GetComponentsInChildren<Component>();
				else
					components = ( (GameObject) assetToSearch ).GetComponents<Component>();

				for( int i = 0; i < components.Length; i++ )
				{
					assetsSet.Add( components[i] );
					allAssetClasses.Add( components[i].GetType() );
				}
			}

			assetClasses = new Type[allAssetClasses.Count];
			allAssetClasses.CopyTo( assetClasses );

			foreach( Object asset in assetsSet )
			{
				// Initialize the nodes of searched asset(s)
				searchedObjects.Add( asset.Hash(), new ReferenceNode( asset ) );

				if( asset is Texture )
				{
					searchMaterialAssets = true;
					searchRenderers = true;
					searchMaterialsForTexture = true;
				}
				else if( asset is Material )
				{
					searchRenderers = true;
				}
				else if( asset is MonoScript )
				{
					searchMonoBehavioursForScript = true;
				}
				else if( asset is Shader )
				{
					searchMaterialAssets = true;
					searchRenderers = true;
					searchMaterialsForShader = true;
				}
				else if( asset is GameObject )
				{
					searchGameObjectReferences = true;
				}
			}

			// Find the scenes to search for references
			HashSet<string> scenesToSearch = new HashSet<string>();
			if( searchInAllScenes )
			{
				// Get all scenes from the Assets folder
				string[] scenesTemp = AssetDatabase.FindAssets( "t:SceneAsset" );
				for( int i = 0; i < scenesTemp.Length; i++ )
					scenesToSearch.Add( AssetDatabase.GUIDToAssetPath( scenesTemp[i] ) );
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
			if( searchInAssetsFolder && isSearchingAsset )
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
				if( scenePath != null )
					SearchScene( scenePath );
			}

			// Search through all the GameObjects under the DontDestroyOnLoad scene (if exists)
			if( EditorApplication.isPlaying )
			{
				currentReferenceHolder = new ReferenceHolder( "DontDestroyOnLoad", false );

				GameObject[] rootGameObjects = GetDontDestroyOnLoadObjects().ToArray();
				for( int i = 0; i < rootGameObjects.Length; i++ )
					SearchGameObjectRecursively( rootGameObjects[i] );

				if( currentReferenceHolder.NumberOfReferences > 0 )
					searchResult.Add( currentReferenceHolder );
			}

			// Log some c00l stuff to console
			Debug.Log( "Searched " + searchCount + " objects in " + ( EditorApplication.timeSinceStartup - searchStartTime ).ToString( "F2" ) + " seconds" );

			// Search is complete!
			currentPhase = Phase.Complete;
		}

		// Search a scene for references
		private void SearchScene( string scenePath )
		{
			Scene scene = EditorSceneManager.GetSceneByPath( scenePath );

			if( !EditorApplication.isPlaying )
			{
				// Open the scene additively (to access its objects) only if it seems to contain some references to searched object(s)
				if( isSearchingAsset && !AssetDatabase.LoadAssetAtPath<SceneAsset>( scenePath ).HasAnyReferenceTo( assetsSet ) )
					return;

				scene = EditorSceneManager.OpenScene( scenePath, OpenSceneMode.Additive );
			}

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
			if( assetsSet.Contains( obj ) )
			{
				// Rare case: if searched object is a scene GameObject, search its components for references 
				// instead of completely ignoring the GameObject
				if( obj is GameObject )
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
				{
					if( cachedResult != null )
						return cachedResult;

					return null;
				}
			}

			searchCount++;

			ReferenceNode result;
			Object unityObject = obj as Object;
			if( unityObject != null )
			{
				// If we hit a searched asset
				if( assetsSet.Contains( unityObject ) )
				{
					result = new ReferenceNode( unityObject );
					searchedObjects.Add( objHash, result );

					return result;
				}

				// Search the Object in detail only if EditorUtility.CollectDependencies returns a reference
				if( isSearchingAsset && !unityObject.HasAnyReferenceTo( assetsSet ) )
				{
					searchedObjects.Add( objHash, null );
					return null;
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
			// and if the object is a UnityEngine.Object (if not cache the result only if we have actually found something
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
			if( searchGameObjectReferences )
			{
				Object prefab = PrefabUtility.GetPrefabParent( go );
				if( assetsSet.Contains( prefab ) && go == PrefabUtility.FindRootGameObjectWithSameParentPrefab( go ) )
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
				if( assetsSet.Contains( script ) )
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

			if( searchMaterialsForShader && assetsSet.Contains( material.shader ) )
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
						if( assetsSet.Contains( assignedTexture ) )
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
			if( fieldModifiers != BindingFlags.Instance )
			{
				FieldInfo[] fields = type.GetFields( fieldModifiers );
				for( int i = 0; i < fields.Length; i++ )
				{
					// Skip obsolete fields
					if( Attribute.IsDefined( fields[i], typeof( ObsoleteAttribute ) ) )
						continue;

					// Skip primitive types
					Type fieldType = fields[i].FieldType;
					if( fieldType.IsPrimitive || fieldType == typeof( string ) || fieldType.IsEnum )
						continue;

					if( experimentalMethod )
					{
						if( !IsTypeSearchable( fieldType ) )
							continue;
					}
					else if( fieldType.IsPrimitiveUnityType() )
						continue;

					VariableGetVal getter = fields[i].CreateGetter( type );
					if( getter != null )
						validVariables.Add( new VariableGetterHolder( fields[i], getter, fields[i].IsSerializable() ) );
				}
			}

			if( propertyModifiers != BindingFlags.Instance )
			{
				PropertyInfo[] properties = type.GetProperties( propertyModifiers );
				for( int i = 0; i < properties.Length; i++ )
				{
					// Skip obsolete properties
					if( Attribute.IsDefined( properties[i], typeof( ObsoleteAttribute ) ) )
						continue;

					// Skip primitive types
					Type propertyType = properties[i].PropertyType;
					if( propertyType.IsPrimitive || propertyType == typeof( string ) || propertyType.IsEnum )
						continue;

					if( experimentalMethod )
					{
						if( !IsTypeSearchable( propertyType ) )
							continue;
					}
					else if( propertyType.IsPrimitiveUnityType() )
						continue;

					// Additional filtering for properties:
					// 1- Ignore "gameObject", "transform", "rectTransform" and "attachedRigidbody" properties of Component's to get more useful results
					// 2- Ignore "canvasRenderer" and "canvas" properties of Graphic components
					// 3 & 4- Prevent accessing properties of Unity that instantiate an existing resource (causing leak)
					string propertyName = properties[i].Name;
					if( type.IsDerivedFrom( typeof( Component ) ) && ( propertyName.Equals( "gameObject" ) ||
						propertyName.Equals( "transform" ) || propertyName.Equals( "attachedRigidbody" ) || propertyName.Equals( "rectTransform" ) ) )
						continue;
					else if( type.IsDerivedFrom( typeof( UnityEngine.UI.Graphic ) ) &&
						( propertyName.Equals( "canvasRenderer" ) || propertyName.Equals( "canvas" ) ) )
						continue;
					else if( propertyName.Equals( "mesh" ) && type.IsDerivedFrom( typeof( MeshFilter ) ) )
						continue;
					else if( ( propertyName.Equals( "material" ) || propertyName.Equals( "materials" ) ) &&
						( type.IsDerivedFrom( typeof( Renderer ) ) || type.IsDerivedFrom( typeof( Collider ) ) ||
						type.IsDerivedFrom( typeof( Collider2D ) ) || type.IsDerivedFrom( typeof( GUIText ) ) ) )
						continue;
					else
					{
						VariableGetVal getter = properties[i].CreateGetter();
						if( getter != null )
							validVariables.Add( new VariableGetterHolder( properties[i], getter, properties[i].IsSerializable() ) );
					}
				}
			}

			result = validVariables.ToArray();

			// Cache the filtered fields
			typeToVariables.Add( type, result );

			return result;
		}

		// Check if this type can possibly contain references to the searched asset(s)
		private bool IsTypeSearchable( Type type, int depth = 0 )
		{
			if( type.IsPrimitive || type == typeof( string ) )
				return false;

			if( searchedTypesStack.Contains( type ) )
				return false;

			bool result;
			if( searchableTypes.TryGetValue( type, out result ) )
				return result;

			if( type == typeof( GameObject ) || type == typeof( AnimationClip ) || type == typeof( Animation ) ||
				type == typeof( Animator ) || type.IsDerivedFrom( typeof( RuntimeAnimatorController ) ) ||
				( ( searchMaterialsForShader || searchMaterialsForTexture ) && type.IsDerivedFrom( typeof( Material ) ) ) ||
				( searchRenderers && type.IsDerivedFrom( typeof( Renderer ) ) ) )
			{
				searchableTypes.Add( type, true );
				return true;
			}

			for( int i = 0; i < assetClasses.Length; i++ )
			{
				if( assetClasses[i].IsDerivedFrom( type ) )
				{
					searchableTypes.Add( type, true );
					return true;
				}
			}

			if( type.IsArray )
			{
				if( IsTypeSearchable( type.GetElementType(), depth ) )
				{
					searchableTypes.Add( type, true );
					return true;
				}
			}

			if( type.IsGenericType )
			{
				Type[] generics = type.GetGenericArguments();
				for( int i = 0; i < generics.Length; i++ )
				{
					if( IsTypeSearchable( generics[i], depth ) )
					{
						searchableTypes.Add( type, true );
						return true;
					}
				}
			}

			if( depth < searchDepthLimit )
			{
				try
				{
					searchedTypesStack.Push( type );

					HashSet<Type> searchedVariableTypes = new HashSet<Type>();
					FieldInfo[] fields = type.GetFields( fieldModifiers );
					for( int i = 0; i < fields.Length; i++ )
					{
						Type fieldType = fields[i].FieldType;
						if( searchedVariableTypes.Contains( fieldType ) )
							continue;

						searchedVariableTypes.Add( fieldType );
						if( IsTypeSearchable( fieldType, depth + 1 ) )
						{
							searchableTypes.Add( type, true );
							return true;
						}
					}

					PropertyInfo[] properties = type.GetProperties( propertyModifiers );
					for( int i = 0; i < properties.Length; i++ )
					{
						Type propertyType = properties[i].PropertyType;
						if( searchedVariableTypes.Contains( propertyType ) )
							continue;

						if( properties[i].GetIndexParameters().Length == 0 && properties[i].GetGetMethod( ( propertyModifiers & BindingFlags.NonPublic ) == BindingFlags.NonPublic ) != null )
						{
							searchedVariableTypes.Add( propertyType );
							if( IsTypeSearchable( properties[i].PropertyType, depth + 1 ) )
							{
								searchableTypes.Add( type, true );
								return true;
							}
						}
					}
				}
				finally
				{
					searchedTypesStack.Pop();
				}
			}

			if( depth == 0 )
				searchableTypes.Add( type, false );

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
			if( nodesPool.Count == 0 )
			{
				for( int i = 0; i < 32; i++ )
					nodesPool.Add( new ReferenceNode() );
			}

			int index = nodesPool.Count - 1;
			ReferenceNode node = nodesPool[index];
			node.nodeObject = nodeObject;
			nodesPool.RemoveAt( index );

			return node;
		}

		// Pool a reference node
		private void PoolReferenceNode( ReferenceNode node )
		{
			node.Clear();
			nodesPool.Add( node );
		}

		// Retrieve the game objects listed under the DontDestroyOnLoad scene
		private List<GameObject> GetDontDestroyOnLoadObjects()
		{
			List<GameObject> result = new List<GameObject>();

			List<GameObject> rootGameObjectsExceptDontDestroyOnLoad = new List<GameObject>();
			for( int i = 0; i < SceneManager.sceneCount; i++ )
			{
				rootGameObjectsExceptDontDestroyOnLoad.AddRange( SceneManager.GetSceneAt( i ).GetRootGameObjects() );
			}

			List<GameObject> rootGameObjects = new List<GameObject>();
			Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
			for( int i = 0; i < allTransforms.Length; i++ )
			{
				Transform root = allTransforms[i].root;
				if( root.hideFlags == HideFlags.None && !rootGameObjects.Contains( root.gameObject ) )
				{
					rootGameObjects.Add( root.gameObject );
				}
			}

			for( int i = 0; i < rootGameObjects.Count; i++ )
			{
				if( !rootGameObjectsExceptDontDestroyOnLoad.Contains( rootGameObjects[i] ) )
					result.Add( rootGameObjects[i] );
			}

			return result;
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
	}
}