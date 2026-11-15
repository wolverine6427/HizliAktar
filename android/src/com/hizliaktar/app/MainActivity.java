package com.hizliaktar.app;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.widget.*;

public class MainActivity extends Activity {
    private TextView statusView, logView;
    private final Handler h = new Handler(Looper.getMainLooper());
    private final Runnable tick = new Runnable() {
        @Override public void run() {
            int n = ReceiverService.active.get();
            statusView.setText(ReceiverService.status + "   •   IP: " + ReceiverService.localIp()
                    + (n > 0 ? "   •   Alınıyor: " + n + " dosya" : "")
                    + "\nKayıt yeri: İndirilenler / HizliAktar");
            logView.setText(ReceiverService.logText());
            h.postDelayed(this, 500);
        }
    };

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        int pad = (int) (24 * getResources().getDisplayMetrics().density);

        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.VERTICAL);
        l.setPadding(pad, pad, pad, pad);

        TextView title = new TextView(this);
        title.setText("Hızlı Aktar");
        title.setTextSize(30);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        l.addView(title);

        TextView nameLbl = new TextView(this);
        nameLbl.setText("\nCihaz adı (PC listesinde bu ad görünür):");
        l.addView(nameLbl);

        LinearLayout row = new LinearLayout(this);
        final EditText nameEdit = new EditText(this);
        nameEdit.setSingleLine(true);
        nameEdit.setText(Prefs.name(this));
        row.addView(nameEdit, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        Button save = new Button(this);
        save.setText("Kaydet");
        save.setOnClickListener(v -> {
            Prefs.setName(this, nameEdit.getText().toString());
            Toast.makeText(this, "Ad kaydedildi", Toast.LENGTH_SHORT).show();
        });
        row.addView(save);
        l.addView(row);

        TextView pinLbl = new TextView(this);
        pinLbl.setText("\nEşleşme kodu (4-9 rakam). PC ilk gönderimde bir kez sorar ve hatırlar.\n"
                + "Tüm tablet ve telefonlarınıza aynı kodu yazarsanız PC hiçbirinde tekrar sormaz:");
        l.addView(pinLbl);

        LinearLayout pinRow = new LinearLayout(this);
        pinRow.setGravity(Gravity.CENTER_VERTICAL);
        final EditText pinEdit = new EditText(this);
        pinEdit.setText(String.valueOf(Prefs.pin(this)));
        pinEdit.setInputType(android.text.InputType.TYPE_CLASS_NUMBER);
        pinEdit.setTextSize(48);
        pinEdit.setTypeface(Typeface.DEFAULT_BOLD);
        pinEdit.setTextColor(Color.rgb(25, 90, 200));
        pinEdit.setGravity(Gravity.CENTER_HORIZONTAL);
        pinRow.addView(pinEdit, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        Button pinSave = new Button(this);
        pinSave.setText("Kodu kaydet");
        pinSave.setOnClickListener(v -> Toast.makeText(this,
                Prefs.setPin(this, pinEdit.getText().toString()) ? "Kod kaydedildi" : "Kod 4-9 rakamdan oluşmalı (0000 olamaz)",
                Toast.LENGTH_SHORT).show());
        pinRow.addView(pinSave);
        l.addView(pinRow);

        statusView = new TextView(this);
        statusView.setTextSize(16);
        l.addView(statusView);

        Button stop = new Button(this);
        stop.setText("Alıcıyı durdur ve çık");
        stop.setOnClickListener(v -> {
            stopService(new Intent(this, ReceiverService.class));
            finish();
        });
        l.addView(stop);

        TextView logLbl = new TextView(this);
        logLbl.setText("\nAlınan dosyalar:");
        l.addView(logLbl);

        ScrollView sv = new ScrollView(this);
        logView = new TextView(this);
        logView.setTypeface(Typeface.MONOSPACE);
        logView.setTextIsSelectable(true);
        sv.addView(logView);
        l.addView(sv, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1));

        TextView about = new TextView(this);
        about.setText("Hızlı Aktar " + BuildInfo.VERSION + "  •  Yazar: " + BuildInfo.AUTHOR);
        about.setGravity(Gravity.CENTER_HORIZONTAL);
        about.setTextColor(Color.GRAY);
        l.addView(about);

        setContentView(l);
        // Uygulama açıkken ekran kilitlenmesin / kararmasın.
        getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        if (Build.VERSION.SDK_INT < 29 && checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(new String[]{Manifest.permission.WRITE_EXTERNAL_STORAGE}, 2);

        Intent svc = new Intent(this, ReceiverService.class);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(svc); else startService(svc);
    }

    @Override protected void onResume() { super.onResume(); h.post(tick); }
    @Override protected void onPause() { super.onPause(); h.removeCallbacks(tick); }
}
