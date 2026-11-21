using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Finds the League client or game window so capture boxes can follow that
/// client area instead of a one-off absolute pixel rectangle. Caches the
/// resolved HWND briefly so 2-4 Hz polling avoids enumerating every window
/// and process on the desktop on every frame.
/// </summary>
public static class GameWindowLocator
{
    private static readonly string[] PreferredProcesses =
    {
        "League of Legends",
        "LeagueClientUx",
        "LeagueClient"
    };

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMilliseconds(1500);
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, CachedWindow> WindowCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetClientBounds(string? preferredProcess, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        var cacheKey = preferredProcess?.Trim() ?? string.Empty;
        var nowTicks = Environment.TickCount64;

        lock (CacheGate)
        {
            if (WindowCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached.Handle != IntPtr.Zero)
                {
                    if (nowTicks - cached.ObservedAtMs <= CacheTtl.TotalMilliseconds
                        && IsWindow(cached.Handle)
                        && IsWindowVisible(cached.Handle)
                        && TryReadClient(cached.Handle, out bounds)
                        && bounds.Width >= 160
                        && bounds.Height >= 120)
                    {
                        return true;
                    }
                }
                else if (nowTicks - cached.ObservedAtMs <= NegativeCacheTtl.TotalMilliseconds)
                {
                    return false;
                }
            }
        }

        var foundHandle = FindBestWindowHandle(preferredProcess);
        var found = foundHandle != IntPtr.Zero && TryReadClient(foundHandle, out bounds);
        lock (CacheGate)
        {
            WindowCache[cacheKey] = new CachedWindow(found ? foundHandle : IntPtr.Zero, Environment.TickCount64);
        }

        return found;
    }

    public static void InvalidateCache()
    {
        lock (CacheGate)
        {
            WindowCache.Clear();
        }
    }

    public static bool TryBind(ScreenRegion region)
    {
        InvalidateCache();
        var absolute = region.ToRectangle();
        if (absolute.Width < 16 || absolute.Height < 12)
        {
            return region.HasRelative;
        }

        if (TryGetClientBounds(region.AnchorProcess, out var client) && client.IntersectsWith(absolute))
        {
            region.BindToClient(client, string.IsNullOrWhiteSpace(region.AnchorProcess)
                ? "League of Legends"
                : region.AnchorProcess);
            return region.HasRelative;
        }

        foreach (var name in PreferredProcesses)
        {
            if (TryGetClientBounds(name, out client) && client.IntersectsWith(absolute))
            {
                region.BindToClient(client, name);
                return region.HasRelative;
            }
        }

        return false;
    }

    private static IntPtr FindBestWindowHandle(string? preferredProcess)
    {
        var handles = new List<(IntPtr Handle, string ProcessName, int Area)>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, GwOwner) != IntPtr.Zero)
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                var name = process.ProcessName;
                if (!IsGameProcess(name, preferredProcess))
                {
                    return true;
                }

                if (!TryReadClient(window, out var client) || client.Width < 160 || client.Height < 120)
                {
                    return true;
                }

                handles.Add((window, name, client.Width * client.Height));
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }

            return true;
        }, IntPtr.Zero);

        if (handles.Count == 0)
        {
            return IntPtr.Zero;
        }

        return handles
            .OrderByDescending(item => MatchesPreferred(item.ProcessName, preferredProcess) ? 1 : 0)
            .ThenByDescending(item => item.Area)
            .First()
            .Handle;
    }

    private static bool IsGameProcess(string processName, string? preferred)
    {
        if (MatchesPreferred(processName, preferred))
        {
            return true;
        }

        return PreferredProcesses.Any(name =>
            processName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPreferred(string processName, string? preferred) =>
        !string.IsNullOrWhiteSpace(preferred)
        && processName.Equals(preferred, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadClient(IntPtr window, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!GetClientRect(window, out var client) || client.Right <= client.Left || client.Bottom <= client.Top)
        {
            return false;
        }

        var origin = new Point(client.Left, client.Top);
        if (!ClientToScreen(window, ref origin))
        {
            return false;
        }

        bounds = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        return bounds.Width >= 16 && bounds.Height >= 12;
    }

    private const uint GwOwner = 4;

    private readonly record struct CachedWindow(IntPtr Handle, long ObservedAtMs);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
