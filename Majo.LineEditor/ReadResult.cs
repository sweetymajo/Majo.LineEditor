namespace Majo.LineEditor;

/// <summary>
/// Represents the result of a line-read operation
/// </summary>
/// <param name="Status">The read status</param>
/// <param name="Text">The accepted text if any</param>
public record ReadResult(ReadStatus Status, string? Text);
