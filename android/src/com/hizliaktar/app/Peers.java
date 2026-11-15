package com.hizliaktar.app;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONObject;
import java.util.*;

/** Görülen ve hatırlanan cihazlar. Hatırlananlar SharedPreferences'ta ("known") saklanır. */
final class Peers {
    static final class Peer {
        String id, name = "", nick = "", ip = "", type = "mobil";
        int port = ReceiverService.TCP_PORT, pin;   // pin 0 = bilinmiyor
        boolean known;
        long lastSeen;

        boolean online() { return System.currentTimeMillis() - lastSeen < 6000; }
        String label() { return nick.isEmpty() ? name : nick + " (" + name + ")"; }
        @Override public String toString() {
            return ("pc".equals(type) ? "💻  " : "📱  ") + label() + (online() ? "     " + ip : "     — çevrimdışı");
        }
    }

    private static final Map<String, Peer> all = new HashMap<>();
    private static boolean loaded;

    private static void load(Context c) {
        if (loaded) return;
        loaded = true;
        try {
            JSONArray a = new JSONArray(Prefs.get(c).getString("known", "[]"));
            for (int i = 0; i < a.length(); i++) {
                JSONObject o = a.getJSONObject(i);
                Peer p = new Peer();
                p.id = o.getString("id"); p.name = o.optString("name"); p.nick = o.optString("nick");
                p.ip = o.optString("ip"); p.type = o.optString("type", "mobil");
                p.port = o.optInt("port", ReceiverService.TCP_PORT); p.pin = o.optInt("pin");
                p.known = true;
                all.put(p.id, p);
            }
        } catch (Exception ignored) { }
    }

    private static void save(Context c) {
        JSONArray a = new JSONArray();
        try {
            for (Peer p : all.values()) {
                if (!p.known) continue;
                a.put(new JSONObject().put("id", p.id).put("name", p.name).put("nick", p.nick).put("ip", p.ip)
                        .put("type", p.type).put("port", p.port).put("pin", p.pin));
            }
        } catch (Exception ignored) { }
        Prefs.get(c).edit().putString("known", a.toString()).apply();
    }

    /** Keşif yayını ya da gelen bağlantıyla cihaz görüldü. remember: doğru kodla bağlandı. */
    static synchronized Peer seen(Context c, String id, String name, String type, String ip, int port, boolean remember) {
        load(c);
        if (id.equals(Prefs.id(c))) return null;
        Peer p = all.get(id);
        if (p == null) { p = new Peer(); p.id = id; all.put(id, p); }
        boolean changed = !name.equals(p.name) || !ip.equals(p.ip) || port != p.port || !type.equals(p.type) || (remember && !p.known);
        p.name = name; p.ip = ip; p.port = port; p.type = type; p.lastSeen = System.currentTimeMillis();
        p.known |= remember;
        if (changed && p.known) save(c);
        return p;
    }

    static synchronized void setPin(Context c, Peer p, int pin) { load(c); p.pin = pin; if (pin > 0) p.known = true; save(c); }
    static synchronized void setNick(Context c, Peer p, String nick) { load(c); p.nick = nick.trim(); p.known = true; save(c); }
    static synchronized void forget(Context c, Peer p) { load(c); all.remove(p.id); save(c); }

    static synchronized List<Integer> knownPins(Context c) {
        load(c);
        LinkedHashSet<Integer> s = new LinkedHashSet<>();
        s.add(Prefs.pin(c));
        for (Peer p : all.values()) if (p.pin > 0) s.add(p.pin);
        return new ArrayList<>(s);
    }

    /** Çevrimiçi olanlar önce; çevrimdışı olup hatırlanmayanlar listelenmez. */
    static synchronized List<Peer> list(Context c) {
        load(c);
        List<Peer> l = new ArrayList<>();
        for (Peer p : all.values()) if (p.online() || p.known) l.add(p);
        Collections.sort(l, (a, b) -> a.online() != b.online() ? (a.online() ? -1 : 1) : a.label().compareToIgnoreCase(b.label()));
        return l;
    }
}
