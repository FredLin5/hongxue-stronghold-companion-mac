// Integration harness for the PlayCover ("MaaTools" TCP protocol) capture path.
//
// Starts an in-process mock of the PlayTools MaaTools server, points
// GameConnectionService at it, then verifies screenshot decoding, shop OCR and touch.
using System.Net;
using System.Net.Sockets;
using System.Text;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

const int Port = 17171;
var root = FindRepoRoot();
var dataDir = Path.Combine(root, "data");
var testDir = Path.Combine(root, "src/ASPAssistant.Tests/TestData");
var sample = Path.Combine(testDir, "shop-terminal-tier1-color.png");
var expected = new[] { "虹彩活血饮料", "卡达", "慕斯", "角峰" };

using var image = Raster.Decode(File.ReadAllBytes(sample))!;
var rgba = ToRgba(image);
Console.WriteLine($"mock image {image.Width}x{image.Height} rgba={rgba.Length}");

var touches = new List<(byte Phase, int X, int Y, int Contact)>();
using var cts = new CancellationTokenSource();
var listener = StartMock(rgba, image.Width, image.Height, touches, cts.Token);

var settings = new GameConnectionSettings
{
    Source = GameCaptureSource.PlayCover,
    PlayCoverAddress = $"127.0.0.1:{Port}",
    PlayCoverBundleId = "com.hypergryph.arknights",
};
using var connection = new GameConnectionService(settings);
var frame = connection.CaptureDeviceScreen(1920);
if (frame is null)
{
    Console.WriteLine($"FAIL capture: {connection.Status}");
    return 1;
}

var (width, height) = ImageBytes.GetDimensions(frame);
Console.WriteLine($"captured {width}x{height} status={connection.Status}");

var operators = await new ASPAssistant.Core.Data.JsonDataStore(dataDir).LoadOperatorsAsync();
var (equipment, _) = await new ASPAssistant.Core.Data.JsonDataStore(dataDir).LoadEquipmentAsync();
var shopNames = operators.Select(o => o.Name).Concat(equipment.Select(e => e.Name)).Distinct().ToList();

using var ocr = new MaaOcrEngine(dataDir);
var reader = new ShopReader(ocr, shopNames) { Theme = GameShopTheme.Terminal };
var items = await reader.ReadAsync(frame, width, height, shopNames);
Console.WriteLine($"shop items: {string.Join(" / ", items.Select(i => i.Name))}");

var detector = new GameShopThemeDetector(Path.Combine(dataDir, "shop_theme_refs.json"));
Console.WriteLine($"theme: {detector.Detect(frame)}");

var tapped = connection.Tap(500, 400);
Thread.Sleep(200);
Console.WriteLine($"tap sent={tapped} touches={string.Join(",", touches.Select(t => $"{t.Phase}@{t.X},{t.Y}"))}");

var scrolled = connection.ScrollInformationPanelDown();
Thread.Sleep(200);
Console.WriteLine($"scroll sent={scrolled} touches={touches.Count}");

var namesOk = expected.All(name => items.Any(i => i.Name == name));
var touchOk = touches.Count >= 2 && touches.Any(t => t.Phase == 0) && touches.Any(t => t.Phase == 3);
var ok = namesOk && touchOk && width == image.Width && height == image.Height;
Console.WriteLine(ok ? "PLAYCOVER PATH PASS" : "PLAYCOVER PATH FAIL");
cts.Cancel();
listener.Stop();
return ok ? 0 : 1;

static TcpListener StartMock(byte[] rgba, int width, int height, List<(byte, int, int, int)> touches, CancellationToken ct)
{
    var listener = new TcpListener(IPAddress.Loopback, Port);
    listener.Start();
    _ = Task.Run(async () =>
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (Exception ex) { Console.WriteLine($"mock: accept ended: {ex.Message}"); return; }
            Console.WriteLine("mock: client connected");
            _ = Task.Run(() => ServeAsync(client, rgba, width, height, touches, ct));
        }
    }, ct);
    return listener;
}

static async Task ServeAsync(TcpClient client, byte[] rgba, int width, int height,
    List<(byte, int, int, int)> touches, CancellationToken ct)
{
  try
  {
    using var stream = client.GetStream();
    var handshake = new byte[4];
    await stream.ReadExactlyAsync(handshake, ct);
    if (Encoding.ASCII.GetString(handshake) != "MAA\0")
    {
        Console.WriteLine("mock: bad handshake");
        return;
    }
    await stream.WriteAsync(Encoding.ASCII.GetBytes("OKAY"), ct);

    while (!ct.IsCancellationRequested)
    {
        var header = new byte[2];
        try { await stream.ReadExactlyAsync(header, ct); }
        catch { return; }
        int length = (header[0] << 8) | header[1];
        var payload = new byte[length];
        try { await stream.ReadExactlyAsync(payload, ct); }
        catch { return; }
        var command = Encoding.ASCII.GetString(payload, 0, Math.Min(4, payload.Length));
        switch (command)
        {
            case "VERN":
                await stream.WriteAsync(BigEndian(4u), ct);
                break;
            case "SIZE":
                var size = new byte[4];
                size[0] = (byte)(width >> 8); size[1] = (byte)width;
                size[2] = (byte)(height >> 8); size[3] = (byte)height;
                await stream.WriteAsync(size, ct);
                break;
            case "SCRN":
                await stream.WriteAsync(BigEndian((uint)rgba.Length), ct);
                await stream.WriteAsync(rgba, ct);
                break;
            case "TUCH":
                // MaaFramework 5.8 sends phase + x + y (5 bytes); PlayTools ignores a missing contact.
                touches.Add((payload[4], (payload[5] << 8) | payload[6], (payload[7] << 8) | payload[8],
                    payload.Length >= 10 ? payload[9] : 0));
                break;
            case "TERM":
                return;
            default:
                Console.WriteLine($"mock: unknown {command}");
                break;
        }
    }
  }
  catch (Exception ex)
  {
    Console.WriteLine($"mock: connection ended: {ex.GetType().Name} {ex.Message}");
  }
}

static byte[] BigEndian(uint value) =>
    [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

static byte[] ToRgba(Raster image)
{
    // The MaaFramework PlayCover client reads the payload as RGBA and converts to BGR.
    var pixels = image.PixelsBgra;
    var rgba = new byte[pixels.Length];
    for (int i = 0; i < pixels.Length; i += 4)
    {
        rgba[i] = pixels[i + 2];
        rgba[i + 1] = pixels[i + 1];
        rgba[i + 2] = pixels[i];
        rgba[i + 3] = 255;
    }
    return rgba;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "data", "operators.json"))) return dir.FullName;
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("repository root (data/operators.json) not found");
}
