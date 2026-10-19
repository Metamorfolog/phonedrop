package cz.metamorfolog.phonedrop;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Aktualizace appky z GitHub Releases: najde poslední vydání, stáhne APK, ověří SHA-256 podle SHA256SUMS.txt
 * a předá ho systémovému instalátoru (jedno ťuknutí). Android navíc pustí jen APK se stejným podpisem.
 * Adresa je v BuildConfig.UPDATE_URL (pro testy jde přepsat při sestavení: -PupdateUrl=…).
 */
final class Updater {
    private static final String TAG = "PhoneDrop";
    private static final long AUTO_CHECK_MS = 6 * 60 * 60 * 1000L;

    enum State { IDLE, CHECKING, UP_TO_DATE, AVAILABLE, DOWNLOADING, INSTALLING, FAILED }

    static volatile State state = State.IDLE;
    static volatile String latest;       // např. "1.2.0"
    static volatile int progress;        // 0–100 při stahování
    static volatile Runnable onChange;
    private static volatile String apkUrl, apkName, sumsUrl;
    private static final ExecutorService EXEC = Executors.newSingleThreadExecutor();

    private Updater() {
    }

    static String current(Context ctx) {
        try {
            return ctx.getPackageManager().getPackageInfo(ctx.getPackageName(), 0).versionName;
        } catch (Exception e) {
            return "0";
        }
    }

    private static void set(State s) {
        state = s;
        Runnable r = onChange;
        if (r != null) r.run();
    }

    /** Kontrola nejvýš jednou za 6 h (při otevření appky / ze služby); ruční kontrola vždy. */
    static void check(Context ctx, boolean manual) {
        Context app = ctx.getApplicationContext();
        SharedPreferences sp = app.getSharedPreferences("phonedrop", Context.MODE_PRIVATE);
        long now = System.currentTimeMillis();
        if (!manual && now - sp.getLong("updateCheckedAt", 0) < AUTO_CHECK_MS) return;
        if (state == State.CHECKING || state == State.DOWNLOADING || state == State.INSTALLING) return;
        set(State.CHECKING);
        EXEC.execute(() -> {
            try {
                JSONObject release = new JSONObject(new String(get(BuildConfig.UPDATE_URL), StandardCharsets.UTF_8));
                sp.edit().putLong("updateCheckedAt", now).apply();
                String tag = release.getString("tag_name").replaceFirst("^[vV]", "");
                if (compare(tag, current(app)) <= 0) {
                    set(State.UP_TO_DATE);
                    return;
                }
                apkUrl = null;
                sumsUrl = null;
                JSONArray assets = release.getJSONArray("assets");
                for (int i = 0; i < assets.length(); i++) {
                    JSONObject a = assets.getJSONObject(i);
                    String name = a.getString("name");
                    if (name.toLowerCase(Locale.ROOT).endsWith(".apk")) {
                        apkName = name;
                        apkUrl = a.getString("browser_download_url");
                    } else if (name.equalsIgnoreCase("SHA256SUMS.txt")) {
                        sumsUrl = a.getString("browser_download_url");
                    }
                }
                if (apkUrl == null) {
                    set(State.UP_TO_DATE);
                    return;
                }
                latest = tag;
                if (!manual && !tag.equals(sp.getString("updateNotified", ""))) {
                    sp.edit().putString("updateNotified", tag).apply();
                    Notifier.updateAvailable(app, tag);
                }
                set(State.AVAILABLE);
            } catch (Exception e) {
                // Nejde ověřit (offline, soukromé repo, limit API) – nehlásit jako „nejnovější verze“.
                Log.w(TAG, "update check", e);
                set(manual ? State.FAILED : State.IDLE);
            }
        });
    }

    /** Stáhne nové APK, ověří kontrolní součet a spustí systémovou instalaci. */
    static void install(Context ctx) {
        Context app = ctx.getApplicationContext();
        if (state != State.AVAILABLE && state != State.FAILED || apkUrl == null) return;
        progress = 0;
        set(State.DOWNLOADING);
        EXEC.execute(() -> {
            File file = new File(app.getCacheDir(), "update.apk");
            try {
                if (sumsUrl == null) throw new IOException("no SHA256SUMS.txt");
                String expected = null;
                for (String line : new String(get(sumsUrl), StandardCharsets.UTF_8).split("\n")) {
                    String[] p = line.trim().split("\\s+\\*?", 2);
                    if (p.length == 2 && p[1].trim().equals(apkName)) expected = p[0];
                }
                if (expected == null) throw new IOException("checksum missing for " + apkName);

                MessageDigest sha = MessageDigest.getInstance("SHA-256");
                HttpURLConnection c = open(apkUrl);
                long total = c.getContentLengthLong();
                try (InputStream in = c.getInputStream(); OutputStream out = new FileOutputStream(file)) {
                    byte[] buf = new byte[64 * 1024];
                    long done = 0;
                    int n;
                    while ((n = in.read(buf)) != -1) {
                        out.write(buf, 0, n);
                        sha.update(buf, 0, n);
                        done += n;
                        int pct = total > 0 ? (int) (done * 100 / total) : 0;
                        if (pct != progress) {
                            progress = pct;
                            set(State.DOWNLOADING);
                        }
                    }
                } finally {
                    c.disconnect();
                }
                if (!hex(sha.digest()).equalsIgnoreCase(expected)) throw new IOException("checksum mismatch");

                set(State.INSTALLING);
                try (InputStream in = new FileInputStream(file)) {
                    new Inbox(app).accept(apkName, file.length(), in, null);
                }
            } catch (Exception e) {
                Log.w(TAG, "update install", e);
                set(State.FAILED);
            } finally {
                //noinspection ResultOfMethodCallIgnored
                file.delete();
            }
        });
    }

    private static HttpURLConnection open(String url) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(15_000);
        c.setReadTimeout(30_000);
        c.setRequestProperty("Accept", "application/vnd.github+json");
        c.setRequestProperty("User-Agent", "PhoneDrop-Android");
        if (c.getResponseCode() / 100 != 2) {
            int code = c.getResponseCode();
            c.disconnect();
            throw new IOException("HTTP " + code);
        }
        return c;
    }

    private static byte[] get(String url) throws IOException {
        HttpURLConnection c = open(url);
        try (InputStream in = c.getInputStream()) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[16 * 1024];
            int n;
            while ((n = in.read(buf)) != -1) out.write(buf, 0, n);
            return out.toByteArray();
        } finally {
            c.disconnect();
        }
    }

    /** Porovná verze "1.2.0" a "1.10" po číslech. */
    static int compare(String a, String b) {
        String[] x = a.split("\\."), y = b.split("\\.");
        for (int i = 0; i < Math.max(x.length, y.length); i++) {
            int p = i < x.length ? num(x[i]) : 0, q = i < y.length ? num(y[i]) : 0;
            if (p != q) return Integer.compare(p, q);
        }
        return 0;
    }

    private static int num(String s) {
        try {
            return Integer.parseInt(s.replaceAll("[^0-9].*$", ""));
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    private static String hex(byte[] b) {
        StringBuilder sb = new StringBuilder(b.length * 2);
        for (byte x : b) sb.append(Character.forDigit((x >> 4) & 0xf, 16)).append(Character.forDigit(x & 0xf, 16));
        return sb.toString();
    }
}
