using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Hızlı Aktar")]
[assembly: AssemblyProduct("Hızlı Aktar")]
[assembly: AssemblyDescription("Aynı Wi-Fi ağında PC ile tablet/telefon arasında çift yönlü hızlı dosya aktarımı")]
[assembly: AssemblyCompany("Muhammed Hüseyin KILIÇ")]
[assembly: AssemblyCopyright("© 2026 Muhammed Hüseyin KILIÇ - MIT Lisansı")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace HizliAktar
{
    public class MainForm : Form
    {
        public const string Version = "1.3.0";
        public const string Author = "Muhammed Hüseyin KILIÇ";
        public const string RepoUrl = "https://github.com/wolverine6427/HizliAktar";
        public const string ApkUrl = RepoUrl + "/releases/latest/download/HizliAktar.apk";
        const int Parallel = 4;

        readonly Discovery discovery = new Discovery();
        readonly Receiver receiver = new Receiver();
        readonly ListBox lstDevices = new ListBox();
        readonly Label drop = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly Label lblStatus = new Label();
        readonly TextBox txtLog = new TextBox();
        readonly TextBox txtPin = new TextBox();
        readonly TextBox txtName = new TextBox();
        volatile Batch current;
        volatile bool busy;
        long lastBytes; DateTime lastTick;
        string shown = "";

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

        static Image Res(string name) { return Image.FromStream(Assembly.GetExecutingAssembly().GetManifestResourceStream(name)); }

        public MainForm()
        {
            Me.Load();
            Store.Load();

            Text = "Hızlı Aktar " + Version + "  —  PC ⇄ Tablet / Telefon";
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(760, 560);
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ---- Sol: cihazlar + bu PC ----
            var left = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(10) };
            var logo = new PictureBox { Image = Res("logo_small.png"), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Top, Height = 70 };
            var lblDev = new Label { Text = "Cihazlar (sağ tık: takma ad / unut):", Dock = DockStyle.Top, Height = 26 };
            lstDevices.Dock = DockStyle.Fill;
            lstDevices.SelectionMode = SelectionMode.MultiExtended;
            lstDevices.IntegralHeight = false;
            lstDevices.ContextMenuStrip = DeviceMenu();
            lstDevices.MouseDown += (s, e) =>
            {
                int i = lstDevices.IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Right && i >= 0 && !lstDevices.GetSelected(i)) { lstDevices.ClearSelected(); lstDevices.SetSelected(i, true); }
            };

            var me = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = new Padding(0, 8, 0, 0) };
            me.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            me.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            me.Controls.Add(new Label { Text = "Bu bilgisayarın adı:", AutoSize = true }, 0, 0);
            txtName.Text = Me.Name; txtName.Dock = DockStyle.Fill;
            me.Controls.Add(txtName, 0, 1);
            var btnName = new Button { Text = "Kaydet", Dock = DockStyle.Fill };
            btnName.Click += (s, e) => { if (txtName.Text.Trim().Length > 0) { Me.Name = txtName.Text.Trim(); Me.Save(); Log("Ad kaydedildi: " + Me.Name); } };
            me.Controls.Add(btnName, 1, 1);
            me.Controls.Add(new Label { Text = "Bu bilgisayarın eşleşme kodu:", AutoSize = true }, 0, 2);
            txtPin.Text = Me.Pin.ToString(); txtPin.Dock = DockStyle.Fill; txtPin.Font = new Font("Segoe UI", 14f, FontStyle.Bold); txtPin.ForeColor = Color.FromArgb(20, 100, 210);
            me.Controls.Add(txtPin, 0, 3);
            var btnPin = new Button { Text = "Kaydet", Dock = DockStyle.Fill };
            btnPin.Click += (s, e) =>
            {
                int v;
                string t = txtPin.Text.Trim();
                if (t.Length >= 4 && t.Length <= 9 && int.TryParse(t, out v) && v > 0) { Me.Pin = v; Me.Save(); Log("Kod kaydedildi. Tablet/telefonlara da aynı kodu yazarsanız hiç sorulmaz."); }
                else MessageBox.Show(this, "Kod 4-9 rakamdan oluşmalı.", "Hızlı Aktar");
            };
            me.Controls.Add(btnPin, 1, 3);
            var btnIp = new Button { Text = "IP ile cihaz ekle...", Dock = DockStyle.Fill, Height = 32 };
            btnIp.Click += (s, e) => AddByIp();
            me.Controls.Add(btnIp, 0, 4); me.SetColumnSpan(btnIp, 2);
            var btnGuide = new Button { Text = "Tablete / telefona nasıl kurulur?", Dock = DockStyle.Fill, Height = 36, Font = new Font("Segoe UI", 10f, FontStyle.Bold), BackColor = Color.FromArgb(255, 240, 225) };
            btnGuide.Click += (s, e) => ShowGuide();
            me.Controls.Add(btnGuide, 0, 5); me.SetColumnSpan(btnGuide, 2);
            me.Controls.Add(new Label { Text = "Sürüm " + Version + " • " + Author, AutoSize = true, ForeColor = Color.Gray }, 0, 6);
            me.SetColumnSpan(me.GetControlFromPosition(0, 6), 2);

            left.Controls.Add(lstDevices);
            left.Controls.Add(lblDev);
            left.Controls.Add(logo);
            left.Controls.Add(me);

            // ---- Sağ: bırakma alanı + ilerleme + kayıt ----
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            drop.Text = "Dosya veya klasörleri buraya sürükleyip bırakın\n(ya da tıklayıp seçin)\n\nSoldan seçili cihaza hemen gönderilir";
            drop.TextAlign = ContentAlignment.TopCenter;
            drop.Padding = new Padding(0, 30, 0, 0);
            drop.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            drop.BackColor = Color.FromArgb(240, 246, 255);
            drop.ForeColor = Color.FromArgb(25, 70, 140);
            drop.Image = Res("logo_bg.png");
            drop.ImageAlign = ContentAlignment.BottomCenter;
            drop.BorderStyle = BorderStyle.FixedSingle;
            drop.Dock = DockStyle.Fill;
            drop.AllowDrop = true;
            drop.Cursor = Cursors.Hand;
            drop.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            drop.DragDrop += (s, e) => StartSend((string[])e.Data.GetData(DataFormats.FileDrop));
            drop.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Multiselect = true, Title = "Gönderilecek dosyalar" })
                    if (dlg.ShowDialog(this) == DialogResult.OK) StartSend(dlg.FileNames);
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 230, Padding = new Padding(0, 8, 0, 0) };
            bar.Dock = DockStyle.Top; bar.Height = 22; bar.Maximum = 1000;
            lblStatus.Dock = DockStyle.Top; lblStatus.Height = 28;
            var btnFolder = new Button { Text = "Gelen dosyalar klasörünü aç  (" + Receiver.SaveDir + ")", Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.MiddleLeft };
            btnFolder.Click += (s, e) => { Directory.CreateDirectory(Receiver.SaveDir); Process.Start("explorer.exe", "\"" + Receiver.SaveDir + "\""); };
            txtLog.Dock = DockStyle.Fill; txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.BackColor = Color.White;
            bottom.Controls.Add(txtLog);
            bottom.Controls.Add(btnFolder);
            bottom.Controls.Add(lblStatus);
            bottom.Controls.Add(bar);
            right.Controls.Add(drop);
            right.Controls.Add(bottom);

            Controls.Add(right);
            Controls.Add(left);

            receiver.Log = Log;
            try { receiver.Start(Proto.TcpPort); }
            catch (Exception e) { Log("Alıcı başlatılamadı (port " + Proto.TcpPort + " kullanımda mı? Program iki kez açık olabilir): " + e.Message); }
            try { discovery.Start(); }
            catch (Exception e) { Log("Keşif başlatılamadı (UDP " + Proto.UdpPort + "): " + e.Message + " — 'IP ile cihaz ekle' kullanılabilir."); }
            Log("Hazır. Bu PC'nin IP adresi: " + Proto.LocalIps() + " • Gelen dosyalar: " + Receiver.SaveDir);

            var devTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            devTimer.Tick += (s, e) => RefreshDevices();
            devTimer.Start();
            var progTimer = new System.Windows.Forms.Timer { Interval = 300 };
            progTimer.Tick += (s, e) => UpdateProgress();
            progTimer.Start();
            RefreshDevices();
        }

        ContextMenuStrip DeviceMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Takma ad ver...", null, (s, e) =>
            {
                var d = lstDevices.SelectedItem as Device;
                if (d == null) return;
                string n = Prompt("\"" + d.Name + "\" için takma ad (boş bırakılırsa kaldırılır):", d.Nick);
                if (n == null) return;
                Store.Update(d, x => { x.Nick = n.Trim(); x.Known = true; });
                shown = ""; RefreshDevices();
            });
            m.Items.Add("Kodu unut (bir dahaki gönderimde sorulur)", null, (s, e) =>
            {
                foreach (Device d in lstDevices.SelectedItems) Store.Update(d, x => x.Pin = 0);
                Log("Kod silindi.");
            });
            m.Items.Add("Cihazı unut (listeden kaldır)", null, (s, e) =>
            {
                var sel = new List<Device>();
                foreach (Device d in lstDevices.SelectedItems) sel.Add(d);
                foreach (var d in sel) Store.Forget(d);
                shown = ""; RefreshDevices();
            });
            return m;
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        void RefreshDevices()
        {
            var list = Store.Snapshot();
            var selected = new HashSet<string>();
            foreach (Device d in lstDevices.SelectedItems) selected.Add(d.Id);
            var texts = new List<string>();
            foreach (var d in list) texts.Add(d.ToString());
            if (string.Join("\n", texts.ToArray()) != shown)
            {
                shown = string.Join("\n", texts.ToArray());
                lstDevices.BeginUpdate();
                lstDevices.Items.Clear();
                foreach (var d in list) lstDevices.Items.Add(d);
                for (int i = 0; i < list.Count; i++) if (selected.Contains(list[i].Id)) lstDevices.SetSelected(i, true);
                if (lstDevices.SelectedItems.Count == 0 && list.Count > 0 && list[0].Online) lstDevices.SetSelected(0, true);
                lstDevices.EndUpdate();
            }
            if (!busy)
            {
                int online = 0;
                foreach (var d in list) if (d.Online) online++;
                lblStatus.Text = receiver.Active > 0 ? "Alınıyor: " + receiver.Active + " dosya..."
                    : online == 0 ? "Cihaz aranıyor... (tablette/telefonda Hızlı Aktar açık olmalı)"
                    : online + " cihaz çevrimiçi. Dosyaları bırakın.";
            }
        }

        void AddByIp()
        {
            string ip = Prompt("Cihazın IP adresi (Hızlı Aktar ekranında yazar):", "");
            if (string.IsNullOrWhiteSpace(ip)) return;
            try
            {
                string id, name, type;
                ip = ip.Trim();
                Proto.Ping(ip, Proto.TcpPort, 0, out id, out name, out type);
                if (id == Me.Id) throw new IOException("Bu adres bu bilgisayarın kendisi.");
                Store.Seen(id, name, type, ip, Proto.TcpPort, true);
                Log(name + " eklendi (" + ip + ").");
                RefreshDevices();
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Bağlanılamadı", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // Kayıtlı kodu, bu PC'nin kodunu ve daha önce girilmiş kodları dener; olmazsa sorar. İptalde 0.
        int ResolvePin(Device d)
        {
            string id, name, type;
            var tries = new List<int>();
            if (d.Pin > 0) tries.Add(d.Pin);
            foreach (int k in Store.KnownPins()) if (!tries.Contains(k)) tries.Add(k);
            foreach (int k in tries)
            {
                bool ok = Proto.Ping(d.Ip, d.Port, k, out id, out name, out type);
                // Kayıtlı adreste artık başka bir cihaz (ya da bu PC'nin kendisi) olabilir: gönderme.
                if (id != d.Id) throw new IOException("\"" + d.Label + "\" bu adreste (" + d.Ip + ") bulunamadı. Cihazda Hızlı Aktar'ı açın; gerekirse IP ile ekleyin.");
                if (ok)
                {
                    if (k != d.Pin) Log(d.Label + " kayıtlı kodla otomatik eşleşti.");
                    Store.Update(d, x => { x.Pin = k; x.Known = true; });
                    return k;
                }
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                string s = Prompt("\"" + d.Label + "\" ile eşleştirme:\nO cihazın ekranındaki eşleşme kodunu girin (yalnızca bir kez sorulur).", "");
                int v;
                if (s == null) return 0;
                if (!int.TryParse(s.Trim(), out v)) continue;
                if (Proto.Ping(d.Ip, d.Port, v, out id, out name, out type)) { Store.Update(d, x => { x.Pin = v; x.Known = true; }); return v; }
                MessageBox.Show(this, "Kod yanlış. Tekrar deneyin.", "Eşleştirme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return 0;
        }

        void StartSend(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            if (busy) { MessageBox.Show(this, "Bir gönderim sürüyor, bitmesini bekleyin.", "Hızlı Aktar"); return; }
            var targets = new List<Device>();
            foreach (Device d in lstDevices.SelectedItems) targets.Add(d);
            if (targets.Count == 0) { MessageBox.Show(this, "Soldaki listeden bir cihaz seçin.\nCihazda Hızlı Aktar açık olmalı ve aynı Wi-Fi'ye bağlı olmalı.", "Hızlı Aktar"); return; }

            var errors = new List<string>();
            var files = Batch.Collect(paths, errors);
            foreach (var e in errors) Log("Atlandı: " + e);
            if (files.Count == 0) return;

            var jobs = new List<KeyValuePair<Device, int>>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (var d in targets)
                {
                    try
                    {
                        int pin = ResolvePin(d);
                        if (pin > 0) jobs.Add(new KeyValuePair<Device, int>(d, pin));
                    }
                    catch (Exception e) { Log(d.Label + ": " + e.Message); }
                }
            }
            finally { Cursor = Cursors.Default; }
            if (jobs.Count == 0) return;

            busy = true;
            var t = new Thread(() =>
            {
                // Uzun (ör. 100 GB) aktarımlarda bilgisayar uykuya geçmesin.
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                foreach (var job in jobs)
                {
                    var b = new Batch(files);
                    current = b;
                    lastBytes = 0; lastTick = DateTime.Now;
                    var sw = Stopwatch.StartNew();
                    Log(job.Key.Label + " cihazına " + b.TotalFiles + " dosya (" + Mb(b.TotalBytes) + ") gönderiliyor...");
                    b.Run(job.Key, job.Value, Parallel);
                    double sec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    if (b.PinWrong) { Store.Update(job.Key, x => x.Pin = 0); Log("Karşı cihazın kodu değişmiş. Tekrar bırakın, kod sorulacak."); }
                    Log(string.Format("Bitti: {0}/{1} dosya, {2}, {3:0.0} sn, ort. {4:0.0} MB/sn", b.DoneFiles, b.TotalFiles, Mb(b.SentBytes), sec, b.SentBytes / sec / 1048576.0));
                    foreach (var e in b.Errors) Log("HATA " + e);
                }
                SetThreadExecutionState(ES_CONTINUOUS);
                current = null;
                busy = false;
            });
            t.IsBackground = true;
            t.Start();
        }

        void UpdateProgress()
        {
            var b = current;
            if (b == null) { if (!busy && bar.Value != 0 && bar.Value != 1000) bar.Value = 0; return; }
            long sent = Interlocked.Read(ref b.SentBytes);
            bar.Value = b.TotalBytes == 0 ? 1000 : (int)Math.Max(0, Math.Min(1000, sent * 1000 / b.TotalBytes));
            double dt = (DateTime.Now - lastTick).TotalSeconds;
            if (dt < 0.9) return;
            double speed = (sent - lastBytes) / dt;
            lastBytes = sent; lastTick = DateTime.Now;
            string eta = speed > 0 ? TimeSpan.FromSeconds((b.TotalBytes - sent) / speed).ToString(@"hh\:mm\:ss") : "--:--";
            lblStatus.Text = string.Format("{0}/{1} dosya   {2} / {3}   {4:0.0} MB/sn   kalan {5}", b.DoneFiles, b.TotalFiles, Mb(sent), Mb(b.TotalBytes), speed / 1048576.0, eta);
        }

        static string Mb(long b) { return (b / 1048576.0).ToString("0.0") + " MB"; }

        void ShowGuide()
        {
            using (var f = new Form { Text = "Tablete / telefona kurulum", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(640, 520), Font = Font, BackColor = Color.White, MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog })
            {
                var qr = new PictureBox { Image = Res("qr_apk.png"), SizeMode = PictureBoxSizeMode.Zoom, Left = 16, Top = 16, Width = 220, Height = 220 };
                var lblQr = new Label { Text = "Tabletin/telefonun kamerasıyla okutun:\nAPK doğrudan iner.", Left = 16, Top = 240, Width = 220, Height = 48, TextAlign = ContentAlignment.TopCenter };
                var txt = new Label
                {
                    Left = 250, Top = 16, Width = 374, Height = 450,
                    Text =
                        "Tablet / telefon uygulaması (HizliAktar.apk)\n\n" +
                        "1) Tablette kamerayla soldaki QR kodu okutun\n" +
                        "    ya da tarayıcıda şu adresi açın:\n" +
                        "    github.com/wolverine6427/HizliAktar\n" +
                        "    → Releases → HizliAktar.apk\n\n" +
                        "2) İnen HizliAktar.apk dosyasına dokunun.\n" +
                        "    \"Bilinmeyen kaynaklar\" sorulursa izin verin.\n\n" +
                        "3) Hızlı Aktar'ı açın. Bu bilgisayar listede\n" +
                        "    kendiliğinden görünür.\n\n" +
                        "4) İki tarafa da aynı eşleşme kodunu yazarsanız\n" +
                        "    kod hiç sorulmaz. Bu PC'nin kodu: " + Me.Pin + "\n\n" +
                        "Huawei: Ayarlar → Pil → Uygulama başlatma →\nHızlı Aktar → Manuel yönet (hepsini açın).\n\n" +
                        "Modemsiz, en hızlı yol: Tablette ⚡ Doğrudan bağlantı\n→ Bağlantıyı aç. Bu PC'yi Wi-Fi listesinde görünen\nağa bağlayın; Windows sorarsa ağı \"Özel\" yapın."
                };
                var link = new LinkLabel { Text = "GitHub sayfasını aç", Left = 16, Top = 300, Width = 220, TextAlign = ContentAlignment.TopCenter };
                link.LinkClicked += (s, e) => Process.Start(RepoUrl + "/releases/latest");
                var copy = new Button { Text = "APK bağlantısını kopyala", Left = 16, Top = 330, Width = 220, Height = 34 };
                copy.Click += (s, e) => { Clipboard.SetText(ApkUrl); copy.Text = "Kopyalandı ✓"; };
                var ok = new Button { Text = "Tamam", DialogResult = DialogResult.OK, Left = 520, Top = 470, Width = 100, Height = 34 };
                f.Controls.AddRange(new Control[] { qr, lblQr, txt, link, copy, ok });
                f.AcceptButton = ok;
                f.ShowDialog(this);
            }
        }

        string Prompt(string text, string def)
        {
            using (var f = new Form { Text = "Hızlı Aktar", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, 160), Font = Font })
            {
                var lbl = new Label { Text = text, Left = 12, Top = 10, Width = 396, Height = 56 };
                var box = new TextBox { Text = def, Left = 12, Top = 70, Width = 396 };
                var ok = new Button { Text = "Tamam", DialogResult = DialogResult.OK, Left = 226, Top = 112, Width = 86, Height = 32 };
                var cancel = new Button { Text = "İptal", DialogResult = DialogResult.Cancel, Left = 322, Top = 112, Width = 86, Height = 32 };
                f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
