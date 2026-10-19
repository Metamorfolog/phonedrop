package cz.metamorfolog.phonedrop;

import android.content.Context;
import android.os.Build;

import org.json.JSONObject;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Locale;

/** Zpětná vazba autorovi – stejná služba jako formulář na webu (tickety na /stats/tickets/). */
final class Feedback {
    /** Chyba se stavovým kódem (0 = bez spojení). */
    static final class Failed extends IOException {
        final int code;
        Failed(int code) {
            super("HTTP " + code);
            this.code = code;
        }
    }

    private Feedback() {
    }

    /** Pošle zprávu (blokuje – volat mimo hlavní vlákno). Vrací číslo ticketu. */
    static int send(Context ctx, String kind, String message, String email) throws IOException {
        HttpURLConnection c;
        try {
            JSONObject body = new JSONObject()
                    .put("kind", kind)
                    .put("message", message)
                    .put("email", email)
                    .put("source", "app")
                    .put("version", Updater.current(ctx) + " (Android)")
                    .put("os", "Android " + Build.VERSION.RELEASE + " (" + Build.MANUFACTURER + " " + Build.MODEL + ")")
                    .put("lang", Locale.getDefault().getLanguage());
            c = (HttpURLConnection) new URL(BuildConfig.FEEDBACK_URL).openConnection();
            c.setConnectTimeout(15_000);
            c.setReadTimeout(20_000);
            c.setRequestMethod("POST");
            c.setDoOutput(true);
            c.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            try (OutputStream out = c.getOutputStream()) {
                out.write(body.toString().getBytes(StandardCharsets.UTF_8));
            }
        } catch (IOException e) {
            throw new Failed(0);
        } catch (Exception e) {
            throw new IOException(e);
        }
        try {
            int code = c.getResponseCode();
            if (code / 100 != 2) throw new Failed(code);
            try (InputStream in = c.getInputStream()) {
                // readAllBytes() je až od Androidu 13; appka běží od Androidu 10.
                java.io.ByteArrayOutputStream buf = new java.io.ByteArrayOutputStream();
                byte[] chunk = new byte[4096];
                int n;
                while ((n = in.read(chunk)) != -1) buf.write(chunk, 0, n);
                return new JSONObject(buf.toString("UTF-8")).optInt("id", 0);
            } catch (org.json.JSONException e) {
                return 0;
            }
        } catch (Failed f) {
            throw f;
        } catch (IOException e) {
            throw new Failed(0);
        } finally {
            c.disconnect();
        }
    }
}
