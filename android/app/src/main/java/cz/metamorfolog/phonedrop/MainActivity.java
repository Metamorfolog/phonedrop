package cz.metamorfolog.phonedrop;

import android.Manifest;
import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.app.Activity;
import android.app.DownloadManager;
import android.content.Context;
import android.content.Intent;
import android.content.res.Configuration;
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

    /** Vlastní volba vzhledu přepíše noční režim systému jen pro tuhle obrazovku (values-night). */
    @Override
    protected void attachBaseContext(Context base) {
        String theme = new Prefs(base).theme();
        if (!"system".equals(theme)) {
            Configuration c = new Configuration(base.getResources().getConfiguration());
            c.uiMode = (c.uiMode & ~Configuration.UI_MODE_NIGHT_MASK)
                    | ("dark".equals(theme) ? Configuration.UI_MODE_NIGHT_YES : Configuration.UI_MODE_NIGHT_NO);
            base = base.createConfigurationContext(c);
        }
        super.attachBaseContext(base);
    }

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
            int left, top, right, bottom;
            if (android.os.Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars());
                left = bars.left; top = bars.top; right = bars.right; bottom = bars.bottom;
            } else {
                // Android 10: starší API (WindowInsets.Type je až od Androidu 11).
                left = insets.getSystemWindowInsetLeft(); top = insets.getSystemWindowInsetTop();
                right = insets.getSystemWindowInsetRight(); bottom = insets.getSystemWindowInsetBottom();
            }
            v.setPadding(left, top, right, 0);
            root.setPadding(px(18), px(18), px(18), px(28) + bottom);
            return android.os.Build.VERSION.SDK_INT >= 30 ? WindowInsets.CONSUMED : insets.consumeSystemWindowInsets();
        });
        setContentView(frame);

        if (!notificationsAllowed()) requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 1);
        if (prefs.enabled()) ReceiverService.start(this);
    }

    @Override
    protected void onResume() {
        super.onResume();
        visible = true;
        Prefs.pairingWindow(true);
        ReceiverService.onChange = () -> runOnUiThread(this::render);
        Updater.onChange = () -> runOnUiThread(this::render);
        render();
        Updater.check(this, false);
        // Instalace, kterou systém z pozadí nepustil – teď je appka v popředí, tak ji otevřeme.
        InstallResultReceiver.launchPending(this);
    }

    @Override
    protected void onPause() {
        visible = false;
        Prefs.pairingWindow(false);
        ReceiverService.onChange = null;
        Updater.onChange = null;
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
        section(getString(R.string.section_receive), null);
        LinearLayout recv = card();
        Switch sw = new Switch(this);
        sw.setChecked(on);
        int[][] states = {{android.R.attr.state_checked}, {}};
        sw.setThumbTintList(new ColorStateList(states, new int[]{color(R.color.accent), color(R.color.faint)}));
        sw.setTrackTintList(new ColorStateList(states, new int[]{color(R.color.accentTint), color(R.color.line)}));
        sw.setOnCheckedChangeListener((b, checked) -> setEnabled(checked));
        View recvRow = item(recv, R.drawable.ic_download, R.color.accent, R.color.accentTint,
                getString(R.string.recv_title), getString(on ? R.string.recv_on : R.string.recv_off), sw);
        recvRow.setOnClickListener(v -> sw.toggle());
        divider(recv);
        View store = item(recv, R.drawable.ic_folder, R.color.blue, R.color.blueTint,
                getString(R.string.storage_title), getString(R.string.storage_sub), chevron());
        store.setOnClickListener(v -> open(new Intent(DownloadManager.ACTION_VIEW_DOWNLOADS)));
        divider(recv);
        View sendRow = item(recv, R.drawable.ic_upload, R.color.violet, R.color.violetTint,
                getString(R.string.send_title), getString(R.string.send_sub), chevron());
        sendRow.setOnClickListener(v -> pickToSend());

        // Kontrolní seznam: co telefon potřebuje, aby vše šlo na jedno ťuknutí.
        boolean install = getPackageManager().canRequestPackageInstalls();
        boolean battery = getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(getPackageName());
        boolean notify = notificationsAllowed();
        int done = (install ? 1 : 0) + (battery ? 1 : 0) + (notify ? 1 : 0);
        section(getString(R.string.section_setup), done == 3 ? getString(R.string.setup_done) : getString(R.string.setup_progress, done, 3));
        LinearLayout setup = card();
        item(setup, R.drawable.ic_shield, R.color.violet, R.color.violetTint, getString(R.string.install_title),
                getString(install ? R.string.install_ok : R.string.install_missing),
                install ? doneMark() : action(getString(R.string.allow), () -> open(new Intent(
                        Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + getPackageName())))));
        divider(setup);
        item(setup, R.drawable.ic_battery, R.color.warn, R.color.warnTint, getString(R.string.battery_title),
                getString(battery ? R.string.battery_ok : R.string.battery_missing),
                battery ? doneMark() : action(getString(R.string.allow), () -> open(new Intent(
                        Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName())))));
        divider(setup);
        item(setup, R.drawable.ic_bell, R.color.blue, R.color.blueTint, getString(R.string.notify_title),
                getString(notify ? R.string.notify_ok : R.string.notify_missing),
                notify ? doneMark() : action(getString(R.string.allow), () -> open(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
                        .putExtra(Settings.EXTRA_APP_PACKAGE, getPackageName()))));

        // Počítače.
        List<String> clients = prefs.pairedClients();
        section(getString(R.string.section_pcs), clients.isEmpty() ? null : String.valueOf(clients.size()));
        LinearLayout pcs = card();
        if (clients.isEmpty()) {
            step(pcs, 1, getString(R.string.step1));
            step(pcs, 2, getString(R.string.step2));
            step(pcs, 3, getString(R.string.step3));
        } else {
            for (int i = 0; i < clients.size(); i++) {
                if (i > 0) divider(pcs);
                item(pcs, R.drawable.ic_monitor, R.color.accent, R.color.accentTint, clients.get(i), getString(R.string.paired), null);
            }
            TextView unpair = label(getString(R.string.unpair_all), 14, R.color.bad, MEDIUM);
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

        // Aktualizace.
        section(getString(R.string.section_update), null);
        LinearLayout upd = card();
        Updater.State us = Updater.state;
        String sub;
        View trailing = null;
        switch (us) {
            case CHECKING: sub = getString(R.string.upd_checking); break;
            case UP_TO_DATE: sub = getString(R.string.upd_latest); trailing = doneMark(); break;
            case AVAILABLE:
                sub = getString(R.string.upd_available, Updater.latest);
                trailing = action(getString(R.string.upd_button), () -> Updater.install(this));
                break;
            case DOWNLOADING: sub = getString(R.string.upd_downloading, Updater.progress); break;
            case INSTALLING: sub = getString(R.string.upd_installing); break;
            case FAILED:
                sub = getString(R.string.upd_failed);
                if (Updater.latest != null) trailing = action(getString(R.string.upd_button), () -> Updater.install(this));
                break;
            default: sub = getString(R.string.upd_idle);
        }
        View updRow = item(upd, R.drawable.ic_update, R.color.accent, R.color.accentTint,
                getString(R.string.version_title, Updater.current(this)), sub, trailing);
        updRow.setOnClickListener(v -> Updater.check(this, true));

        // Vzhled.
        section(getString(R.string.section_look), null);
        LinearLayout look = card();
        look.setPadding(px(6), px(6), px(6), px(6));
        LinearLayout seg = row();
        String current = prefs.theme();
        String[][] options = {{"system", getString(R.string.theme_system)}, {"light", getString(R.string.theme_light)},
                {"dark", getString(R.string.theme_dark)}};
        for (String[] o : options) {
            boolean sel = o[0].equals(current);
            TextView b = label(o[1], 14, sel ? R.color.onAccent : R.color.dim, MEDIUM);
            b.setGravity(Gravity.CENTER);
            b.setPadding(0, px(11), 0, px(11));
            b.setBackground(sel ? rounded(R.color.accent, 18) : new RippleDrawable(
                    ColorStateList.valueOf(color(R.color.line)), null, rounded(R.color.card, 18)));
            if (!sel) b.setOnClickListener(v -> {
                prefs.setTheme(o[0]);
                recreate();
            });
            seg.addView(b, new LinearLayout.LayoutParams(0, WRAP, 1));
        }
        look.addView(seg, new LinearLayout.LayoutParams(MATCH, WRAP));

        // Zpětná vazba.
        section(getString(R.string.section_feedback), null);
        LinearLayout fb = card();
        View fbRow = item(fb, R.drawable.ic_feedback, R.color.blue, R.color.blueTint,
                getString(R.string.fb_title), getString(R.string.fb_sub), chevron());
        fbRow.setOnClickListener(v -> showFeedback());

        TextView foot = label(getString(R.string.footer, versionName()), 12, R.color.faint, null);
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
        titles.addView(label(getString(R.string.tagline), 13, R.color.dim, null));
        h.addView(titles, new LinearLayout.LayoutParams(0, WRAP, 1));

        LinearLayout pill = row();
        pill.setPadding(px(12), px(7), px(14), px(7));
        pill.setBackground(rounded(running ? R.color.accentTint : R.color.line, 50));
        View dot = new View(this);
        dot.setBackground(oval(running ? R.color.accent : R.color.faint));
        pill.addView(dot, new LinearLayout.LayoutParams(px(8), px(8)));
        TextView t = label(getString(running ? R.string.status_receiving : on ? R.string.status_starting : R.string.status_off), 13, running ? R.color.accent : R.color.dim, MEDIUM);
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
            hero.addView(white(label(getString(R.string.hero_off_title), 22, 0, BOLD), 1f));
            hero.addView(white(label(getString(R.string.hero_off_text), 14, 0, null), 0.85f));
            TextView b = label(getString(R.string.hero_turn_on), 15, R.color.heroEnd, MEDIUM);
            b.setPadding(px(20), px(10), px(20), px(10));
            b.setBackground(new RippleDrawable(ColorStateList.valueOf(0x22000000), roundedRaw(0xFFFFFFFF, 50), null));
            b.setOnClickListener(v -> setEnabled(true));
            LinearLayout.LayoutParams bp = new LinearLayout.LayoutParams(WRAP, WRAP);
            bp.topMargin = px(16);
            hero.addView(b, bp);
            return;
        }

        LinearLayout top = row();
        TextView cap = white(label(getString(R.string.hero_pin).toUpperCase(), 12, 0, MEDIUM), 0.8f);
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

        hero.addView(white(label(getString(R.string.hero_hint), 14, 0, null), 0.88f));
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

    /** Dialog zpětné vazby: druh (čipy), zpráva, nepovinný e-mail; odeslání na pozadí. */
    private void showFeedback() {
        LinearLayout box = column();
        box.setPadding(px(22), px(8), px(22), 0);
        String[] codes = {"bug", "idea", "question", "other"};
        int[] names = {R.string.fb_bug, R.string.fb_idea, R.string.fb_question, R.string.fb_other};
        String[] kind = {"bug"};
        LinearLayout chips = row();
        TextView[] chipViews = new TextView[codes.length];
        Runnable paint = () -> {
            for (int i = 0; i < codes.length; i++) {
                boolean sel = codes[i].equals(kind[0]);
                chipViews[i].setTextColor(color(sel ? R.color.onAccent : R.color.dim));
                chipViews[i].setBackground(rounded(sel ? R.color.accent : R.color.line, 50));
            }
        };
        for (int i = 0; i < codes.length; i++) {
            int n = i;
            TextView c = label(getString(names[i]), 14, R.color.dim, MEDIUM);
            c.setPadding(px(14), px(7), px(14), px(7));
            c.setOnClickListener(v -> { kind[0] = codes[n]; paint.run(); });
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(WRAP, WRAP);
            lp.rightMargin = px(6);
            chips.addView(c, lp);
            chipViews[i] = c;
        }
        paint.run();
        android.widget.HorizontalScrollView chipScroll = new android.widget.HorizontalScrollView(this);
        chipScroll.setHorizontalScrollBarEnabled(false);
        chipScroll.addView(chips);
        box.addView(chipScroll);

        android.widget.EditText message = new android.widget.EditText(this);
        message.setHint(R.string.fb_message_hint);
        message.setMinLines(4);
        message.setGravity(Gravity.TOP | Gravity.START);
        message.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE
                | android.text.InputType.TYPE_TEXT_FLAG_CAP_SENTENCES);
        message.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(5000)});
        LinearLayout.LayoutParams mlp = new LinearLayout.LayoutParams(MATCH, WRAP);
        mlp.topMargin = px(12);
        box.addView(message, mlp);

        android.widget.EditText email = new android.widget.EditText(this);
        email.setHint(R.string.fb_email_hint);
        email.setSingleLine(true);
        email.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);
        box.addView(email, new LinearLayout.LayoutParams(MATCH, WRAP));

        // Snímky obrazovky: systémový výběr fotek (bez oprávnění), náhledy s křížkem.
        fbImages.clear();
        LinearLayout shots = row();
        shots.setGravity(Gravity.CENTER_VERTICAL);
        TextView add = label("+  " + getString(R.string.fb_add_image), 14, R.color.accent, MEDIUM);
        add.setPadding(px(14), px(8), px(14), px(8));
        add.setBackground(rounded(R.color.accentTint, 50));
        shots.addView(add);
        fbThumbs = row();
        android.widget.HorizontalScrollView thumbScroll = new android.widget.HorizontalScrollView(this);
        thumbScroll.setHorizontalScrollBarEnabled(false);
        thumbScroll.addView(fbThumbs);
        LinearLayout.LayoutParams tlp = new LinearLayout.LayoutParams(0, WRAP, 1);
        tlp.leftMargin = px(10);
        shots.addView(thumbScroll, tlp);
        LinearLayout.LayoutParams slp = new LinearLayout.LayoutParams(MATCH, WRAP);
        slp.topMargin = px(12);
        box.addView(shots, slp);

        TextView status = label(getString(R.string.fb_privacy), 13, R.color.dim, null);
        status.setPadding(0, px(8), 0, 0);
        box.addView(status);
        fbStatus = status;
        add.setOnClickListener(v -> {
            if (fbImages.size() >= Feedback.MAX_IMAGES) {
                status.setTextColor(color(R.color.bad));
                status.setText(R.string.fb_images_max);
                return;
            }
            pickScreenshots();
        });

        android.app.AlertDialog dlg = new android.app.AlertDialog.Builder(this)
                .setTitle(R.string.fb_title)
                .setView(box)
                .setPositiveButton(R.string.fb_send, null)
                .setNegativeButton(R.string.fb_cancel, null)
                .create();
        dlg.setOnShowListener(d -> dlg.getButton(android.app.AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            String text = message.getText().toString().trim();
            String mail = email.getText().toString().trim();
            status.setTextColor(color(R.color.bad));
            if (text.length() < 3) { status.setText(R.string.fb_too_short); return; }
            if (!mail.isEmpty() && !android.util.Patterns.EMAIL_ADDRESS.matcher(mail).matches()) {
                status.setText(R.string.fb_bad_email);
                return;
            }
            v.setEnabled(false);
            status.setTextColor(color(R.color.dim));
            status.setText(R.string.fb_sending);
            new Thread(() -> {
                String error = null;
                try {
                    Feedback.send(this, kind[0], text, mail, new java.util.ArrayList<>(fbImages));
                } catch (Feedback.Failed f) {
                    error = f.code == 0 ? getString(R.string.fb_offline)
                            : f.code == 429 ? getString(R.string.fb_too_many)
                            : f.code == 413 || f.attachment ? getString(R.string.fb_image_rejected)
                            : getString(R.string.fb_failed, f.code);
                } catch (java.io.IOException e) {
                    error = getString(R.string.fb_offline);
                }
                String err = error;
                runOnUiThread(() -> {
                    if (err == null) {
                        dlg.dismiss();
                        android.widget.Toast.makeText(this, R.string.fb_thanks, android.widget.Toast.LENGTH_LONG).show();
                    } else {
                        v.setEnabled(true);
                        status.setTextColor(color(R.color.bad));
                        status.setText(err);
                    }
                });
            }, "phonedrop-feedback").start();
        }));
        dlg.show();
    }

    /** Oprávnění k oznámením existuje až od Androidu 13; dřív jsou oznámení povolená vždy. */
    private boolean notificationsAllowed() {
        return android.os.Build.VERSION.SDK_INT < 33
                || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
    }

    private static final int REQ_SEND = 41;
    private static final int REQ_SHOT = 42;

    // Zpětná vazba: obrázky v otevřeném dialogu.
    private final java.util.List<Feedback.Image> fbImages = new java.util.ArrayList<>();
    private LinearLayout fbThumbs;
    private TextView fbStatus;

    private void pickScreenshots() {
        int left = Feedback.MAX_IMAGES - fbImages.size();
        Intent pick;
        if (android.os.Build.VERSION.SDK_INT >= 33) {
            pick = new Intent(android.provider.MediaStore.ACTION_PICK_IMAGES);
            if (left > 1) pick.putExtra(android.provider.MediaStore.EXTRA_PICK_IMAGES_MAX, left);
        } else {
            pick = new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE)
                    .setType("image/*").putExtra(Intent.EXTRA_ALLOW_MULTIPLE, left > 1);
        }
        try {
            startActivityForResult(pick, REQ_SHOT);
        } catch (android.content.ActivityNotFoundException ignored) {
        }
    }

    /** Zmenší a překóduje vybrané obrázky na pozadí a přidá náhledy do dialogu. */
    private void addScreenshots(java.util.List<android.net.Uri> uris) {
        new Thread(() -> {
            for (android.net.Uri u : uris) {
                Feedback.Image img;
                try {
                    img = Feedback.Image.from(this, u);
                } catch (Exception e) {
                    runOnUiThread(() -> {
                        if (fbStatus == null) return;
                        fbStatus.setTextColor(color(R.color.bad));
                        fbStatus.setText(R.string.fb_image_bad);
                    });
                    continue;
                }
                runOnUiThread(() -> {
                    if (fbThumbs == null || fbImages.size() >= Feedback.MAX_IMAGES) return;
                    fbImages.add(img);
                    android.widget.FrameLayout frame = new android.widget.FrameLayout(this);
                    android.widget.ImageView iv = new android.widget.ImageView(this);
                    iv.setImageBitmap(img.thumb);
                    iv.setScaleType(android.widget.ImageView.ScaleType.CENTER_CROP);
                    iv.setBackground(rounded(R.color.line, 10));
                    iv.setClipToOutline(true);
                    frame.addView(iv, new android.widget.FrameLayout.LayoutParams(px(52), px(52)));
                    TextView x = label("✕", 11, R.color.onAccent, MEDIUM);
                    x.setGravity(Gravity.CENTER);
                    x.setBackground(rounded(R.color.bad, 50));
                    x.setContentDescription(getString(R.string.fb_image_remove));
                    android.widget.FrameLayout.LayoutParams xlp = new android.widget.FrameLayout.LayoutParams(px(20), px(20), Gravity.TOP | Gravity.END);
                    frame.addView(x, xlp);
                    LinearLayout.LayoutParams flp = new LinearLayout.LayoutParams(WRAP, WRAP);
                    flp.rightMargin = px(8);
                    fbThumbs.addView(frame, flp);
                    x.setOnClickListener(v -> {
                        fbImages.remove(img);
                        fbThumbs.removeView(frame);
                    });
                });
            }
        }, "phonedrop-shots").start();
    }

    /** Výběr souborů z telefonu → stejná cesta jako Sdílet › PhoneDrop. */
    private void pickToSend() {
        Intent pick = new Intent(Intent.ACTION_OPEN_DOCUMENT)
                .addCategory(Intent.CATEGORY_OPENABLE)
                .setType("*/*")
                .putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        try {
            startActivityForResult(pick, REQ_SEND);
        } catch (android.content.ActivityNotFoundException ignored) {
        }
    }

    @Override
    @SuppressWarnings("deprecation")
    protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request == REQ_SHOT && result == RESULT_OK && data != null) {
            java.util.List<android.net.Uri> shots = new java.util.ArrayList<>();
            if (data.getClipData() != null) {
                for (int i = 0; i < data.getClipData().getItemCount(); i++) shots.add(data.getClipData().getItemAt(i).getUri());
            } else if (data.getData() != null) {
                shots.add(data.getData());
            }
            int left = Feedback.MAX_IMAGES - fbImages.size();
            if (shots.size() > left) shots = shots.subList(0, Math.max(0, left));
            addScreenshots(shots);
            return;
        }
        if (request != REQ_SEND || result != RESULT_OK || data == null) return;
        java.util.ArrayList<android.net.Uri> uris = new java.util.ArrayList<>();
        if (data.getClipData() != null) {
            for (int i = 0; i < data.getClipData().getItemCount(); i++) uris.add(data.getClipData().getItemAt(i).getUri());
        } else if (data.getData() != null) {
            uris.add(data.getData());
        }
        if (uris.isEmpty()) return;
        android.content.ClipData clip = android.content.ClipData.newRawUri("", uris.get(0));
        for (int i = 1; i < uris.size(); i++) clip.addItem(new android.content.ClipData.Item(uris.get(i)));
        Intent share = new Intent(this, ShareActivity.class)
                .setAction(Intent.ACTION_SEND_MULTIPLE)
                .putParcelableArrayListExtra(Intent.EXTRA_STREAM, uris)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        share.setClipData(clip);
        startActivity(share);
    }

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
