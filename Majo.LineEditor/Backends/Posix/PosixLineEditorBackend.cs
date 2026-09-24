using System.Runtime.InteropServices;
using Majo.LineEditor.Backends.Posix.Native;

#if NET9_0_OR_GREATER
using LockType = System.Threading.Lock;
#else
using LockType = System.Object;
#endif

namespace Majo.LineEditor.Backends.Posix;

/// <summary>
/// Implements line editing through the native POSIX backend
/// </summary>
internal class PosixLineEditorBackend : ILineEditorBackend
{
    /// <summary>
    /// Native line editor context handle
    /// </summary>
    private IntPtr _handle;
    
    /// <summary>
    /// Indicates whether this backend has been disposed
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Active read task
    /// </summary>
    private Task<ReadResult>? _activeReadTask;
    
    /// <summary>
    /// Input prompt
    /// </summary>
    private readonly string _prompt;
    
    /// <summary>
    /// Input polling interval in milliseconds
    /// </summary>
    private readonly int _pollInterval;
    
    /// <summary>
    /// Cancellation source triggered during disposal
    /// </summary>
    private readonly CancellationTokenSource _disposeCts = new();
    
    /// <summary>
    /// Maximum number of history entries
    /// </summary>
    private readonly int _historyCount;

    /// <summary>
    /// Managed history entries
    /// </summary>
    private readonly List<string> _history = [];
    
    /// <summary>
    /// Lock protecting managed state
    /// </summary>
    private readonly LockType _stateLock = new();

    /// <summary>
    /// Lock serializing native calls
    /// </summary>
    private readonly LockType _nativeLock = new();

    /// <summary>
    /// Initializes a new POSIX line editor backend
    /// </summary>
    /// <param name="option">Line editor options</param>
    public PosixLineEditorBackend(LineEditorOption option)
    {
        _prompt = option.Prompt;
        _pollInterval = option.PollInterval;
        _historyCount = option.HistoryCount;
        
        _handle = LinenoiseNative.Create(option.CommandBufferSize, option.MultiLine ? 1 : 0);
        
        if (_handle == IntPtr.Zero)
        {
            throw CreateNativeException("Failed to create the POSIX line editor.");
        }
    }

    /// <summary>
    /// Reads a line of input asynchronously
    /// </summary>
    /// <param name="ct">Token used to cancel the read operation</param>
    /// <returns>The result of the read operation</returns>
    public ValueTask<ReadResult> ReadLineAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            
            if (_activeReadTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("A line read operation is already in progress.");
            }
            
            _activeReadTask = Task.Run(() => ReadLineCore(ct), CancellationToken.None);
            
