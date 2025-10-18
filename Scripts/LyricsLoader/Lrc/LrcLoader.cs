using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace CrystalPhoenix.Scripts.LyricsLoader.Lrc
{
    /// <summary>
    /// Lrc格式歌词加载器
    /// </summary>
    [GlobalClass]
    public partial class LrcLoader : Node
    {
        public string LrcContent =
            "[00:00.00]半点心 - 草蜢\n" +
            "[00:05.13]词：潘源良\n" +
            "[00:10.26]曲：Perrier/Francois Bernheim/Elisabeth Depardieu/Bernard Estardy\n" +
            "[00:15.39]我说这里好吗\n" +
            "[00:19.73]你抬头而无话\n" +
            "[00:23.11]你抱我吻上我嘴巴\n" +
            "[00:27.46]却似你吻向他\n" +
            "[00:30.87]我暗中想总有一点爱吧\n" +
            "[00:33.77]可以交给我吧\n" +
            "[00:35.59]总算得恋爱吧\n" +
            "[00:37.59]相爱少点也罢\n" +
            "[00:39.47]我却更了解是\n" +
            "[00:42.48]编织梦话\n" +
            "[00:47.87]半点心\n" +
            "[00:49.97]请交给我不过是个小小愿望吧\n" +
            "[00:55.62]你的心\n" +
            "[00:57.85]却一早已整个完完全全交给他\n" +
            "[01:18.15]怕说到你跟他\n" +
            "[01:22.41]我说无穷傻话\n" +
            "[01:25.88]你听了永远笑哈哈\n" +
            "[01:30.22]我更言而无话\n" +
            "[01:33.73]你我之间总有一点爱吧\n" +
            "[01:36.58]可以交给我吧\n" +
            "[01:38.37]总算得恋爱吧\n" +
            "[01:40.43]相爱少点也罢\n" +
            "[01:42.43]我却更了解是\n" +
            "[01:44.95]编织梦话\n" +
            "[01:50.61]半点心\n" +
            "[01:52.75]请交给我不过是个小小愿望吧\n" +
            "[01:58.47]你的心\n" +
            "[02:00.66]却一早已整个完完全全交给他\n" +
            "[02:20.81]说过爱要潇洒\n" +
            "[02:25.19]错爱了回头吧\n" +
            "[02:28.75]到这晚却说半点心\n" +
            "[02:33.06]仍然求能留下\n" +
            "[02:36.54]你我之间总有一点爱吧\n" +
            "[02:39.36]可以交给我吧\n" +
            "[02:41.22]总算得恋爱吧\n" +
            "[02:43.16]相爱少点也罢\n" +
            "[02:45.28]我却更了解是\n" +
            "[02:48.14]编织梦话\n" +
            "[02:53.44]半点心\n" +
            "[02:55.63]请交给我不过是个小小愿望吧\n" +
            "[03:01.14]你的心\n" +
            "[03:03.45]却一早已整个完完全全交给他\n" +
            "[03:08.91]他跟你好吗\n" +
            "[03:11.34]一切的爱怎么都送给他\n" +
            "[03:15.77]一颗心分一半好吗\n" +
            "[03:19.20]起码一半都交给我好吗\n" +
            "[03:23.65]给我吗";
        [Export]
        public Godot.Collections.Array<LrcLine> Lines;
        
        [Export]
        public Godot.Collections.Dictionary<double, string> LrcContentDict = new();


        public override void _Ready()
        {
            // LoadLrcContent(LrcContent);
            // GenerateLrcDic();
        }

        public void LoadLrcContent(string content)
        {
            // 时间戳正则表达式
            Regex regex = new Regex(@"\[(\d{2}):(\d{2})\.(\d{2,4})\]");
            string[] lines = content.Split('\n');

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                MatchCollection matches = regex.Matches(line);
                Godot.Collections.Array<double> secs = new Godot.Collections.Array<double>();
                
                var lyrics = regex.Replace(line, "");
                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        int minutes = int.Parse(match.Groups[1].Value);
                        int seconds = int.Parse(match.Groups[2].Value);

                        int percentseconds = 0;
                        if (match.Groups[3].Success)
                        {
                            string msString = match.Groups[3].Value;
                            if (msString.Length == 1)
                            {
                                msString += "0";
                            }
                            else if (msString.Length > 2)
                            {
                                msString = msString.Substring(0, 2);
                            }
                        
                            percentseconds = int.Parse(msString);
                        }
                        secs.Add(ConvertToSeconds(minutes, seconds, percentseconds));
                    }

                    var lrcLine = new LrcLine(secs, lyrics);
                    Lines.Add(lrcLine);
                }
                

            }
        }

        private double ConvertToSeconds(int minutes, int seconds, int milliseconds)
        {
            // 明天别忘了改一下百分之一秒
            // 计算总秒数
            double totalSeconds = minutes * 60 + seconds + milliseconds / 100.0;
    
            return totalSeconds;
        }

        private void GenerateLrcDic()
        {
            LrcContentDict.Clear();
            foreach (var lrc in Lines)
            {
                foreach (var sec in lrc.Second)
                {
                    if (LrcContentDict.ContainsKey(sec))
                    {
                        continue;
                    }
                    LrcContentDict.Add(sec, lrc.Text);
                }
            }

            LrcContentDict = QuickSortLrcDict();
        }
        
        /// <summary>
        /// 对LrcContentDict按键进行快速排序
        /// </summary>
        /// <returns>排序后的键值对列表</returns>
        private Godot.Collections.Dictionary<double, string> QuickSortLrcDict()
        {
            var dict = LrcContentDict;
            var keys = new List<double>(dict.Keys);
            QuickSort(keys, 0, keys.Count - 1);
            
            var newDict = new Godot.Collections.Dictionary<double, string>();

            foreach (var key in keys)
            {
                newDict.Add(key, LrcContentDict[key]);
            }
            

            
            return newDict;
        }
        
        /// <summary>
        /// 快速排序递归实现
        /// </summary>
        private void QuickSort(List<double> keys, int left, int right)
        {
            if (left >= right) return;
            var pivotIndex = Partition(keys, left, right);
            QuickSort(keys, left, pivotIndex - 1);
            QuickSort(keys, pivotIndex + 1, right);
        }

        /// <summary>
        /// 快速排序分区操作
        /// </summary>
        private int Partition(List<double> keys, int left, int right)
        {
            var pivot = keys[right];
            var i = left - 1;
            
            for (var j = left; j < right; j++)
            {
                if (!(keys[j] <= pivot)) continue;
                i++;
                Swap(keys, i, j);
            }
            Swap(keys, i + 1, right);
            return i + 1;
        }

        /// <summary>
        /// 交换列表中两个元素的位置
        /// </summary>
        private static void Swap(List<double> list, int i, int j)
        {
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}