using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AssetUsageDetectorNamespace
{
	public class SearchResultTooltip : EditorWindow
	{
		private static SearchResultTooltip mainWindow;
		private static string tooltip;

		private static GUIStyle m_style;
		internal static GUIStyle Style
		{
			get
			{
				if( m_style == null )
				{
					m_style = (GUIStyle) typeof( EditorStyles ).GetProperty( "tooltip", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ).GetValue( null, null );
					m_style.richText = true;
				}

				return m_style;
			}
		}

		public static void Show( Rect sourcePosition, string tooltip )
		{
			Vector2 preferredSize = Style.CalcSize( new GUIContent( tooltip ) ) + Style.contentOffset + new Vector2( Style.padding.horizontal + Style.margin.horizontal, Style.padding.vertical + Style.margin.vertical );
			Rect preferredPosition;

			Rect positionLeft = new Rect( sourcePosition.position - new Vector2( preferredSize.x, 0f ), preferredSize );
			Rect screenFittedPositionLeft = Utilities.GetScreenFittedRect( positionLeft );

			Vector2 positionOffset = positionLeft.position - screenFittedPositionLeft.position;
			Vector2 sizeOffset = positionLeft.size - screenFittedPositionLeft.size;
			if( positionOffset.sqrMagnitude <= 400f && sizeOffset.sqrMagnitude <= 400f )
				preferredPosition = screenFittedPositionLeft;
			else
			{
				Rect positionRight = new Rect( sourcePosition.position + new Vector2( sourcePosition.width, 0f ), preferredSize );
				Rect screenFittedPositionRight = Utilities.GetScreenFittedRect( positionRight );

				Vector2 positionOffset2 = positionRight.position - screenFittedPositionRight.position;
				Vector2 sizeOffset2 = positionRight.size - screenFittedPositionRight.size;
				if( positionOffset2.magnitude + sizeOffset2.magnitude < positionOffset.magnitude + sizeOffset.magnitude )
					preferredPosition = screenFittedPositionRight;
				else
					preferredPosition = screenFittedPositionLeft;
			}

			// Don't lose focus to the previous window
			EditorWindow prevFocusedWindow = focusedWindow;

			if( !mainWindow )
			{
				mainWindow = CreateInstance<SearchResultTooltip>();
				mainWindow.ShowPopup();
			}

			SearchResultTooltip.tooltip = tooltip;
			mainWindow.minSize = preferredPosition.size;
			mainWindow.position = preferredPosition;
			mainWindow.Repaint();

			if( prevFocusedWindow )
				prevFocusedWindow.Focus();
		}

		public static void Hide()
		{
			if( mainWindow )
			{
				mainWindow.Close();
				mainWindow = null;
			}
		}

		private void OnGUI()
		{
			// If somehow the tooltip isn't automatically closed, allow closing it by clicking on it
			if( Event.current.type == EventType.MouseDown )
			{
				Hide();
				GUIUtility.ExitGUI();
			}

			GUI.Label( new Rect( Vector2.zero, position.size ), tooltip, Style );
		}
	}
}