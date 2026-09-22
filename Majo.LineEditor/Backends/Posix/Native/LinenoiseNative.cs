using System.Runtime.InteropServices;

namespace Majo.LineEditor.Backends.Posix.Native;

/// <summary>
/// Provides native bindings for the POSIX line editor
/// </summary>
internal static class LinenoiseNative
{
    /// <summary>
    /// Native library name
    /// </summary>
    private const string LibraryName = "majo_line_editor";

    /// <summary>
    /// Feed status indicating an error
    /// </summary>
    internal const int Error = -1;
    
    /// <summary>
    /// Feed status indicating that more input is required
    /// </summary>
    internal const int More = 0;
    
    /// <summary>
    /// Feed status indicating that the line was accepted
    /// </summary>
    internal const int Accepted = 1;
    
    /// <summary>
    /// Feed status indicating the end of input
    /// </summary>
    internal const int EndOfInput = 2;
    
    /// <summary>
    /// Feed status indicating an interrupted read
    /// </summary>
    internal const int Interrupted = 3;
    
    /// <summary>
    /// Creates a native line editor context
    /// </summary>
    /// <param name="commandBufferSize">Maximum command size in bytes</param>
    /// <param name="multiLine">Nonzero to enable multiline mode</param>
    /// <returns>A native context handle on success or <see cref="IntPtr.Zero"/> on failure</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_create", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern IntPtr Create(int commandBufferSize, int multiLine);

    /// <summary>
    /// Destroys a native line editor context
    /// </summary>
    /// <param name="handle">Native context handle</param>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_destroy", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Destroy(IntPtr handle);

    /// <summary>
    /// Starts a native editing session
    /// </summary>
    /// <param name="handle">Native context handle</param>
    /// <param name="prompt">Prompt displayed before editable input</param>
    /// <returns>Zero on success or minus one on failure</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_start", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern int Start(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt);

    /// <summary>
    /// Stops the current native editing session
    /// </summary>
    /// <param name="handle">Native context handle</param>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_stop", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Stop(IntPtr handle);

    /// <summary>
    /// Waits for console input
    /// </summary>
    /// <param name="handle">Native context handle</param>
    /// <param name="timeoutMilliseconds">Wait timeout in milliseconds</param>
    /// <returns>One when input is available, zero on timeout, or minus one on failure</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_wait_for_input", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern int WaitForInput(IntPtr handle, int timeoutMilliseconds);
    
    /// <summary>
    /// Handles a terminal resize
    /// </summary>
    /// <param name="handle">Native context handle</param>
    /// <returns>Minus one on failure, zero when no resize is pending, or one after recovery</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_handle_resize", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern int HandleResize(IntPtr handle);

    /// <summary>
    /// Feeds available input to the native editor
    /// </summary>
    /// <param name="handle">Native context handle</param>
    /// <param name="line">Receives the allocated accepted line</param>
    /// <returns>A native feed status value</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_feed", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern int Feed(IntPtr handle, out IntPtr line);

    /// <summary>
    /// Frees a line allocated by the native editor
    /// </summary>
    /// <param name="line">Pointer to the allocated line</param>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_free_line", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void FreeLine(IntPtr line);

    /// <summary>
    /// Writes content above the active native input area
    /// </summary>
    /// <param name="handle">Native context handle</param>
    /// <param name="content">Content to write</param>
    /// <param name="newLine">Nonzero to append a newline</param>
    /// <returns>Zero on success or minus one on failure</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_write_above", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern int WriteAbove(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string content, int newLine);
    
    /// <summary>
    /// Initializes native history storage
    /// </summary>
    /// <param name="handle">Native context handle</param>
    /// <param name="entryCount">Maximum number of history entries</param>
    /// <returns>Zero on success or minus one on failure</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_prepare_history", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern int PrepareHistory(IntPtr handle, int entryCount);
    
    /// <summary>
    /// Adds an entry to native history
    /// </summary>
    /// <param name="handle">Native context handle</param>
    /// <param name="line">Line to add</param>
    /// <returns>Nonzero on success or zero on failure</returns>
    [DllImport(LibraryName, EntryPoint = "majo_line_editor_history_add", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    internal static extern int HistoryAdd(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string line);
}
