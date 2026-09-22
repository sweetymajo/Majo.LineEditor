using System.Runtime.InteropServices;
// ReSharper disable MemberCanBePrivate.Global

namespace Majo.LineEditor.Backends.Win32;

/// <summary>
/// Provides Win32 console constants, structures, and native methods
/// </summary>
internal static class Win32ConsoleNative
{
    /// <summary>
    /// Standard input handle identifier
    /// </summary>
    internal const int StdInputHandle = -10;
    
    /// <summary>
    /// Standard output handle identifier
    /// </summary>
    internal const int StdOutputHandle = -11;
    
    /// <summary>
    /// Invalid Win32 handle sentinel
    /// </summary>
    internal static readonly IntPtr InvalidHandleValue = new(-1);
    
    /// <summary>
    /// Console input event type for keyboard events
    /// </summary>
    internal const ushort KeyEventType = 0x0001;
    
    /// <summary>
    /// Console input event type for screen buffer resize events
    /// </summary>
    internal const ushort WindowBufferSizeEventType = 0x0004;
    
    /// <summary>
    /// Virtual key code for Backspace
    /// </summary>
    internal const ushort VirtualKeyBackspace = 0x08;
    
    /// <summary>
    /// Virtual key code for Enter
    /// </summary>
    internal const ushort VirtualKeyReturn = 0x0D;
    
    /// <summary>
    /// Virtual key code for End
    /// </summary>
    internal const ushort VirtualKeyEnd = 0x23;
    
    /// <summary>
    /// Virtual key code for Home
    /// </summary>
    internal const ushort VirtualKeyHome = 0x24;
    
    /// <summary>
    /// Virtual key code for Left Arrow
    /// </summary>
    internal const ushort VirtualKeyLeft = 0x25;
    
    /// <summary>
    /// Virtual key code for Up Arrow
    /// </summary>
    internal const ushort VirtualKeyUp = 0x26;
    
    /// <summary>
    /// Virtual key code for Right Arrow
    /// </summary>
    internal const ushort VirtualKeyRight = 0x27;
    
    /// <summary>
    /// Virtual key code for Down Arrow
    /// </summary>
    internal const ushort VirtualKeyDown = 0x28;
    
    /// <summary>
    /// Virtual key code for Delete
    /// </summary>
    internal const ushort VirtualKeyDelete = 0x2E;
    
    /// <summary>
    /// Virtual key code for C
    /// </summary>
    internal const ushort VirtualKeyC = 0x43;
    
    /// <summary>
    /// Virtual key code for D
    /// </summary>
    internal const ushort VirtualKeyD = 0x44;
    
    /// <summary>
    /// Control-key state flag for the right Ctrl key
    /// </summary>
    internal const uint RightCtrlPressed = 0x0004;
    
    /// <summary>
    /// Control-key state flag for the left Ctrl key
    /// </summary>
    internal const uint LeftCtrlPressed = 0x0008;
    
    /// <summary>
    /// Combined control-key state mask for either Ctrl key
    /// </summary>
    internal const uint CtrlPressed = RightCtrlPressed | LeftCtrlPressed;
    
    /// <summary>
    /// Console mode flag enabling processed input
    /// </summary>
    internal const uint EnableProcessedInput = 0x0001;
    
    /// <summary>
    /// Console mode flag enabling line input
    /// </summary>
    internal const uint EnableLineInput = 0x0002;
    
    /// <summary>
    /// Console mode flag enabling input echo
    /// </summary>
    internal const uint EnableEchoInput = 0x0004;
    
    /// <summary>
    /// Console mode flag enabling window input events
    /// </summary>
    internal const uint EnableWindowInput = 0x0008;
    
    /// <summary>
    /// Console mode flag enabling mouse input
    /// </summary>
    internal const uint EnableMouseInput = 0x0010;
    
    /// <summary>
    /// Console mode flag enabling Quick Edit mode
    /// </summary>
    internal const uint EnableQuickEditMode = 0x0040;
    
    /// <summary>
    /// Console mode flag enabling extended flags
    /// </summary>
    internal const uint EnableExtendedFlags = 0x0080;
    
