using System;
using Godot;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Crystal.Scripts.Win;

/// <summary>
/// NAudio 音频采集播放器（带数据平滑过渡功能）
/// </summary>
[GlobalClass]
public partial class NAudioCaptureAudioPlayer : AudioStreamPlayer
{
    private bool _isRunning;
    public bool IsRunning => _isRunning;
    
    private WasapiCapture _capture;
    public MMDevice Device;
    private WaveFormat _captureFormat;
    private AudioStreamGeneratorPlayback _playback;
    
    // 配置参数
    [Export] public int TargetSampleRate { get; set; } = 192000;
    [Export] public float BufferLength { get; set; } = 0.05f;

    #region 平滑过渡相关配置与状态
    /// <summary>
    /// 音频平滑系数（0~1），值越大平滑效果越明显（过渡越慢），0表示无平滑
    /// </summary>
    [Export] public float SmoothFactor { get; set; } = 0.1f;
    
    /// <summary>
    /// 停止采集时的淡出时长（秒），避免突然静音的突兀感
    /// </summary>
    [Export] public float FadeOutDuration { get; set; } = 0.2f;
    
    /// <summary>
    /// 上一帧左声道样本值（用于线性插值平滑）
    /// </summary>
    private float _lastLeftSample = 0f;
    
    /// <summary>
    /// 上一帧右声道样本值（用于线性插值平滑）
    /// </summary>
    private float _lastRightSample = 0f;
    
    /// <summary>
    /// 是否正在执行淡出流程
    /// </summary>
    private bool _isFadingOut = false;
    
    /// <summary>
    /// 淡出计时器（记录已处理的淡出样本数）
    /// </summary>
    private float _fadeOutTimer = 0f;
    
    /// <summary>
    /// 淡出所需的总样本数
    /// </summary>
    private int _fadeOutTotalSamples = 0;

    /// <summary>
    /// 标记是否已触发Godot主线程清理，避免重复调用
    /// </summary>
    private bool _isGodotCleanupTriggered = false;
    #endregion

    public override void _Ready()
    {
        // 限制平滑系数和淡出时长的合理范围
        SmoothFactor = Mathf.Clamp(SmoothFactor, 0.01f, 0.99f);
        FadeOutDuration = Mathf.Clamp(FadeOutDuration, 0.05f, 1f);
        
        // 初始化Godot音频生成器
        InitializeNAudioCapture();
    }

