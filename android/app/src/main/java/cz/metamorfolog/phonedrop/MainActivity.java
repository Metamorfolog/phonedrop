package cz.metamorfolog.phonedrop;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.PowerManager;
import android.provider.Settings;
import android.view.Gravity;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;

import java.util.List;

/** Stav přijímače, PIN pro párování a odkazy na oprávnění, která instalace potřebuje. */
public class MainActivity extends Activity {
    /** Appka je v popředí → potvrzení instalace lze otevřít hned. */
    static volatile boolean visible;

    private static final Typeface MEDIUM = Typeface.create("sans-serif-medium", Typeface.NORMAL);

    private Prefs prefs;
    private LinearLayout root;
    private float dp;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = new Prefs(this);
        dp = getResources().getDisplayMetrics().density;
        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(px(16), px(20), px(16), px(24));
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
        boolean on = prefs.enabled();
        header(on);

        // Spárování: velký PIN + adresa.
        if (on) {
            LinearLayout pin = card(R.color.surface);
            pin.addView(label("PIN PRO SPÁROVÁNÍ POČÍTAČE", 12, R.color.dim, MEDIUM, 0.08f));
            String p = prefs.pin();
            TextView big = label(p.substring(0, 3) + " " + p.substring(3), 52, R.color.accent,
                    Typeface.create(Typeface.MONOSPACE, Typeface.BOLD), 0.06f);
            big.setPadding(0, px(4), 0, px(6));
            pin.addView(big);
            LinearLayout addr = row();
            addr.addView(chip("Adresa"));
            TextView a = label(ReceiverService.address(this), 16, R.color.text, Typeface.MONOSPACE, 0);
            a.setPadding(px(10), 0, 0, 0);
            addr.addView(a);
            pin.addView(addr);
            TextView hint = label("Ve Windows: PhoneDrop → ⋯ → Spárovat telefon, vyber tenhle telefon a opiš PIN.",
                    13, R.color.dim, null, 0);
            hint.setPadding(0, px(12), 0, 0);
            pin.addView(hint);
        }

