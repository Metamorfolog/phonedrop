package cz.metamorfolog.phonedrop;

import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.nsd.NsdManager;
import android.net.nsd.NsdServiceInfo;
import android.net.wifi.WifiManager;
import android.os.IBinder;
import android.os.PowerManager;
import android.os.SystemClock;
import android.util.Log;

import org.json.JSONObject;

import java.io.IOException;
import java.net.Inet4Address;

/**
 * Popředí služba s HTTP serverem v LAN a ohlášením přes mDNS (_phonedrop._tcp).
 * Protokol je popsaný v PROTOCOL.md v kořeni repa.
 */
public class ReceiverService extends Service {
    private static final String TAG = "PhoneDrop";
    static final String SERVICE_TYPE = "_phonedrop._tcp";
    /** Ze sdílení: zařadit soubory (ClipData) pro počítač {@link #EXTRA_TARGET} (null = kterýkoli). */
    static final String ACTION_SHARE = "cz.metamorfolog.phonedrop.SHARE";
    static final String ACTION_OUTBOX_CLEAR = "cz.metamorfolog.phonedrop.OUTBOX_CLEAR";
    static final String EXTRA_TARGET = "target";

    /** Pro MainActivity: překreslit stav (párování, přijatý soubor). */
    static volatile Runnable onChange;
    static volatile boolean running;

    private Prefs prefs;
    private Inbox inbox;
    private HttpServer server;
    private NsdManager nsd;
    private NsdManager.RegistrationListener nsdListener;
    private ConnectivityManager.NetworkCallback netCallback;

    /** Spustí přijímač; když to systém z pozadí nedovolí (Android 12+), naběhne po otevření appky. */
    static void start(Context ctx) {
        try {
            ctx.startForegroundService(new Intent(ctx, ReceiverService.class));
        } catch (RuntimeException e) {
            Log.w(TAG, "start not allowed now", e);
        }
    }

    static void stop(Context ctx) {
        ctx.stopService(new Intent(ctx, ReceiverService.class));
    }

