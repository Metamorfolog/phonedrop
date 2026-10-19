package cz.metamorfolog.phonedrop;

import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.LinearGradient;
import android.graphics.Outline;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.drawable.Drawable;

/** Pozadí karty s PINem: zelený přechod se dvěma průsvitnými kruhy. */
final class HeroDrawable extends Drawable {
    private final int start, end, glow;
    private final float radius;
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path clip = new Path();
    private final RectF rect = new RectF();

    HeroDrawable(int start, int end, int glow, float radius) {
        this.start = start;
        this.end = end;
        this.glow = glow;
        this.radius = radius;
    }

    @Override
    protected void onBoundsChange(Rect b) {
        rect.set(b);
        clip.reset();
        clip.addRoundRect(rect, radius, radius, Path.Direction.CW);
    }

    @Override
    public void draw(Canvas c) {
        float w = rect.width(), h = rect.height();
        paint.setShader(new LinearGradient(rect.left, rect.top, rect.right, rect.bottom, start, end, Shader.TileMode.CLAMP));
        c.drawPath(clip, paint);
        paint.setShader(null);
        c.save();
        c.clipPath(clip);
        paint.setColor(0x1FFFFFFF);
        c.drawCircle(rect.left + w * 0.95f, rect.top + h * 0.02f, h * 0.72f, paint);
        paint.setColor(0x14FFFFFF);
        c.drawCircle(rect.left + w * 0.95f, rect.top + h * 0.02f, h * 0.46f, paint);
        paint.setColor((glow & 0x00FFFFFF) | 0x33000000);
        c.drawCircle(rect.left + w * 0.70f, rect.bottom + h * 0.10f, h * 0.38f, paint);
        c.restore();
    }

    @Override
    public void getOutline(Outline outline) {
        outline.setRoundRect(getBounds(), radius);
    }

    @Override public void setAlpha(int alpha) { }
    @Override public void setColorFilter(ColorFilter cf) { }
    @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
}
