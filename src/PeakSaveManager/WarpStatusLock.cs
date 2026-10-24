using System;
using System.Reflection;

namespace PEAKSaveManager;

// PEAK 3.0 uses a property; previous builds used a private warp-only field.
// Release only the supported native lock when cancelling our own failed warp.
internal sealed class WarpStatusLock
{
    private readonly MethodInfo setter;
    private readonly FieldInfo legacyField;
    internal bool IsSupported => setter != null || legacyField != null;

    internal WarpStatusLock(Type characterType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo property = characterType.GetProperty("statusesLocked", flags);
        if (property?.PropertyType == typeof(bool)) setter = property.GetSetMethod(true);
        FieldInfo field = characterType.GetField("_warpStatusLock", flags);
        if (field?.FieldType == typeof(bool) && !field.IsInitOnly) legacyField = field;
    }

    internal void Release(object character)
    {
        if (setter != null) setter.Invoke(character, new object[] { false });
        else if (legacyField != null) legacyField.SetValue(character, false);
        else throw new InvalidOperationException("The game's teleport status-lock API is unsupported.");
    }
}
