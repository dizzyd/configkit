using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace ConfigKit;

/// <summary>
/// Runs the DataAnnotations validation attributes an author put on a member, and turns a
/// failure into the message they wrote.
///
/// ConfigKit read <c>[Range]</c> only as a pair of slider bounds, which meant a range said
/// nothing at all wherever a slider was not the control - and an open bound like
/// <c>[Range(0, double.PositiveInfinity)]</c>, which is how "this cannot be negative" is
/// written, is exactly such a case. Typing -5 into it stuck.
///
/// Anything deriving from <see cref="ValidationAttribute"/> works, including an author's own:
/// each is asked through <see cref="ValidationAttribute.GetValidationResult"/> with a real
/// <see cref="ValidationContext"/>, so a custom validator can read the rest of the object and
/// return whatever message it likes.
///
/// A container is checked all the way down. Its own member's attributes run against the
/// whole value, and then every entry that is a class has its fields checked against theirs -
/// a <c>[Range]</c> on a field of the class a dictionary holds constrains that field in every
/// entry, which is what its author meant by writing it. Reported by TheInsanityGod, whose
/// entries were not being checked at all.
/// </summary>
internal static class Validate
{
    /// <summary>
    /// The first failure among a setting's validation attributes, or null when it passes.
    ///
    /// First rather than all: the row has space for one message, and an author who wrote two
    /// constraints wrote the first one to be read first.
    /// </summary>
    internal static string? Check(ConfigSetting setting, SchemaNode node, object owner)
    {
        string? own = CheckRules(node, owner, () => setting.CoercedValue(node.MemberType));
        if (own != null) return own;

        return node.Kind is SchemaKind.Dictionary or SchemaKind.List
            ? CheckContents(setting.Value.Token, node, "")
            : null;
    }

    /// <summary>
    /// The object a value inside a container belongs to, for a validator that wants to read
    /// its siblings. Built from the entry's own JSON, so a custom rule sees the entry as it
    /// is; a blank instance when that cannot be built, and a bare object when nothing can.
    /// </summary>
    internal static object OwnerFor(JToken? token, Type type)
    {
        try
        {
            if (token is JObject && token.ToObject(type) is object built) return built;
        }
        catch (Exception)
        {
            // A sibling the class cannot take - a mistyped enum name, say. The rule being
            // checked is not about that sibling, so a blank instance is the next best owner.
        }

        try
        {
            return Activator.CreateInstance(type) ?? new object();
        }
        catch (Exception)
        {
            return new object();
        }
    }

    /// <summary>
    /// Every entry of a container, and every field of every entry that is a class, against
    /// the rules on their own members. The message names where the failure is - "wolf >
    /// Chance: ..." - because the container's one row has to say which of its entries the
    /// player should open.
    /// </summary>
    private static string? CheckContents(JToken? token, SchemaNode node, string where)
    {
        switch (node.Kind)
        {
            case SchemaKind.Dictionary when token is JObject o && node.ValueNode != null:
                foreach (JProperty property in o.Properties())
                {
                    string? error = CheckEntry(property.Value, node.ValueNode, property.Name, owner: null);
                    if (error != null) return error;
                }
                return null;

            case SchemaKind.List when token is JArray array && node.ElementNode != null:
                for (int index = 0; index < array.Count; index++)
                {
                    string? error = CheckEntry(array[index], node.ElementNode, $"#{index}", owner: null);
                    if (error != null) return error;
                }
                return null;

            case SchemaKind.Object:
            {
                // A null entry is a value, not an object with every field null. There is
                // nothing in it for a rule to look at.
                if (token is not JObject holder) return null;

                object owner = OwnerFor(holder, node.MemberType);

                foreach (SchemaNode child in node.Children)
                {
                    string label = child.Label ?? SchemaBuilder.Humanize(child.Code);

                    // A field the file does not have - one added after the file was written
                    // - deserialises to the class's own initialiser, and that is what the rule
                    // has to see. Reading the absence as null failed a [Required] on every
                    // entry the moment the field was added.
                    string? error = holder.TryGetValue(child.Code, out JToken? value)
                        ? CheckEntry(value, child, label, owner)
                        : CheckAbsent(child, label, owner);
                    if (error != null) return error;
                }
                return null;
            }

            default:
                return null;
        }
    }

