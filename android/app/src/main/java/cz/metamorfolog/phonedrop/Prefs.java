package cz.metamorfolog.phonedrop;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;

/**
 * Stav přijímače: ID zařízení, jednorázový párovací PIN a tokeny spárovaných počítačů.
 * Tokeny se ukládají jen jako SHA-256 ("hash|název počítače"), samotný token zná jen počítač.
 */
final class Prefs {
    static final int PORT = 8765;
    private static final int MAX_PIN_FAILURES = 5;
    private static final SecureRandom RANDOM = new SecureRandom();

    private final SharedPreferences sp;

    Prefs(Context ctx) {
        sp = ctx.getSharedPreferences("phonedrop", Context.MODE_PRIVATE);
    }

    /** Stálé ID telefonu – podle něj ho počítač najde i po změně IP (a jednou i relay). */
    synchronized String deviceId() {
        String id = sp.getString("id", null);
        if (id == null) {
            id = UUID.randomUUID().toString();
            sp.edit().putString("id", id).apply();
        }
        return id;
    }

    String deviceName() {
        String name = Build.MODEL;
        return name == null || name.isEmpty() ? "Android" : name;
    }

    boolean enabled() { return sp.getBoolean("enabled", true); }

    /** Vzhled appky: "system", "light" nebo "dark". */
    String theme() { return sp.getString("theme", "system"); }
    void setTheme(String theme) { sp.edit().putString("theme", theme).commit(); }
    void setEnabled(boolean on) { sp.edit().putBoolean("enabled", on).apply(); }

    synchronized String pin() {
        String pin = sp.getString("pin", null);
        if (pin == null) pin = newPin();
        return pin;
    }

    private String newPin() {
        String pin = String.format(Locale.ROOT, "%06d", RANDOM.nextInt(1_000_000));
        sp.edit().putString("pin", pin).putInt("pinFailures", 0).apply();
        return pin;
    }

    /** Ověří PIN; při úspěchu vydá nový token a PIN se změní. Po 5 chybách se PIN také změní. */
    synchronized String pair(String pin, String client) {
        if (pin == null || !MessageDigest.isEqual(utf8(pin), utf8(pin()))) {
            int fails = sp.getInt("pinFailures", 0) + 1;
            if (fails >= MAX_PIN_FAILURES) newPin();
            else sp.edit().putInt("pinFailures", fails).apply();
            return null;
        }
        byte[] raw = new byte[32];
        RANDOM.nextBytes(raw);
        String token = hex(raw);
        Set<String> tokens = new HashSet<>(sp.getStringSet("tokens", new HashSet<>()));
        tokens.add(sha256(token) + "|" + clean(client));
        sp.edit().putStringSet("tokens", tokens).apply();
        newPin();
        return token;
    }

    /** Vrátí název spárovaného počítače, nebo null, když token neplatí. */
    synchronized String clientFor(String token) {
        if (token == null || token.isEmpty()) return null;
        byte[] hash = utf8(sha256(token));
        for (String entry : sp.getStringSet("tokens", new HashSet<>())) {
            int bar = entry.indexOf('|');
            if (bar > 0 && MessageDigest.isEqual(utf8(entry.substring(0, bar)), hash)) {
                return entry.substring(bar + 1);
            }
        }
        return null;
    }

    synchronized List<String> pairedClients() {
        List<String> out = new ArrayList<>();
        for (String entry : sp.getStringSet("tokens", new HashSet<>())) {
            out.add(entry.substring(entry.indexOf('|') + 1));
        }
        return out;
    }

    synchronized void unpairAll() {
        sp.edit().remove("tokens").apply();
        newPin();
    }

    private static String clean(String client) {
        if (client == null || client.trim().isEmpty()) return "PC";
        String c = client.replace('|', ' ').trim();
        return c.length() > 64 ? c.substring(0, 64) : c;
    }

    private static byte[] utf8(String s) { return s.getBytes(StandardCharsets.UTF_8); }

    static String sha256(String s) {
        try {
            return hex(MessageDigest.getInstance("SHA-256").digest(utf8(s)));
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    private static String hex(byte[] b) {
        StringBuilder sb = new StringBuilder(b.length * 2);
        for (byte x : b) sb.append(Character.forDigit((x >> 4) & 0xf, 16)).append(Character.forDigit(x & 0xf, 16));
        return sb.toString();
    }
}
