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
        if (args.Length >= 2 && args[0] == "--ui-selftest") return UiSelfTest(args[1]);
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

    /// <summary>
    /// Kontrola sestavení (i ořezaného): otevře okno a párovací dialog mimo obrazovku, vykreslí je do PNG a skončí.
    /// Nesahá na jedinou instanci ani na síť (kontrola telefonu a aktualizací se nespustí – okno se neukáže).
    /// </summary>
    static int UiSelfTest(string outDir)
    {
        if (!Console.IsOutputRedirected) AttachConsole(-1);
        ApplicationConfiguration.Initialize();
        MainForm.SelfTest = true;
        Directory.CreateDirectory(outDir);
        int ok = 0;
        // Zpětná vazba se otevírá nad hlavním oknem a umí ho vyfotit – i to se tu vyzkouší (GDI+ kodéry v ořezaném exe).
        var owner = new MainForm([]) { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), ShowInTaskbar = false };
        owner.Show();
        foreach (Form f in new Form[] { new MainForm([]), new PairForm(), new FeedbackForm(owner) })
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-10000, -10000);
            f.ShowInTaskbar = false;
            f.Show();
            for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(20); }
            if (f is FeedbackForm fb && !fb.AddWindowShot()) return 2;
            if (f is MainForm qrMain && f != owner) qrMain.ShowQrForTest();
            for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(20); }
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
            var file = Path.Combine(outDir, f.GetType().Name + ".png");
            bmp.Save(file);
            Console.WriteLine($"{f.GetType().Name}: {f.Width}x{f.Height} → {file}");
            if (f is MainForm main) main.Quit(); else f.Dispose();
            ok++;
        }
        owner.Quit();
        return ok == 3 ? 0 : 1;
    }

    [DllImport("kernel32.dll")]
    internal static extern bool AttachConsole(int pid);
}
