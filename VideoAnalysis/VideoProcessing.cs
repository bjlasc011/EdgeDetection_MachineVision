using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using VideoAnalysis;

namespace VideoProcessing
{
    public partial class VideoProcessing : Form
    {
        private int gauss = 7;
        private int thresh1 = 20;
        private int thresh2 = 25;
        private int binaryMin = 180;
        private int binaryMax = 255;
        private const float hueByteRatio = 256f / 360f;
        private VideoCapture capture;

        private Dictionary<string, Bgr> pallette = new Dictionary<string, Bgr>() { };
        private Gray black = new Gray(0);
        private MCvScalar redScalar = new MCvScalar(0, 0, 255);
        private VideoMode videoMode = VideoMode.Canny;
        private Image<Bgr, byte> imgSrc;
        private Image<Gray, byte> imgGray;
        private Mat model;
        private int imgWidth;
        private int imgHeight;

        private NodeList nodeList = new NodeList();
        private int accumLimit = 4;
        private bool isAccumulating = false;
        private Gray BinMax { get; set; }
        private Gray BinMin { get; set; }

        public VideoProcessing()
        {
            InitializeComponent();
        }

        public Mat DrawFeatures(Mat frame)
        {
            return FeatureDetect.Draw(model, frame, out _);
        }

        private void StartVideoCapture()
        {
            if (capture == null)
            {
                capture = new VideoCapture(0);
            }
            imageBox1.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.Minimum;
            capture.ImageGrabbed += ImageGrabbed;
            capture.Start();
            fpsLabel.Text = capture.GetCaptureProperty(Emgu.CV.CvEnum.CapProp.Fps).ToString() + " fps";
        }

        private void startToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StartVideoCapture();
        }

        private void ImageGrabbed(object sender, EventArgs e)
        {
            Mat mat = new Mat();
            capture.Retrieve(mat);
            imgHeight = mat.Height;
            imgWidth = mat.Width;
            using (Image<Bgr, byte> frame = new Image<Bgr, byte>(mat.Bitmap))
            using (Image<Gray, byte> graySmooth = frame.Convert<Gray, byte>().SmoothGaussian(gauss))
            {
                if (videoMode == VideoMode.Canny)
                {
                    Image<Gray, byte>[] cannies = new Image<Gray, byte>[]
                    {
                            new Image<Gray, byte>(imgWidth, imgHeight),
                            new Image<Gray, byte>(imgWidth, imgHeight),
                            new Image<Gray, byte>(imgWidth, imgHeight),
                            new Image<Gray, byte>(imgWidth, imgHeight),
                            new Image<Gray, byte>(imgWidth, imgHeight)
                    };
                    using (Image<Gray, byte> gray = frame.Convert<Gray, byte>())
                    using (Image<Bgr, byte> temp = new Image<Bgr, byte>(imgWidth, imgHeight))
                    {
                        cannies[0] = gray.SmoothGaussian(gauss).Canny(thresh1, thresh2);
                        cannies[1] = gray.SmoothGaussian(7).Canny(20, 15);
                        cannies[2] = gray.SmoothGaussian(11).Canny(15, 30);
                        cannies[3] = gray.SmoothGaussian(15).Canny(40, 60);
                        if (isAccumulating)
                        {
                            AccumulateFrames(cannies[1], out Image<Gray, byte> accumFrames);
                            cannies[4] = accumFrames.SmoothGaussian(1);
                            temp.SetValue(pallette["green3"], cannies[4]);
                        }
                        else
                        {
                            temp.SetValue(pallette["green3"], cannies[1]);
                        }
                        temp.SetValue(pallette["green2"], cannies[0]);
                        temp.SetValue(pallette["green1"], cannies[2]);
                        temp.SetValue(pallette["green0"], cannies[3]);
                        imageBox1.Image = temp.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                        return;
                    }
                }
                Image<Gray, byte> canny = new Image<Gray, byte>(imgWidth, imgHeight);
                if (videoMode != VideoMode.Hue_Gray)
                {
                    canny = graySmooth.Canny(thresh1, thresh2);
                }
                switch(videoMode)
                {
                    case (VideoMode.Gray):
                        graySmooth.SetValue(black, canny);
                        imageBox1.Image = graySmooth.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                        canny.Dispose();
                        break;
                    case (VideoMode.Color):
                        using (Image<Bgr, byte> frameOverlay = frame.Clone())
                        {
                            frameOverlay.SetValue(pallette[Color.LawnGreen.Name], canny);
                            imageBox1.Image = frameOverlay.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                            frameOverlay.Dispose();
                            canny.Dispose();
                        }
                        break;
                    case (VideoMode.Contour):
                        Image<Bgr, byte> contourImg = new Image<Bgr, byte>(imgWidth, imgHeight);
                        GetContours(graySmooth, out contourImg);
                        imageBox1.Image = contourImg.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                        contourImg.Dispose();
                        break;
                    case (VideoMode.Hue_Gray):
                        Image<Gray, byte> hueImgGray = new Image<Gray, byte>(imgWidth, imgHeight);
                        using (Image<Bgr, byte> grayImg = new Image<Bgr, byte>(mat.Bitmap))
                        {
                            GetHueGray(grayImg, out hueImgGray);
                            imageBox1.Image = hueImgGray.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                        }
                        break;
                    case (VideoMode.Laplacian):
                        using (Image<Gray, byte> gray = frame.Convert<Gray, byte>())
                        using (Image<Gray, float> laplacianImg = gray.Laplace(5))
                        {
                            imageBox1.Image = laplacianImg.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                        }
                        break;
                    case (VideoMode.Sobel):
                        using (Image<Gray, byte> gray = frame.Convert<Gray, byte>())
                        using (Image<Gray, float> sobelImg = gray.Sobel(1, 1, 5))
                        {
                            imageBox1.Image = sobelImg.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                        }
                        break;
                    case (VideoMode.Binary):
                        using (Image<Gray, byte> gray = frame.Convert<Gray, byte>())
                        using (Image<Gray, byte> imgBinary = new Image<Gray, byte>(imgWidth, imgHeight))
                        {
                            CvInvoke.Threshold(gray, imgBinary, binaryMin, binaryMax, Emgu.CV.CvEnum.ThresholdType.Binary);
                            imageBox1.Image = imgBinary.Flip(Emgu.CV.CvEnum.FlipType.Horizontal);
                        }
                        break;
                    case (VideoMode.FeaturesTracking):
                        imageBox1.Image = DrawFeatures(mat);
                        break;
                }
            }
        }

