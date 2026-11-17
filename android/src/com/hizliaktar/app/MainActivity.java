package com.hizliaktar.app;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.*;

import java.io.IOException;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;

public class MainActivity extends Activity {
    private static final int REQ_PICK = 7;
    private static final ExecutorService bg = Executors.newCachedThreadPool();

    private TextView statusView, logView, sendView;
    private Button sendBtn;
    private ListView listView;
    private ArrayAdapter<String> adapter;
    private final List<Peers.Peer> shown = new ArrayList<>();
    private String shownKey = "", selectedId;
    private final List<Uri> pending = new ArrayList<>();
    private static volatile boolean sending;
    private static volatile String sendStatus = "";
    private final Handler h = new Handler(Looper.getMainLooper());
    private final Runnable tick = new Runnable() {
        @Override public void run() {
            refresh();
            h.postDelayed(this, 500);
        }
    };

    private int dp(int v) { return (int) (v * getResources().getDisplayMetrics().density); }
    private int drawable(String n) { return getResources().getIdentifier(n, "drawable", getPackageName()); }

    private TextView text(String s, float size) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(size);
        return t;
    }

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        int pad = dp(20);

        // Arka planda soluk logo, üstünde kaydırılabilir içerik
        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.WHITE);
        ImageView wm = new ImageView(this);
        wm.setImageResource(drawable("logo_bg"));
        root.addView(wm, new FrameLayout.LayoutParams(dp(380), dp(360), Gravity.CENTER));
        ScrollView scroll = new ScrollView(this);
        root.addView(scroll);

        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.VERTICAL);
        l.setPadding(pad, pad, pad, pad);
        scroll.addView(l);

        ImageView header = new ImageView(this);
        header.setImageResource(drawable("logo_full"));
        header.setAdjustViewBounds(true);
        header.setScaleType(ImageView.ScaleType.FIT_START);
        l.addView(header, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(70)));

        // Cihaz adı
        l.addView(text("\nBu cihazın adı (diğer cihazlarda bu ad görünür):", 14));
        LinearLayout row = new LinearLayout(this);
        final EditText nameEdit = new EditText(this);
        nameEdit.setSingleLine(true);
        nameEdit.setText(Prefs.name(this));
        row.addView(nameEdit, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));
        Button save = new Button(this);
        save.setText("Kaydet");
        save.setOnClickListener(v -> { Prefs.setName(this, nameEdit.getText().toString()); toast("Ad kaydedildi"); });
        row.addView(save);
        l.addView(row);

        // Eşleşme kodu
        l.addView(text("\nEşleşme kodu (4-9 rakam). Tüm cihazlarınıza (PC dahil) aynı kodu yazarsanız hiç sorulmaz:", 14));
        LinearLayout pinRow = new LinearLayout(this);
        pinRow.setGravity(Gravity.CENTER_VERTICAL);
        final EditText pinEdit = new EditText(this);
        pinEdit.setText(String.valueOf(Prefs.pin(this)));
        pinEdit.setInputType(InputType.TYPE_CLASS_NUMBER);
        pinEdit.setTextSize(40);
        pinEdit.setTypeface(Typeface.DEFAULT_BOLD);
        pinEdit.setTextColor(Color.rgb(20, 100, 210));
        pinEdit.setGravity(Gravity.CENTER_HORIZONTAL);
        pinRow.addView(pinEdit, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));
        Button pinSave = new Button(this);
        pinSave.setText("Kodu kaydet");
        pinSave.setOnClickListener(v -> toast(Prefs.setPin(this, pinEdit.getText().toString()) ? "Kod kaydedildi" : "Kod 4-9 rakamdan oluşmalı (0000 olamaz)"));
        pinRow.addView(pinSave);
        l.addView(pinRow);

        statusView = text("", 15);
        l.addView(statusView);

        // Cihaz listesi
        TextView devLbl = text("\nCihazlar  (dokun: seç  •  basılı tut: takma ad / unut)", 15);
        devLbl.setTypeface(Typeface.DEFAULT_BOLD);
        l.addView(devLbl);
        listView = new ListView(this);
        adapter = new ArrayAdapter<>(this, android.R.layout.simple_list_item_single_choice, new ArrayList<>());
        listView.setAdapter(adapter);
        listView.setChoiceMode(ListView.CHOICE_MODE_SINGLE);
        listView.setBackgroundColor(Color.argb(30, 20, 100, 210));
        listView.setOnItemClickListener((p, v, pos, id) -> selectedId = shown.get(pos).id);
        listView.setOnItemLongClickListener((p, v, pos, id) -> { deviceMenu(shown.get(pos)); return true; });
        // ScrollView içindeki listenin kendi kaydırması çalışsın
        listView.setOnTouchListener((v, e) -> { v.getParent().requestDisallowInterceptTouchEvent(true); return false; });
        l.addView(listView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(200)));

        LinearLayout btns = new LinearLayout(this);
        sendBtn = new Button(this);
        sendBtn.setText("📤  Dosya seç ve gönder");
        sendBtn.setTextSize(16);
        sendBtn.setOnClickListener(v -> onSendClick());
        btns.addView(sendBtn, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 2));
        Button ipBtn = new Button(this);
        ipBtn.setText("IP ile ekle");
        ipBtn.setOnClickListener(v -> addByIp());
        btns.addView(ipBtn, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));
        l.addView(btns);
        Button directBtn = new Button(this);
        directBtn.setText("⚡  Doğrudan bağlantı (modemsiz, en hızlı)");
        directBtn.setOnClickListener(v -> directMenu());
        l.addView(directBtn);
        sendView = text("", 15);
        sendView.setTextColor(Color.rgb(230, 100, 20));
        l.addView(sendView);

        // Kayıt
        l.addView(text("\nAlınan / gönderilen dosyalar  (gelenler: İndirilenler → HizliAktar):", 14));
        ScrollView sv = new ScrollView(this);
        logView = new TextView(this);
        logView.setTypeface(Typeface.MONOSPACE);
        logView.setTextIsSelectable(true);
        sv.addView(logView);
        sv.setBackgroundColor(Color.argb(20, 0, 0, 0));
        sv.setOnTouchListener((v, e) -> { v.getParent().requestDisallowInterceptTouchEvent(true); return false; });
        l.addView(sv, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(170)));

        Button guide = new Button(this);
        guide.setText("💻  Bilgisayar programını nasıl bulurum?");
        guide.setOnClickListener(v -> showGuide());
        l.addView(guide);

        Button stop = new Button(this);
        stop.setText("Hızlı Aktar'ı durdur ve çık");
        stop.setOnClickListener(v -> { stopService(new Intent(this, ReceiverService.class)); finish(); });
        l.addView(stop);

        TextView about = text("Hızlı Aktar " + BuildInfo.VERSION + "  •  Yazar: " + BuildInfo.AUTHOR, 13);
        about.setGravity(Gravity.CENTER_HORIZONTAL);
        about.setTextColor(Color.GRAY);
        l.addView(about);

        setContentView(root);
        // Uygulama açıkken ekran kilitlenmesin / kararmasın.
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        if (Build.VERSION.SDK_INT < 29 && checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.WRITE_EXTERNAL_STORAGE}, 2);

        Intent svc = new Intent(this, ReceiverService.class);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(svc); else startService(svc);
        handleShare(getIntent());
    }

    @Override protected void onNewIntent(Intent i) { super.onNewIntent(i); handleShare(i); }
    @Override protected void onResume() { super.onResume(); h.post(tick); }
    @Override protected void onPause() { super.onPause(); h.removeCallbacks(tick); }

    private void toast(String s) { Toast.makeText(this, s, Toast.LENGTH_SHORT).show(); }

    /** Başka bir uygulamadan "Paylaş → Hızlı Aktar" ile gelen dosyalar. */
    private void handleShare(Intent i) {
        if (i == null) return;
        List<Uri> uris = new ArrayList<>();
        if (Intent.ACTION_SEND.equals(i.getAction())) {
            Uri u = i.getParcelableExtra(Intent.EXTRA_STREAM);
            if (u != null) uris.add(u);
        } else if (Intent.ACTION_SEND_MULTIPLE.equals(i.getAction())) {
            ArrayList<Uri> list = i.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (list != null) uris.addAll(list);
        }
        if (uris.isEmpty()) return;
        pending.clear();
        pending.addAll(uris);
        toast(uris.size() + " dosya hazır. Bir cihaz seçip Gönder'e dokunun.");
    }

    private void refresh() {
        int n = ReceiverService.active.get();
        statusView.setText(ReceiverService.status + "   •   Bu cihazın IP'si: " + ReceiverService.localIp()
                + (n > 0 ? "   •   Alınıyor: " + n + " dosya" : ""));
        logView.setText(ReceiverService.logText());
        sendView.setText(sendStatus);
        sendBtn.setText(pending.isEmpty() ? "📤  Dosya seç ve gönder" : "📤  Paylaşılan " + pending.size() + " dosyayı gönder");

        List<Peers.Peer> list = Peers.list(this);
        StringBuilder key = new StringBuilder();
        for (Peers.Peer p : list) key.append(p).append('\n');
        if (key.toString().equals(shownKey)) return;
        shownKey = key.toString();
        shown.clear();
        shown.addAll(list);
        adapter.clear();
        for (Peers.Peer p : list) adapter.add(p.toString());
        if (selectedId == null && !list.isEmpty() && list.get(0).online()) selectedId = list.get(0).id;
        listView.clearChoices();
        for (int i = 0; i < list.size(); i++) if (list.get(i).id.equals(selectedId)) listView.setItemChecked(i, true);
    }

    private Peers.Peer selected() {
        for (Peers.Peer p : shown) if (p.id.equals(selectedId)) return p;
        return null;
    }

    private void onSendClick() {
        if (sending) { toast("Bir gönderim sürüyor"); return; }
        Peers.Peer p = selected();
        if (p == null) { toast("Önce listeden bir cihaz seçin. Cihazda Hızlı Aktar açık olmalı."); return; }
        if (!pending.isEmpty()) { List<Uri> u = new ArrayList<>(pending); pending.clear(); startSend(p, u); return; }
        Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        i.addCategory(Intent.CATEGORY_OPENABLE);
        i.setType("*/*");
        i.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        startActivityForResult(i, REQ_PICK);
    }

    @Override
    protected void onActivityResult(int req, int res, Intent data) {
        super.onActivityResult(req, res, data);
        if (req != REQ_PICK || res != RESULT_OK || data == null) return;
        List<Uri> uris = new ArrayList<>();
        if (data.getClipData() != null)
            for (int i = 0; i < data.getClipData().getItemCount(); i++) uris.add(data.getClipData().getItemAt(i).getUri());
        else if (data.getData() != null) uris.add(data.getData());
        Peers.Peer p = selected();
        if (p != null && !uris.isEmpty()) startSend(p, uris);
    }

    private void finishSend(String msg) {
        sendStatus = msg;
        sending = false;
        ReceiverService.log(msg);
    }

    /** Önce kayıtlı kodu, bu cihazın kodunu ve bilinen kodları dener; olmazsa kullanıcıya sorar. */
    private void startSend(Peers.Peer p, List<Uri> uris) {
        sending = true;
        sendStatus = "Bağlanılıyor: " + p.label() + "...";
        bg.execute(() -> {
            List<Integer> tries = new ArrayList<>();
            if (p.pin > 0) tries.add(p.pin);
            for (int k : Peers.knownPins(this)) if (!tries.contains(k)) tries.add(k);
            String[] info = new String[3];
            try {
                for (int k : tries) {
                    boolean ok = Sender.ping(this, p.ip, p.port, k, info);
                    // Kayıtlı adreste artık başka bir cihaz (ya da bu cihazın kendisi) olabilir: gönderme.
                    if (!p.id.equals(info[0])) {
                        finishSend("\"" + p.label() + "\" bu adreste (" + p.ip + ") bulunamadı. Cihazda Hızlı Aktar'ı açın; gerekirse IP ile ekleyin.");
                        return;
                    }
                    if (ok) { Peers.setPin(this, p, k); doSend(p, k, uris); return; }
                }
            } catch (IOException e) {
                finishSend("Bağlanılamadı (" + p.label() + "): " + e.getMessage());
                return;
            }
            runOnUiThread(() -> askPin(p, uris));
        });
    }

    private void askPin(Peers.Peer p, List<Uri> uris) {
        final EditText e = new EditText(this);
        e.setInputType(InputType.TYPE_CLASS_NUMBER);
        e.setTextSize(28);
        new AlertDialog.Builder(this)
                .setTitle("\"" + p.label() + "\" ile eşleştirme")
                .setMessage("O cihazın ekranındaki eşleşme kodunu girin. Yalnızca bir kez sorulur.")
                .setView(e)
                .setPositiveButton("Eşleş ve gönder", (d, w) -> bg.execute(() -> {
                    int v;
                    try { v = Integer.parseInt(e.getText().toString().trim()); } catch (NumberFormatException x) { v = -1; }
                    try {
                        if (v > 0 && Sender.ping(this, p.ip, p.port, v, new String[3])) { Peers.setPin(this, p, v); doSend(p, v, uris); }
                        else runOnUiThread(() -> { toast("Kod yanlış"); askPin(p, uris); });
                    } catch (IOException x) { finishSend("Bağlanılamadı: " + x.getMessage()); }
                }))
                .setNegativeButton("İptal", (d, w) -> finishSend("Gönderim iptal edildi"))
                .setOnCancelListener(d -> finishSend("Gönderim iptal edildi"))
                .show();
    }

    /** Dosyaları 4 paralel bağlantıyla gönderir (arka plan iş parçacığında çağrılır). */
    private void doSend(Peers.Peer p, int pin, List<Uri> uris) {
        List<Sender.Item> items = new ArrayList<>();
        long total = 0;
        for (Uri u : uris) { Sender.Item it = Sender.describe(this, u); items.add(it); total += Math.max(0, it.size); }
        final long totalBytes = total;
        AtomicLong prog = new AtomicLong();
        AtomicInteger ok = new AtomicInteger();
        final boolean[] pinWrong = {false};
        ExecutorService ex = Executors.newFixedThreadPool(Math.max(1, Math.min(4, items.size())));
        for (Sender.Item it : items)
            ex.execute(() -> {
                if (pinWrong[0]) return;
                try {
                    Sender.send(this, p, pin, it, prog);
                    ok.incrementAndGet();
                    ReceiverService.log("Gönderildi → " + p.label() + ": " + it.name);
                } catch (Sender.PinException e) {
                    pinWrong[0] = true;
                } catch (Exception e) {
                    ReceiverService.log("HATA gönderim " + it.name + ": " + e.getMessage());
                }
            });
        ex.shutdown();
        long start = System.currentTimeMillis(), lastBytes = 0, lastT = start;
        try {
            while (!ex.awaitTermination(500, TimeUnit.MILLISECONDS)) {
                long now = System.currentTimeMillis(), b = prog.get();
                double speed = (b - lastBytes) * 1000.0 / Math.max(1, now - lastT);
                lastBytes = b; lastT = now;
                sendStatus = String.format(Locale.US, "Gönderiliyor → %s:  %d/%d dosya  •  %.1f / %.1f MB  •  %.1f MB/sn",
                        p.label(), ok.get(), items.size(), b / 1048576.0, totalBytes / 1048576.0, speed / 1048576.0);
            }
        } catch (InterruptedException ignored) { }
        if (pinWrong[0]) Peers.setPin(this, p, 0);
        double sec = Math.max(0.001, (System.currentTimeMillis() - start) / 1000.0);
        finishSend(String.format(Locale.US, "Bitti → %s: %d/%d dosya, %.1f MB, ort. %.1f MB/sn%s", p.label(), ok.get(), items.size(),
                prog.get() / 1048576.0, prog.get() / sec / 1048576.0, pinWrong[0] ? " (karşı cihazın kodu değişmiş, tekrar deneyin)" : ""));
    }

    private void deviceMenu(Peers.Peer p) {
        new AlertDialog.Builder(this)
                .setTitle(p.label())
                .setItems(new String[]{"Takma ad ver", "Kodu unut (bir dahaki gönderimde sorulur)", "Cihazı unut (listeden kaldır)"}, (d, which) -> {
                    if (which == 0) {
                        final EditText e = new EditText(this);
                        e.setSingleLine(true);
                        e.setText(p.nick);
                        new AlertDialog.Builder(this).setTitle("Takma ad").setMessage("Boş bırakılırsa takma ad kaldırılır.").setView(e)
                                .setPositiveButton("Kaydet", (d2, w) -> { Peers.setNick(this, p, e.getText().toString()); shownKey = ""; })
                                .setNegativeButton("İptal", null).show();
                    } else if (which == 1) {
                        Peers.setPin(this, p, 0);
                        toast("Kod silindi");
                    } else {
                        Peers.forget(this, p);
                        if (p.id.equals(selectedId)) selectedId = null;
                        shownKey = "";
                    }
                })
                .show();
    }

    private void addByIp() {
        final EditText e = new EditText(this);
        e.setHint("örn. 192.168.1.20");
        e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        new AlertDialog.Builder(this)
                .setTitle("IP ile cihaz ekle")
                .setMessage("Karşı cihazın IP adresini girin (PC'de Hızlı Aktar penceresinin altındaki kayıtta yazar).")
                .setView(e)
                .setPositiveButton("Ekle", (d, w) -> {
                    // "IP" ya da "IP:port"
                    String[] hp = e.getText().toString().trim().split(":");
                    String ip = hp[0];
                    int port;
                    try { port = hp.length > 1 ? Integer.parseInt(hp[1]) : ReceiverService.TCP_PORT; } catch (NumberFormatException x) { port = ReceiverService.TCP_PORT; }
                    final int fport = port;
                    bg.execute(() -> {
                        String[] info = new String[3];
                        try {
                            Sender.ping(this, ip, fport, 0, info);
                            if (Prefs.id(this).equals(info[0])) { runOnUiThread(() -> toast("Bu adres bu cihazın kendisi")); return; }
                            Peers.Peer p = Peers.seen(this, info[0], info[1], info[2], ip, fport, true);
                            runOnUiThread(() -> { if (p != null) selectedId = p.id; toast(info[1] + " eklendi"); });
                        } catch (IOException x) {
                            runOnUiThread(() -> toast("Bağlanılamadı: " + x.getMessage()));
                        }
                    });
                })
                .setNegativeButton("İptal", null)
                .show();
    }

    /** Wi-Fi Direct: modem yerine iki cihaz arasında doğrudan, tek atlamalı bağlantı. */
    private void directMenu() {
        if (!Direct.supported()) { toast("Doğrudan bağlantı Android 10 ve üstünde çalışır. Aynı Wi-Fi'yi kullanın."); return; }
        String perm = Build.VERSION.SDK_INT >= 33 ? "android.permission.NEARBY_WIFI_DEVICES" : Manifest.permission.ACCESS_FINE_LOCATION;
        if (checkSelfPermission(perm) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{perm}, 3);
            toast("İzin verip düğmeye tekrar dokunun");
            return;
        }
        new AlertDialog.Builder(this)
                .setTitle("⚡ Doğrudan bağlantı")
                .setMessage("Cihazlar modeme gerek olmadan birbirine doğrudan bağlanır (Huawei Share gibi). "
                        + "Aynı modemden geçen bağlantıya göre yaklaşık 2 kat hızlıdır ve Wi-Fi olmayan yerde de çalışır.\n\n"
                        + "1) Bir cihazda \"Bağlantıyı aç\" seçin.\n"
                        + "2) Diğer Android cihazda \"Bağlantıya katıl\" seçin. İki cihazın eşleşme kodu aynı olmalı.\n"
                        + "3) Bilgisayar ise Wi-Fi listesinden açılan ağa bağlanır (ağ adı ve şifre bağlantıyı açan cihazda görünür).\n\n"
                        + "Bu cihazın kodu: " + Prefs.pin(this))
                .setPositiveButton("Bağlantıyı aç", (d, w) -> Direct.host(this, err -> runOnUiThread(() -> {
                    if (err != null) { toast(err); return; }
                    ReceiverService.log("Doğrudan bağlantı açıldı: " + Direct.ssid(this));
                    new AlertDialog.Builder(this)
                            .setTitle("Doğrudan bağlantı açık")
                            .setMessage("Diğer Android cihaz: Hızlı Aktar → ⚡ Doğrudan bağlantı → Bağlantıya katıl.\n\n"
                                    + "Bilgisayar veya başka cihaz: Wi-Fi listesinden bu ağa bağlanın:\n\n"
                                    + "Ağ adı:  " + Direct.ssid(this) + "\nŞifre:  " + Direct.pass(this) + "\n\n"
                                    + "Bağlanınca cihazlar listede kendiliğinden görünür. Görünmezse \"IP ile ekle\": " + Direct.OWNER_IP)
                            .setPositiveButton("Tamam", null).show();
                })))
                .setNeutralButton("Bağlantıya katıl", (d, w) -> Direct.join(this, err -> runOnUiThread(() -> {
                    if (err != null) { toast(err); return; }
                    toast("Katılınıyor...");
                    findOwner();
                })))
                .setNegativeButton("Bağlantıyı kapat", (d, w) -> Direct.close(this, err -> runOnUiThread(() -> toast("Doğrudan bağlantı kapatıldı"))))
                .show();
    }

    /** Katılan taraf: bağlantıyı açan cihazı (her zaman 192.168.49.1) bulup listeye ekler. */
    private void findOwner() {
        bg.execute(() -> {
            for (int i = 0; i < 30; i++) {
                String[] info = new String[3];
                try {
                    Sender.ping(this, Direct.OWNER_IP, ReceiverService.TCP_PORT, 0, info);
                    Peers.Peer p = Peers.seen(this, info[0], info[1], info[2], Direct.OWNER_IP, ReceiverService.TCP_PORT, true);
                    runOnUiThread(() -> { if (p != null) selectedId = p.id; toast("Doğrudan bağlandı: " + info[1]); });
                    return;
                } catch (IOException e) {
                    try { Thread.sleep(1000); } catch (InterruptedException x) { return; }
                }
            }
            runOnUiThread(() -> toast("Bağlantıyı açan cihaz bulunamadı. Eşleşme kodları aynı mı?"));
        });
    }

    private void showGuide() {
        TextView t = new TextView(this);
        int p = dp(20);
        t.setPadding(p, p / 2, p, 0);
        t.setTextSize(15);
        t.setTextIsSelectable(true);
        t.setText("Bilgisayar programı (Windows): HizliAktar.exe\n\n"
                + "1) Bilgisayarda tarayıcıyı açın ve şu adrese gidin:\n"
                + "   github.com/wolverine6427/HizliAktar\n\n"
                + "2) Sağ taraftaki \"Releases\" bölümüne tıklayın ve en son sürümdeki HizliAktar.exe dosyasını indirin.\n"
                + "   (Doğrudan indirme bağlantısını aşağıdaki düğmeyle kopyalayıp kendinize gönderebilirsiniz.)\n\n"
                + "3) İnen HizliAktar.exe dosyasına çift tıklayın. Kurulum gerekmez.\n"
                + "   Windows \"bilinmeyen yayıncı\" uyarısı verirse \"Ek bilgi → Yine de çalıştır\" seçin.\n"
                + "   Güvenlik duvarı sorarsa \"Özel ağlar\" için izin verin.\n\n"
                + "4) Bilgisayar ve bu cihaz aynı Wi-Fi'de ise birbirlerini listede kendiliğinden görür.\n"
                + "   İki tarafa da aynı eşleşme kodunu yazarsanız kod hiç sorulmaz. Bu cihazın kodu: " + Prefs.pin(this));
        new AlertDialog.Builder(this)
                .setTitle("Bilgisayar programını bulma")
                .setView(t)
                .setPositiveButton("GitHub'ı aç", (d, w) -> startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(BuildInfo.REPO + "/releases/latest"))))
                .setNeutralButton("Bağlantıyı kopyala", (d, w) -> {
                    ((ClipboardManager) getSystemService(CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("Hızlı Aktar", BuildInfo.EXE_URL));
                    toast("İndirme bağlantısı kopyalandı");
                })
                .setNegativeButton("Kapat", null)
                .show();
    }
}
