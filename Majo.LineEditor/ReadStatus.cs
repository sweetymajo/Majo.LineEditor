namespace Majo.LineEditor;

/// <summary>
/// Describes how a line-read operation completed
/// </summary>
public enum ReadStatus
{
    /// <summary>
    /// The user accepted the input by pressing Enter
    /// </summary>
    Accepted,
    
    /// <summary>
    /// The user interrupted the read by pressing Ctrl+C
    /// </summary>
    Interrupted,
    
    /// <summary>
    /// The user requested end of input such as by pressing Ctrl+D on an empty line
    /// </summary>
    EndOfInput
}
