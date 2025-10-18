using System;
using Godot;

namespace CrystalPhoenix.Scripts.AudioSpectrum.UnderWater
{
    /// <summary>
    /// 音频气泡效果控制器
    /// 根据音频频谱调整气泡的速度和数量
    /// </summary>
    [GlobalClass]
    public partial class UnderWaterAudioSpectrum : Control, IAudioSpectrum
    {
        private AudioEffectSpectrumAnalyzerInstance _fft;
        
        
        public void Initialize(AudioEffectSpectrumAnalyzerInstance fft)
        {
            return;
        }

        public void UpdateSpectrum()
        {
            return;
        }
    }
}