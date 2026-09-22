using System.Text;
using Majo.LineEditor;
using Editor = Majo.LineEditor.LineEditor;
// ReSharper disable AccessToDisposedClosure

Console.OutputEncoding = Encoding.UTF8;

string test = args.Length == 0 ? "basic" : args[0].ToLowerInvariant();

try
{
    switch (test)
    {
        case "basic":
            await RunBasicAsync();
            break;
        
        case "write-above":
            await RunWriteAboveAsync();
            break;
        
        case "cancellation":
            await RunCancellationAsync();
            break;

        case "dispose":
            await RunDisposeAsync();
            break;

        case "resize":
            await RunResizeAsync();
            break;
        
        case "resize-write-above":
            await RunResizeWriteAboveAsync();
            break;
        
        case "multiline":
            await RunMultiLineAsync();
            break;
        
        case "multiline-write-above":
            await RunMultiLineWriteAboveAsync();
            break;
        
        case "multiline-resize":
            await RunMultiLineResizeAsync();
            break;
        
        case "multiline-resize-write-above":
            await RunMultiLineResizeWriteAboveAsync();
            break;
        
        default:
            PrintUsage();
            return 1;
    }
    
    Console.WriteLine();
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey(intercept: true);

    return 0;
}
catch (Exception exception)
{
    Console.WriteLine();
    Console.WriteLine($"Unhandled exception: {exception}");
    return 1;
}

// Runs the basic line editing test
static async Task RunBasicAsync()
{
    Console.WriteLine("Basic interactive test");
    Console.WriteLine("Try:");
    Console.WriteLine("- ASCII / 中文 / emoji");
    Console.WriteLine("- Left / Right / Home / End");
    Console.WriteLine("- Backspace / Delete");
    Console.WriteLine("- Ctrl+D on a non-empty line");
    Console.WriteLine("- Up / Down history");
    Console.WriteLine("- Edit a history item, move away, then return to it");
    Console.WriteLine("- Ctrl+C");
    Console.WriteLine("- Ctrl+D on an empty line to finish");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> ",
        HistoryCount = 3,
        MultiLine = false
    });

    while (true)
    {
        ReadResult result = await editor.ReadLineAsync();

        switch (result.Status)
        {
            case ReadStatus.Accepted:
                Console.WriteLine($"Accepted: [{result.Text}]");
                break;

            case ReadStatus.Interrupted:
                Console.WriteLine("Interrupted");
                break;

            case ReadStatus.EndOfInput:
                Console.WriteLine("EndOfInput");
                return;
        }
    }
}

// Runs the background WriteAbove test
static async Task RunWriteAboveAsync()
{
    Console.WriteLine("WriteAbove test");
    Console.WriteLine("Start typing and move the cursor around while background messages appear.");
    Console.WriteLine("The current buffer and cursor position should remain unchanged.");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> "
    });

    using var writerCancellation = new CancellationTokenSource();

    Task writerTask = Task.Run(async () =>
    {
        int index = 1;

        try
        {
            while (true)
            {
                await Task.Delay(1000, writerCancellation.Token);
                editor.WriteAbove($"[background {index++}]");
            }
        }
        catch (OperationCanceledException)
        {
        }
    });

    try
    {
        ReadResult result = await editor.ReadLineAsync();
        Console.WriteLine($"Result: {result.Status}, Text: [{result.Text}]");
    }
    finally
    {
        writerCancellation.Cancel();
        await writerTask;
    }
}

// Runs the cancellation behavior test
static async Task RunCancellationAsync()
{
    Console.WriteLine("Cancellation test");
    Console.WriteLine("The first ReadLineAsync will be cancelled after 10 seconds.");
    Console.WriteLine("You may type something before cancellation, but do not press Enter.");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> ",
        PollInterval = 100
    });

    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    try
    {
        await editor.ReadLineAsync(cancellation.Token);
        Console.WriteLine("ERROR: ReadLineAsync completed normally.");
        return;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("OperationCanceledException received as expected.");
    }
    
    Console.WriteLine();
    Console.WriteLine("Console mode restoration check.");
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey(intercept: true);

    Console.WriteLine();
    Console.WriteLine("Starting another ReadLineAsync on the same editor.");
    Console.WriteLine("Type something and press Enter.");

    ReadResult result = await editor.ReadLineAsync();

    Console.WriteLine($"Result: {result.Status}, Text: [{result.Text}]");
}

