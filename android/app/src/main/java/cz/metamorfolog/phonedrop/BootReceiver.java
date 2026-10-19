package cz.metamorfolog.phonedrop;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** Po startu telefonu (a po aktualizaci appky) znovu spustí přijímač, pokud je zapnutý. */
public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context ctx, Intent intent) {
        if (new Prefs(ctx).enabled()) ReceiverService.start(ctx);
    }
}
