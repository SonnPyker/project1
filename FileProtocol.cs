using System.Net.Sockets;
using System.Text;

namespace Base64Transfer;

/// <summary>
/// Gói tin nhị phân đơn giản:
///     [tên file (UTF-8)] [\n] [bytes gốc của file]
/// Bên gửi đóng kết nối => bên nhận biết là đã hết dữ liệu.
/// </summary>
public static class FileProtocol
{
    private const byte NewLine = (byte)'\n';

    /// <summary>Ghép tên file + '\n' + nội dung thành 1 mảng byte.</summary>
    public static byte[] Encode(string filePath, byte[] fileBytes)
    {
        byte[] name = Encoding.UTF8.GetBytes(Path.GetFileName(filePath));

        byte[] packet = new byte[name.Length + 1 + fileBytes.Length];
        name.CopyTo(packet, 0);                       // phần 1: tên file
        packet[name.Length] = NewLine;                // phần 2: dấu phân cách
        fileBytes.CopyTo(packet, name.Length + 1);    // phần 3: bytes gốc, giữ nguyên
        return packet;
    }

    /// <summary>Tìm '\n' đầu tiên: trước nó là tên file, sau nó là nội dung.</summary>
    public static (string FileName, byte[] FileBytes) Decode(byte[] packet)
    {
        int pos = -1;
        for (int i = 0; i < packet.Length; i++)
        {
            if (packet[i] == NewLine) { pos = i; break; }
        }
        if (pos < 0) throw new InvalidDataException("Gói tin thiếu ký tự xuống dòng.");

        string fileName = Path.GetFileName(Encoding.UTF8.GetString(packet, 0, pos)); // bỏ đường dẫn, chặn "../"

        byte[] fileBytes = new byte[packet.Length - pos - 1];
        Array.Copy(packet, pos + 1, fileBytes, 0, fileBytes.Length);
        return (fileName, fileBytes);
    }

    public static async Task SendAsync(string host, int port, byte[] packet)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port);
        using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(packet);
    } // ra khỏi khối using => đóng kết nối => bên nhận biết đã hết

    /// <summary>Đọc đến khi bên gửi đóng kết nối.</summary>
    public static async Task<byte[]> ReceiveAllAsync(TcpClient client)
    {
        using NetworkStream stream = client.GetStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
