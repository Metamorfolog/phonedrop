using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace PhoneDrop;

public sealed record FoundPhone(string Instance, string Address, string? Id);

/// <summary>
/// Jednoduchý mDNS/DNS-SD dotaz (RFC 6762/6763) bez knihoven: PTR dotaz na službu na každém rozhraní,
/// z odpovědí se poskládá instance → SRV (port) + A (IP) + TXT (id telefonu).
/// </summary>
public static class Mdns
{
    public const string PhoneDropService = "_phonedrop._tcp.local";
    static readonly IPEndPoint Group = new(IPAddress.Parse("224.0.0.251"), 5353);

    public static async Task<List<FoundPhone>> DiscoverAsync(string service = PhoneDropService, int timeoutMs = 2500, CancellationToken ct = default)
    {
        var query = BuildQuery(service);
        var sockets = new List<UdpClient>();
        foreach (var local in LocalAddresses())
        {
            try
            {
                // Odesílá z náhodného portu → odpovědi chodí unicastem zpět (RFC 6762 §6.7, QU bit).
                var udp = new UdpClient(new IPEndPoint(local, 0));
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
                sockets.Add(udp);
            }
            catch (SocketException) { }
        }
        // Navíc poslech na 5353 (sdíleně), kdyby odpověď přišla multicastem.
        UdpClient? shared = null;
        try
        {
            shared = new UdpClient();
            shared.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            shared.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
            foreach (var local in LocalAddresses())
                try { shared.JoinMulticastGroup(Group.Address, local); } catch (SocketException) { }
            sockets.Add(shared);
        }
        catch (SocketException) { shared?.Dispose(); }

        var answers = new Answers();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            var receivers = sockets.Select(s => ReceiveLoop(s, answers, cts.Token)).ToList();
            for (int i = 0; i < 3 && !cts.IsCancellationRequested; i++)
            {
                foreach (var s in sockets.Where(s => s != shared))
                    try { await s.SendAsync(query, Group, cts.Token); } catch (Exception) { }
                try { await Task.Delay(700, cts.Token); } catch (OperationCanceledException) { }
            }
            await Task.WhenAll(receivers);
        }
        finally
        {
            foreach (var s in sockets) s.Dispose();
        }
        return answers.Resolve(service);
    }

    static async Task ReceiveLoop(UdpClient udp, Answers answers, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = await udp.ReceiveAsync(ct);
                lock (answers) answers.Parse(r.Buffer, r.RemoteEndPoint.Address);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* poškozený paket nebo zavřený socket */ if (ct.IsCancellationRequested) return; }
        }
    }

    static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .Distinct();

    static byte[] BuildQuery(string name)
    {
        var b = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }; // id, flags, 1 otázka
        WriteName(b, name);
        b.AddRange([0, 12, 0x80, 1]); // PTR, IN + QU (prosba o unicast odpověď)
        return [.. b];
    }

    static void WriteName(List<byte> b, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            b.Add((byte)bytes.Length);
            b.AddRange(bytes);
        }
        b.Add(0);
    }

    sealed class Answers
    {
        readonly Dictionary<string, HashSet<string>> ptr = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, (string Target, int Port)> srv = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Dictionary<string, string>> txt = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, IPAddress> a = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, IPAddress> source = new(StringComparer.OrdinalIgnoreCase);

        public void Parse(byte[] p, IPAddress from)
        {
            if (p.Length < 12 || (p[2] & 0x80) == 0) return; // jen odpovědi
            int qd = p[4] << 8 | p[5];
            int records = (p[6] << 8 | p[7]) + (p[8] << 8 | p[9]) + (p[10] << 8 | p[11]);
            int pos = 12;
            for (int i = 0; i < qd; i++) { ReadName(p, ref pos); pos += 4; }
            for (int i = 0; i < records && pos + 10 <= p.Length; i++)
            {
                var name = ReadName(p, ref pos);
                int type = p[pos] << 8 | p[pos + 1];
                int len = p[pos + 8] << 8 | p[pos + 9];
                pos += 10;
                int data = pos;
                if (data + len > p.Length) return;
                switch (type)
                {
                    case 12: // PTR
                        int q = data;
                        var instance = ReadName(p, ref q);
                        if (!ptr.TryGetValue(name, out var set)) ptr[name] = set = new(StringComparer.OrdinalIgnoreCase);
                        set.Add(instance);
                        source[instance] = from;
                        break;
                    case 33: // SRV
                        int port = p[data + 4] << 8 | p[data + 5];
                        int t = data + 6;
                        srv[name] = (ReadName(p, ref t), port);
                        source[name] = from;
                        break;
                    case 16: // TXT
                        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int k = data; k < data + len;)
                        {
                            int l = p[k++];
                            var entry = Encoding.UTF8.GetString(p, k, Math.Min(l, data + len - k));
                            k += l;
                            int eq = entry.IndexOf('=');
                            if (eq > 0) kv[entry[..eq]] = entry[(eq + 1)..];
                        }
                        txt[name] = kv;
                        break;
                    case 1 when len == 4: // A
                        a[name] = new IPAddress(p.AsSpan(data, 4));
                        break;
                }
                pos = data + len;
            }
        }

        public List<FoundPhone> Resolve(string service)
        {
            var found = new List<FoundPhone>();
            if (!ptr.TryGetValue(service, out var instances)) return found;
            foreach (var inst in instances)
            {
                if (!srv.TryGetValue(inst, out var s)) continue;
                var ip = a.TryGetValue(s.Target, out var addr) ? addr : source.GetValueOrDefault(inst);
                if (ip == null) continue;
                txt.TryGetValue(inst, out var kv);
                var label = inst.EndsWith("." + service, StringComparison.OrdinalIgnoreCase) ? inst[..^(service.Length + 1)] : inst;
                found.Add(new FoundPhone(label, $"{ip}:{s.Port}", kv?.GetValueOrDefault("id")));
            }
            return found;
        }
    }

    static string ReadName(byte[] p, ref int pos)
    {
        var labels = new List<string>();
        int jumps = 0;
        int cur = pos;
        bool jumped = false;
        while (cur < p.Length)
        {
            int len = p[cur];
            if (len == 0) { cur++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                if (cur + 1 >= p.Length || ++jumps > 20) break;
                int target = (len & 0x3F) << 8 | p[cur + 1];
                if (!jumped) pos = cur + 2;
                jumped = true;
                cur = target;
                continue;
            }
            cur++;
            if (cur + len > p.Length) break;
            labels.Add(Encoding.UTF8.GetString(p, cur, len));
            cur += len;
        }
        if (!jumped) pos = cur;
        return string.Join('.', labels);
    }
}
