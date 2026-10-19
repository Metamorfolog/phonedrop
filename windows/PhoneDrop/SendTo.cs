using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PhoneDrop;

/// <summary>Zástupce v %APPDATA%\Microsoft\Windows\SendTo → položka „Odeslat do › Telefon (PhoneDrop)“.</summary>
public static class SendTo
{
    static string LinkPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Telefon (PhoneDrop).lnk");

    public static bool Installed => File.Exists(LinkPath);

    public static void Install()
    {
        var exe = Environment.ProcessPath!;
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exe);
        link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
        link.SetIconLocation(exe, 0);
        link.SetDescription("Odeslat do telefonu přes PhoneDrop");
        ((IPersistFile)link).Save(LinkPath, true);
        Marshal.ReleaseComObject(link);
    }

    public static void Uninstall()
    {
        if (File.Exists(LinkPath)) File.Delete(LinkPath);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int cch, IntPtr fd, uint flags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string rel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
