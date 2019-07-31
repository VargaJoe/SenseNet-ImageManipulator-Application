using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web;
using SenseNet.ApplicationModel;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage;
using SenseNet.Portal.Virtualization;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Linq;
using SenseNet.Services;

namespace SenseNet.Media.ContentHandler
{
    public enum ResizeTypeList
    {
        Resize = 1,
        Crop = 2,
        ResizeCrop = 3
    }

    public class Dimension
    {
        public long X = 0;
        public long Y = 0;
        public double ratio = 0;
        public bool IsPortrait = false;

        public Dimension() : this(0, 0) { }

        public Dimension(long imgX, long imgY)
        {
            X = imgX;
            Y = imgY;
            if (imgY != 0) ratio = (double)imgX / (double)imgY;
            IsPortrait = !(X > Y);
        }

        public Dimension GetRealSize(Dimension desired, bool largest)
        {
            Dimension resized = new Dimension();
            if (desired.X == 0 || ((largest ? desired.ratio < ratio : desired.ratio >= ratio)))
            {
                resized.Y = desired.Y;
                resized.X = (long)(ratio * resized.Y);
            }
            else
            {
                resized.X = desired.X;
                resized.Y = (long)(resized.X / ratio);
            }
            return resized;
        }
    }


    [ContentHandler]
    public class ImageHandlerApplication : Application, IHttpHandler
    {
        public ImageHandlerApplication(Node parent) : this(parent, "ImgResizeApplication") { }
        public ImageHandlerApplication(Node parent, string nodeTypeName) : base(parent, nodeTypeName) { }
        protected ImageHandlerApplication(NodeToken nt) : base(nt) { }
        //private const string CacheFolder = "/Root/Global/ResizedImages";


        public struct ImageObject
        {
            public byte[] imageData;
            public string imageFileName;
            public int imageId;
        }

        public struct ResolvedImageOptions
        {
            public long X;
            public long Y;
            public long Q;
            public string Default;
            public bool Largest;
            public bool Resize;
            public bool Crop;
            public bool OverCrop;
            public bool Dimensions;
            public bool Stretch;
            public long Top;
            public long Left;
            public long Bottom;
            public long Right;
            public long CropX;
            public long CropY;
            public string HorizontalAlignment;
            public string VerticalAlignment;
            public bool missing;
            //public string ResizerAppPath;
            //public int ResizerAppId;
            Content imgResAppCnt;

            public static char[] semiColonChar = new char[] { ';' };
            public static char[] colonChar = new char[] { ':' };

            public ResolvedImageOptions(ImageHandlerApplication imgResApp)
            {
                imgResAppCnt = Content.LoadByIdOrPath(imgResApp.Path);
                X = imgResApp.Width;
                Y = imgResApp.Height;
                Q = imgResApp.Quality;
                Default = string.Empty;
                Resize = IsChecked(imgResAppCnt, "ResizerOptions", "Resize");
                Largest = IsChecked(imgResAppCnt, "ResizerOptions", "Largest");
                Crop = IsChecked(imgResAppCnt, "ResizerOptions", "Crop");
                OverCrop = IsChecked(imgResAppCnt, "ResizerOptions", "OverCrop");
                Dimensions = IsChecked(imgResAppCnt, "ResizerOptions", "Dimensions");
                Stretch = IsChecked(imgResAppCnt, "ResizerOptions", "Stretch");
                Top = imgResApp.CropTop;
                Left = imgResApp.CropLeft;
                Bottom = imgResApp.CropBottom;
                Right = imgResApp.CropRight;
                CropX = imgResApp.CropWidth;
                CropY = imgResApp.CropHeight;
                HorizontalAlignment = GetChoiceOptionValue(imgResAppCnt, "CropHAlign").ToLower();
                VerticalAlignment = GetChoiceOptionValue(imgResAppCnt, "CropVAlign").ToLower();
                missing = IsChecked(imgResAppCnt, "ResizerOptions", "Missing");

                //ResizerAppPath = imgResApp.Path;
                //ResizerAppId = imgResApp.Id;

                ///further possible simplifications, but need to think through: 
                ///crop=true -> largest=true
                ///dimensions=true -> crop=true
                ///stretch=true -> largest=false, crop=false, dimensions=false
                ///pl: cmd=normal, largest, crop, stretch?

                //?????????
                //if (Dimensions) Crop = true;
                //if (Crop) Largest = true;
                //if (Stretch)
                //{
                //    Crop = false;
                //    Largest = false;
                //    Dimensions = false;
                //}
                //?????????
            }

        }

        //****************************************** Start of Repository Properties *********************************************//

        //[RepositoryProperty("ResizerOptions", RepositoryDataType.String)]
        //public IEnumerable<string> ResizerOptions
        //{
        //    get
        //    {
        //        var value = GetProperty<string>("ResizerOptions");
        //        var result = SenseNet.ContentRepository.Fields.ChoiceField.ConvertToStringList(value);
        //        return result;
        //    }
        //    set { this["ResizerOptions"] = SenseNet.ContentRepository.Fields.ChoiceField.ConvertToStringList(value); }
        //}

        [RepositoryProperty("ImageType", RepositoryDataType.String)]
        public string ImageType
        {
            get { return this.GetProperty<string>("ImageType"); }
            set { this["ImageType"] = value; }
        }

        [RepositoryProperty("ImageFieldName", RepositoryDataType.String)]
        public string ImageFieldName
        {
            get { return this.GetProperty<string>("ImageFieldName"); }
            set { this["ImageFieldName"] = value; }
        }

        [RepositoryProperty("Width", RepositoryDataType.Int)]
        public int Width
        {
            get { return this.GetProperty<int>("Width"); }
            set { this["Width"] = value; }
        }

