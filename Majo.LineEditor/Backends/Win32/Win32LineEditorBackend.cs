using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Wcwidth;

namespace Majo.LineEditor.Backends.Win32;

/// <summary>
/// Implements line editing through the Win32 console API
/// </summary>
[SupportedOSPlatform("windows")]
internal class Win32LineEditorBackend : ILineEditorBackend
{
    /// <summary>
    /// Indicates whether this backend has been disposed
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Active read task
    /// </summary>
    private Task<ReadResult>? _activeReadTask;
    
    /// <summary>
    /// UTF-16 index of the logical cursor
    /// </summary>
    private int _cursorIndex;
    
    /// <summary>
    /// Origin of the rendered input area
    /// </summary>
    private Win32ConsoleNative.Coord _origin;
    
    /// <summary>
    /// End coordinate of the rendered input area
    /// </summary>
    private Win32ConsoleNative.Coord _renderedEnd;

    /// <summary>
    /// Indicates whether an editing session is active
    /// </summary>
    private bool _editing;
    
    /// <summary>
    /// Screen buffer width used by the most recent successful render
    /// </summary>
    private short _renderedBufferWidth;
    
    /// <summary>
    /// Screen buffer height used by the most recent successful render
    /// </summary>
    private short _renderedBufferHeight;
    
    /// <summary>
    /// Indicates whether a resize is waiting to be processed
    /// </summary>
    private bool _resizePending;

    /// <summary>
    /// Tick count of the most recent resize event
    /// </summary>
    private long _lastResizeTick;
    
    /// <summary>
    /// Generation number of observed resize activity
    /// </summary>
    private long _resizeVersion;
    
    /// <summary>
    /// Number of console cells occupied from the origin to the rendered end
    /// </summary>
    private long _renderedCellLength;
    
    /// <summary>
    /// Number of console cells occupied from the origin to the logical cursor
    /// </summary>
    private long _renderedCursorCellOffset;
    
    /// <summary>
    /// Input prompt
    /// </summary>
    private readonly string _prompt;
    
    /// <summary>
    /// Maximum command size in UTF-8 bytes
    /// </summary>
    private readonly int _commandBufferSize;
    
    /// <summary>
    /// Input polling interval in milliseconds
    /// </summary>
    private readonly int _pollInterval;
    
    /// <summary>
    /// Indicates whether multiline mode is enabled
    /// </summary>
    private readonly bool _multiLine;

    /// <summary>
    /// Console input handle
    /// </summary>
    private readonly IntPtr _inputHandle;
    
    /// <summary>
    /// Console output handle
    /// </summary>
    private readonly IntPtr _outputHandle;
    
    /// <summary>
    /// Cancellation source triggered during disposal
    /// </summary>
    private readonly CancellationTokenSource _disposeCts = new();
    
    /// <summary>
    /// Editable input buffer
    /// </summary>
    private readonly StringBuilder _buffer = new();
    
    /// <summary>
    /// Maximum number of history entries
    /// </summary>
    private readonly int _historyCount;
    
    /// <summary>
    /// Retained command history
    /// </summary>
    private readonly List<string> _history = [];
    
    /// <summary>
    /// Lock protecting managed state
    /// </summary>
    private readonly Lock _stateLock = new();

    /// <summary>
    /// Lock serializing console access
    /// </summary>
    private readonly Lock _consoleLock = new();
    
    /// <summary>
    /// Time required for resize activity to settle in milliseconds
    /// </summary>
    private const int ResizeSettle = 100;
    
    /// <summary>
    /// Initializes a new Win32 line editor backend
    /// </summary>
    /// <param name="option">Line editor options</param>
    public Win32LineEditorBackend(LineEditorOption option)
    {
        _prompt = option.Prompt;
        _commandBufferSize = option.CommandBufferSize;
        _pollInterval = option.PollInterval;
        _historyCount = option.HistoryCount;
        _multiLine = option.MultiLine;
        
        _inputHandle = GetConsoleHandle(Win32ConsoleNative.StdInputHandle, "standard input");
        _outputHandle = GetConsoleHandle(Win32ConsoleNative.StdOutputHandle, "standard output");
        
        if (!Win32ConsoleNative.GetConsoleMode(_inputHandle, out _))
        {
            throw CreateNativeException("Failed to get console input mode.");
        }
        
        if (!Win32ConsoleNative.GetConsoleMode(_outputHandle, out _))
        {
            throw CreateNativeException("Failed to get console output mode.");
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
                throw new InvalidOperationException("A read operation is already in progress.");
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
            
            bool newLine = !content.EndsWith('\n');

            while (true)
            {
                lock (_consoleLock)
                {
                    if (!_editing)
                    {
                        WriteConsole(newLine ? string.Concat(content, Environment.NewLine) : content);

                        return;
                    }
                    
                    BeginSynchronizedOutput();

                    try
                    {
                        WriteAboveFastPathResult fastPathResult = TryWriteAboveWithoutRedraw(content);

                        switch (fastPathResult)
                        {
                            case WriteAboveFastPathResult.Success:
                                return;
                            case WriteAboveFastPathResult.Unsupported:
                                // Fall back to clearing and rendering only when the content cannot use the fast path
                                SynchronizeRenderGeometry();
                                ClearRenderedInput();

                                WriteConsole(newLine ? string.Concat(content, Environment.NewLine) : content);

                                Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();

                                _origin = info.CursorPosition;
                                _renderedEnd = _origin;
                                _renderedBufferWidth = info.Size.X;
                                _renderedBufferHeight = info.Size.Y;
                                _renderedCellLength = 0;
                                _renderedCursorCellOffset = 0;

                                RenderLine();

                                return;
                            case WriteAboveFastPathResult.Retry:
                                // A resize is transient and must be retried
                                if (_multiLine)
                                {
                                    SynchronizeRenderGeometry();
                                }
                                else
                                {
                                    TryApplyPendingResize();
                                }

                                break;
                        }
                    }
                    finally
                    {
                        EndSynchronizedOutput();
                    }
                }
                
                Thread.Sleep(10);
            }
        }
    }
    
    /// <summary>
    /// Releases resources used by this backend
    /// </summary>
    public void Dispose()
    {
        Task<ReadResult>? readTask;
        
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposeCts.Cancel();
            readTask = _activeReadTask;
        }
        
        if (readTask is not null)
        {
            try
            {
                readTask.GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore cancellation exception
            }
        }
        
