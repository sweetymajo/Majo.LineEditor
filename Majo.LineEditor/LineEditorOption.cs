// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace Majo.LineEditor;

/// <summary>
/// Configures a <see cref="LineEditor"/> instance
/// </summary>
public class LineEditorOption
{
    /// <summary>
    /// Gets or sets the prompt displayed before editable input
    /// </summary>
    public string Prompt { get; set; } = "> ";
    
    /// <summary>
    /// Gets or sets the maximum command size in UTF-8 bytes
    /// </summary>
    public int CommandBufferSize { get; set; } = 64 * 1024;
    
    /// <summary>
    /// Gets or sets the maximum number of history entries retained in memory
    /// </summary>
    public int HistoryCount { get; set; } = 100;
    
    /// <summary>
    /// Gets or sets the input polling interval in milliseconds
    /// </summary>
    public int PollInterval { get; set; } = 100;
    
    /// <summary>
    /// Gets or sets whether input may wrap across multiple lines
    /// </summary>
    public bool MultiLine { get; set; } = false;
}
