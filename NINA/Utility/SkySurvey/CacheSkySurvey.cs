﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NINA.Utility.Astrometry;

namespace NINA.Utility.SkySurvey {

    internal class CacheSkySurvey : ISkySurvey {
        private static string FRAMINGASSISTANTCACHEPATH = Path.Combine(Utility.APPLICATIONTEMPPATH, "FramingAssistantCache");
        private static string FRAMINGASSISTANTCACHEINFOPATH = Path.Combine(FRAMINGASSISTANTCACHEPATH, "CacheInfo.xml");

        static CacheSkySurvey() {
            Initialize();
        }

        private static void Initialize() {
            if (!Directory.Exists(FRAMINGASSISTANTCACHEPATH)) {
                Directory.CreateDirectory(FRAMINGASSISTANTCACHEPATH);
            }

            if (!File.Exists(FRAMINGASSISTANTCACHEINFOPATH)) {
                XElement info = new XElement("ImageCacheInfo");
                info.Save(FRAMINGASSISTANTCACHEINFOPATH);
                Cache = info;
                return;
            } else {
                Cache = XElement.Load(FRAMINGASSISTANTCACHEINFOPATH);
            }
        }

        public static void Clear() {
            System.IO.DirectoryInfo di = new DirectoryInfo(FRAMINGASSISTANTCACHEPATH);

            foreach (FileInfo file in di.GetFiles()) {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories()) {
                dir.Delete(true);
            }
            Initialize();
        }

        public static void SaveImageToCache(SkySurveyImage skySurveyImage) {
            try {
                var element =
                    Cache
                    .Elements("Image")
                    .Where(
                        x => x.Attribute("Id").Value == skySurveyImage.Id.ToString()
                    ).FirstOrDefault();
                if (element == null) {
                    if (!Directory.Exists(FRAMINGASSISTANTCACHEPATH)) {
                        Directory.CreateDirectory(FRAMINGASSISTANTCACHEPATH);
                    }

                    var imgFilePath = Path.Combine(FRAMINGASSISTANTCACHEPATH, skySurveyImage.Name + ".png");

                    imgFilePath = Utility.GetUniqueFilePath(imgFilePath);
                    var name = Path.GetFileNameWithoutExtension(imgFilePath);

                    using (var fileStream = new FileStream(imgFilePath, FileMode.Create)) {
                        var encoder = new PngBitmapEncoder();
                        //encoder.QualityLevel = 80;
                        encoder.Frames.Add(BitmapFrame.Create(skySurveyImage.Image));
                        encoder.Save(fileStream);
                    }

                    XElement xml = new XElement("Image",
                        new XAttribute("Id", skySurveyImage.Id),
                        new XAttribute("RA", skySurveyImage.Coordinates.RA),
                        new XAttribute("Dec", skySurveyImage.Coordinates.Dec),
                        new XAttribute("Rotation", skySurveyImage.Rotation),
                        new XAttribute("FoVW", skySurveyImage.FoVWidth),
                        new XAttribute("FoVH", skySurveyImage.FoVHeight),
                        new XAttribute("FileName", imgFilePath),
                        new XAttribute("Name", name)
                    );

                    Cache.Add(xml);
                    Cache.Save(FRAMINGASSISTANTCACHEINFOPATH);
                }
            } catch (Exception ex) {
                Logger.Error(ex);
                throw ex;
            }
        }

        public static XElement Cache { get; private set; }

        public object CulutureInfo { get; private set; }

        public Task<SkySurveyImage> GetImage(Coordinates coordinates, double fieldOfView, CancellationToken ct, IProgress<int> progress) {
            return Task.Run(() => {
                var element =
                    Cache
                    .Elements("Image")
                    .Where(
                        x =>
                            x.Attribute("RA").Value == coordinates.RA.ToString(CultureInfo.InvariantCulture)
                            && x.Attribute("Dec").Value == coordinates.Dec.ToString(CultureInfo.InvariantCulture)
                            && x.Attribute("FoVW").Value == fieldOfView.ToString(CultureInfo.InvariantCulture)
                            && x.Attribute("FoVH").Value == fieldOfView.ToString(CultureInfo.InvariantCulture)
                    ).FirstOrDefault();

                if (element != null) {
                    var img = LoadPng(element.Attribute("FileName").Value);
                    Guid id = Guid.Parse(element.Attribute("Id").Value);
                    var fovW = double.Parse(element.Attribute("FoVW").Value, CultureInfo.InvariantCulture);
                    var fovH = double.Parse(element.Attribute("FoVH").Value, CultureInfo.InvariantCulture);
                    var rotation = double.Parse(element.Attribute("Rotation").Value, CultureInfo.InvariantCulture);
                    var name = element.Attribute("Name").Value;
                    var ra = double.Parse(element.Attribute("RA").Value, CultureInfo.InvariantCulture);
                    var dec = double.Parse(element.Attribute("Dec").Value, CultureInfo.InvariantCulture);

                    img.Freeze();
                    return new SkySurveyImage() {
                        Id = id,
                        Image = img,
                        FoVHeight = fovH,
                        FoVWidth = fovW,
                        Coordinates = new Coordinates(ra, dec, Epoch.J2000, Coordinates.RAType.Hours),
                        Name = name,
                        Rotation = rotation
                    };
                } else {
                    return null;
                }
            });
        }

        private BitmapSource LoadPng(string filename) {
            PngBitmapDecoder PngDec = new PngBitmapDecoder(new Uri(filename), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return PngDec.Frames[0];
        }
    }
}