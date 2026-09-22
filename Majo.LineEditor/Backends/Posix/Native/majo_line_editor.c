#include "linenoise.h"

#include <errno.h>
#include <limits.h>
#include <poll.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/ioctl.h>

enum
{
    MAJO_LINE_EDITOR_ERROR = -1,
    MAJO_LINE_EDITOR_MORE = 0,
    MAJO_LINE_EDITOR_ACCEPTED = 1,
    MAJO_LINE_EDITOR_END_OF_INPUT = 2,
    MAJO_LINE_EDITOR_INTERRUPTED = 3
};

/// Native line editor context backed by linenoise
/// Stores editing state, input storage, terminal dimensions, and resize state
typedef struct
{
    // linenoise editing state
    struct linenoiseState state;

    // Buffer containing the current input
    char *buffer;
    
    // Prompt displayed before editable input
    char *prompt;
    
    // Input buffer size including the null terminator
    size_t buffer_size;
    
    // Indicates whether multiline mode is enabled
    int multi_line;

    // Indicates whether an editing session is active
    int editing;
    
    // Most recently observed terminal width
    int terminal_columns;
    
    // Terminal width to apply before the next editing operation
    int pending_columns;
    
    // Indicates whether a resize is waiting to be applied
    int resize_pending;
} majo_line_editor;

/// Gets the current terminal width
/// @return The terminal width in columns or -1 when it cannot be read
static int get_terminal_columns(void)
{
    struct winsize size;

    if (ioctl(STDOUT_FILENO, TIOCGWINSZ, &size) == -1 || size.ws_col == 0)
    {
        return -1;
    }

    return size.ws_col;
}

/// Applies a pending terminal resize to linenoise
/// @param editor Pointer to the native line editor context
/// @return 0 when no resize is pending or the resize succeeds, otherwise -1
static int apply_pending_resize(majo_line_editor *editor)
{
    if (!editor->resize_pending)
    {
        return 0;
    }

    if (linenoiseEditResize(&editor->state, editor->pending_columns) == -1)
    {
        return -1;
    }

    editor->resize_pending = 0;
    return 0;
}

/// Writes an entire buffer to a file descriptor
/// @param fd Destination file descriptor
/// @param data Buffer to write
/// @param length Number of bytes to write
/// @return 0 on success or -1 on failure
static int write_all(int fd, const char *data, size_t length)
{
    // Number of bytes already written
    size_t written = 0;

    // Continue until the complete buffer has been written
    while (written < length)
    {
        // Write the remaining bytes
        ssize_t result = write(fd, data + written, length - written);
        
        // Handle a failed write
        if (result < 0)
        {
            // Retry writes interrupted by a signal
            if (errno == EINTR)
            {
                continue;
            }

            return -1;
        }

        written += (size_t)result;
    }

    return 0;
}

/// Detects a change in terminal width and records it for deferred recovery
/// @param editor Pointer to the native line editor context
/// @return 1 when a resize is detected, 0 when the width is unchanged, or -1 on failure
static int detect_terminal_resize(majo_line_editor *editor)
{
    int cols = get_terminal_columns();
    
    if (cols <= 0)
    {
        return -1;
    }
    
    if (cols == editor->terminal_columns)
    {
        return 0;
    }

    editor->terminal_columns = cols;
    editor->pending_columns = cols;
    editor->resize_pending = 1;

    return 1;
}

/// Hides the active input area after resolving any concurrent resize
/// @param editor Pointer to the native line editor context
/// @return 0 on success or -1 on failure
static int hide_for_write_above(majo_line_editor *editor)
{
    while (1)
    {
        if (detect_terminal_resize(editor) == -1)
        {
            return -1;
        }

        if (apply_pending_resize(editor) == -1)
        {
            return -1;
        }

        linenoiseHide(&editor->state);

        /*
         * Resize may happen after recovery but before or during Hide()
         *
         * If that happened, the single-line viewport may have been reflowed
         * into multiple physical rows while Hide() only cleared one of them
         *
         * Detect it immediately so the next iteration can use the existing resize
         * recovery to clear the partially hidden old viewport, redraw it once,
         * and then attempt Hide() again
         */
        int resize_result = detect_terminal_resize(editor);

        if (resize_result < 0)
        {
            return -1;
        }

        if (resize_result == 0)
        {
            return 0;
        }
    }
}

/// Synchronizes linenoise with the terminal width while the input area is hidden
/// @param editor Pointer to the native line editor context
/// @return 0 on success or -1 on failure
static int synchronize_hidden_resize(majo_line_editor *editor)
{
    int columns = get_terminal_columns();

    if (columns <= 0)
    {
        return -1;
    }

    editor->terminal_columns = columns;
    editor->pending_columns = columns;
    editor->resize_pending = 0;

    /*
     * The input area is currently hidden, so there is no old rendered region
     * to recover, so linenoise can use the current width when Show()
     * rebuilds the input area
     */
    editor->state.cols = columns;

    if (editor->multi_line)
    {
        editor->state.oldrows = 0;
        editor->state.oldrpos = 1;
    }

    return 0;
}

