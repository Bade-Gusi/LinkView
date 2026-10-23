package com.linkview.app.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.linkview.app.data.ThemeMode

val LocalMotionEnabled = staticCompositionLocalOf { true }

private val LightColors = lightColorScheme(
    primary = Color(0xFF2F6FEB),
    onPrimary = Color.White,
    primaryContainer = Color(0xFFE4EDFC),
    onPrimaryContainer = Color(0xFF16325E),
    secondary = Color(0xFF0F8A5F),
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFE1F4EC),
    onSecondaryContainer = Color(0xFF0A3D2B),
    background = Color(0xFFF4F6F9),
    onBackground = Color(0xFF16202C),
    surface = Color(0xFFFFFFFF),
    onSurface = Color(0xFF16202C),
    surfaceVariant = Color(0xFFEDF1F6),
    onSurfaceVariant = Color(0xFF576678),
    outline = Color(0xFFC6D0DD),
    outlineVariant = Color(0xFFDDE3EB),
    error = Color(0xFFC23B3B),
    errorContainer = Color(0xFFFBE9E9),
    onErrorContainer = Color(0xFF5B1616),
    tertiary = Color(0xFFA3650A),
    tertiaryContainer = Color(0xFFFBF0DC)
)

private val DarkColors = darkColorScheme(
    primary = Color(0xFF6AAEFF),
    onPrimary = Color(0xFF0A1522),
    primaryContainer = Color(0xFF1A2B42),
    onPrimaryContainer = Color(0xFFD7E6FF),
    secondary = Color(0xFF3DDC97),
    onSecondary = Color(0xFF06251B),
    secondaryContainer = Color(0xFF14302A),
    onSecondaryContainer = Color(0xFFB8F4DE),
    background = Color(0xFF0D1117),
    onBackground = Color(0xFFE9EEF5),
    surface = Color(0xFF151B23),
    onSurface = Color(0xFFE9EEF5),
    surfaceVariant = Color(0xFF1A222C),
    onSurfaceVariant = Color(0xFF97A6B8),
    outline = Color(0xFF354454),
    outlineVariant = Color(0xFF26313E),
    error = Color(0xFFFF7A7A),
    errorContainer = Color(0xFF351D20),
    onErrorContainer = Color(0xFFFFD9D9),
    tertiary = Color(0xFFF3C15C),
    tertiaryContainer = Color(0xFF332A18)
)

private val LinkTypography = Typography(
    displaySmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 30.sp,
        lineHeight = 36.sp,
        letterSpacing = (-0.4).sp
    ),
    headlineMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 23.sp,
        lineHeight = 29.sp,
        letterSpacing = (-0.2).sp
    ),
    headlineSmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 19.sp,
        lineHeight = 25.sp
    ),
    titleMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.SemiBold,
        fontSize = 15.sp,
        lineHeight = 21.sp
    ),
    titleSmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 13.sp,
        lineHeight = 18.sp
    ),
    bodyLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 15.sp,
        lineHeight = 22.sp
    ),
    bodyMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 13.5.sp,
        lineHeight = 20.sp
    ),
    bodySmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Normal,
        fontSize = 12.sp,
        lineHeight = 17.sp
    ),
    labelLarge = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 14.sp,
        lineHeight = 18.sp
    ),
    labelMedium = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 12.sp,
        lineHeight = 16.sp
    ),
    labelSmall = TextStyle(
        fontFamily = FontFamily.SansSerif,
        fontWeight = FontWeight.Medium,
        fontSize = 11.sp,
        lineHeight = 14.sp
    )
)

private val LinkShapes = Shapes(
    extraSmall = androidx.compose.foundation.shape.RoundedCornerShape(6.dp),
    small = androidx.compose.foundation.shape.RoundedCornerShape(10.dp),
    medium = androidx.compose.foundation.shape.RoundedCornerShape(14.dp),
    large = androidx.compose.foundation.shape.RoundedCornerShape(18.dp),
    extraLarge = androidx.compose.foundation.shape.RoundedCornerShape(24.dp)
)

@Composable
fun LinkViewTheme(
    themeMode: ThemeMode,
    animations: Boolean,
    content: @Composable () -> Unit
) {
    val dark = when (themeMode) {
        ThemeMode.System -> isSystemInDarkTheme()
        ThemeMode.Light -> false
        ThemeMode.Dark -> true
    }
    MaterialTheme(
        colorScheme = if (dark) DarkColors else LightColors,
        typography = LinkTypography,
        shapes = LinkShapes
    ) {
        CompositionLocalProvider(LocalMotionEnabled provides animations) {
            content()
        }
    }
}
