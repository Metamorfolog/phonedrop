using System.Runtime.InteropServices;

namespace PhoneDrop;

/// <summary>Zástupce v %APPDATA%\Microsoft\Windows\SendTo → položka „Odeslat do › Telefon (PhoneDrop)“.</summary>
public static class SendTo
{
    static string Dir => Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
    static string LinkPath => Path.Combine(Dir, L.SendToLinkName + ".lnk");

    /// <summary>Odkazy ze všech jazyků (po změně jazyka zůstane starý název).</summary>
    static IEnumerable<string> AllLinks => new[] { "Phone (PhoneDrop)", "Telefon (PhoneDrop)" }.Select(n => Path.Combine(Dir, n + ".lnk"));

    public static bool Installed => AllLinks.Any(File.Exists);

    public static void Install()
    {
        Uninstall();
        var exe = Environment.ProcessPath!;
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exe);
        link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
        link.SetIconLocation(exe, 0);
        link.SetDescription(L.SendToDescription);
        ((IPersistFile)link).Save(LinkPath, true);
        Marshal.ReleaseComObject(link);
    }

    public static void Uninstall()
    {
        foreach (var link in AllLinks) if (File.Exists(link)) File.Delete(link);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    /// <summary>
    /// Vlastní IPersistFile se všemi metodami: verzi z .NET by ořezání v self-contained exe zmenšilo
    /// a posunulo sloty – Save by pak tiše volal jinou metodu.
    /// </summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, int mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

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
