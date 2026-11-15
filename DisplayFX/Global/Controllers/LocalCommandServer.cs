using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayFX.Global.Controllers;

public sealed class LocalCommandServer : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<LocalCommand, Task<LocalResponse>> _handle;
    private readonly Task _worker;
    public LocalCommandServer(Func<LocalCommand, Task<LocalResponse>> handle) { _handle = handle; _worker = Run(); }
    private async Task Run()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(LocalCommandProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_shutdown.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var command = await LocalCommandProtocol.Read<LocalCommand>(server, 8192, timeout.Token);
                var response = LocalCommandProtocol.IsAllowed(command) ? await _handle(command) : new LocalResponse(false, "Unsupported local command.");
                using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token); writeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await LocalCommandProtocol.Write(server, response, writeTimeout.Token);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Debug(ex, "Local command connection ended.");
                try { await Task.Delay(250, _shutdown.Token); } catch (OperationCanceledException) { break; }
            }
        }
    }
    public void Dispose() => _shutdown.Cancel();
}
