using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace HexWin.Output;

/// <summary>
/// A copy of the clipboard, to be restored after a paste.
///
/// <para><b>Why a copy and not a reference.</b>
/// <c>Clipboard.GetDataObject()</c> does not hand back the data but an object
/// pointing at the current clipboard content. As soon as something is written
/// over it, that reference designates nothing: "restoring" it empties the
/// clipboard instead of putting it back. The data therefore has to be
/// extracted while it is still there.</para>
///
/// <para>Three shapes are covered — text, image, file list — which spans
/// nearly every use. An exotic format is not saved: better to restore nothing
/// than to write something wrong.</para>
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Win32 shell: the clipboard is a global desktop resource.")]
internal sealed class ClipboardSnapshot
{
    private readonly string? _text;
    private readonly Image? _image;
    private readonly StringCollection? _files;

    private ClipboardSnapshot(string? text, Image? image, StringCollection? files)
    {
        _text = text;
        _image = image;
        _files = files;
    }

    /// <summary>True if something could be copied, and so restored later.</summary>
    public bool HasContent => _text is not null || _image is not null || _files is not null;

    /// <summary>
    /// Extracts the current clipboard content. Returns an empty snapshot if
    /// the clipboard is locked by another application: dictation takes
    /// priority over restoration.
    /// </summary>
    public static ClipboardSnapshot Capture()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                return new ClipboardSnapshot(Clipboard.GetText(), null, null);
            }

            if (Clipboard.ContainsImage())
            {
                return new ClipboardSnapshot(null, Clipboard.GetImage(), null);
            }

            if (Clipboard.ContainsFileDropList())
            {
                return new ClipboardSnapshot(null, null, Clipboard.GetFileDropList());
            }
        }
        catch (ExternalException)
        {
            // Clipboard busy in another application.
        }

        return new ClipboardSnapshot(null, null, null);
    }

    /// <summary>
    /// Writes the saved content back, or empties the clipboard if nothing
    /// could be saved.
    ///
    /// <para>The emptying matters as much as the restoring. Without it, a
    /// clipboard that started out empty — or was locked by another application
    /// at capture time — left the dictated text in place indefinitely. No
    /// special privilege is needed to cause that: a program merely has to hold
    /// the clipboard open during the dictation.</para>
    /// </summary>
    public void Restore()
    {
        if (!HasContent)
        {
            ClearSafely();
            return;
        }

        try
        {
            if (_files is not null)
            {
                Clipboard.SetFileDropList(_files);
                return;
            }

            object? content = (object?)_text ?? _image;

            if (content is not null)
            {
                // copy: true asks Windows to keep the data after the process
                // ends. Without that flag, the clipboard would empty itself
                // when the application closes — exactly what we were trying to
                // avoid.
                Clipboard.SetDataObject(content, copy: true);
            }
        }
        catch (ExternalException)
        {
            // Clipboard busy: give up quietly rather than fail an otherwise
            // successful dictation.
        }
    }

    private static void ClearSafely()
    {
        try
        {
            Clipboard.Clear();
        }
        catch (ExternalException)
        {
            // Same again: busy, so give up.
        }
    }
}
