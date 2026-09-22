namespace Majo.LineEditor.Backends;

/// <summary>
/// Defines the contract implemented by platform-specific line editor backends
/// </summary>
public interface ILineEditorBackend : IDisposable
{
    /// <summary>
    /// Reads a line of input asynchronously
    /// </summary>
    /// <param name="ct">Token used to cancel the read operation</param>
    /// <returns>The result of the read operation</returns>
    ValueTask<ReadResult> ReadLineAsync(CancellationToken ct = default);

    /// <summary>
    /// Writes content above the active input area
    /// </summary>
    /// <param name="content">The content to write</param>
    void WriteAbove(string content);
}
