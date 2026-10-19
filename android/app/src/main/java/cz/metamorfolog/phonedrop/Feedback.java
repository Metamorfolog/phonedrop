package cz.metamorfolog.phonedrop;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.ImageDecoder;
import android.net.Uri;
import android.os.Build;
import android.util.Base64;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Locale;

/** Zpětná vazba autorovi – stejná služba jako formulář na webu (tickety na /stats/tickets/). */
final class Feedback {
    /** Chyba se stavovým kódem (0 = bez spojení). */
    static final class Failed extends IOException {
        final int code;
        /** Server odmítl přílohu (attachment_type / attachment_size). */
        final boolean attachment;
        Failed(int code) {
            this(code, false);
        }
        Failed(int code, boolean attachment) {
            super("HTTP " + code);
            this.code = code;
            this.attachment = attachment;
        }
    }

    static final int MAX_IMAGES = 3;
    private static final int MAX_BYTES = 2_400_000;   // server bere 2,5 MB
    private static final int MAX_SIDE = 2560;

    /** Snímek k odeslání: zmenšený a překódovaný (nová bitmapa = bez EXIF s polohou a jiných metadat). */
    static final class Image {
        final String name;
        final String type;
        final byte[] data;
        final Bitmap thumb;

        private Image(String name, String type, byte[] data, Bitmap thumb) {
            this.name = name; this.type = type; this.data = data; this.thumb = thumb;
        }

        /** Blokuje (dekódování) – volat mimo hlavní vlákno. */
        static Image from(Context ctx, Uri uri) throws IOException {
            String mime = ctx.getContentResolver().getType(uri);
            ImageDecoder.Source src = ImageDecoder.createSource(ctx.getContentResolver(), uri);
            Bitmap bmp = ImageDecoder.decodeBitmap(src, (decoder, info, s) -> {
                int w = info.getSize().getWidth(), h = info.getSize().getHeight();
                double k = Math.min(1, (double) MAX_SIDE / Math.max(w, h));
                if (k < 1) decoder.setTargetSize(Math.max(1, (int) (w * k)), Math.max(1, (int) (h * k)));
                decoder.setAllocator(ImageDecoder.ALLOCATOR_SOFTWARE);
            });
            // Snímky obrazovky (PNG) zůstanou PNG, když se vejdou; fotky a velké obrázky jako JPEG.
            byte[] out = null;
            String type = "image/jpeg";
            if ("image/png".equals(mime)) {
                out = encode(bmp, Bitmap.CompressFormat.PNG, 100);
                type = "image/png";
                if (out.length > MAX_BYTES) out = null;
            }
            if (out == null) {
                type = "image/jpeg";
                for (int q : new int[]{85, 72, 60}) {
                    out = encode(bmp, Bitmap.CompressFormat.JPEG, q);
                    if (out.length <= MAX_BYTES) break;
                }
                if (out.length > MAX_BYTES) throw new IOException("too big");
            }
            int th = 160;
            Bitmap thumb = Bitmap.createScaledBitmap(bmp, Math.max(1, bmp.getWidth() * th / Math.max(1, bmp.getHeight())), th, true);
            if (thumb != bmp) bmp.recycle();
            String name = "screenshot" + ("image/png".equals(type) ? ".png" : ".jpg");
            return new Image(name, type, out, thumb);
        }

        private static byte[] encode(Bitmap bmp, Bitmap.CompressFormat format, int quality) {
            ByteArrayOutputStream buf = new ByteArrayOutputStream();
            bmp.compress(format, quality, buf);
            return buf.toByteArray();
        }
    }

    private Feedback() {
    }

    /** Pošle zprávu (blokuje – volat mimo hlavní vlákno). Vrací číslo ticketu. */
    static int send(Context ctx, String kind, String message, String email, List<Image> images) throws IOException {
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
            JSONArray attachments = new JSONArray();
            for (Image img : images) {
                attachments.put(new JSONObject().put("name", img.name).put("type", img.type)
                        .put("data", Base64.encodeToString(img.data, Base64.NO_WRAP)));
            }
            body.put("attachments", attachments);
            byte[] bytes = body.toString().getBytes(StandardCharsets.UTF_8);
            c = (HttpURLConnection) new URL(BuildConfig.FEEDBACK_URL).openConnection();
            c.setConnectTimeout(15_000);
            // Se snímky jde i pár MB – na mobilních datech to chvíli trvá.
            c.setReadTimeout(90_000);
            c.setFixedLengthStreamingMode(bytes.length);
            c.setRequestMethod("POST");
            c.setDoOutput(true);
            c.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            try (OutputStream out = c.getOutputStream()) {
                out.write(bytes);
            }
        } catch (IOException e) {
            throw new Failed(0);
        } catch (Exception e) {
            throw new IOException(e);
        }
        try {
            int code = c.getResponseCode();
            if (code / 100 != 2) {
                boolean attachment = false;
                try (InputStream err = c.getErrorStream()) {
                    if (err != null) {
                        java.io.ByteArrayOutputStream e = new java.io.ByteArrayOutputStream();
                        byte[] b = new byte[1024];
                        int n;
                        while ((n = err.read(b)) != -1 && e.size() < 4096) e.write(b, 0, n);
                        attachment = e.toString("UTF-8").contains("\"attachment");
                    }
                } catch (IOException ignored) {
                }
                throw new Failed(code, attachment);
            }
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
