using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Hızlı Aktar")]
[assembly: AssemblyProduct("Hızlı Aktar")]
[assembly: AssemblyDescription("Aynı Wi-Fi ağında PC'den tablet/telefona hızlı dosya aktarımı")]
[assembly: AssemblyCompany("Muhammed Hüseyin KILIÇ")]
[assembly: AssemblyCopyright("© 2026 Muhammed Hüseyin KILIÇ - MIT Lisansı")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace HizliAktar
{
    public class MainForm : Form
    {
        const int Parallel = 4;
        readonly Discovery discovery = new Discovery();
        readonly ListBox lstDevices = new ListBox();
        readonly Label drop = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly Label lblStatus = new Label();
        readonly TextBox txtLog = new TextBox();
        volatile Batch current;
        volatile bool busy;
        long lastBytes; DateTime lastTick;

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

        public const string Version = "1.1.0";
        public const string Author = "Muhammed Hüseyin KILIÇ";

        public MainForm()
        {
            Text = "Hızlı Aktar " + Version + " - PC → Tablet / Telefon";
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(640, 450);

            var left = new Panel { Dock = DockStyle.Left, Width = 280, Padding = new Padding(10) };
            var lblDev = new Label { Text = "Aynı Wi-Fi'deki tabletler:", Dock = DockStyle.Top, Height = 26 };
            lstDevices.Dock = DockStyle.Fill;
            lstDevices.SelectionMode = SelectionMode.MultiExtended;
            lstDevices.IntegralHeight = false;
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 80, FlowDirection = FlowDirection.TopDown };
            var btnIp = new Button { Text = "IP ile ekle...", Width = 250, Height = 32 };
            var btnPin = new Button { Text = "Seçilinin PIN'ini unut", Width = 250, Height = 32 };
            btnIp.Click += (s, e) => AddByIp();
            btnPin.Click += (s, e) => { foreach (Device d in lstDevices.SelectedItems) Pins.Remove(d.Id); Log("PIN silindi, sonraki gönderimde tekrar sorulacak."); };
            btnPanel.Height = 110;
            btnPanel.Controls.Add(btnIp);
            btnPanel.Controls.Add(btnPin);
            btnPanel.Controls.Add(new Label { Text = "Yazar: " + Author, Width = 250, Height = 24, ForeColor = Color.Gray });
            left.Controls.Add(lstDevices);
            left.Controls.Add(btnPanel);
            left.Controls.Add(lblDev);

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            drop.Text = "Dosya veya klasörleri buraya sürükleyip bırakın\n\n(ya da tıklayıp seçin)\n\nSeçili tablete hemen gönderilir";
            drop.TextAlign = ContentAlignment.MiddleCenter;
            drop.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            drop.BackColor = Color.FromArgb(232, 241, 255);
            drop.ForeColor = Color.FromArgb(30, 60, 120);
            drop.BorderStyle = BorderStyle.FixedSingle;
            drop.Dock = DockStyle.Fill;
            drop.AllowDrop = true;
            drop.Cursor = Cursors.Hand;
            drop.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            drop.DragDrop += (s, e) => StartSend((string[])e.Data.GetData(DataFormats.FileDrop));
            drop.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Multiselect = true, Title = "Tablete gönderilecek dosyalar" })
                    if (dlg.ShowDialog(this) == DialogResult.OK) StartSend(dlg.FileNames);
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 190, Padding = new Padding(0, 8, 0, 0) };
            bar.Dock = DockStyle.Top; bar.Height = 22; bar.Maximum = 1000;
            lblStatus.Dock = DockStyle.Top; lblStatus.Height = 28; lblStatus.Text = "Tablet aranıyor... (tablette Hızlı Aktar açık olmalı)";
            txtLog.Dock = DockStyle.Fill; txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            bottom.Controls.Add(txtLog);
            bottom.Controls.Add(lblStatus);
            bottom.Controls.Add(bar);
            right.Controls.Add(drop);
            right.Controls.Add(bottom);

            Controls.Add(right);
            Controls.Add(left);

            try { discovery.Start(); }
            catch (Exception e) { Log("Keşif başlatılamadı (UDP " + Proto.UdpPort + " kullanımda mı?): " + e.Message + " — 'IP ile ekle' kullanabilirsiniz."); }

            var devTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            devTimer.Tick += (s, e) => RefreshDevices();
            devTimer.Start();
            var progTimer = new System.Windows.Forms.Timer { Interval = 300 };
            progTimer.Tick += (s, e) => UpdateProgress();
            progTimer.Start();
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        void RefreshDevices()
        {
            var list = discovery.Snapshot();
            var selected = new HashSet<string>();
            foreach (Device d in lstDevices.SelectedItems) selected.Add(d.Id);
            bool same = list.Count == lstDevices.Items.Count;
            for (int i = 0; same && i < list.Count; i++)
                same = lstDevices.Items[i].ToString() == list[i].ToString();
            if (same) return;
            lstDevices.BeginUpdate();
            lstDevices.Items.Clear();
            foreach (var d in list) lstDevices.Items.Add(d);
            for (int i = 0; i < list.Count; i++) if (selected.Contains(list[i].Id)) lstDevices.SetSelected(i, true);
            if (lstDevices.SelectedItems.Count == 0 && list.Count > 0) lstDevices.SetSelected(0, true);
            lstDevices.EndUpdate();
            if (!busy) lblStatus.Text = list.Count == 0 ? "Tablet aranıyor... (tablette Hızlı Aktar açık olmalı)" : list.Count + " tablet bulundu. Dosyaları bırakın.";
        }

        void AddByIp()
        {
            string ip = Prompt("Tabletin IP adresi (tablet uygulamasında yazar):", "");
            if (string.IsNullOrWhiteSpace(ip)) return;
            try
            {
                string id, name;
                Proto.Ping(ip.Trim(), Proto.TcpPort, 0, out id, out name);
                discovery.Upsert(id, name, ip.Trim(), Proto.TcpPort, true);
                Log(name + " eklendi (" + ip.Trim() + ").");
                RefreshDevices();
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Bağlanılamadı", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // Kayıtlı PIN'i doğrular, yoksa/yanlışsa kullanıcıya sorar. İptalde null.
        int? ResolvePin(Device d)
        {
            int? pin = Pins.Get(d.Id);
            if (pin == null)
            {
                string id, name;
                foreach (int k in Pins.Known())
                    if (Proto.Ping(d.Ip, d.Port, k, out id, out name)) { Pins.Set(d.Id, k); Log(d.Name + " kayıtlı kodla otomatik eşleşti."); return k; }
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (pin == null)
                {
                    string s = Prompt("\"" + d.Name + "\" ile eşleştirme:\nCihazda görünen eşleşme kodunu girin (bir kez sorulur).", "");
                    int v;
                    if (s == null) return null;
                    if (!int.TryParse(s.Trim(), out v)) continue;
                    pin = v;
                }
                string id, name;
                if (Proto.Ping(d.Ip, d.Port, pin.Value, out id, out name)) { Pins.Set(d.Id, pin.Value); return pin; }
                Pins.Remove(d.Id);
                MessageBox.Show(this, "PIN yanlış. Tekrar deneyin.", "Eşleştirme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pin = null;
            }
            return null;
        }

        void StartSend(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            if (busy) { MessageBox.Show(this, "Bir aktarım sürüyor, bitmesini bekleyin.", "Hızlı Aktar"); return; }
            var targets = new List<Device>();
            foreach (Device d in lstDevices.SelectedItems) targets.Add(d);
            if (targets.Count == 0) { MessageBox.Show(this, "Listede seçili tablet yok.\nTablette Hızlı Aktar'ı açın ve iki cihazın aynı Wi-Fi'de olduğundan emin olun.", "Hızlı Aktar"); return; }

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
                        int? pin = ResolvePin(d);
                        if (pin != null) jobs.Add(new KeyValuePair<Device, int>(d, pin.Value));
                    }
                    catch (Exception e) { Log(d.Name + ": " + e.Message); }
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
                    Log(job.Key.Name + " cihazına " + b.TotalFiles + " dosya (" + Mb(b.TotalBytes) + ") gönderiliyor...");
                    b.Run(job.Key, job.Value, Parallel);
                    double sec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    if (b.PinWrong) { Pins.Remove(job.Key.Id); Log("Tabletteki PIN değişmiş. Tekrar bırakın, PIN sorulacak."); }
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
            string eta = speed > 0 ? TimeSpan.FromSeconds((b.TotalBytes - sent) / speed).ToString(@"mm\:ss") : "--:--";
            lblStatus.Text = string.Format("{0}/{1} dosya   {2} / {3}   {4:0.0} MB/sn   kalan {5}", b.DoneFiles, b.TotalFiles, Mb(sent), Mb(b.TotalBytes), speed / 1048576.0, eta);
        }

        static string Mb(long b) { return (b / 1048576.0).ToString("0.0") + " MB"; }

        string Prompt(string text, string def)
        {
            using (var f = new Form { Text = "Hızlı Aktar", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(380, 150), Font = Font })
            {
                var lbl = new Label { Text = text, Left = 12, Top = 10, Width = 356, Height = 50 };
                var box = new TextBox { Text = def, Left = 12, Top = 64, Width = 356 };
                var ok = new Button { Text = "Tamam", DialogResult = DialogResult.OK, Left = 186, Top = 104, Width = 86, Height = 32 };
                var cancel = new Button { Text = "İptal", DialogResult = DialogResult.Cancel, Left = 282, Top = 104, Width = 86, Height = 32 };
                f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
