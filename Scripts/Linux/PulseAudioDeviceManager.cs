using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace Crystal.Scripts.Linux;

/// <summary>
/// 使用 PulseAudio API 获取音频设备列表
/// </summary>
public partial class PulseAudioDeviceManager : RefCounted
{
    #region PulseAudio Native Bindings
    private const string PULSE_LIB = "libpulse.so.0";
    
    // 上下文回调委托
    private delegate void pa_context_success_cb_t(IntPtr context, int success, IntPtr userdata);
    
    // 源信息回调委托
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void pa_source_info_cb_t(IntPtr context, IntPtr info, int eol, IntPtr userdata);
    
    // 接收器信息回调委托
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void pa_sink_info_cb_t(IntPtr context, IntPtr info, int eol, IntPtr userdata);
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_mainloop_new();
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_mainloop_get_api(IntPtr loop);
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_context_new(IntPtr api, [MarshalAs(UnmanagedType.LPStr)] string name);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_context_connect(IntPtr context, [MarshalAs(UnmanagedType.LPStr)] string server, 
        uint flags, IntPtr api);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_context_get_state(IntPtr context);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_context_get_source_info_list(IntPtr context, pa_source_info_cb_t cb, IntPtr userdata);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_context_get_sink_info_list(IntPtr context, pa_sink_info_cb_t cb, IntPtr userdata);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_context_disconnect(IntPtr context);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_context_unref(IntPtr context);
    
    [DllImport(PULSE_LIB)]
    private static extern int pa_mainloop_iterate(IntPtr loop, int block, out int retval);
    
    [DllImport(PULSE_LIB)]
    private static extern void pa_mainloop_free(IntPtr loop);
    
    [DllImport(PULSE_LIB)]
    private static extern IntPtr pa_strerror(int error);
    
    // 源信息结构体
    [StructLayout(LayoutKind.Sequential)]
    private struct pa_source_info
    {
        public IntPtr name;                // 设备名称
        public uint index;                 // 设备索引
        public IntPtr description;         // 设备描述
        public pa_sample_spec sample_spec; // 采样规格
        public uint channel_map;           // 声道映射
        public uint owner_module;          // 所属模块
        public uint volume;                // 音量
        public IntPtr monitor_of_sink;     // 关联的接收器（如果是监视器）
        public uint monitor_of_sink_name;  // 关联接收器名称
        public ulong latency;              // 延迟
        public IntPtr driver;              // 驱动名称
        public uint flags;                 // 标志位
        public uint configured_latency;    // 配置的延迟
        public uint base_volume;           // 基准音量
        public uint state;                 // 状态
        public uint n_volume_steps;        // 音量步数
        public uint card;                  // 声卡索引
        public uint n_formats;             // 格式数量
        public IntPtr formats;             // 格式列表
    }
    
