// Hızlı Aktar - PC tarafı: protokol, keşif, alıcı, cihaz hafızası, gönderici.
// Her cihaz hem gönderici hem alıcıdır (PC <-> tablet/telefon, tablet <-> tablet...).
//
// Keşif : her cihaz UDP 47801'e her 1,5 sn "HA2|<id>|<tcpPort>|<tür>|<ad>" yayınlar (tür: pc / mobil).
// Aktarım: dosya başına bir TCP bağlantısı (alıcının 47800 portu), sayılar big-endian:
//   "HA2" + kod(int32) + gönderenId + gönderenAd + gönderenTür + yol + boyut(int64)   (metinler: uint16 uzunluk + UTF-8)
//   ("HA1" eski biçim: gönderen alanları yoktur.)
//   Alıcı: 'K' kabul / 'P' kod yanlış / 'S' yer yok; veri bitince 'D'.
//   boyut = -1 ise ping: durum baytı + alıcı id + ad + tür.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HizliAktar
{
    public class Device
    {
        public string Id, Name = "", Nick = "", Ip = "", Type = "mobil";
        public int Port = Proto.TcpPort;
        public int Pin;          // 0 = kod bilinmiyor
        public bool Known;       // hatırlanan (eşleşmiş) cihaz
        public DateTime LastSeen = DateTime.MinValue;

        public bool Online { get { return (DateTime.Now - LastSeen).TotalSeconds < 6; } }
        public string Label { get { return string.IsNullOrEmpty(Nick) ? Name : Nick + " (" + Name + ")"; } }
        public override string ToString()
        {
            return (Type == "pc" ? "💻 " : "📱 ") + Label + (Online ? "   " + Ip : "   — çevrimdışı");
        }
    }

    public class PinException : Exception { public PinException() : base("Kod yanlış") { } }

    // Bu bilgisayarın kimliği, adı ve kodu: %APPDATA%\HizliAktar\ben.txt
    public static class Me
    {
        public static string Id, Name;
        public static int Pin;
        static readonly string file = Path.Combine(Store.Dir, "ben.txt");

        public static void Load()
        {
            string[] l = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
            Id = l.Length > 0 && l[0].Length > 0 ? l[0] : Guid.NewGuid().ToString("N").Substring(0, 12);
            if (l.Length < 2 || !int.TryParse(l[1], out Pin) || Pin <= 0) Pin = new Random().Next(1000, 10000);
            Name = l.Length > 2 && l[2].Trim().Length > 0 ? l[2] : Environment.MachineName;
            Save();
        }

        public static void Save()
        {
            Directory.CreateDirectory(Store.Dir);
            File.WriteAllLines(file, new[] { Id, Pin.ToString(), Name });
        }
    }

    // Tüm cihazlar: canlı görülenler + hatırlananlar (%APPDATA%\HizliAktar\cihazlar.txt)
    public static class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HizliAktar");
        static readonly string file = Path.Combine(Dir, "cihazlar.txt");
        static readonly Dictionary<string, Device> all = new Dictionary<string, Device>();

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        public static void Load()
        {
            lock (all)
            {
                if (File.Exists(file))
                    foreach (var line in File.ReadAllLines(file))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length < 7) continue;
                        var d = new Device { Id = p[0], Nick = p[2], Ip = p[3], Type = p[5], Name = p[6], Known = true };
                        int.TryParse(p[1], out d.Pin);
                        int.TryParse(p[4], out d.Port);
                        all[d.Id] = d;
                    }
                // 1.0/1.1 sürümlerinden kalan kodları al
                string old = Path.Combine(Dir, "pins.txt");
                if (File.Exists(old))
                {
                    foreach (var line in File.ReadAllLines(old))
                    {
                        string[] p = line.Split('=');
                        int pin;
                        if (p.Length == 2 && int.TryParse(p[1], out pin) && !all.ContainsKey(p[0]))
                            all[p[0]] = new Device { Id = p[0], Name = "Eski cihaz", Pin = pin, Known = true };
                    }
                    SaveLocked();
                    File.Move(old, old + ".eski");
                }
            }
        }

        static void SaveLocked()
        {
            Directory.CreateDirectory(Dir);
            var lines = new List<string>();
            foreach (var d in all.Values)
                if (d.Known)
                    lines.Add(string.Join("\t", new[] { d.Id, d.Pin.ToString(), Clean(d.Nick), d.Ip, d.Port.ToString(), d.Type, Clean(d.Name) }));
            File.WriteAllLines(file, lines.ToArray());
        }

        public static void Save() { lock (all) SaveLocked(); }

        // Keşif yayını veya gelen bağlantıyla cihaz görüldü.
        public static Device Seen(string id, string name, string type, string ip, int port, bool remember)
        {
            if (id == Me.Id) return null;
            lock (all)
            {
                Device d;
                if (!all.TryGetValue(id, out d)) { d = new Device { Id = id }; all[id] = d; }
                bool changed = d.Name != name || d.Ip != ip || d.Port != port || d.Type != type || (remember && !d.Known);
                d.Name = name; d.Ip = ip; d.Port = port; d.Type = type; d.LastSeen = DateTime.Now;
                d.Known |= remember;
                if (changed && d.Known) SaveLocked();
                return d;
            }
        }

        public static void Update(Device d, Action<Device> change) { lock (all) { change(d); SaveLocked(); } }

        public static void Forget(Device d) { lock (all) { all.Remove(d.Id); SaveLocked(); } }

        public static List<int> KnownPins()
        {
            var set = new HashSet<int> { Me.Pin };
            lock (all) foreach (var d in all.Values) if (d.Pin > 0) set.Add(d.Pin);
            return new List<int>(set);
        }

        // Liste: çevrimiçi olanlar önce; çevrimdışı ama hatırlanmayanlar gösterilmez.
        public static List<Device> Snapshot()
        {
            lock (all)
            {
                var list = new List<Device>();
                foreach (var d in all.Values) if (d.Online || d.Known) list.Add(d);
                list.Sort((a, b) => a.Online != b.Online ? (a.Online ? -1 : 1) : string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
                return list;
            }
        }
    }

    public static class Proto
    {
        public const int TcpPort = 47800, UdpPort = 47801;
        public const int Buf = 1 << 20;

        public static void WriteBE(Stream s, long v, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i)));
        }

        public static long ReadBE(Stream s, int bytes)
        {
            long v = 0;
            for (int i = 0; i < bytes; i++) v = (v << 8) | (uint)ReadByte(s);
            return v;
        }

        public static void WriteStr(Stream s, string v)
        {
            byte[] b = Encoding.UTF8.GetBytes(v ?? "");
            if (b.Length > 65535) throw new IOException("Dosya yolu çok uzun");
            WriteBE(s, b.Length, 2);
            s.Write(b, 0, b.Length);
        }

        public static int ReadByte(Stream s)
        {
            int b = s.ReadByte();
            if (b < 0) throw new IOException("Karşı cihaz bağlantıyı kapattı");
            return b;
        }

        public static void ReadFully(Stream s, byte[] b, int n)
        {
            int o = 0;
            while (o < n)
            {
                int r = s.Read(b, o, n - o);
                if (r <= 0) throw new IOException("Karşı cihaz bağlantıyı kapattı");
                o += r;
            }
        }

        public static string ReadStr(Stream s)
        {
            int n = (int)ReadBE(s, 2);
            byte[] b = new byte[n];
            ReadFully(s, b, n);
            return Encoding.UTF8.GetString(b);
        }

        static byte[] Header(int pin, string path, long size)
        {
            var m = new MemoryStream();
            m.Write(Encoding.ASCII.GetBytes("HA2"), 0, 3);
            WriteBE(m, pin, 4);
            WriteStr(m, Me.Id);
            WriteStr(m, Me.Name);
            WriteStr(m, "pc");
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
                throw new IOException(ip + " adresine bağlanılamadı (cihazda Hızlı Aktar açık mı, aynı Wi-Fi'de mi?)");
            }
            c.EndConnect(ar);
            return c;
        }

        // Kod doğruysa true. Kod yanlış olsa da cihazın id, ad ve türünü döndürür.
        public static bool Ping(string ip, int port, int pin, out string id, out string name, out string type)
        {
            using (var c = Connect(ip, port))
            {
                var s = c.GetStream();
                byte[] h = Header(pin, "", -1);
                s.Write(h, 0, h.Length);
                int st = ReadByte(s);
                id = ReadStr(s);
                name = ReadStr(s);
                try { type = ReadStr(s); } catch (IOException) { type = "mobil"; } // 1.1 alıcıları tür göndermez
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
                if (st == 'S') throw new IOException("Karşı cihazda yeterli boş alan yok");
                if (st != 'K') throw new IOException("Karşı cihaz dosyayı reddetti");

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
                c.ReceiveTimeout = 180000; // karşı taraf son tamponu diske yazarken bekle
                if (ReadByte(s) != 'D') throw new IOException("Karşı cihaz kaydı onaylamadı");
            }
        }

        public static List<IPAddress> BroadcastAddresses()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] a = ua.Address.GetAddressBytes(), m = ua.IPv4Mask.GetAddressBytes();
                        for (int i = 0; i < 4; i++) a[i] = (byte)(a[i] | ~m[i]);
                        list.Add(new IPAddress(a));
                    }
                }
            }
            catch (Exception) { }
            return list;
        }

        public static string LocalIps()
        {
            var ips = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ni.GetIPProperties().GatewayAddresses.Count > 0)
                            ips.Add(ua.Address.ToString());
                }
            }
            catch (Exception) { }
            return ips.Count == 0 ? "?" : string.Join(", ", ips.ToArray());
        }
    }

    // UDP: diğer cihazların yayınlarını dinler ve bu PC'yi duyurur.
    public class Discovery
    {
        UdpClient udp;

        public void Start()
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Proto.UdpPort));
            var t = new Thread(Listen) { IsBackground = true };
            t.Start();
            var a = new Thread(Announce) { IsBackground = true };
            a.Start();
        }

        void Listen()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try { Handle(Encoding.UTF8.GetString(udp.Receive(ref ep)), ep.Address.ToString()); }
                catch (ObjectDisposedException) { return; }
                catch (Exception) { }
            }
        }

        public static void Handle(string msg, string ip)
        {
            int port;
            string[] p = msg.Split(new[] { '|' }, 5);
            if (p.Length == 5 && p[0] == "HA2" && int.TryParse(p[2], out port)) Store.Seen(p[1], p[4], p[3], ip, port, false);
            else
            {
                p = msg.Split(new[] { '|' }, 4);
                if (p.Length == 4 && p[0] == "HA1" && int.TryParse(p[2], out port)) Store.Seen(p[1], p[3], "mobil", ip, port, false);
            }
        }

        void Announce()
        {
            using (var u = new UdpClient())
            {
                u.EnableBroadcast = true;
                while (true)
                {
                    byte[] m = Encoding.UTF8.GetBytes("HA2|" + Me.Id + "|" + Proto.TcpPort + "|pc|" + Me.Name.Replace("|", " "));
                    foreach (var bc in Proto.BroadcastAddresses())
                        try { u.Send(m, m.Length, new IPEndPoint(bc, Proto.UdpPort)); } catch (Exception) { }
                    Thread.Sleep(1500);
                }
            }
        }
    }

    // TCP 47800: tablet/telefondan gelen dosyaları İndirilenler\HizliAktar'a kaydeder.
    public class Receiver
    {
        public static string SaveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "HizliAktar");
        public int Active;
        public long Bytes;
        public Action<string> Log = s => { };
        static readonly Regex reserved = new Regex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", RegexOptions.IgnoreCase);

        public void Start(int port)
        {
            var l = new TcpListener(IPAddress.Any, port);
            l.Server.ReceiveBufferSize = 4 << 20;
            l.Start();
            var t = new Thread(() =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = l.AcceptTcpClient(); } catch (Exception) { return; }
                    var w = new Thread(() => Handle(c)) { IsBackground = true };
                    w.Start();
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Karşıdan gelen yolu güvenli klasör parçalarına böler ("..", sürücü harfi vb. atılır).
        public static List<string> SafeParts(string path)
        {
            var parts = new List<string>();
            foreach (var raw in path.Split('/', '\\'))
            {
                var sb = new StringBuilder();
                foreach (char ch in raw) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
                string p = sb.ToString().Trim().TrimEnd('.');
                if (p.Length == 0 || p == "." || p == "..") continue;
                if (reserved.IsMatch(p)) p = "_" + p;
                parts.Add(p);
            }
            return parts;
        }

        static string Unique(string dir, string name)
        {
            string f = Path.Combine(dir, name), b = Path.GetFileNameWithoutExtension(name), e = Path.GetExtension(name);
            for (int i = 1; File.Exists(f); i++) f = Path.Combine(dir, b + " (" + i + ")" + e);
            return f;
        }

        void Handle(TcpClient client)
        {
            string target = null, path = "?";
            try
            {
                using (client)
                {
                    client.ReceiveBufferSize = 4 << 20;
                    client.ReceiveTimeout = 60000;
                    var ns = client.GetStream();
                    var s = new BufferedStream(ns, Proto.Buf);
                    byte[] magic = new byte[3];
                    Proto.ReadFully(s, magic, 3);
                    string mg = Encoding.ASCII.GetString(magic);
                    if (mg != "HA1" && mg != "HA2") return;
                    int pin = (int)Proto.ReadBE(s, 4);
                    string sid = null, sname = null, stype = null;
                    if (mg == "HA2") { sid = Proto.ReadStr(s); sname = Proto.ReadStr(s); stype = Proto.ReadStr(s); }
                    path = Proto.ReadStr(s);
                    long size = Proto.ReadBE(s, 8);
                    bool ok = pin == Me.Pin;
                    string ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    // Doğru kodla bağlanan cihaz bu tarafta da hatırlanır.
                    if (ok && sid != null) Store.Seen(sid, sname, stype, ip, Proto.TcpPort, true);

                    if (size < 0)
                    {
                        ns.WriteByte((byte)(ok ? 'K' : 'P'));
                        var m = new MemoryStream();
                        Proto.WriteStr(m, Me.Id); Proto.WriteStr(m, Me.Name); Proto.WriteStr(m, "pc");
                        ns.Write(m.ToArray(), 0, (int)m.Length);
                        return;
                    }
                    if (!ok) { ns.WriteByte((byte)'P'); Log("Yanlış kodla gönderim reddedildi: " + ip); return; }

                    var parts = SafeParts(path);
                    if (parts.Count == 0) return;
                    string name = parts[parts.Count - 1];
                    parts.RemoveAt(parts.Count - 1);
                    string dir = SaveDir;
                    foreach (var p in parts) dir = Path.Combine(dir, p);
                    Directory.CreateDirectory(dir);
                    // Boyut sınırı yok; tek koşul diskte yer olması.
                    if (new DriveInfo(Path.GetPathRoot(dir)).AvailableFreeSpace < size + (64L << 20))
                    {
                        ns.WriteByte((byte)'S');
                        Log("Yer yok: " + path);
                        return;
                    }
                    Interlocked.Increment(ref Active);
                    try
                    {
                        lock (this) { target = Unique(dir, name); File.Create(target).Dispose(); }
                        using (var fo = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, Proto.Buf, FileOptions.SequentialScan))
                        {
                            ns.WriteByte((byte)'K');
                            byte[] buf = new byte[Proto.Buf];
                            long left = size;
                            while (left > 0)
                            {
                                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, left));
                                if (n <= 0) throw new IOException("Bağlantı yarıda kesildi");
                                fo.Write(buf, 0, n);
                                left -= n;
                                Interlocked.Add(ref Bytes, n);
                            }
                        }
                        ns.WriteByte((byte)'D');
                        string from = sname ?? ip;
                        target = null;
                        Log("Alındı ← " + from + ": " + path + "  (" + (size / 1048576.0).ToString("0.0") + " MB)");
                    }
                    finally { Interlocked.Decrement(ref Active); }
                }
            }
            catch (Exception e)
            {
                Log("Alma hatası (" + path + "): " + e.Message);
                if (target != null) try { File.Delete(target); } catch (Exception) { }
            }
        }
    }

    // Bir cihaza bir grup dosyayı paralel bağlantılarla gönderir.
    public class Batch
    {
        public long TotalBytes, SentBytes;
        public int TotalFiles, DoneFiles;
        public volatile bool PinWrong;
        public readonly List<string> Errors = new List<string>();
        readonly List<KeyValuePair<string, string>> files; // tam yol -> karşıdaki göreli yol

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