// Runs the disposal behavior test
static async Task RunDisposeAsync()
{
    Console.WriteLine("Dispose test");
    Console.WriteLine("The editor will be disposed after 10 seconds while ReadLineAsync is waiting.");
    Console.WriteLine("You may type something before disposal, but do not press Enter.");
    Console.WriteLine();

    var editor = new Editor(new LineEditorOption
    {
        Prompt = "> ",
        PollInterval = 100
    });

    Task<ReadResult> readTask = editor.ReadLineAsync().AsTask();

    await Task.Delay(10000);

    Console.WriteLine();
    Console.WriteLine("Disposing editor...");
    
    editor.Dispose();

    try
    {
        ReadResult result = await readTask;
        Console.WriteLine($"Read task completed normally: {result.Status}, Text: [{result.Text}]");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Read task was cancelled by Dispose as expected.");
    }

    Console.WriteLine();
    Console.WriteLine("Console mode restoration check.");
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey(intercept: true);

    Console.WriteLine();
    Console.WriteLine("Disposed instance check.");

    try
    {
        await editor.ReadLineAsync();
        Console.WriteLine("ERROR: ReadLineAsync succeeded after Dispose.");
    }
    catch (ObjectDisposedException)
    {
        Console.WriteLine("ObjectDisposedException received as expected.");
    }

    Console.WriteLine();
    Console.WriteLine("Creating a new LineEditor instance.");

    using var secondEditor = new Editor(new LineEditorOption
    {
        Prompt = "> "
    });

    ReadResult secondResult = await secondEditor.ReadLineAsync();

    Console.WriteLine($"Result: {secondResult.Status}, Text: [{secondResult.Text}]");
}

// Runs the single-line resize test
static async Task RunResizeAsync()
{
    Console.WriteLine("Resize test");
    Console.WriteLine("While editing:");
    Console.WriteLine("- type a long line");
    Console.WriteLine("- move the cursor into the middle");
    Console.WriteLine("- make the terminal narrower and wider several times");
    Console.WriteLine("- continue typing, deleting and moving the cursor");
    Console.WriteLine("- press Enter when finished");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> "
    });

    ReadResult result = await editor.ReadLineAsync();

    Console.WriteLine($"Result: {result.Status}, Text: [{result.Text}]");
}

// Runs the single-line resize and WriteAbove stress test
static async Task RunResizeWriteAboveAsync()
{
    Console.WriteLine("Resize + WriteAbove test");
    Console.WriteLine("While editing:");
    Console.WriteLine("- type a long line");
    Console.WriteLine("- move the cursor into the middle");
    Console.WriteLine("- resize the terminal repeatedly");
    Console.WriteLine("- continue typing, deleting and moving the cursor");
    Console.WriteLine("- background messages should not corrupt the input");
    Console.WriteLine("- press Enter when finished");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> "
    });

    using var writerCancellation = new CancellationTokenSource();

    Task writerTask = Task.Run(async () =>
    {
        int index = 1;

        try
        {
            while (true)
            {
                await Task.Delay(1000, writerCancellation.Token);
                editor.WriteAbove($"[background {index++}]");
            }
        }
        catch (OperationCanceledException)
        {
        }
    });

    try
    {
        ReadResult result = await editor.ReadLineAsync();
        Console.WriteLine($"Result: {result.Status}, Text: [{result.Text}]");
    }
    finally
    {
        writerCancellation.Cancel();
        await writerTask;
    }
}

// Runs the multiline editing test
static async Task RunMultiLineAsync()
{
    Console.WriteLine("Multi-line interactive test");
    Console.WriteLine("Try:");
    Console.WriteLine("- type a line long enough to wrap across multiple terminal rows");
    Console.WriteLine("- include ASCII / 中文 / emoji");
    Console.WriteLine("- move Left / Right across wrapped rows");
    Console.WriteLine("- use Home / End");
    Console.WriteLine("- use Backspace / Delete");
    Console.WriteLine("- press Enter and verify the returned text");
    Console.WriteLine("- enter another command and test Up / Down history");
    Console.WriteLine("- Ctrl+C should interrupt");
    Console.WriteLine("- Ctrl+D on an empty line should finish");
    Console.WriteLine();
    Console.WriteLine("Do not resize the terminal during this test.");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> ",
        MultiLine = true
    });

    while (true)
    {
        ReadResult result = await editor.ReadLineAsync();

        Console.WriteLine();

        switch (result.Status)
        {
            case ReadStatus.Accepted:
                Console.WriteLine($"Accepted: [{result.Text}]");
                break;

            case ReadStatus.Interrupted:
                Console.WriteLine("Interrupted.");
                break;

            case ReadStatus.EndOfInput:
                Console.WriteLine("EndOfInput.");
                return;
        }

        Console.WriteLine();
    }
}

