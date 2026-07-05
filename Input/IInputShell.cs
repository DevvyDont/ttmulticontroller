using System;

namespace TTMulti.Input
{
    /// <summary>
    /// What <see cref="InputCaptureHost"/> needs from the UI shell that hosts it — deliberately small so both
    /// the WinForms main window and the WPF main window can provide it. The shell supplies an HWND (RegisterHotKey
    /// target / activation target), UI-thread marshalling, UI-thread timers, and user-facing notifications.
    /// </summary>
    internal interface IInputShell
    {
        /// <summary>
        /// The shell window's HWND. Only read on the UI thread (the host marshals via <see cref="SafeInvoke"/>
        /// before reading it from background threads).
        /// </summary>
        IntPtr Handle { get; }

        /// <summary>Fire-and-forget marshal onto the UI thread. Must be safe to call from hook callbacks.</summary>
        void BeginInvoke(Action action);

        /// <summary>
        /// Synchronously marshal onto the UI thread, returning false instead of throwing when the shell is
        /// gone (disposed / handle destroyed). Used by the activation thread (CORR-01 semantics).
        /// </summary>
        bool SafeInvoke(Action action);

        /// <summary>Create a stopped UI-thread timer that raises <paramref name="tick"/> every interval.</summary>
        IUiTimer CreateTimer(int intervalMs, Action tick);

        /// <summary>Bring the shell window to front after the activation thread has done the Win32 dance
        /// (TopMost pulse + Activate, restoring the configured on-top state).</summary>
        void FinishActivation();

        /// <summary>Show a non-fatal warning to the user (e.g. hotkey registration failures).</summary>
        void ShowWarning(string message, string title);
    }

    /// <summary>A UI-thread timer abstraction (WinForms Timer / WPF DispatcherTimer).</summary>
    internal interface IUiTimer : IDisposable
    {
        bool Enabled { get; }
        void Start();
        void Stop();
    }
}
