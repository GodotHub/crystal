using System;
using Godot;

namespace CrystalPhoenix.Scripts.Player
{
    [GlobalClass]
    public partial class AudioLoader : Node
    {
        // 用于显示文件对话框的节点
        private FileDialog _fileDialog;

        // 音频播放器节点
        [Export]
        private AudioStreamPlayer _audioPlayer;

        // 当前加载的音频流
        private AudioStream _loadedAudio;

        // 界面控制节点
        [Export]
        private Button _loadButton;
        [Export]
        private Button _playButton;
        [Export]
        private Button _pauseButton;
        [Export]
        private Label _statusLabel;
        
        [Export]
        private float _curAudioPos = 0;

        public override void _Ready()
        {
            // 创建UI元素
            CreateUI();

            // 创建文件对话框
            _fileDialog = new FileDialog();
            _fileDialog.Title = "选择音频";
            _fileDialog.UseNativeDialog = true;
            _fileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
            _fileDialog.Access = FileDialog.AccessEnum.Filesystem;
            _fileDialog.Filters = ["*.wav, *.ogg, *.mp3; Audio Files"];
            _fileDialog.FileSelected += OnFileSelected;
            AddChild(_fileDialog);
            

            // 更新按钮状态
            UpdateButtonStates();
        }

        private void CreateUI()
        {
            // 创建加载按钮
            //_loadButton = new Button();
            //_loadButton.Text = "加载音频";
           _loadButton.Pressed += OnLoadButtonPressed;
           // AddChild(_loadButton);

            // 创建播放按钮
            //_playButton = new Button();
            //_playButton.Text = "播放";
            _playButton.Pressed += OnPlayButtonPressed;
           // _playButton.Position = new Vector2(0, 40);
           // AddChild(_playButton);

            // 创建停止按钮
            //_stopButton = new Button();
            //_stopButton.Text = "停止";
            _pauseButton.Pressed += OnPauseButtonPressed;
            
            //_stopButton.Position = new Vector2(0, 80);
           // AddChild(_stopButton);

            // 创建状态标签
           // _statusLabel = new Label();
            _statusLabel.Text = "未加载音频";
            //_statusLabel.Position = new Vector2(0, 120);
            //AddChild(_statusLabel);
        }

        private void OnLoadButtonPressed()
        {
            // 显示文件对话框
            _fileDialog.PopupCentered(new Vector2I(500, 400));
        }

        private void OnFileSelected(string path)
        {
            try
            {
                // 根据文件扩展名加载音频
                string extension = path.GetExtension().ToLower();

                switch (extension)
                {
                    case "wav":
                        _loadedAudio = LoadWav(path);
                        break;
                    case "ogg":
                        _loadedAudio = LoadOgg(path);
                        break;
                    case "mp3":
                        _loadedAudio = LoadMp3(path);
                        break;
                    case "flac":
                        GD.PrintErr("FLAC格式暂不支持");
                        return;
                    default:
                        _statusLabel.Text = "不支持的音频格式: " + extension;
                        return;
                }

                if (_loadedAudio != null)
                {
                    _audioPlayer.Stream = _loadedAudio;
                    _statusLabel.Text = "已加载: " + path.GetFile();
                    
                    UpdateButtonStates();
                }
                else
                {
                    _statusLabel.Text = "加载音频失败";
                }
            }
            catch (Exception e)
            {
                _statusLabel.Text = "错误: " + e.Message;
                GD.PrintErr("加载音频时出错: ", e.Message);
            }
        }

        private AudioStream LoadWav(string path)
        {
            var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var stream = AudioStreamWav.LoadFromBuffer(file.GetBuffer((long)file.GetLength()));
                file.Close();
                return stream;
            }

            return null;
        }

        private AudioStream LoadOgg(string path)
        {
            var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var stream = AudioStreamOggVorbis.LoadFromBuffer(file.GetBuffer((long)file.GetLength()));
                file.Close();
                return stream;
            }

            return null;
        }

        private AudioStream LoadMp3(string path)
        {
            var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var stream = AudioStreamMP3.LoadFromBuffer(file.GetBuffer((long)file.GetLength()));
                file.Close();
                return stream;
            }

            return null;
        }

        private void OnPlayButtonPressed()
        {
            if (_audioPlayer.Stream != null)
            {
                _audioPlayer.Play(_curAudioPos);
                _statusLabel.Text = "播放中...";
                UpdateButtonStates();
            }
        }

        private void OnPauseButtonPressed()
        {
            _curAudioPos = _audioPlayer.GetPlaybackPosition();
            _audioPlayer.Stop();
            _statusLabel.Text = "已暂停";
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            _playButton.Disabled = _audioPlayer.Stream == null;
            _pauseButton.Disabled = _audioPlayer.Stream == null || !_audioPlayer.Playing;
        }

        public override void _Process(double delta)
        {
            // 更新按钮状态
            if (_audioPlayer.Stream != null && !_audioPlayer.Playing && _statusLabel.Text == "播放中...")
            {
                _statusLabel.Text = "播放完成";
                UpdateButtonStates();
            }
        }
    }
}