    public static MMDeviceCollection GetRenderDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        
        var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
        enumerator.Dispose();
        return captureDevices;
    }
    
    public void InitializeNAudioCapture(MMDevice device = null)
    {
        // 重置主线程清理标记
        _isGodotCleanupTriggered = false;

        var generator = new AudioStreamGenerator();
        generator.MixRate = TargetSampleRate;
        generator.BufferLength = BufferLength;
        this.Stream = generator;
        this.Play();

        // 重置平滑过渡状态
        ResetSmoothState();

        if (this.IsPlaying())
        {
            _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;

            if (device == null)
            {
                device = WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice();
                GD.Print("选择默认设备" + device);
            }
            
            Device = device;

            if (_capture != null)
            {
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }

            try
            {
                _capture = new WasapiCapture();

                if (device.DataFlow == DataFlow.Render)
                {
                    _capture = new WasapiLoopbackCapture(device);
                }
                else if (device.DataFlow == DataFlow.Capture)
                {
                    _capture = new WasapiCapture(device);
                }

                _captureFormat = _capture.WaveFormat;
                TargetSampleRate = _captureFormat.SampleRate;

                GD.Print(
                    $"NAudio捕获格式: {_captureFormat.SampleRate}Hz, {_captureFormat.BitsPerSample}bit, {_captureFormat.Channels}channels");
                GD.Print($"Godot目标格式: {TargetSampleRate}Hz");

                _capture.DataAvailable += OnNAudioDataAvailable;
                _capture.StartRecording();
                GD.Print("NAudio 循环捕获已启动。");
            }
            catch (Exception e)
            {
                GD.PrintErr($"初始化NAudio捕获失败: {e.Message}");
                _capture = null;
            }
        }
        _isRunning =  true;
    }

    /// <summary>
    /// 重置平滑过渡相关状态
    /// </summary>
    private void ResetSmoothState()
    {
        _lastLeftSample = 0f;
        _lastRightSample = 0f;
        _isFadingOut = false;
        _fadeOutTimer = 0f;
        _fadeOutTotalSamples = 0;
    }

    public void StopNAudioCapture()
    {
        if (_isFadingOut) return; // 避免重复触发淡出流程

        // 若正在播放，先执行淡出过渡，再停止资源
        if (this.IsPlaying() && _playback != null && _capture != null)
        {
            _isFadingOut = true;
            // 计算淡出所需的总样本数（淡出时长 * 采样率）
            _fadeOutTotalSamples = (int)(FadeOutDuration * TargetSampleRate);
            _fadeOutTimer = 0f;
            GD.Print("开始音频淡出过渡...");
            return;
        }

        // 若未在播放，直接清理资源
        CleanupCaptureResources();
    }

    /// <summary>
    /// 【新增】Godot主线程专属清理方法（仅处理Node相关操作）
    /// 该方法将通过CallDeferred延迟到主线程执行
    /// </summary>
    private void CleanupGodotAudioPlayer()
    {
        if (this.IsPlaying())
        {
            this.Stop();
            this.Stream = null;
        }
        GD.Print("Godot音频播放器资源已清理");
    }

    /// <summary>
    /// 清理音频捕获相关资源
    /// 【修改】拆分Godot节点操作（延迟执行）和NAudio资源操作（直接执行）
    /// </summary>
    private void CleanupCaptureResources()
    {
        // 避免重复触发Godot主线程清理
        if (!_isGodotCleanupTriggered)
        {
            _isGodotCleanupTriggered = true;
            // 关键：通过CallDeferred将Godot节点操作延迟到主线程执行
            this.CallDeferred(nameof(CleanupGodotAudioPlayer));
        }

        // NAudio资源释放（线程安全，可直接在工作线程执行）
        if (_capture != null)
        {
            // 先移除事件订阅，避免内存泄漏
            _capture.DataAvailable -= OnNAudioDataAvailable;
            try
            {
                if (_capture.CaptureState == CaptureState.Capturing)
                {
                    _capture.StopRecording();
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"停止NAudio捕获失败: {e.Message}");
            }
            _capture.Dispose();
            _capture = null;
        }
        
        Device = null;
        _isRunning = false;
        ResetSmoothState(); // 重置平滑状态
        GD.Print("NAudio捕获资源已清理完成。");
    }
    
    private void OnNAudioDataAvailable(object sender, WaveInEventArgs e)
    {
        if (_playback == null || (_isFadingOut && _fadeOutTimer >= _fadeOutTotalSamples)) return;

        int bytesPerSample = _captureFormat.BitsPerSample / 8;
        int sampleCount = e.BytesRecorded / (bytesPerSample * _captureFormat.Channels);
        
        // 根据格式处理音频数据
        switch (_captureFormat.Encoding)
        {
            case WaveFormatEncoding.Pcm:
                if (_captureFormat.BitsPerSample == 16)
                    Process16BitPcmData(e.Buffer, sampleCount, _captureFormat.Channels);
                else if (_captureFormat.BitsPerSample == 32)
                    Process32BitPcmData(e.Buffer, sampleCount, _captureFormat.Channels);
                break;
            case WaveFormatEncoding.IeeeFloat:
                ProcessFloatData(e.Buffer, sampleCount);
                break;
            default:
                GD.PrintErr($"不支持的音频格式: {_captureFormat.Encoding}");
                break;
        }
    }
    
    private void Process16BitPcmData(byte[] buffer, int sampleCount, int channels)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            if (!_playback.CanPushBuffer(1)) break;
            
            int byteOffset = i * channels * 2; // 16bit = 2 bytes
            
            // 读取左声道（或混合所有声道）
            short leftSample = BitConverter.ToInt16(buffer, byteOffset);
            float leftFloat = leftSample / 32768.0f;
            
            // 限制范围防止爆音
            leftFloat = Mathf.Clamp(leftFloat, -1.0f, 1.0f);
            
            // 如果是立体声，也读取右声道，否则复制左声道
            float rightFloat = leftFloat;
            if (channels >= 2)
            {
                short rightSample = BitConverter.ToInt16(buffer, byteOffset + 2);
                rightFloat = rightSample / 32768.0f;
                rightFloat = Mathf.Clamp(rightFloat, -1.0f, 1.0f);
            }

            // 1. 线性插值平滑过渡：避免音频帧突变
            float smoothedLeft = Mathf.Lerp(_lastLeftSample, leftFloat, 1 - SmoothFactor);
            float smoothedRight = Mathf.Lerp(_lastRightSample, rightFloat, 1 - SmoothFactor);
            
            // 重新限制范围
            smoothedLeft = Mathf.Clamp(smoothedLeft, -1.0f, 1.0f);
            smoothedRight = Mathf.Clamp(smoothedRight, -1.0f, 1.0f);

            // 2. 处理停止时的淡出过渡
            if (_isFadingOut)
            {
                _fadeOutTimer++;
                // 计算淡出权重（从1线性衰减到0）
                float fadeWeight = Mathf.Clamp(1 - (_fadeOutTimer / _fadeOutTotalSamples), 0f, 1f);
                // 应用淡出效果
                smoothedLeft *= fadeWeight;
                smoothedRight *= fadeWeight;

                // 淡出完成后清理资源
                if (_fadeOutTimer >= _fadeOutTotalSamples)
                {
                    CleanupCaptureResources();
                    break;
                }
            }

            // 推送平滑后的音频帧
            _playback.PushFrame(new Vector2(smoothedLeft, smoothedRight));

            // 更新上一帧样本值，用于下一帧插值
            _lastLeftSample = smoothedLeft;
            _lastRightSample = smoothedRight;
        }
    }
    
    private void Process32BitPcmData(byte[] buffer, int sampleCount, int channels)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            if (!_playback.CanPushBuffer(1)) break;
            
            int byteOffset = i * channels * 4; // 32bit = 4 bytes
            
            int leftSample = BitConverter.ToInt32(buffer, byteOffset);
            float leftFloat = leftSample / 2147483648.0f; // 2^31
            
            leftFloat = Mathf.Clamp(leftFloat, -1.0f, 1.0f);
            
            float rightFloat = leftFloat;
            if (channels >= 2)
            {
                int rightSample = BitConverter.ToInt32(buffer, byteOffset + 4);
                rightFloat = rightSample / 2147483648.0f;
                rightFloat = Mathf.Clamp(rightFloat, -1.0f, 1.0f);
            }

            // 1. 线性插值平滑过渡
            float smoothedLeft = Mathf.Lerp(_lastLeftSample, leftFloat, 1 - SmoothFactor);
            float smoothedRight = Mathf.Lerp(_lastRightSample, rightFloat, 1 - SmoothFactor);
            
            smoothedLeft = Mathf.Clamp(smoothedLeft, -1.0f, 1.0f);
            smoothedRight = Mathf.Clamp(smoothedRight, -1.0f, 1.0f);

            // 2. 处理停止时的淡出过渡
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

            _playback.PushFrame(new Vector2(smoothedLeft, smoothedRight));

            // 更新上一帧样本值
            _lastLeftSample = smoothedLeft;
            _lastRightSample = smoothedRight;
        }
    }
    
    private void ProcessFloatData(byte[] buffer, int sampleCount)
    {
        int channels = _captureFormat.Channels;
        
        for (int i = 0; i < sampleCount; i++)
        {
            if (!_playback.CanPushBuffer(1)) break;
            
            int byteOffset = i * channels * 4; // float = 4 bytes
            
            float leftFloat = BitConverter.ToSingle(buffer, byteOffset);
            leftFloat = Mathf.Clamp(leftFloat, -1.0f, 1.0f);
            
            float rightFloat = leftFloat;
            if (channels >= 2)
            {
                float rightSample = BitConverter.ToSingle(buffer, byteOffset + 4);
                rightFloat = Mathf.Clamp(rightSample, -1.0f, 1.0f);
            }

            // 1. 线性插值平滑过渡
            float smoothedLeft = Mathf.Lerp(_lastLeftSample, leftFloat, 1 - SmoothFactor);
            float smoothedRight = Mathf.Lerp(_lastRightSample, rightFloat, 1 - SmoothFactor);
            
            smoothedLeft = Mathf.Clamp(smoothedLeft, -1.0f, 1.0f);
            smoothedRight = Mathf.Clamp(smoothedRight, -1.0f, 1.0f);

            // 2. 处理停止时的淡出过渡
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

            _playback.PushFrame(new Vector2(smoothedLeft, smoothedRight));

            // 更新上一帧样本值
            _lastLeftSample = smoothedLeft;
            _lastRightSample = smoothedRight;
        }
    }

    public override void _ExitTree()
    {
        // 退出时强制清理资源，确保无内存泄漏
        _isFadingOut = false; // 跳过淡出流程，直接强制清理
        CleanupCaptureResources();
        base._ExitTree();
    }
}