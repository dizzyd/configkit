using System.Globalization;
using System.IO;
using System.Text;

namespace SimpleExpressionEngine;

public class FloatTokenizer : ITokenizer<float>
{
    private readonly TextReader mReader;
    private char mCurrentCharacter;
    private char mPreviousCharacter = '\0';
    private char mBeforePreviousCharacter = '\0';
    private Token mCurrentToken;
    private float mNumber;
    private string mIdentifier = "";

    public FloatTokenizer(TextReader reader)
    {
        mReader = reader;
        NextChar();
        NextToken();
    }

    public Token Token => mCurrentToken;
    public float Value => mNumber;
    public string Identifier => mIdentifier;

    public void NextToken()
    {
        while (char.IsWhiteSpace(mCurrentCharacter))
        {
            NextChar();
        }

        if (ProcessSpecialSymbol()) return;
        if (ProcessDigit()) return;
        _ = ProcessIdentifier();
    }

    private void NextChar()
    {
        int code = mReader.Read();
        char character = code < 0 ? '\0' : (char)code;
        if (!char.IsWhiteSpace(character))
        {
            if (!char.IsWhiteSpace(mPreviousCharacter)) mBeforePreviousCharacter = mPreviousCharacter;
            if (!char.IsWhiteSpace(mCurrentCharacter)) mPreviousCharacter = mCurrentCharacter;
        }
        mCurrentCharacter = character;
    }

    private bool ProcessSpecialSymbol()
    {
        switch (mCurrentCharacter)
        {
            case '\0':
                mCurrentToken = Token.EOF;
                return true;

            case '+':
                if (mPreviousCharacter == 'E' && char.IsDigit(mBeforePreviousCharacter))
                {
                    return false;
                }
                NextChar();
                mCurrentToken = Token.Add;
                return true;

            case '-':
                if (mPreviousCharacter == 'E' && char.IsDigit(mBeforePreviousCharacter))
                {
                    return false;
                }
                NextChar();
                mCurrentToken = Token.Subtract;
                return true;

            case '*':
                NextChar();
                mCurrentToken = Token.Multiply;
                return true;

            case '/':
                NextChar();
                mCurrentToken = Token.Divide;
                return true;

            case '(':
                NextChar();
                mCurrentToken = Token.OpenParenthesis;
                return true;

            case ')':
                NextChar();
                mCurrentToken = Token.CloseParenthesis;
                return true;

            case ',':
                NextChar();
                mCurrentToken = Token.Comma;
                return true;
            default:
                return false;
        }
    }

    private bool ProcessDigit()
    {
        if (
            !char.IsDigit(mCurrentCharacter) && mCurrentCharacter != '.' ||
            !char.IsDigit(mPreviousCharacter) && mCurrentCharacter == 'E' ||
            mCurrentCharacter == '-' && mPreviousCharacter != 'E' ||
            mCurrentCharacter == '+' && mPreviousCharacter != 'E'
        ) return false;

        StringBuilder numberString = new();
        bool haveDecimalPoint = false;
        while (
            char.IsDigit(mCurrentCharacter) || 
            !haveDecimalPoint && mCurrentCharacter == '.' ||
            mCurrentCharacter == 'E' ||
            mPreviousCharacter == 'E' && mCurrentCharacter == '-' ||
            mPreviousCharacter == 'E' && mCurrentCharacter == '+'
        )
        {
            numberString.Append(mCurrentCharacter);
            haveDecimalPoint = mCurrentCharacter == '.';
            NextChar();
        }

        mNumber = float.Parse(numberString.ToString(), CultureInfo.InvariantCulture);
        mCurrentToken = Token.Number;
        return true;
    }

    private bool ProcessIdentifier()
    {
        if (!char.IsLetter(mCurrentCharacter) && mCurrentCharacter != '_') return false;

        StringBuilder identifier = new();

        while (char.IsLetterOrDigit(mCurrentCharacter) || mCurrentCharacter == '_')
        {
            identifier.Append(mCurrentCharacter);
            NextChar();
        }

        mIdentifier = identifier.ToString();
        mCurrentToken = Token.Identifier;
        return true;
    }
}
