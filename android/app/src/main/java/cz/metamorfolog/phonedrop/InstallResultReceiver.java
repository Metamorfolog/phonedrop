package cz.metamorfolog.phonedrop;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.util.Log;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/** Výsledek instalace z PackageInstaller session; stav si drží pro dotaz z počítače (GET /install). */
public class InstallResultReceiver extends BroadcastReceiver {
    static final String EXTRA_NAME = "name";
    private static final Map<Integer, String> STATES = new ConcurrentHashMap<>();
    /** Potvrzení instalace, která systém nedovolil otevřít z pozadí – otevře je MainActivity. */
    private static final Map<Integer, Intent> CONFIRMS = new ConcurrentHashMap<>();

    /** Otevře čekající potvrzení (volá MainActivity v popředí, kde ho systém pustí). */
    static void launchPending(Context ctx) {
        for (Integer session : CONFIRMS.keySet()) {
            Intent i = CONFIRMS.remove(session);
            if (i == null) continue;
            Notifier.cancelInstall(ctx, session);
            ctx.startActivity(i);
            return;
        }
    }

    static void setState(int session, String state) { STATES.put(session, state); }

    /** pending | confirm | installed | failed: … | aborted | unknown */
    static String state(int session) {
        String s = STATES.get(session);
        return s != null ? s : "unknown";
    }

    @Override
    public void onReceive(Context ctx, Intent intent) {
        int session = intent.getIntExtra(PackageInstaller.EXTRA_SESSION_ID, -1);
        int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
        String name = intent.getStringExtra(EXTRA_NAME);
        String message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
        Log.i("PhoneDrop", "install " + session + " status=" + status + " " + message);

        if (status != PackageInstaller.STATUS_PENDING_USER_ACTION) CONFIRMS.remove(session);
        switch (status) {
            case PackageInstaller.STATUS_PENDING_USER_ACTION: {
                Intent confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent.class);
                if (confirm == null) break;
                setState(session, "confirm");
                confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                if (MainActivity.visible) {
                    ctx.startActivity(confirm);
                } else {
                    // Z pozadí systém aktivitu nepustí (BAL) – oznámení → PhoneDrop → potvrzení.
                    CONFIRMS.put(session, confirm);
                    Notifier.installConfirm(ctx, session, name);
                }
                break;
            }
            case PackageInstaller.STATUS_SUCCESS:
                setState(session, "installed");
                Notifier.installDone(ctx, session, "Nainstalováno", name);
                break;
            case PackageInstaller.STATUS_FAILURE_ABORTED:
                setState(session, "aborted");
                Notifier.installDone(ctx, session, "Instalace zrušena", name);
                break;
            default:
                setState(session, "failed: " + (message != null ? message : status));
                Notifier.installDone(ctx, session, "Instalace selhala", name + (message != null ? " – " + message : ""));
        }
    }
}