/// Creates a native line editor context
/// @param command_buffer_size Maximum command size in bytes
/// @param multi_line Nonzero to enable multiline mode
/// @return A context handle on success or NULL on failure
void *majo_line_editor_create(int command_buffer_size, int multi_line)
{
        
    if (command_buffer_size <= 0)
    {
        errno = EINVAL;
        return NULL;
    }
    
    if (!isatty(STDIN_FILENO) || !isatty(STDOUT_FILENO))
    {
        errno = ENOTTY;
        return NULL;
    }

    majo_line_editor *editor = calloc(1, sizeof(majo_line_editor));
    
    if (editor == NULL)
    {
        return NULL;
    }
    
    editor->multi_line = multi_line != 0;
    editor->buffer_size = (size_t)command_buffer_size + 1; // +1 for null terminator
    editor->buffer = malloc(editor->buffer_size);
    
    if (editor->buffer == NULL)
    {
        free(editor);
        return NULL;
    }
    
    return editor;
}

/// Destroys a native line editor context and releases its resources
/// @param handle Native context handle
void majo_line_editor_destroy(void *handle)
{
    if (handle == NULL)
    {
        return;
    }

    majo_line_editor *editor = handle;

    // Stop an active editing session before releasing its state
    if (editor->editing)
    {
        linenoiseEditStop(&editor->state);
        editor->editing = 0;
    }

    free(editor->prompt);
    free(editor->buffer);
    free(editor);
}

/// Starts a native editing session
/// @param handle Native context handle
/// @param prompt Prompt displayed before editable input
/// @return 0 on success or -1 on failure
int majo_line_editor_start(void *handle, const char *prompt)
{
    if (handle == NULL || prompt == NULL)
    {
        errno = EINVAL;
        return -1;
    }

    majo_line_editor *editor = handle;

    // Reject a second start while an editing session is active
    if (editor->editing)
    {
        errno = EBUSY;
        return -1;
    }
    
    // Replace any prompt retained from a previous session
    free(editor->prompt);
    editor->prompt = strdup(prompt);
    
    if (editor->prompt == NULL)
    {
        return -1;
    }

    linenoiseSetMultiLine(editor->multi_line);
    
    // Start linenoise with the retained state, input buffer, and prompt
    if (-1 == linenoiseEditStart(&editor->state, STDIN_FILENO, STDOUT_FILENO, 
        editor->buffer, editor->buffer_size, editor->prompt))
    {
        int saved_errno = errno;
        free(editor->prompt);
        editor->prompt = NULL;
        
        errno = saved_errno;
        return -1;
    }

    editor->editing = 1;
    editor->terminal_columns = (int)editor->state.cols;
    editor->pending_columns = editor->terminal_columns;
    editor->resize_pending = 0;
    
    return 0;
}

/// Stops the current native editing session
/// @param handle Native context handle
void majo_line_editor_stop(void *handle)
{
    if (handle == NULL)
    {
        return;
    }

    majo_line_editor *editor = handle;

    if (editor->editing)
    {
        linenoiseEditStop(&editor->state);

        editor->editing = 0;
    }
    
    free(editor->prompt);
    editor->prompt = NULL;
}

/// Waits for terminal input
/// @param handle Native context handle
/// @param timeout_ms Timeout in milliseconds or a negative value to wait indefinitely
/// @return 1 when input is available, 0 on timeout or interruption, or -1 on failure
int majo_line_editor_wait_for_input(void *handle, int timeout_ms)
{
    if (handle == NULL || timeout_ms < 0)
    {
        errno = EINVAL;
        return -1;
    }

    majo_line_editor *editor = handle;

    if (!editor->editing)
    {
        errno = EINVAL;
        return -1;
    }
    
    struct pollfd descriptor = 
    {
        .fd = editor->state.ifd,
        .events = POLLIN,
        .revents = 0
    };
    
    int result = poll(&descriptor, 1, timeout_ms);
    
    if (result < 0)
    {
        if (errno == EINTR)
        {
            return 0; // An interrupted poll has no available input
        }
        
        return -1;
    }
    
    if (result == 0)
    {
        return 0; // The poll timed out
    }
    
    if (descriptor.revents & (POLLIN | POLLHUP))
    {
        return 1; // Input is available
    }
    
    if (descriptor.revents & (POLLERR | POLLNVAL))
    {
        errno = EIO; // The descriptor reported an I/O error
        return -1;
    }
    
    return 0; // No input is available
}

/// Checks whether the terminal width changed and schedules recovery when needed
/// @param handle Native context handle
/// @return 1 when a resize is detected, 0 when no work is needed, or -1 on failure
int majo_line_editor_handle_resize(void *handle)
{
    if (handle == NULL)
    {
        errno = EINVAL;
        return -1;
    }

    majo_line_editor *editor = handle;

    if (!editor->editing)
    {
        return 0;
    }
    
    return detect_terminal_resize(editor);
}