    @Override
    public void onCreate() {
        super.onCreate();
        prefs = new Prefs(this);
        inbox = new Inbox(this);
        Notifier.channels(this);
        if (android.os.Build.VERSION.SDK_INT >= 34) {
            startForeground(Notifier.ID_SERVICE, Notifier.service(this, address()),
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            // Typ „specialUse“ je až od Androidu 14; dřív stačí obyčejná služba v popředí.
            startForeground(Notifier.ID_SERVICE, Notifier.service(this, address()));
        }
        server = new HttpServer(Prefs.PORT, this::handle);
        try {
            server.start();
        } catch (IOException e) {
            Log.e(TAG, "server start", e);
            stopSelf();
            return;
        }
        running = true;
        registerNsd();
        netCallback = new ConnectivityManager.NetworkCallback() {
            @Override public void onLinkPropertiesChanged(Network network, LinkProperties lp) { refresh(); }
            @Override public void onLost(Network network) { refresh(); }
        };
        getSystemService(ConnectivityManager.class).registerDefaultNetworkCallback(netCallback);
        changed();
        updateTick.run();
    }

    /** Kontrola aktualizací i bez otevření appky (Updater sám hlídá, aby to bylo nejvýš jednou za 6 h). */
    private final android.os.Handler handler = new android.os.Handler(android.os.Looper.getMainLooper());
    private final Runnable updateTick = new Runnable() {
        @Override public void run() {
            Updater.check(ReceiverService.this, false);
            handler.postDelayed(this, 60 * 60 * 1000L);
        }
    };

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent != null ? intent.getAction() : null;
        if (ACTION_SHARE.equals(action)) {
            // Spuštěno přes startForegroundService – i běžící služba musí znovu potvrdit popředí.
            if (android.os.Build.VERSION.SDK_INT >= 34) {
                startForeground(Notifier.ID_SERVICE, Notifier.service(this, address()),
                        ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
            } else {
                startForeground(Notifier.ID_SERVICE, Notifier.service(this, address()));
            }
            java.util.List<android.net.Uri> uris = new java.util.ArrayList<>();
            android.content.ClipData clip = intent.getClipData();
            if (clip != null) for (int i = 0; i < clip.getItemCount(); i++) {
                android.net.Uri u = clip.getItemAt(i).getUri();
                if (u != null) uris.add(u);
            }
            String target = intent.getStringExtra(EXTRA_TARGET);
            // Kopírování může trvat (video); oprávnění ke čtení drží služba, dokud běží.
            new Thread(() -> {
                java.util.List<String> failed = new java.util.ArrayList<>();
                Outbox.get(this).add(uris, target, failed);
                if (!failed.isEmpty()) handler.post(() -> android.widget.Toast.makeText(this,
                        getString(R.string.share_failed, String.join(", ", failed)), android.widget.Toast.LENGTH_LONG).show());
                handler.post(this::stopIfIdle);
            }, "phonedrop-share").start();
        } else if (ACTION_OUTBOX_CLEAR.equals(action)) {
            Outbox.get(this).clear();
            stopIfIdle();
        }
        return START_STICKY;
    }

    /** Spuštěno jen kvůli odeslání do počítače (příjem vypnutý) a fronta je prázdná → skončit. */
    private void stopIfIdle() {
        if (!prefs.enabled() && Outbox.get(this).pending() == 0) stopSelf();
    }

    @Override
    public void onDestroy() {
        running = false;
        handler.removeCallbacks(updateTick);
        if (netCallback != null) {
            try {
                getSystemService(ConnectivityManager.class).unregisterNetworkCallback(netCallback);
            } catch (RuntimeException ignored) {
            }
        }
        if (nsd != null && nsdListener != null) {
            try {
                nsd.unregisterService(nsdListener);
            } catch (RuntimeException ignored) {
            }
        }
        if (server != null) server.stop();
        changed();
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    private void refresh() {
        getSystemService(android.app.NotificationManager.class)
                .notify(Notifier.ID_SERVICE, Notifier.service(this, address()));
        changed();
    }

    private static void changed() {
        Runnable r = onChange;
        if (r != null) r.run();
    }

    private void registerNsd() {
        NsdServiceInfo info = new NsdServiceInfo();
        info.setServiceName("PhoneDrop " + prefs.deviceName());
        info.setServiceType(SERVICE_TYPE);
        info.setPort(Prefs.PORT);
        info.setAttribute("id", prefs.deviceId());
        info.setAttribute("v", "1");
        nsdListener = new NsdManager.RegistrationListener() {
            @Override public void onServiceRegistered(NsdServiceInfo i) { Log.i(TAG, "mDNS " + i.getServiceName()); }
            @Override public void onRegistrationFailed(NsdServiceInfo i, int err) { Log.w(TAG, "mDNS failed " + err); }
            @Override public void onServiceUnregistered(NsdServiceInfo i) { }
            @Override public void onUnregistrationFailed(NsdServiceInfo i, int err) { }
        };
        nsd = getSystemService(NsdManager.class);
        nsd.registerService(info, NsdManager.PROTOCOL_DNS_SD, nsdListener);
    }

    /** IPv4 adresa aktivní sítě (Wi-Fi) – zobrazí se pro ruční zadání v počítači. */
    String address() {
        return address(this);
    }

    static String address(Context ctx) {
        ConnectivityManager cm = ctx.getSystemService(ConnectivityManager.class);
        Network n = cm.getActiveNetwork();
        LinkProperties lp = n != null ? cm.getLinkProperties(n) : null;
        if (lp != null) {
            for (LinkAddress a : lp.getLinkAddresses()) {
                if (a.getAddress() instanceof Inet4Address) {
                    return a.getAddress().getHostAddress() + ":" + Prefs.PORT;
                }
            }
        }
        return ctx.getString(R.string.no_network);
    }

    // ---- HTTP ----

    private HttpServer.Response handle(HttpServer.Request req) throws Exception {
        String route = req.method + " " + req.path;
        switch (route) {
            case "GET /info":
                return ok(new JSONObject().put("ok", true).put("app", "PhoneDrop").put("v", 1)
                        .put("id", prefs.deviceId()).put("name", prefs.deviceName()));
            case "POST /pair": {
                if (!Prefs.pairingOpen()) {
                    return new HttpServer.Response(403, HttpServer.error("pairing_closed", "open PhoneDrop on the phone to pair"));
                }
                long wait = prefs.pairLockSeconds();
                if (wait > 0) {
                    return new HttpServer.Response(429, HttpServer.error("pairing_locked", "too many wrong PINs")
                            .put("retryAfter", wait));
                }
                String token = prefs.pair(req.query.get("pin"), req.query.get("client"));
                refresh();
                if (token == null) return new HttpServer.Response(403, HttpServer.error("bad_pin", "wrong PIN"));
                return ok(new JSONObject().put("ok", true).put("token", token)
                        .put("id", prefs.deviceId()).put("name", prefs.deviceName()));
            }
            case "GET /ping":
                if (client(req) == null) return unauthorized();
                return ok(new JSONObject().put("ok", true).put("id", prefs.deviceId()).put("name", prefs.deviceName()));
            case "PUT /upload":
                return upload(req);
            case "GET /outbox":
                return outbox(req);
            case "GET /outbox/file":
                return outboxFile(req);
            case "POST /outbox/done": {
                String key = clientKey(req);
                if (key == null) return unauthorized();
                Outbox.get(this).done(key, req.query.getOrDefault("id", ""));
                handler.post(this::stopIfIdle);
                return ok(new JSONObject().put("ok", true));
            }
            case "GET /install": {
                if (client(req) == null) return unauthorized();
                int session = Integer.parseInt(req.query.getOrDefault("session", "-1"));
                return ok(new JSONObject().put("ok", true).put("state", InstallResultReceiver.state(session)));
            }
            default:
                return new HttpServer.Response(404, HttpServer.error("not_found", "not found"));
        }
    }

    private HttpServer.Response upload(HttpServer.Request req) throws Exception {
        String client = client(req);
        if (client == null) return unauthorized();
        if (req.contentLength < 0) return new HttpServer.Response(411, HttpServer.error("no_length", "Content-Length required"));
        String name = Inbox.safeName(req.query.get("name"));
        JSONObject noSpace = inbox.checkSpace(name, req.contentLength);
        if (noSpace != null) return new HttpServer.Response(507, noSpace);
        req.proceed();
        Log.i(TAG, "upload " + name + " " + req.contentLength + " B od " + client + " (" + req.remote + ")");

        PowerManager.WakeLock wake = getSystemService(PowerManager.class)
                .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "phonedrop:upload");
        WifiManager.WifiLock wifi = getSystemService(WifiManager.class)
                .createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "phonedrop:upload");
        wake.acquire(60 * 60 * 1000L);
        wifi.acquire();
        long[] last = {0};
        try {
            JSONObject res = inbox.accept(name, req.query.get("dir"), req.contentLength, req.body, (done, total) -> {
                long now = SystemClock.elapsedRealtime();
                if (now - last[0] > 500 || done == total) {
                    last[0] = now;
                    Notifier.progress(this, name, done, total);
                }
            });
            changed();
            return ok(res);
        } finally {
            Notifier.progressDone(this);
            wifi.release();
            wake.release();
        }
    }

