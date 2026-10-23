package com.linkview.app.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Devices
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Link
import androidx.compose.material.icons.rounded.ScreenShare
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.linkview.app.LinkViewApplication
import com.linkview.app.session.SessionState
import com.linkview.app.ui.screens.DevicesScreen
import com.linkview.app.ui.screens.DiagnosticsScreen
import com.linkview.app.ui.screens.HomeScreen
import com.linkview.app.ui.screens.LogsScreen
import com.linkview.app.ui.screens.PairScreen
import com.linkview.app.ui.screens.SessionScreen
import com.linkview.app.ui.screens.SettingsScreen

private enum class Destination(val key: String, val label: String, val icon: ImageVector) {
    Home("home", "首页", Icons.Rounded.Home),
    Devices("devices", "设备", Icons.Rounded.Devices),
    Pair("pair", "连接", Icons.Rounded.Link),
    Session("session", "共享", Icons.Rounded.ScreenShare),
    Settings("settings", "设置", Icons.Rounded.Settings)
}

@Composable
fun LinkViewRoot(
    app: LinkViewApplication,
    onRequestScreenShare: () -> Unit,
    onStopScreenShare: () -> Unit
) {
    val state by app.session.state.collectAsStateWithLifecycle()
    var destination by rememberSaveable { mutableStateOf(Destination.Home.key) }
    val motion = LocalMotionEnabled.current

    LaunchedEffect(state.state) {
        if (state.state in setOf(
                SessionState.Negotiating,
                SessionState.Connecting,
                SessionState.Connected,
                SessionState.Sharing,
                SessionState.Reconnecting
            ) && destination == Destination.Pair.key
        ) {
            destination = Destination.Session.key
        }
    }

    BackHandler(enabled = destination != Destination.Home.key) {
        destination = Destination.Home.key
    }

    if (state.pendingGuest != null && state.state == SessionState.AwaitingConsent) {
        AlertDialog(
            onDismissRequest = { },
            title = { Text("收到连接请求") },
            text = {
                Text(
                    "${state.pendingGuest?.deviceName ?: "未知设备"} 请求加入你的会话。" +
                        "\n\n只有在你确认对方身份后才应允许连接。"
                )
            },
            confirmButton = {
                TextButton(onClick = { app.session.acceptJoin() }) { Text("允许连接") }
            },
            dismissButton = {
                TextButton(onClick = { app.session.rejectJoin() }) { Text("拒绝") }
            }
        )
    }

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        bottomBar = {
            NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                Destination.entries.forEach { item ->
                    val selected = destination == item.key ||
                        (item == Destination.Settings && destination in setOf("diagnostics", "logs"))
                    NavigationBarItem(
                        selected = selected,
                        onClick = { destination = item.key },
                        icon = { Icon(item.icon, contentDescription = item.label) },
                        label = { Text(item.label, style = MaterialTheme.typography.labelSmall) }
                    )
                }
            }
        }
    ) { innerPadding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding)
                .statusBarsPadding()
                .background(MaterialTheme.colorScheme.background)
        ) {
            val contentModifier = Modifier.fillMaxSize()
            AnimatedContent(
                targetState = destination,
                transitionSpec = {
                    if (motion) {
                        fadeIn(tween(180)) togetherWith fadeOut(tween(120))
                    } else {
                        fadeIn(tween(0)) togetherWith fadeOut(tween(0))
                    }
                },
                label = "screen",
                modifier = contentModifier
            ) { screen ->
                when (screen) {
                    Destination.Home.key -> HomeScreen(
                        app = app,
                        state = state,
                        onNavigate = { destination = it }
                    )
                    Destination.Devices.key -> DevicesScreen(
                        app = app,
                        onNavigate = { destination = it }
                    )
                    Destination.Pair.key -> PairScreen(
                        app = app,
                        state = state,
                        onNavigate = { destination = it }
                    )
                    Destination.Session.key -> SessionScreen(
                        app = app,
                        state = state,
                        onRequestScreenShare = onRequestScreenShare,
                        onStopScreenShare = onStopScreenShare,
                        onDisconnect = { app.session.disconnect() },
                        onNavigate = { destination = it }
                    )
                    Destination.Settings.key -> SettingsScreen(
                        app = app,
                        onNavigate = { destination = it }
                    )
                    "diagnostics" -> DiagnosticsScreen(app = app, onBack = { destination = Destination.Settings.key })
                    "logs" -> LogsScreen(app = app, onBack = { destination = Destination.Settings.key })
                    else -> HomeScreen(app = app, state = state, onNavigate = { destination = it })
                }
            }
        }
    }
}
