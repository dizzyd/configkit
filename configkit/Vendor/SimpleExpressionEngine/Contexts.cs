using SimpleExpressionEngine.Nodes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace SimpleExpressionEngine;

public sealed class CombinedContext<TResult, TArguments> : IContext<TResult, TArguments>
{
    private readonly IEnumerable<IContext<TResult, TArguments>> mContexts;

    public CombinedContext(IEnumerable<IContext<TResult, TArguments>> contexts)
    {
        mContexts = contexts;
    }

    public bool Resolvable(string name)
    {
        foreach (IContext<TResult, TArguments> context in mContexts)
        {
            if (context.Resolvable(name)) return true;
        }

        return false;
    }
    public TResult Resolve(string name, params TArguments[] arguments)
    {
        foreach (IContext<TResult, TArguments> context in mContexts)
        {
            if (context.Resolvable(name)) return context.Resolve(name, arguments);
        }

        throw new InvalidDataException($"Unresolvable: '{name}'");
    }
}

public sealed class MathContext : IContext<float, float>
{
    private const float cEpsilon = 1E-15f;

    public MathContext()
    {
    }

    public bool Resolvable(string name)
    {
        return name switch
        {
            "pi" => true,
            "e" => true,
            "sin" => true,
            "cos" => true,
            "abs" => true,
            "sqrt" => true,
            "ceiling" => true,
            "floor" => true,
            "exp" => true,
            "log" => true,
            "round" => true,
            "sign" => true,
            "clamp" => true,
            "max" => true,
            "min" => true,
            "greater" => true,
            "lesser" => true,
            "equal" => true,
            _ => false
        };
    }

    public float Resolve(string name, params float[] arguments)
    {
        return name switch
        {
            "pi" => MathF.PI,
            "e" => MathF.E,
            "sin" => MathF.Sin(arguments[0]),
            "cos" => MathF.Cos(arguments[0]),
            "abs" => MathF.Abs(arguments[0]),
            "sqrt" => MathF.Sqrt(arguments[0]),
            "ceiling" => MathF.Ceiling(arguments[0]),
            "floor" => MathF.Floor(arguments[0]),
            "exp" => MathF.Exp(arguments[0]),
            "log" => MathF.Log(arguments[0]),
            "round" => MathF.Round(arguments[0]),
            "sign" => MathF.Sign(arguments[0]),
            "clamp" => Math.Clamp(arguments[0], arguments[1], arguments[2]),
            "max" => MathF.Max(arguments[0], arguments[1]),
            "min" => MathF.Min(arguments[0], arguments[1]),
            "greater" => arguments[0] > arguments[1] ? arguments[2] : arguments[3],
            "lesser" => arguments[0] < arguments[1] ? arguments[2] : arguments[3],
            "equal" => MathF.Abs(arguments[0] - arguments[1]) < MathF.Max(cEpsilon, cEpsilon * MathF.Min(arguments[0], arguments[1])) ? arguments[2] : arguments[3],
            "notequal" => MathF.Abs(arguments[0] - arguments[1]) > MathF.Max(cEpsilon, cEpsilon * MathF.Min(arguments[0], arguments[1])) ? arguments[2] : arguments[3],
            _ => throw new InvalidDataException($"Unknown function: '{name}'")
        };
    }
}

// ReflectionContext<TResult,TArguments> and StatsContext<TArguments> were removed from this
// vendored copy. Neither was reachable - ConfigKit wires up only MathContext,
// BooleanMathContext, NumberSettingsContext, ValueContext and CombinedContext - and
// ReflectionContext resolved and invoked members by string name with BindingFlags.NonPublic
// on an arbitrary object. That is a useful primitive to no one but an attacker, and it sat
// in the one namespace the build's containment check has to allow through.
