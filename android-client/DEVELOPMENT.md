# ExpandScreen Android - Development Guide

## 开发环境搭建

### 必需工具

1. **Android Studio** (推荐最新稳定版)
   - 下载地址: https://developer.android.com/studio
   - 版本要求: Arctic Fox (2020.3.1) 或更高

2. **JDK 17**
   - Android Studio 自带，或从 Oracle/OpenJDK 下载
   - 配置 JAVA_HOME 环境变量

3. **Android SDK**
   - SDK Platform: Android 14 (API 34)
   - Build Tools: 34.0.0+
   - NDK: 可选，用于原生代码

4. **Git**
   - 版本控制工具
   - 配置用户名和邮箱

### 项目导入

```bash
# 克隆仓库
git clone <repository-url>
cd ExpandScreen/android-client

# 使用Android Studio打开项目
# File -> Open -> 选择android-client目录

# 首次同步会下载所有依赖，需要一些时间
```

### Gradle配置

项目使用 Gradle 8.2 和 Kotlin DSL。主要配置文件：

- `build.gradle.kts` - 项目级配置
- `app/build.gradle.kts` - 应用级配置
- `gradle.properties` - Gradle属性
- `settings.gradle.kts` - 项目设置

### 依赖管理

所有依赖在 `app/build.gradle.kts` 中声明：

```kotlin
dependencies {
    // Jetpack Compose
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")

    // Hilt DI
    implementation("com.google.dagger:hilt-android:2.50")
    ksp("com.google.dagger:hilt-android-compiler:2.50")

    // 更多依赖...
}
```

## 项目结构详解

### 模块划分

#### 1. UI模块 (`ui/`)
负责所有用户界面相关代码。

**主要组件:**
- `MainActivity` - 应用入口，显示设备列表和连接选项
- `DisplayActivity` - 全屏显示视频流
- `screens/` - 各个页面的Composable函数
- `components/` - 可复用的UI组件
- `theme/` - Material Design主题配置
- `navigation/` - 导航图和路由

**示例 - 创建新页面:**
```kotlin
@Composable
fun SettingsScreen(
    viewModel: SettingsViewModel = hiltViewModel(),
    onNavigateBack: () -> Unit
) {
    val uiState by viewModel.uiState.collectAsState()

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Settings") },
                navigationIcon = {
                    IconButton(onClick = onNavigateBack) {
                        Icon(Icons.Default.ArrowBack, "Back")
                    }
                }
            )
        }
    ) { padding ->
        SettingsContent(
            uiState = uiState,
            onSettingChanged = viewModel::updateSetting,
            modifier = Modifier.padding(padding)
        )
    }
}
```

#### 2. Core模块 (`core/`)
核心业务逻辑，不依赖Android UI框架。

**子模块:**
- `decoder/` - 视频解码器
  - `VideoDecoder` - 解码器接口
  - `H264Decoder` - MediaCodec实现

- `renderer/` - OpenGL渲染
  - `VideoRenderer` - 渲染器接口
  - `GLVideoRenderer` - OpenGL ES实现

- `network/` - 网络通信
  - `NetworkManager` - 连接管理
  - TCP socket封装

**MediaCodec使用示例:**
```kotlin
class H264Decoder : VideoDecoder {
    private var mediaCodec: MediaCodec? = null

    override fun initialize() {
        mediaCodec = MediaCodec.createDecoderByType("video/avc").apply {
            val format = MediaFormat.createVideoFormat(
                "video/avc",
                1920,
                1080
            ).apply {
                setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
                setInteger(MediaFormat.KEY_PRIORITY, 0)
            }
            configure(format, surface, null, 0)
            start()
        }
    }
}
```

#### 3. Service模块 (`service/`)
后台服务，处理长时间运行的任务。

**DisplayService - 前台服务:**
```kotlin
@AndroidEntryPoint
class DisplayService : Service() {
    @Inject lateinit var networkManager: NetworkManager
    @Inject lateinit var videoDecoder: VideoDecoder

    private val serviceScope = CoroutineScope(
        SupervisorJob() + Dispatchers.Default
    )

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground(NOTIFICATION_ID, createNotification())

        serviceScope.launch {
            // 数据处理循环
            networkManager.messageFlow.collect { message ->
                when (message) {
                    is NetworkMessage.VideoFrame -> {
                        videoDecoder.decode(message.data)
                    }
                    // 处理其他消息...
                }
            }
        }

        return START_STICKY
    }
}
```

