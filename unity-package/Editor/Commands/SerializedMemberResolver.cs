using System;
using System.Reflection;
using UnityEngine;

namespace Patina.Editor.Commands
{
    internal static class SerializedMemberResolver
    {
        private const BindingFlags DeclaredInstance =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        internal static bool TryResolve(
            Type type,
            string memberName,
            out Type memberType,
            out Action<object, object> assign
        )
        {
            PropertyInfo prop = type.GetProperty(
                memberName,
                BindingFlags.Public | BindingFlags.Instance
            );
            if (prop != null && prop.CanWrite)
            {
                memberType = prop.PropertyType;
                assign = (target, value) => prop.SetValue(target, value);
                return true;
            }

            FieldInfo field =
                type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance)
                ?? FindSerializedField(type, memberName);
            if (field != null)
            {
                memberType = field.FieldType;
                assign = (target, value) => field.SetValue(target, value);
                return true;
            }

            memberType = null;
            assign = null;
            return false;
        }

        internal static string NotFoundMessage(string memberName, string componentType)
        {
            return $"Property or field '{memberName}' not found on '{componentType}'. "
                + "Only public fields, public writable properties and [SerializeField] fields "
                + "(including inherited ones) are supported";
        }

        private static FieldInfo FindSerializedField(Type type, string memberName)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(memberName, DeclaredInstance);
                if (
                    field != null
                    && (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null)
                )
                    return field;
            }
            return null;
        }
    }
}