/// Feeds available input to linenoise
/// @param handle Native context handle
/// @param line Receives an allocated accepted line or NULL when no line is available
/// @return A MAJO_LINE_EDITOR status value describing the result
int majo_line_editor_feed(void *handle, char **line)
{
    if (handle == NULL || line == NULL)
    {
        errno = EINVAL;
        return MAJO_LINE_EDITOR_ERROR;
    }

    majo_line_editor *editor = handle;

    if (!editor->editing)
    {
        errno = EINVAL;
        return MAJO_LINE_EDITOR_ERROR;
    }

    *line = NULL;
    
    if (apply_pending_resize(editor) == -1)
    {
        return MAJO_LINE_EDITOR_ERROR;
    }
    
    // linenoise reports Ctrl+C through errno set to EAGAIN
    // Clear errno first to avoid observing a stale value
    errno = 0;
    
    // Feed available terminal input into linenoise
    char *result = linenoiseEditFeed(&editor->state);

    // Preserve errno before another function call can overwrite it
    int saved_errno = errno;
    
    if (result == linenoiseEditMore)
    {
        return MAJO_LINE_EDITOR_MORE;
    }

    if (result != NULL)
    {
        *line = result;
        return  MAJO_LINE_EDITOR_ACCEPTED;
    }
    
    // Ctrl+C must not make linenoise stop the editor directly
    // Notify the managed layer and let application lifetime control final shutdown
    if (saved_errno == EAGAIN)
    {
        return MAJO_LINE_EDITOR_INTERRUPTED;
    }
    
    // Treat ENOENT or zero as end of input
    if (saved_errno == ENOENT || saved_errno == 0)
    {
        return MAJO_LINE_EDITOR_END_OF_INPUT;
    }
    
    errno = saved_errno;
    return MAJO_LINE_EDITOR_ERROR;
}

/// Frees a line returned by majo_line_editor_feed
/// @param line Pointer to the allocated line
void majo_line_editor_free_line(char *line)
{
    if (line != NULL)
    {
        linenoiseFree(line);
    }
}

/// Writes content above the active input area without changing its logical state
/// @param handle Native context handle
/// @param content Null-terminated content to write
/// @param new_line Nonzero to append a newline
/// @return 0 on success or -1 on failure
int majo_line_editor_write_above(void *handle, const char *content, int new_line)
{
    if (content == NULL || handle == NULL)
    {
        errno = EINVAL;
        return -1;
    }

    majo_line_editor *editor = handle;
    
    if (editor->editing)
    {
        if (hide_for_write_above(editor) == -1)
        {
            return -1;
        }
    }

    // Write content to standard output
    int result = write_all(STDOUT_FILENO, content, strlen(content));

    if (result == 0 && new_line)
    {
        result = write_all(STDOUT_FILENO, "\n", 1);
    }
    
    if (editor->editing)
    {
        /*
         * The terminal may have resized while the input area was hidden
         * No recovery is necessary in that state; Show() just needs the
         * current width
         */
        if (synchronize_hidden_resize(editor) == -1)
        {
            return -1;
        }
        
        linenoiseShow(&editor->state);
    }

    return result;
}

/// Initializes process-global linenoise history for this editor
/// @param handle Native context handle
/// @param entry_count Maximum number of retained history entries
/// @return 0 on success or -1 on failure
int majo_line_editor_prepare_history(void *handle, int entry_count)
{
    if (handle == NULL || entry_count < 0 || entry_count == INT_MAX)
    {
        errno = EINVAL;
        return -1;
    }

    /*
     * linenoise has process-global history and no public clear API
     *
     * Reduce it to one item first, then insert an empty sentinel to leave
     * exactly one empty entry regardless of the previous LineEditor instance
     *
     * We then resize to entry_count + 1 so the caller can seed the real history
     * entries afterward and linenoiseEditStart() finally adds its own temporary
     * empty entry, which evicts this sentinel when real history exists
     */
    if (!linenoiseHistorySetMaxLen(1))
    {
        errno = ENOMEM;
        return -1;
    }
    
    linenoiseHistoryAdd(""); // sentinel
    
    if (!linenoiseHistorySetMaxLen(entry_count + 1))
    {
        errno = ENOMEM;
        return -1;
    }
    
    return 0;
}

/// Adds a line to process-global linenoise history
/// @param handle Native context handle
/// @param line Nonempty line to add
/// @return 0 on success or -1 on failure
int majo_line_editor_history_add(void *handle, const char *line)
{
    if (handle == NULL || line == NULL || line[0] == '\0')
    {
        errno = EINVAL;
        return -1;
    }

    if (!linenoiseHistoryAdd(line))
    {
        errno = ENOMEM;
        return -1;
    }

    return 0;
}
