using Godot;
using System;


namespace CrystalPhoenix.Scripts.AudioSpectrum.Kaleidoscope
{
    /// <summary>
    /// 万花筒音频频谱
    /// </summary>
    [GlobalClass]
    public partial class KaleidoscopeAudioSpectrum : Control, IAudioSpectrum
    {
        // 导出变量以便在编辑器中设置
        [Export] 
        public NodePath AudioPlayerPath;
        
        [Export] 
        private ShaderMaterial _shaderMaterial;

        // 频谱分析参数
        private const int SpectrumSize = 64;
        private readonly float[] _spectrumData = new float[SpectrumSize];

        private readonly float[] _smoothedSpectrum = new float[SpectrumSize];

        // 平滑参数
        private const float SmoothFactor = 0.65f;

        private AudioEffectSpectrumAnalyzerInstance _fft;

        public override void _Ready()
        {
            // 确保材质存在
            if (_shaderMaterial == null)
            {
                GD.PrintErr("ShaderMaterial not found!");
            }
        }
        
        public void Initialize(AudioEffectSpectrumAnalyzerInstance fft)
        {
            this._fft = fft;
        }

        void IAudioSpectrum.UpdateSpectrum()
        {
            if (_fft == null)
            {
                GD.PrintErr("RgbHaloAudioSpectrum not found!");
                return;
            }

            // 获取频率范围
            float minFreq = 20.0f; // 最低频率20Hz
            float maxFreq = 20000.0f; // 最高频率20kHz

            // 使用对数尺度获取频谱数据
            float frequencyStep = (float)Math.Log(maxFreq / minFreq) / SpectrumSize;
            
            for (int i = 0; i < SpectrumSize; i++)
            {
                float freq = minFreq * (float)Math.Exp(frequencyStep * i);
                float magnitude = _fft.GetMagnitudeForFrequencyRange(
                    freq,
                    freq * (float)Math.Exp(frequencyStep)
                ).Length();

                // 将幅度转换为dB并归一化
                float db = (float)(20.0 * Math.Log10(magnitude + 0.0001));
                // spectrumData[i] = Mathf.Clamp((db + 60) / 60, 0, 1);
                float newValue = Mathf.Clamp((db + 60) / 60, 0, 1);
                _smoothedSpectrum[i] = SmoothFactor * _smoothedSpectrum[i] + (1 - SmoothFactor) * newValue;
                _spectrumData[i] = _smoothedSpectrum[i];
            }
            
            if (_shaderMaterial != null)
            {
                _shaderMaterial.SetShaderParameter("spectrum", _spectrumData);
            }
        }
    }
}