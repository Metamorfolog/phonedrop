package cz.metamorfolog.phonedrop;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Parcelable;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Cíl v nabídce Sdílet: soubory jdou do počítače (uloží se tam do Stažených).
 * Bez vlastního okna – jen výběr počítače, když je jich spárovaných víc. Kopírování a odeslání
 * obstará {@link ReceiverService} na pozadí, počítač si soubory vyzvedne sám.
 */
public class ShareActivity extends Activity {
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        List<Uri> uris = uris(getIntent());
        uris.removeIf(u -> !Outbox.shareable(this, u));
        if (uris.isEmpty()) {
            finish();
            return;
        }
        Prefs prefs = new Prefs(this);
        Map<String, String> paired = prefs.paired();
        if (paired.isEmpty()) {
            Toast.makeText(this, R.string.share_unpaired, Toast.LENGTH_LONG).show();
            startActivity(new Intent(this, MainActivity.class));
            finish();
            return;
        }
        // Připojené počítače (právě se ptají); jen spárované – po zrušení párování se nenabízí.
        Map<String, String> online = new java.util.LinkedHashMap<>();
        for (Map.Entry<String, String> e : Outbox.get(this).online().entrySet()) {
            if (paired.containsKey(e.getKey())) online.put(e.getKey(), e.getValue());
        }
        if (paired.size() == 1) {
            Map.Entry<String, String> only = paired.entrySet().iterator().next();
            send(uris, only.getKey(), only.getValue(), online.containsKey(only.getKey()));
            return;
        }
        // Víc spárovaných počítačů: vždycky se zeptat. Připojené (běží v nich PhoneDrop) nahoře,
        // u ostatních je vidět, že soubor počká, až se zapnou.
        List<String> keys = new ArrayList<>(online.keySet());
        for (String k : paired.keySet()) if (!keys.contains(k)) keys.add(k);
        String[] labels = new String[keys.size()];
        for (int i = 0; i < keys.size(); i++) {
            String k = keys.get(i);
            labels[i] = online.containsKey(k)
                    ? getString(R.string.share_pc_online, online.get(k))
                    : getString(R.string.share_pc_offline, paired.get(k));
        }
        new AlertDialog.Builder(this)
                .setTitle(R.string.share_pick)
                .setItems(labels, (d, which) -> {
                    String k = keys.get(which);
                    send(uris, k, online.containsKey(k) ? online.get(k) : paired.get(k), online.containsKey(k));
                })
                .setOnCancelListener(d -> finish())
                .show();
    }

    private void send(List<Uri> uris, String target, String name, boolean isOnline) {
        Intent svc = new Intent(this, ReceiverService.class)
                .setAction(ReceiverService.ACTION_SHARE)
                .putExtra(ReceiverService.EXTRA_TARGET, target)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        ClipData clip = null;
        for (Uri u : uris) {
            if (clip == null) clip = ClipData.newRawUri("", u);
            else clip.addItem(new ClipData.Item(u));
        }
        svc.setClipData(clip);
        try {
            startForegroundService(svc);
            Toast.makeText(this, isOnline ? getString(R.string.share_sending, name)
                    : getString(R.string.share_queued_for, name), Toast.LENGTH_LONG).show();
        } catch (RuntimeException e) {
            Toast.makeText(this, getString(R.string.share_failed, e.getMessage()), Toast.LENGTH_LONG).show();
        }
        finish();
    }

    @SuppressWarnings("deprecation")
    private static List<Uri> uris(Intent intent) {
        List<Uri> out = new ArrayList<>();
        if (intent == null) return out;
        String action = intent.getAction();
        if (Intent.ACTION_SEND.equals(action)) {
            Uri u = Build.VERSION.SDK_INT >= 33
                    ? intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri.class)
                    : intent.getParcelableExtra(Intent.EXTRA_STREAM);
            if (u != null) out.add(u);
        } else if (Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            ArrayList<? extends Parcelable> list = Build.VERSION.SDK_INT >= 33
                    ? intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri.class)
                    : intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (list != null) for (Parcelable p : list) if (p instanceof Uri) out.add((Uri) p);
        }
        // Záloha: některé appky dávají soubory jen do ClipData.
        if (out.isEmpty() && intent.getClipData() != null) {
            ClipData c = intent.getClipData();
            for (int i = 0; i < c.getItemCount(); i++) if (c.getItemAt(i).getUri() != null) out.add(c.getItemAt(i).getUri());
        }
        return out;
    }
}
