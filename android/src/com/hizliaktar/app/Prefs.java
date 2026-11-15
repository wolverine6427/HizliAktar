package com.hizliaktar.app;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import java.security.SecureRandom;
import java.util.UUID;

/** Cihaz kimliği, adı ve eşleştirme PIN'i (ilk açılışta üretilir, kalıcıdır). */
final class Prefs {
    private static SharedPreferences sp;

    static synchronized SharedPreferences get(Context c) {
        if (sp == null) sp = c.getApplicationContext().getSharedPreferences("ha", Context.MODE_PRIVATE);
        return sp;
    }

    static String id(Context c) {
        SharedPreferences p = get(c);
        String id = p.getString("id", null);
        if (id == null) {
            id = UUID.randomUUID().toString().replace("-", "").substring(0, 12);
            p.edit().putString("id", id).apply();
        }
        return id;
    }

    static int pin(Context c) {
        SharedPreferences p = get(c);
        int pin = p.getInt("pin", 0);
        if (pin == 0) {
            pin = 1000 + new SecureRandom().nextInt(9000);
            p.edit().putInt("pin", pin).apply();
        }
        return pin;
    }

    /** Kullanıcı kodu kendisi belirleyebilir; tüm cihazlara aynı kod yazılırsa PC bir kez sorar. */
    static boolean setPin(Context c, String s) {
        s = s.trim();
        if (!s.matches("[0-9]{4,9}") || Integer.parseInt(s) == 0) return false;
        get(c).edit().putInt("pin", Integer.parseInt(s)).apply();
        return true;
    }

    static String name(Context c) {
        String def = Build.MANUFACTURER + " " + Build.MODEL;
        return get(c).getString("name", def);
    }

    static void setName(Context c, String name) {
        get(c).edit().putString("name", name.replace("|", " ").trim()).apply();
    }
}