    private static string? CheckEntry(JToken? value, SchemaNode node, string where, object? owner)
    {
        string? error = node.Kind switch
        {
            SchemaKind.Scalar => CheckRules(node, owner ?? new object(), () => Coerce(value, node.MemberType)),
            SchemaKind.Object or SchemaKind.Dictionary or SchemaKind.List => CheckContents(value, node, where),
            _ => null
        };

        if (error == null) return null;

        // A leaf names itself with a colon; a level above it is a step on the way there.
        return node.Kind == SchemaKind.Scalar ? $"{where}: {error}" : $"{where} > {error}";
    }

    /// <summary>A field with no value in the file, checked as the value the object holds for it.</summary>
    private static string? CheckAbsent(SchemaNode node, string where, object owner)
    {
        if (node.Kind != SchemaKind.Scalar) return null;

        string? error = CheckRules(node, owner, () => node.Member switch
        {
            PropertyInfo property when property.CanRead => property.GetValue(owner),
            FieldInfo field => field.GetValue(owner),
            _ => throw new InvalidOperationException("no member to read")
        });

        return error == null ? null : $"{where}: {error}";
    }

    /// <summary>A stored token as the member's own type. Null stays null; anything unconvertible throws, and is skipped.</summary>
    private static object? Coerce(JToken? value, Type type)
        => value == null || value.Type == JTokenType.Null ? null : value.ToObject(type);

    private static string? CheckRules(SchemaNode node, object owner, Func<object?> coerce)
    {
        ValidationAttribute[] rules = RulesFor(node);
        if (rules.Length == 0) return null;

        object? value;

        try
        {
            value = coerce();
        }
        catch (Exception)
        {
            // Nothing this library can convert is worth reporting as the author's fault.
            return null;
        }

        string name = node.Member?.Name ?? node.Code;

        ValidationContext context = new(owner)
        {
            MemberName = name,
            DisplayName = node.Label ?? name,
        };

        foreach (ValidationAttribute rule in rules)
        {
            ValidationResult? result;

            try
            {
                result = rule.GetValidationResult(value, context);
            }
            catch (Exception exception)
            {
                // A custom validator is someone else's code running on every keystroke. One
                // that throws is a bug in that mod, and reporting it is more use than either
                // swallowing it or letting it escape into the GUI's event handling.
                return $"{rule.GetType().Name} failed: {exception.Message}";
            }

            if (result != ValidationResult.Success)
            {
                return result?.ErrorMessage is { Length: > 0 } message
                    ? message
                    : $"{context.DisplayName} is not valid";
            }
        }

        return null;
    }

    private static readonly Dictionary<MemberInfo, ValidationAttribute[]> _rules = [];

    /// <summary>
    /// A member's validation attributes, cached: this runs on every edit, and an edit is
    /// every keystroke in a text field.
    /// </summary>
    private static ValidationAttribute[] RulesFor(SchemaNode node)
    {
        // The shape of a container's values is described from a type alone and has no
        // member to carry attributes.
        if (node.Member == null) return [];

        lock (_rules)
        {
            if (_rules.TryGetValue(node.Member, out ValidationAttribute[]? cached)) return cached;

            ValidationAttribute[] rules;

            try
            {
                rules = [.. node.Member.GetCustomAttributes<ValidationAttribute>(inherit: true)
                    // A container's contents are validated as themselves, not through the
                    // attribute on the member holding them - and DataType is a ValidationAttribute
                    // that validates nothing, so running it only costs time.
                    .Where(rule => rule is not DataTypeAttribute)];
            }
            catch (Exception)
            {
                rules = [];
            }

            _rules[node.Member] = rules;
            return rules;
        }
    }

    /// <summary>Drops the attribute cache. For tests, which build many throwaway types.</summary>
    internal static void ClearCache()
    {
        lock (_rules) _rules.Clear();
    }
}
