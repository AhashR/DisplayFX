using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DisplayFX.Global.Controllers;

public sealed record LocalCommand(string Action, Guid? ProfileId = null);
public sealed record LocalProfile(Guid Id, string Monitor, string Name, bool Active);
public sealed record LocalResponse(bool Success, string Message, LocalProfile[]? Profiles = null);
public static class LocalCommandProtocol
{
    public static string PipeName => "DisplayFX." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    public static bool IsAllowed(LocalCommand command) => command.Action == "list" && !command.ProfileId.HasValue || command.Action == "apply" && command.ProfileId.HasValue && command.ProfileId != Guid.Empty;
    public static async Task Write<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Command response is too large.");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    public static async Task<T> Read<T>(Stream stream, int maximum, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        var length = BitConverter.ToInt32(header);
        if (length <= 0 || length > maximum) throw new InvalidDataException("Invalid command frame length.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Missing command payload.");
    }
    public static async Task<LocalResponse> Send(LocalCommand command, CancellationToken token)
    {
        if (!IsAllowed(command)) throw new InvalidDataException("Allowed commands: list; apply <profile-id>.");
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(3000, token);
        await Write(client, command, token); return await Read<LocalResponse>(client, 1024 * 1024, token);
    }
}
