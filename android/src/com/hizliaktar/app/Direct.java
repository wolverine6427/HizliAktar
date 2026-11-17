package com.hizliaktar.app;

import android.content.Context;
import android.net.wifi.p2p.WifiP2pConfig;
import android.net.wifi.p2p.WifiP2pManager;
import android.os.Build;
import android.os.Looper;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;

/**
 * Wi-Fi Direct ile modemsiz, doğrudan bağlantı (Huawei Share / Quick Share'in kullandığı yöntem).
 * Veri modemden iki kez geçmek yerine tek atlamada, mümkünse 5 GHz'te gider.
 * Ağ adı ve şifresi eşleşme kodundan türetilir: aynı kodu kullanan cihazlar onaysız katılır.
 * Bağlantı kurulduktan sonra normal Hızlı Aktar protokolü (TCP 47800) aynen çalışır.
 */
final class Direct {
    /** Android'de Wi-Fi Direct grup sahibinin adresi her zaman budur. */
    static final String OWNER_IP = "192.168.49.1";

    interface Done { void result(String error); }

    private static WifiP2pManager mgr;
    private static WifiP2pManager.Channel ch;

    static boolean supported() { return Build.VERSION.SDK_INT >= 29; }

    private static String hash(String s) {
        try {
            byte[] d = MessageDigest.getInstance("SHA-256").digest(s.getBytes(StandardCharsets.UTF_8));
            StringBuilder b = new StringBuilder();
            for (byte x : d) b.append(String.format("%02x", x));
            return b.toString();
        } catch (Exception e) { throw new RuntimeException(e); }
    }

    static String ssid(Context c) { return "DIRECT-HA-HizliAktar-" + hash("ssid" + Prefs.pin(c)).substring(0, 6); }
    static String pass(Context c) { return hash("pass" + Prefs.pin(c)).substring(0, 12); }

    private static synchronized boolean init(Context c) {
        if (mgr == null) {
            mgr = (WifiP2pManager) c.getApplicationContext().getSystemService(Context.WIFI_P2P_SERVICE);
            if (mgr != null) ch = mgr.initialize(c.getApplicationContext(), Looper.getMainLooper(), null);
        }
        return mgr != null && ch != null;
    }

    private static WifiP2pConfig config(Context c, int band) {
        return new WifiP2pConfig.Builder().setNetworkName(ssid(c)).setPassphrase(pass(c)).setGroupOperatingBand(band).build();
    }

    private static String reason(int r) {
        switch (r) {
            case WifiP2pManager.P2P_UNSUPPORTED: return "Bu cihaz Wi-Fi Direct desteklemiyor";
            case WifiP2pManager.BUSY: return "Wi-Fi Direct meşgul. Wi-Fi'yi kapatıp açın, tekrar deneyin";
            default: return "Wi-Fi Direct hatası (" + r + "). Wi-Fi ve Konum açık mı, izin verildi mi?";
        }
    }

    private static WifiP2pManager.ActionListener listener(Done d) {
        return new WifiP2pManager.ActionListener() {
            @Override public void onSuccess() { d.result(null); }
            @Override public void onFailure(int r) { d.result(reason(r)); }
        };
    }

    /** Bu cihazda bağlantıyı açar (önce 5 GHz, olmazsa otomatik bant). */
    @SuppressWarnings("MissingPermission")
    static void host(Context c, Done d) {
        if (!init(c)) { d.result(reason(WifiP2pManager.P2P_UNSUPPORTED)); return; }
        Runnable create = () -> mgr.createGroup(ch, config(c, WifiP2pConfig.GROUP_OWNER_BAND_5GHZ), new WifiP2pManager.ActionListener() {
            @Override public void onSuccess() { d.result(null); }
            @Override public void onFailure(int r) { mgr.createGroup(ch, config(c, WifiP2pConfig.GROUP_OWNER_BAND_AUTO), listener(d)); }
        });
        // Eski bir grup açık kalmışsa önce kapat.
        mgr.removeGroup(ch, new WifiP2pManager.ActionListener() {
            @Override public void onSuccess() { create.run(); }
            @Override public void onFailure(int r) { create.run(); }
        });
    }

    /** Aynı kodla açılmış bağlantıya onaysız katılır. */
    @SuppressWarnings("MissingPermission")
    static void join(Context c, Done d) {
        if (!init(c)) { d.result(reason(WifiP2pManager.P2P_UNSUPPORTED)); return; }
        mgr.connect(ch, config(c, WifiP2pConfig.GROUP_OWNER_BAND_AUTO), listener(d));
    }

    static void close(Context c, Done d) {
        if (!init(c)) { d.result(null); return; }
        mgr.removeGroup(ch, listener(d));
    }
}
