using System.Collections.Generic;
using Crystal.Scripts.Linux;
using Crystal.Scripts.Win;
using Godot;

namespace Crystal.Scripts.AudioSpectrum
{
    /// <summary>
    /// 音频频谱管理器
    /// </summary>
    [GlobalClass]
    public partial class AudioSpectrumManager : Node
    {
        [Export]
        private bool _autoInit = true;

        [Export] 
        private Godot.Collections.Dictionary<NodePath, bool> _audioSpectrumPath = new();

        
        private List<IAudioSpectrum> _audioSpectrums = [];
        
        [Export]
        private AudioStreamPlayer _audioStreamPlayer;
        
        public AudioStreamPlayer AudioStreamPlayer => _audioStreamPlayer;

        // 这么写只是为了测试
        public override void _Ready()
        {
            if (OS.GetName() == "Windows")
            {
                _audioStreamPlayer = new NAudioCaptureAudioPlayer();
            }
            else if (OS.GetName() == "Linux")
            {
                _audioStreamPlayer = new PulseAudioCaptureAudioPlayer();
            }
            AddChild(AudioStreamPlayer);
            RefreshAudioSpectrums();
            
        }

        /// <summary>
        /// 初始化管理器
        /// </summary>
        /// <param name="audioSpectrum"></param>
        /// <param name="audioStreamPlayer"></param>
        public void Initialize(IAudioSpectrum audioSpectrum, AudioStreamPlayer audioStreamPlayer)
        {
            _audioStreamPlayer = audioStreamPlayer;

            var fft = AudioServer.GetBusEffectInstance(
                AudioServer.GetBusIndex(audioStreamPlayer.Bus),
                0
            ) as AudioEffectSpectrumAnalyzerInstance;
            if (fft == null)
            {
                GD.PrintErr("请在Music音频总线添加AudioEffectSpectrumAnalyzer");
                return;
            }

            _audioSpectrums.Add(audioSpectrum);
            audioSpectrum.Initialize(fft);
            GD.Print("AudioSpectrum Initialized");
        }
        

        public void RefreshAudioSpectrums()
        {
            foreach (var path in _audioSpectrumPath)
            {
                // 判断是否启用
                var audioSpectrum = GetNode<IAudioSpectrum>(path.Key);
                if (audioSpectrum == null)
                {
                    GD.PrintErr("AudioSpectrumManager::_AudioSpectrumPath is null");
                    return;
                }
                if (path.Value)
                {
                    GetNode<Control>(path.Key).SetVisible(true);
                    Initialize(audioSpectrum, _audioStreamPlayer);
                }
                else
                {
                    Remove(path.Key);
                    GD.Print("AudioSpectrumManager::_AudioSpectrumPath is not enabled");
                }
            }
        }

        /// <summary>
        /// 移除音频频谱图
        /// </summary>
        /// <param name="path"></param>
        public void Remove(NodePath path)
        {
            if (!_audioSpectrumPath.ContainsKey(path))
            {
                GD.PrintErr("音频频谱图不存在");
                return;
            }
            var audioSpectrum = GetNode<IAudioSpectrum>(path);
            GetNode<Control>(path).SetVisible(false);
            _audioSpectrums.Remove(audioSpectrum);
        }

        public override void _Process(double delta)
        {
            if (_audioSpectrums == null) return;
            if (_audioStreamPlayer == null || !_audioStreamPlayer.Playing) return;

            foreach (var audioSpectrum in _audioSpectrums)
            {
                audioSpectrum.UpdateSpectrum();
                
            }
            
        }
    }
}