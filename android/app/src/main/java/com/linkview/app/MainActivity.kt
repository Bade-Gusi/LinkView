package com.linkview.app

import androidx.compose.runtime.getValue

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import com.linkview.app.media.ScreenShareService
import com.linkview.app.ui.LinkViewRoot
import com.linkview.app.ui.LinkViewTheme
import androidx.lifecycle.compose.collectAsStateWithLifecycle

class MainActivity : ComponentActivity() {

    private lateinit var projectionManager: MediaProjectionManager
    private var pendingProjection = false

    private val notificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) {
        if (pendingProjection) {
            pendingProjection = false
            launchProjection()
        }
    }

    private val projectionLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK && result.data != null) {
            val serviceIntent = ScreenShareService.startIntent(this, result.data!!)
            ContextCompat.startForegroundService(this, serviceIntent)
        } else {
            (application as LinkViewApplication).logs.warn("Main", "用户取消了屏幕共享授权")
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        projectionManager = getSystemService(MediaProjectionManager::class.java)

        val app = application as LinkViewApplication
        setContent {
            val settings by app.settings.settings.collectAsStateWithLifecycle()
            LinkViewTheme(settings.theme, settings.animations) {
                LinkViewRoot(
                    app = app,
                    onRequestScreenShare = ::requestScreenShare,
                    onStopScreenShare = ::stopScreenShare
                )
            }
        }
    }

    private fun requestScreenShare() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            pendingProjection = true
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
            return
        }
        launchProjection()
    }

    private fun launchProjection() {
        projectionLauncher.launch(projectionManager.createScreenCaptureIntent())
    }

    private fun stopScreenShare() {
        stopService(Intent(this, ScreenShareService::class.java))
        (application as LinkViewApplication).session.stopSharing()
    }
}
