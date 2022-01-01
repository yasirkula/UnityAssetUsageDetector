using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetUsageDetectorNamespace
{
	public abstract class ListDrawer<T>
	{
		private readonly string label;
		private readonly bool acceptSceneObjects;

		protected ListDrawer( string label, bool acceptSceneObjects )
		{
			this.label = label;
			this.acceptSceneObjects = acceptSceneObjects;
		}

		// Exposes a list on GUI
		public bool Draw( List<T> list )
		{
			bool hasChanged = false;
			bool guiEnabled = GUI.enabled;

			Event ev = Event.current;

			GUILayout.BeginHorizontal();

			GUILayout.Label( label );

			if( guiEnabled )
			{
				// Handle drag & drop references to array
				// Credit: https://answers.unity.com/answers/657877/view.html
				if( ( ev.type == EventType.DragPerform || ev.type == EventType.DragUpdated ) && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
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
									bool replacedNullElement = false;
									for( int j = 0; j < list.Count; j++ )
									{
										if( IsElementNull( list[j] ) )
										{
											list[j] = CreateElement( draggedObjects[i] );

											replacedNullElement = true;
											break;
										}
									}

									if( !replacedNullElement )
										list.Add( CreateElement( draggedObjects[i] ) );

									hasChanged = true;
								}
							}
						}
					}

					ev.Use();
				}
				else if( ev.type == EventType.ContextClick && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					GenericMenu contextMenu = new GenericMenu();
					contextMenu.AddItem( new GUIContent( "Clear" ), false, () =>
					{
						list.Clear();
						list.Add( CreateElement( null ) );
					} );
					contextMenu.ShowAsContext();

					ev.Use();
				}

				if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
					list.Insert( 0, CreateElement( null ) );
			}

			GUILayout.EndHorizontal();

			for( int i = 0; i < list.Count; i++ )
			{
				T element = list[i];

				GUI.changed = false;
				GUILayout.BeginHorizontal();

				Object prevObject = GetObjectFromElement( element );
				Object newObject = EditorGUILayout.ObjectField( "", prevObject, typeof( Object ), acceptSceneObjects );

				if( GUI.changed )
				{
					hasChanged = true;
					SetObjectOfElement( list, i, newObject );
				}

				if( guiEnabled )
				{
					if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
						list.Insert( i + 1, CreateElement( null ) );

					if( GUILayout.Button( "-", Utilities.GL_WIDTH_25 ) )
					{
						if( element != null && !element.Equals( null ) )
							hasChanged = true;

						// Lists with no elements look ugly, always keep a dummy null variable
						if( list.Count > 1 )
							list.RemoveAt( i-- );
						else
							list[0] = CreateElement( null );
					}
				}

				GUILayout.EndHorizontal();

				PostElementDrawer( element );
			}

			return hasChanged;
		}

		protected abstract T CreateElement( Object source );
		protected abstract Object GetObjectFromElement( T element );
		protected abstract void SetObjectOfElement( List<T> list, int index, Object value );
		protected abstract bool IsElementNull( T element );
		protected abstract void PostElementDrawer( T element );
	}

	public class ObjectListDrawer : ListDrawer<Object>
	{
		public ObjectListDrawer( string label, bool acceptSceneObjects ) : base( label, acceptSceneObjects )
		{
		}

		protected override Object CreateElement( Object source )
		{
			return source;
		}

		protected override Object GetObjectFromElement( Object element )
		{
			return element;
		}

		protected override void SetObjectOfElement( List<Object> list, int index, Object value )
		{
			list[index] = value;
		}

		protected override bool IsElementNull( Object element )
		{
			return element == null || element.Equals( null );
		}

		protected override void PostElementDrawer( Object element )
		{
		}
	}

	public class ObjectToSearchListDrawer : ListDrawer<ObjectToSearch>
	{
		public ObjectToSearchListDrawer() : base( "Find references of:", true )
		{
		}

		protected override ObjectToSearch CreateElement( Object source )
		{
			return new ObjectToSearch( source );
		}

		protected override Object GetObjectFromElement( ObjectToSearch element )
		{
			return element.obj;
		}

		protected override void SetObjectOfElement( List<ObjectToSearch> list, int index, Object value )
		{
			list[index].obj = value;
			list[index].RefreshSubAssets();
		}

		protected override bool IsElementNull( ObjectToSearch element )
		{
			return element == null || element.obj == null || element.obj.Equals( null );
		}

		protected override void PostElementDrawer( ObjectToSearch element )
		{
			List<ObjectToSearch.SubAsset> subAssetsToSearch = element.subAssets;
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

				element.showSubAssetsFoldout = EditorGUILayout.Foldout( element.showSubAssetsFoldout, "Include sub-assets in search:", true );

				GUILayout.EndHorizontal();

				if( element.showSubAssetsFoldout )
				{
					for( int j = 0; j < subAssetsToSearch.Count; j++ )
					{
						GUILayout.BeginHorizontal();

						subAssetsToSearch[j].shouldSearch = EditorGUILayout.Toggle( subAssetsToSearch[j].shouldSearch, Utilities.GL_WIDTH_25 );

						bool guiEnabled = GUI.enabled;
						GUI.enabled = false;
						EditorGUILayout.ObjectField( string.Empty, subAssetsToSearch[j].subAsset, typeof( Object ), true );
						GUI.enabled = guiEnabled;

						GUILayout.EndHorizontal();
					}
				}
			}
		}
	}
}