        [RepositoryProperty("Height", RepositoryDataType.Int)]
        public int Height
        {
            get { return this.GetProperty<int>("Height"); }
            set { this["Height"] = value; }
        }

        [RepositoryProperty("Stretch", RepositoryDataType.Int)]
        public virtual bool Stretch
        {
            get { return (this.GetProperty<int>("Stretch") != 0); }
            set { this["Stretch"] = value ? 1 : 0; }
        }

        [RepositoryProperty("Largest", RepositoryDataType.Int)]
        public virtual bool Largest
        {
            get { return (this.GetProperty<int>("Largest") != 0); }
            set { this["Largest"] = value ? 1 : 0; }
        }

        [RepositoryProperty("CropWidth", RepositoryDataType.Int)]
        public int CropWidth
        {
            get { return this.GetProperty<int>("CropWidth"); }
            set { this["CropWidth"] = value; }
        }

        [RepositoryProperty("CropHeight", RepositoryDataType.Int)]
        public int CropHeight
        {
            get { return this.GetProperty<int>("CropHeight"); }
            set { this["CropHeight"] = value; }
        }

        [RepositoryProperty("CropTop", RepositoryDataType.Int)]
        public int CropTop
        {
            get { return this.GetProperty<int>("CropTop"); }
            set { this["CropTop"] = value; }
        }


        [RepositoryProperty("CropLeft", RepositoryDataType.Int)]
        public int CropLeft
        {
            get { return this.GetProperty<int>("CropLeft"); }
            set { this["CropLeft"] = value; }
        }

        [RepositoryProperty("CropBottom", RepositoryDataType.Int)]
        public int CropBottom
        {
            get { return this.GetProperty<int>("CropBottom"); }
            set { this["CropBottom"] = value; }
        }

        [RepositoryProperty("CropRight", RepositoryDataType.Int)]
        public int CropRight
        {
            get { return this.GetProperty<int>("CropRight"); }
            set { this["CropRight"] = value; }
        }

        [RepositoryProperty("Dimensions", RepositoryDataType.Int)]
        public virtual bool Dimensions
        {
            get { return (this.GetProperty<int>("Dimensions") != 0); }
            set { this["Dimensions"] = value ? 1 : 0; }
        }

        [RepositoryProperty("Quality", RepositoryDataType.Int)]
        public int Quality
        {
            get { return (this.HasProperty("Quality")) ? this.GetProperty<int>("Quality") : 100; }
            set { this["Quality"] = value; }
        }

        /// <summary>
        /// Specifies the output format of the resized Image. When it's set to 'Auto' it returns null.
        /// </summary>
        [RepositoryProperty("OutputFormat", RepositoryDataType.String)]
        public ImageFormat ResizeOutputFormat
        {
            get
            {
                if (this.GetProperty<string>("OutputFormat") == null) return null;

                switch (this.GetProperty<string>("OutputFormat").ToLower())
                {
                    case "jpeg": return ImageFormat.Jpeg;
                    case "png": return ImageFormat.Png;
                    case "icon": return ImageFormat.Icon;
                    case "tiff": return ImageFormat.Tiff;
                    case "gif": return ImageFormat.Gif;
                    case "auto": return null;
                    default: return ImageFormat.Png;
                }
            }
            set
            {
                if (value == ImageFormat.Jpeg) this["OutputFormat"] = "Jpeg";
                else if (value == ImageFormat.Png) this["OutputFormat"] = "Png";
                else if (value == ImageFormat.Gif) this["OutputFormat"] = "Gif";
                else if (value == ImageFormat.Tiff) this["OutputFormat"] = "Tiff";
                else if (value == ImageFormat.Icon) this["OutputFormat"] = "Icon";
                else if (value == null) { this["OutputFormat"] = "Auto"; }
                else this["OutputFormat"] = "Png";
            }
        }

        [RepositoryProperty("SmoothingMode", RepositoryDataType.String)]
        public SmoothingMode ResizeSmoothingMode
        {
            get
            {
                if (this.GetProperty<string>("SmoothingMode") == null) return SmoothingMode.AntiAlias;
                return (SmoothingMode)Enum.Parse(typeof(System.Drawing.Drawing2D.SmoothingMode), this.GetProperty<string>("SmoothingMode"), true);
            }
            set { this["SmoothingMode"] = ((SmoothingMode)value).ToString().ToLower(); }
        }

        [RepositoryProperty("InterpolationMode", RepositoryDataType.String)]
        public InterpolationMode ResizeInterpolationMode
        {
            get
            {
                if (this.GetProperty<string>("InterpolationMode") == null) return InterpolationMode.HighQualityBicubic;
                return (InterpolationMode)Enum.Parse(typeof(System.Drawing.Drawing2D.InterpolationMode), this.GetProperty<string>("InterpolationMode"), true);
            }
            set { this["InterpolationMode"] = ((InterpolationMode)value).ToString().ToLower(); }
        }

        [RepositoryProperty("PixelOffsetMode", RepositoryDataType.String)]
        public PixelOffsetMode ResizePixelOffsetMode
        {
            get
            {
                if (this.GetProperty<string>("PixelOffsetMode") == null) return PixelOffsetMode.HighQuality;
                return (PixelOffsetMode)Enum.Parse(typeof(System.Drawing.Drawing2D.PixelOffsetMode), this.GetProperty<string>("PixelOffsetMode"), true);
            }
            set { this["PixelOffsetMode"] = ((PixelOffsetMode)value).ToString().ToLower(); }
        }

