using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace Crystal.Scripts.Linux;

/// <summary>
/// Linux PulseAudio 音频采集播放器
/// </summary>
[GlobalClass]
public partial class PulseAudioCaptureAudioPlayer : AudioStreamPlayer
{
    private bool _isRunning;
    public bool IsRunning => _isRunning;
    
    private AudioStreamGeneratorPlayback _playback;
    
    // 配置参数
    [Export] public int TargetSampleRate { get; set; } = 48000;
    [Export] public float BufferLength { get; set; } = 0.05f;
    
    // PulseAudio 相关
    private IntPtr _pulseContext;
    private IntPtr _pulseMainLoop;
    private IntPtr _pulseStream;
    private Thread _pulseThread;
    private bool _shouldCapture = true;
    
    // 音频缓冲队列 + 【修复1：添加队列最大容量限制，防止无限堆积】
    private System.Collections.Concurrent.ConcurrentQueue<float[]> _audioBufferQueue = 
        new System.Collections.Concurrent.ConcurrentQueue<float[]>();
    private const int BUFFER_SIZE = 4096;
    [Export] public int MaxBufferQueueCount { get; set; } = 50; // 队列最大容量，可通过编辑器调整

    #region PulseAudio Native Bindings
    private const string PULSE_LIB = "libpulse.so.0";
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_mainloop_new();
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_mainloop_get_api(IntPtr loop);
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_context_new(IntPtr api, string name);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_context_connect(IntPtr context, string server, uint flags, IntPtr api);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_mainloop_iterate(IntPtr loop, int block, out int retval);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_context_disconnect(IntPtr context);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_context_unref(IntPtr context);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_mainloop_free(IntPtr loop);
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_stream_new(IntPtr context, string name, 
        ref pa_sample_spec sampleSpec, IntPtr channelMap);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_stream_connect_record(IntPtr stream, string device, 
        ref pa_buffer_attr attr, uint flags);
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_stream_peek(IntPtr stream, out IntPtr data, out ulong length);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_stream_drop(IntPtr stream);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_stream_unref(IntPtr stream);
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_strerror(int error);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_context_get_state(IntPtr context);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_stream_get_state(IntPtr stream);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct pa_sample_spec
    {
        public int format;
        public uint rate;
        public byte channels;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct pa_buffer_attr
    {
        public uint maxlength;
        public uint tlength;
        public uint prebuf;
        public uint minreq;
        public uint fragsize;
    }
    
    private enum pa_sample_format : int
    {
        PA_SAMPLE_U8 = 0,
        PA_SAMPLE_ALAW = 1,
        PA_SAMPLE_ULAW = 2,
        PA_SAMPLE_S16LE = 3,
        PA_SAMPLE_S16BE = 4,
        PA_SAMPLE_FLOAT32LE = 5,
        PA_SAMPLE_FLOAT32BE = 6,
        PA_SAMPLE_S32LE = 7,
        PA_SAMPLE_S32BE = 8,
        PA_SAMPLE_S24LE = 9,
        PA_SAMPLE_S24BE = 10,
        PA_SAMPLE_S24_32LE = 11,
        PA_SAMPLE_S24_32BE = 12,
        PA_SAMPLE_MAX = 13,
        PA_SAMPLE_INVALID = -1
    }
    
    private enum pa_context_state : int
    {
        UNCONNECTED = 0,
        CONNECTING = 1,
        AUTHORIZING = 2,
        SETTING_NAME = 3,
        READY = 4,
        FAILED = 5,
        TERMINATED = 6
    }
    
    private enum pa_stream_state : int
    {
        UNCONNECTED = 0,
        CREATING = 1,
        READY = 2,
        FAILED = 3,
        TERMINATED = 4
    }
    #endregion
    
    #region 平滑过渡相关配置与状态
    [Export] public float SmoothFactor { get; set; } = 0.1f;
    [Export] public float FadeOutDuration { get; set; } = 0.2f;
    private float _lastLeftSample = 0f;
    private float _lastRightSample = 0f;
    private bool _isFadingOut = false;
    private float _fadeOutTimer = 0f;
    private int _fadeOutTotalSamples = 0;
    private bool _isGodotCleanupTriggered = false;
    #endregion

    public override void _Ready()
    {
        SetBus("Music");
        SmoothFactor = Mathf.Clamp(SmoothFactor, 0.01f, 0.99f);
        FadeOutDuration = Mathf.Clamp(FadeOutDuration, 0.05f, 1f);
        MaxBufferQueueCount = Mathf.Clamp(MaxBufferQueueCount, 10, 200); // 限制队列容量范围
        // 初始化音频生成器
        InitializePulseAudioCapture();
    }

    /// <summary>
    /// 获取可用的 PulseAudio 设备列表
    /// </summary>
    public List<string> GetAudioDevices()
    {
        var devices = new List<string>();

        devices.Add("default");                     // 默认输入设备
        devices.Add("alsa_output.*.monitor");       // 系统输出监视器（用于捕获系统声音）
        var deviceManager = new PulseAudioDeviceManager();
        var deviceNames = deviceManager.GetAudioDeviceNames();
        
        // 使用 Concat 和 ToList 合并列表
        devices = devices.Concat(deviceNames).ToList();

        
        return devices;
    }
    
    

    /// <summary>
    /// 初始化 PulseAudio 捕获
    /// </summary>
    public void InitializePulseAudioCapture(string deviceName = "default")
    {
        _isGodotCleanupTriggered = false;
        ResetSmoothState();

        // 初始化 Godot 音频生成器
        var generator = new AudioStreamGenerator();
        generator.MixRate = TargetSampleRate;
        generator.BufferLength = BufferLength;
        this.Stream = generator;
        this.Play();

        if (this.IsPlaying())
        {
            _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;

            // 停止现有线程
            StopCaptureThread();

            // 启动 PulseAudio 捕获线程
            _shouldCapture = true;
            _pulseThread = new Thread(() => CaptureWithPulseAudio(deviceName));
            _pulseThread.IsBackground = true;
            _pulseThread.Start();

            _isRunning = true;
            GD.Print($"PulseAudio 音频捕获已启动，设备: {deviceName}");
        }
    }

    /// <summary>
    /// 使用 PulseAudio 捕获音频
    /// </summary>
    private void CaptureWithPulseAudio(string deviceName)
    {
        GD.Print($"开始 PulseAudio 捕获: {deviceName}");
        
        try
        {
            // 1. 创建 PulseAudio 主循环
            _pulseMainLoop = pa_mainloop_new();
            if (_pulseMainLoop == IntPtr.Zero)
            {
                throw new Exception("无法创建 PulseAudio 主循环");
            }
            
            var api = pa_mainloop_get_api(_pulseMainLoop);
            
            // 2. 创建上下文
            _pulseContext = pa_context_new(api, "Godot Audio Capture");
            if (_pulseContext == IntPtr.Zero)
            {
                throw new Exception("无法创建 PulseAudio 上下文");
            }
            
            // 3. 连接上下文
            int result = pa_context_connect(_pulseContext, null, 0, IntPtr.Zero);
            if (result < 0)
            {
                throw new Exception($"无法连接到 PulseAudio 服务器: {Marshal.PtrToStringAnsi(pa_strerror(result))}");
            }
            
            // 4. 等待连接就绪
            if (!WaitForContextReady())
            {
                throw new Exception("PulseAudio 上下文未就绪");
            }
            
            GD.Print("PulseAudio 连接成功");
            
            // 5. 创建音频流
            var sampleSpec = new pa_sample_spec
            {
                format = (int)pa_sample_format.PA_SAMPLE_FLOAT32LE,
                rate = (uint)TargetSampleRate,
                channels = 2  // 立体声
            };
            
            _pulseStream = pa_stream_new(_pulseContext, "Godot Capture Stream", 
                ref sampleSpec, IntPtr.Zero);
            if (_pulseStream == IntPtr.Zero)
            {
                throw new Exception("无法创建 PulseAudio 流");
            }
            
            // 6. 配置缓冲区属性
            var bufferAttr = new pa_buffer_attr
            {
                maxlength = (uint)(BUFFER_SIZE * 4 * 2),  // float32 * 声道数
                tlength = (uint)(BUFFER_SIZE * 4 * 2),
                prebuf = 0,
                minreq = (uint)(BUFFER_SIZE * 4 * 2),
                fragsize = (uint)(BUFFER_SIZE * 4 * 2)
            };
            
            // 7. 连接音频流进行录制
            result = pa_stream_connect_record(_pulseStream, deviceName, ref bufferAttr, 0);
            if (result < 0)
            {
                throw new Exception($"无法连接音频流: {Marshal.PtrToStringAnsi(pa_strerror(result))}");
            }
            
            // 8. 等待流就绪
            if (!WaitForStreamReady())
            {
                throw new Exception("音频流未就绪");
            }
            
            GD.Print("PulseAudio 音频流就绪，开始捕获");
            
            // 9. 主捕获循环
            MainCaptureLoop();
        }
        catch (Exception e)
        {
            GD.PrintErr($"PulseAudio 捕获失败: {e.Message}");
            
            // 回退到模拟捕获
            GD.Print("回退到模拟音频捕获");
            SimulateAudioCapture();
        }
        finally
        {
            CleanupPulseAudioResources();
        }
    }
    
    /// <summary>
    /// 等待 PulseAudio 上下文就绪
    /// </summary>
    private bool WaitForContextReady()
    {
        int maxAttempts = 100; // 10秒超时
        int attempt = 0;
        
        while (_shouldCapture && attempt < maxAttempts)
        {
            var state = (pa_context_state)pa_context_get_state(_pulseContext);
            
            switch (state)
            {
                case pa_context_state.READY:
                    return true;
                case pa_context_state.FAILED:
                case pa_context_state.TERMINATED:
                    return false;
                default:
                    Thread.Sleep(100); // 等待 100ms
                    attempt++;
                    
                    // 处理 PulseAudio 事件
                    int retval;
                    pa_mainloop_iterate(_pulseMainLoop, 0, out retval);
                    break;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// 等待音频流就绪
    /// </summary>
    private bool WaitForStreamReady()
    {
        int maxAttempts = 100; // 10秒超时
        int attempt = 0;
        
        while (_shouldCapture && attempt < maxAttempts)
        {
            var state = (pa_stream_state)pa_stream_get_state(_pulseStream);
            
            switch (state)
            {
                case pa_stream_state.READY:
                    return true;
                case pa_stream_state.FAILED:
                case pa_stream_state.TERMINATED:
                    return false;
                default:
                    Thread.Sleep(100); // 等待 100ms
                    attempt++;
                    
                    // 处理 PulseAudio 事件
                    int retval;
                    pa_mainloop_iterate(_pulseMainLoop, 0, out retval);
                    break;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// 主捕获循环
    /// </summary>
    private void MainCaptureLoop()
    {
        while (_shouldCapture && _isRunning)
        {
            // 处理 PulseAudio 事件
            int retval;
            pa_mainloop_iterate(_pulseMainLoop, 0, out retval);
            
            // 从流中读取数据
            ReadAudioDataFromStream();
            
            Thread.Sleep(10); // 控制循环频率
        }
    }
    
    /// <summary>
    /// 从 PulseAudio 流中读取音频数据
    /// </summary>
    private void ReadAudioDataFromStream()
    {
        if (_pulseStream == IntPtr.Zero)
            return;
            
        IntPtr dataPtr;
        ulong length;
        
        // 查看流中是否有可用数据
        pa_stream_peek(_pulseStream, out dataPtr, out length);
        
        if (dataPtr != IntPtr.Zero && length > 0)
        {
            try
            {
                // 【修复2：避免创建超大数组，拆分为固定BUFFER_SIZE的小数组】
                int totalSampleCount = (int)(length / 4); // float32 = 4 bytes
                if (totalSampleCount <= 0) return;

                // 拆分大缓冲区为固定大小的小缓冲区
                int offset = 0;
                while (offset < totalSampleCount && _shouldCapture)
                {
                    int currentSampleCount = Math.Min(BUFFER_SIZE, totalSampleCount - offset);
                    float[] audioData = new float[currentSampleCount];
                    
                    // 复制对应偏移量的数据
                    Marshal.Copy(IntPtr.Add(dataPtr, offset * 4), audioData, 0, currentSampleCount);
                    
                    // 【修复3：入队前判断队列容量，超过则丢弃旧数据（避免堆积）】
                    while (_audioBufferQueue.Count >= MaxBufferQueueCount)
                    {
                        _audioBufferQueue.TryDequeue(out var discardedBuffer); // 丢弃最旧的缓冲区
                    }
                    _audioBufferQueue.Enqueue(audioData);
                    
                    offset += currentSampleCount;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"复制音频数据失败: {e.Message}");
            }
            finally
            {
                // 丢弃已处理的数据
                pa_stream_drop(_pulseStream);
            }
        }
    }
    
    /// <summary>
    /// 模拟音频捕获（用于测试和回退）
    /// </summary>
    private void SimulateAudioCapture()
    {
        GD.Print("启动模拟音频捕获");
        
        float time = 0f;
        const float frequency1 = 440f; // A4
        const float frequency2 = 523.25f; // C5
        
        while (_shouldCapture && _isRunning)
        {
            if (_playback == null)
            {
                Thread.Sleep(1);
                continue;
            }
            
            // 生成测试音频（双声道正弦波）
            var buffer = new float[BUFFER_SIZE];
            
            for (int i = 0; i < BUFFER_SIZE; i += 2)
            {
                // 左声道：440Hz 正弦波
                float leftSample = Mathf.Sin(2 * Mathf.Pi * frequency1 * time) * 0.3f;
                
                // 右声道：523.25Hz 正弦波（略有延迟制造立体声效果）
                float rightSample = Mathf.Sin(2 * Mathf.Pi * frequency2 * (time + 0.01f)) * 0.3f;
                
                // 添加一些谐波
                leftSample += Mathf.Sin(2 * Mathf.Pi * frequency1 * 2 * time) * 0.1f;
                rightSample += Mathf.Sin(2 * Mathf.Pi * frequency2 * 3 * time) * 0.1f;
                
                buffer[i] = leftSample;
                buffer[i + 1] = rightSample;
                
                time += 1.0f / TargetSampleRate;
                
                // 防止 time 值过大
                if (time > 1000f) time -= 1000f;
            }
            
            // 【修复4：模拟捕获也添加队列容量限制】
            while (_audioBufferQueue.Count >= MaxBufferQueueCount)
            {
                _audioBufferQueue.TryDequeue(out var discardedBuffer);
            }
            // 添加到处理队列
            _audioBufferQueue.Enqueue(buffer);
            
            Thread.Sleep(20); // 控制捕获速率
        }
        
        GD.Print("模拟音频捕获结束");
    }
    
    /// <summary>
    /// 清理 PulseAudio 资源
    /// </summary>
    private void CleanupPulseAudioResources()
    {
        try
        {
            // 清理流
            if (_pulseStream != IntPtr.Zero)
            {
                pa_stream_unref(_pulseStream);
                _pulseStream = IntPtr.Zero;
            }
            
            // 清理上下文
            if (_pulseContext != IntPtr.Zero)
            {
                pa_context_disconnect(_pulseContext);
                pa_context_unref(_pulseContext);
                _pulseContext = IntPtr.Zero;
            }
            
            // 清理主循环
            if (_pulseMainLoop != IntPtr.Zero)
            {
                pa_mainloop_free(_pulseMainLoop);
                _pulseMainLoop = IntPtr.Zero;
            }
            
            GD.Print("PulseAudio 资源已清理");
        }
        catch (Exception e)
        {
            GD.PrintErr($"清理 PulseAudio 资源失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 重置平滑状态
    /// </summary>
    private void ResetSmoothState()
    {
        _lastLeftSample = 0f;
        _lastRightSample = 0f;
        _isFadingOut = false;
        _fadeOutTimer = 0f;
        _fadeOutTotalSamples = 0;
    }
    
    /// <summary>
    /// 停止音频捕获
    /// </summary>
    public void StopAudioCapture()
    {
        if (_isFadingOut) return;

        if (this.IsPlaying() && _playback != null)
        {
            _isFadingOut = true;
            _fadeOutTotalSamples = (int)(FadeOutDuration * TargetSampleRate);
            _fadeOutTimer = 0f;
            GD.Print("开始音频淡出过渡...");
            return;
        }

        CleanupCaptureResources();
    }
    
    /// <summary>
    /// 处理音频数据并推送到 Godot
    /// </summary>
    private void ProcessAndPushAudioData(float[] buffer)
    {
        if (_playback == null || (_isFadingOut && _fadeOutTimer >= _fadeOutTotalSamples))
            return;

        // 【修复5：校验缓冲区长度为偶数（双声道），避免数组越界】
        int safeBufferLength = buffer.Length % 2 == 0 ? buffer.Length : buffer.Length - 1;
        for (int i = 0; i < safeBufferLength; i += 2)
        {
            if (!_playback.CanPushBuffer(1)) break;

            float leftFloat = buffer[i];
            float rightFloat = (i + 1 < safeBufferLength) ? buffer[i + 1] : leftFloat;

            // 限制范围
            leftFloat = Mathf.Clamp(leftFloat, -1.0f, 1.0f);
            rightFloat = Mathf.Clamp(rightFloat, -1.0f, 1.0f);

            // 平滑过渡
            float smoothedLeft = Mathf.Lerp(_lastLeftSample, leftFloat, 1 - SmoothFactor);
            float smoothedRight = Mathf.Lerp(_lastRightSample, rightFloat, 1 - SmoothFactor);

            smoothedLeft = Mathf.Clamp(smoothedLeft, -1.0f, 1.0f);
            smoothedRight = Mathf.Clamp(smoothedRight, -1.0f, 1.0f);

            // 淡出处理
            if (_isFadingOut)
            {
                _fadeOutTimer++;
                float fadeWeight = Mathf.Clamp(1 - (_fadeOutTimer / _fadeOutTotalSamples), 0f, 1f);
                smoothedLeft *= fadeWeight;
                smoothedRight *= fadeWeight;

                if (_fadeOutTimer >= _fadeOutTotalSamples)
                {
                    CleanupCaptureResources();
                    break;
                }
            }

            // 推送音频帧
            _playback.PushFrame(new Vector2(smoothedLeft, smoothedRight));

            // 更新上一帧样本
            _lastLeftSample = smoothedLeft;
            _lastRightSample = smoothedRight;
        }
    }
    
    public override void _Process(double delta)
    {
        if (!_isRunning || _playback == null) return;

        // 【修复6：先判断队列是否过载，批量丢弃冗余数据，减轻处理压力】
        if (_audioBufferQueue.Count > MaxBufferQueueCount * 2) // 过载阈值（队列容量的2倍）
        {
            int discardCount = _audioBufferQueue.Count - MaxBufferQueueCount;
            for (int i = 0; i < discardCount; i++)
            {
                _audioBufferQueue.TryDequeue(out var discardedBuffer);
            }
            GD.Print($"音频队列过载，已丢弃 {discardCount} 个旧缓冲区");
        }

        // 处理缓冲队列中的音频数据
        int maxProcessPerFrame = 10; // 每帧最多处理10个缓冲区
        int processed = 0;
        
        while (_audioBufferQueue.TryDequeue(out var buffer) && processed < maxProcessPerFrame)
        {
            ProcessAndPushAudioData(buffer);
            processed++;
        }
    }
    
    /// <summary>
    /// Godot 主线程清理
    /// </summary>
    private void CleanupGodotAudioPlayer()
    {
        if (this.IsPlaying())
        {
            this.Stop();
            this.Stream = null;
        }
        GD.Print("Godot 音频播放器资源已清理");
    }
    
    /// <summary>
    /// 停止捕获线程
    /// </summary>
    private void StopCaptureThread()
    {
        _shouldCapture = false;
        
        if (_pulseThread != null && _pulseThread.IsAlive)
        {
            _pulseThread.Join(1000);
            _pulseThread = null;
        }
    }
    
    /// <summary>
    /// 清理捕获资源
    /// </summary>
    private void CleanupCaptureResources()
    {
        if (!_isGodotCleanupTriggered)
        {
            _isGodotCleanupTriggered = true;
            this.CallDeferred(nameof(CleanupGodotAudioPlayer));
        }

        StopCaptureThread();
        CleanupPulseAudioResources();
        
        // 【修复7：清空音频缓冲队列，释放所有残留的float[]数组（核心修复内存泄露）】
        while (_audioBufferQueue.TryDequeue(out var remainingBuffer))
        {
            // 主动释放引用，让GC可以回收
            remainingBuffer = null;
        }
        
        _isRunning = false;
        ResetSmoothState();
        GD.Print("音频捕获资源已清理完成。");
    }
    
    /// <summary>
    /// 启用/禁用循环捕获（捕获系统输出）
    /// </summary>
    [Export] public bool UseLoopbackCapture
    {
        get => _useLoopbackCapture;
        set
        {
            _useLoopbackCapture = value;
            if (_isRunning)
            {
                // 重新初始化捕获
                StopAudioCapture();
                CallDeferred(nameof(ReinitializeWithNewSettings));
            }
        }
    }
    private bool _useLoopbackCapture = true;
    
    private void ReinitializeWithNewSettings()
    {
        if (_useLoopbackCapture)
        {
            InitializePulseAudioCapture("alsa_output.*.monitor");
        }
        else
        {
            InitializePulseAudioCapture("default");
        }
    }
    
    public override void _ExitTree()
    {
        _isFadingOut = false;
        CleanupCaptureResources();
        base._ExitTree();
    }
}