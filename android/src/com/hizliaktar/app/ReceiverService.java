package com.hizliaktar.app;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Environment;
import android.os.IBinder;
import android.os.PowerManager;
import android.provider.MediaStore;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.*;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Arka planda çalışan alıcı. Protokol PC tarafındaki Proto.cs ile aynıdır:
 * UDP 47801'e yayın ile kendini duyurur, TCP 47800'den dosya alır,
 * İndirilenler/HizliAktar klasörüne kaydeder.
 */
public class ReceiverService extends Service {
    static final int TCP_PORT = 47800, UDP_PORT = 47801;
    static final int BUF = 1 << 20;
    static volatile String status = "Başlatılıyor...";
    static final AtomicInteger active = new AtomicInteger();
    private static final ArrayDeque<String> LOG = new ArrayDeque<>();

    private volatile boolean running;
    private ServerSocket server;
    private final ExecutorService pool = Executors.newCachedThreadPool();
    private WifiManager.WifiLock wifiLock;
    private PowerManager.WakeLock wakeLock;
    private WifiManager.MulticastLock multicastLock;
    private DatagramSocket udpListen;

    static synchronized void log(String s) {
        LOG.addFirst(new SimpleDateFormat("HH:mm:ss", Locale.US).format(new Date()) + "  " + s);
        while (LOG.size() > 300) LOG.removeLast();
    }

    static synchronized String logText() {
        StringBuilder b = new StringBuilder();
        for (String s : LOG) b.append(s).append('\n');
        return b.toString();
    }

