package cz.metamorfolog.phonedrop;

import android.app.PendingIntent;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.database.Cursor;
import android.net.Uri;
import android.os.Environment;
import android.provider.MediaStore;
import android.webkit.MimeTypeMap;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.Locale;

/**
 * Co se stane s přijatým souborem – nezávislé na tom, odkud přišel (LAN dnes, relay později).
 * .apk → PackageInstaller session (systém si vyžádá jedno potvrzení), ostatní → Download/Drop.
 */
final class Inbox {
    static final String DIR = Environment.DIRECTORY_DOWNLOADS + "/Drop";

    interface Progress {
        void update(long done, long total);
    }

    private final Context ctx;

    Inbox(Context ctx) {
        this.ctx = ctx.getApplicationContext();
    }

    static boolean isApk(String name) {
        return name.toLowerCase(Locale.ROOT).endsWith(".apk");
    }

    JSONObject accept(String name, long length, InputStream in, Progress progress) throws IOException, JSONException {
        name = safeName(name);
        return isApk(name) ? install(name, length, in, progress) : save(name, length, in, progress);
    }

    private JSONObject save(String name, long length, InputStream in, Progress progress) throws IOException, JSONException {
        ContentResolver cr = ctx.getContentResolver();
        ContentValues v = new ContentValues();
        v.put(MediaStore.Downloads.DISPLAY_NAME, name);
        v.put(MediaStore.Downloads.MIME_TYPE, mimeOf(name));
        v.put(MediaStore.Downloads.RELATIVE_PATH, DIR);
        v.put(MediaStore.Downloads.IS_PENDING, 1);
        Uri uri = cr.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
        if (uri == null) throw new IOException("MediaStore insert selhal");
        boolean ok = false;
        try {
            try (OutputStream out = cr.openOutputStream(uri)) {
                if (out == null) throw new IOException("nelze zapisovat");
                copy(in, out, length, progress);
            }
            v.clear();
            v.put(MediaStore.Downloads.IS_PENDING, 0);
            cr.update(uri, v, null, null);
            ok = true;
        } finally {
            if (!ok) cr.delete(uri, null, null);
        }
        // MediaStore při kolizi přejmenuje na "jméno (1).ext" – vrátíme skutečné jméno.
        String saved = name;
        try (Cursor c = cr.query(uri, new String[]{MediaStore.Downloads.DISPLAY_NAME}, null, null, null)) {
            if (c != null && c.moveToFirst()) saved = c.getString(0);
        }
        Notifier.fileSaved(ctx, saved, uri, mimeOf(saved));
        return new JSONObject().put("ok", true).put("kind", "file").put("path", DIR + "/" + saved);
    }

    private JSONObject install(String name, long length, InputStream in, Progress progress) throws IOException, JSONException {
        PackageInstaller pi = ctx.getPackageManager().getPackageInstaller();
        PackageInstaller.SessionParams params =
                new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        params.setSize(length);
        int id = pi.createSession(params);
        boolean committed = false;
        try (PackageInstaller.Session session = pi.openSession(id)) {
            try (OutputStream out = session.openWrite("base.apk", 0, length)) {
                copy(in, out, length, progress);
                session.fsync(out);
            }
            Intent result = new Intent(ctx, InstallResultReceiver.class)
                    .putExtra(InstallResultReceiver.EXTRA_NAME, name);
            // MUTABLE: systém do intentu doplní stav a případně potvrzovací aktivitu.
            PendingIntent pending = PendingIntent.getBroadcast(ctx, id, result,
                    PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE);
            InstallResultReceiver.setState(id, "pending");
            session.commit(pending.getIntentSender());
            committed = true;
        } finally {
            if (!committed) pi.abandonSession(id);
        }
        return new JSONObject().put("ok", true).put("kind", "apk").put("session", id)
                .put("canInstall", ctx.getPackageManager().canRequestPackageInstalls());
    }

    private static void copy(InputStream in, OutputStream out, long length, Progress progress) throws IOException {
        byte[] buf = new byte[64 * 1024];
        long done = 0;
        int n;
        while ((n = in.read(buf)) != -1) {
            out.write(buf, 0, n);
            done += n;
            if (progress != null) progress.update(done, length);
        }
        if (done != length) throw new IOException("přenos přerušen (" + done + " z " + length + " B)");
    }

    static String safeName(String name) {
        if (name == null) name = "";
        int slash = Math.max(name.lastIndexOf('/'), name.lastIndexOf('\\'));
        name = name.substring(slash + 1).replaceAll("[\\x00-\\x1f:*?\"<>|]", "_").trim();
        while (name.startsWith(".")) name = name.substring(1);
        if (name.isEmpty()) name = "soubor";
        return name.length() > 200 ? name.substring(name.length() - 200) : name;
    }

    static String mimeOf(String name) {
        int dot = name.lastIndexOf('.');
        String mime = dot >= 0
                ? MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase(Locale.ROOT))
                : null;
        return mime != null ? mime : "application/octet-stream";
    }
}
