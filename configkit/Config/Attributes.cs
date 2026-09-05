using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;

namespace ConfigKit;

/// <summary>
/// Turning a config value into a game attribute tree, without handing the game something it
/// cannot serialise.
///
/// <see cref="TreeAttribute.ToBytes(System.IO.BinaryWriter)"/> walks its entries and calls
/// <c>val.Value.GetAttributeId()</c> on each with no null check, and
/// <see cref="JsonObject.ToAttribute()"/> returns <c>null</c> for a JSON null - a JValue
/// holding null matches none of its type checks and falls off the end. So a config carrying
/// a null anywhere produces a tree that throws a NullReferenceException the moment anything
/// writes it, which for ConfigKit is a client with controlserver editing a setting and the
/// change being sent to the server.
///
/// Reported against WearAndTear, whose part props legitimately hold nulls -
/// <c>"MaintenanceLimit": null</c> means "no limit".
///
/// There is no null in the attribute format, so a null cannot be carried; the key is left out
/// instead, which is the nearest true thing and the one the reader already copes with. The
/// config file keeps the null - only this event copy drops it.
/// </summary>
public static class Attributes
{
    /// <summary>
    /// A value as an attribute, or null when there is nothing serialisable to send. Callers
    /// must not put a null into a tree; there is no way to write one.
    /// </summary>
    public static IAttribute? For(JsonObject value) => WithoutNulls(value.ToAttribute());

    private static IAttribute? WithoutNulls(IAttribute? attribute)
    {
        switch (attribute)
        {
            case null:
                return null;

            case ITreeAttribute tree:
                // Materialised first: the entries are edited while walking them.
                foreach (string key in tree.Select(entry => entry.Key).ToArray())
                {
                    IAttribute? child = WithoutNulls(tree[key]);

                    if (child == null) tree.RemoveAttribute(key);
                    else tree[key] = child;
                }

                return tree;

            case TreeArrayAttribute array:
                array.value = [.. array.value
                    .Select(element => WithoutNulls(element) as TreeAttribute)
                    .Where(element => element != null)
                    .Select(element => element!)];

                return array;

            default:
                return attribute;
        }
    }
}
