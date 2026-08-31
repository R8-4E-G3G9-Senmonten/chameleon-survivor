using System;
using System.Collections.Generic;
using System.Text;

// Minimal, dependency-free QR code encoder (ISO 18004), restricted to:
// Byte mode, error-correction level L, versions 1-5 (single RS block,
// so no interleaving is needed), fixed mask pattern 0, no version-info
// block (that's only required for version >= 7). This covers any LAN
// URL like "http://192.168.1.23:8787/" with room to spare.
//
// Algorithm mirrors the well-known Kazuhiko Arase QR encoder. Verified
// against OpenCV's QR decoder across several input lengths (forcing
// versions 1, 2 and 5) before being ported here.
public static class QRCodeGenerator
{
    static readonly int[] ExpTable = new int[256];
    static readonly int[] LogTable = new int[256];

    static QRCodeGenerator()
    {
        for (int i = 0; i < 8; i++) ExpTable[i] = 1 << i;
        for (int i = 8; i < 256; i++) ExpTable[i] = ExpTable[i - 4] ^ ExpTable[i - 5] ^ ExpTable[i - 6] ^ ExpTable[i - 8];
        for (int i = 0; i < 255; i++) LogTable[ExpTable[i]] = i;
    }

    static int GLog(int n) => LogTable[n];

    static int GExp(int n)
    {
        while (n < 0) n += 255;
        while (n >= 256) n -= 255;
        return ExpTable[n];
    }

    class Poly
    {
        public int[] Num;
        public Poly(int[] num, int shift = 0)
        {
            int offset = 0;
            while (offset < num.Length && num[offset] == 0) offset++;
            Num = new int[num.Length - offset + shift];
            Array.Copy(num, offset, Num, 0, num.Length - offset);
        }
        public int Get(int i) => Num[i];
        public int Length => Num.Length;

        public Poly Multiply(Poly e)
        {
            int[] num = new int[Length + e.Length - 1];
            for (int i = 0; i < Length; i++)
                for (int j = 0; j < e.Length; j++)
                    num[i + j] ^= GExp(GLog(Get(i)) + GLog(e.Get(j)));
            return new Poly(num);
        }

        public Poly Mod(Poly e)
        {
            Poly cur = this;
            while (cur.Length - e.Length >= 0)
            {
                int ratio = GLog(cur.Get(0)) - GLog(e.Get(0));
                int[] num = (int[])cur.Num.Clone();
                for (int i = 0; i < e.Length; i++)
                    num[i] ^= GExp(GLog(e.Get(i)) + ratio);
                cur = new Poly(num);
            }
            return cur;
        }
    }

    static byte[] RsBlock(byte[] dataBytes, int ecCount)
    {
        Poly rsPoly = new Poly(new[] { 1 });
        for (int i = 0; i < ecCount; i++)
            rsPoly = rsPoly.Multiply(new Poly(new[] { 1, GExp(i) }));

        int[] raw = new int[dataBytes.Length];
        for (int i = 0; i < dataBytes.Length; i++) raw[i] = dataBytes[i];
        Poly rawPoly = new Poly(raw, ecCount);
        Poly modPoly = rawPoly.Mod(rsPoly);

        byte[] ec = new byte[ecCount];
        for (int i = 0; i < ecCount; i++)
        {
            int idx = i + modPoly.Length - ecCount;
            ec[i] = (byte)(idx >= 0 ? modPoly.Get(idx) : 0);
        }
        return ec;
    }

    // version -> (total data codewords at level L, ec codewords per block at level L)
    static readonly Dictionary<int, (int dataCw, int ecCw)> VersionTable = new Dictionary<int, (int, int)>
    {
        { 1, (19, 7) },
        { 2, (34, 10) },
        { 3, (55, 15) },
        { 4, (80, 20) },
        { 5, (108, 26) },
    };

    static readonly Dictionary<int, int[]> AlignPos = new Dictionary<int, int[]>
    {
        { 1, new int[0] },
        { 2, new[] { 6, 18 } },
        { 3, new[] { 6, 22 } },
        { 4, new[] { 6, 26 } },
        { 5, new[] { 6, 30 } },
    };

    static int SizeForVersion(int v) => v * 4 + 17;

    static int ChooseVersion(int byteLen)
    {
        for (int v = 1; v <= 5; v++)
        {
            int capBits = VersionTable[v].dataCw * 8;
            int headerBits = 4 + 8;
            if (headerBits + byteLen * 8 + 4 <= capBits) return v;
        }
        throw new ArgumentException("QR data too long for supported versions (max ~108 bytes)");
    }

    static byte[] BuildDataCodewords(byte[] dataBytes, int version)
    {
        var (dataCw, ecCw) = VersionTable[version];
        int totalDataBits = dataCw * 8;

        var bits = new List<int>();
        void Put(int val, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add((val >> i) & 1);
        }

        Put(0b0100, 4);
        Put(dataBytes.Length, 8);
        foreach (byte b in dataBytes) Put(b, 8);

        int termBits = Math.Min(4, totalDataBits - bits.Count);
        for (int i = 0; i < termBits; i++) bits.Add(0);

        while (bits.Count % 8 != 0) bits.Add(0);

        int[] padBytes = { 0xEC, 0x11 };
        int pi = 0;
        while (bits.Count < totalDataBits)
        {
            Put(padBytes[pi % 2], 8);
            pi++;
        }

        byte[] dataFull = new byte[bits.Count / 8];
        for (int i = 0; i < dataFull.Length; i++)
        {
            int b = 0;
            for (int j = 0; j < 8; j++) b = (b << 1) | bits[i * 8 + j];
            dataFull[i] = (byte)b;
        }

        byte[] ecBytes = RsBlock(dataFull, ecCw);
        byte[] all = new byte[dataFull.Length + ecBytes.Length];
        Array.Copy(dataFull, all, dataFull.Length);
        Array.Copy(ecBytes, 0, all, dataFull.Length, ecBytes.Length);
        return all;
    }

