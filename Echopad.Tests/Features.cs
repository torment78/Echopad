using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Echopad.App;
using Echopad.App.Services;
using Echopad.App.Settings;
using Echopad.App.UI.Controls;
using Echopad.Core;

static partial class Program
{
    static void UpdateChecks()
    {
        const string feed = """
        [
          {"tag_name":"snapshot-latest","name":"snapshot","html_url":"https://github.com/torment78/Echopad/releases/tag/snapshot-latest"},
          {"tag_name":"v1.1.0-dev.20260929.2","name":"Unsigned Dev Release","prerelease":true,"html_url":"https://github.com/torment78/Echopad/releases/tag/v1.1.0-dev.20260929.2"},
          {"tag_name":"v1.0.0.0.latest","name":"Old release","html_url":"https://github.com/torment78/Echopad/releases/tag/v1.0.0.0.latest"},
          {"tag_name":"v9.0.0","draft":true,"html_url":"https://github.com/torment78/Echopad/releases/tag/v9.0.0"},
          {"tag_name":"v8.0.0","html_url":"https://example.com/foreign-release"}
        ]
        """;
        using var client=new HttpClient(new FeedHandler(feed));
        var service=new UpdateService(client);
        var dev=service.CheckAsync("1.1.0-dev.20260929.1").GetAwaiter().GetResult();
        Check(dev.Available && dev.ReleaseName!.Contains("Unsigned Dev Release"),"dev updater finds newer numbered dev release and ignores snapshots/drafts/foreign links");
        var same=service.CheckAsync("1.1.0-dev.20260929.2").GetAwaiter().GetResult();
        Check(!same.Available && same.Message.Contains("newest"),"same development version does not offer an update");
        var stable=service.CheckAsync("1.0.0").GetAwaiter().GetResult();
        Check(!stable.Available,"stable updater excludes development releases and understands legacy tag");
        Check(ReleaseVersion.Parse("v1.1.0")!.CompareTo(ReleaseVersion.Parse("1.1.0-dev.999"))>0,"stable release supersedes same-version development builds");
        Check(ReleaseVersion.Parse("1.1.0-dev.10")!.CompareTo(ReleaseVersion.Parse("1.1.0-dev.2"))>0,"development build numbers compare numerically");
        using var malformed=new HttpClient(new FeedHandler("not json"));
        Check(new UpdateService(malformed).CheckAsync("1.0.0").Result.Message.Contains("could not be read"),"malformed update response reports failure");
        using var limited=new HttpClient(new FeedHandler("",HttpStatusCode.Forbidden));
        Check(new UpdateService(limited).CheckAsync("1.0.0").Result.Message.Contains("limiting"),"rate limit does not claim up to date");
        using var empty=new HttpClient(new FeedHandler("[]"));
        Check(new UpdateService(empty).CheckAsync("1.0.0").Result.Message.Contains("No versioned"),"empty feed does not claim up to date");
        using var offline=new HttpClient(new FeedHandler("",fail:true));
        Check(new UpdateService(offline).CheckAsync("1.0.0").Result.Message.Contains("connect"),"offline update check reports connection failure");
    }
    sealed class FeedHandler(string body,HttpStatusCode status=HttpStatusCode.OK,bool fail=false):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            if(fail)throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body)});
        }
    }
    static void GraphicsChecks(SettingsService settings)
    {
        Directory.CreateDirectory(settings.DataDirectory);
        string source=Path.Combine(settings.DataDirectory,"test-artwork.png");
        var drawing=new DrawingVisual();
        using(var dc=drawing.RenderOpen()) {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(232,145,57)),null,new Rect(0,0,400,200));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(35,145,175)),null,new Rect(100,0,200,200));
            for(int i=0;i<12;i++) {double h=25+Math.Sin(i*.7)*20;dc.DrawRoundedRectangle(Brushes.White,null,new Rect(65+i*23,100-h,10,h*2),5,5);}
        }
        var bitmap=new RenderTargetBitmap(400,200,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file=File.Create(source))encoder.Save(file);
        string imported=PadImageStore.Import(source,settings.DataDirectory);
        Check(imported==PadImageStore.Import(source,settings.DataDirectory),"PNG import deduplicates immutable copies");
        File.Delete(source);
        Check(PadImageStore.Load(imported)!=null,"imported PNG survives moving/deleting original");
        string invalid=Path.Combine(settings.DataDirectory,"invalid.png");File.WriteAllText(invalid,"not a PNG");
        bool rejected=false;try{PadImageStore.Import(invalid,settings.DataDirectory);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"invalid PNG rejected before persistence");
        var pad=new PadModel(1);
        var vm=new PadSettingsViewModel(pad,settings);
        vm.MidiLedActiveEntry="OFF";
        Check(!vm.MidiLedActiveEnabled && vm.MidiLedActiveEntry=="OFF","OFF disables pad MIDI LED feedback");
        vm.MidiLedActiveEntry="28";
        Check(vm.MidiLedActiveEnabled && vm.MidiLedActiveValue==28,"numeric LED value re-enables feedback");
        vm.ImportImage(false,imported);vm.ImportImage(true,imported);vm.StoppedFit=PadImageFit.Fit;vm.PlayingFit=PadImageFit.Crop;vm.StoppedOpacity=45;vm.PlayingOpacity=80;
        Check(pad.Graphics.StoppedImage==null && settings.Load().GetOrCreatePad(1).Graphics.StoppedImage==null,"graphics remain staged until Save pad");
        vm.Save();
        var saved=settings.Load().GetOrCreatePad(1).Graphics;
        Check(saved.StoppedImage==imported && saved.StoppedOpacity==45 && saved.PlayingOpacity==80 && saved.StoppedFit==PadImageFit.Fit && saved.PlayingFit==PadImageFit.Crop,"both PNG states, opacity and sizing persist");
        vm.StoppedOpacity=20;
        Check(pad.Graphics.StoppedOpacity==45,"editing graphics does not mutate saved runtime artwork");
        var profiles=new ProfileService(settings);profiles.SavePadsToProfile(settings.Load(),1);
        Check(profiles.GetProfile(1).Pads[1].Graphics.StoppedImage==imported,"profile stores managed PNG assignment");
        var artwork=new PadArtwork{Graphics=new PadGraphicsSettings{StoppedImage=imported,StoppedOpacity=100,StoppedFit=PadImageFit.Fit}};
        Check(AlphaAt(artwork,100,10)==0,"Fit leaves transparent space beyond image aspect ratio");
        artwork.Graphics=new PadGraphicsSettings{StoppedImage=imported,StoppedOpacity=100,StoppedFit=PadImageFit.Crop};
        Check(AlphaAt(artwork,100,10)==255,"Crop fills the pad");
        artwork.Graphics=new PadGraphicsSettings{StoppedImage=imported,StoppedOpacity=50,StoppedFit=PadImageFit.Crop};
        Check(AlphaAt(artwork,100,10) is >=126 and <=129,"PNG opacity affects the image independently");
        Check(AlphaAt(artwork,0,0)==0,"PNG corners stay clipped to the rounded pad");
        vm.StoppedOpacity=45;vm.PadName="Studio clip";vm.PadHotkey="Ctrl+Shift+F12";vm.MidiTriggerRaw="CC:16:127:127|RAW:BF 7F 7F";
        var window=new PadSettingsWindow(vm);var tabs=(TabControl)window.FindName("PadPages");
        foreach(TabItem tab in tabs.Items){tabs.SelectedItem=tab;Render((FrameworkElement)window.Content,"pad-"+tab.Tag,1060,940);}
        Render((FrameworkElement)window.Content,"pad-Graphics-compact",900,710);
    }
    static byte AlphaAt(PadArtwork artwork,int x,int y)
    {
        artwork.Measure(new Size(200,200));artwork.Arrange(new Rect(0,0,200,200));artwork.UpdateLayout();
        var image=new RenderTargetBitmap(200,200,96,96,PixelFormats.Pbgra32);image.Render(artwork);
        var pixel=new byte[4];image.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel[3];
    }
    static void RenderUpdateAvailable(SettingsWindow window,TabControl tabs)
    {
        tabs.SelectedItem=tabs.Items.Cast<TabItem>().First(t=>Equals(t.Tag,"Updates"));
        var asset = TestInstallerAsset(new byte[] { 1, 2, 3 });
        typeof(SettingsWindow).GetMethod("ShowUpdateResult",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{new UpdateResult(true,"Example: a newer release is ready to download and install.","Unsigned Dev Release (example version)","https://github.com/torment78/Echopad/releases/tag/example",asset)});
        Render((FrameworkElement)window.Content,"settings-Updates-available",1010,720);
        Check(((Button)window.FindName("OpenReleaseButton")).Visibility==Visibility.Visible,"available update exposes release link");
        Check(((Button)window.FindName("UpdateCheckButton")).Content?.ToString()=="Download and install" &&
            ((Button)window.FindName("RecheckUpdateButton")).Visibility==Visibility.Visible,"verified installer exposes download/install and check-again actions");
        ((ProgressBar)window.FindName("UpdateDownloadProgress")).Visibility=Visibility.Visible;
        ((Button)window.FindName("UpdateCheckButton")).IsEnabled=false;
        ((Button)window.FindName("RecheckUpdateButton")).IsEnabled=false;
        ((ProgressBar)window.FindName("UpdateDownloadProgress")).Value=63;
        ((Button)window.FindName("CancelUpdateButton")).Visibility=Visibility.Visible;
        ((TextBlock)window.FindName("UpdateStatus")).Text="Example: downloading installer… 63% (36.8 / 58.4 MB)";
        Render((FrameworkElement)window.Content,"settings-Updates-downloading",1010,720);
    }
    static void CaptureAndWaveformChecks(SettingsService settings)
    {
        var format=NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(8000,1);
        var buffer=new Echopad.Audio.RollingAudioBuffer(15,format);
        var samples=Enumerable.Range(0,8000*16).Select(i=>(float)(.65*Math.Sin(i*.05)*(0.5+0.5*Math.Sin(i*.0003)))).ToArray();
        buffer.AddSamples(samples[..(8000*13)],8000*13);buffer.AddSamples(samples[(8000*13)..],8000*3);
        Check(buffer.ReadAll().SequenceEqual(samples[8000..]),"rolling capture retains exactly the latest 15 seconds in chronological order");
        var clip=new Echopad.Audio.RollingBufferCommitService().CommitToWav(buffer,settings.DataDirectory,"waveform-example");
        using(var reader=new NAudio.Wave.WaveFileReader(clip.FilePath))
            Check(reader.TotalTime==TimeSpan.FromSeconds(15) && reader.WaveFormat.BitsPerSample==16,"rolling capture commits a readable 15-second PCM WAV");
        var wave=new SpectrogramTrimControl{AudioPath=clip.FilePath,DurationMs=15000,StartMs=1200,EndMs=13800};
        // An offscreen, non-activating WPF host exercises the real Loaded/worker path.
        var host=new Window{Content=wave,Width=640,Height=180,Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None};
        try {
            host.Show();
            var image=(Image)wave.FindName("Img");
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(image.Source==null && timer.ElapsedMilliseconds<5000) {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
                Thread.Sleep(10);
            }
            Check(image.Source!=null && typeof(SpectrogramTrimControl).GetField("_waveform",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(wave)!=null,"waveform worker loads real audio without cross-thread UI access");
            Render(wave,"waveform-trim",640,140);
        } finally {host.Close();}
    }
}
