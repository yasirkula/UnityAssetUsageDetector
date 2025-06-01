﻿using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AssetUsageDetectorNamespace
{
	// Delegate to get the value of a variable (either field or property)
	public delegate object VariableGetVal( object obj );

	// Custom struct to hold a variable, its important properties and its getter function
	public readonly struct VariableGetterHolder
	{
		public readonly MemberInfo variable;
		public readonly bool isSerializable;
		private readonly VariableGetVal getter;

		public readonly string Name { get { return variable.Name; } }
		public readonly bool IsProperty { get { return variable is PropertyInfo; } }

		public VariableGetterHolder( FieldInfo fieldInfo, VariableGetVal getter, bool isSerializable )
		{
			this.variable = fieldInfo;
			this.isSerializable = isSerializable;
			this.getter = getter;
		}

		public VariableGetterHolder( PropertyInfo propertyInfo, VariableGetVal getter, bool isSerializable )
		{
			this.variable = propertyInfo;
			this.isSerializable = isSerializable;
			this.getter = getter;
		}

		public readonly object Get( object obj )
		{
			try
			{
				return getter( obj );
			}
			catch( Exception e )
			{
				StringBuilder sb = Utilities.stringBuilder;
				sb.Length = 0;
				sb.Append( "Error while getting the value of (" ).Append( IsProperty ? ( (PropertyInfo) variable ).PropertyType : ( (FieldInfo) variable ).FieldType ).Append( ") " )
					.Append( variable.DeclaringType ).Append( "." ).Append( Name ).Append( ": " ).Append( e );

				Debug.LogError( sb.ToString() );
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
				// Property getters may return various kinds of exceptions if their backing fields are not initialized (yet)
				return null;
			}
		}
	}
}