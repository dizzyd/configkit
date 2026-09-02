using System;
using System.Collections.Generic;
using System.IO;

namespace SimpleExpressionEngine;

public class MathParser
{
    private readonly ITokenizer<float> mTokenizer;

    public MathParser(ITokenizer<float> tokenizer)
    {
        mTokenizer = tokenizer;
    }

    public INode<float, float, float> ParseExpression()
    {
        INode<float, float, float> expression = ParseAddSubtract();

        if (mTokenizer.Token != Token.EOF)
        {
            throw new SyntaxException("Unexpected characters at end of expression");
        }

        return expression;
    }

    INode<float, float, float> ParseAddSubtract()
    {
        INode<float, float, float> leftOperand = ParseMultiplyDivide();

        while (true)
        {
            Func<float, float, float>? operation = null;
            if (mTokenizer.Token == Token.Add)
            {
                operation = (a, b) => a + b;
            }
            else if (mTokenizer.Token == Token.Subtract)
            {
                operation = (a, b) => a - b;
            }

            if (operation == null) return leftOperand;

            mTokenizer.NextToken();

            INode<float, float, float> rightOperand = ParseMultiplyDivide();

            leftOperand = new Nodes.Binary<float, float, float>(leftOperand, rightOperand, operation);
        }
    }

    INode<float, float, float> ParseMultiplyDivide()
    {
        INode<float, float, float> leftOperand = ParseUnary();

        while (true)
        {
            Func<float, float, float>? operation = null;
            if (mTokenizer.Token == Token.Multiply)
            {
                operation = (a, b) => a * b;
            }
            else if (mTokenizer.Token == Token.Divide)
            {
                operation = (a, b) => a / b;
            }

            if (operation == null) return leftOperand;

            mTokenizer.NextToken();

            INode<float, float, float> rightOperand = ParseUnary();

            leftOperand = new Nodes.Binary<float, float, float>(leftOperand, rightOperand, operation);
        }
    }

    INode<float, float, float> ParseUnary()
    {
        while (true)
        {
            if (mTokenizer.Token == Token.Add)
            {
                mTokenizer.NextToken();
                continue;
            }

            if (mTokenizer.Token == Token.Subtract)
            {
                mTokenizer.NextToken();
                INode<float, float, float> rightOperand = ParseUnary();
                return new Nodes.Unary<float, float, float>(rightOperand, (a) => -a);
            }

            return ParseNumber();
        }
    }

    INode<float, float, float> ParseNumber()
    {
        if (mTokenizer.Token != Token.Identifier && mTokenizer.Token != Token.Number && mTokenizer.Token != Token.OpenParenthesis) throw new SyntaxException($"Unexpected token: {mTokenizer.Token}");

        if (mTokenizer.Token == Token.Number)
        {
            Nodes.Value<float, float, float> node = new(mTokenizer.Value);
            mTokenizer.NextToken();
            return node;
        }

        if (mTokenizer.Token == Token.OpenParenthesis)
        {
            mTokenizer.NextToken();

            INode<float, float, float> node = ParseAddSubtract();

            if (mTokenizer.Token != Token.CloseParenthesis) throw new SyntaxException("Missing close parenthesis");
            mTokenizer.NextToken();

            return node;
        }

        string name = mTokenizer.Identifier;
        mTokenizer.NextToken();

        if (mTokenizer.Token != Token.OpenParenthesis)
        {
            return new Nodes.Variable<float, float>(name);
        }

        mTokenizer.NextToken();

        List<INode<float, float, float>> arguments = new();
        while (true)
        {
            arguments.Add(ParseAddSubtract());

            if (mTokenizer.Token == Token.Comma)
            {
                mTokenizer.NextToken();
                continue;
            }

            break;
        }


        if (mTokenizer.Token != Token.CloseParenthesis) throw new SyntaxException("Missing close parenthesis");
        mTokenizer.NextToken();

        return new Nodes.FunctionCall<float, float>(name, arguments.ToArray());
    }


    #region Convenience Helpers

    public static INode<float, float, float> Parse(string str)
    {
        return Parse(new FloatTokenizer(new StringReader(str)));
    }

    public static INode<float, float, float> Parse(FloatTokenizer tokenizer)
    {
        MathParser parser = new(tokenizer);
        return parser.ParseExpression();
    }

    #endregion
}