        private void AccumulateFrames(Image<Gray, byte> grayFrame, out Image<Gray, byte> outImg)
        {
            outImg = new Image<Gray, byte>(imgWidth, imgHeight);
            nodeList.PushFront(grayFrame);
            if (nodeList.count == accumLimit)
            {
                nodeList.Pop();
                Node current = nodeList.firstNode;
                while (current.next != null)
                {
                    outImg += current.data;
                    current = current.next;
                }
                outImg += current.data;
            }
            else
            {
                Node current = nodeList.firstNode;
                while (current.next != null)
                {
                    outImg += current.data;
                    current = current.next;
                }
                outImg += current.data;
            }
        }

        private void GetContours(Image<Gray, byte> grayImg, out Image<Bgr, byte> outImg)
        {

            VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint();
            Image<Bgr, byte> temp = new Image<Bgr, byte>(grayImg.Width, grayImg.Height);
            Image<Gray, byte> binaryGrayImg = grayImg.ThresholdBinary(new Gray(binaryMin), new Gray(binaryMax));
            Mat hierarchy = new Mat();
            CvInvoke.FindContours(
                binaryGrayImg,
                contours,
                hierarchy,
                Emgu.CV.CvEnum.RetrType.List,
                Emgu.CV.CvEnum.ChainApproxMethod.ChainApproxSimple
            );
            CvInvoke.DrawContours(temp, contours, -1, redScalar);
            outImg = temp;
        }

        private void GetHueGray(Image<Bgr, byte> img, out Image<Gray, byte> hueGrayImg)
        {
            hueGrayImg = img.Convert<Gray, byte>();
            Image<Bgr, byte> temp = new Image<Bgr, byte>(img.Bitmap);
            for (int y = 0; y < img.Height; ++y)
            {
                for (int x = 0; x < img.Width; ++x)
                {
                    float hue = GetHue(img, x, y);
                    float ratioHue = (float)Math.Floor(hue * hueByteRatio);
                    hueGrayImg.Data[y, x, 0] = (byte)ratioHue;
                }
            }
        }

        private float GetHue(Image<Bgr, byte> img, int x, int y)
        {
            Color clr = Color.FromArgb(img.Data[y, x, 2], img.Data[y, x, 1], img.Data[y, x, 0]);
            return clr.GetHue();
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (capture != null)
                capture.Stop();
        }

        private void analyzeBttn_Click(object sender, EventArgs e)
        {
            TrySetCannyInputs();
        }

