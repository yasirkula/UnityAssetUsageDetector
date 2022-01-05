using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	public delegate void SearchRefactoring( SearchMatch match );

	public abstract class SearchMatch
	{
		public readonly object Source;
		public readonly Object Context; // Almost always equal to Source. This is the Object that needs to be dirtied (if not null) to notify Unity of changes to Value
		public Object Value { get; private set; }

		protected SearchMatch( object source, Object value )
		{
			Source = source;
			Context = source as Object;
			Value = value;
		}

		protected SearchMatch( object source, Object value, Object context ) : this( source, value )
		{
			Context = context;
		}

		public void ChangeValue( Object newValue )
		{
			if( newValue == Value )
				return;

			if( Context && ( Context.hideFlags & HideFlags.NotEditable ) == HideFlags.NotEditable )
			{
				Debug.LogWarning( "Can't change value of read-only Object: " + Context, Context );
				return;
			}

			try
			{
				bool setContextDirty;
				if( ChangeValue( newValue, out setContextDirty ) )
					OnValueChanged( newValue, setContextDirty );
			}
			catch( Exception e )
			{
				Debug.LogException( e );
			}
		}

		protected abstract bool ChangeValue( Object newValue, out bool setContextDirty );

		public void OnValueChanged( Object newValue, bool setContextDirty = true )
		{
			Value = newValue;

			if( setContextDirty )
			{
				if( Context )
				{
					if( AssetDatabase.Contains( Context ) )
						EditorUtility.SetDirty( Context );
					else if( !EditorApplication.isPlaying )
					{
						EditorUtility.SetDirty( Context );

						if( Context is Component )
							EditorSceneManager.MarkSceneDirty( ( (Component) Context ).gameObject.scene );
						else if( Context is GameObject )
							EditorSceneManager.MarkSceneDirty( ( (GameObject) Context ).scene );
						else
							EditorSceneManager.MarkAllScenesDirty();
					}
				}
				else if( !EditorApplication.isPlaying )
					EditorSceneManager.MarkAllScenesDirty();
			}
		}
	}

	public abstract class GenericSearchMatch : SearchMatch
	{
		public delegate void SetterFunction( Object newValue );

		public readonly SetterFunction Setter;

		internal GenericSearchMatch( object source, Object value, SetterFunction setter ) : base( source, value ) { Setter = setter; }
		internal GenericSearchMatch( object source, Object value, Object context, SetterFunction setter ) : base( source, value, context ) { Setter = setter; }

		protected override bool ChangeValue( Object newValue, out bool setContextDirty )
		{
			Setter( newValue );

			setContextDirty = true;
			return true;
		}
	}

	public abstract class ReadOnlySearchMatch : SearchMatch
	{
		internal ReadOnlySearchMatch( object source, Object value ) : base( source, value ) { }

		protected override bool ChangeValue( Object newValue, out bool setContextDirty )
		{
			Debug.LogWarning( "Can't change value of " + GetType().Name );

			setContextDirty = false;
			return false;
		}
	}

	/// <summary>
	/// - Source: Object whose SerializedProperty points to Value
	/// - Value: Referenced object
	/// - SerializedProperty: The SerializedProperty that points to Value
	/// </summary>
	public class SerializedPropertyMatch : SearchMatch
	{
		public readonly SerializedProperty SerializedProperty; // Next or NextVisible mustn't be called with this SerializedProperty

		internal SerializedPropertyMatch( Object source, Object value, SerializedProperty property ) : base( source, value ) { SerializedProperty = property; }

		protected override bool ChangeValue( Object newValue, out bool setContextDirty )
		{
			setContextDirty = true;

			switch( SerializedProperty.propertyType )
			{
				case SerializedPropertyType.ObjectReference:
					SerializedProperty.objectReferenceValue = newValue;
					if( SerializedProperty.objectReferenceValue != newValue )
					{
						Debug.LogWarning( "Couldn't cast " + newValue.GetType() + " to " + SerializedProperty.type );
						SerializedProperty.objectReferenceValue = Value;

						return false;
					}

					break;
				case SerializedPropertyType.ExposedReference:
					SerializedProperty.exposedReferenceValue = newValue;
					if( SerializedProperty.exposedReferenceValue != newValue )
					{
						Debug.LogWarning( "Couldn't cast " + newValue.GetType() + " to " + SerializedProperty.type );
						SerializedProperty.exposedReferenceValue = Value;

						return false;
					}

					break;
#if UNITY_2019_3_OR_NEWER
				case SerializedPropertyType.ManagedReference: SerializedProperty.managedReferenceValue = newValue; break;
#endif
			}

			SerializedProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
			return true;
		}
	}

	/// <summary>
	/// - Source: Object whose variable points to Value
	/// - Value: Referenced object
	/// - Variable: FieldInfo, PropertyInfo or IEnumerable (ChangeValue may not work for all IEnumerables) 
	/// </summary>
	public class ReflectionMatch : SearchMatch
	{
		public readonly object Variable;

		internal ReflectionMatch( object source, Object value, object variable ) : base( source, value ) { Variable = variable; }

		protected override bool ChangeValue( Object newValue, out bool setContextDirty )
		{
			setContextDirty = true;

			if( Variable is FieldInfo )
				( (FieldInfo) Variable ).SetValue( Source, newValue );
			else if( Variable is PropertyInfo )
			{
				PropertyInfo property = (PropertyInfo) Variable;
				if( !property.CanWrite )
				{
					Debug.LogWarning( "Property is read-only: " + property.DeclaringType.FullName + "." + property.Name );
					return false;
				}

				property.SetValue( Source, newValue, null );
			}
			else if( Variable is IList )
			{
				IList list = (IList) Variable;
				for( int i = list.Count - 1; i >= 0; i-- )
				{
					if( ReferenceEquals( list[i], Value ) )
						list[i] = newValue;
				}
			}
			else if( Variable is IDictionary )
			{
				IDictionary dictionary = (IDictionary) Variable;
				bool dictionaryModified;
				do
				{
					dictionaryModified = false;
					foreach( object dictKey in dictionary.Keys )
					{
						object dictValue = dictionary[dictKey];
						if( ReferenceEquals( dictKey, Value ) )
						{
							dictionary.Remove( dictKey );
							if( newValue )
								dictionary[newValue] = dictValue;

							dictionaryModified = true;
							break;
						}
						else if( ReferenceEquals( dictValue, Value ) )
						{
							dictionary[dictKey] = newValue;
							dictionaryModified = true;
							break;
						}
					}
				} while( dictionaryModified );
			}
			else
			{
				Debug.LogWarning( "Can't change value of " + Variable.GetType().Name );
				return false;
			}

			return true;
		}
	}

	/// <summary>
	/// - Source: MonoImporter (for scripts) or ShaderImporter
	/// - Value: Default value assigned to Source's specified variable in the Inspector
	/// - Variable: The variable of Source that Value is assigned to as default value
	/// - MonoScriptAllVariables: All variables of Source script if it's MonoImporter
	/// </summary>
	public class AssetImporterDefaultValueMatch : SearchMatch
	{
		public readonly string Variable;
		public readonly VariableGetterHolder[] MonoScriptAllVariables;

		internal AssetImporterDefaultValueMatch( Object source, Object value, string variable, VariableGetterHolder[] monoScriptAllVariables ) : base( source, value )
		{
			Variable = variable;
			MonoScriptAllVariables = monoScriptAllVariables;
		}

		protected override bool ChangeValue( Object newValue, out bool setContextDirty )
		{
			setContextDirty = false;

			if( Source is MonoImporter )
			{
				MonoImporter monoImporter = (MonoImporter) Source;

				List<string> variableNames = new List<string>( 8 );
				List<Object> variableValues = new List<Object>( 8 );

				for( int i = 0; i < MonoScriptAllVariables.Length; i++ )
				{
					if( MonoScriptAllVariables[i].isSerializable && !MonoScriptAllVariables[i].IsProperty )
					{
						Object variableDefaultValue = monoImporter.GetDefaultReference( MonoScriptAllVariables[i].Name );
						if( variableDefaultValue == Value && MonoScriptAllVariables[i].Name == Variable )
							variableDefaultValue = newValue;

						variableNames.Add( MonoScriptAllVariables[i].Name );
						variableValues.Add( variableDefaultValue );
					}
				}

				monoImporter.SetDefaultReferences( variableNames.ToArray(), variableValues.ToArray() );
				EditorApplication.delayCall += () => AssetDatabase.ImportAsset( monoImporter.assetPath ); // If code recompiles during search, it will break the search. Give it a 1 frame delay
			}
			else if( Source is ShaderImporter )
			{
				ShaderImporter shaderImporter = (ShaderImporter) Source;
				Shader shader = shaderImporter.GetShader();

				List<string> textureNames = new List<string>( 16 );
				List<Texture> textureValues = new List<Texture>( 16 );
#if UNITY_2018_1_OR_NEWER
				List<string> nonModifiableTextureNames = new List<string>( 16 );
				List<Texture> nonModifiableTextureValues = new List<Texture>( 16 );
#endif

				int shaderPropertyCount = ShaderUtil.GetPropertyCount( shader );
				for( int i = 0; i < shaderPropertyCount; i++ )
				{
					if( ShaderUtil.GetPropertyType( shader, i ) != ShaderUtil.ShaderPropertyType.TexEnv )
						continue;

					string propertyName = ShaderUtil.GetPropertyName( shader, i );
#if UNITY_2018_1_OR_NEWER
					if( ShaderUtil.IsShaderPropertyNonModifiableTexureProperty( shader, i ) )
					{
						Texture propertyDefaultValue = shaderImporter.GetNonModifiableTexture( propertyName );
						if( propertyDefaultValue == Value && propertyName == Variable )
							propertyDefaultValue = (Texture) newValue;

						nonModifiableTextureNames.Add( propertyName );
						nonModifiableTextureValues.Add( propertyDefaultValue );
					}
					else
#endif
					{
						Texture propertyDefaultValue = shaderImporter.GetDefaultTexture( propertyName );
						if( propertyDefaultValue == Value && propertyName == Variable )
							propertyDefaultValue = (Texture) newValue;

						textureNames.Add( propertyName );
						textureValues.Add( propertyDefaultValue );
					}
				}

				shaderImporter.SetDefaultTextures( textureNames.ToArray(), textureValues.ToArray() );
#if UNITY_2018_1_OR_NEWER
				shaderImporter.SetNonModifiableTextures( nonModifiableTextureNames.ToArray(), nonModifiableTextureValues.ToArray() );
#endif
				AssetDatabase.ImportAsset( shaderImporter.assetPath );
			}
			else
			{
				Debug.LogWarning( "Can't change default value of: " + Source.GetType() );
				return false;
			}

			return true;
		}
	}

	/// <summary>
	/// - Source: Animation, Animator, AnimatorStateMachine, AnimatorState, AnimatorControllerLayer, BlendTree, PlayableDirector* or AnimationClip*
	/// - Context: If Source is AnimatorControllerLayer, then its RuntimeAnimatorController. Otherwise, equal to Source
	/// - Value: AnimationClip, AnimatorController or AvatarMask used in Source (*for PlayableDirector and AnimationClip, it can be any Object value)
	/// </summary>
	public class AnimationSystemMatch : GenericSearchMatch
	{
		internal AnimationSystemMatch( object source, Object value, SetterFunction setter ) : base( source, value, setter ) { }
		internal AnimationSystemMatch( object source, Object value, Object context, SetterFunction setter ) : base( source, value, context, setter ) { }
	}

	/// <summary>
	/// - Source: GameObject, AnimatorStateMachine or AnimatorState
	/// - Value: The attached behaviour's source script (C# script or DLL, i.e. MonoScript)
	/// - Behaviour: The attached behaviour (MonoBehaviour or StateMachineBehaviour)
	/// </summary>
	public class BehaviourUsageMatch : ReadOnlySearchMatch
	{
		public readonly Object Behaviour;

		internal BehaviourUsageMatch( Object source, MonoScript value, Object behaviour ) : base( source, value ) { Behaviour = behaviour; }
	}

	/// <summary>
	/// - Source: GameObject Instance
	/// - Value: Prefab of that GameObject
	/// </summary>
	public class PrefabMatch : ReadOnlySearchMatch
	{
		internal PrefabMatch( Object source, Object value ) : base( source, value ) { }
	}

	/// <summary>
	/// - Source: Object that references Value
	/// - Value: Matched object
	/// </summary>
	public class OtherSearchMatch : GenericSearchMatch
	{
		internal OtherSearchMatch( object source, Object value, SetterFunction setter ) : base( source, value, setter ) { }
		internal OtherSearchMatch( object source, Object value, Object context, SetterFunction setter ) : base( source, value, context, setter ) { }
	}
}