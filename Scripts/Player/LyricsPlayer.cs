using System.Collections.Generic;
using CrystalPhoenix.Scripts.LyricsLoader.Lrc;
using Godot;

namespace CrystalPhoenix.Scripts.Player
{
    /// <summary>
    /// 歌词播放器
    /// </summary>
    [GlobalClass]
    public partial class LyricsPlayer : Node
    {
        [Export] 
        public AudioStreamPlayer AudioPlayer;
        [Export] 
        public LrcLoader LrcLoader;
        
        [Export]
        public Label LyricsLabel;
        
        private List<double> _sortedTimestamps;
        private int _currentLyricIndex = -1;
        private string _lastDisplayedLyric = "";
        
        public override void _Ready()
        {
            if (AudioPlayer == null)
            {
                GD.PrintErr("AudioPlayer is not assigned!");
                return;
            }
            
            // 确保有歌词加载器引用
            if (LrcLoader == null)
            {
                GD.PrintErr("LrcLoader is not assigned!");
                return;
            }
            
            // 等待歌词加载完成
            if (LrcLoader.LrcContentDict.Count == 0)
            {
                GD.Print("Waiting for LrcLoader to process lyrics...");
                Callable.From(SetupLyricsPlayer).CallDeferred();
                return;
            }
            
            SetupLyricsPlayer();
        }
        
        private void SetupLyricsPlayer()
        {
            // 获取排序后的时间戳列表
            _sortedTimestamps = new List<double>(LrcLoader.LrcContentDict.Keys);
            _sortedTimestamps.Sort();
            
            GD.Print($"Lyrics loaded with {_sortedTimestamps.Count} timestamps");
            
            // 重置状态
            _currentLyricIndex = -1;
            _lastDisplayedLyric = "";
            
            AudioPlayer.Play();
        }
        
        public override void _Process(double delta)
        {
            // 如果没有音频播放或歌词数据，直接返回
            if (AudioPlayer == null || !AudioPlayer.Playing || 
                LrcLoader.LrcContentDict.Count == 0 || _sortedTimestamps == null)
            {
                return;
            }
            
            // 获取当前播放时间
            double currentTime = AudioPlayer.GetPlaybackPosition();
            
            // 查找当前应该显示的歌词
            var newIndex = FindCurrentLyricIndex(currentTime);
            
            // 如果歌词索引发生变化，更新显示的歌词
            if (newIndex == _currentLyricIndex) return;
            _currentLyricIndex = newIndex;
                
            if (_currentLyricIndex >= 0 && _currentLyricIndex < _sortedTimestamps.Count)
            {
                var timestamp = _sortedTimestamps[_currentLyricIndex];
                var lyric = LrcLoader.LrcContentDict[timestamp];

                if (lyric == _lastDisplayedLyric) return;
                _lastDisplayedLyric = lyric;
                        
                LyricsLabel.SetText(lyric);
                GD.Print($"[{TimeToString(timestamp)}] {lyric}");
            }
            else
            {
                // 没有找到合适的歌词
                if (string.IsNullOrEmpty(_lastDisplayedLyric)) return;
                _lastDisplayedLyric = "";
                LyricsLabel.SetText(_lastDisplayedLyric);
            }
        }
        
        /// <summary>
        /// 使用二分查找确定当前时间对应的歌词索引
        /// </summary>
        private int FindCurrentLyricIndex(double currentTime)
        {
            var left = 0;
            var right = _sortedTimestamps.Count - 1;
            var result = -1;
            
            while (left <= right)
            {
                var mid = left + (right - left) / 2;
                
                if (_sortedTimestamps[mid] <= currentTime)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// 将秒数转换为时间字符串 (mm:ss.ms)
        /// </summary>
        private string TimeToString(double time)
        {
            var minutes = (int)(time / 60);
            var seconds = (int)(time % 60);
            var milliseconds = (int)((time - (int)time) * 100);
            
            return $"{minutes:D2}:{seconds:D2}.{milliseconds:D2}";
        }
        
        /// <summary>
        /// 手动设置播放时间（用于测试或特殊情况）
        /// </summary>
        private void SetPlaybackTime(double time)
        {
            var newIndex = FindCurrentLyricIndex(time);

            if (newIndex < 0 || newIndex >= _sortedTimestamps.Count) return;
            _currentLyricIndex = newIndex;
            var timestamp = _sortedTimestamps[_currentLyricIndex];
            var lyric = LrcLoader.LrcContentDict[timestamp];
            
            _lastDisplayedLyric = lyric;
            GD.Print($"[{TimeToString(timestamp)}] {lyric}");
        }
    }
}