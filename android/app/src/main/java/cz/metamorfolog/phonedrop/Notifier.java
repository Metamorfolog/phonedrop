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
    private static final String CH_SERVICE = "service";
    private static final String CH_FILES = "files";
    private static final String CH_INSTALL = "install";

    private Notifier() {
    }

    static void channels(Context ctx) {
        NotificationManager nm = ctx.getSystemService(NotificationManager.class);
        nm.createNotificationChannel(new NotificationChannel(CH_SERVICE, "Příjem na pozadí", NotificationManager.IMPORTANCE_MIN));
        nm.createNotificationChannel(new NotificationChannel(CH_FILES, "Přijaté soubory", NotificationManager.IMPORTANCE_DEFAULT));
        nm.createNotificationChannel(new NotificationChannel(CH_INSTALL, "Instalace APK", NotificationManager.IMPORTANCE_HIGH));
    }

    private static PendingIntent openApp(Context ctx) {
        return PendingIntent.getActivity(ctx, 0, new Intent(ctx, MainActivity.class),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
    }

    static Notification service(Context ctx, String address, String pin) {
        return new Notification.Builder(ctx, CH_SERVICE)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle("PhoneDrop čeká na soubory")
                .setContentText(address + " · PIN " + pin)
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
                .setContentTitle("Přijímám " + name)
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

    static void fileSaved(Context ctx, String name, Uri uri, String mime) {
        Intent view = new Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        PendingIntent open = PendingIntent.getActivity(ctx, name.hashCode(), view,
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification n = new Notification.Builder(ctx, CH_FILES)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle("Uloženo: " + name)
                .setContentText(Inbox.DIR)
                .setContentIntent(open)
                .setAutoCancel(true)
                .build();
        nm(ctx).notify(name.hashCode(), n);
    }

    /** Ťuknutí otevře PhoneDrop, který v popředí spustí systémové potvrzení instalace. */
    static void installConfirm(Context ctx, int session, String name) {
        PendingIntent pi = openApp(ctx);
        Notification n = new Notification.Builder(ctx, CH_INSTALL)
                .setSmallIcon(R.drawable.ic_stat)
                .setColor(ctx.getColor(R.color.accent))
                .setContentTitle("Instalovat " + name)
                .setContentText("Ťukni a potvrď instalaci")
                .setContentIntent(pi)
                .setCategory(Notification.CATEGORY_RECOMMENDATION)
                .setGroup("install-" + session)
                .setAutoCancel(true)
                .build();
        nm(ctx).notify(installId(session), n);
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

    private static NotificationManager nm(Context ctx) {
        return ctx.getSystemService(NotificationManager.class);
    }
}
