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
        startForeground(Notifier.ID_SERVICE, Notifier.service(this, address()),
                ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
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
        return START_STICKY;
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
            JSONObject res = inbox.accept(name, req.contentLength, req.body, (done, total) -> {
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
