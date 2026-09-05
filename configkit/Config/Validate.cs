using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

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
/// </summary>
internal static class Validate
{
    /// <summary>
    /// The first failure among a member's validation attributes, or null when it passes.
    ///
    /// First rather than all: the row has space for one message, and an author who wrote two
    /// constraints wrote the first one to be read first.
    /// </summary>
    internal static string? Check(ConfigSetting setting, SchemaNode node, object owner)
    {
        ValidationAttribute[] rules = RulesFor(node);
        if (rules.Length == 0) return null;

        Type memberType = node.MemberType;
        object? value;

        try
        {
            value = setting.CoercedValue(memberType);
        }
        catch (Exception)
        {
            // Nothing this library can convert is worth reporting as the author's fault.
            return null;
        }

        ValidationContext context = new(owner)
        {
            MemberName = node.Member.Name,
            DisplayName = node.Label ?? node.Member.Name,
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
