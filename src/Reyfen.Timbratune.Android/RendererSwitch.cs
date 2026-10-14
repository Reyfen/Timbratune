using System.Reflection;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;

namespace Reyfen.Timbratune.Android;

/// <summary>
/// Changes Avalonia.Android's graphics backend (OpenGL ES or Vulkan) in a running process.
/// Avalonia picks it once, in <c>AndroidPlatform.Initialize</c>: an internal static
/// <c>Compositor</c> built on the chosen <c>IPlatformGraphics</c>, which every new view reads
/// back through <c>TopLevelImpl.Compositor</c>. So the process-wide switch is: build the new
/// graphics, build a new compositor on it, store both where Avalonia reads them. The Activity
/// is then re-created: <c>OnDestroy</c> detaches the main view and disposes the old view, and the
/// new Activity's view picks up the new compositor and re-attaches the same main view.
/// The old compositor and graphics are left to the GC (Avalonia has no teardown for them).
///
/// This leans on Avalonia internals, so it only runs on the Avalonia version it was checked
/// against (11.3.x); anywhere else it reports "not supported" and the renderer stays as it is.
/// </summary>
internal static class RendererSwitch
{
    private const string Tag = "Timbratune";

    /// <summary>True if the graphics Avalonia is rendering with right now is Vulkan.</summary>
    public static bool IsVulkan =>
        Resolve(typeof(IPlatformGraphics))?.GetType().Name.Contains("Vulkan", StringComparison.OrdinalIgnoreCase) == true;

    public static string Describe() => Resolve(typeof(IPlatformGraphics))?.GetType().Name ?? "software";

    public static bool Supported =>
        typeof(Compositor).Assembly.GetName().Version is { Major: 11, Minor: 3 };

    // AvaloniaLocator's Current / CurrentMutable / Bind are internal.
    private static object Locator(string property) =>
        typeof(IAvaloniaDependencyResolver).Assembly.GetType("Avalonia.AvaloniaLocator", throwOnError: true)!
            .GetProperty(property, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(null)!;

    private static object? Resolve(Type service) =>
        typeof(IAvaloniaDependencyResolver)
            .GetMethod("GetService", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(Type)])!
            .Invoke(Locator("Current"), [service]);

    private static void Bind(Type service, object value)
    {
        var locator = Locator("CurrentMutable");
        var helper = locator.GetType().GetMethod("Bind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes)!
            .MakeGenericMethod(service).Invoke(locator, null)!;
        helper.GetType().GetMethod("ToConstant")!.MakeGenericMethod(service).Invoke(helper, [value]);
    }

    /// <summary>
    /// Rebuilds the graphics layer on <paramref name="vulkan"/> (otherwise OpenGL ES). On
    /// failure nothing has been changed. Call on the UI thread, with no Avalonia view alive.
    /// </summary>
    public static bool TryRebuild(bool vulkan, out string error)
    {
        error = "";
        try
        {
            if (!Supported) { error = "Avalonia version not supported"; return false; }

            var android = typeof(Avalonia.Android.AvaloniaMainActivity).Assembly;
            var platform = android.GetType("Avalonia.Android.AndroidPlatform", throwOnError: true)!;
            var compositorProp = platform.GetProperty("Compositor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                 ?? throw new MissingMemberException("AndroidPlatform.Compositor");
            var compositorSetter = compositorProp.GetSetMethod(nonPublic: true)
                                   ?? throw new MissingMemberException("AndroidPlatform.Compositor setter");

            var graphics = vulkan ? CreateVulkan(android) : CreateEgl();
            if (graphics is null) { error = vulkan ? "Vulkan isn't available" : "OpenGL ES isn't available"; return false; }

            var ctor = typeof(Compositor).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                           null, [typeof(IPlatformGraphics), typeof(bool)], null)
                       ?? throw new MissingMemberException("Compositor(IPlatformGraphics, bool)");
            var compositor = (Compositor)ctor.Invoke([graphics, false]);

            compositorSetter.Invoke(null, [compositor]);
            Bind(typeof(IPlatformGraphics), graphics);
            Bind(typeof(Compositor), compositor);
            global::Android.Util.Log.Info(Tag, $"renderer rebuilt: {graphics.GetType().Name}");
            return true;
        }
        catch (Exception e)
        {
            error = (e is TargetInvocationException { InnerException: { } inner } ? inner : e).Message;
            global::Android.Util.Log.Warn(Tag, $"renderer switch failed: {e}");
            return false;
        }
    }

    private static IPlatformGraphics? CreateEgl()
    {
        var type = Type.GetType("Avalonia.OpenGL.Egl.EglPlatformGraphics, Avalonia.OpenGL", throwOnError: true)!;
        return (IPlatformGraphics?)Invoke(type, "TryCreate", null);
    }

    private static IPlatformGraphics? CreateVulkan(Assembly android)
    {
        var type = android.GetType("Avalonia.Android.Platform.Vulkan.VulkanSupport", throwOnError: true)!;
        var method = type.GetMethod("TryInitialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                     ?? throw new MissingMemberException("VulkanSupport.TryInitialize");
        var optionsType = method.GetParameters()[0].ParameterType;
        var options = Resolve(optionsType) ?? Activator.CreateInstance(optionsType);
        return (IPlatformGraphics?)method.Invoke(null, [options]);
    }

    /// <summary>Calls a static method by name with its parameters (all optional) left at null.</summary>
    private static object? Invoke(Type type, string name, object? _)
    {
        var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == name)
                         .OrderBy(m => m.GetParameters().Length)
                         .FirstOrDefault()
                     ?? throw new MissingMemberException($"{type.Name}.{name}");
        return method.Invoke(null, new object?[method.GetParameters().Length]);
    }
}
