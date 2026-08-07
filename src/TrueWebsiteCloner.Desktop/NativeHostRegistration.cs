using System.IO;
using Microsoft.Win32;
using TrueWebsiteCloner.Shared;

namespace TrueWebsiteCloner.Desktop;

public static class NativeHostRegistration
{
    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Google\Chrome\NativeMessagingHosts\{AppPaths.NativeHostName}");
        var path = key?.GetValue(null) as string;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }
}