#### 4. Data模块 (`data/`)
数据层，负责数据存储和访问。

**Room数据库使用:**
```kotlin
// Entity定义
@Entity(tableName = "windows_devices")
data class WindowsDeviceEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val deviceName: String,
    val ipAddress: String?,
    val lastConnected: Long
)

// DAO定义
@Dao
interface DeviceDao {
    @Query("SELECT * FROM windows_devices ORDER BY lastConnected DESC")
    fun getAllDevices(): Flow<List<WindowsDeviceEntity>>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertDevice(device: WindowsDeviceEntity): Long
}

// Repository使用
@Singleton
class DeviceRepository @Inject constructor(
    private val deviceDao: DeviceDao
) {
    fun getAllDevices(): Flow<List<WindowsDeviceEntity>> {
        return deviceDao.getAllDevices()
    }
}
```

#### 5. Protocol模块 (`protocol/`)
通信协议定义。

**消息格式:**
```kotlin
// 消息头 (24字节)
data class MessageHeader(
    val magic: Int = 0x45585053,    // "EXPS"
    val version: Byte = 1,
    val messageType: MessageType,
    val sequenceNumber: Int,
    val timestamp: Long,
    val payloadLength: Int
)

// 编解码
object MessageCodec {
    fun encodeHeader(header: MessageHeader): ByteArray {
        return ByteBuffer.allocate(24).apply {
            putInt(header.magic)
            put(header.version)
            put(header.messageType.value)
            putShort(header.flags)
            putInt(header.sequenceNumber)
            putLong(header.timestamp)
            putInt(header.payloadLength)
        }.array()
    }
}
```

## 依赖注入 (Hilt)

### Module定义

```kotlin
@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Provides
    @Singleton
    fun provideNetworkManager(): NetworkManager {
        return NetworkManager()
    }

    @Provides
    fun provideVideoDecoder(): VideoDecoder {
        return H264Decoder()
    }
}
```

### ViewModel注入

```kotlin
@HiltViewModel
class MainViewModel @Inject constructor(
    private val deviceRepository: DeviceRepository,
    private val networkManager: NetworkManager
) : ViewModel() {

    val devices: StateFlow<List<WindowsDeviceEntity>> =
        deviceRepository.getAllDevices()
            .stateIn(
                scope = viewModelScope,
                started = SharingStarted.WhileSubscribed(5000),
                initialValue = emptyList()
            )
}
```

## 异步编程

### Kotlin Coroutines

```kotlin
// ViewModel中启动协程
viewModelScope.launch {
    try {
        val result = networkManager.connectViaUSB()
        result.onSuccess {
            _connectionState.value = ConnectionState.Connected
        }.onFailure { error ->
            _connectionState.value = ConnectionState.Error(error.message)
        }
    } catch (e: Exception) {
        Timber.e(e, "Connection failed")
    }
}

// Service中使用协程
serviceScope.launch {
    networkManager.messageFlow
        .flowOn(Dispatchers.IO)
        .collect { message ->
            processMessage(message)
        }
}
```

### Flow操作符

```kotlin
// 组合多个Flow
combine(
    networkManager.connectionState,
    performanceMonitor.fpsFlow,
    performanceMonitor.latencyFlow
) { connection, fps, latency ->
    DisplayState(connection, fps, latency)
}.collect { state ->
    updateUI(state)
}

// 转换和过滤
deviceRepository.getAllDevices()
    .map { devices -> devices.filter { it.isFavorite } }
    .distinctUntilChanged()
    .collect { favoriteDevices ->
        // 更新UI
    }
```

## 代码规范

### Kotlin编码规范

1. **命名约定:**
   - 类名: PascalCase (例: `NetworkManager`)
   - 函数名: camelCase (例: `connectViaUSB`)
   - 常量: UPPER_SNAKE_CASE (例: `MAX_RETRY_COUNT`)
   - 私有属性: _camelCase (例: `_connectionState`)

2. **文件组织:**
   - 一个文件一个公开类
   - 相关的私有类可以放在同一文件
   - 按逻辑分组imports

