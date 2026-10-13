using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gitstar.Desktop;

public sealed class OwnerAvatar : Grid
{
    public static readonly DependencyProperty RepositoryProperty=DependencyProperty.Register(nameof(Repository),typeof(string),typeof(OwnerAvatar),new PropertyMetadata("",Changed));
    public string Repository {get=>(string)GetValue(RepositoryProperty);set=>SetValue(RepositoryProperty,value);}
    private readonly TextBlock fallback=new(){FontSize=16,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(103,116,142))};
    private readonly Image avatar=new(){Stretch=Stretch.UniformToFill};
    public OwnerAvatar()
    {
        Width=38;Height=38;Background=new SolidColorBrush(Color.FromRgb(230,234,244));
        Clip=new RectangleGeometry(new Rect(0,0,38,38),9,9);Children.Add(fallback);Children.Add(avatar);
        Loaded+=async(_,_)=>await Load();
    }
    private static void Changed(DependencyObject sender,DependencyPropertyChangedEventArgs e)
    {
        var view=(OwnerAvatar)sender;view.avatar.Source=null;view.fallback.Text=view.Repository.Split('/')[0].FirstOrDefault().ToString().ToUpperInvariant();
        view.ToolTip="仓库作者："+view.Repository.Split('/')[0];if(view.IsLoaded)_=view.Load();
    }
    private async Task Load()
    {
        var repository=Repository;var image=await AvatarCache.Load(repository.Split('/')[0]);
        if(Repository==repository)avatar.Source=image;
    }
}

public static class AvatarCache
{
    private static HttpClient? http;
    private static bool offline;
    private static readonly SemaphoreSlim slots=new(4);
    private static readonly ConcurrentDictionary<string,(DateTimeOffset Created,Lazy<Task<BitmapSource?>> Image)> images=new(StringComparer.OrdinalIgnoreCase);
    public static void Configure(Settings settings,bool smoke=false)
    {
        offline=smoke;images.Clear();http?.Dispose();
        var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=!settings.DirectConnection};
        if(settings.Proxy.Length>0)handler.Proxy=new WebProxy(settings.Proxy);
        http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(20)};http.DefaultRequestHeaders.UserAgent.ParseAdd("Gitstar");
    }
    public static Task<BitmapSource?> Load(string owner)
    {
        if(offline || !Regex.IsMatch(owner,@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}$"))return Task.FromResult<BitmapSource?>(null);
        if(images.TryGetValue(owner,out var existing) && DateTimeOffset.UtcNow-existing.Created>TimeSpan.FromMinutes(5) && existing.Image.IsValueCreated && existing.Image.Value.IsCompletedSuccessfully && existing.Image.Value.Result==null)images.TryRemove(owner,out _);
        var result=images.GetOrAdd(owner,key=>(DateTimeOffset.UtcNow,new Lazy<Task<BitmapSource?>>(()=>Fetch(key)))).Image.Value;
        if(images.Count>64)foreach(var item in images.Where(p=>p.Value.Image.IsValueCreated && p.Value.Image.Value.IsCompleted).OrderBy(p=>p.Value.Created).Take(images.Count-64))images.TryRemove(item.Key,out _);
        return result;
    }
    private static async Task<BitmapSource?> Fetch(string owner)
    {
        var client=http;if(client==null)return null;
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Gitstar","avatars",owner.ToLowerInvariant()+"_avatar.img");
        await slots.WaitAsync();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if(File.Exists(path) && DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromDays(7))
            {
                try{return Decode(await File.ReadAllBytesAsync(path));}catch{File.Delete(path);}
            }
            var url=new Uri("https://github.com/"+owner+".png?size=96");
            for(var redirect=0;redirect<4;redirect++)
            {
                if(url.Scheme!="https" || url.Host is not ("github.com" or "avatars.githubusercontent.com"))return null;
                // Public avatar requests never carry the OAuth token.
                using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                if((int)response.StatusCode is >=300 and <400 && response.Headers.Location is {} location){url=location.IsAbsoluteUri?location:new Uri(url,location);continue;}
                response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>1024*1024)return null;
                using var input=await response.Content.ReadAsStreamAsync(timeout.Token);using var output=new MemoryStream();var buffer=new byte[8192];int count;
                while((count=await input.ReadAsync(buffer,timeout.Token))>0){if(output.Length+count>1024*1024)return null;output.Write(buffer,0,count);}
                var bytes=output.ToArray();var image=Decode(bytes);try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes);}catch(IOException){}catch(UnauthorizedAccessException){}return image;
            }
        }
        catch{try{if(File.Exists(path))return Decode(await File.ReadAllBytesAsync(path));}catch{} }
        finally{slots.Release();}
        return null;
    }
    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=96;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
}

