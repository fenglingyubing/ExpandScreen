package com.expandscreen.ui.qr

data class QrConnectInfo(
    val host: String,
    val port: Int,
    val token: String? = null,
    val deviceName: String? = null,
)

