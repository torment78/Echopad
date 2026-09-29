using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Echopad.App;
using Echopad.App.Services;
using Echopad.App.Settings;
using Echopad.Core;
using Echopad.Core.Controllers;
using Echopad.Core.Devices;
using NAudio.Midi;

static partial class Program
{
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception("FAILED: "+name);checks++;Console.WriteLine("PASS "+name);}
    [STAThread]
    static int Main()
    {
        try {
            var bindingErrors=new StringWriter();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(bindingErrors));
            PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
            var app=new Echopad.App.App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var dir=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));
            var settings=new SettingsService(dir);
            var devices=new Devices();
            UpdateChecks();
            GraphicsChecks(settings);
            CaptureAndWaveformChecks(settings);
            CopyChecks();
            Check(HotkeyTextBuilder.Build(Key.F1,ModifierKeys.Control|ModifierKeys.Shift)=="Ctrl+Shift+F1","Ctrl+Shift chord");
            Check(HotkeyTextBuilder.Build(Key.A,ModifierKeys.Control|ModifierKeys.Alt)=="Ctrl+Alt+A","Ctrl+Alt chord");
            Check(HotkeyTextBuilder.Build(Key.F2,ModifierKeys.Shift|ModifierKeys.Alt)=="Shift+Alt+F2","Shift+Alt chord");
            Check(HotkeyTextBuilder.Modifiers(ModifierKeys.Control|ModifierKeys.Shift)=="Ctrl+Shift","modifier-only chord");
            MidiChecks();
            VbanPreviewChecks();
            var edges=new MidiCcEdges();
            edges.Observe(1,22,99);Check(!edges.Crossed(1,22,100),"CC below threshold stays idle");
            edges.Observe(1,22,100);Check(edges.Crossed(1,22,100),"CC crossing triggers once");
            edges.Observe(1,22,127);Check(!edges.Crossed(1,22,100),"held CC does not retrigger");
            edges.Observe(1,22,0);edges.Observe(1,22,100);Check(edges.Crossed(1,22,100),"CC release rearms trigger");
            edges.ObserveSuppressedRelease(1,22,0);edges.ObserveSuppressedRelease(1,22,127);edges.Observe(1,22,100);
            Check(edges.Crossed(1,22,100),"suppressed release rearms without LED echo generating a press");
            using var vm=new SettingsViewModel(settings,devices,devices);
            vm.Profiles[2].Name="Voice clips";
            vm.Profiles[2].HotkeyBind="Ctrl+Alt+F3";
            vm.Profiles[2].MidiBind="CC:1:22:100";
            vm.Routes[0].VbanEnabled=true;
            Check(!vm.Routes[0].LocalEnabled && vm.Routes[0].VbanEnabled,"route switches exclusively to VBAN");
            vm.Routes[0].VbanEnabled=false;
            Check(!vm.Routes[0].LocalEnabled && !vm.Routes[0].VbanEnabled,"route can be fully off");
            vm.Routes[0].LocalEnabled=true;
            vm.AppearanceColors[6].Hue=320;
            vm.Save();
            var profiles=new ProfileService(settings);
            Check(profiles.GetProfile(3).Name=="Voice clips","profile name persisted");
            var saved=settings.Load();
            Check(saved.ProfileSwitch.Slots[2].HotkeyBind=="Ctrl+Alt+F3","slot hotkey persisted");
            Check(saved.Appearance.PadHue==320,"appearance persisted");
            vm.ProfileSearch="voice";
            Check(vm.ProfileView.Cast<object>().Count()==1,"profile search by name");
            vm.ProfileSearch="16";
            Check(vm.ProfileView.Cast<object>().Count()==1,"profile search by number");
            vm.ProfileSearch="";
            saved.GetOrCreatePad(4).ClipPath="external-capture.wav";settings.Save(saved);
            vm.Save();
            Check(settings.Load().GetOrCreatePad(4).ClipPath=="external-capture.wav","settings do not overwrite new pad data");
            var p2=profiles.GetProfile(2);p2.Pads[1]=new PadSettings{Index=1,MidiTriggerDisplay="NOTE:1:40:1",PadHotkey="F4"};profiles.UpdateProfile(p2);
            var linked=settings.Load();linked.ProfileSwitch.PadsMidiSameAsProfile1=true;linked.GetOrCreatePad(1).MidiTriggerDisplay="NOTE:1:99:1";linked.GetOrCreatePad(1).PadHotkey="F8";
            profiles.SavePadsToProfile(linked,2);
            Check(profiles.GetProfile(2).Pads[1].MidiTriggerDisplay=="NOTE:1:40:1","MIDI linking preserves independent triggers");
            Check(profiles.GetProfile(2).Pads[1].PadHotkey=="F8","MIDI-only linking permits independent hotkey edits");
            foreach(double hue in new[]{0d,90,180,270,359}) {
                var color=AppearanceTheme.ColorAt(hue,.25,.14);
                Check(Math.Max(color.R,Math.Max(color.G,color.B))==36,"pad brightness fixed at hue "+hue);
            }
            vm.Routes[0].UpdateMeter(-6);
            Check(vm.Routes[0].LocalDb==-6 && vm.Routes[0].VbanDb==-60,"only the active source meter responds");
            RenderChecks(vm,settings);
            Check(string.IsNullOrWhiteSpace(bindingErrors.ToString()),"no WPF binding errors: "+bindingErrors);
            Console.WriteLine($"SUCCESS: {checks} checks. Previews: {Path.Combine(AppContext.BaseDirectory,"previews")}");
            return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void CopyChecks()
    {
        var pads=Enumerable.Range(1,4).Select(n=>new PadModel(n)).ToList();
        pads[0].ClipPath="source.wav";pads[0].PadName="Hello";pads[0].GainDb=-4; pads[0].StartMs=150; pads[0].EndMs=850; pads[0].ClipDuration=TimeSpan.FromSeconds(1);
        pads[1].InputSource=2;
        pads[0].Graphics.PlayingImage="sample.png";
        var controller=new PadActionController(pads);int copies=0,stops=0;
        controller.PadCopied+=(_,_)=>copies++;controller.StopRequested+=_=>stops++;
        controller.SetEditMode(true);controller.SetCopyHeld(true);controller.ActivatePad(1);
        Check(pads[0].ClipMod==ClipMod.CopySource,"Ctrl copy selects source");
        controller.ActivatePad(2);controller.ActivatePad(3);
        Check(copies==2 && pads[2].ClipPath=="source.wav" && pads[1].StartMs==150 && pads[2].GainDb==-4 && pads[1].PadName=="Hello","copy transfers clip, trim, name and gain to multiple targets");
        Check(pads[1].InputSource==2,"copy preserves target routing");
        Check(pads[1].Graphics.PlayingImage=="sample.png" && !ReferenceEquals(pads[0].Graphics,pads[1].Graphics),"copy transfers independent graphics settings");
        pads[3].State=PadState.Playing;pads[3].IsBusy=true;controller.ActivatePad(4);
        Check(stops==1 && pads[3].ClipPath=="source.wav" && !pads[3].IsBusy,"copy stops target playback");
        controller.SetCopyHeld(false);
        Check(pads.All(p=>p.ClipMod==ClipMod.None),"Ctrl release clears all copy outlines");
        controller.SetCopyHeld(true);controller.ActivatePad(3);Check(pads[2].ClipMod==ClipMod.CopySource,"fresh Ctrl gesture chooses a new source");
        controller.SetEditMode(false);Check(pads.All(p=>p.ClipMod==ClipMod.None),"leaving Edit cancels copy");
    }
    static object? Invoke(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
    static void MidiChecks()
    {
        var cc=Invoke("TryParseMidiBind","CC:1:22:100|RAW:B0 16 64")!;
        Check((int)cc.GetType().GetProperty("MinValue")!.GetValue(cc)! == 100,"RAW suffix preserves CC threshold");
        Check(!(bool)Invoke("DoesEventMatchBind",new ControlChangeEvent(0,1,(MidiController)22,99),cc)!,"CC below threshold does not trigger");
        Check((bool)Invoke("DoesEventMatchBind",new ControlChangeEvent(0,1,(MidiController)22,100),cc)!,"CC threshold triggers");
        Check((bool)Invoke("IsRelease",new ControlChangeEvent(0,1,(MidiController)22,20),cc)!,"CC dropping below threshold releases modifier");
        var note=Invoke("TryParseMidiBind","NOTE:2:48:1")!;
        Check((bool)Invoke("IsRelease",new NoteEvent(0,2,MidiCommandCode.NoteOff,48,64),note)!,"real NoteOff releases modifier");
        Check((string?)Invoke("BuildMidiLearnBindText",new ControlChangeEvent(0,1,(MidiController)22,64))=="CC:1:22:64","learn accepts non-127 CC values");
    }
    static void VbanPreviewChecks()
    {
        using var receiver=new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,0));
        int port=((System.Net.IPEndPoint)receiver.Client.LocalEndPoint!).Port;
        using var player=new Echopad.Audio.Vban.VbanWavePlayer(new VbanTxSettings{RemoteIp="127.0.0.1",Port=port,StreamName="TEST_MONITOR",FrameSamples=128});
        using var source=new NAudio.Wave.RawSourceWaveStream(new MemoryStream(new byte[192000]),new NAudio.Wave.WaveFormat(48000,16,2));
        player.Init(source);player.Play();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var packet=receiver.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult().Buffer;
        Check(System.Text.Encoding.ASCII.GetString(packet,0,4)=="VBAN" && packet[5]==127 && packet[6]==1,"monitor preview sends correctly framed stereo VBAN on loopback");
        player.Pause();Check(player.PlaybackState==NAudio.Wave.PlaybackState.Paused,"VBAN preview pauses without a reader race");
        player.Stop();Check(player.PlaybackState==NAudio.Wave.PlaybackState.Stopped,"VBAN preview stops cleanly");
    }
    static void RenderChecks(SettingsViewModel vm,SettingsService settings)
    {
        // No windows are shown and no real audio or MIDI endpoints are started.
        vm.ProfileMidiModifier="CC:16:127:127|RAW:BF 7F 7F";
        AppearanceTheme.Apply(vm.Settings.Appearance);
        var window=new SettingsWindow(vm);
        var tabs=(TabControl)window.FindName("Pages");
        foreach(TabItem tab in tabs.Items) {
            tabs.SelectedItem=tab;
            Render((FrameworkElement)window.Content,"settings-"+tab.Tag,1010,720);
            if(Equals(tab.Tag,"Appearance")) Render((FrameworkElement)window.Content,"settings-Appearance-full",1010,1070);
            if(Equals(tab.Tag,"Folders")) Render((FrameworkElement)window.Content,"settings-Folders",1010,900);
            if(Equals(tab.Tag,"Profiles")) Render((FrameworkElement)window.Content,"settings-Profiles",1010,920);
            if(Equals(tab.Tag,"Audio")) {
                var routes=(ListBox)window.FindName("RouteList");routes.SelectedIndex=2;
                Render((FrameworkElement)window.Content,"settings-Audio-outputs",1010,720);
                routes.SelectedIndex=0;
            }
        }
        RenderUpdateAvailable(window,tabs);
        tabs.SelectedItem=tabs.Items.Cast<TabItem>().First(t=>Equals(t.Tag,"Profiles"));
        Render((FrameworkElement)window.Content,"settings-Profiles-compact",900,640);
        var main=new MainWindow(settings);
        Render((FrameworkElement)main.Content,"main",700,760);
        var model=(MainViewModel)main.DataContext;
        var demoGraphics=model.Pads[0].Graphics.Clone();
        model.Pads[0].Graphics=new();
        main.GlobalSettings.GetOrCreatePad(1).UiActiveHex="#FF3030";
        main.GlobalSettings.GetOrCreatePad(3).UiRunningHex="#00FF6A";
        main.GlobalSettings.GetOrCreatePad(4).UiActiveHex="#4D83FF";
        model.Pads[0].ClipPath="preview.wav";model.Pads[0].State=PadState.Loaded;model.Pads[0].PadName="A · Loaded";
        model.Pads[1].IsEchoMode=true;model.Pads[1].State=PadState.Armed;model.Pads[1].PadName="Listening";
        model.Pads[2].ClipPath="preview.wav";model.Pads[2].State=PadState.Playing;model.Pads[2].PadName="Playing";
        model.Pads[3].ClipPath="preview.wav";model.Pads[3].State=PadState.Loaded;model.Pads[3].PadName="Blue clip";
        Render((FrameworkElement)main.Content,"main-states",700,760);
        model.Pads[0].Graphics=demoGraphics;
        model.Pads[2].Graphics=demoGraphics.Clone();
        Render((FrameworkElement)main.Content,"main-artwork",700,760);
        model.Pads[0].Graphics=new();model.Pads[2].Graphics=new();
        var menu=(ContextMenu)typeof(MainWindow).GetMethod("CreateProfileDropdown",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,new object[]{main})!;
        Render(menu,"profile-dropdown",300,560);
        model.IsEditMode=true;
        model.Pads[0].ClipPath="preview.wav";model.Pads[0].State=PadState.Loaded;model.Pads[0].PadName="Voice clip";model.Pads[0].ClipMod=ClipMod.CopySource;
        model.Pads[1].IsEchoMode=true;model.Pads[1].State=PadState.Armed;
        model.Pads[2].ClipPath="preview.wav";model.Pads[2].State=PadState.Playing;
        Render((FrameworkElement)main.Content,"main-edit",650,690);
        var settingsContent=(FrameworkElement)window.Content;
        Check(Math.Abs(settingsContent.ActualWidth+settingsContent.Margin.Left+settingsContent.Margin.Right-900)<1,"settings layout measured successfully");
        var capture=new HotkeyCaptureWindow("Ctrl+Shift+F12");
        Render((FrameworkElement)capture.Content,"hotkey",408,178);
    }
    static void Render(FrameworkElement content,string name,int width,int height)
    {
        content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
        var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual();
        using(var drawing=background.RenderOpen())drawing.DrawRectangle(Window.GetWindow(content)?.Background??Brushes.Black,null,new Rect(0,0,width,height));
        image.Render(background);image.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
        var path=Path.Combine(AppContext.BaseDirectory,"previews");Directory.CreateDirectory(path);
        using var file=File.Create(Path.Combine(path,name+".png"));png.Save(file);
    }
    static T? FindChild<T>(DependencyObject parent) where T:DependencyObject
    {
        if(parent is T found)return found;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            if(FindChild<T>(VisualTreeHelper.GetChild(parent,i)) is T child)return child;
        return null;
    }
    sealed class Devices:IAudioDeviceProvider,IMidiDeviceProvider
    {
        public IReadOnlyList<DeviceItem> GetInputDevices()=>new[]{new DeviceItem("","System default input"),new DeviceItem("mic","Studio microphone")};
        public IReadOnlyList<DeviceItem> GetOutputDevices()=>new[]{new DeviceItem("","System default output"),new DeviceItem("headphones","Headphones")};
        public IReadOnlyList<DeviceItem> GetMidiInputs()=>Array.Empty<DeviceItem>();
        public IReadOnlyList<DeviceItem> GetMidiOutputs()=>Array.Empty<DeviceItem>();
    }
}
