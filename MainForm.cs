using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Base64Transfer;

public class MainForm : Form
{
    private readonly TextBox txtIp = new() { Text = "127.0.0.1", Width = 120 };
    private readonly NumericUpDown numSendPort = NewPortBox();
    private readonly Button btnChoose = new() { Text = "Chọn file...", AutoSize = true };
    private readonly Button btnSend = new() { Text = "Gửi", AutoSize = true, Enabled = false };
    private readonly PictureBox picSend = NewPictureBox();

    private readonly NumericUpDown numListenPort = NewPortBox();
    private readonly Button btnListen = new() { Text = "Bắt đầu nhận", AutoSize = true };
    private readonly PictureBox picReceive = NewPictureBox();

    private readonly TextBox txtHex = NewReadOnlyBox();
    private readonly TextBox txtLog = NewReadOnlyBox();

    private string? selectedFile;
    private TcpListener? listener;
    private CancellationTokenSource? listenCts;

    private const int HexPreviewBytes = 512; // chỉ hiện 512 byte đầu cho nhẹ

    private const string FileFilter =
        "Tất cả file|*.*" +
        "|Ảnh|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico" +
        "|Tài liệu|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt" +
        "|Âm thanh / video|*.mp3;*.wav;*.mp4;*.mkv;*.avi" +
        "|File nén|*.zip;*.rar;*.7z";

    public MainForm()
    {
        Text = "Truyền file nhị phân qua LAN";
        Size = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;

        var sendBar = NewBar(new Label { Text = "IP nhận:", AutoSize = true, Anchor = AnchorStyles.Left },
                             txtIp, new Label { Text = "Port:", AutoSize = true, Anchor = AnchorStyles.Left },
                             numSendPort, btnChoose, btnSend);
        var receiveBar = NewBar(new Label { Text = "Port:", AutoSize = true, Anchor = AnchorStyles.Left },
                                numListenPort, btnListen);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(6) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.Controls.Add(NewGroup("GỬI", picSend, sendBar), 0, 0);
        layout.Controls.Add(NewGroup("NHẬN", picReceive, receiveBar), 1, 0);
        layout.Controls.Add(NewGroup($"Bytes của file dạng hex ({HexPreviewBytes} byte đầu)", txtHex), 0, 1);
        layout.Controls.Add(NewGroup("Nhật ký", txtLog), 1, 1);
        Controls.Add(layout);

        btnChoose.Click += (_, _) => ChooseFile();
        btnSend.Click += async (_, _) => await SendFileAsync();
        btnListen.Click += (_, _) => ToggleListening();
        FormClosing += (_, _) => StopListening();
    }


    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Filter = FileFilter };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        selectedFile = dialog.FileName;
        byte[] bytes = File.ReadAllBytes(selectedFile);
        ShowFile(picSend, bytes);
        btnSend.Enabled = true;
        Log($"Đã chọn: {selectedFile} ({bytes.Length:N0} bytes)");
    }

    private async Task SendFileAsync()
    {
        if (selectedFile is null) return;
        btnSend.Enabled = false;
        try
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(selectedFile);
            byte[] packet = FileProtocol.Encode(selectedFile, fileBytes);
            await FileProtocol.SendAsync(txtIp.Text.Trim(), (int)numSendPort.Value, packet);
            Log($"Đã gửi {packet.Length:N0} bytes (file {fileBytes.Length:N0} + tên file) " +
                $"tới {txtIp.Text}:{numSendPort.Value}");
        }
        catch (Exception ex)
        {
            Log("LỖI gửi: " + ex.Message);
        }
        finally
        {
            btnSend.Enabled = true;
        }
    }


    private void ToggleListening()
    {
        if (listener is null) _ = ListenLoopAsync();
        else StopListening();
    }

    private async Task ListenLoopAsync()
    {
        int port = (int)numListenPort.Value;
        try
        {
            listener = new TcpListener(IPAddress.Any, port); // Any = nghe trên mọi card mạng
            listener.Start();
        }
        catch (Exception ex)
        {
            Log("LỖI mở cổng: " + ex.Message);
            listener = null;
            return;
        }

        listenCts = new CancellationTokenSource();
        btnListen.Text = "Dừng nhận";
        numListenPort.Enabled = false;
        Log($"Đang nghe ở cổng {port}... (IP máy này: {string.Join(", ", LocalIPv4())})");

        try
        {
            while (true)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(listenCts.Token);
                Log($"Kết nối từ {client.Client.RemoteEndPoint}");
                await HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) { /* người dùng bấm Dừng */ }
        catch (Exception ex) { Log("LỖI nhận: " + ex.Message); }
        finally
        {
            StopListening();
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            byte[] packet = await FileProtocol.ReceiveAllAsync(client);
            var (fileName, fileBytes) = FileProtocol.Decode(packet);

            string folder = Path.Combine(AppContext.BaseDirectory, "Received");
            Directory.CreateDirectory(folder);
            string savePath = Path.Combine(folder, fileName);
            await File.WriteAllBytesAsync(savePath, fileBytes);

            Log($"Nhận {packet.Length:N0} bytes -> file {fileBytes.Length:N0} bytes, lưu: {savePath}");
            ShowFile(picReceive, fileBytes);
        }
        catch (Exception ex)
        {
            Log("LỖI xử lý gói tin: " + ex.Message);
        }
    }

    private void StopListening()
    {
        listenCts?.Cancel();
        listener?.Stop();
        listener = null;
        btnListen.Text = "Bắt đầu nhận";
        numListenPort.Enabled = true;
    }


    /// <summary>Hiện hex của file; nếu là ảnh thì hiện thêm ảnh.</summary>
    private void ShowFile(PictureBox box, byte[] bytes)
    {
        txtHex.Text = ToHex(bytes);

        box.Image?.Dispose();
        box.Image = null;
        try
        {
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            box.Image = new Bitmap(img); // copy để không phụ thuộc vào stream đã đóng
        }
        catch (ArgumentException)
        {
            // không phải ảnh -> chỉ hiện hex
        }
    }

    /// <summary>Mỗi dòng 16 byte, mỗi byte 2 chữ số hex. VD: 89 50 4E 47 ... (đầu file PNG)</summary>
    private static string ToHex(byte[] bytes)
    {
        int count = Math.Min(bytes.Length, HexPreviewBytes);
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            sb.Append(bytes[i].ToString("X2")).Append(' ');
            if (i % 16 == 15) sb.AppendLine();
        }
        if (bytes.Length > count) sb.AppendLine().Append($"... (còn {bytes.Length - count:N0} bytes)");
        return sb.ToString();
    }

    private void Log(string message) =>
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private static IEnumerable<string> LocalIPv4() =>
        Dns.GetHostAddresses(Dns.GetHostName())
           .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
           .Select(a => a.ToString());

    private static NumericUpDown NewPortBox() => new() { Minimum = 1, Maximum = 65535, Value = 5000, Width = 70 };

    private static PictureBox NewPictureBox() => new()
    {
        Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle
    };

    private static TextBox NewReadOnlyBox() => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9)
    };

    private static FlowLayoutPanel NewBar(params Control[] controls)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        bar.Controls.AddRange(controls);
        return bar;
    }

    private static GroupBox NewGroup(string title, Control fill, Control? top = null)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill };
        group.Controls.Add(fill);           // Fill thêm trước...
        if (top is not null) group.Controls.Add(top); // ...Top thêm sau để nằm trên cùng
        return group;
    }
}