3. **代码格式:**
   ```kotlin
   // 使用ktlint自动格式化
   ./gradlew ktlintFormat

   // 检查格式
   ./gradlew ktlintCheck
   ```

### Compose最佳实践

1. **状态提升:**
   ```kotlin
   // 不好的做法
   @Composable
   fun BadExample() {
       var text by remember { mutableStateOf("") }
       TextField(value = text, onValueChange = { text = it })
   }

   // 好的做法
   @Composable
   fun GoodExample(
       text: String,
       onTextChange: (String) -> Unit
   ) {
       TextField(value = text, onValueChange = onTextChange)
   }
   ```

2. **避免副作用:**
   ```kotlin
   // 使用LaunchedEffect处理副作用
   @Composable
   fun ConnectionScreen(deviceId: Long) {
       LaunchedEffect(deviceId) {
           connectToDevice(deviceId)
       }
   }
   ```

3. **性能优化:**
   ```kotlin
   // 使用derivedStateOf避免不必要的重组
   val filteredList by remember {
       derivedStateOf {
           deviceList.filter { it.isFavorite }
       }
   }

   // 使用key避免错误的重组
   LazyColumn {
       items(
           items = devices,
           key = { it.id }
       ) { device ->
           DeviceItem(device)
       }
   }
   ```

## 调试技巧

### 日志记录

```kotlin
// 使用Timber
class MyClass {
    fun doSomething() {
        Timber.d("Debug message")
        Timber.i("Info message")
        Timber.w("Warning message")
        Timber.e("Error message")

        // 带异常的日志
        try {
            riskyOperation()
        } catch (e: Exception) {
            Timber.e(e, "Operation failed")
        }
    }
}
```

### ADB命令

```bash
# 查看日志
adb logcat -s ExpandScreen:V

# 过滤特定tag
adb logcat | grep "NetworkManager"

# 清除日志
adb logcat -c

# 安装APK
adb install -r app/build/outputs/apk/debug/app-debug.apk

# 截屏
adb shell screencap -p /sdcard/screen.png
adb pull /sdcard/screen.png

# 录屏
adb shell screenrecord /sdcard/demo.mp4
```

### Android Studio Profiler

1. **CPU Profiler**: 分析方法调用和CPU使用
2. **Memory Profiler**: 检测内存泄漏
3. **Network Profiler**: 监控网络请求
4. **Energy Profiler**: 分析电池消耗

## 测试

### 单元测试

```kotlin
@Test
fun `test network connection success`() = runTest {
    val networkManager = NetworkManager()
    val result = networkManager.connectViaWiFi("192.168.1.100", 8080)

    assertTrue(result.isSuccess)
    assertTrue(networkManager.isConnected())
}
```

### UI测试 (Compose)

```kotlin
@Test
fun `test device list displays correctly`() {
    composeTestRule.setContent {
        DeviceList(
            devices = testDevices,
            onDeviceClick = {}
        )
    }

    composeTestRule
        .onNodeWithText("Test Device")
        .assertExists()
        .performClick()
}
```

## 性能优化

### 延迟优化

1. 使用硬件解码 (MediaCodec)
2. 减少数据拷贝 (DirectByteBuffer)
3. OpenGL零拷贝渲染
4. TCP_NODELAY选项
5. 线程优先级调整

### 内存优化

1. 对象池复用
2. 及时释放资源
3. 使用LeakCanary检测泄漏
4. 避免大对象分配

### 电池优化

1. 使用JobScheduler/WorkManager
2. 合理使用WakeLock
3. 网络批量请求
4. 省电模式适配

## 常见问题

### Q: Gradle同步失败
A:
1. 检查网络连接
2. 清理缓存: `./gradlew clean`
3. 删除 `.gradle` 目录重新同步

### Q: Hilt编译错误
A: 确保在Application类添加 `@HiltAndroidApp` 注解

### Q: Compose预览不显示
A: 检查 `@Preview` 注解和函数签名

### Q: Room数据库迁移失败
A: 开发阶段可以使用 `.fallbackToDestructiveMigration()`

## 发布流程

1. 更新版本号
2. 运行完整测试
3. 生成签名APK
4. 测试Release版本
5. 创建Git tag
6. 上传到应用商店

---

**文档版本**: 1.0
**最后更新**: 2026-01-22
