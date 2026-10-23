package com.linkview.app.media

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import com.linkview.app.LinkViewApplication
import com.linkview.app.MainActivity
import com.linkview.app.R

/**
 * 持有 MediaProjection 所需的前台服务。
 * Android 14 起，屏幕共享必须在 mediaProjection 类型的前台服务中运行。
 * 采集器由 WebRtcClient 创建并绑定到当前会话；服务保证授权与通知的合规性。
 */
class ScreenShareService : Service() {

    companion object {
        const val ACTION_START = "com.linkview.app.action.START_SHARE"
        const val ACTION_STOP = "com.linkview.app.action.STOP_SHARE"
        const val EXTRA_RESULT_DATA = "projection_data"
        private const val CHANNEL_ID = "linkview.screen"
        private const val NOTIFICATION_ID = 4211

        fun startIntent(context: Context, projectionData: Intent): Intent =
            Intent(context, ScreenShareService::class.java).apply {
                action = ACTION_START
                putExtra(EXTRA_RESULT_DATA, projectionData)
            }

        fun stopIntent(context: Context): Intent =
            Intent(context, ScreenShareService::class.java).apply { action = ACTION_STOP }
    }

    private val app: LinkViewApplication get() = application as LinkViewApplication

    override fun onCreate() {
        super.onCreate()
        createChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            app.session.stopSharing()
            stopSelf()
            return START_NOT_STICKY
        }

        startForeground(NOTIFICATION_ID, buildNotification())

        @Suppress("DEPRECATION")
        val projectionData = intent?.getParcelableExtra(EXTRA_RESULT_DATA) as? Intent
        if (projectionData == null) {
            app.logs.error("ScreenShareService", "缺少 MediaProjection 授权数据")
            stopSelf()
            return START_NOT_STICKY
        }

        app.session.attachScreenCapture(projectionData)
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        app.session.stopSharing()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(NotificationManager::class.java)
            val channel = NotificationChannel(
                CHANNEL_ID,
                getString(R.string.notification_channel_name),
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = getString(R.string.notification_channel_desc)
                setShowBadge(false)
            }
            manager.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(): Notification {
        val openIntent = PendingIntent.getActivity(
            this,
            0,
            Intent(this, MainActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            },
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val stopPendingIntent = PendingIntent.getService(
            this,
            1,
            stopIntent(this),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_launcher)
            .setContentTitle(getString(R.string.notification_title))
            .setContentText(getString(R.string.notification_text))
            .setContentIntent(openIntent)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .addAction(0, getString(R.string.notification_stop), stopPendingIntent)
            .build()
    }
}
