using System.Collections;
using System.Reflection;

namespace SbExplorer.Core.Management;

public static class DefinitionMerge
{
    /// <summary>
    /// Applies the settings that differ between <paramref name="baseline"/> (the form as it was opened) and
    /// <paramref name="edited"/> onto <paramref name="current"/> (the entity as it is now). Saving a form then
    /// can't undo changes made elsewhere since it was opened, such as someone disabling the queue.
    /// Collections (a topic's subscriptions, a subscription's rules) are left as they are in <paramref name="current"/>.
    /// </summary>
    public static T ChangesOnto<T>(T baseline, T edited, T current) where T : class
    {
        // Records have a compiler-generated copy method; init-only setters can be set through reflection.
        var merged = (T)typeof(T).GetMethod("<Clone>$")!.Invoke(current, null)!;
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            var value = property.GetValue(edited);
            if (!Equals(value, property.GetValue(baseline)))
            {
                property.SetValue(merged, value);
            }
        }

        return merged;
    }
}
