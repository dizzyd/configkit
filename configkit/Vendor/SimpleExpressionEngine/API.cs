using System;

namespace SimpleExpressionEngine;

public interface IContext<out TResult, in TArguments>
{
    TResult Resolve(string name, params TArguments[] arguments);
    bool Resolvable(string name);
}

public interface INode<TOutput, TIntermediate, TInput>
{
    TOutput Evaluate(IContext<TIntermediate, TInput> context);
}

public interface ITokenizer<out TValue>
{
    void NextToken();

    Token Token { get; }
    TValue Value { get; }
    string Identifier { get; }
}

public enum Token
{
    EOF,
    Add,
    Subtract,
    Multiply,
    Divide,
    OpenParenthesis,
    CloseParenthesis,
    Comma,
    Identifier,
    Number,
}

public class SyntaxException : Exception
{
    public SyntaxException(string message)
        : base(message)
    {
    }
}