        [RepositoryProperty("ResizeTypeMode", RepositoryDataType.String)]
        public ResizeTypeList ResizeType
        {
            get
            {
                if (this.GetProperty<string>("ResizeTypeMode") == null) return ResizeTypeList.Resize;
                return (ResizeTypeList)Enum.Parse(typeof(ResizeTypeList), this.GetProperty<string>("ResizeTypeMode").Replace(";", ""), true);
            }
            set { this["ResizeTypeMode"] = ((ResizeTypeList)value).ToString().ToLower(); }
        }

        [RepositoryProperty("CropVAlign", RepositoryDataType.String)]
        public string CropVAlign
        {
            get { return this.GetProperty<string>("CropVAlign"); }
            set { this["CropVAlign"] = value; }
        }

        [RepositoryProperty("CropHAlign", RepositoryDataType.String)]
        public string CropHAlign
        {
            get { return this.GetProperty<string>("CropHAlign"); }
            set { this["CropHAlign"] = value; }
        }

        //****************************************** Start of Properties *********************************************//

        public bool IsAutoOutputFormat
        {
            get { return ResizeOutputFormat == null; }
        }

        private string resizedImageExtension = null;
        /// <summary>
        /// The extension of the resized image file according to the value of OutputFormat field. It returns null when set to 'Auto'.
        /// </summary>
        public string ResizedImageExtension
        {
            get
            {
                if (!string.IsNullOrEmpty(resizedImageExtension))
                    return resizedImageExtension;

                var of = this.GetProperty<string>("OutputFormat") ?? string.Empty;
                switch (of.ToLower())
                {
                    case "jpeg": resizedImageExtension = ".jpg"; break;
                    case "png": resizedImageExtension = ".png"; break;
                    case "icon": resizedImageExtension = ".ico"; break;
                    case "tiff": resizedImageExtension = ".tif"; break;
                    case "gif": resizedImageExtension = ".gif"; break;
                    case "auto": resizedImageExtension = null; break;
                    default: resizedImageExtension = ".png"; break;
                }
                return resizedImageExtension;
            }
        }

        public override object GetProperty(string name)
        {
            switch (name.ToLower())
            {
                //case "resizeroptions": return this.ResizerOptions;
                case "imagetype": return this.ImageType;
                case "imagefieldname": return this.ImageFieldName;
                case "width": return this.Width;
                case "height": return this.Height;
                case "stretch": return this.Stretch;
                case "outputformat": return this.ResizeOutputFormat;
                case "smoothingmode": return this.ResizeSmoothingMode;
                case "interpolationmode": return this.ResizeInterpolationMode;
                case "pixeloffsetmode": return this.ResizePixelOffsetMode;
                case "resizetypemode": return this.ResizeType;
                case "cropvalign": return this.CropVAlign;
                case "crophalign": return this.CropHAlign;
                case "quality": return this.Quality;
                default: return base.GetProperty(name);
            }
        }
        public override void SetProperty(string name, object value)
        {
            switch (name.ToLower())
            {
                //case "resizeroptions":
                //    this.ResizerOptions = value as List<string>;
                //    break;
                case "imagetype":
                    this.ImageType = GetStringValue(value);
                    break;
                case "imagefieldname":
                    this.ImageFieldName = GetStringValue(value);
                    break;
                case "width":
                    try
                    {
                        int w = Int32.Parse(GetStringValue(value));
                        if (w < 0) throw new Exception();
                        this.Width = w;
                    }
                    catch (Exception)
                    {
                        throw new Exception("Property 'Width' is not a valid number or less than zero.");
                    }
                    break;
                case "height":
                    try
                    {
                        int h = Int32.Parse(GetStringValue(value));
                        if (h < 0) throw new Exception();
                        this.Height = h;
                    }
                    catch (Exception)
                    {
                        throw new Exception("Property 'Height' is not a valid number or less than zero.");
                    }
                    break;
                case "stretch":
                    try
                    {
                        this.Stretch = Boolean.Parse(GetStringValue(value));
                    }
                    catch (Exception)
                    {
                        throw new Exception("Property 'Stretch' is not a valid boolean value.");
                    }
                    break;
                case "outputformat": this.ResizeOutputFormat = GetImageFormat((value != null) ? value.ToString() : "auto"); break;
                case "smoothingmode": this.ResizeSmoothingMode = (SmoothingMode)Enum.Parse(ResizeSmoothingMode.GetType(), GetStringValue(value), true); break;
                case "interpolationmode": this.ResizeInterpolationMode = (InterpolationMode)Enum.Parse(ResizeInterpolationMode.GetType(), GetStringValue(value), true); break;
                case "pixeloffsetmode": this.ResizePixelOffsetMode = (PixelOffsetMode)Enum.Parse(ResizePixelOffsetMode.GetType(), GetStringValue(value), true); break;
                case "resizetypemode": this.ResizeType = (ResizeTypeList)Enum.Parse(ResizeType.GetType(), GetStringValue(value), true); break;
                case "cropvalign":
                    this.CropVAlign = GetStringValue(value);
                    break;
                case "crophalign":
                    this.CropHAlign = GetStringValue(value);
                    break;
                case "quality":
                    try
                    {
                        int q = (int)value;
                        if (q < 0) throw new Exception();
                        this.Quality = q;
                    }
                    catch (Exception)
                    {
                        throw new Exception("Property 'Quality' is not a valid number or less than zero.");
                    }
                    break;
                default:
                    base.SetProperty(name, value);
                    break;
            }
        }

        private static string GetStringValue(object value)
        {
            return value == null ? string.Empty : value.ToString();
        }

