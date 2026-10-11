namespace CobolCS.Frontend.Parsing;

/// <summary>復旧対象のCOBOL構文エラーです。</summary>
internal sealed class ParseException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ParseException"/> class.</summary>
    /// <param name="message">構文診断。</param>
    internal ParseException(string message)
        : base(message)
    {
    }
}