            return new ValueTask<ReadResult>(_activeReadTask);
        }
    }

    /// <summary>
    /// Writes content above the active input area
    /// </summary>
    /// <param name="content">Content to write</param>
    public void WriteAbove(string content)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            
            lock (_nativeLock)
            {
                bool newLine = !content.EndsWith('\n');
                
                if (LinenoiseNative.WriteAbove(_handle, content, newLine ? 1 : 0) != 0)
                {
                    throw CreateNativeException("Failed to write above the current line.");
                }
            }
        }
    }

    /// <summary>
    /// Releases resources used by this backend
    /// </summary>
    public void Dispose()
    {
        Task<ReadResult>? activeReadTask;

        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            activeReadTask = _activeReadTask;
            _disposeCts.Cancel();
        }

        try
        {
            try
            {
                activeReadTask?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
                // Ignore cancellation caused by disposal
            }

            lock (_nativeLock)
            {
                if (_handle != IntPtr.Zero)
                {
                    LinenoiseNative.Destroy(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }
        finally
        {
            _disposeCts.Dispose();
        }
    }
    
    /// <summary>
    /// Performs a synchronous line-read operation on a worker thread
    /// </summary>
    /// <param name="ct">Token used to cancel the read operation</param>
    /// <returns>The result of the read operation</returns>
    private ReadResult ReadLineCore(CancellationToken ct = default)
    {
        IntPtr handle;

        lock (_nativeLock)
        {
            // Read the handle while holding the state lock to preserve the disposal invariant
            handle = _handle;
        }
        
        // Link caller cancellation with backend disposal
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        // Use the linked token for the complete read loop
        CancellationToken token = linkedCts.Token;
        
        bool started = false;

        try
        {
            token.ThrowIfCancellationRequested();

            lock (_nativeLock)
            {
                if (LinenoiseNative.PrepareHistory(handle, _historyCount) != 0)
                {
                    throw CreateNativeException("Failed to prepare history in the POSIX line editor.");
                }

                foreach (var entry in _history)
                {
                    if (LinenoiseNative.HistoryAdd(handle, entry) != 0)
                    {
                        throw CreateNativeException("Failed to add history entry in the POSIX line editor.");
                    }
                }
                
                if (LinenoiseNative.Start(handle, _prompt) != 0)
                {
                    throw CreateNativeException("Failed to start the POSIX line editor.");
                }

                started = true;
            }

            while (true)
            {
                token.ThrowIfCancellationRequested();

                int waitResult = LinenoiseNative.WaitForInput(handle, _pollInterval);

                if (waitResult < 0)
                {
                    throw CreateNativeException("Failed to wait for input in the POSIX line editor.");
                }
                
                // Always check resize before feeding input
                // On timeout this also gives us idle resize recovery within PollInterval
                // When input arrives immediately after a resize, handling resize here prevents
                // linenoiseEditFeed() from refreshing with the stale terminal width first
                HandleResize(handle);

                if (waitResult == 0)
                {
                    continue; // Continue polling after a timeout
                }

                int feedResult;
                IntPtr line;

                lock (_nativeLock)
                {
                    feedResult = LinenoiseNative.Feed(handle, out line);
                }

                switch (feedResult)
                {
                    case LinenoiseNative.More:
                        continue; // Continue polling until a complete line is available
                    case LinenoiseNative.Accepted:
                        string text = ReadAndFreeLine(line);
                        AddHistory(text);
                        return new ReadResult(ReadStatus.Accepted, text);
                    case LinenoiseNative.Interrupted:
                        return new ReadResult(ReadStatus.Interrupted, null);
                    case LinenoiseNative.EndOfInput:
                        return new ReadResult(ReadStatus.EndOfInput, null);
                    default:
                        throw CreateNativeException($"Unexpected feed result: {feedResult}");
                }
            }
        }
        finally
        {
            if (started)
            {
                lock (_nativeLock)
                {
                    LinenoiseNative.Stop(handle);
                }
            }
        }
    }
    
    /// <summary>
    /// Handles a pending terminal resize
    /// </summary>
    /// <param name="handle">Native context handle</param>
    private void HandleResize(IntPtr handle)
    {
        int result;

        lock (_nativeLock)
        {
            result = LinenoiseNative.HandleResize(handle);
        }

        if (0 > result)
        {
            throw CreateNativeException("Failed to handle terminal resize in the POSIX line editor.");
        }
    }
    
    /// <summary>
    /// Adds a nonempty unique entry to managed history
    /// </summary>
    /// <param name="text">History text</param>
    private void AddHistory(string text)
    {
        if (_historyCount == 0 || text.Length == 0)
        {
            return;
        }

        if (_history.Count > 0 && _history[^1] == text)
        {
            return;
        }

        _history.Add(text);

        if (_history.Count > _historyCount)
        {
            _history.RemoveAt(0);
        }
    }
    
    /// <summary>
    /// Creates an exception for the most recent native error
    /// </summary>
    /// <param name="message">Error message</param>
    /// <returns>An exception containing the native error code</returns>
    private static IOException CreateNativeException(string message)
    {
        int error = Marshal.GetLastPInvokeError();
        return new IOException($"{message} (Error code: {error})");
    }
    
    /// <summary>
    /// Converts and frees a line returned by the native editor
    /// </summary>
    /// <param name="line">Pointer to the native UTF-8 line</param>
    /// <returns>The managed line content</returns>
    private static string ReadAndFreeLine(IntPtr line)
    {
        if (line == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUTF8(line) ?? string.Empty;
        }
        finally
        {
            LinenoiseNative.FreeLine(line);
        }
    }
}
