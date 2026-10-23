package com.linkview.app

import android.app.Application
import com.linkview.app.data.IdentityStore
import com.linkview.app.data.LogStore
import com.linkview.app.data.SettingsStore
import com.linkview.app.diagnostics.Diagnostics
import com.linkview.app.net.DiscoveryService
import com.linkview.app.session.SessionManager
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.webrtc.PeerConnectionFactory

class LinkViewApplication : Application() {
    lateinit var settings: SettingsStore
        private set
    lateinit var identity: IdentityStore
        private set
    lateinit var logs: LogStore
        private set
    lateinit var session: SessionManager
        private set
    lateinit var discovery: DiscoveryService
        private set
    lateinit var diagnostics: Diagnostics
        private set

    val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun onCreate() {
        super.onCreate()
        PeerConnectionFactory.initialize(
            PeerConnectionFactory.InitializationOptions.builder(this)
                .setEnableInternalTracer(false)
                .createInitializationOptions()
        )
        settings = SettingsStore(this)
        identity = IdentityStore(this)
        logs = LogStore()
        session = SessionManager(this, settings, identity, logs, appScope)
        discovery = DiscoveryService(this, settings, identity, logs, appScope)
        diagnostics = Diagnostics(this, settings, logs)
        logs.info("App", "LinkView 已启动，设备 ${identity.identity.deviceId.take(10)}…")
        if (settings.settings.value.lanDiscovery) discovery.start()
    }
}
