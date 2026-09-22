using Majo.LineEditor.Backends;
using Majo.LineEditor.Backends.Posix;
using Majo.LineEditor.Backends.Win32;

namespace Majo.LineEditor;

/// <summary>
/// Provides cross-platform interactive line editing
/// </summary>
public class LineEditor : IDisposable
{
    /// <summary>
    /// Indicates whether a LineEditor instance already exists
    /// </summary>
    private static int _instanceExists;
    
    /// <summary>
    /// Platform-specific line editor backend
    /// </summary>
    private readonly ILineEditorBackend _backend;
    
    /// <summary>
    /// Indicates whether this instance has been disposed
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LineEditor"/> class
    /// </summary>
    /// <param name="option">Line editor options or <see langword="null"/> to use the defaults</param>
    public LineEditor(LineEditorOption? option = null)
    {
        if (Interlocked.CompareExchange(ref _instanceExists, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one instance of LineEditor can be created.");
        }

        try
        {
            option ??= new LineEditorOption();
        
            ValidateOption(option);
            
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                throw new InvalidOperationException("Console input or output is redirected.");
            }
        
            _backend = CreateBackend(option);
        }
        catch
        {
            Volatile.Write(ref _instanceExists, 0);
            throw;
        }
        

    }
    
    /// <summary>
    /// Reads a line of input asynchronously
    /// </summary>
    /// <param name="ct">Token used to cancel the read operation</param>
    /// <returns>The result of the read operation</returns>
    /// <exception cref="ObjectDisposedException">This instance has been disposed</exception>
    public ValueTask<ReadResult> ReadLineAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _backend.ReadLineAsync(ct);
    }
    
    /// <summary>
    /// Writes content above the active input area
    /// </summary>
    /// <param name="content">The content to write</param>
    /// <exception cref="ObjectDisposedException">This instance has been disposed</exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/></exception>
    public void WriteAbove(string content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(content);

        _backend.WriteAbove(content);
    }
    
    /// <summary>
    /// Releases the resources used by this instance
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _backend.Dispose();
        }
        finally
        {
            Volatile.Write(ref _instanceExists, 0);
        }
        
        _disposed = true;
    }
    
    /// <summary>
    /// Validates the supplied options
    /// </summary>
    /// <param name="option">The options to validate</param>
    /// <exception cref="ArgumentOutOfRangeException">An option value is outside its valid range</exception>
    private static void ValidateOption(LineEditorOption option)
    {
        ArgumentNullException.ThrowIfNull(option.Prompt);
        
        if (option.CommandBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(option.CommandBufferSize), 
                "CommandBufferSize must be greater than 0.");
        }

        if (option.HistoryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(option.HistoryCount), 
                "HistoryCount must be greater than or equal to 0.");
        }

        if (option.PollInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(option.PollInterval), 
                "PollInterval must be greater than 0.");
        }
    }

    /// <summary>
    /// Creates the backend for the current operating system
    /// </summary>
    /// <param name="option">The line editor options</param>
    /// <returns>The platform-specific backend</returns>
    private static ILineEditorBackend CreateBackend(LineEditorOption option)
    {
        if (OperatingSystem.IsWindows())
        {
            return new Win32LineEditorBackend(option);
        }
        else if (OperatingSystem.IsLinux())
        {
            return new PosixLineEditorBackend(option);
        }
        else
        {
            throw new PlatformNotSupportedException("This platform is not supported yet.");
        }
    }
}
