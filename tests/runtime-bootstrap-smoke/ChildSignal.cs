using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Flowzer.Smoke;

/// <summary>Eine bereits gebundene Kernelidentität, niemals eine erneut aufgelöste numerische PID.</summary>
internal interface IChildSignal : IDisposable
{
    void Send(int signal);
}

internal static class ChildSignal
{
    internal static void ValidateReaper(int processId, IntPtr handler, int flags)
    {
        if (processId == 1 || handler != IntPtr.Zero || (flags & 2) != 0)
            throw new SmokeFailure(Failure.Cleanup);
    }

    /// <summary>
    /// Vor JEDEM ersten Process.Start dieses Feeders: Linux-x64/glibc-SIGCHLD muss noch DFL
    /// ohne SA_NOCLDWAIT sein. Zusammen mit Nicht-PID1 verhindert dies .NET10 reapAll.
    /// Die enge Plattformbindung ist im Liveadapter bereits geprüft; keine Signalzustandsänderung.
    /// </summary>
    internal static void VerifyReaper()
    {
        var action = new byte[152]; // glibc Linux-x64: handler0, sigset8..135, flags136, restorer144.
        if (ReadAction(17, IntPtr.Zero, action) != 0) throw new SmokeFailure(Failure.Cleanup);
        ValidateReaper(Environment.ProcessId, new IntPtr(BitConverter.ToInt64(action, 0)), BitConverter.ToInt32(action, 136));
    }

    [DllImport("libc", EntryPoint = "sigaction", SetLastError = true)]
    private static extern int ReadAction(int signal, IntPtr action, [Out] byte[] oldAction);

    /// <summary>
    /// Erst pidfd öffnen, danach den ursprünglichen .NET-Kindzustand prüfen. In .NET 10 sind
    /// Reaping und SetExited unter derselben ProcessWaitState-Sperre wie HasExited geschützt.
    /// Wurde vor dem Öffnen bereits gereapt/recycelt, wird der möglicherweise fremde fd verworfen.
    /// Ohne diesen Nachtest wäre auch pidfd_open(PID) allein nicht wiederverwendungssicher.
    /// </summary>
    internal static IChildSignal? Acquire(Func<IChildSignal> open, Func<bool> originalExited)
    {
        var handle = open();
        try
        {
            if (!originalExited()) return handle;
        }
        catch { handle.Dispose(); throw; }
        handle.Dispose();
        return null;
    }

    internal static IChildSignal? Acquire(System.Diagnostics.Process process) =>
        Acquire(() => new LinuxChildSignal(process.Id), () => process.HasExited);

    /// <summary>Fehlende Kernel/libc-Unterstützung ist ein Fehler, kein Anlass für kill(PID).</summary>
    private sealed class LinuxChildSignal : IChildSignal
    {
        private readonly SafeFileHandle handle;
        internal LinuxChildSignal(int processId)
        {
            if (!OperatingSystem.IsLinux()) throw new SmokeFailure(Failure.Cleanup);
            var fd = Open(processId, 0);
            if (fd < 0) throw new SmokeFailure(Failure.Cleanup);
            handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        }
        public void Send(int signal)
        {
            // SafeHandle hält den fd während des nativen Aufrufs offen. ESRCH heißt: genau das
            // gebundene Kind ist bereits beendet; eine neue gleichnamige PID wird nie adressiert.
            if (Signal(handle, signal, IntPtr.Zero, 0) != 0 && Marshal.GetLastPInvokeError() != 3)
                throw new SmokeFailure(Failure.Cleanup);
        }
        public void Dispose() => handle.Dispose();

        [DllImport("libc", EntryPoint = "pidfd_open", SetLastError = true)]
        private static extern int Open(int processId, uint flags);
        [DllImport("libc", EntryPoint = "pidfd_send_signal", SetLastError = true)]
        private static extern int Signal(SafeFileHandle handle, int signal, IntPtr info, uint flags);
    }
}
