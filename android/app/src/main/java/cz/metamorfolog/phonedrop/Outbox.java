package cz.metamorfolog.phonedrop;

import android.content.ContentResolver;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.SystemClock;
import android.provider.OpenableColumns;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * Soubory sdílené z telefonu do počítače. Telefon je server, takže si je počítač sám vyzvedne:
 * drží otevřený dotaz {@code GET /outbox?wait=25} (na počítači tak není potřeba otevírat port ve firewallu),
 * stáhne {@code GET /outbox/file?id=…} a potvrdí {@code POST /outbox/done?id=…}.
 * Sdílený obsah se nejdřív zkopíruje k appce (oprávnění ke cizímu URI po sdílení zaniká); nevyzvednuté
 * soubory se po 3 dnech smažou.
 */
final class Outbox {
    private static final String TAG = "PhoneDrop";
    private static final long KEEP_MS = 3L * 24 * 60 * 60 * 1000;
    /** Počítač, který se ozval (dlouhým dotazem) před méně než touto dobou, je „připojený“. */
    private static final long ONLINE_MS = 45_000;

    static final class Item {
        final String id;
        final String name;
        final long size;
        /** Klíč počítače (hash tokenu); null = kterýkoli spárovaný. */
        final String target;
        final long created;
        Item(String id, String name, long size, String target, long created) {
            this.id = id; this.name = name; this.size = size; this.target = target; this.created = created;
        }
    }

    private static Outbox instance;

    static synchronized Outbox get(Context ctx) {
        if (instance == null) instance = new Outbox(ctx.getApplicationContext());
        return instance;
    }

    private final Context ctx;
    private final File dir;
    private final List<Item> items = new ArrayList<>();
    /** Klíč počítače → kdy se naposledy ptal (elapsedRealtime). */
    private final Map<String, Long> seen = new HashMap<>();
    /** Klíč počítače → název (z párování). */
    private final Map<String, String> names = new HashMap<>();
    private int preparing;

    private Outbox(Context ctx) {
        this.ctx = ctx;
        dir = new File(ctx.getFilesDir(), "outbox");
        //noinspection ResultOfMethodCallIgnored
        dir.mkdirs();
        load();
    }

    // ---- stav ----

    synchronized int pending() { return items.size() + preparing; }

    synchronized boolean preparing() { return preparing > 0; }

    /** Počítače, které teď čekají na soubory (klíč → název). */
    synchronized Map<String, String> online() {
        Map<String, String> out = new HashMap<>();
        long now = SystemClock.elapsedRealtime();
        for (Map.Entry<String, Long> e : seen.entrySet()) {
            if (now - e.getValue() < ONLINE_MS) out.put(e.getKey(), names.getOrDefault(e.getKey(), "PC"));
        }
        return out;
    }

    synchronized void touch(String key, String name) {
        seen.put(key, SystemClock.elapsedRealtime());
        names.put(key, name);
    }

    synchronized void gone(String key) {
        seen.remove(key);
    }

    // ---- přidání ----

    /** Zkopíruje sdílené soubory k appce (blokuje). Vrací počet zařazených; chyby hlásí přes {@code failed}. */
    int add(List<Uri> uris, String target, List<String> failed) {
        synchronized (this) { preparing += uris.size(); }
        Notifier.outboxChanged(ctx);
        int added = 0;
        ContentResolver cr = ctx.getContentResolver();
        for (Uri uri : uris) {
            String name = Inbox.safeName(displayName(cr, uri));
            String id = UUID.randomUUID().toString().replace("-", "").substring(0, 16);
            File file = new File(dir, id);
            try (InputStream in = cr.openInputStream(uri); OutputStream out = new FileOutputStream(file)) {
                if (in == null) throw new IOException("no stream");
                long expected = size(cr, uri);
                if (expected > 0 && dir.getUsableSpace() < expected + 50L * 1024 * 1024) throw new IOException("no space");
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
            } catch (IOException | SecurityException e) {
                Log.w(TAG, "outbox add " + uri, e);
                //noinspection ResultOfMethodCallIgnored
                file.delete();
                failed.add(name);
                synchronized (this) { preparing--; }
                continue;
            }
            synchronized (this) {
                preparing--;
                items.add(new Item(id, name, file.length(), target, System.currentTimeMillis()));
                save();
                notifyAll();
            }
            added++;
        }
        Notifier.outboxChanged(ctx);
        return added;
    }

