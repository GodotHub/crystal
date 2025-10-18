using Godot;

namespace CrystalPhoenix.Scripts.AudioSpectrum
{
    /// <summary>
    /// 音频频谱图接口
    /// </summary>
    public interface IAudioSpectrum
    {
        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="fft">AudioEffectSpectrumAnalyzerInstance</param>
        void Initialize(AudioEffectSpectrumAnalyzerInstance fft);
        
        /// <summary>
        /// 更新音谱图
        /// </summary>
        void UpdateSpectrum();
    }
}