        // Příjem zapnout/vypnout.
        LinearLayout recv = card(R.color.surface);
        LinearLayout r = row();
        LinearLayout texts = new LinearLayout(this);
        texts.setOrientation(LinearLayout.VERTICAL);
        texts.addView(label("Přijímat soubory", 17, R.color.text, MEDIUM, 0));
        texts.addView(label(on ? "Soubory → Stažené/Drop, APK → instalace" : "Počítač teď telefon nenajde",
                13, R.color.dim, null, 0));
        r.addView(texts, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        Switch sw = new Switch(this);
        sw.setChecked(on);
        int[][] states = {{android.R.attr.state_checked}, {}};
        sw.setThumbTintList(new ColorStateList(states, new int[]{getColor(R.color.accent), getColor(R.color.dim)}));
        sw.setTrackTintList(new ColorStateList(states, new int[]{getColor(R.color.accentDim), getColor(R.color.line)}));
        sw.setOnCheckedChangeListener((b, checked) -> {
            prefs.setEnabled(checked);
            if (checked) ReceiverService.start(this);
            else ReceiverService.stop(this);
            root.postDelayed(this::render, 300);
        });
        r.addView(sw);
        recv.addView(r);

        // Co ještě chybí k pohodlnému používání.
        if (!getPackageManager().canRequestPackageInstalls()) {
            warning("Instalace APK", "Povol PhoneDropu „Instalovat neznámé aplikace“, jinak se systém bude ptát u každé instalace.",
                    "Povolit", () -> startActivity(new Intent(
                            Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName()))));
        }
        if (!getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(getPackageName())) {
            warning("Příjem se zhasnutým displejem", "Vypni pro PhoneDrop optimalizaci baterie, ať ho systém v klidu neuspí.",
                    "Vypnout optimalizaci", () -> startActivity(new Intent(
                            Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName()))));
        }

        // Spárované počítače.
        List<String> clients = prefs.pairedClients();
        LinearLayout pcs = card(R.color.surface);
        pcs.addView(label("SPÁROVANÉ POČÍTAČE", 12, R.color.dim, MEDIUM, 0.08f));
        if (clients.isEmpty()) {
            TextView none = label("Zatím žádný.", 15, R.color.text, null, 0);
            none.setPadding(0, px(8), 0, 0);
            pcs.addView(none);
        } else {
            for (String c : clients) {
                LinearLayout pc = row();
                pc.setPadding(0, px(10), 0, 0);
                ImageView icon = new ImageView(this);
                icon.setImageResource(R.drawable.ic_pc);
                pc.addView(icon, new LinearLayout.LayoutParams(px(22), px(22)));
                TextView name = label(c, 16, R.color.text, null, 0);
                name.setPadding(px(12), 0, 0, 0);
                pc.addView(name);
                pcs.addView(pc);
            }
            TextView unpair = button("Zrušit všechna párování", false);
            unpair.setTextColor(getColor(R.color.bad));
            unpair.setOnClickListener(v -> {
                prefs.unpairAll();
                ReceiverService.stop(this);
                if (prefs.enabled()) ReceiverService.start(this);
                render();
            });
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
            lp.topMargin = px(14);
            pcs.addView(unpair, lp);
        }

        TextView foot = label("PhoneDrop " + versionName() + "  ·  jen v místní síti", 12, R.color.faint, null, 0);
        foot.setGravity(Gravity.CENTER);
        foot.setPadding(0, px(8), 0, 0);
        root.addView(foot, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));
    }

    // ---- stavební kameny ----

    private void header(boolean on) {
        LinearLayout h = row();
        h.setPadding(px(4), 0, px(4), px(18));
        ImageView icon = new ImageView(this);
        icon.setImageResource(R.drawable.ic_launcher);
        h.addView(icon, new LinearLayout.LayoutParams(px(44), px(44)));
        TextView title = label("PhoneDrop", 26, R.color.text, Typeface.create("sans-serif-medium", Typeface.BOLD), -0.01f);
        title.setPadding(px(12), 0, 0, 0);
        h.addView(title, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));

        boolean running = on && ReceiverService.running;
        TextView pill = label(running ? "●  Přijímá" : "●  Vypnuto", 13, running ? R.color.accent : R.color.dim, MEDIUM, 0);
        pill.setPadding(px(12), px(6), px(12), px(6));
        pill.setBackground(rounded(running ? R.color.accentBg : R.color.surface, 50));
        h.addView(pill);
        root.addView(h);
    }

    private LinearLayout card(int color) {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(px(18), px(16), px(18), px(18));
        c.setBackground(rounded(color, 22));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.bottomMargin = px(12);
        root.addView(c, lp);
        return c;
    }

    private void warning(String title, String text, String action, Runnable run) {
        LinearLayout c = card(R.color.warnBg);
        c.addView(label(title, 16, R.color.warn, MEDIUM, 0));
        TextView t = label(text, 14, R.color.text, null, 0);
        t.setPadding(0, px(4), 0, px(12));
        c.addView(t);
        TextView b = button(action, true);
        b.setBackground(ripple(R.color.warn));
        b.setOnClickListener(v -> run.run());
        c.addView(b, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT));
    }

    private TextView button(String text, boolean primary) {
        TextView b = label(text, 15, primary ? R.color.onAccent : R.color.text, MEDIUM, 0);
        b.setGravity(Gravity.CENTER);
        b.setPadding(px(20), px(10), px(20), px(10));
        b.setBackground(ripple(primary ? R.color.accent : R.color.surface2));
        b.setClickable(true);
        b.setFocusable(true);
        return b;
    }

    private TextView chip(String text) {
        TextView c = label(text, 12, R.color.dim, MEDIUM, 0.02f);
        c.setPadding(px(10), px(3), px(10), px(3));
        c.setBackground(rounded(R.color.surface2, 50));
        return c;
    }

    private TextView label(String s, float sp, int color, Typeface face, float spacing) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        t.setTextColor(getColor(color));
        if (face != null) t.setTypeface(face);
        if (spacing != 0) t.setLetterSpacing(spacing);
        return t;
    }

    private LinearLayout row() {
        LinearLayout r = new LinearLayout(this);
        r.setOrientation(LinearLayout.HORIZONTAL);
        r.setGravity(Gravity.CENTER_VERTICAL);
        return r;
    }

    private GradientDrawable rounded(int color, int radiusDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(getColor(color));
        d.setCornerRadius(px(radiusDp));
        return d;
    }

    private RippleDrawable ripple(int color) {
        return new RippleDrawable(ColorStateList.valueOf(0x33FFFFFF), rounded(color, 50), null);
    }

    private String versionName() {
        try {
            return getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
        } catch (PackageManager.NameNotFoundException e) {
            return "";
        }
    }

    private int px(float dpValue) {
        return Math.round(dpValue * dp);
    }
}
