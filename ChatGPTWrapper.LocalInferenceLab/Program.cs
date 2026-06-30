using ChatGPTWrapper.Core.LocalInference;

namespace ChatGPTWrapper.LocalInferenceLab;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].ToLowerInvariant();
        LocalInferenceOptions options;
        string[] remainder;
        try
        {
            options = ParseOptions(args.Skip(1).ToArray(), out var positional);
            remainder = positional.ToArray();
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        using var client = new OpenAiCompatibleChatClient(options);

        return command switch
        {
            "probe" => await RunProbeAsync(client),
            "chat" => await RunChatAsync(client, remainder),
            "entity-demo" => await RunEntityDemoAsync(client),
            _ => UnknownCommand(command),
        };
    }

    private static async Task<int> RunProbeAsync(OpenAiCompatibleChatClient client)
    {
        Console.WriteLine($"Probing {client.Options.NormalizeBaseUrl()} …");
        Console.WriteLine($"Configured model: {client.Options.Model}");
        Console.WriteLine();

        var health = await client.ProbeAsync();
        if (!health.Reachable)
        {
            Console.Error.WriteLine($"Unreachable: {health.Error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Start Ollama (ollama serve) and pull a model, e.g.:");
            Console.Error.WriteLine("  ollama pull qwen2.5:7b-instruct");
            return 2;
        }

        Console.WriteLine("Server reachable.");
        Console.WriteLine($"Models ({health.Models.Count}):");
        foreach (var model in health.Models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  - {model}");

        Console.WriteLine();
        if (health.RequestedModelAvailable)
            Console.WriteLine($"Requested model '{health.RequestedModel}' is available.");
        else
        {
            Console.WriteLine($"Requested model '{health.RequestedModel}' was NOT found.");
            Console.WriteLine("Set CGW_OLLAMA_MODEL or pass --model to match an installed tag.");
            return 3;
        }

        return 0;
    }

    private static async Task<int> RunChatAsync(OpenAiCompatibleChatClient client, IReadOnlyList<string> positional)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("Usage: chat <message> [--model name] [--url base]");
            return 1;
        }

        var message = string.Join(' ', positional);
        Console.WriteLine($"Model: {client.Options.Model}");
        Console.WriteLine($"User: {message}");
        Console.WriteLine();

        var result = await client.CompleteAsync(new ChatCompletionRequest
        {
            Model = client.Options.Model,
            Messages = [ChatMessage.User(message)],
            Temperature = 0.7,
        });

        return PrintCompletion(result);
    }

    private static async Task<int> RunEntityDemoAsync(OpenAiCompatibleChatClient client)
    {
        Console.WriteLine($"Model: {client.Options.Model}");
        Console.WriteLine("Scenario: entity extraction (utility-job shape, lab only)");
        Console.WriteLine();

        var result = await client.CompleteAsync(LocalInferenceLabScenarios.EntityExtractionDemo(client.Options.Model));
        return PrintCompletion(result);
    }

    private static int PrintCompletion(ChatCompletionResult result)
    {
        if (!result.Success)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return 4;
        }

        if (!string.IsNullOrWhiteSpace(result.Model))
            Console.WriteLine($"Model: {result.Model}");
        if (result.PromptTokens is not null || result.CompletionTokens is not null)
            Console.WriteLine($"Tokens: prompt={result.PromptTokens ?? 0} completion={result.CompletionTokens ?? 0}");
        Console.WriteLine();
        Console.WriteLine(result.Content);
        return 0;
    }

    private static LocalInferenceOptions ParseOptions(string[] args, out List<string> positional)
    {
        var baseOptions = LocalInferenceOptions.FromEnvironment();
        var baseUrl = baseOptions.BaseUrl;
        var model = baseOptions.Model;
        positional = [];

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--url" or "-u")
            {
                if (++i >= args.Length)
                    throw new ArgumentException("Missing value for --url");
                baseUrl = args[i];
                continue;
            }

            if (arg is "--model" or "-m")
            {
                if (++i >= args.Length)
                    throw new ArgumentException("Missing value for --model");
                model = args[i];
                continue;
            }

            positional.Add(arg);
        }

        return new LocalInferenceOptions
        {
            BaseUrl = baseUrl,
            Model = model,
        };
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "help";

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            ChatGPT Wrapper — Local Inference Lab
            Standalone Ollama / OpenAI-compatible server tester (not wired into the main app).

            Environment:
              CGW_OLLAMA_BASE_URL   default http://127.0.0.1:11434
              CGW_OLLAMA_MODEL      default qwen2.5:7b-instruct

            Commands:
              probe                 Check server reachability and list models
              chat <message>        Send a single user message
              entity-demo           Run utility-style entity extraction sample

            Options (all commands):
              --url, -u <base>      Server base URL
              --model, -m <name>    Model tag

            Examples:
              dotnet run --project ChatGPTWrapper.LocalInferenceLab -- probe
              dotnet run --project ChatGPTWrapper.LocalInferenceLab -- chat "Say hello in one sentence."
              dotnet run --project ChatGPTWrapper.LocalInferenceLab -- entity-demo --model llama3.2
            """);
    }
}
