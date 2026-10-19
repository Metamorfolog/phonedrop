package cz.metamorfolog.phonedrop;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Bundle;
import android.os.PowerManager;
import android.provider.Settings;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;

import java.util.List;

/** Stav přijímače, PIN pro párování a odkazy na oprávnění, která instalace potřebuje. */
public class MainActivity extends Activity {
    /** Appka je v popředí → potvrzení instalace lze otevřít hned. */
    static volatile boolean visible;

    private Prefs prefs;
    private LinearLayout root;
    private int pad;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = new Prefs(this);
        pad = Math.round(16 * getResources().getDisplayMetrics().density);
        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(pad, pad * 2, pad, pad);
        ScrollView scroll = new ScrollView(this);
        scroll.setFitsSystemWindows(true);
        scroll.addView(root);
        setContentView(scroll);

        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        }
        if (prefs.enabled()) ReceiverService.start(this);
    }

    @Override
    protected void onResume() {
        super.onResume();
        visible = true;
        ReceiverService.onChange = () -> runOnUiThread(this::render);
        render();
        // Instalace, kterou systém z pozadí nepustil – teď je appka v popředí, tak ji otevřeme.
        InstallResultReceiver.launchPending(this);
    }

    @Override
    protected void onPause() {
        visible = false;
        ReceiverService.onChange = null;
        super.onPause();
    }

    private void render() {
        root.removeAllViews();
        text("PhoneDrop", 28, R.color.text).setTypeface(Typeface.DEFAULT_BOLD);
        text("Přetáhni soubor do okénka PhoneDrop ve Windows. APK se rovnou nabídne k instalaci, "
                + "ostatní soubory se uloží do Stažené/Drop.", 14, R.color.dim);
        gap();

        Switch on = new Switch(this);
        on.setText("Přijímat soubory");
        on.setTextColor(getColor(R.color.text));
        on.setTextSize(18);
        on.setChecked(prefs.enabled());
        on.setOnCheckedChangeListener((b, checked) -> {
            prefs.setEnabled(checked);
            if (checked) ReceiverService.start(this);
            else ReceiverService.stop(this);
            root.postDelayed(this::render, 300);
        });
        root.addView(on);

        if (prefs.enabled()) {
            text("Adresa: " + ReceiverService.address(this), 16, R.color.text);
            gap();
            text("PIN pro spárování počítače", 14, R.color.dim);
            String pin = prefs.pin();
            TextView big = text(pin.substring(0, 3) + " " + pin.substring(3), 44, R.color.accent);
            big.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        }
        gap();

        if (!getPackageManager().canRequestPackageInstalls()) {
            text("Pro instalaci APK povol PhoneDropu „Instalovat neznámé aplikace“.", 14, R.color.warn);
            button("Povolit instalaci aplikací", v -> startActivity(new Intent(
                    Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName()))));
        }
        if (!getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(getPackageName())) {
            text("Aby příjem fungoval i se zhasnutým displejem, vypni pro PhoneDrop optimalizaci baterie.", 14, R.color.warn);
            button("Vypnout optimalizaci baterie", v -> startActivity(new Intent(
                    Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName()))));
        }

        List<String> clients = prefs.pairedClients();
        gap();
        if (clients.isEmpty()) {
            text("Zatím není spárovaný žádný počítač.", 14, R.color.dim);
        } else {
            text("Spárované počítače: " + String.join(", ", clients), 14, R.color.dim);
            button("Zrušit všechna párování", v -> {
                prefs.unpairAll();
                ReceiverService.stop(this);
                if (prefs.enabled()) ReceiverService.start(this);
                render();
            });
        }
    }

    private TextView text(String s, int sp, int color) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(getColor(color));
        t.setPadding(0, pad / 4, 0, pad / 4);
        root.addView(t);
        return t;
    }

    private void button(String label, View.OnClickListener click) {
        Button b = new Button(this);
        b.setText(label);
        b.setAllCaps(false);
        b.setOnClickListener(click);
        root.addView(b);
    }

    private void gap() {
        View v = new View(this);
        root.addView(v, new LinearLayout.LayoutParams(1, pad));
    }
}
