using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace PhoneDrop;

static class Program
{
    static readonly string InstanceName = "PhoneDrop-" + Environment.UserName;

    /// <summary>Okno se má po zavření postavit znovu (změna jazyka).</summary>
    internal static bool Restart;
    static MainForm? current;

    [STAThread]
    static int Main(string[] args)
    {
        var startup = Config.Load();
        L.Init(startup.Language);
        Theme.Init(startup.Appearance);

        // Spuštěno po aktualizaci: počkat, až stará verze skončí a uvolní jedinou instanci.
        bool updated = false;
        if (args.Length >= 2 && args[0] == "--after-update")
        {
            if (int.TryParse(args[1], out var pid))
                try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(15000); } catch (ArgumentException) { }
            args = [];
            updated = true;
        }
        if (args.Length > 0 && args[0].StartsWith("--")) return Cli.Run(args);
        Updater.CleanUp();

        var files = args.Where(a => File.Exists(a) || Directory.Exists(a)).ToArray(); // složky rozbalí okno
        using var mutex = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            // Už běží (např. spuštěno z „Odeslat do“) – předat soubory hlavnímu oknu.
            try
            {
                using var pipe = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out);
                pipe.Connect(3000);
                using var w = new StreamWriter(pipe);
                w.WriteLine(files.Length == 0 ? "\u0001show" : string.Join('\n', files));
                return 0;
            }
            catch (Exception) { /* staré okno nereaguje – spustíme vlastní */ }
        }

        ApplicationConfiguration.Initialize();
        bool listening = false;
        do
        {
            Restart = false;
            current = new MainForm(files, updated);
            updated = false;
            files = [];
            if (first && !listening) { listening = true; _ = ListenAsync(); }
            Application.Run(current);
        } while (Restart);
        return 0;
    }

    static async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(InstanceName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var r = new StreamReader(pipe);
                var text = await r.ReadToEndAsync();
                var files = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(f => f != "\u0001show").ToArray();
                var form = current;
                if (form != null && form.IsHandleCreated) form.BeginInvoke(() => form.Enqueue(files, bringToFront: true));
            }
            catch (Exception)
            {
                await Task.Delay(500);
            }
        }
    }

    [DllImport("kernel32.dll")]
    internal static extern bool AttachConsole(int pid);
}
