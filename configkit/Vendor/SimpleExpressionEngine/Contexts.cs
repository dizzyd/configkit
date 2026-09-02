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

public sealed class ReflectionContext<TResult, TArguments> : IContext<TResult, TArguments>
{
    private readonly object mSource;

    public ReflectionContext(object source)
    {
        mSource = source;
    }

    public TResult Resolve(string name, params TArguments[] arguments)
    {
        if (arguments.Length != 0)
        {
            (bool resolved, TResult? value) = CallFunction(name, arguments);
            if (!resolved || value == null) throw new InvalidDataException($"Unknown function: '{name}'");
            return value;
        }

        (bool propertyResolved, TResult? propertyValue) = ResolveProperty(name);
        if (propertyResolved && propertyValue != null) return propertyValue;

        (bool fieldResolved, TResult? fieldValue) = ResolveField(name);
        if (fieldResolved && fieldValue != null) return fieldValue;

        (bool functionResolved, TResult? functionValue) = CallFunction(name, arguments);
        if (functionResolved && functionValue != null) return functionValue;

        throw new InvalidDataException($"Unknown function, property or field: '{name}'");
    }

    public bool Resolvable(string name)
    {
        if (mSource.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public) != null) return true;
        if (mSource.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public) != null) return true;
        if (mSource.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public) != null) return true;
        
        return false;
    }

    private (bool resolved, TResult? value) ResolveProperty(string name)
    {
        PropertyInfo? property = mSource.GetType().GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        object? value = property?.GetValue(mSource);
        return (value != null, value == null ? default : (TResult?)value);
    }

    private (bool resolved, TResult? value) ResolveField(string name)
    {
        FieldInfo? field = mSource.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        object? value = field?.GetValue(mSource);
        return (value != null, value == null ? default : (TResult?)value);
    }

    public (bool resolved, TResult? value) CallFunction(string name, params TArguments[] arguments)
    {
        MethodInfo? methodInfo = mSource.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (methodInfo == null) return (false, default);
        return (true, (TResult?)methodInfo.Invoke(mSource, arguments.Select(value => (object?)value).ToArray()));
    }
}

public sealed class StatsContext<TArguments> : IContext<float, TArguments>
{
    private readonly IPlayer mPlayer;

    public StatsContext(IPlayer player)
    {
        mPlayer = player;
    }

    public bool Resolvable(string name)
    {
        return mPlayer.Entity.Stats.Select(entry => entry.Key).Contains(name);
    }
    public float Resolve(string name, params TArguments[] arguments) => mPlayer.Entity.Stats.GetBlended(name);
}