using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class InfoWindow : Window, INotifyPropertyChanged
    {
        public string FileInfo { get; }

        private string _IpInfo = "Loading...";
        public string IpInfo
        {
            get { return _IpInfo; }
            private set { _IpInfo = value; RaisePropertyChanged(); }
        }

        private string _LabelText = "Loading...";
        public string LabelText
        {
            get { return _LabelText; }
            private set { _LabelText = value; RaisePropertyChanged(); }
        }

        private Avalonia.Media.Imaging.Bitmap _GdTexture = null;
        public Avalonia.Media.Imaging.Bitmap GdTexture
        {
            get { return _GdTexture; }
            private set { _GdTexture = value; RaisePropertyChanged(); }
        }

        private GdItem item;
        public new event PropertyChangedEventHandler PropertyChanged;

        public InfoWindow() { }

        public InfoWindow(GdItem item)
        {
            InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
            //this.OpenDevTools();
#endif

            this.Opened += InfoWindow_Opened;

            this.item = item;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Folder: {Path.GetFileName(item.FullFolderPath)}");
            sb.Append($"File: {Path.GetFileName(item.ImageFile)}");

            FileInfo = sb.ToString();

            if (item.FileFormat != FileFormat.Uncompressed)
            {
                IpInfo = "Compressed file";
            }

            this.KeyUp += (ss, ee) => { if (ee.Key == Avalonia.Input.Key.Escape) Close(); };
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void InfoWindow_Opened(object sender, EventArgs e)
        {
            await Task.Delay(100);

            if (item.FileFormat == FileFormat.SevenZip)
            {
                IpInfo = "Can't load from compressed files.";
                LabelText = "Can't load from compressed files.";
                return;
            }

            var filePath = Path.Combine(item.FullFolderPath, item.ImageFile);

            // Load IP.BIN data
            try
            {
                var ip = await ImageHelper.GetIpBinFromImage(filePath);
                if (ip != null)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"Title: {ip.Name}");
                    sb.AppendLine($"Version: {ip.Version}");
                    sb.AppendLine($"Disc: {ip.Disc}");
                    sb.AppendLine($"VGA: {(ip.Vga ? "Yes" : "No")}");
                    sb.AppendLine($"Serial: {ip.ProductNumber}");
                    sb.Append($"Region: {ip.Region}");

                    if (ip.SpecialDisc != SpecialDisc.None)
                    {
                        sb.AppendLine();
                        sb.Append("Detected as: " + ip.SpecialDisc);
                    }
                    IpInfo = sb.ToString();
                }
                else
                {
                    IpInfo = "Could not read IP.BIN";
                }
            }
            catch (Exception ex)
            {
                IpInfo = $"Error: {ex.Message}";
            }

            // Load GDTEX texture
            try
            {
                var gdtexture = await Task.Run(() => ImageHelper.GetGdText(filePath));

                if (gdtexture == null)
                {
                    LabelText = "Unable to find or read file";
                }
                else
                {
                    var decoded = await Task.Run(() => new PuyoTools.PvrTexture().GetDecoded(gdtexture));

                    using (var memory = new MemoryStream())
                    {
                        byte[] data = decoded.Item1;
                        using (var writeableBitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(decoded.Item2, decoded.Item3), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul))
                        {
                            using (var l = writeableBitmap.Lock())
                                System.Runtime.InteropServices.Marshal.Copy(data, 0, l.Address, data.Length);

                            writeableBitmap.Save(memory);
                            memory.Position = 0;
                            GdTexture = new Avalonia.Media.Imaging.Bitmap(memory);
                            LabelText = null;
                        }
                    }
                }
            }
            catch
            {
                LabelText = "Unable to find or read file";
            }
        }
    }
}
