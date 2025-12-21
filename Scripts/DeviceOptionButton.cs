using System.Collections.Generic;
using System.Linq;
using Crystal.Scripts.AudioSpectrum;
using Crystal.Scripts.Linux;
using Godot;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Crystal.Scripts;

public partial class DeviceOptionButton : OptionButton
{
    [Export] public AudioSpectrumManager  _spectrumManager;
    [Export] public Linux.PulseAudioCaptureAudioPlayer player { get; set; }

    private Dictionary<int, string> _devices = new Dictionary<int, string>();

    public override void _Ready()
    {
        player = _spectrumManager.AudioStreamPlayer as PulseAudioCaptureAudioPlayer;
        
        var devs = player.GetAudioDevices();

        this._devices = new Dictionary<int, string>();

        var target = -1;

        for (int i = 0; i < devs.Count(); i++)
        {
            var dev = devs.ElementAt(i);
            if (dev != null)
            {
                this._devices[i] = dev;
                this.AddItem(dev, i);
            }

        }
        
        this.ItemSelected += index =>
        {
            if (index >= 0 && index < _devices.Count)
            {
                if (player.IsRunning)
                {
                    player.StopAudioCapture();
                }

                player.InitializePulseAudioCapture(_devices[this.Selected]);
            }
        };
    }
}