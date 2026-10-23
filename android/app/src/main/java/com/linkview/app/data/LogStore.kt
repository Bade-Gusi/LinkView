package com.linkview.app.data

import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

data class LogEntry(
    val time: Long,
    val level: String,
    val tag: String,
    val message: String
)

class LogStore {
    private val _entries = MutableStateFlow<List<LogEntry>>(emptyList())
    val entries: StateFlow<List<LogEntry>> = _entries

    fun info(tag: String, message: String) = add("INFO", tag, message)

    fun warn(tag: String, message: String) = add("WARN", tag, message)

    fun error(tag: String, message: String, throwable: Throwable? = null) {
        Log.e("LinkView", message, throwable)
        add("ERROR", tag, if (throwable == null) message else "$message · ${throwable.message}")
    }

    private fun add(level: String, tag: String, message: String) {
        val priority = when (level) {
            "ERROR" -> Log.ERROR
            "WARN" -> Log.WARN
            else -> Log.INFO
        }
        Log.println(priority, "LinkView", "[$tag] $message")
        val sanitized = sanitize(message)
        _entries.value = (_entries.value + LogEntry(System.currentTimeMillis(), level, tag, sanitized)).takeLast(600)
    }

    private fun sanitize(value: String): String = value
        .replace(
            Regex("(credential|password|secret|token)\\s*[=:]\\s*\\S+", RegexOption.IGNORE_CASE),
            "$1=***"
        )
        .replace(
            Regex("\"pairingCode\"\\s*:\\s*\"[^\"]*\"", RegexOption.IGNORE_CASE),
            "\"pairingCode\":\"***\""
        )

    fun clear() {
        _entries.value = emptyList()
    }
}
