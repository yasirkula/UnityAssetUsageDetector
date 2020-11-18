using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_2017_1_OR_NEWER
using UnityEngine.U2D;
using UnityEngine.Playables;
#endif
#if UNITY_2018_2_OR_NEWER
using UnityEditor.U2D;
#endif
#if UNITY_2017_3_OR_NEWER
using UnityEditor.Compilation;
#endif
#if UNITY_2017_2_OR_NEWER
using UnityEngine.Tilemaps;
#endif
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	public partial class AssetUsageDetector
	{
		#region Helper Classes
#if UNITY_2017_3_OR_NEWER
#pragma warning disable 0649 // The fields' values are assigned via JsonUtility
		[Serializable]
		private struct AssemblyDefinitionReferences
		{
			public string reference; // Used by AssemblyDefinitionReferenceAssets
			public List<string> references; // Used by AssemblyDefinitionAssets
		}
#pragma warning restore 0649
#endif

#if UNITY_2018_1_OR_NEWER
#pragma warning disable 0649 // The fields' values are assigned via JsonUtility
		[Serializable]
		private struct ShaderGraphReferences // Used by old Shader Graph serialization format
		{
			[Serializable]
			public struct JSONHolder
			{
				public string JSONnodeData;
			}

			[Serializable]
			public class TextureHolder
			{
				public string m_SerializedTexture;
				public string m_SerializedCubemap;
				public string m_Guid;

				public string GetTexturePath()
				{
					string guid = ExtractGUIDFromString( !string.IsNullOrEmpty( m_SerializedTexture ) ? m_SerializedTexture : m_SerializedCubemap );
					if( string.IsNullOrEmpty( guid ) )
						guid = m_Guid;

					return string.IsNullOrEmpty( guid ) ? null : AssetDatabase.GUIDToAssetPath( guid );
				}
			}

			[Serializable]
			public struct PropertyData
			{
				public string m_Name;
				public string m_DefaultReferenceName;
				public string m_OverrideReferenceName;
				public TextureHolder m_Value;

				public string GetName()
				{
					if( !string.IsNullOrEmpty( m_OverrideReferenceName ) )
						return m_OverrideReferenceName;
					if( !string.IsNullOrEmpty( m_DefaultReferenceName ) )
						return m_DefaultReferenceName;
					if( !string.IsNullOrEmpty( m_Name ) )
						return m_Name;

					return "Property";
				}
			}

			[Serializable]
			public struct NodeData
			{
				public string m_Name;
				public string m_FunctionSource; // Custom Function node's Source field
				public string m_SerializedSubGraph; // Sub-graph node
				public List<JSONHolder> m_SerializableSlots;

				public string GetSubGraphPath()
				{
					string guid = ExtractGUIDFromString( m_SerializedSubGraph );
					return string.IsNullOrEmpty( guid ) ? null : AssetDatabase.GUIDToAssetPath( guid );
				}
			}

			[Serializable]
			public struct NodeSlotData
			{
				public TextureHolder m_Texture;
				public TextureHolder m_TextureArray;
				public TextureHolder m_Cubemap;

				public string GetTexturePath()
				{
					if( m_Texture != null )
						return m_Texture.GetTexturePath();
					if( m_Cubemap != null )
						return m_Cubemap.GetTexturePath();
					if( m_TextureArray != null )
						return m_TextureArray.GetTexturePath();

					return null;
				}
			}

			public List<JSONHolder> m_SerializedProperties;
			public List<JSONHolder> m_SerializableNodes;

			// String can be in one of the following formats:
			// "guid":"GUID_VALUE"
			// "guid": "GUID_VALUE"
			// "guid" : "GUID_VALUE"
			private static string ExtractGUIDFromString( string str )
			{
				if( !string.IsNullOrEmpty( str ) )
				{
					int guidStartIndex = str.IndexOf( "\"guid\"" );
					if( guidStartIndex >= 0 )
					{
						guidStartIndex += 6;
						guidStartIndex = str.IndexOf( '"', guidStartIndex );
						if( guidStartIndex > 0 )
						{
							guidStartIndex++;

							int guidEndIndex = str.IndexOf( '"', guidStartIndex );
							if( guidEndIndex > 0 )
								return str.Substring( guidStartIndex, guidEndIndex - guidStartIndex );
						}
					}
				}

				return null;
			}
		}
#pragma warning restore 0649
#endif
		#endregion

		// Dictionary to quickly find the function to search a specific type with
		private Dictionary<Type, Func<Object, ReferenceNode>> typeToSearchFunction;
		// Dictionary to associate special file extensions with their search functions
		private Dictionary<string, Func<Object, ReferenceNode>> extensionToSearchFunction;

		// An optimization to fetch & filter fields and properties of a class only once
		private readonly Dictionary<Type, VariableGetterHolder[]> typeToVariables = new Dictionary<Type, VariableGetterHolder[]>( 4096 );
		private readonly List<VariableGetterHolder> validVariables = new List<VariableGetterHolder>( 32 );

		// Path(s) of .cginc, .cg, .hlsl and .glslinc assets in assetsToSearchSet
		private readonly HashSet<string> shaderIncludesToSearchSet = new HashSet<string>();

#if UNITY_2017_3_OR_NEWER
		// Path(s) of the Assembly Definition Files in objectsToSearchSet (Value: files themselves)
		private readonly Dictionary<string, Object> assemblyDefinitionFilesToSearch = new Dictionary<string, Object>( 8 );
#endif

		// An optimization to fetch an animation clip's curve bindings only once
		private readonly Dictionary<AnimationClip, EditorCurveBinding[]> animationClipUniqueBindings = new Dictionary<AnimationClip, EditorCurveBinding[]>( 256 );

		private bool searchPrefabConnections;
		private bool searchMonoBehavioursForScript;
		private bool searchRenderers;
		private bool searchMaterialsForShader;
		private bool searchTextureReferences;
#if UNITY_2018_1_OR_NEWER
		private bool searchShaderGraphsForSubGraphs;
#endif

		private bool searchSerializableVariablesOnly;
		private bool prevSearchSerializableVariablesOnly;

		private BindingFlags fieldModifiers, propertyModifiers;
		private BindingFlags prevFieldModifiers, prevPropertyModifiers;

		// Unity's internal function that returns a SerializedProperty's corresponding FieldInfo
		private delegate FieldInfo FieldInfoGetter( SerializedProperty p, out Type t );
		private FieldInfoGetter fieldInfoGetter;

		private void InitializeSearchFunctionsData( Parameters searchParameters )
		{
			if( typeToSearchFunction == null )
			{
				typeToSearchFunction = new Dictionary<Type, Func<Object, ReferenceNode>>()
				{
					{ typeof( GameObject ), SearchGameObject },
					{ typeof( Material ), SearchMaterial },
					{ typeof( Shader ), SearchShader },
					{ typeof( MonoScript ), SearchMonoScript },
					{ typeof( RuntimeAnimatorController ), SearchAnimatorController },
					{ typeof( AnimatorOverrideController ), SearchAnimatorController },
					{ typeof( AnimatorController ), SearchAnimatorController },
					{ typeof( AnimatorStateMachine ), SearchAnimatorStateMachine },
					{ typeof( AnimatorState ), SearchAnimatorState },
					{ typeof( AnimatorStateTransition ), SearchAnimatorStateTransition },
					{ typeof( BlendTree ), SearchBlendTree },
					{ typeof( AnimationClip ), SearchAnimationClip },
#if UNITY_2017_1_OR_NEWER
					{ typeof( SpriteAtlas ), SearchSpriteAtlas },
#endif
				};
			}

			if( extensionToSearchFunction == null )
			{
				extensionToSearchFunction = new Dictionary<string, Func<Object, ReferenceNode>>()
				{
					{ "compute", SearchShaderSecondaryAsset },
					{ "cginc", SearchShaderSecondaryAsset },
					{ "cg", SearchShaderSecondaryAsset },
					{ "glslinc", SearchShaderSecondaryAsset },
					{ "hlsl", SearchShaderSecondaryAsset },
#if UNITY_2017_3_OR_NEWER
					{ "asmdef", SearchAssemblyDefinitionFile },
#endif
#if UNITY_2019_2_OR_NEWER
					{ "asmref", SearchAssemblyDefinitionFile },
#endif
#if UNITY_2018_1_OR_NEWER
					{ "shadergraph", SearchShaderGraph },
					{ "shadersubgraph", SearchShaderGraph },
#endif
				};
			}

			fieldModifiers = searchParameters.fieldModifiers | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			propertyModifiers = searchParameters.propertyModifiers | BindingFlags.Instance | BindingFlags.DeclaredOnly;
			searchSerializableVariablesOnly = !searchParameters.searchNonSerializableVariables;

			if( prevFieldModifiers != fieldModifiers || prevPropertyModifiers != propertyModifiers || prevSearchSerializableVariablesOnly != searchSerializableVariablesOnly )
				typeToVariables.Clear();

			prevFieldModifiers = fieldModifiers;
			prevPropertyModifiers = propertyModifiers;
			prevSearchSerializableVariablesOnly = searchSerializableVariablesOnly;

			searchPrefabConnections = false;
			searchMonoBehavioursForScript = false;
			searchRenderers = false;
			searchMaterialsForShader = false;
			searchTextureReferences = false;
#if UNITY_2018_1_OR_NEWER
			searchShaderGraphsForSubGraphs = false;
#endif

			foreach( Object obj in objectsToSearchSet )
			{
				if( obj is Texture )
				{
					searchRenderers = true;
					searchTextureReferences = true;
				}
				else if( obj is Material )
					searchRenderers = true;
				else if( obj is MonoScript )
					searchMonoBehavioursForScript = true;
				else if( obj is Shader )
				{
					searchRenderers = true;
					searchMaterialsForShader = true;
				}
				else if( obj is GameObject )
					searchPrefabConnections = true;
#if UNITY_2017_3_OR_NEWER
				else if( obj is UnityEditorInternal.AssemblyDefinitionAsset )
					assemblyDefinitionFilesToSearch[AssetDatabase.GetAssetPath( obj )] = obj;
#endif
			}

			foreach( string path in assetsToSearchPathsSet )
			{
				string extension = Utilities.GetFileExtension( path );
				if( extension == "hlsl" || extension == "cginc" || extension == "cg" || extension == "glslinc" )
					shaderIncludesToSearchSet.Add( path );
#if UNITY_2018_1_OR_NEWER
				else if( extension == "shadersubgraph" )
					searchShaderGraphsForSubGraphs = true;
#endif
			}

			// AssetDatabase.GetDependencies doesn't take #include lines in shader source codes into consideration. If we are searching for references
			// of a potential #include target (shaderIncludesToSearchSet), we must search all shader assets and check their #include lines manually
			if( shaderIncludesToSearchSet.Count > 0 )
			{
				alwaysSearchedExtensionsSet.Add( "shader" );
				alwaysSearchedExtensionsSet.Add( "compute" );
				alwaysSearchedExtensionsSet.Add( "cginc" );
				alwaysSearchedExtensionsSet.Add( "cg" );
				alwaysSearchedExtensionsSet.Add( "glslinc" );
				alwaysSearchedExtensionsSet.Add( "hlsl" );
			}

#if UNITY_2017_3_OR_NEWER
			// AssetDatabase.GetDependencies doesn't return references from Assembly Definition Files to their Assembly Definition References,
			// so if we are searching for an Assembly Definition File's usages, we must search all Assembly Definition Files' references manually.
			if( assemblyDefinitionFilesToSearch.Count > 0 )
			{
				alwaysSearchedExtensionsSet.Add( "asmdef" );
#if UNITY_2019_2_OR_NEWER
				alwaysSearchedExtensionsSet.Add( "asmref" );
#endif
			}
#endif

#if UNITY_2018_1_OR_NEWER
			// AssetDatabase.GetDependencies doesn't work with Shader Graph assets. We must search all Shader Graph assets in the following cases:
			// searchTextureReferences: to find Texture references used in various nodes and properties
			// searchShaderGraphsForSubGraphs: to find Shader Sub-graph references in other Shader Graph assets
			// shaderIncludesToSearchSet: to find .cginc, .cg, .glslinc and .hlsl references used in Custom Function nodes
			if( searchTextureReferences || searchShaderGraphsForSubGraphs || shaderIncludesToSearchSet.Count > 0 )
			{
				alwaysSearchedExtensionsSet.Add( "shadergraph" );
				alwaysSearchedExtensionsSet.Add( "shadersubgraph" );
			}
#endif

#if UNITY_2019_3_OR_NEWER
			MethodInfo fieldInfoGetterMethod = typeof( Editor ).Assembly.GetType( "UnityEditor.ScriptAttributeUtility" ).GetMethod( "GetFieldInfoAndStaticTypeFromProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
#else
			MethodInfo fieldInfoGetterMethod = typeof( Editor ).Assembly.GetType( "UnityEditor.ScriptAttributeUtility" ).GetMethod( "GetFieldInfoFromProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
#endif
			fieldInfoGetter = (FieldInfoGetter) Delegate.CreateDelegate( typeof( FieldInfoGetter ), fieldInfoGetterMethod );
		}

		private ReferenceNode SearchGameObject( Object unityObject )
		{
			GameObject go = (GameObject) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( go );

			// Check if this GameObject's prefab is one of the selected assets
			if( searchPrefabConnections )
			{
#if UNITY_2018_3_OR_NEWER
				Object prefab = PrefabUtility.GetCorrespondingObjectFromSource( go );
#else
				Object prefab = PrefabUtility.GetPrefabParent( go );
#endif
				if( objectsToSearchSet.Contains( prefab ) && assetsToSearchRootPrefabs.ContainsFast( prefab as GameObject ) )
					referenceNode.AddLinkTo( GetReferenceNode( prefab ), "Prefab object" );
			}

			// Search through all the components of the object
			Component[] components = go.GetComponents<Component>();
			for( int i = 0; i < components.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( components[i] ) );

			return referenceNode;
		}

		private ReferenceNode SearchComponent( Object unityObject )
		{
			Component component = (Component) unityObject;

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
			else if( component is ParticleSystemRenderer )
			{
				// Search ParticleSystemRenderer's custom meshes for references (they aren't searched by SerializedObject, unfortunately)
				Mesh[] meshes = new Mesh[( (ParticleSystemRenderer) component ).meshCount];
				int meshCount = ( (ParticleSystemRenderer) component ).GetMeshes( meshes );
				for( int i = 0; i < meshCount; i++ )
					referenceNode.AddLinkTo( SearchObject( meshes[i] ), "Custom particle mesh" );
			}
			else if( searchRenderers && component is Renderer )
			{
				// If an asset is a shader, texture or material, and this component is a Renderer,
				// search it for references
				Material[] materials = ( (Renderer) component ).sharedMaterials;
				for( int i = 0; i < materials.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( materials[i] ) );
			}
			else if( component is Animation )
			{
				// Search animation clips for references
				foreach( AnimationState anim in (Animation) component )
					referenceNode.AddLinkTo( SearchObject( anim.clip ) );

				// Search the objects that are animated by this Animation component for references
				SearchAnimatedObjects( referenceNode );
			}
			else if( component is Animator )
			{
				// Search animation clips for references (via AnimatorController)
				referenceNode.AddLinkTo( SearchObject( ( (Animator) component ).runtimeAnimatorController ) );

				// Search the objects that are animated by this Animator component for references
				SearchAnimatedObjects( referenceNode );
			}
#if UNITY_2017_2_OR_NEWER
			else if( component is Tilemap )
			{
				// Search the tiles for references
				TileBase[] tiles = new TileBase[( (Tilemap) component ).GetUsedTilesCount()];
				( (Tilemap) component ).GetUsedTilesNonAlloc( tiles );

				if( tiles != null )
				{
					for( int i = 0; i < tiles.Length; i++ )
						referenceNode.AddLinkTo( SearchObject( tiles[i] ), "Tile" );
				}
			}
#endif
#if UNITY_2017_1_OR_NEWER
			else if( component is PlayableDirector )
			{
				// Search the PlayableAsset's scene bindings for references
				PlayableAsset playableAsset = ( (PlayableDirector) component ).playableAsset;
				if( playableAsset != null && !playableAsset.Equals( null ) )
				{
					foreach( PlayableBinding binding in playableAsset.outputs )
						referenceNode.AddLinkTo( SearchObject( ( (PlayableDirector) component ).GetGenericBinding( binding.sourceObject ) ), "Binding: " + binding.streamName );
				}
			}
#endif

			SearchVariablesWithSerializedObject( referenceNode );
			return referenceNode;
		}

		private ReferenceNode SearchMaterial( Object unityObject )
		{
			Material material = (Material) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( material );

			if( searchMaterialsForShader && objectsToSearchSet.Contains( material.shader ) )
				referenceNode.AddLinkTo( GetReferenceNode( material.shader ), "Shader" );

			if( searchTextureReferences )
			{
				// Search through all the textures attached to this material
				// Credit: http://answers.unity3d.com/answers/1116025/view.html
				Shader shader = material.shader;
				int shaderPropertyCount = ShaderUtil.GetPropertyCount( shader );
				for( int i = 0; i < shaderPropertyCount; i++ )
				{
					if( ShaderUtil.GetPropertyType( shader, i ) == ShaderUtil.ShaderPropertyType.TexEnv )
					{
						string propertyName = ShaderUtil.GetPropertyName( shader, i );
						Texture assignedTexture = material.GetTexture( propertyName );
						if( objectsToSearchSet.Contains( assignedTexture ) )
							referenceNode.AddLinkTo( GetReferenceNode( assignedTexture ), "Shader property: " + propertyName );
					}
				}
			}

			return referenceNode;
		}

		// Searches default Texture values assigned to shader properties, as well as #include references in shader source code
		private ReferenceNode SearchShader( Object unityObject )
		{
			Shader shader = (Shader) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( shader );

			if( searchTextureReferences )
			{
				ShaderImporter shaderImporter = AssetImporter.GetAtPath( AssetDatabase.GetAssetPath( unityObject ) ) as ShaderImporter;
				if( shaderImporter != null )
				{
					int shaderPropertyCount = ShaderUtil.GetPropertyCount( shader );
					for( int i = 0; i < shaderPropertyCount; i++ )
					{
						if( ShaderUtil.GetPropertyType( shader, i ) == ShaderUtil.ShaderPropertyType.TexEnv )
						{
							string propertyName = ShaderUtil.GetPropertyName( shader, i );
							Texture defaultTexture = shaderImporter.GetDefaultTexture( propertyName );
#if UNITY_2018_1_OR_NEWER
							if( !defaultTexture )
								defaultTexture = shaderImporter.GetNonModifiableTexture( propertyName );
#endif

							if( objectsToSearchSet.Contains( defaultTexture ) )
								referenceNode.AddLinkTo( GetReferenceNode( defaultTexture ), "Default Texture: " + propertyName );
						}
					}
				}
			}

			// Search shader source code for #include references
			if( shaderIncludesToSearchSet.Count > 0 )
				SearchShaderSourceCodeForCGIncludes( referenceNode );

			return referenceNode;
		}

		// Searches .compute, .cginc, .cg, .hlsl and .glslinc assets for #include references
		private ReferenceNode SearchShaderSecondaryAsset( Object unityObject )
		{
			if( shaderIncludesToSearchSet.Count == 0 )
				return null;

			ReferenceNode referenceNode = PopReferenceNode( unityObject );
			SearchShaderSourceCodeForCGIncludes( referenceNode );
			return referenceNode;
		}

		// Searches default UnityEngine.Object values assigned to script variables
		private ReferenceNode SearchMonoScript( Object unityObject )
		{
			MonoScript script = (MonoScript) unityObject;
			Type scriptType = script.GetClass();
			if( scriptType == null || ( !scriptType.IsSubclassOf( typeof( MonoBehaviour ) ) && !scriptType.IsSubclassOf( typeof( ScriptableObject ) ) ) )
				return null;

			MonoImporter scriptImporter = AssetImporter.GetAtPath( AssetDatabase.GetAssetPath( unityObject ) ) as MonoImporter;
			if( scriptImporter == null )
				return null;

			ReferenceNode referenceNode = PopReferenceNode( script );

			VariableGetterHolder[] variables = GetFilteredVariablesForType( scriptType );
			for( int i = 0; i < variables.Length; i++ )
			{
				if( variables[i].isSerializable && !variables[i].isProperty )
				{
					Object defaultValue = scriptImporter.GetDefaultReference( variables[i].name );
					if( objectsToSearchSet.Contains( defaultValue ) )
						referenceNode.AddLinkTo( GetReferenceNode( defaultValue ), "Default variable value: " + variables[i].name );
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorController( Object unityObject )
		{
			RuntimeAnimatorController controller = (RuntimeAnimatorController) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( controller );

			if( controller is AnimatorController )
			{
				AnimatorControllerLayer[] layers = ( (AnimatorController) controller ).layers;
				for( int i = 0; i < layers.Length; i++ )
				{
					if( objectsToSearchSet.Contains( layers[i].avatarMask ) )
						referenceNode.AddLinkTo( GetReferenceNode( layers[i].avatarMask ), layers[i].name + " Mask" );

					referenceNode.AddLinkTo( SearchObject( layers[i].stateMachine ) );
				}
			}
			else
			{
				if( controller is AnimatorOverrideController )
				{
					RuntimeAnimatorController parentController = ( (AnimatorOverrideController) controller ).runtimeAnimatorController;
					if( objectsToSearchSet.Contains( parentController ) )
						referenceNode.AddLinkTo( GetReferenceNode( parentController ) );
				}

				AnimationClip[] animClips = controller.animationClips;
				for( int i = 0; i < animClips.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( animClips[i] ) );
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorStateMachine( Object unityObject )
		{
			AnimatorStateMachine animatorStateMachine = (AnimatorStateMachine) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( animatorStateMachine );

			ChildAnimatorStateMachine[] stateMachines = animatorStateMachine.stateMachines;
			for( int i = 0; i < stateMachines.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( stateMachines[i].stateMachine ), "Child State Machine" );

			ChildAnimatorState[] states = animatorStateMachine.states;
			for( int i = 0; i < states.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( states[i].state ) );

			if( searchMonoBehavioursForScript )
			{
				StateMachineBehaviour[] behaviours = animatorStateMachine.behaviours;
				for( int i = 0; i < behaviours.Length; i++ )
				{
					MonoScript script = MonoScript.FromScriptableObject( behaviours[i] );
					if( objectsToSearchSet.Contains( script ) )
						referenceNode.AddLinkTo( GetReferenceNode( script ) );
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorState( Object unityObject )
		{
			AnimatorState animatorState = (AnimatorState) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( animatorState );

			referenceNode.AddLinkTo( SearchObject( animatorState.motion ), "Motion" );

			if( searchMonoBehavioursForScript )
			{
				StateMachineBehaviour[] behaviours = animatorState.behaviours;
				for( int i = 0; i < behaviours.Length; i++ )
				{
					MonoScript script = MonoScript.FromScriptableObject( behaviours[i] );
					if( objectsToSearchSet.Contains( script ) )
						referenceNode.AddLinkTo( GetReferenceNode( script ) );
				}
			}

			return referenceNode;
		}

		private ReferenceNode SearchAnimatorStateTransition( Object unityObject )
		{
			// Don't search AnimatorStateTransition objects, it will just return duplicate results of SearchAnimatorStateMachine
			return PopReferenceNode( unityObject );
		}

		private ReferenceNode SearchBlendTree( Object unityObject )
		{
			BlendTree blendTree = (BlendTree) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( blendTree );

			ChildMotion[] children = blendTree.children;
			for( int i = 0; i < children.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( children[i].motion ), "Motion" );

			return referenceNode;
		}

		private ReferenceNode SearchAnimationClip( Object unityObject )
		{
			AnimationClip clip = (AnimationClip) unityObject;
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

			// Get all events from animation clip
			AnimationEvent[] events = AnimationUtility.GetAnimationEvents( clip );
			for( int i = 0; i < events.Length; i++ )
				referenceNode.AddLinkTo( SearchObject( events[i].objectReferenceParameter ), "AnimationEvent: " + events[i].time );

			return referenceNode;
		}

#if UNITY_2017_1_OR_NEWER
		private ReferenceNode SearchSpriteAtlas( Object unityObject )
		{
			SpriteAtlas spriteAtlas = (SpriteAtlas) unityObject;
			ReferenceNode referenceNode = PopReferenceNode( spriteAtlas );

			SerializedObject spriteAtlasSO = new SerializedObject( spriteAtlas );
			if( spriteAtlas.isVariant )
			{
				Object masterAtlas = spriteAtlasSO.FindProperty( "m_MasterAtlas" ).objectReferenceValue;
				if( objectsToSearchSet.Contains( masterAtlas ) )
					referenceNode.AddLinkTo( SearchObject( masterAtlas ), "Master Atlas" );
			}

#if UNITY_2018_2_OR_NEWER
			Object[] packables = spriteAtlas.GetPackables();
			if( packables != null )
			{
				for( int i = 0; i < packables.Length; i++ )
					referenceNode.AddLinkTo( SearchObject( packables[i] ), "Packed Texture" );
			}
#else
			SerializedProperty packables = spriteAtlasSO.FindProperty( "m_EditorData.packables" );
			if( packables != null )
			{
				for( int i = 0, length = packables.arraySize; i < length; i++ )
					referenceNode.AddLinkTo( SearchObject( packables.GetArrayElementAtIndex( i ).objectReferenceValue ), "Packed Texture" );
			}
#endif

			return referenceNode;
		}
#endif

#if UNITY_2017_3_OR_NEWER
		// Find references from an Assembly Definition File to its Assembly Definition References
		private ReferenceNode SearchAssemblyDefinitionFile( Object unityObject )
		{
			if( assemblyDefinitionFilesToSearch.Count == 0 )
				return null;

			AssemblyDefinitionReferences assemblyDefinitionFile = JsonUtility.FromJson<AssemblyDefinitionReferences>( ( (TextAsset) unityObject ).text );
			ReferenceNode referenceNode = PopReferenceNode( unityObject );

			if( !string.IsNullOrEmpty( assemblyDefinitionFile.reference ) )
			{
				if( assemblyDefinitionFile.references == null )
					assemblyDefinitionFile.references = new List<string>( 1 ) { assemblyDefinitionFile.reference };
				else
					assemblyDefinitionFile.references.Add( assemblyDefinitionFile.reference );
			}

			if( assemblyDefinitionFile.references != null )
			{
				for( int i = 0; i < assemblyDefinitionFile.references.Count; i++ )
				{
#if UNITY_2019_1_OR_NEWER
					string assemblyPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyReference( assemblyDefinitionFile.references[i] );
#else
					string assemblyPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName( assemblyDefinitionFile.references[i] );
#endif
					if( !string.IsNullOrEmpty( assemblyPath ) )
					{
						Object searchedAssemblyDefinitionFile;
						if( assemblyDefinitionFilesToSearch.TryGetValue( assemblyPath, out searchedAssemblyDefinitionFile ) )
							referenceNode.AddLinkTo( GetReferenceNode( searchedAssemblyDefinitionFile ), "Referenced Assembly" );
					}
				}
			}

			return referenceNode;
		}
#endif

#if UNITY_2018_1_OR_NEWER
		// Searches Shader Graph assets for references
		private ReferenceNode SearchShaderGraph( Object unityObject )
		{
			if( !searchTextureReferences && !searchShaderGraphsForSubGraphs && shaderIncludesToSearchSet.Count == 0 )
				return null;

			ReferenceNode referenceNode = PopReferenceNode( unityObject );

			// Shader Graph assets are JSON files, they must be crawled manually to find references
			string graphJson = File.ReadAllText( AssetDatabase.GetAssetPath( unityObject ) );
			if( graphJson.IndexOf( "\"m_ObjectId\"", 0, Mathf.Min( 200, graphJson.Length ) ) >= 0 )
			{
				// New Shader Graph serialization format is used: https://github.com/Unity-Technologies/Graphics/pull/222
				// Iterate over all these occurrences:   "guid\": \"GUID_VALUE\" (\" is used instead of " because it is a nested JSON)
				IterateOverValuesInString( graphJson, "\"guid\\\"", '"', ( guid ) =>
				{
					if( guid.Length > 1 )
					{
						if( guid[guid.Length - 1] == '\\' )
							guid = guid.Substring( 0, guid.Length - 1 );

						string referencePath = AssetDatabase.GUIDToAssetPath( guid );
						if( !string.IsNullOrEmpty( referencePath ) && assetsToSearchPathsSet.Contains( referencePath ) )
						{
							Object reference = AssetDatabase.LoadMainAssetAtPath( referencePath );
							if( objectsToSearchSet.Contains( reference ) )
								referenceNode.AddLinkTo( GetReferenceNode( reference ), "Used in graph" );
						}
					}
				} );

				if( shaderIncludesToSearchSet.Count > 0 )
				{
					// Iterate over all these occurrences:   "m_FunctionSource": "GUID_VALUE" (this one is not nested JSON)
					IterateOverValuesInString( graphJson, "\"m_FunctionSource\"", '"', ( guid ) =>
					{
						string referencePath = AssetDatabase.GUIDToAssetPath( guid );
						if( !string.IsNullOrEmpty( referencePath ) && assetsToSearchPathsSet.Contains( referencePath ) )
						{
							Object reference = AssetDatabase.LoadMainAssetAtPath( referencePath );
							if( objectsToSearchSet.Contains( reference ) )
								referenceNode.AddLinkTo( GetReferenceNode( reference ), "Used in node: Custom Function" );
						}
					} );
				}
			}
			else
			{
				// Old Shader Graph serialization format is used. Although we could use the same search method as the new serialization format (which
				// is potentially faster), this alternative search method yields more information about references
				ShaderGraphReferences shaderGraph = JsonUtility.FromJson<ShaderGraphReferences>( graphJson );

				if( shaderGraph.m_SerializedProperties != null )
				{
					for( int i = shaderGraph.m_SerializedProperties.Count - 1; i >= 0; i-- )
					{
						string propertyJSON = shaderGraph.m_SerializedProperties[i].JSONnodeData;
						if( string.IsNullOrEmpty( propertyJSON ) )
							continue;

						ShaderGraphReferences.PropertyData propertyData = JsonUtility.FromJson<ShaderGraphReferences.PropertyData>( propertyJSON );
						if( propertyData.m_Value == null )
							continue;

						string texturePath = propertyData.m_Value.GetTexturePath();
						if( string.IsNullOrEmpty( texturePath ) || !assetsToSearchPathsSet.Contains( texturePath ) )
							continue;

						Texture texture = AssetDatabase.LoadAssetAtPath<Texture>( texturePath );
						if( objectsToSearchSet.Contains( texture ) )
							referenceNode.AddLinkTo( GetReferenceNode( texture ), "Default Texture: " + propertyData.GetName() );
					}
				}

				if( shaderGraph.m_SerializableNodes != null )
				{
					for( int i = shaderGraph.m_SerializableNodes.Count - 1; i >= 0; i-- )
					{
						string nodeJSON = shaderGraph.m_SerializableNodes[i].JSONnodeData;
						if( string.IsNullOrEmpty( nodeJSON ) )
							continue;

						ShaderGraphReferences.NodeData nodeData = JsonUtility.FromJson<ShaderGraphReferences.NodeData>( nodeJSON );
						if( !string.IsNullOrEmpty( nodeData.m_FunctionSource ) )
						{
							string customFunctionPath = AssetDatabase.GUIDToAssetPath( nodeData.m_FunctionSource );
							if( !string.IsNullOrEmpty( customFunctionPath ) && assetsToSearchPathsSet.Contains( customFunctionPath ) )
							{
								Object customFunction = AssetDatabase.LoadMainAssetAtPath( customFunctionPath );
								if( objectsToSearchSet.Contains( customFunction ) )
									referenceNode.AddLinkTo( GetReferenceNode( customFunction ), "Used in node: " + nodeData.m_Name );
							}
						}

						if( searchShaderGraphsForSubGraphs )
						{
							string subGraphPath = nodeData.GetSubGraphPath();
							if( !string.IsNullOrEmpty( subGraphPath ) && assetsToSearchPathsSet.Contains( subGraphPath ) )
							{
								Object subGraph = AssetDatabase.LoadMainAssetAtPath( subGraphPath );
								if( objectsToSearchSet.Contains( subGraph ) )
									referenceNode.AddLinkTo( GetReferenceNode( subGraph ), "Used as Sub-graph" );
							}
						}

						if( nodeData.m_SerializableSlots == null )
							continue;

						for( int j = nodeData.m_SerializableSlots.Count - 1; j >= 0; j-- )
						{
							string nodeSlotJSON = nodeData.m_SerializableSlots[j].JSONnodeData;
							if( string.IsNullOrEmpty( nodeSlotJSON ) )
								continue;

							string texturePath = JsonUtility.FromJson<ShaderGraphReferences.NodeSlotData>( nodeSlotJSON ).GetTexturePath();
							if( string.IsNullOrEmpty( texturePath ) || !assetsToSearchPathsSet.Contains( texturePath ) )
								continue;

							Texture texture = AssetDatabase.LoadAssetAtPath<Texture>( texturePath );
							if( objectsToSearchSet.Contains( texture ) )
								referenceNode.AddLinkTo( GetReferenceNode( texture ), "Used in node: " + nodeData.m_Name );
						}
					}
				}
			}

			return referenceNode;
		}
#endif

		// Find references from an Animation/Animator component to the objects that it animates
		private void SearchAnimatedObjects( ReferenceNode referenceNode )
		{
			GameObject root = ( (Component) referenceNode.nodeObject ).gameObject;
			AnimationClip[] clips = AnimationUtility.GetAnimationClips( root );
			for( int i = 0; i < clips.Length; i++ )
			{
				AnimationClip clip = clips[i];
				bool isClipUnique = true;
				for( int j = i - 1; j >= 0; j-- )
				{
					if( clips[j] == clip )
					{
						isClipUnique = false;
						break;
					}
				}

				if( !isClipUnique )
					continue;

				EditorCurveBinding[] uniqueBindings;
				if( !animationClipUniqueBindings.TryGetValue( clip, out uniqueBindings ) )
				{
					// Calculate all the "unique" paths that the animation clip's curves have
					// Both float curves (GetCurveBindings) and object reference curves (GetObjectReferenceCurveBindings) are checked
					List<EditorCurveBinding> _uniqueBindings = new List<EditorCurveBinding>( 2 );
					EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings( clip );
					for( int j = 0; j < bindings.Length; j++ )
					{
						string bindingPath = bindings[j].path;
						if( string.IsNullOrEmpty( bindingPath ) ) // Ignore the root animated object
							continue;

						bool isBindingUnique = true;
						for( int k = _uniqueBindings.Count - 1; k >= 0; k-- )
						{
							if( bindingPath == _uniqueBindings[k].path )
							{
								isBindingUnique = false;
								break;
							}
						}

						if( isBindingUnique )
							_uniqueBindings.Add( bindings[j] );
					}

					bindings = AnimationUtility.GetObjectReferenceCurveBindings( clip );
					for( int j = 0; j < bindings.Length; j++ )
					{
						string bindingPath = bindings[j].path;
						if( string.IsNullOrEmpty( bindingPath ) ) // Ignore the root animated object
							continue;

						bool isBindingUnique = true;
						for( int k = _uniqueBindings.Count - 1; k >= 0; k-- )
						{
							if( bindingPath == _uniqueBindings[k].path )
							{
								isBindingUnique = false;
								break;
							}
						}

						if( isBindingUnique )
							_uniqueBindings.Add( bindings[j] );
					}

					uniqueBindings = _uniqueBindings.ToArray();
					animationClipUniqueBindings[clip] = uniqueBindings;
				}

				string clipName = clip.name;
				for( int j = 0; j < uniqueBindings.Length; j++ )
					referenceNode.AddLinkTo( SearchObject( AnimationUtility.GetAnimatedObject( root, uniqueBindings[j] ) ), "Animated via clip: " + clipName );
			}
		}

		// Search #include references in shader source code
		private void SearchShaderSourceCodeForCGIncludes( ReferenceNode referenceNode )
		{
			string shaderPath = AssetDatabase.GetAssetPath( (Object) referenceNode.nodeObject );

			// Iterate over all these occurrences:   #include: "INCLUDE_REFERENCE"
			IterateOverValuesInString( File.ReadAllText( shaderPath ), "#include ", '"', ( include ) =>
			{
				bool isIncludePotentialReference = shaderIncludesToSearchSet.Contains( include );
				if( !isIncludePotentialReference )
				{
					// Get absolute path of the #include
					include = Path.GetFullPath( Path.Combine( Path.GetDirectoryName( shaderPath ), include ) );

					int trimStartLength = Directory.GetCurrentDirectory().Length + 1; // Convert absolute path to a Project-relative path
					if( include.Length > trimStartLength )
					{
						include = include.Substring( trimStartLength ).Replace( '\\', '/' );
						isIncludePotentialReference = shaderIncludesToSearchSet.Contains( include );
					}
				}

				if( isIncludePotentialReference )
				{
					Object cgShader = AssetDatabase.LoadMainAssetAtPath( include );
					if( objectsToSearchSet.Contains( cgShader ) )
						referenceNode.AddLinkTo( GetReferenceNode( cgShader ), "Used with #include" );
				}
			} );
		}

		// Search through variables of an object with SerializedObject
		private void SearchVariablesWithSerializedObject( ReferenceNode referenceNode )
		{
			if( !isInPlayMode || referenceNode.nodeObject.IsAsset() )
			{
				SerializedObject so = new SerializedObject( (Object) referenceNode.nodeObject );
				SerializedProperty iterator = so.GetIterator();
				SerializedProperty iteratorVisible = so.GetIterator();
				if( iterator.Next( true ) )
				{
					bool iteratingVisible = iteratorVisible.NextVisible( true );
					bool enterChildren;
					do
					{
						// Iterate over NextVisible properties AND the properties that have corresponding FieldInfos (internal Unity
						// properties don't have FieldInfos so we are skipping them, which is good because search results found in
						// those properties aren't interesting and mostly confusing)
						bool isVisible = iteratingVisible && SerializedProperty.EqualContents( iterator, iteratorVisible );
						if( isVisible )
							iteratingVisible = iteratorVisible.NextVisible( true );
						else
						{
							Type propFieldType;
							isVisible = iterator.type == "Array" || fieldInfoGetter( iterator, out propFieldType ) != null;
						}

						if( !isVisible )
						{
							enterChildren = false;
							continue;
						}

						ReferenceNode searchResult;
						switch( iterator.propertyType )
						{
							case SerializedPropertyType.ObjectReference:
								searchResult = SearchObject( iterator.objectReferenceValue );
								enterChildren = false;
								break;
							case SerializedPropertyType.ExposedReference:
								searchResult = SearchObject( iterator.exposedReferenceValue );
								enterChildren = false;
								break;
#if UNITY_2019_3_OR_NEWER
							case SerializedPropertyType.ManagedReference:
								searchResult = SearchObject( GetRawSerializedPropertyValue( iterator ) );
								enterChildren = false;
								break;
#endif
							case SerializedPropertyType.Generic:
								searchResult = null;
								enterChildren = true;
								break;
							default:
								searchResult = null;
								enterChildren = false;
								break;
						}

						if( searchResult != null && searchResult != referenceNode )
						{
							string propertyPath = iterator.propertyPath;

							// m_RD.texture is a redundant reference that shows up when searching sprites
							if( !propertyPath.EndsWithFast( "m_RD.texture" ) )
								referenceNode.AddLinkTo( searchResult, "Variable: " + propertyPath.Replace( ".Array.data[", "[" ) ); // "arrayVariable.Array.data[0]" becomes "arrayVariable[0]"
						}
					} while( iterator.Next( enterChildren ) );

					return;
				}
			}

			// Use reflection algorithm as fallback
			SearchVariablesWithReflection( referenceNode );
		}

		// Search through variables of an object with reflection
		private void SearchVariablesWithReflection( ReferenceNode referenceNode )
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

					// Values stored inside ICollection objects are searched using IEnumerable,
					// no need to have duplicate search entries
					if( !( variableValue is ICollection ) )
					{
						ReferenceNode searchResult = SearchObject( variableValue );
						if( searchResult != null && searchResult != referenceNode )
							referenceNode.AddLinkTo( searchResult, ( variables[i].isProperty ? "Property: " : "Variable: " ) + variables[i].name );
					}

					if( variableValue is IEnumerable && !( variableValue is Transform ) )
					{
						// If the field is IEnumerable (possibly an array or collection), search through members of it
						// Note that Transform IEnumerable (children of the transform) is not iterated
						int index = 0;
						foreach( object element in (IEnumerable) variableValue )
						{
							ReferenceNode searchResult = SearchObject( element );
							if( searchResult != null && searchResult != referenceNode )
								referenceNode.AddLinkTo( searchResult, string.Concat( variables[i].isProperty ? "Property: " : "Variable: ", variables[i].name, "[", index + "]" ) );

							index++;
						}
					}
				}
				catch( UnassignedReferenceException ) { }
				catch( MissingReferenceException ) { }
				catch( MissingComponentException ) { }
				catch( NotImplementedException ) { }
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
			// 
			// P.S. IsIgnoredUnityType() extension function handles steps 2) and 3)

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
						FieldInfo field = fields[i];

						// Skip obsolete fields
						if( Attribute.IsDefined( field, typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						if( field.FieldType.IsIgnoredUnityType() )
							continue;

						// Additional filtering for fields:
						// 1- Ignore "m_RectTransform", "m_CanvasRenderer" and "m_Canvas" fields of Graphic components
						string fieldName = field.Name;
						if( typeof( Graphic ).IsAssignableFrom( currType ) &&
							( fieldName == "m_RectTransform" || fieldName == "m_CanvasRenderer" || fieldName == "m_Canvas" ) )
							continue;

						VariableGetVal getter = field.CreateGetter( type );
						if( getter != null )
							validVariables.Add( new VariableGetterHolder( field, getter, searchSerializableVariablesOnly ? field.IsSerializable() : true ) );
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
						PropertyInfo property = properties[i];

						// Skip obsolete properties
						if( Attribute.IsDefined( property, typeof( ObsoleteAttribute ) ) )
							continue;

						// Skip primitive types
						if( property.PropertyType.IsIgnoredUnityType() )
							continue;

						// Skip properties without a getter function
						MethodInfo propertyGetter = property.GetGetMethod( true );
						if( propertyGetter == null )
							continue;

						// Skip indexer properties
						if( property.GetIndexParameters().Length > 0 )
							continue;

						// No need to check properties with 'override' keyword
						if( propertyGetter.GetBaseDefinition().DeclaringType != propertyGetter.DeclaringType )
							continue;

						// Additional filtering for properties:
						// 1- Ignore "gameObject", "transform", "rectTransform" and "attachedRigidbody" properties of Component's to get more useful results
						// 2- Ignore "canvasRenderer" and "canvas" properties of Graphic components
						// 3 & 4- Prevent accessing properties of Unity that instantiate an existing resource (causing memory leak)
						string propertyName = property.Name;
						if( typeof( Component ).IsAssignableFrom( currType ) && ( propertyName == "gameObject" ||
							propertyName == "transform" || propertyName == "attachedRigidbody" || propertyName == "rectTransform" ) )
							continue;
						else if( typeof( Graphic ).IsAssignableFrom( currType ) &&
							( propertyName == "canvasRenderer" || propertyName == "canvas" ) )
							continue;
						else if( typeof( MeshFilter ).IsAssignableFrom( currType ) && propertyName == "mesh" )
							continue;
						else if( typeof( Renderer ).IsAssignableFrom( currType ) &&
							( propertyName == "sharedMaterial" || propertyName == "sharedMaterials" ) )
							continue;
						else if( ( propertyName == "material" || propertyName == "materials" ) &&
							( typeof( Renderer ).IsAssignableFrom( currType ) || typeof( Collider ).IsAssignableFrom( currType ) ||
#if !UNITY_2019_3_OR_NEWER
#pragma warning disable 0618
							typeof( GUIText ).IsAssignableFrom( currType ) ||
#pragma warning restore 0618
#endif
							typeof( Collider2D ).IsAssignableFrom( currType ) ) )
							continue;
						else
						{
							VariableGetVal getter = property.CreateGetter();
							if( getter != null )
								validVariables.Add( new VariableGetterHolder( property, getter, searchSerializableVariablesOnly ? property.IsSerializable() : true ) );
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

		// Credit: http://answers.unity.com/answers/425602/view.html
		// Returns the raw System.Object value of a SerializedProperty
		public object GetRawSerializedPropertyValue( SerializedProperty property )
		{
			object result = property.serializedObject.targetObject;
			string[] path = property.propertyPath.Replace( ".Array.data[", "[" ).Split( '.' );
			for( int i = 0; i < path.Length; i++ )
			{
				string pathElement = path[i];

				int arrayStartIndex = pathElement.IndexOf( '[' );
				if( arrayStartIndex < 0 )
					result = GetFieldValue( result, pathElement );
				else
				{
					string variableName = pathElement.Substring( 0, arrayStartIndex );

					int arrayEndIndex = pathElement.IndexOf( ']', arrayStartIndex + 1 );
					int arrayElementIndex = int.Parse( pathElement.Substring( arrayStartIndex + 1, arrayEndIndex - arrayStartIndex - 1 ) );
					result = GetFieldValue( result, variableName, arrayElementIndex );
				}
			}

			return result;
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		private object GetFieldValue( object source, string fieldName )
		{
			if( source == null )
				return null;

			FieldInfo fieldInfo = null;
			Type type = source.GetType();
			while( fieldInfo == null && type != typeof( object ) )
			{
				fieldInfo = type.GetField( fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly );
				type = type.BaseType;
			}

			if( fieldInfo != null )
				return fieldInfo.GetValue( source );

			PropertyInfo propertyInfo = null;
			type = source.GetType();
			while( propertyInfo == null && type != typeof( object ) )
			{
				propertyInfo = type.GetProperty( fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.IgnoreCase );
				type = type.BaseType;
			}

			if( propertyInfo != null )
				return propertyInfo.GetValue( source, null );

			if( fieldName.Length > 2 && fieldName.StartsWith( "m_", StringComparison.OrdinalIgnoreCase ) )
				return GetFieldValue( source, fieldName.Substring( 2 ) );

			return null;
		}

		// Credit: http://answers.unity.com/answers/425602/view.html
		private object GetFieldValue( object source, string fieldName, int arrayIndex )
		{
			IEnumerable enumerable = GetFieldValue( source, fieldName ) as IEnumerable;
			if( enumerable == null )
				return null;

			if( enumerable is IList )
				return ( (IList) enumerable )[arrayIndex];

			IEnumerator enumerator = enumerable.GetEnumerator();
			for( int i = 0; i <= arrayIndex; i++ )
				enumerator.MoveNext();

			return enumerator.Current;
		}

		// Iterates over all occurrences of specific key-value pairs in string
		// Example1: #include "VALUE"  valuePrefix=#include, valueWrapperChar="
		// Example2: "guid": "VALUE"  valuePrefix="guid", valueWrapperChar="
		private void IterateOverValuesInString( string str, string valuePrefix, char valueWrapperChar, Action<string> valueAction )
		{
			int valueStartIndex, valueEndIndex = 0;
			while( true )
			{
				valueStartIndex = str.IndexOf( valuePrefix, valueEndIndex );
				if( valueStartIndex < 0 )
					return;

				valueStartIndex = str.IndexOf( valueWrapperChar, valueStartIndex + valuePrefix.Length );
				if( valueStartIndex < 0 )
					return;

				valueStartIndex++;
				valueEndIndex = str.IndexOf( valueWrapperChar, valueStartIndex );
				if( valueEndIndex < 0 )
					return;

				if( valueEndIndex > valueStartIndex )
					valueAction( str.Substring( valueStartIndex, valueEndIndex - valueStartIndex ) );
			}
		}
	}
}