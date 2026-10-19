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
import android.os.StatFs;
import android.provider.MediaStore;
import android.webkit.MimeTypeMap;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/**
 * Co se stane s přijatým souborem – nezávislé na tom, odkud přišel (LAN dnes, relay později).
 * .apk → PackageInstaller session (systém si vyžádá jedno potvrzení),
 * .apks / .xapk (split APK) → všechna APK z archivu do jedné session,
 * ostatní → Download/Drop (u složek se zachovanou strukturou podsložek).
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

    /** Balík split APK: .apks (SAI, bundletool) nebo .xapk (APKPure). */
    static boolean isBundle(String name) {
        String n = name.toLowerCase(Locale.ROOT);
        return n.endsWith(".apks") || n.endsWith(".xapk");
    }

    private static boolean installs(String name) {
        return isApk(name) || isBundle(name);
    }

    /** Vrátí chybu pro počítač, když se soubor do telefonu nevejde (APK potřebuje místo i pro instalaci). */
    JSONObject checkSpace(String name, long length) {
        File dir = installs(name) ? Environment.getDataDirectory() : Environment.getExternalStorageDirectory();
        long free = new StatFs(dir.getPath()).getAvailableBytes();
        long need = (installs(name) ? 2 * length : length) + 16L * 1024 * 1024;
        if (free >= need) return null;
        JSONObject e = HttpServer.error("no_space", String.format(Locale.ROOT,
                "not enough space on the phone: need %d MB, free %d MB", need / 1048576, free / 1048576));
        try {
            e.put("needMb", need / 1048576).put("freeMb", free / 1048576);
        } catch (JSONException ignored) {
        }
        return e;
    }

    JSONObject accept(String name, long length, InputStream in, Progress progress) throws IOException, JSONException {
        return accept(name, null, length, in, progress);
    }

    /** dir: podsložka uvnitř Download/Drop (u poslané složky), nebo null. */
    JSONObject accept(String name, String dir, long length, InputStream in, Progress progress) throws IOException, JSONException {
        name = safeName(name);
        if (isApk(name)) return install(name, length, in, progress);
        if (isBundle(name)) return installBundle(name, length, in, progress);
        return save(name, safeDir(dir), length, in, progress);
    }

    private JSONObject save(String name, String dir, long length, InputStream in, Progress progress) throws IOException, JSONException {
        String relative = dir.isEmpty() ? DIR : DIR + "/" + dir;
        ContentResolver cr = ctx.getContentResolver();
        ContentValues v = new ContentValues();
        v.put(MediaStore.Downloads.DISPLAY_NAME, name);
        v.put(MediaStore.Downloads.MIME_TYPE, mimeOf(name));
        v.put(MediaStore.Downloads.RELATIVE_PATH, relative);
        v.put(MediaStore.Downloads.IS_PENDING, 1);
        Uri uri = cr.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v);
        if (uri == null) throw new IOException("MediaStore insert failed");
        boolean ok = false;
        try {
            try (OutputStream out = cr.openOutputStream(uri)) {
                if (out == null) throw new IOException("cannot write");
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
        return new JSONObject().put("ok", true).put("kind", "file").put("path", relative + "/" + saved);
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
            commit(session, id, name);
            committed = true;
        } finally {
            if (!committed) pi.abandonSession(id);
        }
        return installed(id);
    }

    /**
     * Split APK: archiv se čte proudem rovnou ze sítě, každé .apk z něj jde do téže session.
     * Vynechá se „standalones/“ z bundletoolu (samostatné varianty pro staré Androidy) a data OBB,
     * která by stejně nešla zapsat (Android 11+ nepustí aplikace do cizích složek obb).
     */
    private JSONObject installBundle(String name, long length, InputStream raw, Progress progress) throws IOException, JSONException {
        CountingStream counted = new CountingStream(raw, length, progress);
        PackageInstaller pi = ctx.getPackageManager().getPackageInstaller();
        PackageInstaller.SessionParams params =
                new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        int id = pi.createSession(params);
        boolean committed = false;
        boolean obb = false;
        List<String> splits = new ArrayList<>();
        try (PackageInstaller.Session session = pi.openSession(id)) {
            ZipInputStream zip = new ZipInputStream(counted);
            ZipEntry e;
            byte[] buf = new byte[64 * 1024];
            while ((e = zip.getNextEntry()) != null) {
                String path = e.getName().replace('\\', '/');
                String lower = path.toLowerCase(Locale.ROOT);
                if (lower.endsWith(".obb")) obb = true;
                if (e.isDirectory() || !lower.endsWith(".apk") || lower.startsWith("standalones/")) continue;
                String entry = "split" + splits.size() + "_" + safeName(path.substring(path.lastIndexOf('/') + 1));
                try (OutputStream out = session.openWrite(entry, 0, e.getSize() >= 0 ? e.getSize() : -1)) {
                    int n;
                    while ((n = zip.read(buf)) != -1) out.write(buf, 0, n);
                    session.fsync(out);
                }
                splits.add(entry);
            }
            // Dočíst zbytek těla (konec archivu), ať je jisté, že dorazil celý.
            byte[] rest = new byte[16 * 1024];
            while (counted.read(rest) != -1) { /* drain */ }
            if (counted.done != length) throw new IOException("transfer interrupted (" + counted.done + " of " + length + " B)");
            if (splits.isEmpty()) throw new IOException("no APK inside " + name);
            commit(session, id, name);
            committed = true;
        } finally {
            if (!committed) pi.abandonSession(id);
        }
        return installed(id).put("splits", splits.size()).put("obbSkipped", obb);
    }

    private void commit(PackageInstaller.Session session, int id, String name) {
        Intent result = new Intent(ctx, InstallResultReceiver.class)
                .putExtra(InstallResultReceiver.EXTRA_NAME, name);
        // MUTABLE: systém do intentu doplní stav a případně potvrzovací aktivitu.
        PendingIntent pending = PendingIntent.getBroadcast(ctx, id, result,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_MUTABLE);
        InstallResultReceiver.setState(id, "pending");
        session.commit(pending.getIntentSender());
    }

    private JSONObject installed(int id) throws JSONException {
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
        if (done != length) throw new IOException("transfer interrupted (" + done + " of " + length + " B)");
    }

    /** Počítá přečtené bajty (u archivu nevidíme, kolik z těla už ZipInputStream spotřeboval). */
    private static final class CountingStream extends FilterInputStream {
        long done;
        private final long length;
        private final Progress progress;

        CountingStream(InputStream in, long length, Progress progress) {
            super(in);
            this.length = length;
            this.progress = progress;
        }

        @Override public int read() throws IOException {
            int b = super.read();
            if (b >= 0) count(1);
            return b;
        }

        @Override public int read(byte[] b, int off, int len) throws IOException {
            int n = super.read(b, off, len);
            if (n > 0) count(n);
            return n;
        }

        private void count(int n) {
            done += n;
            if (progress != null) progress.update(done, length);
        }
    }

    static String safeName(String name) {
        if (name == null) name = "";
        int slash = Math.max(name.lastIndexOf('/'), name.lastIndexOf('\\'));
        name = name.substring(slash + 1).replaceAll("[\\x00-\\x1f:*?\"<>|]", "_").trim();
        while (name.startsWith(".")) name = name.substring(1);
        if (name.isEmpty()) name = "file";
        return name.length() > 200 ? name.substring(name.length() - 200) : name;
    }

    /** Podsložka z počítače: jen bezpečné části (žádné „..“, absolutní cesty ani řídicí znaky), nejvýš 12 úrovní. */
    static String safeDir(String dir) {
        if (dir == null) return "";
        StringBuilder out = new StringBuilder();
        int depth = 0;
        for (String part : dir.split("[/\\\\]")) {
            String p = part.replaceAll("[\\x00-\\x1f:*?\"<>|]", "_").trim();
            while (p.startsWith(".")) p = p.substring(1);
            if (p.isEmpty()) continue;
            if (p.length() > 100) p = p.substring(0, 100);
            if (++depth > 12) break;
            if (out.length() > 0) out.append('/');
            out.append(p);
        }
        return out.toString();
    }

    static String mimeOf(String name) {
        int dot = name.lastIndexOf('.');
        String mime = dot >= 0
                ? MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase(Locale.ROOT))
                : null;
        return mime != null ? mime : "application/octet-stream";
    }
}
