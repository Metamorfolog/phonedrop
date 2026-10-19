package cz.metamorfolog.phonedrop;

import android.util.Log;

import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Minimální HTTP/1.1 server: jeden požadavek na spojení, tělo se čte proudem (i velké soubory). */
final class HttpServer {
    private static final String TAG = "PhoneDrop";

    static final class Request {
        String method;
        String path;
        final Map<String, String> query = new HashMap<>();
        final Map<String, String> headers = new HashMap<>();
        long contentLength = -1;
        InputStream body;
        String remote;
        OutputStream out;
        private boolean continued;

        String header(String name) { return headers.get(name.toLowerCase(Locale.ROOT)); }

        /**
         * Klient s "Expect: 100-continue" čeká s tělem, až ho server přijme – voláme po kontrole
         * oprávnění a místa. Když handler vrátí chybu dřív, tělo se vůbec neposílá.
         */
        void proceed() throws IOException {
            if (continued || !"100-continue".equalsIgnoreCase(header("expect"))) return;
            continued = true;
            out.write("HTTP/1.1 100 Continue\r\n\r\n".getBytes(StandardCharsets.US_ASCII));
            out.flush();
        }
    }

    static final class Response {
        final int code;
        final JSONObject json;
        Response(int code, JSONObject json) { this.code = code; this.json = json; }
    }

    interface Handler {
        Response handle(Request req) throws Exception;
    }

    private final int port;
    private final Handler handler;
    private final ExecutorService pool = Executors.newCachedThreadPool();
    private volatile ServerSocket server;

    HttpServer(int port, Handler handler) {
        this.port = port;
        this.handler = handler;
    }

    void start() throws IOException {
        ServerSocket s = new ServerSocket();
        s.setReuseAddress(true);
        s.bind(new InetSocketAddress(port));
        server = s;
        Thread t = new Thread(this::acceptLoop, "phonedrop-accept");
        t.setDaemon(true);
        t.start();
    }

    void stop() {
        try {
            if (server != null) server.close();
        } catch (IOException ignored) {
        }
        pool.shutdownNow();
    }

    private void acceptLoop() {
        ServerSocket s = server;
        while (!s.isClosed()) {
            try {
                Socket c = s.accept();
                pool.execute(() -> serve(c));
            } catch (IOException e) {
                if (!s.isClosed()) Log.w(TAG, "accept", e);
            }
        }
    }

    private void serve(Socket c) {
        try (Socket sock = c) {
            sock.setSoTimeout(60_000);
            InputStream in = new BufferedInputStream(sock.getInputStream(), 64 * 1024);
            OutputStream out = sock.getOutputStream();
            Request req;
            try {
                req = parse(in);
            } catch (IOException e) {
                write(out, new Response(400, error("bad request")));
                return;
            }
            req.remote = sock.getInetAddress().getHostAddress();
            req.out = out;
            Response res;
            try {
                res = handler.handle(req);
            } catch (Exception e) {
                Log.w(TAG, "handler " + req.path, e);
                res = new Response(500, error(String.valueOf(e.getMessage())));
            }
            write(out, res);
        } catch (IOException e) {
            Log.w(TAG, "serve", e);
        }
    }

    static JSONObject error(String msg) {
        JSONObject o = new JSONObject();
        try {
            o.put("ok", false).put("error", msg);
        } catch (Exception ignored) {
        }
        return o;
    }

    private static Request parse(InputStream in) throws IOException {
        Request r = new Request();
        String line = readLine(in);
        if (line == null) throw new IOException("empty");
        String[] parts = line.split(" ");
        if (parts.length < 2) throw new IOException("request line");
        r.method = parts[0].toUpperCase(Locale.ROOT);
        String target = parts[1];
        int q = target.indexOf('?');
        r.path = q >= 0 ? target.substring(0, q) : target;
        if (q >= 0) {
            for (String kv : target.substring(q + 1).split("&")) {
                if (kv.isEmpty()) continue;
                int eq = kv.indexOf('=');
                String k = eq >= 0 ? kv.substring(0, eq) : kv;
                String v = eq >= 0 ? kv.substring(eq + 1) : "";
                r.query.put(URLDecoder.decode(k, "UTF-8"), URLDecoder.decode(v, "UTF-8"));
            }
        }
        int count = 0;
        while ((line = readLine(in)) != null && !line.isEmpty()) {
            if (++count > 100) throw new IOException("too many headers");
            int colon = line.indexOf(':');
            if (colon <= 0) continue;
            r.headers.put(line.substring(0, colon).trim().toLowerCase(Locale.ROOT), line.substring(colon + 1).trim());
        }
        String len = r.header("content-length");
        if (len != null) {
            try {
                r.contentLength = Long.parseLong(len);
            } catch (NumberFormatException e) {
                throw new IOException("content-length");
            }
        }
        r.body = new LimitedInputStream(in, Math.max(0, r.contentLength));
        return r;
    }

    private static String readLine(InputStream in) throws IOException {
        ByteArrayOutputStream buf = new ByteArrayOutputStream();
        int b;
        while ((b = in.read()) != -1) {
            if (b == '\n') break;
            if (b != '\r') buf.write(b);
            if (buf.size() > 16 * 1024) throw new IOException("line too long");
        }
        if (b == -1 && buf.size() == 0) return null;
        return buf.toString("UTF-8");
    }

    private static void write(OutputStream out, Response res) throws IOException {
        byte[] body = res.json.toString().getBytes(StandardCharsets.UTF_8);
        String head = "HTTP/1.1 " + res.code + " " + reason(res.code) + "\r\n"
                + "Content-Type: application/json; charset=utf-8\r\n"
                + "Content-Length: " + body.length + "\r\n"
                + "Connection: close\r\n\r\n";
        out.write(head.getBytes(StandardCharsets.US_ASCII));
        out.write(body);
        out.flush();
    }

    private static String reason(int code) {
        switch (code) {
            case 200: return "OK";
            case 400: return "Bad Request";
            case 401: return "Unauthorized";
            case 403: return "Forbidden";
            case 404: return "Not Found";
            case 411: return "Length Required";
            case 507: return "Insufficient Storage";
            default: return "Error";
        }
    }

    /** Tělo požadavku omezené na Content-Length. */
    static final class LimitedInputStream extends InputStream {
        private final InputStream in;
        private long left;

        LimitedInputStream(InputStream in, long length) {
            this.in = in;
            this.left = length;
        }

        @Override public int read() throws IOException {
            if (left <= 0) return -1;
            int b = in.read();
            if (b >= 0) left--;
            return b;
        }

        @Override public int read(byte[] b, int off, int len) throws IOException {
            if (left <= 0) return -1;
            int n = in.read(b, off, (int) Math.min(len, left));
            if (n > 0) left -= n;
            return n;
        }
    }
}
