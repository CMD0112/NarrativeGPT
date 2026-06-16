using System.IO.Pipes;
using System.Text.Json;
using ChatGPTWrapper.Core.SessionHost;

namespace ChatGPTWrapper.SessionHost;

internal static class Program
{
    public const string DefaultPipeName = "ChatGPTWrapper.SessionHost";

    public static async Task Main(string[] args)
    {
        var pipeName = args.FirstOrDefault() ?? DefaultPipeName;
        Console.WriteLine($"ChatGPT Wrapper Session Host listening on \\\\.\\pipe\\{pipeName}");
        Console.WriteLine("Out-of-process WebView host stub — use in-process ChatGptSessionHost in the WPF shell for full functionality.");

        while (true)
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync();
            try
            {
                using var reader = new StreamReader(server, leaveOpen: true);
                await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                SessionHostRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<SessionHostRequest>(line);
                }
                catch
                {
                    request = null;
                }

                var response = new SessionHostResponse
                {
                    Id = request?.Id,
                    Ok = false,
                    Error = "oop_host_not_configured",
                    Result = new
                    {
                        message = "Use ChatGptSessionHost in-process. This executable exposes the RPC pipe contract for future isolation.",
                        supportedMethods = new[]
                        {
                            SessionHostRpcMethods.EnsureReady,
                            SessionHostRpcMethods.SendMessage,
                            SessionHostRpcMethods.Regenerate,
                            SessionHostRpcMethods.CaptureAssistant,
                            SessionHostRpcMethods.DiscoverProjects,
                            SessionHostRpcMethods.SyncSources,
                        },
                    },
                };

                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }
    }
}