    const int G15 = 0x537;
    const int G15_MASK = 0x5412;

    static int BchDigit(int data)
    {
        int d = 0;
        while (data != 0) { d++; data >>= 1; }
        return d;
    }

    static int BchTypeInfo(int data)
    {
        int d = data << 10;
        while (BchDigit(d) - BchDigit(G15) >= 0)
            d ^= (G15 << (BchDigit(d) - BchDigit(G15)));
        return ((data << 10) | d) ^ G15_MASK;
    }

    class QR
    {
        public int Version;
        public int Size;
        public bool?[,] Modules;

        public QR(int version)
        {
            Version = version;
            Size = SizeForVersion(version);
            Modules = new bool?[Size, Size];
        }

        void Set(int r, int c, bool val)
        {
            if (r >= 0 && r < Size && c >= 0 && c < Size) Modules[r, c] = val;
        }

        void PlaceFinder(int r0, int c0)
        {
            for (int r = -1; r <= 7; r++)
            {
                for (int c = -1; c <= 7; c++)
                {
                    int rr = r0 + r, cc = c0 + c;
                    if (rr < 0 || cc < 0 || rr >= Size || cc >= Size) continue;
                    bool dark = (r >= 0 && r <= 6 && (c == 0 || c == 6)) ||
                                (c >= 0 && c <= 6 && (r == 0 || r == 6)) ||
                                (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                    Set(rr, cc, dark);
                }
            }
        }

        void PlaceAlignment(int r0, int c0)
        {
            for (int r = -2; r <= 2; r++)
            {
                for (int c = -2; c <= 2; c++)
                {
                    bool dark = Math.Abs(r) == 2 || Math.Abs(c) == 2 || (r == 0 && c == 0);
                    Set(r0 + r, c0 + c, dark);
                }
            }
        }

        void SetupPositions()
        {
            PlaceFinder(0, 0);
            PlaceFinder(Size - 7, 0);
            PlaceFinder(0, Size - 7);

            int[] pos = AlignPos[Version];
            foreach (int r in pos)
            {
                foreach (int c in pos)
                {
                    if (Modules[r, c] != null) continue;
                    PlaceAlignment(r, c);
                }
            }

            for (int i = 8; i < Size - 8; i++)
            {
                bool val = (i % 2 == 0);
                if (Modules[i, 6] == null) Set(i, 6, val);
                if (Modules[6, i] == null) Set(6, i, val);
            }

            Set(Size - 8, 8, true);
        }

        void SetupTypeInfo(int maskPattern, int ecLevelBits = 1)
        {
            int data = (ecLevelBits << 3) | maskPattern;
            int bch = BchTypeInfo(data);

            for (int i = 0; i < 15; i++)
            {
                bool mod = ((bch >> i) & 1) == 1;
                if (i < 6) Set(i, 8, mod);
                else if (i < 8) Set(i + 1, 8, mod);
                else Set(Size - 15 + i, 8, mod);
            }

            for (int i = 0; i < 15; i++)
            {
                bool mod = ((bch >> i) & 1) == 1;
                if (i < 8) Set(8, Size - i - 1, mod);
                else if (i < 9) Set(8, 15 - i - 1 + 1, mod);
                else Set(8, 15 - i - 1, mod);
            }
        }

        void MapData(byte[] data)
        {
            int bitIndex = 8;
            int byteIndex = 0;
            int n = data.Length;

            int inc = -1;
            int row = Size - 1;
            int col = Size - 1;

            while (col > 0)
            {
                if (col == 6) col--;
                while (true)
                {
                    for (int c = 0; c < 2; c++)
                    {
                        int cc = col - c;
                        if (Modules[row, cc] == null)
                        {
                            bool masked;
                            bool maskBit = ((row + cc) % 2) == 0;
                            if (byteIndex < n)
                            {
                                bool dark = ((data[byteIndex] >> (bitIndex - 1)) & 1) == 1;
                                masked = dark ^ maskBit;
                            }
                            else
                            {
                                masked = maskBit;
                            }
                            Modules[row, cc] = masked;
                            bitIndex--;
                            if (bitIndex == 0)
                            {
                                byteIndex++;
                                bitIndex = 8;
                            }
                        }
                    }
                    row += inc;
                    if (row < 0 || row >= Size)
                    {
                        row -= inc;
                        inc = -inc;
                        break;
                    }
                }
                col -= 2;
            }
        }

        public void Build(byte[] data)
        {
            SetupPositions();
            SetupTypeInfo(0);
            MapData(data);
        }
    }

    // Returns a Size x Size boolean matrix (true = dark module).
    public static bool[,] Encode(string text, out int size)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(text);
        int version = ChooseVersion(dataBytes.Length);
        byte[] codewords = BuildDataCodewords(dataBytes, version);
        var qr = new QR(version);
        qr.Build(codewords);

        size = qr.Size;
        bool[,] result = new bool[size, size];
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                result[r, c] = qr.Modules[r, c] == true;
        return result;
    }
}