    /** Dlouhý dotaz počítače: vrátí soubory, které pro něj telefon má, nebo po {@code wait} s prázdný seznam. */
    private HttpServer.Response outbox(HttpServer.Request req) throws Exception {
        String key = clientKey(req);
        if (key == null) return unauthorized();
        String name = client(req);
        Outbox box = Outbox.get(this);
        boolean wasOnline = box.online().containsKey(key);
        box.touch(key, name);
        if (!wasOnline) Notifier.outboxChanged(this);
        long wait;
        try {
            wait = Math.max(0, Math.min(30, Long.parseLong(req.query.getOrDefault("wait", "0"))));
        } catch (NumberFormatException e) {
            wait = 0;
        }
        java.util.List<Outbox.Item> items;
        try {
            items = box.waitFor(key, wait * 1000);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            items = new java.util.ArrayList<>();
        }
        org.json.JSONArray a = new org.json.JSONArray();
        for (Outbox.Item i : items) a.put(new JSONObject().put("id", i.id).put("name", i.name).put("size", i.size));
        return ok(new JSONObject().put("ok", true).put("items", a));
    }

    private HttpServer.Response outboxFile(HttpServer.Request req) {
        String key = clientKey(req);
        if (key == null) return unauthorized();
        Outbox box = Outbox.get(this);
        Outbox.Item item = box.find(key, req.query.getOrDefault("id", ""));
        if (item == null) return new HttpServer.Response(404, HttpServer.error("not_found", "not found"));
        String pc = client(req);
        long[] last = {0};
        return HttpServer.Response.file(box.file(item), (done, total) -> {
            long now = SystemClock.elapsedRealtime();
            if (now - last[0] > 500 || done == total) {
                last[0] = now;
                Notifier.outboxProgress(this, item.name, pc, done, total);
            }
        });
    }

    private String clientKey(HttpServer.Request req) {
        String auth = req.header("authorization");
        if (auth == null || !auth.startsWith("Bearer ")) return null;
        return prefs.keyFor(auth.substring(7).trim());
    }

    private String client(HttpServer.Request req) {
        String auth = req.header("authorization");
        if (auth == null || !auth.startsWith("Bearer ")) return null;
        return prefs.clientFor(auth.substring(7).trim());
    }

    private static HttpServer.Response ok(JSONObject o) {
        return new HttpServer.Response(200, o);
    }

    private static HttpServer.Response unauthorized() {
        return new HttpServer.Response(401, HttpServer.error("unpaired", "not paired"));
    }
}