    /// <summary>
    /// Console mode flag enabling virtual terminal input
    /// </summary>
    internal const uint EnableVirtualTerminalInput = 0x0200;
    
    /// <summary>
    /// Console mode flag enabling processed output
    /// </summary>
    internal const uint EnableProcessedOutput = 0x0001;
    
    /// <summary>
    /// Console mode flag enabling virtual terminal output processing
    /// </summary>
    internal const uint EnableVirtualTerminalProcessing = 0x0004;
    
    /// <summary>
    /// Wait result indicating that the first object was signaled
    /// </summary>
    internal const uint WaitObject0 = 0x00000000;
    
    /// <summary>
    /// Wait result indicating a timeout
    /// </summary>
    internal const uint WaitTimeout = 0x00000102;
    
    /// <summary>
    /// Wait result indicating failure
    /// </summary>
    internal const uint WaitFailed = 0xFFFFFFFF;

    /// <summary>
    /// Represents a Win32 console input record
    /// </summary>
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    internal struct InputRecord
    {
        /// <summary>
        /// Input event type
        /// </summary>
        [FieldOffset(0)]
        public ushort EventType;

        /// <summary>
        /// Keyboard event data
        /// </summary>
        [FieldOffset(4)]
        public KeyEventRecord KeyEvent;
        
        /// <summary>
        /// Screen buffer resize event data
        /// </summary>
        [FieldOffset(4)]
        public WindowBufferSizeRecord WindowBufferSizeEvent;
    }

    /// <summary>
    /// Represents a Win32 keyboard input event
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct KeyEventRecord
    {
        /// <summary>
        /// Indicates whether the key is pressed
        /// </summary>
        public int KeyDown;
        
        /// <summary>
        /// Number of repeated key events
        /// </summary>
        public ushort RepeatCount;
        
        /// <summary>
        /// Virtual key code
        /// </summary>
        public ushort VirtualKeyCode;
        
        /// <summary>
        /// Virtual scan code
        /// </summary>
        public ushort VirtualScanCode;
        
        /// <summary>
        /// Unicode character generated by the key
        /// </summary>
        public char UnicodeChar;
        
        /// <summary>
        /// Control-key state flags
        /// </summary>
        public uint ControlKeyState;
    }

    /// <summary>
    /// Represents a character and its console attributes
    /// </summary>
    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    internal struct CharInfo
    {
        /// <summary>
        /// Unicode character
        /// </summary>
        [FieldOffset(0)]
        public char UnicodeChar;

        /// <summary>
        /// Console character attributes
        /// </summary>
        [FieldOffset(2)]
        public ushort Attributes;
    }
    
    /// <summary>
    /// Represents a coordinate in a console screen buffer
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord
    {
        /// <summary>
        /// Horizontal coordinate
        /// </summary>
        public short X;
        
        /// <summary>
        /// Vertical coordinate
        /// </summary>
        public short Y;
    }

    /// <summary>
    /// Represents a rectangular console region
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SmallRect
    {
        /// <summary>
        /// Left edge coordinate
        /// </summary>
        public short Left;
        
        /// <summary>
        /// Top edge coordinate
        /// </summary>
        public short Top;
        
        /// <summary>
        /// Right edge coordinate
        /// </summary>
        public short Right;
        
        /// <summary>
        /// Bottom edge coordinate
        /// </summary>
        public short Bottom;
    }

    /// <summary>
    /// Contains information about a console screen buffer
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ConsoleScreenBufferInfo
    {
        /// <summary>
        /// Screen buffer dimensions
        /// </summary>
        public Coord Size;
        
        /// <summary>
        /// Current cursor position
        /// </summary>
        public Coord CursorPosition;
        
        /// <summary>
        /// Current screen buffer attributes
        /// </summary>
        public ushort Attributes;
        
        /// <summary>
        /// Visible console window bounds
        /// </summary>
        public SmallRect Window;
        
        /// <summary>
        /// Maximum window dimensions
        /// </summary>
        public Coord MaximumWindowSize;
    }
    
    /// <summary>
    /// Represents a console screen buffer resize event
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowBufferSizeRecord
    {
        /// <summary>
        /// New screen buffer dimensions
        /// </summary>
        public Coord Size;
    }
    
