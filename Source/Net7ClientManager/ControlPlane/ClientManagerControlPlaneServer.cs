namespace Net7ClientManager.ControlPlane;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Net7ClientManager.ControlPlane.Contracts;

internal sealed class ClientManagerControlPlaneServer : IDisposable
{
    private readonly ClientManagerControlPlaneService service;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentDictionary<int, Task> connections = new();
    private readonly JsonSerializerOptions jsonOptions =
        ControlPlaneProtocol.CreateJsonOptions();
    private Task? acceptLoop;
    private int nextConnectionId;
    private bool disposed;

    public ClientManagerControlPlaneServer(
        ClientManagerControlPlaneService service)
    {
        this.service = service;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        this.acceptLoop ??= Task.Run(
            () => this.AcceptLoopAsync(this.cancellation.Token));
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.cancellation.Cancel();

        try
        {
            this.acceptLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception) when (
            exception is AggregateException or OperationCanceledException)
        {
            Debug.WriteLine(
                $"[ControlPlane] Server shutdown: {exception.Message}");
        }

        try
        {
            Task.WaitAll(
                [.. this.connections.Values],
                TimeSpan.FromSeconds(2));
        }
        catch (Exception exception) when (
            exception is AggregateException or OperationCanceledException)
        {
            Debug.WriteLine(
                $"[ControlPlane] Connection shutdown: {exception.Message}");
        }

        this.cancellation.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;

            try
            {
                pipe = CreateServerPipe();

                await pipe.WaitForConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

                var connectionId = Interlocked.Increment(
                    ref this.nextConnectionId);
                var connectedPipe = pipe;
                pipe = null;
                var task = this.HandleConnectionAsync(
                    connectedPipe,
                    cancellationToken);
                this.connections[connectionId] = task;
                _ = task.ContinueWith(
                    completed => this.connections.TryRemove(
                        connectionId,
                        out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception exception)
            {
                pipe?.Dispose();
                Debug.WriteLine(
                    $"[ControlPlane] Accept failed: {exception}");

                try
                {
                    await Task.Delay(
                            TimeSpan.FromMilliseconds(500),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private static NamedPipeServerStream CreateServerPipe()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User ??
                      throw new InvalidOperationException(
                          "The current Windows user could not be identified.");

        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetOwner(userSid);
        pipeSecurity.SetAccessRuleProtection(
            isProtected: true,
            preserveInheritance: false);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            userSid,
            PipeAccessRights.ReadWrite |
            PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        // Net7 Client Manager runs elevated. Give this IPC object medium
        // integrity so a normal process owned by the same signed-in user can
        // call the deliberately exposed control-plane operations. Low-integrity
        // sandboxed processes remain unable to write to it.
        pipeSecurity.SetSecurityDescriptorSddlForm(
            "S:(ML;;NW;;;ME)",
            AccessControlSections.Audit);

        // Applying a mandatory integrity label writes the pipe object's SACL.
        // Windows keeps SeSecurityPrivilege disabled until it is explicitly
        // enabled, even for an elevated administrator process. Enable it only
        // for the duration of pipe creation, then restore the token state.
        using var securityPrivilege = WindowsPrivilegeScope.Enable(
            "SeSecurityPrivilege");

        return NamedPipeServerStreamAcl.Create(
            ControlPlaneProtocol.GetPipeName(),
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: pipeSecurity,
            inheritability: HandleInheritability.None,
            additionalAccessRights: (PipeAccessRights)0);
    }

    private sealed class WindowsPrivilegeScope : IDisposable
    {
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint PrivilegeEnabled = 0x00000002;
        private const int ErrorNotAllAssigned = 1300;

        private readonly nint tokenHandle;
        private readonly TokenPrivileges previousState;
        private bool disposed;

        private WindowsPrivilegeScope(
            nint tokenHandle,
            TokenPrivileges previousState)
        {
            this.tokenHandle = tokenHandle;
            this.previousState = previousState;
        }

        public static WindowsPrivilegeScope Enable(string privilegeName)
        {
            if (!OpenProcessToken(
                    GetCurrentProcess(),
                    TokenAdjustPrivileges | TokenQuery,
                    out var tokenHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!LookupPrivilegeValue(
                        systemName: null,
                        privilegeName: privilegeName,
                        luid: out var privilegeLuid))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var requestedState = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Privileges = new LuidAndAttributes
                    {
                        Luid = privilegeLuid,
                        Attributes = PrivilegeEnabled,
                    },
                };

                if (!AdjustTokenPrivileges(
                        tokenHandle,
                        disableAllPrivileges: false,
                        ref requestedState,
                        (uint)Marshal.SizeOf<TokenPrivileges>(),
                        out var previousState,
                        out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotAllAssigned)
                {
                    throw new Win32Exception(
                        error,
                        $"The current process does not hold {privilegeName}.");
                }

                return new WindowsPrivilegeScope(
                    tokenHandle,
                    previousState);
            }
            catch
            {
                CloseHandle(tokenHandle);
                throw;
            }
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            var state = this.previousState;
            _ = AdjustTokenPrivileges(
                this.tokenHandle,
                disableAllPrivileges: false,
                ref state,
                bufferLength: (uint)Marshal.SizeOf<TokenPrivileges>(),
                out _,
                out _);
            _ = CloseHandle(this.tokenHandle);
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint handle);

        [DllImport("kernel32.dll")]
        private static extern nint GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(
            nint processHandle,
            uint desiredAccess,
            out nint tokenHandle);

        [DllImport(
            "advapi32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(
            string? systemName,
            string privilegeName,
            out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(
            nint tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TokenPrivileges newState,
            uint bufferLength,
            out TokenPrivileges previousState,
            out uint returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LuidAndAttributes
        {
            public Luid Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public LuidAndAttributes Privileges;
        }
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        using (pipe)
        using (var reader = new StreamReader(
                   pipe,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 4096,
                   leaveOpen: true))
        using (var writer = new StreamWriter(
                   pipe,
                   new UTF8Encoding(
                       encoderShouldEmitUTF8Identifier: false),
                   bufferSize: 4096,
                   leaveOpen: true)
               {
                   AutoFlush = true,
               })
        {
            while (pipe.IsConnected &&
                   !cancellationToken.IsCancellationRequested)
            {
                string? line;

                try
                {
                    line = await reader.ReadLineAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (IOException)
                {
                    return;
                }

                if (line == null)
                {
                    return;
                }

                ControlPlaneResponse response;

                try
                {
                    var request = JsonSerializer.Deserialize<ControlPlaneRequest>(
                        line,
                        this.jsonOptions);

                    response = request == null
                        ? CreateProtocolFailure(
                            "",
                            "The request was empty.")
                        : await this.service.ExecuteAsync(
                                request,
                                cancellationToken)
                            .ConfigureAwait(false);
                }
                catch (JsonException exception)
                {
                    response = CreateProtocolFailure(
                        "",
                        $"The request was not valid JSON: {exception.Message}");
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(
                        $"[ControlPlane] Request failed: {exception}");
                    response = new ControlPlaneResponse
                    {
                        ExitCode = ControlPlaneExitCode.InternalError,
                        Code = ControlPlaneProtocol.GetDefaultResultCode(
                            ControlPlaneExitCode.InternalError),
                        Message = "Net7 Client Manager could not complete the request.",
                    };
                }

                try
                {
                    await writer.WriteLineAsync(
                            JsonSerializer.Serialize(
                                response,
                                this.jsonOptions))
                        .ConfigureAwait(false);
                }
                catch (IOException)
                {
                    return;
                }
            }
        }
    }

    private static ControlPlaneResponse CreateProtocolFailure(
        string requestId,
        string message)
    {
        return new ControlPlaneResponse
        {
            RequestId = requestId,
            ExitCode = ControlPlaneExitCode.InvalidArguments,
            Code = ControlPlaneProtocol.GetDefaultResultCode(
                ControlPlaneExitCode.InvalidArguments),
            Message = message,
        };
    }
}
