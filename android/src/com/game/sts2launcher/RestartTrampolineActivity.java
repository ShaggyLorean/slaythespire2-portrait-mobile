package com.game.sts2launcher;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

// Lives in its own ":phoenix" process. The main process cannot relaunch
// itself on modern Android: startActivity followed by exit races the system,
// and a PendingIntent fired after process death hits the background-launch
// restriction (observed on Android 16 / OnePlus: the app simply closed).
// This activity survives the main process exit, relaunches the app from the
// foreground where no restriction applies, then removes itself.
public class RestartTrampolineActivity extends Activity {
	private static final String TAG = "StS2Phoenix";
	private static final long RELAUNCH_DELAY_MS = 250;

	@Override
	protected void onCreate(Bundle savedInstanceState) {
		super.onCreate(savedInstanceState);
		Log.i(TAG, "Trampoline up; relaunching main activity shortly");
		new Handler(Looper.getMainLooper()).postDelayed(this::relaunch, RELAUNCH_DELAY_MS);
	}

	private void relaunch() {
		Intent intent = getPackageManager().getLaunchIntentForPackage(getPackageName());
		if (intent != null) {
			intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
			startActivity(intent);
		} else {
			Log.e(TAG, "No launch intent available");
		}
		finish();
		new Handler(Looper.getMainLooper()).postDelayed(
				() -> android.os.Process.killProcess(android.os.Process.myPid()), 400);
	}
}
