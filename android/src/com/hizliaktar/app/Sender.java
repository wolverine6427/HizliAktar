package com.hizliaktar.app;

import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;

import java.io.*;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.atomic.AtomicLong;

/** Tabletten/telefondan başka bir cihaza (PC dahil) gönderim. Protokol: ReceiverService açıklamasına bakın. */
final class Sender {
    static final class PinException extends IOException { PinException() { super("Kod yanlış"); } }

    static final class Item {
        final Uri uri; String name = "dosya"; long size = -1;
        Item(Uri u) { uri = u; }
    }

    /** Dosyanın adını ve boyutunu ContentResolver'dan okur. */
    static Item describe(Context c, Uri u) {
        Item it = new Item(u);
        try (Cursor cur = c.getContentResolver().query(u, new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE}, null, null, null)) {
            if (cur != null && cur.moveToFirst()) {
                if (!cur.isNull(0)) it.name = cur.getString(0);
                if (!cur.isNull(1)) it.size = cur.getLong(1);
            }
        } catch (Exception ignored) { }
        if (it.size < 0) {
            try (android.content.res.AssetFileDescriptor fd = c.getContentResolver().openAssetFileDescriptor(u, "r")) {
                if (fd != null) it.size = fd.getLength();
            } catch (Exception ignored) { }
        }
        return it;
    }

    private static Socket connect(String ip, int port) throws IOException {
        Socket s = new Socket();
        s.setSendBufferSize(4 << 20);
        s.setTcpNoDelay(true);
        s.connect(new InetSocketAddress(ip, port), 3000);
        s.setSoTimeout(60000);
        return s;
    }

    private static void header(Context c, DataOutputStream o, int pin, String path, long size) throws IOException {
        o.write("HA2".getBytes(StandardCharsets.US_ASCII));
        o.writeInt(pin);
        ReceiverService.writeStr(o, Prefs.id(c));
        ReceiverService.writeStr(o, Prefs.name(c));
        ReceiverService.writeStr(o, "mobil");
        ReceiverService.writeStr(o, path);
        o.writeLong(size);
        o.flush();
    }

    /** Kod doğruysa true. Kod yanlış olsa da karşı cihazın kimliğini döndürür: {id, ad, tür}. */
    static boolean ping(Context c, String ip, int port, int pin, String[] info) throws IOException {
        try (Socket s = connect(ip, port)) {
            DataOutputStream o = new DataOutputStream(new BufferedOutputStream(s.getOutputStream()));
            header(c, o, pin, "", -1);
            DataInputStream in = new DataInputStream(new BufferedInputStream(s.getInputStream()));
            int st = in.read();
            if (st < 0) throw new EOFException("Karşı cihaz bağlantıyı kapattı");
            info[0] = ReceiverService.readStr(in);
            info[1] = ReceiverService.readStr(in);
            try { info[2] = ReceiverService.readStr(in); } catch (EOFException e) { info[2] = "mobil"; }
            return st == 'K';
        }
    }

    static void send(Context c, Peers.Peer p, int pin, Item it, AtomicLong progress) throws IOException {
        if (it.size < 0) throw new IOException("Dosya boyutu okunamadı");
        try (Socket s = connect(p.ip, p.port);
             InputStream fi = c.getContentResolver().openInputStream(it.uri)) {
            if (fi == null) throw new IOException("Dosya açılamadı");
            DataOutputStream o = new DataOutputStream(new BufferedOutputStream(s.getOutputStream(), ReceiverService.BUF));
            header(c, o, pin, it.name, it.size);
            InputStream in = s.getInputStream();
            int st = in.read();
            if (st == 'P') throw new PinException();
            if (st == 'S') throw new IOException("Karşı cihazda yeterli boş alan yok");
            if (st != 'K') throw new IOException("Karşı cihaz dosyayı reddetti");
            byte[] buf = new byte[ReceiverService.BUF];
            long left = it.size;
            while (left > 0) {
                int n = fi.read(buf, 0, (int) Math.min(buf.length, left));
                if (n < 0) throw new EOFException("Dosya beklenenden kısa");
                o.write(buf, 0, n);
                left -= n;
                progress.addAndGet(n);
            }
            o.flush();
            s.setSoTimeout(180000);
            if (in.read() != 'D') throw new IOException("Karşı cihaz kaydı onaylamadı");
        }
    }
}
