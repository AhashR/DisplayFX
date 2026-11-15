using DisplayFX.Global.Controllers;
using System.Text.Json;

try
{
    LocalCommand? command = args.Length == 1 && args[0] == "list" ? new("list") :
        args.Length == 2 && args[0] == "apply" && Guid.TryParse(args[1], out var id) ? new("apply", id) : null;
    if (command == null)
    {
        Console.Error.WriteLine("Usage: DisplayFX.Cli list | apply <profile-id>\nDisplayFX must be running in this Windows session.");
        return 2;
    }
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var result = await LocalCommandProtocol.Send(command, timeout.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    return result.Success ? 0 : 1;
}
catch (Exception ex) { Console.Error.WriteLine("Local command failed: " + ex.Message); return 1; }