    /// <summary>
    /// Gets the handle for a standard device
    /// </summary>
    /// <param name="stdHandle">Standard device identifier</param>
    /// <returns>The standard device handle</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int stdHandle);
    
    /// <summary>
    /// Gets the current mode of a console buffer
    /// </summary>
    /// <param name="consoleHandle">Console buffer handle</param>
    /// <param name="mode">Receives the current console mode</param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);
    
    /// <summary>
    /// Sets the mode of a console buffer
    /// </summary>
    /// <param name="consoleHandle">Console buffer handle</param>
    /// <param name="mode">Console mode to set</param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);
    
    /// <summary>
    /// Reads an event from a console input buffer
    /// </summary>
    /// <param name="consoleHandle">Console input buffer handle</param>
    /// <param name="buffer">Receives the input event record</param>
    /// <param name="length">Maximum number of records to read</param>
    /// <param name="numberOfEventsRead">Receives the number of records read</param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadConsoleInput(IntPtr consoleHandle, out InputRecord buffer, 
        uint length, out uint numberOfEventsRead);
    
    /// <summary>
    /// Writes characters to a console output buffer
    /// </summary>
    /// <param name="consoleHandle">Console output buffer handle</param>
    /// <param name="buffer">Text to write</param>
    /// <param name="numberOfCharsToWrite">Number of characters to write</param>
    /// <param name="numberOfCharsWritten">Receives the number of characters written</param>
    /// <param name="reserved">Reserved and required to be <see cref="IntPtr.Zero"/></param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", EntryPoint = "WriteConsoleW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteConsole(IntPtr consoleHandle, string buffer, uint numberOfCharsToWrite, 
        out uint numberOfCharsWritten, IntPtr reserved);
    
    /// <summary>
    /// Waits until an object is signaled or the timeout expires
    /// </summary>
    /// <param name="handle">Handle to wait on</param>
    /// <param name="milliseconds">Timeout in milliseconds</param>
    /// <returns>A Win32 wait result</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    
    /// <summary>
    /// Gets information about a console screen buffer
    /// </summary>
    /// <param name="consoleHandle">Console output buffer handle</param>
    /// <param name="consoleScreenBufferInfo">Receives the screen buffer information</param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetConsoleScreenBufferInfo(IntPtr consoleHandle, 
        out ConsoleScreenBufferInfo consoleScreenBufferInfo);
    
    /// <summary>
    /// Sets the cursor position in a console screen buffer
    /// </summary>
    /// <param name="consoleHandle">Console output buffer handle</param>
    /// <param name="cursorPosition">New cursor position</param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleCursorPosition(IntPtr consoleHandle, Coord cursorPosition);
    
    /// <summary>
    /// Fills a console screen buffer range with a character
    /// </summary>
    /// <param name="consoleHandle">Console output buffer handle</param>
    /// <param name="character">Fill character</param>
    /// <param name="length">Number of cells to fill</param>
    /// <param name="writeCoord">Starting coordinate</param>
    /// <param name="numberOfCharsWritten">Receives the number of cells filled</param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", EntryPoint = "FillConsoleOutputCharacterW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FillConsoleOutputCharacter(IntPtr consoleHandle, char character, uint length, 
        Coord writeCoord, out uint numberOfCharsWritten);
    
    /// <summary>
    /// Moves a region within a console screen buffer
    /// </summary>
    /// <param name="consoleHandle">Console output buffer handle</param>
    /// <param name="scrollRectangle">Region to move</param>
    /// <param name="clipRectangle">Optional clipping region</param>
    /// <param name="destinationOrigin">Upper-left destination coordinate</param>
    /// <param name="fill">Character and attributes used for vacated cells</param>
    /// <returns><see langword="true"/> on success</returns>
    [DllImport("kernel32.dll", EntryPoint = "ScrollConsoleScreenBufferW", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScrollConsoleScreenBuffer(IntPtr consoleHandle, ref SmallRect scrollRectangle, 
        IntPtr clipRectangle, Coord destinationOrigin, ref CharInfo fill);
}