    @Override public IBinder onBind(Intent i) { return null; }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (running) return START_STICKY;
        running = true;
        startForeground(1, notification());
        // Wi-Fi güç tasarrufu hızı ciddi düşürür; açık kaldığı sürece kapat.
        WifiManager wm = (WifiManager) getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        wifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "HizliAktar:wifi");
        wifiLock.acquire();
        PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
        wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "HizliAktar:cpu");
        wakeLock.acquire();
        // Bazı cihazlar yayın paketlerini ancak bu kilit varken uygulamaya iletir.
        multicastLock = wm.createMulticastLock("HizliAktar:discovery");
        multicastLock.setReferenceCounted(false);
        multicastLock.acquire();
        pool.execute(this::serve);
        pool.execute(this::announce);
        pool.execute(this::listen);
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        running = false;
        try { if (server != null) server.close(); } catch (IOException ignored) { }
        pool.shutdownNow();
        if (wifiLock != null && wifiLock.isHeld()) wifiLock.release();
        if (wakeLock != null && wakeLock.isHeld()) wakeLock.release();
        if (multicastLock != null && multicastLock.isHeld()) multicastLock.release();
        if (udpListen != null) udpListen.close();
        status = "Durduruldu";
        super.onDestroy();
    }

    private Notification notification() {
        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel ch = new NotificationChannel("ha", "Hızlı Aktar", NotificationManager.IMPORTANCE_LOW);
            ((NotificationManager) getSystemService(NOTIFICATION_SERVICE)).createNotificationChannel(ch);
            b = new Notification.Builder(this, "ha");
        } else {
            b = new Notification.Builder(this);
        }
        PendingIntent pi = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class), PendingIntent.FLAG_IMMUTABLE);
        return b.setSmallIcon(android.R.drawable.stat_sys_download_done)
                .setContentTitle("Hızlı Aktar hazır")
                .setContentText("Dosya alıp göndermeye hazır")
                .setContentIntent(pi)
                .setOngoing(true)
                .build();
    }

    static String localIp() {
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback()) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses()))
                    if (a instanceof Inet4Address && a.isSiteLocalAddress()) return a.getHostAddress();
            }
        } catch (SocketException ignored) { }
        return "?";
    }

    private void serve() {
        try {
            server = new ServerSocket();
            server.setReuseAddress(true);
            server.setReceiveBufferSize(4 << 20);
            server.bind(new InetSocketAddress(TCP_PORT));
            status = "Hazır";
            while (running) {
                final Socket s = server.accept();
                pool.execute(() -> handle(s));
            }
        } catch (IOException e) {
            if (running) status = "Sunucu hatası: " + e.getMessage();
        }
    }

    private void announce() {
        try (DatagramSocket u = new DatagramSocket()) {
            u.setBroadcast(true);
            while (running) {
                byte[] m = ("HA2|" + Prefs.id(this) + "|" + TCP_PORT + "|mobil|" + Prefs.name(this)).getBytes(StandardCharsets.UTF_8);
                for (InetAddress bc : broadcastAddresses()) {
                    try { u.send(new DatagramPacket(m, m.length, bc, UDP_PORT)); } catch (IOException ignored) { }
                }
                Thread.sleep(1500);
            }
        } catch (Exception e) {
            if (running) log("Yayın hatası: " + e);
        }
    }

    /** Diğer cihazların (PC, tablet, telefon) yayınlarını dinler. */
    private void listen() {
        try {
            udpListen = new DatagramSocket(null);
            udpListen.setReuseAddress(true);
            udpListen.bind(new InetSocketAddress(UDP_PORT));
            byte[] buf = new byte[1024];
            while (running) {
                DatagramPacket pk = new DatagramPacket(buf, buf.length);
                udpListen.receive(pk);
                String[] p = new String(pk.getData(), 0, pk.getLength(), StandardCharsets.UTF_8).split("\\|", 5);
                try {
                    String ip = pk.getAddress().getHostAddress();
                    if (p.length == 5 && p[0].equals("HA2")) Peers.seen(this, p[1], p[4], p[3], ip, Integer.parseInt(p[2]), false);
                    else if (p.length >= 4 && p[0].equals("HA1")) Peers.seen(this, p[1], p[3], "mobil", ip, Integer.parseInt(p[2]), false);
                } catch (NumberFormatException ignored) { }
            }
        } catch (IOException e) {
            if (running) log("Keşif dinleme hatası: " + e.getMessage());
        }
    }

    private static List<InetAddress> broadcastAddresses() {
        List<InetAddress> list = new ArrayList<>();
        try {
            list.add(InetAddress.getByName("255.255.255.255"));
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback()) continue;
                for (InterfaceAddress ia : ni.getInterfaceAddresses())
                    if (ia.getBroadcast() != null) list.add(ia.getBroadcast());
            }
        } catch (Exception ignored) { }
        return list;
    }

    static String readStr(DataInputStream in) throws IOException {
        byte[] b = new byte[in.readUnsignedShort()];
        in.readFully(b);
        return new String(b, StandardCharsets.UTF_8);
    }

    static void writeStr(DataOutputStream out, String s) throws IOException {
        byte[] b = s.getBytes(StandardCharsets.UTF_8);
        out.writeShort(b.length);
        out.write(b);
    }

    /** PC'den gelen yolu güvenli klasör parçalarına böler ("..", sürücü harfi vb. atılır). */
    static List<String> safeParts(String path) {
        List<String> parts = new ArrayList<>();
        for (String p : path.split("[/\\\\]")) {
            p = p.replaceAll("[\\x00-\\x1f:*?\"<>|]", "_").trim();
            if (p.isEmpty() || p.equals(".") || p.equals("..")) continue;
            parts.add(p);
        }
        return parts;
    }

    private void handle(Socket s) {
        Uri pendingUri = null;
        File legacyFile = null;
        String path = "?";
        try (Socket sock = s) {
            sock.setReceiveBufferSize(4 << 20);
            sock.setSoTimeout(60000);
            DataInputStream in = new DataInputStream(new BufferedInputStream(sock.getInputStream(), BUF));
            DataOutputStream out = new DataOutputStream(sock.getOutputStream());
            byte[] magic = new byte[3];
            in.readFully(magic);
            String mg = new String(magic, StandardCharsets.US_ASCII);
            if (!mg.equals("HA1") && !mg.equals("HA2")) return;
            int pin = in.readInt();
            String sid = null, sname = null, stype = null;
            if (mg.equals("HA2")) { sid = readStr(in); sname = readStr(in); stype = readStr(in); }
            path = readStr(in);
            long size = in.readLong();
            boolean ok = pin == Prefs.pin(this);
            // Doğru kodla bağlanan cihaz bu tarafta da hatırlanır.
            if (ok && sid != null) Peers.seen(this, sid, sname, stype, sock.getInetAddress().getHostAddress(), TCP_PORT, true);

            if (size < 0) { // ping / eşleştirme kontrolü
                out.write(ok ? 'K' : 'P');
                writeStr(out, Prefs.id(this));
                writeStr(out, Prefs.name(this));
                writeStr(out, "mobil");
                out.flush();
                return;
            }
            if (!ok) {
                out.write('P');
                out.flush();
                log("Yanlış kodla gönderim reddedildi: " + sock.getInetAddress().getHostAddress());
                return;
            }
            List<String> parts = safeParts(path);
            if (parts.isEmpty()) return;
            String name = parts.remove(parts.size() - 1);
            String sub = android.text.TextUtils.join("/", parts);
            // Boyut sınırı yok; tek koşul tablette yeterli boş alan olması.
            long free = new android.os.StatFs(Environment.getExternalStorageDirectory().getPath()).getAvailableBytes();
            if (free < size + (64L << 20)) {
                out.write('S');
                out.flush();
                log(String.format(Locale.US, "Yer yok: %s (%.1f MB gerekli, %.1f MB boş)", path, size / 1048576.0, free / 1048576.0));
                return;
            }

            active.incrementAndGet();
            try {
                OutputStream fo;
                if (Build.VERSION.SDK_INT >= 29) {
                    ContentValues v = new ContentValues();
                    v.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
                    v.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/HizliAktar" + (sub.isEmpty() ? "" : "/" + sub));
                    v.put(MediaStore.MediaColumns.IS_PENDING, 1);
                    pendingUri = getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
                    if (pendingUri == null) throw new IOException("Dosya oluşturulamadı");
                    fo = getContentResolver().openOutputStream(pendingUri);
                } else {
                    File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "HizliAktar/" + sub);
                    if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("Klasör oluşturulamadı (depolama izni?)");
                    legacyFile = unique(dir, name);
                    fo = new FileOutputStream(legacyFile);
                }
                out.write('K');
                out.flush();

                byte[] buf = new byte[BUF];
                long left = size;
                try (OutputStream o = new BufferedOutputStream(fo, BUF)) {
                    while (left > 0) {
                        int n = in.read(buf, 0, (int) Math.min(buf.length, left));
                        if (n < 0) throw new EOFException("Bağlantı yarıda kesildi");
                        o.write(buf, 0, n);
                        left -= n;
                    }
                }
                if (pendingUri != null) {
                    ContentValues v = new ContentValues();
                    v.put(MediaStore.MediaColumns.IS_PENDING, 0);
                    getContentResolver().update(pendingUri, v, null, null);
                }
                pendingUri = null;
                legacyFile = null;
                out.write('D');
                out.flush();
                log("Alındı ← " + (sname != null ? sname : sock.getInetAddress().getHostAddress()) + ": " + path + String.format(Locale.US, "  (%.1f MB)", size / 1048576.0));
            } finally {
                active.decrementAndGet();
            }
        } catch (Exception e) {
            log("HATA " + path + ": " + e.getMessage());
            if (pendingUri != null) try { getContentResolver().delete(pendingUri, null, null); } catch (Exception ignored) { }
            if (legacyFile != null) legacyFile.delete();
        }
    }

    private static File unique(File dir, String name) {
        File f = new File(dir, name);
        int dot = name.lastIndexOf('.');
        String base = dot > 0 ? name.substring(0, dot) : name, ext = dot > 0 ? name.substring(dot) : "";
        for (int i = 1; f.exists(); i++) f = new File(dir, base + " (" + i + ")" + ext);
        return f;
    }
}
