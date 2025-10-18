using System;
using Godot;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CrystalPhoenix.Scripts;

[GlobalClass]
public partial class NAudioCaptureAudioPlayer : AudioStreamPlayer
{
    
    private WasapiCapture _capture;
    public MMDevice _device;
    private WaveFormat _captureFormat;
    private AudioStreamGeneratorPlayback _playback;
    
    // 配置参数
    [Export] public int TargetSampleRate { get; set; } = 192000;
    [Export] public float BufferLength { get; set; } = 0.05f;

    public override void _Ready()
    {
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
        var generator = new AudioStreamGenerator();
        generator.MixRate = TargetSampleRate;
        generator.BufferLength = BufferLength;
        this.Stream = generator;
        this.Play();

        if (this.IsPlaying())
        {
            _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;

            if (device == null)
            {
                GD.PushWarning("选择默认设备");
                device = WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice();
                GD.Print(device);
            }
            
            _device = device;

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
    }
    
    private void OnNAudioDataAvailable(object sender, WaveInEventArgs e)
    {
        if (_playback == null) return;

        int bytesPerSample = _captureFormat.BitsPerSample / 8;
        int sampleCount = e.BytesRecorded / (bytesPerSample * _captureFormat.Channels);
        
        // 根据格式处理音频数据
        switch (_captureFormat.Encoding)
        {
            case WaveFormatEncoding.Pcm:
                if (_captureFormat.BitsPerSample == 16)
                    Process16BitPcmData(e.Buffer, sampleCount);
                else if (_captureFormat.BitsPerSample == 32)
                    Process32BitPcmData(e.Buffer, sampleCount);
                break;
            case WaveFormatEncoding.IeeeFloat:
                ProcessFloatData(e.Buffer, sampleCount);
                break;
            default:
                GD.PrintErr($"不支持的音频格式: {_captureFormat.Encoding}");
                break;
        }
    }
    
    private void Process16BitPcmData(byte[] buffer, int sampleCount)
    {
        int channels = _captureFormat.Channels;
        
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
            
            _playback.PushFrame(new Vector2(leftFloat, rightFloat));
        }
    }
    
    private void Process32BitPcmData(byte[] buffer, int sampleCount)
    {
        int channels = _captureFormat.Channels;
        
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
            
            _playback.PushFrame(new Vector2(leftFloat, rightFloat));
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
            
            _playback.PushFrame(new Vector2(leftFloat, rightFloat));
        }
    }

    public override void _ExitTree()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        base._ExitTree();
    }
}