    private static String displayName(ContentResolver cr, Uri uri) {
        if ("file".equals(uri.getScheme())) return uri.getLastPathSegment();
        try (Cursor c = cr.query(uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null)) {
            if (c != null && c.moveToFirst() && !c.isNull(0)) return c.getString(0);
        } catch (RuntimeException ignored) {
        }
        String last = uri.getLastPathSegment();
        return last != null ? last : "shared";
    }

    private static long size(ContentResolver cr, Uri uri) {
        try (Cursor c = cr.query(uri, new String[]{OpenableColumns.SIZE}, null, null, null)) {
            if (c != null && c.moveToFirst() && !c.isNull(0)) return c.getLong(0);
        } catch (RuntimeException ignored) {
        }
        return -1;
    }

    // ---- vyzvednutí ----

    /** Čeká až {@code waitMs}, než je pro počítač něco připravené; vrací seznam (může být prázdný). */
    synchronized List<Item> waitFor(String key, long waitMs) throws InterruptedException {
        long end = SystemClock.elapsedRealtime() + waitMs;
        while (true) {
            List<Item> mine = forClient(key);
            long left = end - SystemClock.elapsedRealtime();
            if (!mine.isEmpty() || left <= 0) return mine;
            wait(Math.min(left, 5_000));
            seen.put(key, SystemClock.elapsedRealtime());
        }
    }

    private List<Item> forClient(String key) {
        List<Item> out = new ArrayList<>();
        for (Item i : items) if (i.target == null || i.target.equals(key)) out.add(i);
        return out;
    }

    synchronized Item find(String key, String id) {
        for (Item i : items) if (i.id.equals(id) && (i.target == null || i.target.equals(key))) return i;
        return null;
    }

    File file(Item i) { return new File(dir, i.id); }

    /** Počítač soubor uložil: smazat a ohlásit. */
    void done(String key, String id) {
        Item found;
        synchronized (this) {
            found = find(key, id);
            if (found == null) return;
            items.remove(found);
            save();
        }
        //noinspection ResultOfMethodCallIgnored
        file(found).delete();
        Notifier.outboxSent(ctx, found.name, names.getOrDefault(key, "PC"));
        Notifier.outboxChanged(ctx);
    }

    /** Smaže vše, co čeká (z oznámení „Zrušit“). */
    void clear() {
        synchronized (this) {
            for (Item i : items) {
                //noinspection ResultOfMethodCallIgnored
                file(i).delete();
            }
            items.clear();
            save();
        }
        Notifier.outboxChanged(ctx);
    }

    // ---- uložení seznamu ----

    private void load() {
        File index = new File(dir, "index.json");
        long now = System.currentTimeMillis();
        try {
            if (index.exists()) {
                JSONArray a = new JSONArray(new String(Files.readAllBytes(index.toPath()), StandardCharsets.UTF_8));
                for (int k = 0; k < a.length(); k++) {
                    JSONObject o = a.getJSONObject(k);
                    Item i = new Item(o.getString("id"), o.getString("name"), o.getLong("size"),
                            o.isNull("target") ? null : o.optString("target", null), o.getLong("created"));
                    if (now - i.created < KEEP_MS && file(i).isFile()) items.add(i);
                }
            }
        } catch (Exception e) {
            Log.w(TAG, "outbox load", e);
        }
        // Úklid: soubory, které nejsou v seznamu (přerušené kopírování, prošlé).
        File[] files = dir.listFiles();
        if (files != null) {
            for (File f : files) {
                if (f.getName().equals("index.json")) continue;
                boolean known = false;
                for (Item i : items) if (i.id.equals(f.getName())) { known = true; break; }
                //noinspection ResultOfMethodCallIgnored
                if (!known) f.delete();
            }
        }
        save();
    }

    private void save() {
        try {
            JSONArray a = new JSONArray();
            for (Iterator<Item> it = items.iterator(); it.hasNext(); ) {
                Item i = it.next();
                a.put(new JSONObject().put("id", i.id).put("name", i.name).put("size", i.size)
                        .put("target", i.target == null ? JSONObject.NULL : i.target).put("created", i.created));
            }
            File tmp = new File(dir, "index.json.tmp");
            Files.write(tmp.toPath(), a.toString().getBytes(StandardCharsets.UTF_8));
            //noinspection ResultOfMethodCallIgnored
            tmp.renameTo(new File(dir, "index.json"));
        } catch (Exception e) {
            Log.w(TAG, "outbox save", e);
        }
    }

    /** Pro oznámení: názvy čekajících souborů. */
    synchronized List<String> names() {
        List<String> out = new ArrayList<>();
        for (Item i : items) out.add(i.name);
        return out;
    }
}