        _disposeCts.Dispose();
    }

    /// <summary>
    /// Gets a standard console handle
    /// </summary>
    /// <param name="stdHandle">Standard device identifier</param>
    /// <param name="name">Device name used in error messages</param>
    /// <returns>The console handle</returns>
    /// <exception cref="IOException">The handle could not be acquired</exception>
    private static IntPtr GetConsoleHandle(int stdHandle, string name)
    {
        IntPtr handle = Win32ConsoleNative.GetStdHandle(stdHandle);

        if (handle == IntPtr.Zero || handle == Win32ConsoleNative.InvalidHandleValue)
        {
            throw CreateNativeException($"Failed to get {name} handle.");
        }
        
        return handle;
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
    /// Performs a synchronous line-read operation on a worker thread
    /// </summary>
    /// <param name="ct">Token used to cancel the read operation</param>
    /// <returns>The result of the read operation</returns>
    private ReadResult ReadLineCore(CancellationToken ct = default)
    {
        // Link caller cancellation with backend disposal
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        // Use the linked token for the complete read loop
        CancellationToken token = linkedCts.Token;

        // Preserve the original console modes for restoration
        uint originalInputMode = 0;
        uint originalOutputMode = 0;
        bool inputModeChanged = false;
        bool outputModeChanged = false;

        try
        {
            token.ThrowIfCancellationRequested();
            
            int utf8ByteCount = 0;
            char? pendingHighSurrogate = null;

            int historyIndex = -1;
            string historyDraft = string.Empty;
            
            List<string> historySession = [.. _history];
            
            lock (_consoleLock)
            {
                _buffer.Clear();
                _cursorIndex = 0;
                
                if (!Win32ConsoleNative.GetConsoleMode(_inputHandle, out originalInputMode))
                {
                    throw CreateNativeException("Failed to get console input mode.");
                }

                uint inputMode = originalInputMode;
                inputMode &= ~Win32ConsoleNative.EnableProcessedInput;
                inputMode &= ~Win32ConsoleNative.EnableLineInput;
                inputMode &= ~Win32ConsoleNative.EnableEchoInput;
                inputMode &= ~Win32ConsoleNative.EnableMouseInput;
                inputMode &= ~Win32ConsoleNative.EnableQuickEditMode;
                inputMode &= ~Win32ConsoleNative.EnableVirtualTerminalInput;
                inputMode |= Win32ConsoleNative.EnableExtendedFlags;
                inputMode |= Win32ConsoleNative.EnableWindowInput;

                if (!Win32ConsoleNative.SetConsoleMode(_inputHandle, inputMode))
                {
                    throw CreateNativeException("Failed to set console input mode.");
                }

                inputModeChanged = true;
                
                if (!Win32ConsoleNative.GetConsoleMode(_outputHandle, out originalOutputMode))
                {
                    throw CreateNativeException("Failed to get console output mode.");
                }
                
                uint outputMode = originalOutputMode;
                outputMode |= Win32ConsoleNative.EnableProcessedOutput;
                outputMode |= Win32ConsoleNative.EnableVirtualTerminalProcessing;

                if (outputMode != originalOutputMode)
                {
                    if (!Win32ConsoleNative.SetConsoleMode(_outputHandle, outputMode))
                    {
                        throw CreateNativeException("Failed to set console output mode.");
                    }

                    outputModeChanged = true;
                }

                Win32ConsoleNative.ConsoleScreenBufferInfo screenInfo = GetConsoleScreenBufferInfo();
                _origin = screenInfo.CursorPosition;
                _renderedEnd = _origin;
                _renderedBufferWidth = screenInfo.Size.X;
                _renderedBufferHeight = screenInfo.Size.Y;
                _renderedCellLength = 0;
                _renderedCursorCellOffset = 0;
                
                RenderLine();
                
                _editing = true;
            }

            while (true)
            {
                token.ThrowIfCancellationRequested();

                uint waitResult = Win32ConsoleNative.WaitForSingleObject(_inputHandle, (uint)_pollInterval);

                if (waitResult == Win32ConsoleNative.WaitTimeout)
                {
                    lock (_consoleLock)
                    {
                        TryApplyPendingResize();
                    }
                    
                    continue;
                }

                if (waitResult == Win32ConsoleNative.WaitFailed)
                {
                    throw CreateNativeException("Failed to wait for console input.");
                }

                if (waitResult != Win32ConsoleNative.WaitObject0)
                {
                    throw new IOException($"Unexpected wait result: {waitResult}");
                }

                if (!Win32ConsoleNative.ReadConsoleInput(_inputHandle, out Win32ConsoleNative.InputRecord input,
                        1, out uint eventsRead))
                {
                    throw CreateNativeException("Failed to read console input.");
                }

                if (0 == eventsRead)
                {
                    continue;
                }
                
                if (input.EventType == Win32ConsoleNative.WindowBufferSizeEventType)
                {
                    // Record the resize before waiting because WriteAbove may hold the console lock
                    // Recording afterward would hide active resize activity from the fast path
                    RecordResizeActivity();
                    
                    lock (_consoleLock)
                    {
                        HandleResize();
                    }
                    
                    continue;
                }
                
                // Process only key-down events
                if (input.EventType != Win32ConsoleNative.KeyEventType || 0 == input.KeyEvent.KeyDown)
                {
                    continue;
                }

                Win32ConsoleNative.KeyEventRecord key = input.KeyEvent;
                
                int repeatCount = Math.Max((ushort)1, key.RepeatCount);

                bool ctrlPressed = (key.ControlKeyState & Win32ConsoleNative.CtrlPressed) != 0;

                // Handle Ctrl+C as an interrupted read
                if (ctrlPressed && key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyC)
                {
                    return new ReadResult(ReadStatus.Interrupted, null);
                }

                // Handle Ctrl+D as end of input or forward delete
                if (ctrlPressed && key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyD)
                {
                    pendingHighSurrogate = null;

                    lock (_consoleLock)
                    {
                        if (0 == _buffer.Length)
                        {
                            return new ReadResult(ReadStatus.EndOfInput, null);
                        }
                    
                        bool changed = false;
                    
                        for (int i = 0; i < repeatCount; i++)
                        {
                            // Delete the text element under the cursor
                            changed |= Delete(_buffer, _cursorIndex);
                        }

                        if (changed)
                        {
                            utf8ByteCount = Encoding.UTF8.GetByteCount(_buffer.ToString());
                            RenderLine();
                        }
                    }
                    
                    continue;
                }

                // Accept the current input when Enter is pressed
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyReturn)
                {
                    string text;

                    lock (_consoleLock)
                    {
                        text = _buffer.ToString();
                    }
                    
                    AddHistory(text);
                    
                    return new ReadResult(ReadStatus.Accepted, text);
                }
                
                // Navigate to an older history entry
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyUp)
                {
                    pendingHighSurrogate = null;
                    
                    if (historySession.Count == 0)
                    {
                        continue;
                    }

                    lock (_consoleLock)
                    {
                        if (0 <= historyIndex)
                        {
                            historySession[historyIndex] = _buffer.ToString();
                        }
                        
                        for (int i = 0; i < repeatCount; i++)
                        {
                            if (historyIndex < 0)
                            {
                                historyDraft = _buffer.ToString();
                                historyIndex = historySession.Count - 1;
                            }
                            else if (historyIndex > 0)
                            {
                                historyIndex--;
                            }
                            else
                            {
                                break;
                            }
                        }
                    
                        SetBuffer(_buffer, historySession[historyIndex], out _cursorIndex, out utf8ByteCount);
                        RenderLine();
                    }
                    
                    continue;
                }

                // Navigate to a newer history entry
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyDown)
                {
                    pendingHighSurrogate = null;
                    
                    if (historyIndex < 0)
                    {
                        continue;
                    }

                    lock (_consoleLock)
                    {
                        historySession[historyIndex] = _buffer.ToString();
                        
                        bool restoreDraft = false;

                        for (int i = 0; i < repeatCount; i++)
                        {
                            if (historyIndex < historySession.Count - 1)
                            {
                                historyIndex++;
                            }
                            else
                            {
                                historyIndex = -1;
                                restoreDraft = true;
                                break;
                            }
                        }

                        string text = restoreDraft ? historyDraft : historySession[historyIndex];
                        SetBuffer(_buffer, text, out _cursorIndex, out utf8ByteCount);
                        RenderLine();
                    }
                    
                    continue;
                }
                
                // Move to the previous text element
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyLeft)
                {
                    pendingHighSurrogate = null;

                    lock (_consoleLock)
                    {
                        string text = _buffer.ToString();
                        
                        for (int i = 0; i < repeatCount; i++)
                        {
                            _cursorIndex = GetPreviousTextElementIndex(text, _cursorIndex);
                        }
                    
                        RenderLine();
                    }
                    
                    continue;
                }
                
                // Move to the next text element
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyRight)
                {
                    pendingHighSurrogate = null;

                    lock (_consoleLock)
                    {
                        string text = _buffer.ToString();
                    
                        for (int i = 0; i < repeatCount; i++)
                        {
                            _cursorIndex = GetNextTextElementIndex(text, _cursorIndex);
                        }
                    
                        RenderLine();
                    }

                    
                    continue;
                }

                // Move to the beginning of the input
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyHome)
                {
                    pendingHighSurrogate = null;
                    
                    lock (_consoleLock)
                    {
                        _cursorIndex = 0;
                        RenderLine();
                    }
                    
                    continue;
                }
                
                // Move to the end of the input
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyEnd)
                {
                    pendingHighSurrogate = null;
                    
                    lock (_consoleLock)
                    {
                        _cursorIndex = _buffer.Length;
                        RenderLine();
                    }
                    
                    continue;
                }
                
                // Handle Backspace
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyBackspace)
                {
                    pendingHighSurrogate = null;

                    lock (_consoleLock)
                    {
                        bool changed = false;

                        for (int i = 0; i < repeatCount; i++)
                        {
                            // Remove the text element before the cursor
                            changed |= Backspace(_buffer, ref _cursorIndex);
                        }

                        if (changed)
                        {
                            utf8ByteCount = Encoding.UTF8.GetByteCount(_buffer.ToString());
                            RenderLine();
                        }
                    }
                    
                    continue;
                }
                
                // Handle Delete
                if (key.VirtualKeyCode == Win32ConsoleNative.VirtualKeyDelete)
                {
                    pendingHighSurrogate = null;
                    lock (_consoleLock)
                    {
                        bool changed = false;

                        for (int i = 0; i < repeatCount; i++)
                        {
                            // Delete the text element under the cursor
                            changed |= Delete(_buffer, _cursorIndex);
                        }

                        if (changed)
                        {
                            utf8ByteCount = Encoding.UTF8.GetByteCount(_buffer.ToString());
                            RenderLine();
                        }
                    }
                    
                    continue;
                }
                
                char value = key.UnicodeChar;

                if ('\0' == value || char.IsControl(value))
                {
                    pendingHighSurrogate = null;
                    continue;
                }

                if (char.IsHighSurrogate(value))
                {
                    pendingHighSurrogate = value;
                    continue;
                }

                string inputText;

                if (char.IsLowSurrogate(value))
                {
                    if (pendingHighSurrogate is not { } highSurrogate)
                    {
                        continue;
                    }

                    inputText = string.Concat(highSurrogate, value);
                    pendingHighSurrogate = null;
                }
                else
                {
                    pendingHighSurrogate = null;
                    inputText = value.ToString();
                }

                int byteCount = Encoding.UTF8.GetByteCount(inputText);
                
                lock (_consoleLock)
                {
                    bool inserted = false;

                    for (int i = 0; i < repeatCount; i++)
                    {
                        if (utf8ByteCount + byteCount > _commandBufferSize)
                        {
                            break;
                        }

                        _buffer.Insert(_cursorIndex, inputText);
                        _cursorIndex += inputText.Length;
                        utf8ByteCount += byteCount;
                        inserted = true;
                    }

                    if (inserted)
                    {
                        RenderLine();
                    }
                }
            }
        }
        finally
        {
            lock (_consoleLock)
            {
                bool wasEditing = _editing;

                try
                {
                    if (wasEditing)
                    {
                        SynchronizeRenderGeometry();
                        SetCursorPosition(_renderedEnd);
                    }

                    if (inputModeChanged && !Win32ConsoleNative.SetConsoleMode(_inputHandle, originalInputMode))
                    {
                        throw CreateNativeException("Failed to restore the Windows console input mode.");
                    }

                    if (wasEditing)
                    {
                        WriteConsole(Environment.NewLine);
                    }
                    
                    if (outputModeChanged && !Win32ConsoleNative.SetConsoleMode(_outputHandle, originalOutputMode))
                    {
                        throw CreateNativeException("Failed to restore the Windows console output mode.");
                    }
                }
                finally
                {
                    _editing = false;
                    _resizePending = false;
                    _renderedBufferWidth = 0;
                    _renderedBufferHeight = 0;
                    _renderedCellLength = 0;
                    _renderedCursorCellOffset = 0;
                }
            }
        }
    }

    /// <summary>
    /// Writes text to the console output buffer
    /// </summary>
    /// <param name="text">Text to write</param>
    private void WriteConsole(string text)
    {
        if (0 == text.Length)
        {
            return;
        }
        
        if (!Win32ConsoleNative.WriteConsole(_outputHandle, text, (uint)text.Length, 
                out uint written, IntPtr.Zero))
        {
            throw CreateNativeException("Failed to write to console.");
        }
        
        if (written != text.Length)
        {
            throw new IOException(
                $"Failed to write all characters to console. Written: {written}, Expected: {text.Length}");
        }
    }

    /// <summary>
    /// Renders the current input using the configured mode
    /// </summary>
    private void RenderLine()
    {
        if (_multiLine)
        {
            RenderMultiLine();
        }
        else
        {
            RenderSingleLine();
        }
    }
    
    /// <summary>
    /// Renders the current input in multiline mode
    /// </summary>
    private void RenderMultiLine()
    {
        SynchronizeRenderGeometry();
        EnsureRenderSpace();
        
        string text = _buffer.ToString();
        
        SetCursorPosition(_origin);
        WriteConsole(_prompt);

        // Write the portion before the cursor when it is nonempty
        if (0 < _cursorIndex)
        {
            WriteConsole(text[.._cursorIndex]);
        }

        Win32ConsoleNative.ConsoleScreenBufferInfo cursorInfo = GetConsoleScreenBufferInfo();
        Win32ConsoleNative.Coord cursorPos = cursorInfo.CursorPosition;

        if (_cursorIndex < text.Length)
        {
            WriteConsole(text[_cursorIndex..]);
        }
        
        Win32ConsoleNative.ConsoleScreenBufferInfo endInfo = GetConsoleScreenBufferInfo();

        if (cursorInfo.Size.X != endInfo.Size.X)
        {
            SetCursorPosition(cursorPos);
            return;
        }

        short bufferWidth = endInfo.Size.X;
        Win32ConsoleNative.Coord currentEnd = endInfo.CursorPosition;

        if (_renderedBufferWidth == bufferWidth)
        {
            ClearRange(currentEnd, _renderedEnd, bufferWidth);
        }
        
        SetCursorPosition(cursorPos);
        
        long originIndex = GetLinearPosition(_origin, bufferWidth);
        
        _renderedCursorCellOffset = Math.Max(0, GetLinearPosition(cursorPos, bufferWidth) - originIndex);
        _renderedCellLength = Math.Max(0, GetLinearPosition(currentEnd, bufferWidth) - originIndex);
        
        _renderedEnd = currentEnd;
        _renderedBufferWidth = bufferWidth;
        _renderedBufferHeight = endInfo.Size.Y;
    }
    
    /// <summary>
    /// Renders the current input in single-line viewport mode
    /// </summary>
    private void RenderSingleLine()
    {
        SynchronizeRenderGeometry();

        Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();
        string text = _buffer.ToString();

        int promptWidth = GetDisplayWidth(_prompt);
        int maxTextWidth = Math.Max(0, info.Size.X - _origin.X - promptWidth - 1);

        GetSingleLineView(text, _cursorIndex, maxTextWidth, out int start, out int end, out int cursorWidth);

        string visibleText = text[start..end];
        int visibleWidth = GetDisplayWidth(visibleText);
        int renderedCellLength = promptWidth + visibleWidth;
        
        Win32ConsoleNative.Coord oldRenderedEnd = _renderedEnd;

        SetCursorPosition(_origin);
        WriteConsole(string.Concat(_prompt, visibleText));

        Win32ConsoleNative.ConsoleScreenBufferInfo endInfo = GetConsoleScreenBufferInfo();
        Win32ConsoleNative.Coord currentEnd = endInfo.CursorPosition;
        
        // Avoid positioning the logical cursor with stale dimensions when another resize occurs during rendering
        // The physical cursor remains at the end of the newly written content
        // Record the complete rendered length so later recovery can infer the origin
        if (info.Size.X != endInfo.Size.X || info.Size.Y != endInfo.Size.Y)
        {
            _renderedCursorCellOffset = renderedCellLength;
            _renderedCellLength = renderedCellLength;
            _renderedEnd = currentEnd;
            _renderedBufferWidth = endInfo.Size.X;
            _renderedBufferHeight = endInfo.Size.Y;
            
            MarkResizePending();
            return;
        }

        // Clear the trailing portion left by a longer previous render
        if (_renderedBufferWidth == info.Size.X)
        {
            ClearRange(currentEnd, oldRenderedEnd, info.Size.X);
        }

        Win32ConsoleNative.Coord cursorPosition = new()
        {
            X = (short)(_origin.X + promptWidth + cursorWidth),
            Y = _origin.Y
        };

        if (!TrySetCursorPosition(cursorPosition))
        {
            Win32ConsoleNative.ConsoleScreenBufferInfo latestInfo = GetConsoleScreenBufferInfo();
            
            // A failed SetCursorPosition leaves the cursor at the rendered end
            // Preserve that geometry so the next resize recovery can still infer the origin
            _renderedEnd = latestInfo.CursorPosition;
            _renderedBufferWidth = latestInfo.Size.X;
            _renderedBufferHeight = latestInfo.Size.Y;
            _renderedCursorCellOffset = renderedCellLength;
            _renderedCellLength = renderedCellLength;
            
            MarkResizePending();
            return;
        }

        _renderedEnd = currentEnd;
        _renderedBufferWidth = info.Size.X;
        _renderedBufferHeight = info.Size.Y;
        _renderedCursorCellOffset = promptWidth + cursorWidth;
        _renderedCellLength = promptWidth + GetDisplayWidth(text[start..end]);
    }
    
    /// <summary>
    /// Records and applies a console window resize when it settles
    /// </summary>
    private void HandleResize()
    {
        _lastResizeTick = Environment.TickCount64;
        
        if (_multiLine)
        {
            SynchronizeRenderGeometry();
            return;
        }

        _resizePending = true;
    }
    
    /// <summary>
    /// Records one occurrence of resize activity
    /// </summary>
    private void RecordResizeActivity()
    {
        Interlocked.Exchange(ref _lastResizeTick, Environment.TickCount64);
        Interlocked.Increment(ref _resizeVersion);
    }
    
    /// <summary>
    /// Marks a resize as pending
    /// </summary>
    private void MarkResizePending()
    {
        _resizePending = true;
        RecordResizeActivity();
    }
    
    /// <summary>
    /// Attempts to rebuild rendered input after a single-line resize
    /// </summary>
    /// <returns><see langword="true"/> when rebuilding succeeds</returns>
    private bool RebuildSingleLineAfterResize()
    {
        Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();
        
        long capacity = (long)info.Size.X * info.Size.Y;
        
        if (0 >= capacity)
        {
            return false;
        }
        
        // Windows Terminal may have reflowed the old input after the resize
        // The physical cursor still represents the logical cursor position
        // The recorded cursor cell offset therefore reveals the theoretical new origin
        long cursorIndex = GetLinearPosition(info.CursorPosition, info.Size.X);
        long oldOriginIndex = cursorIndex - _renderedCursorCellOffset;
        long oldEndIndex = oldOriginIndex + _renderedCellLength;
        
        // A smaller screen buffer may have clipped part of the old input
        // Clear only the portion that still intersects the current buffer
        long clearStart = Math.Clamp(oldOriginIndex, 0, capacity);
        long clearEnd = Math.Clamp(oldEndIndex, 0, capacity);
        
        if (clearStart < clearEnd)
        {
            ClearLinearRange(clearStart, clearEnd, info.Size.X);
        }
        
        // Abort this render if another resize invalidates the calculated geometry during clearing
        Win32ConsoleNative.ConsoleScreenBufferInfo currentInfo = GetConsoleScreenBufferInfo();
        
        if (currentInfo.Size.X != info.Size.X || currentInfo.Size.Y != info.Size.Y)
        {
            return false;
        }
        
        // Preserve the old physical origin when it still exists
        // Otherwise use the current physical cursor as the new anchor
        if (oldOriginIndex >= 0 && oldOriginIndex < capacity)
        {
            _origin = GetCoordinate(oldOriginIndex, info.Size.X);
        }
        else
        {
            _origin = info.CursorPosition;
        }
        
        _renderedEnd = _origin;
        _renderedBufferWidth = info.Size.X;
        _renderedBufferHeight = info.Size.Y;
        _renderedCellLength = 0;
        _renderedCursorCellOffset = 0;
        
        RenderSingleLine();
        
        return true;
    }
    
    /// <summary>
    /// Attempts to apply a pending resize after activity settles
    /// </summary>
    private void TryApplyPendingResize()
    {
        if (!_resizePending)
        {
            return;
        }

        long lastResizeTick = Volatile.Read(ref _lastResizeTick);
        
        // Defer recovery until resize activity settles
        if (Environment.TickCount64 - lastResizeTick < ResizeSettle)
        {
            return;
        }

        _resizePending = false;

        if (!RebuildSingleLineAfterResize())
        {
            // A concurrent resize may invalidate rebuilding while input is cleared and rendered
            MarkResizePending();
        }
    }
    
    /// <summary>
    /// Synchronizes tracked render geometry with the current screen buffer
    /// </summary>
    private void SynchronizeRenderGeometry()
    {
        Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();

        // Initialize geometry when no valid render has been recorded
        if (0 >= _renderedBufferWidth || 0 >= _renderedBufferHeight)
        {
            ResetRenderGeometry(info);
            return;
        }

        // Check whether the screen buffer dimensions changed
        bool sizeChanged = info.Size.X != _renderedBufferWidth || info.Size.Y != _renderedBufferHeight;
        
        if (!sizeChanged)
        {
            // Validate the recorded coordinates when dimensions are unchanged
            if (!IsPositionValid(_origin, info.Size) || !IsPositionValid(_renderedEnd, info.Size))
            {
                ResetRenderGeometry(info);
            }
            
            return;
        }

        // Recalculate the origin and rendered end from the physical cursor
        // and the previously recorded relative cell offset after a width change
        long capacity = (long)info.Size.X * info.Size.Y;
        long cursorIndex = GetLinearPosition(info.CursorPosition, info.Size.X);

        long originIndex = cursorIndex - _renderedCursorCellOffset;
        long endIndex = originIndex + _renderedCellLength;

        if (0 > originIndex || originIndex >= capacity || endIndex < originIndex || endIndex >= capacity)
        {
            ResetRenderGeometry(info);
            return;
        }

        Win32ConsoleNative.Coord origin = GetCoordinate(originIndex, info.Size.X);
        Win32ConsoleNative.Coord renderedEnd = GetCoordinate(endIndex, info.Size.X);

        if (!IsPositionValid(origin, info.Size) || !IsPositionValid(renderedEnd, info.Size))
        {
            ResetRenderGeometry(info);
            return;
        }

        _origin = origin;
        _renderedEnd = renderedEnd;
        _renderedBufferWidth = info.Size.X;
        _renderedBufferHeight = info.Size.Y;
    }
    
    /// <summary>
    /// Resets tracked render geometry to the current cursor
    /// </summary>
    /// <param name="info">Current screen buffer information</param>
    private void ResetRenderGeometry(Win32ConsoleNative.ConsoleScreenBufferInfo info)
    {
        _origin = info.CursorPosition;
        _renderedEnd = _origin;
        _renderedBufferWidth = info.Size.X;
        _renderedBufferHeight = info.Size.Y;
        _renderedCellLength = 0;
        _renderedCursorCellOffset = 0;
    }
    
    /// <summary>
    /// Clears the currently rendered input area
    /// </summary>
    private void ClearRenderedInput()
    {
        Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();
        ClearRange(_origin, _renderedEnd, info.Size.X);
        SetCursorPosition(_origin);
    }
    
    /// <summary>
    /// Clears a coordinate range in the console output buffer
    /// </summary>
    /// <param name="start">Inclusive starting coordinate</param>
    /// <param name="end">Exclusive ending coordinate</param>
    /// <param name="bufferWidth">Screen buffer width</param>
    private void ClearRange(Win32ConsoleNative.Coord start, Win32ConsoleNative.Coord end, short bufferWidth)
    {
        long startIndex = GetLinearPosition(start, bufferWidth);
        long endIndex = GetLinearPosition(end, bufferWidth);

        if (startIndex >= endIndex)
        {
            return;
        }

        uint length = checked((uint)(endIndex - startIndex));
        
        if (!Win32ConsoleNative.FillConsoleOutputCharacter(_outputHandle, ' ', length, start, out uint written))
        {
            throw CreateNativeException("Failed to clear the Windows console output range.");
        }
        
        if (written != length)
        {
            throw new IOException(
                $"Failed to clear all characters in the console output range. Written: {written}, Expected: {length}");
        }
    }
    
    /// <summary>
    /// Clears a linear range in the console output buffer
    /// </summary>
    /// <param name="startIndex">Inclusive starting cell index</param>
    /// <param name="endIndex">Exclusive ending cell index</param>
    /// <param name="bufferWidth">Screen buffer width</param>
    private void ClearLinearRange(long startIndex, long endIndex, short bufferWidth)
    {
        if (startIndex >= endIndex)
        {
            return;
        }

        Win32ConsoleNative.Coord start = GetCoordinate(startIndex, bufferWidth);
        uint length = checked((uint)(endIndex - startIndex));
        
        if (!Win32ConsoleNative.FillConsoleOutputCharacter(_outputHandle, ' ', length, start, out uint written))
        {
            throw CreateNativeException("Failed to clear the Windows console output range.");
        }
        
        if (written != length)
        {
            throw new IOException(
                $"Failed to clear all characters in the console output range. Written: {written}, Expected: {length}");
        }
    }
    
    /// <summary>
    /// Sets the console cursor position
    /// </summary>
    /// <param name="position">Target cursor position</param>
    private void SetCursorPosition(Win32ConsoleNative.Coord position)
    {
        if (!Win32ConsoleNative.SetConsoleCursorPosition(_outputHandle, position))
        {
            throw CreateNativeException("Failed to set the Windows console cursor position.");
        }
    }

    /// <summary>
    /// Attempts to set the console cursor position
    /// </summary>
    /// <param name="position">Target cursor position</param>
    /// <returns><see langword="true"/> on success or <see langword="false"/> when a concurrent resize invalidates the position</returns>
    private bool TrySetCursorPosition(Win32ConsoleNative.Coord position)
    {
        if (Win32ConsoleNative.SetConsoleCursorPosition(_outputHandle, position))
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();

        if (87 == error)
        {
            return false;
        }

        throw new IOException(
            $"Failed to set the Windows console cursor position. (Error code: {error})");
    }
    
    /// <summary>
    /// Gets current console screen buffer information
    /// </summary>
    /// <returns>The current screen buffer information</returns>
    private Win32ConsoleNative.ConsoleScreenBufferInfo GetConsoleScreenBufferInfo()
    {
        if (!Win32ConsoleNative.GetConsoleScreenBufferInfo(_outputHandle, 
                out Win32ConsoleNative.ConsoleScreenBufferInfo info))
        {
            throw CreateNativeException("Failed to read the Windows console screen buffer state.");
        }

        return info;
    }

    /// <summary>
    /// Adds a nonempty unique entry to command history
    /// </summary>
    /// <param name="text">History text</param>
    private void AddHistory(string text)
    {
        if (0 == _historyCount || 0 == text.Length)
        {
            return;
        }
        
        // Do not add a duplicate of the most recent entry
        if (0 < _history.Count && _history[^1] == text)
        {
            return;
        }
        
        _history.Add(text);
        
        // Remove the oldest entry when history exceeds its limit
        if (_history.Count > _historyCount)
        {
            _history.RemoveAt(0);
        }
    }

    /// <summary>
    /// Scrolls the console screen buffer upward
    /// </summary>
    /// <param name="rows">Number of rows to scroll</param>
    /// <param name="info">Current screen buffer information</param>
    private void ScrollBufferUp(int rows, Win32ConsoleNative.ConsoleScreenBufferInfo info)
    {
        if (0 >= rows)
        {
            return;
        }
        
        Win32ConsoleNative.SmallRect scrollRect = new()
        {
            Left = 0,
            Top = (short)rows,
            Right = (short)(info.Size.X - 1),
            Bottom = (short)(info.Size.Y - 1)
        };
        
        Win32ConsoleNative.Coord dest = new()
        {
            X = 0,
            Y = 0
        };
        
        Win32ConsoleNative.CharInfo fill = new()
        {
            UnicodeChar = ' ',
            Attributes = info.Attributes
        };
        
        if (!Win32ConsoleNative.ScrollConsoleScreenBuffer(
                _outputHandle, ref scrollRect, IntPtr.Zero, dest, ref fill))
        {
            throw CreateNativeException("Failed to scroll the Windows console screen buffer.");
        }
        
        _origin.Y = (short)Math.Max(0, _origin.Y - rows);
        _renderedEnd.Y = (short)Math.Max(0, _renderedEnd.Y - rows);
    }

    /// <summary>
    /// Ensures that the screen buffer has room for the complete multiline input
    /// </summary>
    private void EnsureRenderSpace()
    {
        Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();
        
        RenderPosition position = new(_origin.X, _origin.Y);
        position = AdvancePosition(position, _prompt, info.Size.X);
        position = AdvancePosition(position, _buffer.ToString(), info.Size.X);
        
        int lastRow = info.Size.Y - 1;

        if (position.Y <= lastRow)
        {
            return;
        }
        
        int rowsToScroll = position.Y - lastRow;
        
        // Scrolling cannot preserve the prompt and complete input when they exceed the screen buffer height
        rowsToScroll = Math.Min(rowsToScroll, _origin.Y);
        
        if (0 < rowsToScroll)
        {
            ScrollBufferUp(rowsToScroll, info);
        }
    }
    
    /// <summary>
    /// Attempts to write one line above the input area without clearing or rerendering it
    /// </summary>
    /// <param name="content">Content to write</param>
    /// <returns>The fast-path outcome</returns>
    private WriteAboveFastPathResult TryWriteAboveWithoutRedraw(string content)
    {
        if (!TryGetSingleWriteAboveLine(content, out string line))
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();

        long now = Environment.TickCount64;
        long lastResizeTick = Volatile.Read(ref _lastResizeTick);
        long resizeVersion = Interlocked.Read(ref _resizeVersion);

        //
        // The current dimensions differ from those used by the last successful render
        //
        // SingleLine:
        // Mark the resize as pending but record activity only when it is first observed
        // Refreshing the tick on every retry would prevent the settle interval from elapsing
        //
        // MultiLine:
        // WriteAbove synchronizes render geometry after a retry so one resize event is sufficient here
        //
        if (_renderedBufferWidth != info.Size.X || _renderedBufferHeight != info.Size.Y)
        {
            if (_multiLine)
            {
                RecordResizeActivity();
            }
            else if (!_resizePending)
            {
                MarkResizePending();
            }

            return WriteAboveFastPathResult.Retry;
        }

        
        // Retry while recovery is pending or recent resize activity has not settled
        if (_resizePending || (0 != lastResizeTick && ResizeSettle > now - lastResizeTick))
        {
            return WriteAboveFastPathResult.Retry;
        }

        if (0 >= info.Size.X || 0 >= info.Size.Y)
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        if (!IsPositionValid(_origin, info.Size) ||
            !IsPositionValid(_renderedEnd, info.Size))
        {
            return WriteAboveFastPathResult.Unsupported;
        }
        
        // The fast path supports only input areas that start at the beginning of a row
        if (0 != _origin.X)
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        if (0 >= _renderedCellLength)
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        // The fast path supports only output that does not wrap
        if (GetDisplayWidth(line) >= info.Size.X)
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        long capacity = (long)info.Size.X * info.Size.Y;
        long originIndex = GetLinearPosition(_origin, info.Size.X);
        long endIndex = originIndex + _renderedCellLength;
        long cursorIndex = originIndex + _renderedCursorCellOffset;

        if (0 > originIndex || capacity <= originIndex || endIndex <= originIndex || capacity < endIndex || 
            cursorIndex < originIndex || endIndex < cursorIndex)
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        // The physical cursor must still match the tracked position
        if (cursorIndex != GetLinearPosition(info.CursorPosition, info.Size.X))
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        //
        // Another resize may have occurred after the first check
        //
        // Check all resize signals together
        // 1) screen buffer size
        // 2) cursor
        // 3) resize event generation
        //
        // The generation check is essential because a resize event can arrive while WriteAbove holds the console lock
        // RecordResizeActivity runs before acquiring that lock
        //
        Win32ConsoleNative.ConsoleScreenBufferInfo currentInfo = GetConsoleScreenBufferInfo();

        if (currentInfo.Size.X != info.Size.X || currentInfo.Size.Y != info.Size.Y ||
            currentInfo.CursorPosition.X != info.CursorPosition.X || 
            currentInfo.CursorPosition.Y != info.CursorPosition.Y ||
            resizeVersion != Interlocked.Read(ref _resizeVersion))
        {
            return WriteAboveFastPathResult.Retry;
        }

        if (_multiLine)
        {
            return TryWriteAboveMultiLineWithoutClear(line);
        }
        
        Win32ConsoleNative.Coord lastCell = GetCoordinate(endIndex - 1, info.Size.X);

        short logRow;

        if (lastCell.Y < info.Size.Y - 1)
        {
            //
            // Space remains below the input area
            //
            // history
            // > input
            // empty
            //
            // Transform the layout into
            //
            // history
            // empty <- log
            // > input
            //
            if (capacity <= endIndex + info.Size.X || capacity <= cursorIndex + info.Size.X)
            {
                return WriteAboveFastPathResult.Retry;
            }
            
            // Perform one final generation check before modifying the screen buffer
            if (resizeVersion != Interlocked.Read(ref _resizeVersion))
            {
                return WriteAboveFastPathResult.Retry;
            }

            Win32ConsoleNative.SmallRect scrollRect = new()
            {
                Left = 0,
                Top = _origin.Y,
                Right = (short)(info.Size.X - 1),
                Bottom = lastCell.Y
            };

            Win32ConsoleNative.Coord destination = new()
            {
                X = 0,
                Y = (short)(_origin.Y + 1)
            };

            Win32ConsoleNative.CharInfo fill = new()
            {
                UnicodeChar = ' ',
                Attributes = info.Attributes
            };

            if (!Win32ConsoleNative.ScrollConsoleScreenBuffer(_outputHandle, ref scrollRect, IntPtr.Zero,
                    destination, ref fill))
            {
                throw CreateNativeException("Failed to move the Windows console input region.");
            }

            _origin.Y++;
            _renderedEnd.Y++;

            logRow = (short)(_origin.Y - 1);
        }
        else
        {
            // Keep the input area fixed at the buffer bottom and scroll only the history above it
            if (0 >= _origin.Y)
            {
                return WriteAboveFastPathResult.Unsupported;
            }

            logRow = (short)(_origin.Y - 1);
            
            // Check the resize generation again before modifying the screen buffer
            if (resizeVersion != Interlocked.Read(ref _resizeVersion))
            {
                return WriteAboveFastPathResult.Retry;
            }

            if (1 < _origin.Y)
            {
                Win32ConsoleNative.SmallRect scrollRect = new()
                {
                    Left = 0,
                    Top = 1,
                    Right = (short)(info.Size.X - 1),
                    Bottom = logRow
                };

                Win32ConsoleNative.Coord destination = new()
                {
                    X = 0,
                    Y = 0
                };

                Win32ConsoleNative.CharInfo fill = new()
                {
                    UnicodeChar = ' ',
                    Attributes = info.Attributes
                };

                if (!Win32ConsoleNative.ScrollConsoleScreenBuffer(_outputHandle, ref scrollRect, IntPtr.Zero,
                        destination, ref fill))
                {
                    throw CreateNativeException("Failed to scroll the Windows console output history.");
                }
            }
            else
            {
                ClearLinearRange(0, info.Size.X, info.Size.X);
            }
        }

        // Emit a real newline even though the input area is already below the log line
        // This terminates the log line before restoring the logical input cursor
        SetCursorPosition(new Win32ConsoleNative.Coord
        {
            X = 0,
            Y = logRow
        });

        WriteConsole(string.Concat(line, Environment.NewLine));

        long newOriginIndex = GetLinearPosition(_origin, info.Size.X);

        long newCursorIndex = newOriginIndex + _renderedCursorCellOffset;

        SetCursorPosition(GetCoordinate(newCursorIndex, info.Size.X));

        return WriteAboveFastPathResult.Success;
    }
    
    /// <summary>
    /// Writes one line above multiline input without clearing the input area first
    /// </summary>
    /// <param name="line">Single line of output</param>
    /// <returns>The fast-path outcome</returns>
    private WriteAboveFastPathResult TryWriteAboveMultiLineWithoutClear(string line)
    {
        Win32ConsoleNative.ConsoleScreenBufferInfo info = GetConsoleScreenBufferInfo();

        if (0 >= info.Size.X || 0 >= info.Size.Y)
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        if (!IsPositionValid(_origin, info.Size))
        {
            return WriteAboveFastPathResult.Unsupported;
        }

        // Leave the physical cells of existing multiline input in place
        // Overwrite from the original input origin with the log line
        SetCursorPosition(_origin);
        WriteConsole(line);

        Win32ConsoleNative.ConsoleScreenBufferInfo lineEndInfo =
            GetConsoleScreenBufferInfo();

        // Clear the tail left by the original input on this physical row
        // FillConsoleOutputCharacter does not move the current cursor
        long lineEndIndex = GetLinearPosition(lineEndInfo.CursorPosition, lineEndInfo.Size.X);

        long rowEndIndex = ((long)lineEndInfo.CursorPosition.Y + 1) * lineEndInfo.Size.X;

        long capacity = (long)lineEndInfo.Size.X * lineEndInfo.Size.Y;

        rowEndIndex = Math.Min(rowEndIndex, capacity);

        if (lineEndIndex < rowEndIndex)
        {
            ClearLinearRange(lineEndIndex, rowEndIndex, lineEndInfo.Size.X);
        }

        // Emit a real newline to create a logical line boundary between the log and input
        WriteConsole(Environment.NewLine);

        Win32ConsoleNative.ConsoleScreenBufferInfo originInfo = GetConsoleScreenBufferInfo();

        // The current cursor becomes the new input origin
        _origin = originInfo.CursorPosition;
        _renderedEnd = _origin;
        _renderedBufferWidth = originInfo.Size.X;
        _renderedBufferHeight = originInfo.Size.Y;
        _renderedCellLength = 0;
        _renderedCursorCellOffset = 0;

        // Rerender input directly without calling ClearRenderedInput first
        // RenderMultiLine writes the prompt and buffer from the new origin to restore normal soft wrapping
        RenderMultiLine();

        return WriteAboveFastPathResult.Success;
    }
    
    /// <summary>
    /// Begins synchronized virtual terminal output
    /// </summary>
    private void BeginSynchronizedOutput()
    {
        WriteConsole("\x1b[?2026h");
    }

    /// <summary>
    /// Ends synchronized virtual terminal output
    /// </summary>
    private void EndSynchronizedOutput()
    {
        WriteConsole("\x1b[?2026l");
    }
    
    /// <summary>
    /// Gets the index of the previous text element
    /// </summary>
    /// <param name="text">Input text</param>
    /// <param name="cursorIndex">Current UTF-16 cursor index</param>
    /// <returns>The index of the previous text element</returns>
    private static int GetPreviousTextElementIndex(string text, int cursorIndex)
    {
        if (cursorIndex <= 0)
        {
            return 0;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);

        for (int i = starts.Length - 1; i >= 0; i--)
        {
            if (starts[i] < cursorIndex)
            {
                return starts[i];
            }
        }

        return 0;
    }

    /// <summary>
    /// Gets the index after the next text element
    /// </summary>
    /// <param name="text">Input text</param>
    /// <param name="cursorIndex">Current UTF-16 cursor index</param>
    /// <returns>The index after the next text element</returns>
    private static int GetNextTextElementIndex(string text, int cursorIndex)
    {
        if (cursorIndex >= text.Length)
        {
            return text.Length;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(text);

        foreach (int start in starts)
        {
            if (start > cursorIndex)
            {
                return start;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Removes the text element before the cursor
    /// </summary>
    /// <param name="buffer">Editable input buffer</param>
    /// <param name="cursorIndex">UTF-16 cursor index updated after removal</param>
    /// <returns><see langword="true"/> when a text element is removed</returns>
    private static bool Backspace(StringBuilder buffer, ref int cursorIndex)
    {
        if (cursorIndex == 0)
        {
            return false;
        }

        string text = buffer.ToString();
        int previous = GetPreviousTextElementIndex(text, cursorIndex);

        buffer.Remove(previous, cursorIndex - previous);
        cursorIndex = previous;

        return true;
    }

    /// <summary>
    /// Removes the text element under the cursor
    /// </summary>
    /// <param name="buffer">Editable input buffer</param>
    /// <param name="cursorIndex">Current UTF-16 cursor index</param>
    /// <returns><see langword="true"/> when a text element is removed</returns>
    private static bool Delete(StringBuilder buffer, int cursorIndex)
    {
        if (cursorIndex >= buffer.Length)
        {
            return false;
        }

        string text = buffer.ToString();
        int next = GetNextTextElementIndex(text, cursorIndex);

        buffer.Remove(cursorIndex, next - cursorIndex);

        return true;
    }
    
    /// <summary>
    /// Converts a coordinate to a linear cell index
    /// </summary>
    /// <param name="position">Console coordinate</param>
    /// <param name="bufferWidth">Screen buffer width</param>
    /// <returns>The linear cell index</returns>
    private static long GetLinearPosition(Win32ConsoleNative.Coord position, short bufferWidth)
        => (long)position.Y * bufferWidth + position.X;

    /// <summary>
    /// Converts a linear cell index to a coordinate
    /// </summary>
    /// <param name="linearPosition">Linear cell index</param>
    /// <param name="bufferWidth">Screen buffer width</param>
    /// <returns>The corresponding console coordinate</returns>
    private static Win32ConsoleNative.Coord GetCoordinate(long linearPosition, short bufferWidth)
    {
        return new Win32ConsoleNative.Coord
        {
            X = (short)(linearPosition % bufferWidth),
            Y = (short)(linearPosition / bufferWidth)
        };
    }
    
    /// <summary>
    /// Replaces editable input and recalculates its metadata
    /// </summary>
    /// <param name="buffer">Editable input buffer</param>
    /// <param name="text">Replacement text</param>
    /// <param name="cursorIndex">Receives the UTF-16 cursor index</param>
    /// <param name="utf8ByteCount">Receives the UTF-8 byte count</param>
    private static void SetBuffer(StringBuilder buffer, string text, out int cursorIndex, out int utf8ByteCount)
    {
        buffer.Clear();
        buffer.Append(text);
        cursorIndex = buffer.Length;
        utf8ByteCount = Encoding.UTF8.GetByteCount(text);
    }
    
    /// <summary>
    /// Advances a render position through text
    /// </summary>
    /// <param name="position">Starting render position</param>
    /// <param name="text">Text to measure</param>
    /// <param name="bufferWidth">Screen buffer width</param>
    /// <returns>The render position after the text</returns>
    private static RenderPosition AdvancePosition(RenderPosition position, string text, int bufferWidth)
    {
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            int width = UnicodeCalculator.GetWidth(element);

            if (0 >= width)
            {
                continue;
            }

            if (position.X + width > bufferWidth)
            {
                position = new RenderPosition(0, position.Y + 1);
            }
            
            int x = position.X + width;
            int y = position.Y;
            
            if (x >= bufferWidth)
            {
                x = 0;
                y++;
            }
            
            position = new RenderPosition(x, y);
        }
        
        return position;
    }

    /// <summary>
    /// Gets the terminal display width of text
    /// </summary>
    /// <param name="text">Text to measure</param>
    /// <returns>The width in console cells</returns>
    private static int GetDisplayWidth(string text)
    {
        int width = 0;
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            width += Math.Max(0, UnicodeCalculator.GetWidth(enumerator.GetTextElement()));
        }

        return width;
    }

    /// <summary>
    /// Calculates the visible slice and cursor offset for single-line mode
    /// </summary>
    /// <param name="text">Input text</param>
    /// <param name="cursorIndex">Current UTF-16 cursor index</param>
    /// <param name="maxWidth">Maximum visible width in console cells</param>
    /// <param name="start">Receives the inclusive UTF-16 start index</param>
    /// <param name="end">Receives the exclusive UTF-16 end index</param>
    /// <param name="cursorWidth">Receives the cursor offset in console cells</param>
    private static void GetSingleLineView(string text, int cursorIndex, int maxWidth, out int start, out int end,
        out int cursorWidth)
    {
        start = 0;
        cursorWidth = GetDisplayWidth(text[..cursorIndex]);

        while (cursorWidth > maxWidth && start < cursorIndex)
        {
            int next = GetNextTextElementIndex(text, start);
            cursorWidth -= UnicodeCalculator.GetWidth(text[start..next]);
            start = next;
        }

        end = text.Length;
        int visibleWidth = cursorWidth + GetDisplayWidth(text[cursorIndex..]);
        
        while (visibleWidth > maxWidth && end > cursorIndex)
        {
            int previous = GetPreviousTextElementIndex(text, end);
            visibleWidth -= UnicodeCalculator.GetWidth(text[previous..end]);
            end = previous;
        }
    }
    
    /// <summary>
    /// Checks whether a coordinate is inside the screen buffer
    /// </summary>
    /// <param name="position">Coordinate to validate</param>
    /// <param name="size">Screen buffer dimensions</param>
    /// <returns><see langword="true"/> when the coordinate is valid</returns>
    private static bool IsPositionValid(Win32ConsoleNative.Coord position, Win32ConsoleNative.Coord size)
    {
        // ReSharper disable once MergeIntoPattern
        return position.X >= 0 && position.Y >= 0 && position.X < size.X && position.Y < size.Y;
    }
    
    /// <summary>
    /// Attempts to extract one line suitable for the WriteAbove fast path
    /// </summary>
    /// <param name="content">Original content</param>
    /// <param name="line">Receives the single output line</param>
    /// <returns><see langword="true"/> when the content can use the fast path</returns>
    private static bool TryGetSingleWriteAboveLine(string content, out string line)
    {
        int length = content.Length;

        // WriteAbove is line oriented so remove one trailing newline supplied by the caller
        // The fast path represents that newline through the physical screen layout
        if (0 < length && '\n' == content[length - 1])
        {
            length--;

            if (0 < length && '\r' == content[length - 1])
            {
                length--;
            }
        }

        line = content[..length];

        foreach (char value in line)
        {
            // Route CR, LF, Tab, ANSI escape sequences, and other control characters through the fallback
            if (char.IsControl(value))
            {
                return false;
            }
        }

        return true;
    }
    
    /// <summary>
    /// Represents a position reached while measuring rendered text
    /// </summary>
    /// <param name="X">Horizontal coordinate</param>
    /// <param name="Y">Vertical coordinate</param>
    private readonly record struct RenderPosition(int X, int Y);
    
    /// <summary>
    /// Describes the outcome of a WriteAbove fast-path attempt
    /// </summary>
    private enum WriteAboveFastPathResult
    {
        /// <summary>
        /// The fast path completed successfully
        /// </summary>
        Success,
        
        /// <summary>
        /// A transient state such as resize requires a retry and must not fall back
        /// </summary>
        Retry,
        
        /// <summary>
        /// The content is unsupported by the fast path and may use the fallback
        /// </summary>
        Unsupported
    }
}