        private void pauseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (capture != null)
                capture.Pause();
        }

        public bool TrySetCannyInputs() {
            if (
                int.TryParse(gaussTxt.Text, out gauss) &&
                int.TryParse(thresh1Txt.Text, out thresh1) &&
                int.TryParse(thresh2Txt.Text, out thresh2) &&
                int.TryParse(binaryMinTxt.Text, out binaryMin) &&
                int.TryParse(binaryMaxTxt.Text, out binaryMax)
            )
            {
                BinMin = new Gray(binaryMin); 
                BinMax = new Gray(binaryMax);
                return true;
            }
            return false;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            StartVideoCapture();
            binaryMaxTxt.Text = binaryMax.ToString();
            binaryMinTxt.Text = binaryMin.ToString();
            gaussTxt.Text = gauss.ToString();
            thresh1Txt.Text = thresh1.ToString();
            thresh2Txt.Text = thresh2.ToString();
            InitColorPalette();
        }

        private void InitColorPalette()
        {
            KnownColor[] values = (KnownColor[])Enum.GetValues(typeof(KnownColor));
            foreach (KnownColor kc in values)
            {
                Color clr = Color.FromKnownColor(kc);
                pallette.Add(clr.Name, new Bgr(clr.B, clr.G, clr.R));
            }
            pallette.Add("green0", new Bgr(80, 255, 80));
            pallette.Add("green1", new Bgr(45, 150, 45));
            pallette.Add("green2", new Bgr(10, 80, 10));
            pallette.Add("green3", new Bgr(5, 50, 5));
        }

        private void VideoInputRadioBttn_CheckChanged(object sender, EventArgs e)
        {
            RadioButton chk = sender as RadioButton;
            if (chk.Checked)
            {
                switch (chk.Name) {
                    case "colorRadio":
                        videoMode = VideoMode.Color;
                        break;
                    case "grayRadio":
                        videoMode = VideoMode.Gray;
                        break;
                    case "contourRadio":
                        videoMode = VideoMode.Contour;
                        break;
                    case "cannyHierarchyRadio":
                        videoMode = VideoMode.Canny;
                        break;
                    case "hueGrayRadio":
                        videoMode = VideoMode.Hue_Gray;
                        break;
                    case "laplacianRadio":
                        videoMode = VideoMode.Laplacian;
                        break;
                    case "sobelRadio":
                        videoMode = VideoMode.Sobel;
                        break;
                    case "binaryRadio":
                        videoMode = VideoMode.Binary;
                        break;
                    case "featuresRadio":
                        videoMode = VideoMode.FeaturesTracking;
                        if (model == null)
                        {
                            MessageBox.Show("Cannot track image until model is set. Please set Model first.");
                        }
                        break;
                }
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            isAccumulating = checkBox1.Checked;
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            imageBox1.FunctionalMode = Emgu.CV.UI.ImageBox.FunctionalModeOption.PanAndZoom;
            OpenFileDialog open = new OpenFileDialog();

            if (open.ShowDialog() == DialogResult.OK)
            {
                this.imgSrc = new Image<Bgr, byte>(open.FileName);
                imageBox1.Image = imgSrc;
            }
        }

        private void hueGrayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            imgGray = new Image<Gray, byte>(imgSrc.Width, imgSrc.Height);
            imgGray = imgSrc.Convert<Gray, byte>();

            for (int y = 0; y < imgSrc.Height; ++y)
            {
                for (int x = 0; x < imgSrc.Width; ++x)
                {
                    Color clr = Color.FromArgb(imgSrc.Data[y, x, 2], imgSrc.Data[y, x, 1], imgSrc.Data[y, x, 0]);
                    imgGray.Data[y, x, 0] = (byte)(clr.GetHue() * hueByteRatio);
                }
            }

            imageBox1.Image = imgGray;
        }

        private void cannyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (imgSrc != null)
            {
                imgGray = imgSrc.Convert<Gray, byte>();
                imageBox1.Image = imgGray.SmoothGaussian(5).Canny(15, 25);
            }
        }

        private void txtBox_Click(object sender, EventArgs e)
        {
            TextBox txt = sender as TextBox;
            txt.SelectAll();
        }

        private void setModelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Mat mat = new Mat();
            capture.Retrieve(mat);
            model = mat;
            imageBox2.Image = new Image<Bgr, byte>(model.Bitmap).Resize(imageBox2.Width, imageBox2.Height, Emgu.CV.CvEnum.Inter.Linear);
        }

        private void eigenVectorsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            VectorView eigenForm = new VectorView(imgSrc.Mat, new VectorOfVectorOfPoint(), new VectorOfVectorOfPoint());
            eigenForm.Show();
        }
    }

    public enum VideoMode
    {
        Color = 0,
        Gray,
        Contour,
        Canny,
        Hue_Gray,
        Sobel,
        Laplacian,
        Binary,
        FeaturesTracking
    }
}
