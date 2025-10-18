using Godot;
using System;
using System.Linq;
using CrystalPhoenix.Scripts;
using NAudio.CoreAudioApi;
using System.Collections.Generic;
using NAudio.Wave;

public partial class DeviceOptionButton : OptionButton
{
    [Export]
    public NAudioCaptureAudioPlayer player {  get; set; }
    
    private Dictionary<int, MMDevice> _devices = new Dictionary<int, MMDevice>();
    
    public override void _Ready()
    { 
        var devs = NAudioCaptureAudioPlayer.GetRenderDevices();

        this._devices = new Dictionary<int, MMDevice>();
        
        var target = -1;

        for (int i = 0; i < devs.Count(); i++)
        {
            var dev = devs.ElementAt(i);
            if (dev.State == DeviceState.Active)
            {
                if (dev.ID ==  WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice().ID)
                {
                    GD.Print(i);
                    target = i;
                }
                _devices[i] = dev;
                this.AddItem(dev.FriendlyName, i);
            }
            Select(target);
        }
        

        this.ItemSelected += index =>
        {
            if (index >= 0 && index < _devices.Count)
            {
                player.InitializeNAudioCapture(_devices[this.Selected]);
            }
        };
    }
}
