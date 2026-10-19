namespace PhoneDrop;

/// <summary>
/// Minimální generátor QR kódu (bajtový režim, oprava chyb M, verze 1–15) – na párovací odkaz stačí.
/// Postup podle normy ISO/IEC 18004: data + Reed-Solomon, rozmístění, maska s nejnižší penalizací.
/// </summary>
public static class QrCode
{
    // Oprava chyb M: počet EC bajtů na blok a počet bloků pro verze 1–15 (index 0 nevyužitý).
    static readonly int[] EccPerBlock = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24];
    static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10];

    /// <summary>Matice modulů [y, x], true = tmavý. Bez okraje (ten si přidá ten, kdo kreslí).</summary>
    public static bool[,] Encode(string text)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(text);
        int version = 1;
        for (; version <= 15; version++)
        {
            int countBits = version < 10 ? 8 : 16;
            if (4 + countBits + data.Length * 8 <= DataCodewords(version) * 8) break;
        }
        if (version > 15) throw new ArgumentException("text too long for QR", nameof(text));

        // Bitový proud: režim bajtů (0100), délka, data, terminátor, doplnění.
        var bits = new List<bool>();
        void Put(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
        Put(0b0100, 4);
        Put(data.Length, version < 10 ? 8 : 16);
        foreach (var b in data) Put(b, 8);
        int capacity = DataCodewords(version) * 8;
        Put(0, Math.Min(4, capacity - bits.Count));
        Put(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Put(pad, 8);
        var codewords = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(0x80 >> (i & 7));

        var all = AddEccAndInterleave(codewords, version);
        int size = version * 4 + 17;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];
        DrawFunctionPatterns(modules, isFunction, version);
        DrawCodewords(modules, isFunction, all);

        // Maska s nejnižší penalizací.
        int bestMask = 0, bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, mask);
            DrawFormat(modules, isFunction, mask);
            int p = Penalty(modules);
            if (p < bestPenalty) { bestPenalty = p; bestMask = mask; }
            ApplyMask(modules, isFunction, mask); // XOR zpět
        }
        ApplyMask(modules, isFunction, bestMask);
        DrawFormat(modules, isFunction, bestMask);
        return modules;
    }

    static int RawDataModules(int ver)
    {
        int result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            int numAlign = ver / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    static int DataCodewords(int ver) => RawDataModules(ver) / 8 - EccPerBlock[ver] * Blocks[ver];

    static int[] AlignmentPositions(int ver)
    {
        if (ver == 1) return [];
        int numAlign = ver / 7 + 2;
        int step = (ver * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
        var result = new int[numAlign];
        result[0] = 6;
        for (int i = numAlign - 1, pos = ver * 4 + 17 - 7; i >= 1; i--, pos -= step) result[i] = pos;
        return result;
    }

    // ---------------------------------------------------------------- Reed-Solomon

    static byte[] AddEccAndInterleave(byte[] data, int ver)
    {
        int numBlocks = Blocks[ver], eccLen = EccPerBlock[ver];
        int rawCodewords = RawDataModules(ver) / 8;
        int numShort = numBlocks - rawCodewords % numBlocks;
        int shortLen = rawCodewords / numBlocks;
        var divisor = RsDivisor(eccLen);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            int datLen = shortLen - eccLen + (i < numShort ? 0 : 1);
            var dat = data.AsSpan(k, datLen).ToArray();
            k += datLen;
            var ecc = RsRemainder(dat, divisor);
            // Krátké bloky dostanou výplň na místě, které se při prokládání přeskočí.
            var block = new byte[shortLen + 1];
            dat.CopyTo(block, 0);
            ecc.CopyTo(block, shortLen + 1 - eccLen);
            blocks.Add(block);
        }
        var result = new List<byte>(rawCodewords);
        for (int i = 0; i < shortLen + 1; i++)
            for (int j = 0; j < blocks.Count; j++)
                if (i != shortLen - eccLen || j >= numShort)
                    result.Add(blocks[j][i]);
        return [.. result];
    }

    static byte Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    static byte[] RsDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    static byte[] RsRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }

    // ---------------------------------------------------------------- rozmístění

    static void Set(bool[,] m, bool[,] f, int x, int y, bool dark)
    {
        m[y, x] = dark;
        f[y, x] = true;
    }

    static void DrawFunctionPatterns(bool[,] m, bool[,] f, int ver)
    {
        int size = m.GetLength(0);
        for (int i = 0; i < size; i++)
        {
            Set(m, f, 6, i, i % 2 == 0);
            Set(m, f, i, 6, i % 2 == 0);
        }
        DrawFinder(m, f, 3, 3);
        DrawFinder(m, f, size - 4, 3);
        DrawFinder(m, f, 3, size - 4);
        var align = AlignmentPositions(ver);
        int n = align.Length;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0))
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(m, f, align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        DrawFormat(m, f, 0); // rezervace, skutečné bity až po volbě masky
        if (ver >= 7)
        {
            int rem = ver;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = ver << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(m, f, a, b, bit);
                Set(m, f, b, a, bit);
            }
        }
    }

    static void DrawFinder(bool[,] m, bool[,] f, int cx, int cy)
    {
        int size = m.GetLength(0);
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < size && y >= 0 && y < size) Set(m, f, x, y, dist != 2 && dist != 4);
            }
    }

    static void DrawFormat(bool[,] m, bool[,] f, int mask)
    {
        int size = m.GetLength(0);
        int data = 0b00 << 3 | mask; // oprava chyb M = 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = (data << 10 | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Set(m, f, 8, i, Bit(i));
        Set(m, f, 8, 7, Bit(6));
        Set(m, f, 8, 8, Bit(7));
        Set(m, f, 7, 8, Bit(8));
        for (int i = 9; i < 15; i++) Set(m, f, 14 - i, 8, Bit(i));
        for (int i = 0; i < 8; i++) Set(m, f, size - 1 - i, 8, Bit(i));
        for (int i = 8; i < 15; i++) Set(m, f, 8, size - 15 + i, Bit(i));
        Set(m, f, 8, size - 8, true); // tmavý modul
    }

    static void DrawCodewords(bool[,] m, bool[,] f, byte[] data)
    {
        int size = m.GetLength(0);
        int i = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (!f[y, x] && i < data.Length * 8)
                    {
                        m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
        }
    }

    static void ApplyMask(bool[,] m, bool[,] f, int mask)
    {
        int size = m.GetLength(0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (f[y, x]) continue;
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) m[y, x] = !m[y, x];
            }
    }

    /// <summary>Penalizace podle normy: řady stejné barvy, bloky 2×2, vzory podobné vyhledávacím, poměr tmavých.</summary>
    static int Penalty(bool[,] m)
    {
        int size = m.GetLength(0), result = 0, dark = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++)
            {
                int run = 1;
                for (int b = 1; b < size; b++)
                {
                    bool cur = pass == 0 ? m[a, b] : m[b, a], prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) result += 3; else if (run > 5) result++; }
                    else run = 1;
                }
                for (int b = 0; b + 10 < size; b++)
                {
                    bool At(int k) => pass == 0 ? m[a, b + k] : m[b + k, a];
                    bool core = At(4) && !At(5) && At(6) && At(7) && At(8) && !At(9) && At(10);
                    bool coreRev = At(0) && !At(1) && At(2) && At(3) && At(4) && !At(5) && At(6);
                    if (core && !At(0) && !At(1) && !At(2) && !At(3)) result += 40;
                    if (coreRev && !At(7) && !At(8) && !At(9) && !At(10)) result += 40;
                }
            }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (m[y, x]) dark++;
                if (x + 1 < size && y + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) result += 3;
            }
        int total = size * size;
        int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return result + Math.Max(0, k) * 10;
    }
}
