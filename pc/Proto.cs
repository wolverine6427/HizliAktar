// Hızlı Aktar - PC tarafı ağ protokolü.
// Protokol (tablet ile aynı):
//   Keşif : tablet UDP 47801'e her 1,5 sn "HA1|<id>|<tcpPort>|<ad>" yayınlar.
//   Aktarım: dosya başına bir TCP bağlantısı -> "HA1" + pin(int32 BE) + yol(uint16 BE uzunluk + UTF-8) + boyut(int64 BE)
//            tablet: 'K' (kabul) / 'P' (PIN yanlış); veri; tablet kaydedince 'D'.
//            boyut = -1 ise "ping": tablet durum baytı + id + ad döner.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HizliAktar
{
    public class Device
    {
        public string Id, Name, Ip;
        public int Port;
        public DateTime LastSeen;
        public bool Manual;
        public override string ToString() { return Name + "   (" + Ip + ")"; }
    }

    public class PinException : Exception { public PinException() : base("PIN yanlış") { } }

    public static class Proto
    {
        public const int TcpPort = 47800, UdpPort = 47801;
        const int Buf = 1 << 20;

        static void WriteBE(Stream s, long v, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i)));
        }

        static void WriteStr(Stream s, string v)
        {
            byte[] b = Encoding.UTF8.GetBytes(v);
            if (b.Length > 65535) throw new IOException("Dosya yolu çok uzun");
            WriteBE(s, b.Length, 2);
            s.Write(b, 0, b.Length);
        }

        static int ReadByte(Stream s)
        {
            int b = s.ReadByte();
            if (b < 0) throw new IOException("Tablet bağlantıyı kapattı");
            return b;
        }

        static string ReadStr(Stream s)
        {
            int n = (ReadByte(s) << 8) | ReadByte(s);
            byte[] b = new byte[n];
            int o = 0;
            while (o < n)
            {
                int r = s.Read(b, o, n - o);
                if (r <= 0) throw new IOException("Tablet bağlantıyı kapattı");
                o += r;
            }
            return Encoding.UTF8.GetString(b);
        }

        static byte[] Header(int pin, string path, long size)
        {
            var m = new MemoryStream();
            m.Write(Encoding.ASCII.GetBytes("HA1"), 0, 3);
            WriteBE(m, pin, 4);
            WriteStr(m, path);
            WriteBE(m, size, 8);
            return m.ToArray();
        }

        static TcpClient Connect(string ip, int port)
        {
            var c = new TcpClient();
            c.NoDelay = true;
            c.SendBufferSize = 4 << 20;
            c.ReceiveTimeout = 60000;
            c.SendTimeout = 60000;
            IAsyncResult ar = c.BeginConnect(ip, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(3000))
            {
                c.Close();
                throw new IOException(ip + " adresine bağlanılamadı (tablette uygulama açık mı?)");
            }
            c.EndConnect(ar);
            return c;
        }

        // PIN doğruysa true. PIN yanlış olsa da tabletin id ve adını döndürür.
        public static bool Ping(string ip, int port, int pin, out string id, out string name)
        {
            using (var c = Connect(ip, port))
            {
                var s = c.GetStream();
                byte[] h = Header(pin, "", -1);
                s.Write(h, 0, h.Length);
                int st = ReadByte(s);
                id = ReadStr(s);
                name = ReadStr(s);
                return st == 'K';
            }
        }

        public static void SendFile(string ip, int port, int pin, string fullPath, string relPath, Action<long> progress)
        {
            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, Buf, FileOptions.SequentialScan))
            using (var c = Connect(ip, port))
            {
                var s = c.GetStream();
                long size = fs.Length;
                byte[] h = Header(pin, relPath, size);
                s.Write(h, 0, h.Length);
                int st = ReadByte(s);
                if (st == 'P') throw new PinException();
                if (st == 'S') throw new IOException("Tablette yeterli boş alan yok");
                if (st != 'K') throw new IOException("Tablet dosyayı reddetti");

                byte[] buf = new byte[Buf];
                long left = size;
                while (left > 0)
                {
                    int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                    if (n <= 0) throw new IOException("Dosya okunamadı (gönderilirken değişti mi?)");
                    s.Write(buf, 0, n);
                    left -= n;
                    progress(n);
                }
                c.ReceiveTimeout = 180000; // tablet son tamponu diske yazarken bekle
                if (ReadByte(s) != 'D') throw new IOException("Tablet kaydı onaylamadı");
            }
        }
    }

    // Tabletlerin UDP yayınlarını dinler.
    public class Discovery
    {
        readonly Dictionary<string, Device> devices = new Dictionary<string, Device>();
        UdpClient udp;

        public void Start()
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Proto.UdpPort));
            var t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        void Loop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] d = udp.Receive(ref ep);
                    Handle(Encoding.UTF8.GetString(d), ep.Address.ToString());
                }
                catch (ObjectDisposedException) { return; }
                catch (Exception) { }
            }
        }

        public void Handle(string msg, string ip)
        {
            string[] p = msg.Split(new[] { '|' }, 4);
            int port;
            if (p.Length < 4 || p[0] != "HA1" || !int.TryParse(p[2], out port)) return;
            Upsert(p[1], p[3], ip, port, false);
        }

        public void Upsert(string id, string name, string ip, int port, bool manual)
        {
            lock (devices)
            {
                Device dev;
                if (!devices.TryGetValue(id, out dev)) { dev = new Device { Id = id }; devices[id] = dev; }
                dev.Name = name; dev.Ip = ip; dev.Port = port; dev.LastSeen = DateTime.Now;
                dev.Manual |= manual;
            }
        }

        public List<Device> Snapshot()
        {
            lock (devices)
            {
                var gone = new List<string>();
                foreach (var d in devices.Values)
                    if (!d.Manual && (DateTime.Now - d.LastSeen).TotalSeconds > 6) gone.Add(d.Id);
                foreach (var id in gone) devices.Remove(id);
                var list = new List<Device>(devices.Values);
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                return list;
            }
        }
    }

    // Eşleşmiş tabletlerin PIN'leri: %APPDATA%\HizliAktar\pins.txt
    public static class Pins
    {
        static readonly string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HizliAktar", "pins.txt");

        static Dictionary<string, int> Load()
        {
            var d = new Dictionary<string, int>();
            if (!File.Exists(file)) return d;
            foreach (var line in File.ReadAllLines(file))
            {
                string[] p = line.Split('=');
                int pin;
                if (p.Length == 2 && int.TryParse(p[1], out pin)) d[p[0]] = pin;
            }
            return d;
        }

        static void Save(Dictionary<string, int> d)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var lines = new List<string>();
            foreach (var kv in d) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(file, lines.ToArray());
        }

        public static int? Get(string id) { int p; return Load().TryGetValue(id, out p) ? p : (int?)null; }
        public static void Set(string id, int pin) { var d = Load(); d[id] = pin; Save(d); }
        public static void Remove(string id) { var d = Load(); d.Remove(id); Save(d); }

        // Daha önce girilmiş tüm farklı kodlar: yeni cihazda önce bunlar sessizce denenir.
        public static List<int> Known() { return new List<int>(new HashSet<int>(Load().Values)); }
    }

    // Bir cihaza bir grup dosyayı paralel bağlantılarla gönderir.
    public class Batch
    {
        public long TotalBytes, SentBytes;
        public int TotalFiles, DoneFiles;
        public volatile bool PinWrong;
        public readonly List<string> Errors = new List<string>();
        readonly List<KeyValuePair<string, string>> files; // tam yol -> tablet içindeki göreli yol

        public Batch(List<KeyValuePair<string, string>> files)
        {
            this.files = files;
            TotalFiles = files.Count;
            foreach (var f in files) TotalBytes += new FileInfo(f.Key).Length;
        }

        // Sürüklenen dosya ve klasörleri (klasör yapısını koruyarak) listeler.
        public static List<KeyValuePair<string, string>> Collect(string[] paths, List<string> errors)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var raw in paths)
            {
                try
                {
                    string p = raw.TrimEnd('\\');
                    if (Directory.Exists(p))
                    {
                        var parent = Directory.GetParent(p);
                        string baseDir = parent == null ? p : parent.FullName;
                        foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                            list.Add(new KeyValuePair<string, string>(f, f.Substring(baseDir.Length).TrimStart('\\').Replace('\\', '/')));
                    }
                    else if (File.Exists(p))
                        list.Add(new KeyValuePair<string, string>(p, Path.GetFileName(p)));
                }
                catch (Exception e) { errors.Add(raw + ": " + e.Message); }
            }
            return list;
        }

        public void Run(Device d, int pin, int parallel)
        {
            int next = -1;
            var threads = new List<Thread>();
            for (int i = 0; i < Math.Min(parallel, Math.Max(1, files.Count)); i++)
            {
                var t = new Thread(() =>
                {
                    int idx;
                    while (!PinWrong && (idx = Interlocked.Increment(ref next)) < files.Count)
                    {
                        var f = files[idx];
                        long sentHere = 0;
                        try
                        {
                            Proto.SendFile(d.Ip, d.Port, pin, f.Key, f.Value, n => { sentHere += n; Interlocked.Add(ref SentBytes, n); });
                            Interlocked.Increment(ref DoneFiles);
                        }
                        catch (PinException) { PinWrong = true; }
                        catch (Exception e)
                        {
                            Interlocked.Add(ref SentBytes, -sentHere);
                            lock (Errors) Errors.Add(f.Value + ": " + e.Message);
                        }
                    }
                });
                t.IsBackground = true;
                t.Start();
                threads.Add(t);
            }
            foreach (var t in threads) t.Join();
        }
    }
}
