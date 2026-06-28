namespace ChatGPTWrapper.Diagnostics;

/// <summary>
/// Observes fire-and-forget UI tasks so faults are not lost when callers use discard (<c>_ =</c>).
/// </summary>
internal static class UiAsyncTasks
{
    public static void Run(
        Func<Task> work,
        string operation,
        Guid? adventureId = null,
        object? context = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        _ = ObserveAsync(work(), operation, adventureId, context);
    }

    public static void Run(
        Task task,
        string operation,
        Guid? adventureId = null,
        object? context = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        _ = ObserveAsync(task, operation, adventureId, context);
    }

    private static async Task ObserveAsync(
        Task task,
        string operation,
        Guid? adventureId,
        object? context)
    {
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportFault(operation, adventureId, context, ex);
        }
    }

    internal static void ReportFault(string operation, Guid? adventureId, object? context, Exception ex)
    {
        DiagnosticsMirror.LogException(operation, ex, adventureId: adventureId);
        UiEventLogger.Error(
            "async_task_failed",
            ex.Message,
            new
            {
                operation,
                context,
                exceptionType = ex.GetType().Name,
            },
            adventureId: adventureId);
    }
}