    // 接收器信息结构体
    [StructLayout(LayoutKind.Sequential)]
    private struct pa_sink_info
    {
        public IntPtr name;                // 设备名称
        public uint index;                 // 设备索引
        public IntPtr description;         // 设备描述
        public pa_sample_spec sample_spec; // 采样规格
        public uint channel_map;           // 声道映射
        public uint owner_module;          // 所属模块
        public uint volume;                // 音量
        public ulong latency;              // 延迟
        public IntPtr driver;              // 驱动名称
        public uint flags;                 // 标志位
        public uint configured_latency;    // 配置的延迟
        public uint base_volume;           // 基准音量
        public uint state;                 // 状态
        public uint n_volume_steps;        // 音量步数
        public uint card;                  // 声卡索引
        public uint n_formats;             // 格式数量
        public IntPtr formats;             // 格式列表
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct pa_sample_spec
    {
        public int format;
        public uint rate;
        public byte channels;
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
    
    // 源标志位
    [Flags]
    private enum pa_source_flags : uint
    {
        PA_SOURCE_NOFLAGS = 0x0000U,
        PA_SOURCE_HW_VOLUME_CTRL = 0x0001U,    // 支持硬件音量控制
        PA_SOURCE_LATENCY = 0x0002U,           // 支持延迟查询
        PA_SOURCE_HARDWARE = 0x0004U,          // 硬件设备
        PA_SOURCE_NETWORK = 0x0008U,           // 网络设备
        PA_SOURCE_HW_MUTE_CTRL = 0x0010U,      // 支持硬件静音控制
        PA_SOURCE_DECIBEL_VOLUME = 0x0020U,    // 支持分贝音量
        PA_SOURCE_DYNAMIC_LATENCY = 0x0040U,   // 动态延迟
        PA_SOURCE_FLAT_VOLUME = 0x0080U,       // 平坦音量
        PA_SOURCE_DONT_MOVE = 0x0100U,         // 不要移动
        PA_SOURCE_DONT_INVALIDATE = 0x0200U,   // 不要失效
    }
    
    // 状态枚举
    private enum pa_source_state : uint
    {
        PA_SOURCE_INVALID_STATE = 0xFFFFFFFFU,
        PA_SOURCE_RUNNING = 0x0000U,
        PA_SOURCE_IDLE = 0x0001U,
        PA_SOURCE_SUSPENDED = 0x0002U,
    }
    #endregion
    
    #region 设备信息类
    /// <summary>
    /// PulseAudio 设备信息
    /// </summary>
    public class PulseAudioDeviceInfo
    {
        public string Name { get; set; }           // 设备名称
        public string Description { get; set; }    // 设备描述
        public uint Index { get; set; }           // 设备索引
        public bool IsMonitor { get; set; }       // 是否是监视器设备
        public bool IsInput { get; set; }         // 是否是输入设备
        public uint SampleRate { get; set; }      // 采样率
        public byte Channels { get; set; }        // 声道数
        public string Driver { get; set; }        // 驱动程序
        public uint State { get; set; }           // 设备状态
        
        public override string ToString()
        {
            string type = IsInput ? (IsMonitor ? "监视器" : "输入") : "输出";
            string stateStr = GetStateString();
            return $"{Description} ({Name}) [{type}] {SampleRate}Hz {Channels}ch [{stateStr}]";
        }
        
        private string GetStateString()
        {
            return State switch
            {
                0 => "运行中",
                1 => "空闲",
                2 => "暂停",
                _ => "未知"
            };
        }
    }
    #endregion
    
    #region 获取设备列表的实现
    /// <summary>
    /// 获取所有音频设备（包括输入和输出）
    /// </summary>
    public List<PulseAudioDeviceInfo> GetAllAudioDevices()
    {
        var devices = new List<PulseAudioDeviceInfo>();
        
        IntPtr mainloop = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;
        
        try
        {
            // 1. 创建主循环
            mainloop = pa_mainloop_new();
            if (mainloop == IntPtr.Zero)
            {
                GD.PrintErr("无法创建 PulseAudio 主循环");
                return devices;
            }
            
            var api = pa_mainloop_get_api(mainloop);
            
            // 2. 创建上下文
            context = pa_context_new(api, "Godot Device Enumerator");
            if (context == IntPtr.Zero)
            {
                GD.PrintErr("无法创建 PulseAudio 上下文");
                return devices;
            }
            
            // 3. 连接上下文
            int result = pa_context_connect(context, null, 0, IntPtr.Zero);
            if (result < 0)
            {
                GD.PrintErr($"无法连接到 PulseAudio 服务器: {Marshal.PtrToStringAnsi(pa_strerror(result))}");
                return devices;
            }
            
            // 4. 等待连接就绪
            if (!WaitForContextReady(context, mainloop))
            {
                GD.PrintErr("PulseAudio 上下文未就绪");
                return devices;
            }
            
            GD.Print("PulseAudio 连接成功，开始枚举设备...");
            
            // 5. 获取源设备（输入设备，包括监视器）
            var sources = new List<PulseAudioDeviceInfo>();
            var sourceCallbackDone = false;
            
            pa_source_info_cb_t sourceCallback = (ctx, infoPtr, eol, userdata) =>
            {
                if (eol != 0)
                {
                    sourceCallbackDone = true;
                    return;
                }
                
                if (infoPtr != IntPtr.Zero)
                {
                    var info = Marshal.PtrToStructure<pa_source_info>(infoPtr);
                    var device = new PulseAudioDeviceInfo
                    {
                        Name = Marshal.PtrToStringAnsi(info.name) ?? "unknown",
                        Description = Marshal.PtrToStringAnsi(info.description) ?? "Unknown Device",
                        Index = info.index,
                        IsInput = true,
                        IsMonitor = info.monitor_of_sink != IntPtr.Zero,
                        SampleRate = info.sample_spec.rate,
                        Channels = info.sample_spec.channels,
                        Driver = Marshal.PtrToStringAnsi(info.driver) ?? "unknown",
                        State = info.state
                    };
                    sources.Add(device);
                }
            };
            
            pa_context_get_source_info_list(context, sourceCallback, IntPtr.Zero);
            
            // 等待源信息回调完成
            while (!sourceCallbackDone && WaitForIteration(mainloop))
            {
                // 继续迭代
            }
            
            GD.Print($"找到 {sources.Count} 个源设备");
            
            // 6. 获取接收器设备（输出设备）
            var sinks = new List<PulseAudioDeviceInfo>();
            var sinkCallbackDone = false;
            
            pa_sink_info_cb_t sinkCallback = (ctx, infoPtr, eol, userdata) =>
            {
                if (eol != 0)
                {
                    sinkCallbackDone = true;
                    return;
                }
                
                if (infoPtr != IntPtr.Zero)
                {
                    var info = Marshal.PtrToStructure<pa_sink_info>(infoPtr);
                    var device = new PulseAudioDeviceInfo
                    {
                        Name = Marshal.PtrToStringAnsi(info.name) ?? "unknown",
                        Description = Marshal.PtrToStringAnsi(info.description) ?? "Unknown Device",
                        Index = info.index,
                        IsInput = false,
                        IsMonitor = false,
                        SampleRate = info.sample_spec.rate,
                        Channels = info.sample_spec.channels,
                        Driver = Marshal.PtrToStringAnsi(info.driver) ?? "unknown",
                        State = info.state
                    };
                    sinks.Add(device);
                }
            };
            
            pa_context_get_sink_info_list(context, sinkCallback, IntPtr.Zero);
            
            // 等待接收器信息回调完成
            while (!sinkCallbackDone && WaitForIteration(mainloop))
            {
                // 继续迭代
            }
            
            GD.Print($"找到 {sinks.Count} 个接收器设备");
            
            // 7. 合并所有设备
            devices.AddRange(sources);
            devices.AddRange(sinks);
            
            // 8. 按类型排序
            devices.Sort((a, b) =>
            {
                // 先按类型：监视器 > 输入 > 输出
                int typeCompare = GetTypePriority(b).CompareTo(GetTypePriority(a));
                if (typeCompare != 0) return typeCompare;
                
                // 再按状态：运行中 > 空闲 > 暂停
                int stateCompare = b.State.CompareTo(a.State);
                if (stateCompare != 0) return stateCompare;
                
                // 最后按描述排序
                return string.Compare(a.Description, b.Description, StringComparison.Ordinal);
            });
            
            GD.Print($"总共找到 {devices.Count} 个音频设备");
        }
        catch (Exception e)
        {
            GD.PrintErr($"获取音频设备失败: {e.Message}");
            GD.PrintErr($"堆栈跟踪: {e.StackTrace}");
            
            // 回退到默认设备
            devices.Add(new PulseAudioDeviceInfo
            {
                Name = "default",
                Description = "默认设备",
                IsInput = true,
                IsMonitor = false,
                SampleRate = 48000,
                Channels = 2,
                State = 0
            });
        }
        finally
        {
            // 清理资源
            if (context != IntPtr.Zero)
            {
                pa_context_disconnect(context);
                pa_context_unref(context);
            }
            
            if (mainloop != IntPtr.Zero)
            {
                pa_mainloop_free(mainloop);
            }
        }
        
        return devices;
    }
    
    /// <summary>
    /// 获取特定类型的设备
    /// </summary>
    public List<PulseAudioDeviceInfo> GetAudioDevices(string deviceType = "all")
    {
        var allDevices = GetAllAudioDevices();
        
        return deviceType.ToLower() switch
        {
            "input" => allDevices.FindAll(d => d.IsInput && !d.IsMonitor),
            "monitor" => allDevices.FindAll(d => d.IsInput && d.IsMonitor),
            "output" => allDevices.FindAll(d => !d.IsInput),
            "all" => allDevices,
            _ => allDevices
        };
    }
    
    /// <summary>
    /// 获取设备名称列表（兼容旧接口）
    /// </summary>
    public List<string> GetAudioDeviceNames(string deviceType = "all")
    {
        var devices = GetAudioDevices(deviceType);
        var names = new List<string>();
        
        foreach (var device in devices)
        {
            // 对于监视器设备，添加特殊标记
            string prefix = device.IsMonitor ? "[MONITOR] " : 
                           device.IsInput ? "[INPUT] " : "[OUTPUT] ";
            names.Add($"{prefix}{device.Description} ({device.Name})");
        }
        
        if (names.Count == 0)
        {
            names.Add("default");
        }
        
        return names;
    }
    
    /// <summary>
    /// 根据描述查找设备名称
    /// </summary>
    public string FindDeviceByName(string description)
    {
        var devices = GetAllAudioDevices();
        
        foreach (var device in devices)
        {
            if (device.Description.Contains(description, StringComparison.OrdinalIgnoreCase) ||
                device.Name.Contains(description, StringComparison.OrdinalIgnoreCase))
            {
                return device.Name;
            }
        }
        
        return "default";
    }
    
    /// <summary>
    /// 查找默认的监视器设备（用于系统音频捕获）
    /// </summary>
    public string FindDefaultMonitorDevice()
    {
        var devices = GetAllAudioDevices();
        
        // 首先尝试查找默认输出设备的监视器
        foreach (var device in devices)
        {
            if (device.IsInput && device.IsMonitor)
            {
                // 检查是否是默认输出设备的监视器
                if (device.Description.Contains("Monitor of", StringComparison.OrdinalIgnoreCase))
                {
                    return device.Name;
                }
            }
        }
        
        // 如果没有找到，返回第一个监视器设备
        foreach (var device in devices)
        {
            if (device.IsInput && device.IsMonitor)
            {
                return device.Name;
            }
        }
        
        // 如果没有监视器设备，返回默认设备
        return "default";
    }
    
    /// <summary>
    /// 查找默认输入设备
    /// </summary>
    public string FindDefaultInputDevice()
    {
        var devices = GetAllAudioDevices();
        
        // 查找第一个输入设备（非监视器）
        foreach (var device in devices)
        {
            if (device.IsInput && !device.IsMonitor && device.State == 0) // 运行中
            {
                return device.Name;
            }
        }
        
        // 如果没有运行的输入设备，返回第一个输入设备
        foreach (var device in devices)
        {
            if (device.IsInput && !device.IsMonitor)
            {
                return device.Name;
            }
        }
        
        return "default";
    }
    #endregion
    
    #region 辅助方法
    /// <summary>
    /// 等待上下文就绪
    /// </summary>
    private bool WaitForContextReady(IntPtr context, IntPtr mainloop)
    {
        int maxAttempts = 100; // 10秒超时
        int attempt = 0;
        
        while (attempt < maxAttempts)
        {
            var state = (pa_context_state)pa_context_get_state(context);
            
            switch (state)
            {
                case pa_context_state.READY:
                    return true;
                case pa_context_state.FAILED:
                case pa_context_state.TERMINATED:
                    GD.PrintErr($"PulseAudio 上下文状态: {state}");
                    return false;
                default:
                    Thread.Sleep(100); // 等待 100ms
                    attempt++;
                    
                    // 处理 PulseAudio 事件
                    int retval;
                    pa_mainloop_iterate(mainloop, 0, out retval);
                    break;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// 等待主循环迭代
    /// </summary>
    private bool WaitForIteration(IntPtr mainloop)
    {
        try
        {
            int retval;
            pa_mainloop_iterate(mainloop, 1, out retval);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 获取设备类型优先级
    /// </summary>
    private int GetTypePriority(PulseAudioDeviceInfo device)
    {
        if (device.IsInput && device.IsMonitor) return 3;     // 监视器最高优先级
        if (device.IsInput && !device.IsMonitor) return 2;    // 输入设备次之
        return 1;                                            // 输出设备最低
    }
    
    /// <summary>
    /// 检查 PulseAudio 是否可用
    /// </summary>
    public static bool IsPulseAudioAvailable()
    {
        try
        {
            // 尝试加载 PulseAudio 库
            IntPtr handle = pa_mainloop_new();
            if (handle != IntPtr.Zero)
            {
                pa_mainloop_free(handle);
                return true;
            }
        }
        catch (DllNotFoundException)
        {
            GD.Print("PulseAudio 库未找到");
        }
        catch (Exception e)
        {
            GD.Print($"检查 PulseAudio 失败: {e.Message}");
        }
        
        return false;
    }
    #endregion
    
    #region 实用工具方法
    /// <summary>
    /// 获取设备简表（用于 UI 显示）
    /// </summary>
    public string[] GetDeviceSummary()
    {
        var devices = GetAllAudioDevices();
        var summary = new string[devices.Count];
        
        for (int i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            string icon = device.IsMonitor ? "🔊" : 
                         device.IsInput ? "🎤" : "🔈";
            string stateIcon = device.State == 0 ? "▶️" : 
                              device.State == 1 ? "⏸️" : "⏹️";
            
            summary[i] = $"{stateIcon} {icon} {device.Description} ({device.SampleRate}Hz)";
        }
        
        return summary;
    }
    
    /// <summary>
    /// 创建设备字典映射（显示名称 -> 实际设备名称）
    /// </summary>
    public Dictionary<string, string> CreateDeviceDictionary()
    {
        var dict = new Dictionary<string, string>();
        var devices = GetAllAudioDevices();
        
        foreach (var device in devices)
        {
            string key = $"{device.Description} ({device.Name})";
            if (!dict.ContainsKey(key))
            {
                dict[key] = device.Name;
            }
        }
        
        // 添加默认设备
        if (!dict.ContainsKey("默认设备 (default)"))
        {
            dict["默认设备 (default)"] = "default";
        }
        
        return dict;
    }
    #endregion
}