﻿using nom.tam.fits;
using nom.tam.util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AstrophotographyBuddy.Utility {
    static class Utility {

        public class ImageArray {
            public Array SourceArray;
            public Int16[] FlatArray;
            public int X;
            public int Y;
        }

        private static PHD2Client _pHDClient;
        public static PHD2Client PHDClient {
            get {
                if (_pHDClient == null) {
                    _pHDClient = new PHD2Client();
                }
                return _pHDClient;
            }
            set {
                _pHDClient = value;
            }
        }

        public static async Task<ImageArray> convert2DArray(Int32[,] arr) {
            return await Task<ImageArray>.Run(() => { 
                ImageArray iarr = new ImageArray();
                iarr.SourceArray = arr;
                int width = arr.GetLength(0);
                int height = arr.GetLength(1);
                iarr.Y = width;
                iarr.X = height;
                Int16[] flatArray = new Int16[arr.Length];
                unsafe
                {
                    fixed (Int32* ptr = arr)
                    {
                        for (int i = 0; i < arr.Length; i++) {
                            Int16 b = (Int16)ptr[i];
                            flatArray[i] = b;                            
                        }
                    }
                }
                iarr.FlatArray = flatArray;
                return iarr;
            });           
        }

        public static Int16[] flatten2DArray(Array arr) {
            int width = arr.GetLength(0);
            int height = arr.GetLength(1);
            Int16[] flatArray = new Int16[width * height];
            Int16 val;
            int idx = 0;
            for (int i = 0; i < height; i++) {
                for (int j = 0; j < width; j++) {
                    val = (Int16)(Int16.MinValue + Convert.ToInt16(arr.GetValue(j, i)));

                    flatArray[idx] = val;
                    idx++;
                }
            }
            return flatArray;
        }


        public static T[] flatten2DArray<T>(Array arr) {
            int width = arr.GetLength(0);
            int height = arr.GetLength(1);
            T[] flatArray = new T[width * height];
            T val;
            int idx = 0;
            for (int i = 0; i < height; i++) {
                for (int j = 0; j < width; j++) {
                    val = (T)Convert.ChangeType(arr.GetValue(j, i), typeof(T));

                    flatArray[idx] = val;
                    idx++;
                }
            }
            return flatArray;
        }

        public static T[] flatten3DArray<T>(Array arr) {
            int width = arr.GetLength(0);
            int height = arr.GetLength(1);
            int depth = arr.GetLength(2);
            T[] flatArray = new T[width * height * depth];
            T val;
            int idx = 0;
            for (int i = 0; i < height; i++) {
                for (int j = 0; j < width; j++) {
                    for (int k = 0; k < depth; k++) {

                        val = (T)Convert.ChangeType(arr.GetValue(j, i, k), typeof(T));

                        flatArray[idx] = val;
                        idx++;
                    }
                }
            }
            return flatArray;
        }

        /*public static void saveFits(ImageArray iarr) {
            Stopwatch sw = Stopwatch.StartNew();

            Header h = new Header();
            h.AddValue("SIMPLE", "T", "C# FITS");
            h.AddValue("BITPIX", 16, "");
            h.AddValue("NAXIS", 2, "Dimensionality");
            h.AddValue("NAXIS1", iarr.SourceArray.GetUpperBound(0)+1, "" );
            h.AddValue("NAXIS2", iarr.SourceArray.GetUpperBound(1) + 1, "");
            h.AddValue("BZERO", 32768, "");
            h.AddValue("EXTEND", "T", "Extensions are permitted");

            ImageData d = new ImageData(iarr.CurledArray);
            d = new ImageData();            
            sw.Stop();
            Console.WriteLine("CreateDataForHDU: " + sw.Elapsed);
            
            sw = Stopwatch.StartNew();
            ImageHDU imageHdu = new ImageHDU(h,d);

            //BasicHDU imageHdu = FitsFactory.HDUFactory(iarr.CurledArray);
            sw.Stop();
            Console.WriteLine("CreateHDU: " + sw.Elapsed);


            //imageHdu.AddValue("BZERO", 32768, "");

            sw = Stopwatch.StartNew();
            Fits f = new Fits();
            f.AddHDU(imageHdu);
            sw.Stop();
            Console.WriteLine("Create FITS: " + sw.Elapsed);

            sw = Stopwatch.StartNew();
            try {
                FileStream fs = File.Create("test.fit");
                f.Write(fs);
                fs.Close();

            } catch(Exception ex) {

            }
            sw.Stop();
            Console.WriteLine("Save FITS: " + sw.Elapsed);
        }*/

        public static void saveTiff(ImageArray iarr, String path) {         
            
            try {
                BitmapSource bmpSource = createSourceFromArray(iarr.FlatArray, iarr.X, iarr.Y, System.Windows.Media.PixelFormats.Gray16);

                Directory.CreateDirectory(Path.GetDirectoryName(path));

                using (FileStream fs = new FileStream(path + ".tif", FileMode.Create)) {
                    TiffBitmapEncoder encoder = new TiffBitmapEncoder();
                    encoder.Compression = TiffCompressOption.None;
                    encoder.Frames.Add(BitmapFrame.Create(bmpSource));
                    encoder.Save(fs);
                }
            } catch(Exception ex) {
                Logger.error(ex.Message);

            }
        }


        public static BitmapSource NormalizeTiffTo8BitImage(BitmapSource source) {
            // allocate buffer & copy image bytes.
            var rawStride = source.PixelWidth * source.Format.BitsPerPixel / 8;
            var rawImage = new byte[rawStride * source.PixelHeight];
            source.CopyPixels(rawImage, rawStride, 0);

            // get both max values of first & second byte of pixel as scaling bounds.
            var max1 = 0;
            int max2 = 1;
            for (int i = 0; i < rawImage.Length; i++) {
                if ((i & 1) == 0) {
                    if (rawImage[i] > max1)
                        max1 = rawImage[i];
                }
                else if (rawImage[i] > max2)
                    max2 = rawImage[i];
            }

            // determine normalization factors.
            var normFactor = max2 == 0 ? 0.0d : 128.0d / max2;
            var factor = max1 > 0 ? 255.0d / max1 : 0.0d;
            max2 = Math.Max(max2, 1);

            // normalize each pixel to output buffer.
            var buffer8Bit = new byte[rawImage.Length / 2];
            for (int src = 0, dst = 0; src < rawImage.Length; dst++) {
                int value16 = rawImage[src++];
                double value8 = ((value16 * factor) / max2) - normFactor;

                if (rawImage[src] > 0) {
                    int b = rawImage[src] << 8;
                    value8 = ((value16 + b) / max2) - normFactor;
                }
                buffer8Bit[dst] = (byte)Math.Min(255, Math.Max(value8, 0));
                src++;
            }

            // return new bitmap source.
            return BitmapSource.Create(
                source.PixelWidth, source.PixelHeight,
                source.DpiX, source.DpiY,
                PixelFormats.Gray8, BitmapPalettes.Gray256,
                buffer8Bit, rawStride / 2);
        }

        public static BitmapSource createSourceFromArray(Array flatArray, int x, int y, System.Windows.Media.PixelFormat pf) {
            
            //int stride = C.CameraYSize * ((Convert.ToString(C.MaxADU, 2)).Length + 7) / 8;
            int stride = (x * pf.BitsPerPixel + 7) / 8;
            double dpi = 96;

            BitmapSource source = BitmapSource.Create(x, y, dpi, dpi, pf, null, flatArray, stride);
            return source;
        }

        public static int[] getDim(Array arr) {
            int[] dim = new int[2];
            dim[0] = arr.GetUpperBound(1) + 1;
            dim[1] = arr.GetUpperBound(0) + 1;
            return dim;
        }

        public static string getImageFileString(ICollection<ViewModel.OptionsVM.ImagePattern> patterns) {
            string s = Settings.ImageFilePattern;
            foreach(ViewModel.OptionsVM.ImagePattern p in patterns) {
                s = s.Replace(p.Key, p.Value);
            }
            return s;
        }
        
    }


}