package cz.metamorfolog.phonedrop;

import android.Manifest;
import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.app.Activity;
import android.app.DownloadManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Typeface;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.PowerManager;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.animation.DecelerateInterpolator;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;

import java.util.List;

/** Stav přijímače, PIN pro párování a kontrolní seznam oprávnění, která instalace potřebuje. */
public class MainActivity extends Activity {
    /** Appka je v popředí → potvrzení instalace lze otevřít hned. */
    static volatile boolean visible;

    private static final Typeface MEDIUM = Typeface.create("sans-serif-medium", Typeface.NORMAL);
    private static final Typeface BOLD = Typeface.create("sans-serif", Typeface.BOLD);
    private static final Typeface BLACK = Typeface.create("sans-serif-black", Typeface.NORMAL);
    private static final int WRAP = ViewGroup.LayoutParams.WRAP_CONTENT;
    private static final int MATCH = ViewGroup.LayoutParams.MATCH_PARENT;

    private Prefs prefs;
    private LinearLayout root;
    private float dp;
    private boolean animateIn = true;
    private ValueAnimator pulse;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = new Prefs(this);
        dp = getResources().getDisplayMetrics().density;
        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(px(18), px(18), px(18), px(28));
        root.setClipToPadding(false);
        ScrollView scroll = new ScrollView(this);
        scroll.setClipToPadding(false);
        scroll.setVerticalScrollBarEnabled(false);
        scroll.addView(root);
        // Obsah nesmí jet pod stavový a navigační řádek (Android 15+ kreslí od okraje k okraji).
        FrameLayout frame = new FrameLayout(this);
        frame.setBackgroundColor(color(R.color.bg));
        frame.addView(scroll);
        frame.setOnApplyWindowInsetsListener((v, insets) -> {
            android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
            v.setPadding(bars.left, bars.top, bars.right, 0);
            root.setPadding(px(18), px(18), px(18), px(28) + bars.bottom);
            return WindowInsets.CONSUMED;
        });
        setContentView(frame);

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

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        render();
    }

    private void render() {
        if (pulse != null) pulse.cancel();
        root.removeAllViews();
        boolean on = prefs.enabled();
        boolean running = on && ReceiverService.running;

        header(running, on);
        hero(on);

        // Příjem.
        section("Příjem", null);
        LinearLayout recv = card();
        Switch sw = new Switch(this);
        sw.setChecked(on);
        int[][] states = {{android.R.attr.state_checked}, {}};
        sw.setThumbTintList(new ColorStateList(states, new int[]{color(R.color.accent), color(R.color.faint)}));
        sw.setTrackTintList(new ColorStateList(states, new int[]{color(R.color.accentTint), color(R.color.line)}));
        sw.setOnCheckedChangeListener((b, checked) -> setEnabled(checked));
        View recvRow = item(recv, R.drawable.ic_download, R.color.accent, R.color.accentTint,
                "Přijímat soubory", on ? "Počítač telefon najde v síti" : "Vypnuto – počítač telefon nenajde", sw);
        recvRow.setOnClickListener(v -> sw.toggle());
        divider(recv);
        View store = item(recv, R.drawable.ic_folder, R.color.blue, R.color.blueTint,
                "Ukládání", "Stažené › Drop  ·  APK rovnou k instalaci", chevron());
        store.setOnClickListener(v -> open(new Intent(DownloadManager.ACTION_VIEW_DOWNLOADS)));

        // Kontrolní seznam: co telefon potřebuje, aby vše šlo na jedno ťuknutí.
        boolean install = getPackageManager().canRequestPackageInstalls();
        boolean battery = getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(getPackageName());
        boolean notify = checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
        int done = (install ? 1 : 0) + (battery ? 1 : 0) + (notify ? 1 : 0);
        section("Nastavení", done == 3 ? "vše hotovo" : done + " ze 3");
        LinearLayout setup = card();
        item(setup, R.drawable.ic_shield, R.color.violet, R.color.violetTint, "Instalace aplikací",
                install ? "Povoleno – APK jde nainstalovat jedním ťuknutím" : "Bez toho se systém ptá u každého APK",
                install ? doneMark() : action("Povolit", () -> open(new Intent(
                        Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName())))));
        divider(setup);
        item(setup, R.drawable.ic_battery, R.color.warn, R.color.warnTint, "Běh na pozadí",
                battery ? "Příjem funguje i se zhasnutým displejem" : "Jinak telefon v klidu přestane přijímat",
                battery ? doneMark() : action("Povolit", () -> open(new Intent(
                        Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName())))));
        divider(setup);
        item(setup, R.drawable.ic_bell, R.color.blue, R.color.blueTint, "Oznámení",
                notify ? "Uvidíš průběh a potvrzení instalace" : "Potřeba pro potvrzení instalace z pozadí",
                notify ? doneMark() : action("Povolit", () -> open(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                        .putExtra(Settings.EXTRA_APP_PACKAGE, getPackageName()))));

        // Počítače.
        List<String> clients = prefs.pairedClients();
        section("Počítače", clients.isEmpty() ? null : String.valueOf(clients.size()));
        LinearLayout pcs = card();
        if (clients.isEmpty()) {
            step(pcs, 1, "Na počítači spusť PhoneDrop");
            step(pcs, 2, "Klikni na ⋯ → Spárovat telefon a vyber tenhle");
            step(pcs, 3, "Opiš PIN z horní karty – a přetahuj soubory");
        } else {
            for (int i = 0; i < clients.size(); i++) {
                if (i > 0) divider(pcs);
                item(pcs, R.drawable.ic_monitor, R.color.accent, R.color.accentTint, clients.get(i), "Spárováno", null);
            }
            TextView unpair = label("Zrušit všechna párování", 14, R.color.bad, MEDIUM);
            unpair.setGravity(Gravity.CENTER);
            unpair.setPadding(px(16), px(14), px(16), px(16));
            unpair.setBackground(ripple(null));
            unpair.setOnClickListener(v -> {
                prefs.unpairAll();
                ReceiverService.stop(this);
                if (prefs.enabled()) ReceiverService.start(this);
                render();
            });
            divider(pcs);
            pcs.addView(unpair, new LinearLayout.LayoutParams(MATCH, WRAP));
        }

        TextView foot = label("PhoneDrop " + versionName() + "  ·  jen v místní síti", 12, R.color.faint, null);
        foot.setGravity(Gravity.CENTER);
        foot.setPadding(0, px(22), 0, 0);
        root.addView(foot, new LinearLayout.LayoutParams(MATCH, WRAP));

        if (animateIn) {
            animateIn = false;
            for (int i = 0; i < root.getChildCount(); i++) {
                View v = root.getChildAt(i);
                v.setAlpha(0f);
                v.setTranslationY(px(18));
                v.animate().alpha(1f).translationY(0).setStartDelay(40L * i).setDuration(380)
                        .setInterpolator(new DecelerateInterpolator(2f)).start();
            }
        }
    }

    private void setEnabled(boolean on) {
        prefs.setEnabled(on);
        if (on) ReceiverService.start(this);
        else ReceiverService.stop(this);
        root.postDelayed(this::render, 350);
    }

    // ---- sekce ----

    private void header(boolean running, boolean on) {
        LinearLayout h = row();
        h.setPadding(px(2), px(4), px(2), px(18));

        ImageView logo = new ImageView(this);
        logo.setImageResource(R.drawable.ic_launcher);
        logo.setBackground(oval(R.color.heroEnd));
        logo.setElevation(px(6));
        logo.setOutlineSpotShadowColor(color(R.color.heroEnd));
        logo.setOutlineAmbientShadowColor(color(R.color.heroEnd));
        h.addView(logo, new LinearLayout.LayoutParams(px(52), px(52)));

        LinearLayout titles = column();
        titles.setPadding(px(14), 0, px(8), 0);
        titles.addView(label("PhoneDrop", 26, R.color.text, BLACK));
        titles.addView(label("Soubory z počítače jedním tahem", 13, R.color.dim, null));
        h.addView(titles, new LinearLayout.LayoutParams(0, WRAP, 1));

        LinearLayout pill = row();
        pill.setPadding(px(12), px(7), px(14), px(7));
        pill.setBackground(rounded(running ? R.color.accentTint : R.color.line, 50));
        View dot = new View(this);
        dot.setBackground(oval(running ? R.color.accent : R.color.faint));
        pill.addView(dot, new LinearLayout.LayoutParams(px(8), px(8)));
        TextView t = label(running ? "Přijímá" : on ? "Startuje…" : "Vypnuto", 13, running ? R.color.accent : R.color.dim, MEDIUM);
        t.setPadding(px(8), 0, 0, 0);
        pill.addView(t);
        h.addView(pill);
        root.addView(h);

        if (running) {
            pulse = ObjectAnimator.ofFloat(dot, View.ALPHA, 1f, 0.25f);
            pulse.setDuration(900);
            pulse.setRepeatCount(ValueAnimator.INFINITE);
            pulse.setRepeatMode(ValueAnimator.REVERSE);
            pulse.start();
        }
    }

    /** Zelená karta s PINem a adresou; když je příjem vypnutý, nabídne jeho zapnutí. */
    private void hero(boolean on) {
        LinearLayout hero = column();
        hero.setPadding(px(22), px(20), px(22), px(22));
        hero.setBackground(new HeroDrawable(color(R.color.heroStart), color(R.color.heroEnd), color(R.color.heroGlow), px(28)));
        hero.setElevation(px(8));
        hero.setOutlineSpotShadowColor(color(R.color.heroEnd));
        hero.setOutlineAmbientShadowColor(color(R.color.heroEnd));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(MATCH, WRAP);
        lp.bottomMargin = px(8);
        root.addView(hero, lp);

        if (!on) {
            hero.addView(white(label("Příjem je vypnutý", 22, 0, BOLD), 1f));
            hero.addView(white(label("Počítač teď telefon nenajde.", 14, 0, null), 0.85f));
            TextView b = label("Zapnout příjem", 15, R.color.heroEnd, MEDIUM);
            b.setPadding(px(20), px(10), px(20), px(10));
            b.setBackground(new RippleDrawable(ColorStateList.valueOf(0x22000000), roundedRaw(0xFFFFFFFF, 50), null));
            b.setOnClickListener(v -> setEnabled(true));
            LinearLayout.LayoutParams bp = new LinearLayout.LayoutParams(WRAP, WRAP);
            bp.topMargin = px(16);
            hero.addView(b, bp);
            return;
        }

        LinearLayout top = row();
        TextView cap = white(label("PIN PRO SPÁROVÁNÍ", 12, 0, MEDIUM), 0.8f);
        cap.setLetterSpacing(0.12f);
        top.addView(cap, new LinearLayout.LayoutParams(0, WRAP, 1));
        LinearLayout chip = row();
        chip.setPadding(px(10), px(5), px(12), px(5));
        chip.setBackground(roundedRaw(0x2EFFFFFF, 50));
        ImageView wifi = new ImageView(this);
        wifi.setImageResource(R.drawable.ic_wifi);
        wifi.setImageTintList(ColorStateList.valueOf(0xFFFFFFFF));
        chip.addView(wifi, new LinearLayout.LayoutParams(px(15), px(15)));
        String addr = ReceiverService.address(this);
        TextView a = white(label(addr, 13, 0, MEDIUM), 1f);
        a.setPadding(px(6), 0, 0, 0);
        chip.addView(a);
        top.addView(chip);
        hero.addView(top);

        String p = prefs.pin();
        TextView pin = white(label(p.substring(0, 3) + " " + p.substring(3), 60, 0, BLACK), 1f);
        pin.setLetterSpacing(0.06f);
        pin.setIncludeFontPadding(false);
        pin.setPadding(0, px(14), 0, px(10));
        pin.setShadowLayer(px(10), 0, px(2), 0x33000000);
        hero.addView(pin);

        hero.addView(white(label("Ve Windows: PhoneDrop → ⋯ → Spárovat telefon", 14, 0, null), 0.88f));
    }

    private void section(String title, String trailing) {
        LinearLayout s = row();
        s.setPadding(px(6), px(22), px(6), px(10));
        TextView t = label(title.toUpperCase(), 12, R.color.dim, MEDIUM);
        t.setLetterSpacing(0.1f);
        s.addView(t, new LinearLayout.LayoutParams(0, WRAP, 1));
        if (trailing != null) {
            TextView tr = label(trailing, 12, R.color.accent, MEDIUM);
            tr.setPadding(px(10), px(3), px(10), px(3));
            tr.setBackground(rounded(R.color.accentTint, 50));
            s.addView(tr);
        }
        root.addView(s);
    }

    private LinearLayout card() {
        LinearLayout c = column();
        c.setBackground(rounded(R.color.card, 24));
        c.setElevation(px(2));
        c.setClipToOutline(true);
        shadow(c);
        root.addView(c, new LinearLayout.LayoutParams(MATCH, WRAP));
        return c;
    }

    /** Řádek karty: ikona v barevném kolečku, nadpis, popis a volitelný prvek vpravo. */
    private View item(LinearLayout card, int icon, int tint, int tintBg, String title, String sub, View trailing) {
        LinearLayout r = row();
        r.setPadding(px(16), px(14), px(14), px(14));
        r.setBackground(ripple(null));
        r.setMinimumHeight(px(68));

        FrameLayout bubble = new FrameLayout(this);
        bubble.setBackground(rounded(tintBg, 14));
        ImageView iv = new ImageView(this);
        iv.setImageResource(icon);
        iv.setImageTintList(ColorStateList.valueOf(color(tint)));
        bubble.addView(iv, new FrameLayout.LayoutParams(px(22), px(22), Gravity.CENTER));
        r.addView(bubble, new LinearLayout.LayoutParams(px(42), px(42)));

        LinearLayout texts = column();
        texts.setPadding(px(14), 0, px(10), 0);
        texts.addView(label(title, 16, R.color.text, MEDIUM));
        TextView s = label(sub, 13, R.color.dim, null);
        s.setPadding(0, px(2), 0, 0);
        texts.addView(s);
        r.addView(texts, new LinearLayout.LayoutParams(0, WRAP, 1));
        if (trailing != null) r.addView(trailing);
        card.addView(r, new LinearLayout.LayoutParams(MATCH, WRAP));
        return r;
    }

    private void step(LinearLayout card, int n, String text) {
        LinearLayout r = row();
        r.setPadding(px(16), n == 1 ? px(16) : px(8), px(16), n == 3 ? px(16) : px(8));
        TextView num = label(String.valueOf(n), 14, R.color.accent, BOLD);
        num.setGravity(Gravity.CENTER);
        num.setBackground(oval(R.color.accentTint));
        r.addView(num, new LinearLayout.LayoutParams(px(30), px(30)));
        TextView t = label(text, 15, R.color.text, null);
        t.setPadding(px(14), 0, 0, 0);
        r.addView(t, new LinearLayout.LayoutParams(0, WRAP, 1));
        card.addView(r);
    }

    private View doneMark() {
        FrameLayout f = new FrameLayout(this);
        f.setBackground(oval(R.color.accentTint));
        ImageView iv = new ImageView(this);
        iv.setImageResource(R.drawable.ic_check);
        iv.setImageTintList(ColorStateList.valueOf(color(R.color.accent)));
        f.addView(iv, new FrameLayout.LayoutParams(px(18), px(18), Gravity.CENTER));
        f.setLayoutParams(new LinearLayout.LayoutParams(px(30), px(30)));
        return f;
    }

    private View action(String text, Runnable run) {
        TextView b = label(text, 13, R.color.onAccent, MEDIUM);
        b.setPadding(px(16), px(8), px(16), px(8));
        b.setBackground(new RippleDrawable(ColorStateList.valueOf(0x33FFFFFF), rounded(R.color.accent, 50), null));
        b.setOnClickListener(v -> run.run());
        return b;
    }

    private View chevron() {
        ImageView iv = new ImageView(this);
        iv.setImageResource(R.drawable.ic_chevron);
        iv.setImageTintList(ColorStateList.valueOf(color(R.color.faint)));
        iv.setLayoutParams(new LinearLayout.LayoutParams(px(22), px(22)));
        return iv;
    }

    private void divider(LinearLayout card) {
        View d = new View(this);
        d.setBackgroundColor(color(R.color.line));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(MATCH, Math.max(1, px(1)));
        lp.leftMargin = px(72);
        card.addView(d, lp);
    }

    // ---- pomocníci ----

    private void open(Intent i) {
        try {
            startActivity(i);
        } catch (RuntimeException e) {
            startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName())));
        }
    }

    private void shadow(View v) {
        v.setOutlineSpotShadowColor(color(R.color.shadow));
        v.setOutlineAmbientShadowColor(color(R.color.shadow));
    }

    private TextView white(TextView t, float alpha) {
        t.setTextColor(((int) (alpha * 255) << 24) | 0xFFFFFF);
        return t;
    }

    private TextView label(String s, float sp, int colorRes, Typeface face) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(sp);
        if (colorRes != 0) t.setTextColor(color(colorRes));
        if (face != null) t.setTypeface(face);
        return t;
    }

    private LinearLayout row() {
        LinearLayout r = new LinearLayout(this);
        r.setOrientation(LinearLayout.HORIZONTAL);
        r.setGravity(Gravity.CENTER_VERTICAL);
        return r;
    }

    private LinearLayout column() {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        return c;
    }

    private GradientDrawable rounded(int colorRes, int radiusDp) {
        return roundedRaw(color(colorRes), radiusDp);
    }

    private GradientDrawable roundedRaw(int argb, int radiusDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(argb);
        d.setCornerRadius(px(radiusDp));
        return d;
    }

    private GradientDrawable oval(int colorRes) {
        GradientDrawable d = new GradientDrawable();
        d.setShape(GradientDrawable.OVAL);
        d.setColor(color(colorRes));
        return d;
    }

    private RippleDrawable ripple(GradientDrawable content) {
        return new RippleDrawable(ColorStateList.valueOf(color(R.color.line)), content, content != null ? null : new ColorDrawable(0xFFFFFFFF));
    }

    private int color(int res) {
        return getColor(res);
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
