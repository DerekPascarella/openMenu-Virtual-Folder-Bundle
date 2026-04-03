using GDMENUCardManager.Core;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for infoWindow.xaml
    /// </summary>
    public partial class InfoWindow : Window, INotifyPropertyChanged
    {
        public string FileInfo { get; }

        private string _IpInfo = "Loading...";
        public string IpInfo
        {
            get { return _IpInfo; }
            private set { _IpInfo = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IpInfo))); }
        }

        private string _LabelText = "Loading...";
        public string LabelText
        {
            get { return _LabelText; }
            private set { _LabelText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelText))); }
        }

        private BitmapImage _GdTexture = null;
        public BitmapImage GdTexture
        {
            get { return _GdTexture; }
            private set { _GdTexture = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GdTexture))); }
        }

        private GdItem item;

        public event PropertyChangedEventHandler PropertyChanged;


        public InfoWindow(GdItem item)
        {
            InitializeComponent();
            Loaded += InfoWindow_Loaded;

            this.item = item;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Folder: {Path.GetFileName(item.FullFolderPath)}");
            sb.Append($"File: {Path.GetFileName(item.ImageFile)}");

            FileInfo = sb.ToString();

            if (item.FileFormat != FileFormat.Uncompressed)
            {
                IpInfo = "Compressed file";
            }

            this.KeyUp += (ss, ee) => { if (ee.Key == Key.Escape) Close(); };
            DataContext = this;

        }

        private async void InfoWindow_Loaded(object sender, RoutedEventArgs e)
        {
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

                    using (MemoryStream memory = new MemoryStream())
                    {
                        byte[] data = decoded.Item1;

                        using (Bitmap img = new Bitmap(decoded.Item2, decoded.Item3, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                        {
                            BitmapData bitmapData = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, img.PixelFormat);
                            System.Runtime.InteropServices.Marshal.Copy(data, 0, bitmapData.Scan0, data.Length);
                            img.UnlockBits(bitmapData);

                            img.Save(memory, ImageFormat.Png);
                            memory.Position = 0;
                            BitmapImage bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.StreamSource = memory;
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.EndInit();
                            GdTexture = bitmapImage;
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
