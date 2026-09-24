package com.gamedistrict.perftracker;

import android.app.Activity;
import android.app.Application;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.os.SystemClock;

/**
 * Tracks how long the app has been in background (no Activity started) since
 * process start, so GDStartupTime can subtract it from cold-start / app-load
 * readings. SystemClock.uptimeMillis() keeps ticking while backgrounded, so a
 * user who launches, hits home and comes back later would otherwise inflate both.
 * ponytail: backgrounding in the ~100ms before the first Activity starts isn't seen
 * (Android defers the Activity until return) — faster than a human can hit home.
 * It does no real provider work; a ContentProvider is used only for its early onCreate().
 */
public final class GDStartupProvider extends ContentProvider {
    private static final Object LOCK = new Object();
    private static int startedActivities;
    private static long backgroundSince = -1;   // uptimeMillis when backgrounded, -1 = foreground
    private static long backgroundTotal;

    /** Background ms since process start, including an ongoing background stretch. Called from C#. */
    public static long getBackgroundMillis() {
        synchronized (LOCK) {
            return backgroundTotal + (backgroundSince >= 0 ? SystemClock.uptimeMillis() - backgroundSince : 0);
        }
    }

    @Override
    public boolean onCreate() {
        ((Application) getContext().getApplicationContext()).registerActivityLifecycleCallbacks(
            new Application.ActivityLifecycleCallbacks() {
                @Override public void onActivityStarted(Activity a) {
                    synchronized (LOCK) {
                        if (startedActivities++ == 0 && backgroundSince >= 0) {
                            backgroundTotal += SystemClock.uptimeMillis() - backgroundSince;
                            backgroundSince = -1;
                        }
                    }
                }
                @Override public void onActivityStopped(Activity a) {
                    synchronized (LOCK) {
                        // Rotation stops then restarts an Activity — not a trip to background.
                        if (--startedActivities == 0 && !a.isChangingConfigurations())
                            backgroundSince = SystemClock.uptimeMillis();
                    }
                }
                @Override public void onActivityCreated(Activity a, Bundle b) {}
                @Override public void onActivityResumed(Activity a) {}
                @Override public void onActivityPaused(Activity a) {}
                @Override public void onActivitySaveInstanceState(Activity a, Bundle b) {}
                @Override public void onActivityDestroyed(Activity a) {}
            });
        return true;
    }

    @Override public Cursor query(Uri u, String[] p, String s, String[] a, String o) { return null; }
    @Override public String getType(Uri u) { return null; }
    @Override public Uri insert(Uri u, ContentValues v) { return null; }
    @Override public int delete(Uri u, String s, String[] a) { return 0; }
    @Override public int update(Uri u, ContentValues v, String s, String[] a) { return 0; }
}
