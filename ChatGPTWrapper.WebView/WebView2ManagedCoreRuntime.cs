using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WebView;

/// <summary>
/// Ensures the managed <c>Microsoft.Web.WebView2.Core</c> assembly is loaded.
/// WinUI copies it to <c>managed/</c> to avoid the UAP shim at the output root.
/// </summary>
public static class WebView2ManagedCoreRuntime
{
    private const string ManagedCoreFileName = "Microsoft.Web.WebView2.Core.dll";
    private static bool _registered;
    private static Assembly? _managedCoreAssembly;

    static WebView2ManagedCoreRuntime() => Register();

    public static bool IsLoaded => _managedCoreAssembly is not null;

    public static void Register()
    {
        if (_registered)
            return;

        _registered = true;
        AssemblyLoadContext.Default.Resolving += OnResolving;
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    public static Assembly EnsureLoaded()
    {
        if (_managedCoreAssembly is not null)
            return _managedCoreAssembly;

        var managedPath = Path.Combine(AppContext.BaseDirectory, "managed", ManagedCoreFileName);
        if (TryLoadManagedAssembly(managedPath, out var managed))
            return managed;

        // WPF / test hosts: Core is on the default probing path (must be managed, not the WinUI UAP shim).
        var rootPath = Path.Combine(AppContext.BaseDirectory, ManagedCoreFileName);
        if (TryLoadManagedAssembly(rootPath, out var root))
            return root;

        if (File.Exists(managedPath) || File.Exists(rootPath))
        {
            throw new BadImageFormatException(
                $"Found Microsoft.Web.WebView2.Core.dll but it is not a managed assembly " +
                $"(WinUI output root often carries the UAP shim). " +
                $"Expected managed copy at '{managedPath}'. " +
                "Rebuild the WinUI project so CopyManagedWebView2CoreForPlay runs.");
        }

        throw new FileNotFoundException(
            $"Managed WebView2 Core assembly not found at '{managedPath}'. " +
            "Rebuild the WinUI project so CopyManagedWebView2CoreForPlay runs.");
    }

    private static bool TryLoadManagedAssembly(string path, out Assembly assembly)
    {
        assembly = null!;
        if (!File.Exists(path) || !HasManagedAssemblyMetadata(path))
            return false;

        assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        _managedCoreAssembly = assembly;
        return true;
    }

    private static bool HasManagedAssemblyMetadata(string path)
    {
        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }

    public static bool TryAsCore(object? coreObj, [NotNullWhen(true)] out object? core)
    {
        if (coreObj is null)
        {
            core = null;
            return false;
        }

        try
        {
            EnsureLoaded();
        }
        catch
        {
            core = coreObj;
            return true;
        }

        var coreType = _managedCoreAssembly?.GetType("Microsoft.Web.WebView2.Core.CoreWebView2");
        if (coreType is not null && coreType.IsInstanceOfType(coreObj))
        {
            core = coreObj;
            return true;
        }

        // WinUI returns the WinRT projection type; still usable via dynamic script calls.
        core = coreObj;
        return true;
    }

    public static object RequireCore(object coreObj) =>
        TryAsCore(coreObj, out var core) ? core! : throw new ArgumentException("Expected CoreWebView2.", nameof(coreObj));

    public static CoreWebView2 RequireTypedCore(object coreObj)
    {
        if (!TryAsCore(coreObj, out var core) || core is null)
            throw new ArgumentException("Expected CoreWebView2.", nameof(coreObj));

        if (core is CoreWebView2 typed)
            return typed;

        var coreType = _managedCoreAssembly?.GetType("Microsoft.Web.WebView2.Core.CoreWebView2");
        if (coreType?.IsInstanceOfType(core) == true)
            return (CoreWebView2)core;

        throw new InvalidCastException(
            $"Object is not assignable to managed CoreWebView2 (runtime type: {core.GetType().FullName}).");
    }

    public static Task ExecuteScriptAsync(object coreObj, string script)
    {
        dynamic core = RequireCore(coreObj);
        try
        {
            return core.ExecuteScriptAsync(script);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.CompletedTask;
        }
    }

    public static string? GetSource(object? coreObj)
    {
        if (coreObj is null)
            return null;

        try
        {
            dynamic core = coreObj;
            return core.Source as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsWinUiManagedLayout =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "managed", ManagedCoreFileName));

    private static Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName) =>
        IsManagedCoreRequest(assemblyName) ? TryGetLoaded() : null;

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args) =>
        IsManagedCoreRequest(new AssemblyName(args.Name)) ? TryGetLoaded() : null;

    private static bool IsManagedCoreRequest(AssemblyName assemblyName) =>
        string.Equals(assemblyName.Name, ManagedCoreFileName, StringComparison.OrdinalIgnoreCase);

    private static Assembly? TryGetLoaded()
    {
        try
        {
            return _managedCoreAssembly ?? EnsureLoaded();
        }
        catch
        {
            return null;
        }
    }
}
