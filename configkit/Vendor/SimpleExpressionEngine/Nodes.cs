using System;
using System.Linq;

namespace SimpleExpressionEngine.Nodes;

internal sealed class Ternary<TOutput, TIntermediate, TInput> : INode<TOutput, TIntermediate, TInput>
{
    private readonly INode<TIntermediate, TIntermediate, TInput> mFirstOperand;
    private readonly INode<TIntermediate, TIntermediate, TInput> mSecondOperand;
    private readonly INode<TIntermediate, TIntermediate, TInput> mThirdOperand;
    private readonly Func<TIntermediate, TIntermediate, TIntermediate, TOutput> mOperation;

    public Ternary(INode<TIntermediate, TIntermediate, TInput> firstOperand, INode<TIntermediate, TIntermediate, TInput> secondOperand, INode<TIntermediate, TIntermediate, TInput> thirdOperand, Func<TIntermediate, TIntermediate, TIntermediate, TOutput> operation)
    {
        mFirstOperand = firstOperand;
        mSecondOperand = secondOperand;
        mThirdOperand = thirdOperand;
        mOperation = operation;
    }

    public TOutput Evaluate(IContext<TIntermediate, TInput> context) => mOperation(mFirstOperand.Evaluate(context), mSecondOperand.Evaluate(context), mThirdOperand.Evaluate(context));
}

internal sealed class Binary<TOutput, TIntermediate, TInput> : INode<TOutput, TIntermediate, TInput>
{
    private readonly INode<TIntermediate, TIntermediate, TInput> mLeftOperand;
    private readonly INode<TIntermediate, TIntermediate, TInput> mRightOperand;
    private readonly Func<TIntermediate, TIntermediate, TOutput> mOperation;

    public Binary(INode<TIntermediate, TIntermediate, TInput> leftOperand, INode<TIntermediate, TIntermediate, TInput> rightOperand, Func<TIntermediate, TIntermediate, TOutput> operation)
    {
        mLeftOperand = leftOperand;
        mRightOperand = rightOperand;
        mOperation = operation;
    }

    public TOutput Evaluate(IContext<TIntermediate, TInput> context) => mOperation(mLeftOperand.Evaluate(context), mRightOperand.Evaluate(context));
}

internal sealed class FunctionCall<TOutput, TInput> : INode<TOutput, TOutput, TInput>
{
    private readonly INode<TInput, TOutput, TInput>[] mArguments;
    private readonly string mFunctionName;

    public FunctionCall(string functionName, INode<TInput, TOutput, TInput>[] arguments)
    {
        mFunctionName = functionName;
        mArguments = arguments;
    }

    public TOutput Evaluate(IContext<TOutput, TInput> context) => context.Resolve(mFunctionName, mArguments.Select(argument => argument.Evaluate(context)).ToArray());
}

internal readonly struct Value<TOutput, TIntermediate, TInput> : INode<TOutput, TIntermediate, TInput>
{
    private readonly TOutput mValue;

    public Value(TOutput number)
    {
        mValue = number;
    }

    public TOutput Evaluate(IContext<TIntermediate, TInput> context) => mValue;
}

internal sealed class Unary<TOutput, TIntermediate, TInput> : INode<TOutput, TIntermediate, TInput>
{
    private readonly INode<TIntermediate, TIntermediate, TInput> mOperand;
    private readonly Func<TIntermediate, TOutput> mOperation;

    public Unary(INode<TIntermediate, TIntermediate, TInput> operand, Func<TIntermediate, TOutput> operation)
    {
        mOperand = operand;
        mOperation = operation;
    }

    public TOutput Evaluate(IContext<TIntermediate, TInput> context) => mOperation.Invoke(mOperand.Evaluate(context));
}

internal sealed class Variable<TOutput, TInput> : INode<TOutput, TOutput, TInput>
{
    private readonly string mVariableName;

    public Variable(string variableName)
    {
        mVariableName = variableName;
    }

    public TOutput Evaluate(IContext<TOutput, TInput> context) => context.Resolve(mVariableName);
}
