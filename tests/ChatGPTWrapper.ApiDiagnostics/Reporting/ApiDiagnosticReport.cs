using System.Text;
using System.Text.Json;

namespace ChatGPTWrapper.ApiDiagnostics.Reporting;

public sealed class ApiDiagnosticReport
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string? UserDataFolder { get; set; }

    public string? WebViewSource { get; set; }

    public List<ApiDiagnosticStep> Steps { get; } = [];

    public int PassedCount => Steps.Count(s => s.Pass);

    public int FailedCount => Steps.Count(s => !s.Pass);

    public IReadOnlyList<string> FailedStepIds =>
        Steps.Where(s => !s.Pass).Select(s => s.Id).ToList();

    public string? FirstFailedStepId => FailedStepIds.FirstOrDefault();

    public void AddStep(ApiDiagnosticStep step) => Steps.Add(step);

    public static string ReportJsonPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "api-diagnostic-report.json");

    public static string ReportTextPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "api-diagnostic-report.txt");

    public void WriteToDisk()
    {
        var dir = Path.GetDirectoryName(ReportJsonPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ReportJsonPath, json, Encoding.UTF8);
        File.WriteAllText(ReportTextPath, BuildTextSummary(), Encoding.UTF8);
    }

    public string BuildTextSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ChatGPT Wrapper API diagnostic report — {Timestamp:u}");
        sb.AppendLine($"User data: {UserDataFolder ?? "(unknown)"}");
        sb.AppendLine($"WebView:   {WebViewSource ?? "(unknown)"}");
        sb.AppendLine($"Passed: {PassedCount}  Failed: {FailedCount}");
        sb.AppendLine();

        if (FirstFailedStepId is { } firstFail)
        {
            sb.AppendLine($"Start here: first failure is \"{firstFail}\"");
            sb.AppendLine(GetGuidanceForStep(firstFail));
            sb.AppendLine();
        }
        else if (Steps.Count > 0)
        {
            sb.AppendLine("All steps passed.");
            sb.AppendLine();
        }

        foreach (var step in Steps)
        {
            var status = step.Pass ? "PASS" : "FAIL";
            sb.AppendLine($"[{status}] {step.Id} ({step.DurationMs}ms)");
            if (!string.IsNullOrWhiteSpace(step.Detail))
                sb.AppendLine($"       {step.Detail}");
            if (!string.IsNullOrWhiteSpace(step.Error))
                sb.AppendLine($"       error: {step.Error}");
            if (!string.IsNullOrWhiteSpace(step.RawSnippet))
                sb.AppendLine($"       raw: {step.RawSnippet}");
        }

        sb.AppendLine();
        sb.AppendLine($"JSON report: {ReportJsonPath}");
        return sb.ToString();
    }

    private static string GetGuidanceForStep(string stepId) => stepId switch
    {
        "webview_init" =>
            "Install or repair WebView2 Runtime. Delete a corrupted WebView2 profile only if other steps fail after re-login.",
        "page_injectable" =>
            "Ensure navigation reaches https://chatgpt.com (not blocked or redirected off-domain).",
        "bridge_asset_on_disk" =>
            "Rebuild ChatGPTWrapper so wrapper-assets/chatgpt-api-bridge.js is copied to output.",
        "bridge_inject" =>
            "Refresh chatgpt.com in the WebView. Check whether page CSP or navigation timing blocks script injection.",
        "bridge_ping" =>
            "Bridge script invoke or JSON parsing failed. See raw snippet; compare with BridgeScriptJson unit tests.",
        "bridge_postmessage_fallback" =>
            "chrome.webview message listener may not be attached. Check document-created bootstrap in ChatGptApiBridgeInjection.",
        "api_context" or "session_endpoint" =>
            "Sign in to ChatGPT in the diagnostic window (shared profile) and refresh the page.",
        "device_cookie" =>
            "Refresh chatgpt.com after sign-in so oai-did cookie is set (required for Projects sidebar API).",
        "probe_sidebar" =>
            "Backend-api call failed. Open Projects in ChatGPT sidebar, browse a project, then re-run to capture headers.",
        "list_bootstrap" or "list_dom" or "discovery_merge" =>
            "Project listing fallbacks failed. ChatGPT may have changed API shape or DOM; check discovery trace log.",
        "client_profile" =>
            "api-client-profile.json is empty. Browse a project in ChatGPT so header capture runs.",
        "existing_logs" =>
            "Review link-project.log and project-discovery-trace.jsonl for in-app correlation.",
        _ => "See step error and raw snippet above.",
    };
}

public sealed class ApiDiagnosticStep
{
    public required string Id { get; init; }

    public bool Pass { get; init; }

    public long DurationMs { get; init; }

    public string? Detail { get; init; }

    public string? Error { get; init; }

    public string? RawSnippet { get; init; }
}