// Runs the multiline WriteAbove test
static async Task RunMultiLineWriteAboveAsync()
{
    Console.WriteLine("Multi-line + WriteAbove test");
    Console.WriteLine("While editing:");
    Console.WriteLine("- type a line long enough to wrap across multiple terminal rows");
    Console.WriteLine("- include ASCII / 中文 / emoji");
    Console.WriteLine("- move the cursor into the middle of the multi-line input");
    Console.WriteLine("- wait for several background messages");
    Console.WriteLine("- continue typing, deleting and moving the cursor");
    Console.WriteLine("- background messages must not corrupt the input");
    Console.WriteLine("- press Enter when finished");
    Console.WriteLine();
    Console.WriteLine("Do not resize the terminal during this test.");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> ",
        MultiLine = true
    });

    using var writerCancellation = new CancellationTokenSource();

    Task writerTask = Task.Run(async () =>
    {
        int index = 1;

        try
        {
            while (true)
            {
                await Task.Delay(1000, writerCancellation.Token);
                editor.WriteAbove($"[background {index++}]");
            }
        }
        catch (OperationCanceledException)
        {
        }
    });

    try
    {
        ReadResult result = await editor.ReadLineAsync();
        Console.WriteLine();
        Console.WriteLine($"Result: {result.Status}, Text: [{result.Text}]");
    }
    finally
    {
        writerCancellation.Cancel();
        await writerTask;
    }
}

// Runs the multiline resize test
static async Task RunMultiLineResizeAsync()
{
    Console.WriteLine("Multi-line + Resize test");
    Console.WriteLine("While editing:");
    Console.WriteLine("- type a line long enough to wrap across multiple terminal rows");
    Console.WriteLine("- include ASCII / 中文 / emoji");
    Console.WriteLine("- move the cursor into the middle");
    Console.WriteLine("- repeatedly resize the terminal narrower and wider");
    Console.WriteLine("- continue typing, deleting and moving the cursor after each resize");
    Console.WriteLine("- press Enter when finished");
    Console.WriteLine();
    Console.WriteLine("This test is currently observational.");
    Console.WriteLine("Resize-related rendering problems may be expected, especially on POSIX.");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> ",
        MultiLine = true
    });

    ReadResult result = await editor.ReadLineAsync();

    Console.WriteLine();
    Console.WriteLine($"Result: {result.Status}, Text: [{result.Text}]");
}

// Runs the multiline resize and WriteAbove stress test
static async Task RunMultiLineResizeWriteAboveAsync()
{
    Console.WriteLine("Multi-line + Resize + WriteAbove test");
    Console.WriteLine("While editing:");
    Console.WriteLine("- type a line long enough to wrap across multiple terminal rows");
    Console.WriteLine("- include ASCII / 中文 / emoji");
    Console.WriteLine("- move the cursor into the middle");
    Console.WriteLine("- repeatedly resize the terminal narrower and wider");
    Console.WriteLine("- wait for background messages before and after resizing");
    Console.WriteLine("- continue typing, deleting and moving the cursor");
    Console.WriteLine("- press Enter when finished");
    Console.WriteLine();
    Console.WriteLine("This test is currently observational.");
    Console.WriteLine("Resize-related rendering problems may be expected, especially on POSIX.");
    Console.WriteLine();

    using var editor = new Editor(new LineEditorOption
    {
        Prompt = "> ",
        MultiLine = true
    });

    using var writerCancellation = new CancellationTokenSource();

    Task writerTask = Task.Run(async () =>
    {
        int index = 1;

        try
        {
            while (true)
            {
                await Task.Delay(1000, writerCancellation.Token);
                editor.WriteAbove($"[background {index++}]");
            }
        }
        catch (OperationCanceledException)
        {
        }
    });

    try
    {
        ReadResult result = await editor.ReadLineAsync();

        Console.WriteLine();
        Console.WriteLine($"Result: {result.Status}, Text: [{result.Text}]");
    }
    finally
    {
        writerCancellation.Cancel();
        await writerTask;
    }
}

// Prints the available interactive test modes
static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Majo.LineEditor.Test basic");
    Console.WriteLine("  Majo.LineEditor.Test write-above");
    Console.WriteLine("  Majo.LineEditor.Test cancellation");
    Console.WriteLine("  Majo.LineEditor.Test dispose");
    Console.WriteLine("  Majo.LineEditor.Test resize");
    Console.WriteLine("  Majo.LineEditor.Test resize-write-above");
    Console.WriteLine("  Majo.LineEditor.Test multiline");
    Console.WriteLine("  Majo.LineEditor.Test multiline-write-above");
    Console.WriteLine("  Majo.LineEditor.Test multiline-resize");
    Console.WriteLine("  Majo.LineEditor.Test multiline-resize-write-above");
}
