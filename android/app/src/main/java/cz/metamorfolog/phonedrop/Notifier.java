package cz.metamorfolog.phonedrop;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;

/** Všechna oznámení na jednom místě. */
final class Notifier {
    static final int ID_SERVICE = 1;
    private static final int ID_PROGRESS = 2;
    private static final int ID_UPDATE = 3;
    private static final int ID_SAVED = 4;
    private static final int ID_OUTBOX = 5;
    private static final int ID_SENT = 6;
    private static final String CH_SERVICE = "service";
    private static final String CH_FILES = "files";
    private static final String CH_INSTALL = "install";

    private Notifier() {
    }

    static void channels(Context ctx) {
        NotificationManager nm = ctx.getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel(CH_SERVICE, ctx.getString(R.string.ch_service), NotificationManager.IMPORTANCE_MIN));
        nm.createNotificationChannel(new NotificationChannel(CH_FILES, ctx.getString(R.string.ch_files), NotificationManager.IMPORTANCE_DEFAULT));
        nm.createNotificationChannel(new NotificationChannel(CH_INSTALL, ctx.getString(R.string.ch_install), NotificationManager.IMPORTANCE_HIGH));
    }

    private static PendingIntent openApp(Context ctx) {
        return PendingIntent.getActivity(ctx, 0, new Intent(ctx, MainActivity.class),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
    }

    /** Trvalé oznámení služby – bez PINu: párovat jde jen v otevřené appce. */
    static Notification service(Context ctx, String address) {
        return new Notification.Builder(ctx, CH_SERVICE)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(ctx.getString(R.string.n_waiting))
                .setContentText(address)
                .setContentIntent(openApp(ctx))
                .setOngoing(true)
                .setShowWhen(false)
                .build();
    }

    static void progress(Context ctx, String name, long done, long total) {
        int pct = total > 0 ? (int) (done * 100 / total) : 0;
        Notification n = new Notification.Builder(ctx, CH_SERVICE)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(ctx.getString(R.string.n_receiving, name))
                .setContentText(pct + " %")
                .setProgress(100, pct, total <= 0)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .build();
        nm(ctx).notify(ID_PROGRESS, n);
    }

    static void progressDone(Context ctx) {
        nm(ctx).cancel(ID_PROGRESS);
    }

    private static int burst;
    private static long lastSaved;

    /**
     * Jedno oznámení na sérii souborů (třeba celou složku): jeden soubor ho otevře,
     * víc souborů za sebou se sečte („Uloženo 12 souborů“) a ťuknutí otevře Stažené.
     */
    static synchronized void fileSaved(Context ctx, String name, Uri uri, String mime) {
        long now = android.os.SystemClock.elapsedRealtime();
        burst = now - lastSaved < 15_000 ? burst + 1 : 1;
        lastSaved = now;
        Intent view = burst > 1
                ? new Intent(android.app.DownloadManager.ACTION_VIEW_DOWNLOADS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                : new Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime)
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        PendingIntent open = PendingIntent.getActivity(ctx, ID_SAVED, view,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification n = new Notification.Builder(ctx, CH_FILES)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(burst > 1
                        ? ctx.getResources().getQuantityString(R.plurals.n_saved_many, burst, burst)
                        : ctx.getString(R.string.n_saved, name))
                .setContentText(burst > 1 ? name : ctx.getString(R.string.n_saved_where))
                .setContentIntent(open)
                .setOnlyAlertOnce(burst > 1)
                .setAutoCancel(true)
                .build();
        nm(ctx).notify(ID_SAVED, n);
    }

    /** Ťuknutí otevře PhoneDrop, který v popředí spustí systémové potvrzení instalace. */
    static void installConfirm(Context ctx, int session, String name) {
        PendingIntent pi = openApp(ctx);
        Notification n = new Notification.Builder(ctx, CH_INSTALL)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(ctx.getString(R.string.n_install, name))
                .setContentText(ctx.getString(R.string.n_install_text))
                .setContentIntent(pi)
                .setCategory(Notification.CATEGORY_RECOMMENDATION)
                .setGroup("install-" + session)
                .setAutoCancel(true)
                .build();
        nm(ctx).notify(installId(session), n);
    }

    /** Nová verze appky – ťuknutí otevře PhoneDrop, kde je tlačítko Aktualizovat. */
    static void updateAvailable(Context ctx, String version) {
        Notification n = new Notification.Builder(ctx, CH_FILES)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(ctx.getString(R.string.n_update, version))
                .setContentText(ctx.getString(R.string.n_update_text))
                .setContentIntent(openApp(ctx))
                .setAutoCancel(true)
                .build();
        nm(ctx).notify(ID_UPDATE, n);
    }

    static void installDone(Context ctx, int session, String title, String text) {
        Notification n = new Notification.Builder(ctx, CH_FILES)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(title)
                .setContentText(text)
                .setGroup("install-" + session)
                .setAutoCancel(true)
                .build();
        nm(ctx).notify(installId(session), n);
    }

    static void cancelInstall(Context ctx, int session) {
        nm(ctx).cancel(installId(session));
    }

    private static int installId(int session) {
        return 1000 + (session & 0xffff);
    }

    // ---- telefon → počítač ----

    /** Stav fronty k odeslání: připravuje se / čeká na počítač; prázdná fronta oznámení zruší. */
    static void outboxChanged(Context ctx) {
        Outbox box = Outbox.get(ctx);
        int n = box.pending();
        if (n == 0) {
            nm(ctx).cancel(ID_OUTBOX);
            return;
        }
        java.util.Map<String, String> online = box.online();
        PendingIntent cancel = PendingIntent.getService(ctx, ID_OUTBOX,
                new Intent(ctx, ReceiverService.class).setAction(ReceiverService.ACTION_OUTBOX_CLEAR),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        String title = box.preparing() ? ctx.getString(R.string.n_outbox_preparing)
                : ctx.getResources().getQuantityString(R.plurals.n_outbox_waiting, n, n);
        String text = online.isEmpty() ? ctx.getString(R.string.n_outbox_waiting_text)
                : ctx.getString(R.string.n_outbox_online, online.values().iterator().next());
        Notification note = new Notification.Builder(ctx, CH_SERVICE)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(new Notification.BigTextStyle().bigText(text + "\n" + String.join(", ", box.names())))
                .setContentIntent(openApp(ctx))
                .addAction(new Notification.Action.Builder(null, ctx.getString(R.string.n_outbox_cancel), cancel).build())
                .setProgress(0, 0, box.preparing())
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setShowWhen(false)
                .build();
        nm(ctx).notify(ID_OUTBOX, note);
    }

    static void outboxProgress(Context ctx, String name, String pc, long done, long total) {
        int pct = total > 0 ? (int) (done * 100 / total) : 0;
        Notification n = new Notification.Builder(ctx, CH_SERVICE)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(ctx.getString(R.string.n_outbox_sending, pc))
                .setContentText(name + " · " + pct + " %")
                .setProgress(100, pct, false)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setShowWhen(false)
                .build();
        nm(ctx).notify(ID_OUTBOX, n);
    }

    private static int sentBurst;
    private static long lastSent;

    static synchronized void outboxSent(Context ctx, String name, String pc) {
        long now = android.os.SystemClock.elapsedRealtime();
        sentBurst = now - lastSent < 15_000 ? sentBurst + 1 : 1;
        lastSent = now;
        Notification n = new Notification.Builder(ctx, CH_FILES)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle(ctx.getString(R.string.n_outbox_sent, pc))
                .setContentText(sentBurst > 1 ? ctx.getResources().getQuantityString(R.plurals.n_outbox_sent_many, sentBurst, sentBurst) : name)
                .setOnlyAlertOnce(sentBurst > 1)
                .setAutoCancel(true)
                .build();
        nm(ctx).notify(ID_SENT, n);
    }

    private static NotificationManager nm(Context ctx) {
        return ctx.getSystemService(NotificationManager.class);
    }
}
