using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace PhoneDrop;

static class Program
{
    static readonly string InstanceName = "PhoneDrop-" + Environment.UserName;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("--")) return Cli.Run(args);

        var files = args.Where(a => File.Exists(a) || Directory.Exists(a)).ToArray();
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
        var form = new MainForm(files);
        if (first) _ = ListenAsync(form);
        Application.Run(form);
        return 0;
    }

    static async Task ListenAsync(MainForm form)
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
                form.BeginInvoke(() => form.Enqueue(files, bringToFront: true));
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
