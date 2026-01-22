# ExpandScreen Android Client

Android client application for the ExpandScreen project - extend your Windows display to an Android tablet.

## 📱 Overview

The ExpandScreen Android client receives video streams from a Windows PC and displays them in full-screen mode, allowing your Android device to function as an extended display. It supports both USB and WiFi connections with touch input feedback.

## ✨ Features

- **Real-time Display Streaming**: Low-latency video decoding and rendering using MediaCodec and OpenGL ES
- **Dual Connection Modes**: Connect via USB (lower latency) or WiFi (wireless convenience)
- **Touch Input**: Send touch events back to Windows for interactive control
- **Device Management**: Save and manage multiple Windows PCs
- **Performance Monitoring**: Real-time FPS and latency display
- **Customizable Settings**: Adjust resolution, frame rate, and quality presets
- **Material Design 3**: Modern, beautiful UI with dynamic theming

## 🏗️ Project Architecture

```
app/src/main/kotlin/com/expandscreen/
├── ui/                     # Jetpack Compose UI layer
│   ├── screens/           # Screen composables
│   ├── components/        # Reusable UI components
│   ├── theme/            # Material Design theme
│   ├── navigation/       # Navigation graph
│   ├── MainActivity.kt   # Main entry activity
│   └── DisplayActivity.kt # Full-screen display activity
├── core/                  # Core business logic
│   ├── decoder/          # Video decoder (MediaCodec)
│   ├── renderer/         # OpenGL ES renderer
│   └── network/          # Network communication
├── service/              # Background services
│   └── DisplayService.kt # Foreground service for streaming
├── data/                 # Data layer
│   ├── database/         # Room database
│   ├── repository/       # Repository pattern
│   └── model/           # Data models
├── protocol/             # Communication protocol
│   └── Messages.kt      # Message definitions
├── di/                   # Dependency injection (Hilt)
└── utils/               # Utility classes
```

## 🔧 Tech Stack

### Core Technologies
- **Kotlin** - Modern, concise Android development
- **Jetpack Compose** - Declarative UI framework
- **Material Design 3** - Latest design system with dynamic theming

### Architecture & Libraries
- **Hilt** - Dependency injection
- **Room** - Local database for device history
- **Kotlin Coroutines + Flow** - Asynchronous programming
- **OkHttp** - Network communication
- **Timber** - Logging

### Media & Rendering
- **MediaCodec** - Hardware-accelerated H.264 video decoding
- **OpenGL ES** - GPU-accelerated video rendering
- **SurfaceTexture** - Efficient video frame handling

## 📋 Requirements

- **Minimum SDK**: Android 8.0 (API 26)
- **Target SDK**: Android 14 (API 34)
- **Recommended**: Android 10+ for best performance
- **Hardware**: 2GB+ RAM, hardware H.264 decoder support

## 🚀 Getting Started

### Prerequisites

1. Install Android Studio (Arctic Fox or later)
2. Install Android SDK 34
3. Install JDK 17

### Build & Run

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd ExpandScreen/android-client
   ```

2. Open the project in Android Studio

3. Sync Gradle:
   ```bash
   ./gradlew build
   ```

4. Run on device or emulator:
   - Click "Run" in Android Studio, or
   - Use command line: `./gradlew installDebug`

### Code Quality

Run ktlint for code formatting:
```bash
./gradlew ktlintCheck
./gradlew ktlintFormat
```

Run tests:
```bash
./gradlew test
./gradlew connectedAndroidTest
```

## 📦 Building Release APK

1. Generate release keystore (first time only):
   ```bash
   keytool -genkey -v -keystore expandscreen-release.keystore \
     -alias expandscreen -keyalg RSA -keysize 2048 -validity 10000
   ```

2. Create `keystore.properties` in project root:
   ```properties
   storeFile=expandscreen-release.keystore
   storePassword=<your-password>
   keyAlias=expandscreen
   keyPassword=<your-key-password>
   ```

3. Build release APK:
   ```bash
   ./gradlew assembleRelease
   ```

Output: `app/build/outputs/apk/release/app-release.apk`

## 🎨 UI/UX Design

The app follows Material Design 3 principles with:
- Dynamic color theming (Android 12+)
- Dark/Light theme support
- Smooth animations and transitions
- Responsive layouts for tablets
- Immersive full-screen display mode

## 🔌 Connection Flow

1. **USB Connection**:
   - Connect Android device to Windows PC via USB
   - Enable USB debugging on Android
   - Windows PC forwards TCP port via ADB
   - Android app connects to localhost

2. **WiFi Connection**:
   - Ensure both devices are on same network
   - Windows PC broadcasts discovery message
   - Android app displays available PCs
   - Select PC to connect

## 📡 Protocol

Communication uses a custom binary protocol over TCP:
- **Message Header**: 24 bytes (magic, version, type, sequence, timestamp, length)
- **Payload**: Variable length (JSON or Protocol Buffers)
- **Message Types**: Handshake, Video Frame, Audio Frame, Touch Event, etc.

See `protocol/Messages.kt` for full definitions.

## 🧪 Testing

### Unit Tests
```bash
./gradlew test
```

### Instrumented Tests
```bash
./gradlew connectedAndroidTest
```

### Manual Testing Checklist
- [ ] USB connection establishes successfully
- [ ] WiFi connection establishes successfully
- [ ] Video plays smoothly at 60fps
- [ ] Touch input works accurately
- [ ] App handles reconnection gracefully
- [ ] Settings persist correctly
- [ ] Notifications display properly

## 🐛 Debugging

Enable debug logging:
```kotlin
// In ExpandScreenApplication.kt
Timber.plant(Timber.DebugTree())
```

View logs:
```bash
adb logcat -s ExpandScreen:V
```

## 📝 Development Guidelines

### Code Style
- Follow Kotlin coding conventions
- Use ktlint for automatic formatting
- Maximum line length: 120 characters
- Use meaningful variable names

### Git Commit Messages
```
<type>(<scope>): <subject>

<body>

<footer>
```

Types: feat, fix, docs, style, refactor, test, chore

### Pull Request Process
1. Create feature branch from `main`
2. Implement changes with tests
3. Run ktlint and tests
4. Submit PR with description
5. Address code review comments
6. Merge after approval

## 📄 License

[License information here]

## 👥 Contributors

[Contributor list]

## 📞 Support

- Create an issue on GitHub
- Email: support@expandscreen.com
- Documentation: [link]

## 🗺️ Roadmap

### Phase 1 (Current)
- [x] Project architecture setup
- [ ] USB connection implementation
- [ ] Video decoding and rendering
- [ ] Basic UI

### Phase 2
- [ ] WiFi connection
- [ ] Touch input
- [ ] Performance optimization
- [ ] Settings management

### Phase 3
- [ ] Audio support
- [ ] Advanced gestures
- [ ] QR code pairing
- [ ] Multi-device support

## 🙏 Acknowledgments

- Android Open Source Project
- Material Design team
- FFmpeg project
- All open source library contributors

---

**Version**: 1.0.0
**Last Updated**: 2026-01-22
**Minimum Android Version**: 8.0 (API 26)
**Target Android Version**: 14 (API 34)