        /// <summary>
        /// Property of this application's cache folder where to put the resized images. Returns a full path pointing to a folder on the disk.
        /// </summary>
        private string AppCacheFolder
        {
            get
            {
                // get ResizedImagesCacheFolder setting from Web.config 
                var cacheFolder = "~/ResizedImagesCacheFolder"; //System.Configuration.ConfigurationManager.AppSettings["ResizedImagesCacheFolder"];
                if (String.IsNullOrEmpty(cacheFolder))
                    throw new Exception("Configuration for Image Resize Application could not be found.");

                var cacheFolderPathRoot = System.IO.Path.GetPathRoot(cacheFolder);

                // check if it is a path under the website or an absolute path to a folder on a disk
                if (cacheFolderPathRoot == @"\" || String.IsNullOrEmpty(cacheFolderPathRoot))
                {
                    // it is a directory under the website's root folder
                    return HttpContext.Current.Server.MapPath(cacheFolder + "/" + String.Format("{0}_{1}", DisplayName, this.Id.ToString()));
                }

                // it is a folder somewhere on a disk
                return cacheFolder;
            }
        }

        /// <summary>
        /// Returns a string representing the virtual path of the given image (as a node) in the cache folder where it is supposed to be located.
        /// </summary>
        /// <param name="contentPath">Path of image.</param>
        /// <returns>Returns the virtual cache path of the given image.</returns>
        private string GetImageCachePath(string contentPath)
        {
            // We need to cut the starting slash before "Root" and we also need to replace the other slashes in order to able to combine the paths.
            string fileName = System.IO.Path.Combine(AppCacheFolder, contentPath.Replace("/Root", "Root").Replace("/", "\\"));

            if (!String.IsNullOrEmpty(ResizedImageExtension) && System.IO.Path.GetExtension(fileName) != ResizedImageExtension)
            {
                fileName = fileName.Replace(System.IO.Path.GetExtension(fileName), ResizedImageExtension);
            }

            return fileName;
        }

        /// <summary>
        /// Checks if the cache folder is exists and if it doesn't the folder will be created.
        /// </summary>
        private void CheckCacheFolder()
        {
            if (!Directory.Exists(AppCacheFolder))
            {
                try
                {
                    Directory.CreateDirectory(AppCacheFolder);
                }
                catch (Exception)
                {
                    throw new Exception("Could not create this Image Resize Application's cache folder.");
                }
            }
        }

        /// <summary>
        /// Creates this application's cache folder. If it already exists an exception will be thrown.
        /// </summary>
        private void CreateCacheFolder()
        {
            if (!Directory.Exists(AppCacheFolder))
            {
                try
                {
                    Directory.CreateDirectory(AppCacheFolder);
                }
                catch (Exception)
                {
                    throw new Exception("Could not create this Image Resize Application's cache folder.");
                }
            }
            else
            {
                throw new Exception("Image Resize Application's cache folder is already exists.");
            }
        }

        /// <summary>
        /// Deletes this application's cache folder if it exists.
        /// </summary>
        private void DeleteCacheFolder()
        {
            if (Directory.Exists(AppCacheFolder))
            {
                try
                {
                    Directory.Delete(AppCacheFolder, true);
                }
                catch (Exception)
                {
                    throw new Exception("Could not delete this Image Resize Application's cache folder.");
                }
            }
        }

        /// <summary>
        /// Recreates cache folder. If folder does exist then it will be deleted.
        /// </summary>
        private void ReCreateCacheFolder()
        {
            DeleteCacheFolder();
            CreateCacheFolder();
        }

        /// <summary>
        /// Returns the mime type of the given image.
        /// </summary>
        /// <param name="imagePath">Path of image.</param>
        /// <returns>Returns the mime type.</returns>
        private string GetMimeType(string imagePath)
        {
            string ext = System.IO.Path.GetExtension(imagePath).ToLower();
            if (String.IsNullOrEmpty(ext))
                if (!string.IsNullOrEmpty(imagePath))
                    if (imagePath[0] == '/')//if path starts '/' it's a virtual path and could use MapPath
                        ext = System.IO.Path.GetExtension(HttpContext.Current.Server.MapPath(imagePath));
                    else // it's a physical don't need MapPath
                        ext = System.IO.Path.GetExtension(imagePath);
            switch (ext)
            {
                case ".jpg":
                case ".jpeg":
                case ".jpe":
                case ".jif":
                case ".jfif":
                case ".jfi":
                    return "image/jpeg";
                case ".gif":
                    return "image/gif";
                case ".png":
                    return "image/png";
                case ".ico":
                    return "image/ico";
                case ".svg":
                case ".svgz":
                    return "image/svg+xml";
                case ".tif":
                case ".tiff":
                    return "image/tiff";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Checks if the given image is cached on the disk.
        /// </summary>
        /// <param name="contentPath">Path of image in the Repository to be checked for existance on the disk.</param>
        /// <returns>Returns true if the given image can be found on the disk.</returns>
        private bool IsCached(string contentPath)
        {
            return System.IO.File.Exists(GetImageCachePath(contentPath));
        }

        protected override void OnCreating(object sender, SenseNet.ContentRepository.Storage.Events.CancellableNodeEventArgs e)
        {
            if (HttpContext.Current != null)
                ReCreateCacheFolder();
        }

        protected override void OnModifying(object sender, SenseNet.ContentRepository.Storage.Events.CancellableNodeEventArgs e)
        {
            if (HttpContext.Current != null)
                ReCreateCacheFolder();
        }

        protected override void OnDeleted(object sender, SenseNet.ContentRepository.Storage.Events.NodeEventArgs e)
        {
            if (HttpContext.Current != null)
                DeleteCacheFolder();
        }

        //public byte[] GetImageData(string sImagePath, string sResizedStorePath, string sOptions, out DateTime lastModDate, DateTime clientModDate)
        //public static byte[] GetImageData(Content content, out DateTime lastModDate, DateTime clientModDate)
        public virtual ImageObject GetImageData(Content content)
        {
            ImageObject result = new ImageObject();
            if (this.ImageType == "Binary")
            {
                var contentBinary = content.Fields["Binary"].GetData() as BinaryData;

                if (contentBinary == null)
                {
                    throw new Exception("Can not read Binary field from the given Content. Doesn't exists?");
                }
                using (Stream imageStream = contentBinary.GetStream())
                {
                    result.imageData = new byte[imageStream.Length];
                    imageStream.Read(result.imageData, 0, (int)imageStream.Length);
                }
                result.imageFileName = content.Name;
                result.imageId = content.Id;
            }
            else if (ImageType == "ImageData")
            {
                if (!String.IsNullOrEmpty(ImageFieldName))
                {
                    try
                    {
                        var contentImageFieldData = content.Fields[ImageFieldName].GetData() as SenseNet.ContentRepository.Fields.ImageField.ImageFieldData;

                        if (contentImageFieldData.ImgData.Size > 0)
                        {
                            using (Stream imageStream = contentImageFieldData.ImgData.GetStream())
                            {
                                result.imageData = new byte[imageStream.Length];
                                imageStream.Read(result.imageData, 0, (int)imageStream.Length);
                            }
                            result.imageFileName = new FileInfo(contentImageFieldData.ImgData.FileName.FullFileName).Name;
                            result.imageId = contentImageFieldData.ImgData.Id;
                        }
                        else
                        {
                            if (contentImageFieldData.ImgRef != null)
                            {
                                using (Stream imageStream = contentImageFieldData.ImgRef.Binary.GetStream())
                                {
                                    result.imageData = new byte[imageStream.Length];
                                    imageStream.Read(result.imageData, 0, (int)imageStream.Length);
                                }
                                result.imageFileName = new FileInfo(contentImageFieldData.ImgRef.Path).Name;
                                result.imageId = contentImageFieldData.ImgRef.Id;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        throw new Exception("Invalid Image Field Name was given in the application.");
                    }
                }
                else
                {
                    throw new Exception("There was no ImageFieldName specified when using ImageData as ImageType.");
                }
            }
            else if (ImageType == "Reference")
            {
                if (!String.IsNullOrEmpty(ImageFieldName))
                {
                    try
                    {
                        // ami még ide hiányzik:
                        // a GetData visszatérhet "null"-al, "Node"-ként illetve "List<Node>"-ként
                        // ebből már egy meg van valósítva, a többit kell még lekezelni
                        var referenceField = content.Fields[ImageFieldName].GetData() as List<Node>; //.GetType();// as ReferenceField;
                        var refContent = Content.Create(referenceField[0]);
                        var refContentBinary = refContent.Fields["Binary"].GetData() as BinaryData;

                        using (Stream imageStream = refContentBinary.GetStream())
                        {
                            result.imageData = new byte[imageStream.Length];
                            imageStream.Read(result.imageData, 0, (int)imageStream.Length);
                        }
                        result.imageFileName = refContent.Name;
                        result.imageId = refContent.Id;
                    }
                    catch (Exception)
                    {
                        //TODO: empty catch block
                    }
                }
                else
                {
                    throw new Exception("There was no ImageFieldName specified when using ImageData as ImageType.");
                }
            }
            else if (ImageType == "Attachment")
            {
                if (!String.IsNullOrEmpty(ImageFieldName))
                {
                    try
                    {
                        var binary = content.Fields[ImageFieldName].GetData() as BinaryData;
                        using (Stream imageStream = binary.GetStream())
                        {
                            result.imageData = new byte[imageStream.Length];
                            imageStream.Read(result.imageData, 0, (int)imageStream.Length);
                        }
                        result.imageFileName = new FileInfo(binary.FileName.FullFileName).Name;
                        result.imageId = binary.Id;
                    }
                    catch (Exception)
                    {
                        throw new Exception(String.Format("The given image field field '{0}' is not a valid binary field of an image.", result.imageFileName));
                    }
                }
            }
            else if (ImageType == "ShortText")
            {
                if (!String.IsNullOrEmpty(ImageFieldName))
                {
                    try
                    {
                        //Joe: azt a lehetoseget erdemes meg megvizsgalni, hogy tavoli (url) kepet, engedunk-e meretezni 
                        //ez optimalitastol kezdve, tarhelyen at sok kerdest vet fel: esetleg web.config beallitas, engedelyezett folderek, stb
                        string imgNodePath = content.ContentHandler.GetProperty<string>(ImageFieldName) ?? string.Empty;
                        Node imgNode = (Node.Exists(imgNodePath)) ? Node.LoadNode(imgNodePath) : null;
                        if (imgNode != null)
                        {
                            var refContent = Content.Create(imgNode);
                            var refContentBinary = refContent.Fields["Binary"].GetData() as BinaryData;
                            using (Stream imageStream = refContentBinary.GetStream())
                            {
                                result.imageData = new byte[imageStream.Length];
                                imageStream.Read(result.imageData, 0, (int)imageStream.Length);
                            }
                            result.imageFileName = refContent.Name;
                            result.imageId = refContent.Id;
                        }
                    }
                    catch (Exception)
                    {
                        //TODO
                    }
                }
                else
                {
                    throw new Exception("There was no ImageFieldName specified when using ShortText as ImageType.");
                }
            }
            return result;
        }

        public string GenerateFileName(ImageObject imageObj)
        {
            // generating contentPath
            int lastDotIndex = imageObj.imageFileName.LastIndexOf('.');
            string prefix = string.Empty;
            if (ResizeType == ResizeTypeList.Resize) { prefix = "R_"; }
            else if (ResizeType == ResizeTypeList.Crop) { prefix = "C_"; }
            else if (ResizeType == ResizeTypeList.ResizeCrop) { prefix = "RC_"; }

            string result = string.Empty;
            result = (lastDotIndex != -1)
                ? imageObj.imageFileName.Insert(lastDotIndex, String.Format("_{1}{0}", imageObj.imageId.ToString(), prefix))
                : String.Format("{0}_{2}{1}", imageObj.imageFileName, imageObj.imageId.ToString(), prefix);

            return result;
        }

        public static byte[] CreateResizedImageFile(byte[] originalImageData, ResolvedImageOptions imageOpts,
            ImageFormat outputFormat, SmoothingMode smoothingMode,
            InterpolationMode interpolationMode, PixelOffsetMode pixelOffsetMode)
        {
            byte[] resultData = originalImageData;

            if (originalImageData != null)
            {
                //Joe: a meretezett kep kepminosegenek es kepkodolasanak beallitasa, alapfunkcio
                EncoderParameters encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)imageOpts.Q);
                //IEnumerable<ImageCodecInfo> imageEncoders = ImageCodecInfo.GetImageEncoders().AsEnumerable();
                //ImageCodecInfo[] imageEncoders = ImageCodecInfo.GetImageEncoders();
                ImageCodecInfo imageEncoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(a => a.FormatID == outputFormat.Guid);

                using (Stream originalStream = new MemoryStream(originalImageData))
                using (Bitmap originalImage = new Bitmap(originalStream))
                {
                    ///az eredeti es modositott kep dimenzioinak lekerdezese
                    Dimension originalDimensions = new Dimension(originalImage.Width, originalImage.Height);

                    ///ha mindenaron az eredeti kepet akarjuk szethuzni, stretch = true
                    Dimension resizedDimensions = (imageOpts.Stretch)
                        ? new Dimension(imageOpts.X, imageOpts.Y)
                        : originalDimensions.GetRealSize(new Dimension(imageOpts.X, imageOpts.Y), imageOpts.Largest);

                    ///csak akkor meretezzen, ha legalabb az egyik dimenzio meg van adva
                    if (resizedDimensions.X != 0 || resizedDimensions.Y != 0)
                    {
                        using (
                            Bitmap modifiedImage = new Bitmap(originalImage, (int)resizedDimensions.X,
                                (int)resizedDimensions.Y))
                        {
                            using (Graphics gr = Graphics.FromImage(modifiedImage))
                            {
                                gr.SmoothingMode = smoothingMode;
                                gr.InterpolationMode = interpolationMode;
                                gr.PixelOffsetMode = pixelOffsetMode;
                                gr.DrawImage(originalImage,
                                    new Rectangle(0, 0, (int)resizedDimensions.X, (int)resizedDimensions.Y));

                                using (MemoryStream resultStream = new MemoryStream())
                                {
                                    if (imageEncoder != null)
                                        modifiedImage.Save(resultStream, imageEncoder, encoderParams);
                                    else
                                        modifiedImage.Save(resultStream, outputFormat);
                                    resultData = resultStream.ToArray();
                                }
                            }
                        }
                    }

                }
            }

            return resultData;
        }

        //        public static byte[] CreateCropedImageFile(byte[] originalImageData, double x, double y, double q, ImageFormat outputFormat, SmoothingMode smoothingMode, InterpolationMode interpolationMode, PixelOffsetMode pixelOffsetMode, double verticalDiff, double horizontalDiff)
        public static byte[] CreateCropedImageFile(byte[] originalImageData, ResolvedImageOptions options, ImageFormat outputFormat, SmoothingMode smoothingMode, InterpolationMode interpolationMode, PixelOffsetMode pixelOffsetMode)
        {
            byte[] resultData = originalImageData;

            //a vagott kep kepminosegenek es kepkodolasanak beallitasa, alapfunkcio
            EncoderParameters encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, options.Q);
            ImageCodecInfo[] imgCodeInfo = ImageCodecInfo.GetImageEncoders();

            //if (originalStream == null)
            //  return new MemoryStream();

            using (Stream originalStream = new MemoryStream(originalImageData))
            using (var originalImage = System.Drawing.Image.FromStream(originalStream))
            {
                //ha top right bottom left alapjan akarunk cropx cropy-t szamolni
                //akkor itt kell, majd ugras a kepgeneralashoz (pl stretch:true parameterre erdemes tenni)

                if (options.Dimensions)
                {
                    options.CropX = originalImage.Width - options.Left - options.Right;
                    options.CropY = originalImage.Height - options.Top - options.Bottom;
                }
                else
                {//miert nincs olyan opcio ami a beallitott cropxy alapjan vag?
                    if (options.Crop)//ennek inkabb "kiegeszito vagas" booleannak kene lenni
                    {
                        ///ha meretezett kepet akarjuk a keretre szabni, ennek csak largest esetben van ertelme
                        if (options.CropX != 0 && options.CropY != 0)
                        {
                            // crop dimensions already set from settings
                        }
                        else if (options.X != 0 && options.Y != 0 && options.Largest)
                        {
                            options.CropX = options.X;
                            options.CropY = options.Y;
                        }
                        else if (options.CropX != 0 && options.CropY == 0) { options.CropY = originalImage.Height; }
                        else if (options.CropY != 0 && options.CropX == 0) { options.CropX = originalImage.Width; }
                        else
                        {
                            //ez az original kepmeret, talan erdemes lenne jelolni, hogy volt-e munka a keppel
                            //mert ha nem, akkor felesleges atkodolni
                            options.CropX = originalImage.Width;
                            options.CropY = originalImage.Height;
                        };
                    }//ide jelenlegi kialakitassal nem jut, de ez a szimetrikus vagas lenne
                    else if (options.CropX != 0 && options.CropY == 0) { options.CropY = options.CropX; }
                    else if (options.CropX == 0 && options.CropY != 0) { options.CropX = options.CropY; }
                    //else //ide kellene jonnie a valodi cropxy vagasnak
                    //{
                    //    return sFileName;
                    //}

                    switch (options.HorizontalAlignment)
                    {
                        case "center":
                            options.Left = options.Left + (long)Math.Floor(((double)originalImage.Width - (double)options.CropX) / 2) - options.Right;
                            break;
                        case "right":
                            options.Left = options.Left + originalImage.Width - options.CropX - options.Right;
                            break;
                        default:
                            options.Left = (options.Right != 0) ? options.Left + originalImage.Width - options.Right - options.CropX : options.Left;
                            break;
                    }

                    switch (options.VerticalAlignment)
                    {
                        case "center":
                            options.Top = options.Top + (long)Math.Floor(((double)originalImage.Height - (double)options.CropY) / 2) - options.Bottom;
                            break;
                        case "middle":
                            options.Top = options.Top + (long)Math.Floor(((double)originalImage.Height - (double)options.CropY) / 2) - options.Bottom;
                            break;
                        case "bottom":
                            options.Top = options.Top + originalImage.Height - options.CropY - options.Bottom;
                            break;
                        default:
                            options.Top = (options.Bottom != 0) ? options.Top + originalImage.Height - options.Bottom - options.CropY : options.Top;
                            break;
                    }

                }

                ///ha kilogna a vagott kep valamely iranyba, akkor lenyessuk,
                ///kiveve ha kulon ez a szandekunk (OverCrop = true)
                if (options.OverCrop == false)
                {
                    options.CropX = (Math.Abs(options.Left) + options.CropX > originalImage.Width) ? originalImage.Width - Math.Abs(options.Left) : options.CropX;
                    options.CropY = (Math.Abs(options.Top) + options.CropY > originalImage.Height) ? originalImage.Height - Math.Abs(options.Top) : options.CropY;
                    options.Top = (options.Top < 0) ? 0 : options.Top;
                    options.Left = (options.Left < 0) ? 0 : options.Left;
                }

                /////
                using (var bmp = new Bitmap((int)options.CropX, (int)options.CropY))
                {
                    bmp.SetResolution(originalImage.HorizontalResolution, originalImage.VerticalResolution);
                    using (var graphic = Graphics.FromImage(bmp))
                    {
                        graphic.SmoothingMode = smoothingMode;
                        graphic.InterpolationMode = interpolationMode;
                        graphic.PixelOffsetMode = pixelOffsetMode;
                        graphic.DrawImage(originalImage, new Rectangle(0, 0, (int)options.CropX, (int)options.CropY), options.Left, options.Top, (int)options.CropX, (int)options.CropY, GraphicsUnit.Pixel);
                        using (MemoryStream resultStream = new MemoryStream())
                        {
                            bmp.Save(resultStream, originalImage.RawFormat);
                            //bmp.Save(newMemoryStream, imgCodeInfo[ImageTypes[Path.GetExtension(sFileName).ToLower()]], encParams);
                            resultData = resultStream.ToArray();
                        }
                    }
                }
            }
            return resultData;
        }

        public void ResizeImage(HttpContext context)
        {

            //Stream imageStream = null;
            var imageNodePath = HttpContext.Current.Request.FilePath;

            var imgContent = Content.LoadByIdOrPath(PortalContext.Current.ContextNodePath);
            var contentPath = "";

            //var contentFileName = "";
            //var contentId = -1;

            CheckCacheFolder();

            if (!string.IsNullOrEmpty(imageNodePath))
            {
                ImageObject imageObj = new ImageObject();
                imageObj = GetImageData(imgContent);

                if (imageObj.imageData == null)
                {
                    context.Response.StatusCode = 404;
                    context.Response.Flush();
                    context.Response.Close();
                    return;
                }

                ResolvedImageOptions imageOpts = new ResolvedImageOptions(this);
                contentPath = GenerateFileName(imageObj);

                //a nevkonvencio szar, igy hibasan cache-el
                //if (IsCached(contentPath))
                //{
                //    FlushCachedFile(contentPath, context);
                //    return;
                //}

                if (imageOpts.Resize)
                {
                    //Resize image //at kell irni optionsosre
                    imageObj.imageData = CreateResizedImageFile(imageObj.imageData, imageOpts,
                                                                      IsAutoOutputFormat ? GetImageFormat(GetMimeType(contentPath)) : this.ResizeOutputFormat,
                                                                      this.ResizeSmoothingMode,
                                                                      this.ResizeInterpolationMode,
                                                                      this.ResizePixelOffsetMode);
                }

                if (imageOpts.Crop)
                {
                    //Crop image //ki kell egesziteni az optionst
                    imageObj.imageData = CreateCropedImageFile(imageObj.imageData, imageOpts,
                                                                      IsAutoOutputFormat ? GetImageFormat(GetMimeType(contentPath)) : this.ResizeOutputFormat,
                                                                      this.ResizeSmoothingMode,
                                                                      this.ResizeInterpolationMode,
                                                                      this.ResizePixelOffsetMode);
                }

                using (Stream imageStream = new MemoryStream(imageObj.imageData))
                {
                    Cache(imageStream, GetImageCachePath(contentPath));
                    FlushStream(imageStream, context, GetMimeType(contentPath));
                }
                return;
            }
            else
            {
                throw new Exception("There was no image in the requested file path.");
            }
        }

        /// <summary>
        /// Flushes (writes) the image to the output from cache.
        /// </summary>
        /// <param name="imageNodePath">Path of image in the Repository.</param>
        /// <param name="context">Output context where the image should be flushed to.</param>
        private void FlushCachedFile(string imageNodePath, HttpContext context)
        {
            var fileName = GetImageCachePath(imageNodePath);

            //Open the file in ReadOnly mode
            using (var imageStream = System.IO.File.OpenRead(fileName))
            {
                FlushStream(imageStream, context, GetMimeType(fileName));
            }
        }

        /// <summary>
        /// Flushes (writes) the image to the output from the given stream.
        /// </summary>
        /// <param name="imageStream">Stream of image to flush.</param>
        /// <param name="context">Output context where the image should be flushed to.</param>
        /// <param name="mimeType">Mime type of the given image.s</param>
        private void FlushStream(Stream imageStream, HttpContext context, string mimeType)
        {
            context.Response.Clear();
            context.Response.ClearHeaders();
            context.Response.ContentType = mimeType;

            //************* START OF PROXY CACHE CONTROL
            context.Response.Cache.SetCacheability(this.GetCacheControlEnumValue() ?? HttpCacheability.Public);
            context.Response.Cache.SetMaxAge(new TimeSpan(0, 0, this.NumericMaxAge ?? 0));
            context.Response.Cache.SetSlidingExpiration(true);  // max-age does not appear in response header without this...
            //************* END OF PROXY CACHE CONTROL


            const int bufferSize = 256;
            int bytesRead;
            var buffer = new byte[bufferSize];

            context.Response.BufferOutput = true;

            imageStream.Position = 0;
            while ((bytesRead = imageStream.Read(buffer, 0, bufferSize)) > 0)
            {
                context.Response.OutputStream.Write(buffer, 0, bytesRead);
            }

            context.Response.Flush();
            context.Response.End();
        }

        /// <summary>
        /// Caches (creates) the image into the Application's Cache Folder.
        /// </summary>
        /// <param name="imageStream">Stream of the image.</param>
        /// <param name="imgCachePath">Where the images should be created.</param>
        private void Cache(Stream imageStream, string imgCachePath)
        {
            //var imgCachePath = GetImageCachePath(imageNodePath);

            // Create the directory for the image
            if (!Directory.Exists(new FileInfo(imgCachePath).DirectoryName))
            {
                try
                {
                    Directory.CreateDirectory(new FileInfo(imgCachePath).DirectoryName);
                }
                catch (Exception)
                {
                    throw new Exception("Cannot create cache folder for image.");
                }
            }

            // Create the image
            using (var fs = System.IO.File.Create(imgCachePath))
            {
                const int bufferSize = 256;
                var buffer = new byte[bufferSize];

                imageStream.Position = 0;
                int bytesRead;
                while ((bytesRead = imageStream.Read(buffer, 0, bufferSize)) > 0)
                {
                    fs.Write(buffer, 0, bytesRead);
                }
            }
        }

        private ImageFormat GetImageFormat(string imageType)
        {
            ImageFormat imf = null;
            switch (imageType.ToLower())
            {

                case "image/jpeg":
                case "jpeg": imf = ImageFormat.Jpeg; break;

                case "image/gif":
                case "gif": imf = ImageFormat.Gif; break;

                case "image/png":
                case "png": imf = ImageFormat.Png; break;

                case "image/ico":
                case "icon": imf = ImageFormat.Icon; break;

                case "image/svg+xml": imf = ImageFormat.Png; break;

                case "image/tiff":
                case "tiff": imf = ImageFormat.Tiff; break;

                case "auto": imf = null; break;

                default: imf = ImageFormat.Png; break;
            }
            return imf;
        }

        // =================== IHttpHandler members ===================
        #region IHttpHandler functions

        bool IHttpHandler.IsReusable
        {
            get { return false; }
        }

        public void ProcessRequest(HttpContext context)
        {
            /*
            if (Width == 0)
                throw new Exception("The image's width is not set in the applicaton.");
            if (Height == 0)
                throw new Exception("The image's height is not set in the applicaton.");
            */
            ResizeImage(context);
        }

        #endregion

        public static bool IsChecked(Content content, string fieldName, string item = "")
        {
            //return content.ContentHandler.IsChecked(fieldName, item);
            bool result = false;
            if (content.ContentHandler.HasProperty(fieldName))
            {
                switch (content.Fields[fieldName].GetType().Name)
                {
                    case "ChoiceField":
                        Field field = content.Fields[fieldName];
                        List<string> checkedItems = new List<string>();
                        if (field != null)
                            checkedItems = field.GetData() as List<string>;
                        if (checkedItems != null && !string.IsNullOrWhiteSpace(item))
                            result = checkedItems.Contains(item);
                        break;
                    case "BooleanField":
                        result = (bool)content.Fields[fieldName].GetData();
                        break;
                    default:
                        break;
                }
            }
            return result;
        }

        public static string GetChoiceOptionValue(SenseNet.ContentRepository.Content content, string fieldName)
        {
            string result = string.Empty;
            if (content.ContentHandler.HasProperty(fieldName)
                && content.Fields[fieldName].GetType().Name == "ChoiceField")
            {
                Field field = content.Fields[fieldName];
                List<string> checkedItems = new List<string>();
                if (field != null)
                    checkedItems = field.GetData() as List<string>;

                result = string.Join(";", checkedItems);
            }
            return result;
        }
    }
}