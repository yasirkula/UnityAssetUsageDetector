using System;
using System.Reflection;
using UnityEngine;

namespace AssetUsageDetectorNamespace
{
	// Delegate to get the value of a variable (either field or property)
	public delegate object VariableGetVal( object obj );

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
			try
			{
				return getter( obj );
			}
			catch( Exception e )
			{
				Debug.LogException( e );
				return null;
			}
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
}