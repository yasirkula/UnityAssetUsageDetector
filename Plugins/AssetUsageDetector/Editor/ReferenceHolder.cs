using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	// Custom class to hold the results for a single scene or Assets folder
	[Serializable]
	public class ReferenceHolder : ISerializationCallbackReceiver
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

		// Credit: https://docs.unity3d.com/Manual/script-Serialization-Custom.html
		[Serializable]
		public class SerializableNode
		{
			public int instanceId;
			public bool isUnityObject;
			public string description;

			public List<int> links;
			public List<string> linkDescriptions;
		}

		private string title;
		private bool clickable;

		private List<ReferenceNode> references;
		private List<ReferencePath> referencePathsShortUnique;
		private List<ReferencePath> referencePathsShortest;

		private List<SerializableNode> serializedNodes;
		private List<int> initialSerializedNodes;

		public int NumberOfReferences { get { return references.Count; } }

		public ReferenceHolder( string title, bool clickable )
		{
			this.title = title;
			this.clickable = clickable;
			references = new List<ReferenceNode>();
			referencePathsShortUnique = null;
			referencePathsShortest = null;
		}

		// Add a reference to the list
		public void AddReference( ReferenceNode node )
		{
			references.Add( node );
		}

		// Initializes commonly used variables of the nodes
		public void InitializeNodes()
		{
			for( int i = 0; i < references.Count; i++ )
				references[i].InitializeRecursively();
		}

		// Check if node exists in this results set
		public bool Contains( ReferenceNode node )
		{
			for( int i = 0; i < references.Count; i++ )
			{
				if( references[i] == node )
					return true;
			}

			return false;
		}

		// Add all the Object's in this container to the set
		public void AddObjectsTo( HashSet<Object> objectsSet )
		{
			CalculateShortestPathsToReferences();

			for( int i = 0; i < referencePathsShortUnique.Count; i++ )
			{
				Object obj = referencePathsShortUnique[i].startNode.UnityObject;
				if( obj != null && !obj.Equals( null ) )
					objectsSet.Add( obj );
			}
		}

		// Add all the GameObject's in this container to the set
		public void AddGameObjectsTo( HashSet<GameObject> gameObjectsSet )
		{
			CalculateShortestPathsToReferences();

			for( int i = 0; i < referencePathsShortUnique.Count; i++ )
			{
				Object obj = referencePathsShortUnique[i].startNode.UnityObject;
				if( obj != null && !obj.Equals( null ) )
				{
					if( obj is GameObject )
						gameObjectsSet.Add( (GameObject) obj );
					else if( obj is Component )
						gameObjectsSet.Add( ( (Component) obj ).gameObject );
				}
			}
		}

		// Calculate short unique paths to the references
		public void CalculateShortestPathsToReferences()
		{
			if( referencePathsShortUnique != null )
				return;

			referencePathsShortUnique = new List<ReferencePath>( 32 );
			for( int i = 0; i < references.Count; i++ )
				references[i].CalculateShortUniquePaths( referencePathsShortUnique );

			referencePathsShortest = new List<ReferencePath>( referencePathsShortUnique.Count );
			for( int i = 0; i < referencePathsShortUnique.Count; i++ )
			{
				int[] linksToFollow = referencePathsShortUnique[i].pathLinksToFollow;

				// Find the last two nodes in this path
				ReferenceNode nodeBeforeLast = referencePathsShortUnique[i].startNode;
				for( int j = 0; j < linksToFollow.Length - 1; j++ )
					nodeBeforeLast = nodeBeforeLast[linksToFollow[j]].targetNode;

				// Check if these two nodes are unique
				bool isUnique = true;
				for( int j = 0; j < referencePathsShortest.Count; j++ )
				{
					ReferencePath path = referencePathsShortest[j];
					if( path.startNode == nodeBeforeLast && path.pathLinksToFollow[0] == linksToFollow[linksToFollow.Length - 1] )
					{
						isUnique = false;
						break;
					}
				}

				if( isUnique )
					referencePathsShortest.Add( new ReferencePath( nodeBeforeLast, new int[1] { linksToFollow[linksToFollow.Length - 1] } ) );
			}
		}

		// Draw the results found for this container
		public void DrawOnGUI( PathDrawingMode pathDrawingMode )
		{
			Color c = GUI.color;
			GUI.color = Color.cyan;

			if( GUILayout.Button( title, Utilities.BoxGUIStyle, Utilities.GL_EXPAND_WIDTH, Utilities.GL_HEIGHT_40 ) && clickable )
			{
				// If the container (scene, usually) is clicked, highlight it on Project view
				AssetDatabase.LoadAssetAtPath<SceneAsset>( title ).SelectInEditor();
			}

			GUI.color = Color.yellow;

			if( pathDrawingMode == PathDrawingMode.Full )
			{
				for( int i = 0; i < references.Count; i++ )
				{
					GUILayout.Space( 5 );

					references[i].DrawOnGUIRecursively();
				}
			}
			else
			{
				if( referencePathsShortUnique == null )
					CalculateShortestPathsToReferences();

				List<ReferencePath> pathsToDraw;
				if( pathDrawingMode == PathDrawingMode.ShortRelevantParts )
					pathsToDraw = referencePathsShortUnique;
				else
					pathsToDraw = referencePathsShortest;

				for( int i = 0; i < pathsToDraw.Count; i++ )
				{
					GUILayout.Space( 5 );

					GUILayout.BeginHorizontal();

					ReferencePath path = pathsToDraw[i];
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

		// Assembly reloading; serialize nodes in a way that Unity can serialize
		public void OnBeforeSerialize()
		{
			if( references == null )
				return;

			if( serializedNodes == null )
				serializedNodes = new List<SerializableNode>( references.Count * 5 );
			else
				serializedNodes.Clear();

			if( initialSerializedNodes == null )
				initialSerializedNodes = new List<int>( references.Count );
			else
				initialSerializedNodes.Clear();

			Dictionary<ReferenceNode, int> nodeToIndex = new Dictionary<ReferenceNode, int>( references.Count * 5 );
			for( int i = 0; i < references.Count; i++ )
				initialSerializedNodes.Add( references[i].SerializeRecursively( nodeToIndex, serializedNodes ) );
		}

		// Assembly reloaded; deserialize nodes to construct the original graph
		public void OnAfterDeserialize()
		{
			if( initialSerializedNodes == null || serializedNodes == null )
				return;

			if( references == null )
				references = new List<ReferenceNode>( initialSerializedNodes.Count );
			else
				references.Clear();

			List<ReferenceNode> allNodes = new List<ReferenceNode>( serializedNodes.Count );
			for( int i = 0; i < serializedNodes.Count; i++ )
				allNodes.Add( new ReferenceNode() );

			for( int i = 0; i < serializedNodes.Count; i++ )
				allNodes[i].Deserialize( serializedNodes[i], allNodes );

			for( int i = 0; i < initialSerializedNodes.Count; i++ )
				references.Add( allNodes[initialSerializedNodes[i]] );

			referencePathsShortUnique = null;
			referencePathsShortest = null;

			serializedNodes.Clear();
			initialSerializedNodes.Clear();
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

		// Unique identifier is used while serializing the node
		private static int uid_last = 0;
		private readonly int uid;

		public object nodeObject;
		private readonly List<Link> links;

		private int? instanceId; // instanceId of the nodeObject if it is a Unity object, null otherwise
		private string description; // String to print on this node

		public Object UnityObject { get { return instanceId.HasValue ? EditorUtility.InstanceIDToObject( instanceId.Value ) : null; } }

		public int NumberOfOutgoingLinks { get { return links.Count; } }
		public Link this[int index] { get { return links[index]; } }

		public ReferenceNode()
		{
			links = new List<Link>( 2 );
			uid = uid_last++;
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

		// Initialize node's commonly used variables
		public void InitializeRecursively()
		{
			if( description != null ) // Already initialized
				return;

			Object unityObject = nodeObject as Object;
			if( unityObject != null )
			{
				instanceId = unityObject.GetInstanceID();
				description = unityObject.name + " (" + unityObject.GetType() + ")";
			}
			else if( nodeObject != null )
			{
				instanceId = null;
				description = nodeObject.GetType() + " object";
			}
			else
			{
				instanceId = null;
				description = "<<destroyed>>";
			}

			nodeObject = null; // don't hold Object reference, allow Unity to GC used memory

			for( int i = 0; i < links.Count; i++ )
				links[i].targetNode.InitializeRecursively();
		}

		// Clear this node so that it can be reused later
		public void Clear()
		{
			nodeObject = null;
			links.Clear();
		}

		// Calculate short unique paths that start with this node
		public void CalculateShortUniquePaths( List<ReferenceHolder.ReferencePath> currentPaths )
		{
			CalculateShortUniquePaths( currentPaths, new List<ReferenceNode>( 8 ), new List<int>( 8 ) { -1 }, 0 );
		}

		// Just some boring calculations to find the short unique paths recursively
		private void CalculateShortUniquePaths( List<ReferenceHolder.ReferencePath> shortestPaths, List<ReferenceNode> currentPath, List<int> currentPathIndices, int latestObjectIndexInPath )
		{
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

				// Don't allow duplicate short paths
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
				if( instanceId.HasValue ) // nodeObject is Unity object
					latestObjectIndexInPath = currentIndex;

				for( int i = 0; i < links.Count; i++ )
				{
					currentPathIndices.Add( i );
					links[i].targetNode.CalculateShortUniquePaths( shortestPaths, currentPath, currentPathIndices, latestObjectIndexInPath );
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
			if( GUILayout.Button( label, Utilities.BoxGUIStyle, Utilities.GL_EXPAND_HEIGHT ) )
			{
				// If a reference is clicked, highlight it (either on Hierarchy view or Project view)
				UnityObject.SelectInEditor();
			}

			if( AssetUsageDetector.showTooltips && Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains( Event.current.mousePosition ) )
				AssetUsageDetector.tooltip = label;
		}

		// Get the string representation of this node
		private string GetNodeContent( string linkToPrevNodeDescription = null )
		{
			if( !string.IsNullOrEmpty( linkToPrevNodeDescription ) )
				return linkToPrevNodeDescription + "\n" + description;

			return description;
		}

		// Serialize this node and its connected nodes recursively
		public int SerializeRecursively( Dictionary<ReferenceNode, int> nodeToIndex, List<ReferenceHolder.SerializableNode> serializedNodes )
		{
			int index;
			if( nodeToIndex.TryGetValue( this, out index ) )
				return index;

			ReferenceHolder.SerializableNode serializedNode = new ReferenceHolder.SerializableNode()
			{
				instanceId = instanceId ?? 0,
				isUnityObject = instanceId.HasValue,
				description = description
			};

			index = serializedNodes.Count;
			nodeToIndex[this] = index;
			serializedNodes.Add( serializedNode );

			if( links.Count > 0 )
			{
				serializedNode.links = new List<int>( links.Count );
				serializedNode.linkDescriptions = new List<string>( links.Count );

				for( int i = 0; i < links.Count; i++ )
				{
					serializedNode.links.Add( links[i].targetNode.SerializeRecursively( nodeToIndex, serializedNodes ) );
					serializedNode.linkDescriptions.Add( links[i].description );
				}
			}

			return index;
		}

		// Deserialize this node and its links from the serialized data
		public void Deserialize( ReferenceHolder.SerializableNode serializedNode, List<ReferenceNode> allNodes )
		{
			if( serializedNode.isUnityObject )
				instanceId = serializedNode.instanceId;
			else
				instanceId = null;

			description = serializedNode.description;

			if( serializedNode.links != null )
			{
				for( int i = 0; i < serializedNode.links.Count; i++ )
					links.Add( new Link( allNodes[serializedNode.links[i]], serializedNode.linkDescriptions[i] ) );
			}
		}

		public override int GetHashCode()
		{
			return uid;
		}
	}
}