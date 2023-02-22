using Microsoft.Win32;
using Newtonsoft.Json;
using SubTools.HelperClasses;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Threading;
using System.Timers;
using System.Windows;

namespace SubTools
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IEnumerable<string[]> times;
        private IEnumerable<string> speeches;
        private IEnumerable<double> offsetsSrt;
        private double[] offsetsSub;
        private double[] diffs;
        private string fileExtension;
        private string logsPath;
        private string[] API_Keys;
        private string[] links;

        static ReaderWriterLock locker = new ReaderWriterLock();

        public MainWindow()
        {
            InitializeComponent();
            Load_Prefs();
            Processing_Video_Button.Click += Processing_Video;
        }

    private void Window_Closing(object sender, CancelEventArgs e)
        {
            Save_Prefs();
        }

        private void Select_Video(object sender, RoutedEventArgs e)
        {
            SelectFileHelper.Select_Video(UpdateUIAfterVideoSelected);
        }

        private void Select_Srt(object sender, RoutedEventArgs e)
        {
            SelectFileHelper.Select_Srt(UpdateUIAfterSrtSelected);
        }

        private void Select_Keys(object sender, RoutedEventArgs e)
        {
            SelectFileHelper.Select_Keys(UpdateUIAfterKeysSelected);
        }

        private void UpdateUIAfterVideoSelected(string path)
        {
            Input_Video.Text = path;
            fileExtension = Get_File_Extension(path);
        }

        private void UpdateUIAfterKeysSelected(string path)
        {
            API_Keys_Location.Text = path;
        }

        private void UpdateUIAfterSrtSelected(string path)
        {
            Input_Srt.Text = path;
        }

        private void Proccessing_Srt()
        {
            WriteLog($"[SRT] ----------- PROCESSING ----------\n");
            // Converting "hh:mm:ss,msms --> hh:mm:ss,msms" to "hh:mm:ss.msms~hh:mm:ss.msms" for easier split and run ffmpeg command later 
            string[] allLines = File.ReadAllLines(Input_Srt.Text);
            var lines = allLines
                        .Where((line, index) => index % 4 == 1)
                        .Select(line => line.Replace(",", ".")
                                            .Replace(" --> ", "~"));

            // Get all speeches
            speeches = allLines.Where((line, index) => index % 4 == 2);

            var rawTime = lines.Select(line => line.Split('~'));
            // Get Time to cut
            times = rawTime.Select(values => new[]
                        {
                            $"\'{values.First().Replace(":", "\\:")}\'",
                            $"\'{values.Last().Replace(":", "\\:")}\'",
                        });
            // Get offset to compare
            offsetsSrt = Enumerable.Range(0, rawTime.Count() - 1).Select(i =>
            {
                var curr = rawTime.ElementAt(i);
                var next = rawTime.ElementAt(i + 1);
                return DateTime.Parse(next.First()).Subtract(DateTime.Parse(curr.First())).TotalMilliseconds;
            })
                .Append(DateTime.Parse(rawTime.Last().Last()).Subtract(DateTime.Parse(rawTime.Last().First())).TotalMilliseconds);
            offsetsSub = new double[offsetsSrt.Count()];
        }

        private void Select_Output_Folder(object sender, RoutedEventArgs e)
        {
            SelectFileHelper.Select_Output_Folder(Output_Dir);
        }

        private string Get_File_Extension(string path)
        {
            string[] splitPath = path.Split(Path.DirectorySeparatorChar);
            string fileName = splitPath[splitPath.Length - 1];
            string[] splitFileName = fileName.Split('.');
            string fileExtension = splitFileName[splitFileName.Length - 1];

            return fileExtension;
        }

        private void Processing_Video(object sender, RoutedEventArgs e)
        {
            logsPath = Output_Dir.Text + "logs.txt";
            if (!File.Exists(logsPath))
            {
                var file = File.Create(logsPath);
                file.Dispose();
            }
            File.WriteAllText(logsPath, String.Empty);

            API_Keys = File.ReadAllLines(API_Keys_Location.Text);
            WriteLog($"[KEYS] ----------------------- \n{String.Join("\n", API_Keys)}\n");

            Proccessing_Srt();

            var videoPath = Input_Video.Text;
            var srtPath = Input_Srt.Text;
            var ffmpegPath = FFMpeg_Location.Text;

            if (string.IsNullOrEmpty(ffmpegPath))
            {
                MessageBox.Show(
                    "Please select a ffmpeg file by either clicking the \"Select\" button",
                    "Please select a ffmpeg file first",
                    MessageBoxButton.OK
                    );
                return;
            }

            if (string.IsNullOrEmpty(videoPath))
            {
                MessageBox.Show(
                    "Please select a video by either clicking the \"Video\" button, or dragging and dropping a video file. ",
                    "Please select a video first",
                    MessageBoxButton.OK
                    );
                return;
            }

            if (!File.Exists(videoPath))
            {
                MessageBox.Show(
                    "The input video was not found.  Ensure that it has not been moved or deleted, or select another video.",
                    "Input video not found",
                    MessageBoxButton.OK
                    );
                return;
            }

            if (string.IsNullOrEmpty(srtPath))
            {
                MessageBox.Show(
                    "Please select a srt file by either clicking the \"Srt\" button, or dragging and dropping a srt file. ",
                    "Please select a srt file first",
                    MessageBoxButton.OK
                    );
                return;
            }

            if (!File.Exists(srtPath))
            {
                MessageBox.Show(
                    "The input srt file was not found.  Ensure that it has not been moved or deleted, or select another srt file.",
                    "Input srt file not found",
                    MessageBoxButton.OK
                    );
                return;
            }

            if (!Directory.Exists(Output_Dir.Text))
            {
                MessageBox.Show(
                    "The specified output directory does not exist.  Please create it or choose another output location.",
                    "Output directory not found",
                    MessageBoxButton.OK
                    );

                Output_Dir.Select(0, Output_Dir.Text.Length - 1);
                return;
            }
            ProcessVideo();
        }

        private void Save_Prefs()
        {
            Properties.Settings.Default.output_location = Output_Dir.Text;
            Properties.Settings.Default.voice = Voice_TextBox.Text;
            Properties.Settings.Default.api_key = API_Keys_Location.Text;
            Properties.Settings.Default.speed = Speed_TextBox.Text;
            Properties.Settings.Default.ffmpeg_location = FFMpeg_Location.Text;
            Properties.Settings.Default.Save();
        }

        private void ProcessVideo()
        {
            var input = Input_Video.Text;
            int LENGTH = speeches.Count();
            string voice = Voice_TextBox.Text;
            string speed = Speed_TextBox.Text;
            int KEYS = API_Keys.Length;
            int PROCESSORS = Environment.ProcessorCount > 12 ? 12 : Environment.ProcessorCount;
            links = new string[LENGTH];
            List<Thread> threads = new List<Thread>();

            bool useFPT = Provider_Combobox.SelectedIndex == 0;
            if (useFPT) {
                for (int p = 0; p < PROCESSORS; p++)
                {
                    for (int i = p; i < LENGTH; i += PROCESSORS)
                    {
                        var speech = speeches.ElementAt(i);
                        string key = API_Keys[i % KEYS];
                        int iClosure = i;
                        Thread thread = new Thread(() => links[iClosure] = GetAudioSubFileLinkFPT(speech, key, voice, speed));
                        thread.Start();
                        threads.Add(thread);
                        Thread.Sleep(100);
                    }
                }

                foreach (Thread thread in threads.ToList())
                {
                    thread.Join();
                }

                Thread.Sleep(30000);

                threads.Clear();

                for (int p = 0; p < PROCESSORS; p++)
                {
                    for (int i = p; i < LENGTH; i += PROCESSORS)
                    {
                        string output = Output_Dir.Text + i + ".mp3";
                        string link = links[i];
                        Thread thread = new Thread(() => DownloadFile(link, output));
                        thread.Start();
                        threads.Add(thread);
                    }
                }

                foreach (Thread thread in threads.ToList())
                {
                    thread.Join();
                }
            } 
            else
            {
                for (int p = 0; p < PROCESSORS; p++)
                {
                    for (int i = p; i < LENGTH; i += PROCESSORS)
                    {
                        var speech = speeches.ElementAt(i);
                        string key = API_Keys[i % KEYS];
                        string output = Output_Dir.Text + i + ".mp3";
                        int iClosure = i;
                        Thread thread = new Thread(() =>
                        {
                            string base64string = GetAudioSubFileStringTubeKit(speech, key, voice, speed);
                            byte[] binaryData = Convert.FromBase64String(base64string);
                            File.WriteAllBytes(output, binaryData);
                        });
                        thread.Start();
                        threads.Add(thread);
                        Thread.Sleep(1000);
                    }
                }

                foreach (Thread thread in threads.ToList())
                {
                    thread.Join();
                }

                Thread.Sleep(5000);
            }

            for (int i = 0; i < LENGTH; i++)
            {
                string output = Output_Dir.Text + i + ".mp3";
                string time = FFMpegHelper.GetFileDuration(output);
                offsetsSub[i] = TimeSpan.Parse($"00:00:{time}").TotalMilliseconds;
            }

            diffs = offsetsSrt.Select((val, index) => offsetsSub[index] / (double)val).ToArray();

            string audioPath = Output_Dir.Text + "audio.mp3";
            string videoPath = Output_Dir.Text + "video_noAudio." + fileExtension;

            if (File.Exists(audioPath))
            {
                File.Delete(audioPath);
            }
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            FFMpegHelper.ProcessAudio(Output_Dir.Text, LENGTH, "audio.mp3");
            WriteLog($"[AUDIO] -------------- PROCESSED -------------- \n");
            FFMpegHelper.ProcessVideo(input, Output_Dir.Text, "video_noAudio", fileExtension, times, diffs);
            WriteLog($"[VIDEO] -------------- PROCESSED -------------- \n");

            for (int i = 0; i < LENGTH; i++)
            {
                string output = Output_Dir.Text + i + ".mp3";
                File.Delete(output);
            }
            WriteLog($"[CHUNKS] -------------- DELETED -------------- \n");

            string finalOutput = videoPath.Replace("noAudio", "converted");


            if (File.Exists(finalOutput))
            {
                File.Delete(finalOutput);
            }
            FFMpegHelper.AddAudioToVideo(audioPath, videoPath, times.First()[0], finalOutput);
            WriteLog($"[CONVERTED] -------------- DONE -------------- \n {finalOutput}\n");

        }

        private void Load_Prefs()
        {
            Output_Dir.Text = String.IsNullOrEmpty(Properties.Settings.Default.output_location)
                              ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                              : Properties.Settings.Default.output_location;

            API_Keys_Location.Text = String.IsNullOrEmpty(Properties.Settings.Default.api_key)
                              ? String.Empty
                              : Properties.Settings.Default.api_key;

            Speed_TextBox.Text = String.IsNullOrEmpty(Properties.Settings.Default.speed)
                              ? "0"
                              : Properties.Settings.Default.speed;

            Voice_TextBox.Text = String.IsNullOrEmpty(Properties.Settings.Default.voice)
                              ? "banmaiace"
                              : Properties.Settings.Default.voice;

            FFMpeg_Location.Text = String.IsNullOrEmpty(Properties.Settings.Default.ffmpeg_location)
                                   ? String.Empty
                                   : Properties.Settings.Default.ffmpeg_location;

            if (!String.IsNullOrEmpty(FFMpeg_Location.Text))
            {
                string pathToFFMpeg = FFMpeg_Location.Text;
                FFMpegHelper.setFFMpegLocations(pathToFFMpeg);
            }
        }

        private string GetAudioSubFileLinkFPT(string content, string key, string voice, string speed)
        {
            String result = System.Threading.Tasks.Task.Run(async () =>
            {
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("api-key", key);
                client.DefaultRequestHeaders.Add("speed", speed);
                client.DefaultRequestHeaders.Add("voice", voice);
                var response = await client.PostAsync("https://api.fpt.ai/hmi/tts/v5", new StringContent(content));
                return await response.Content.ReadAsStringAsync();
            }).GetAwaiter().GetResult();

            FPT_Response res = JsonConvert.DeserializeObject<FPT_Response>(result);
            WriteLog($"[LINK]\t{key}\t{content}\n{res}\n");
            return res.async;
        }
        private string GetAudioSubFileStringTubeKit(string content, string key, string voice, string speed)
        {
            TubeKit_Request tubeKit_Request = new TubeKit_Request(text: content, voiceServer: "ttsgo", voiceID: voice, voiceSpeed: speed);
            String result = System.Threading.Tasks.Task.Run(async () =>
            {
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("apikey", key);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await client.PostAsync("https://www.tubekit.win/api/v1/tts/synthesize", new StringContent(JsonConvert.SerializeObject(tubeKit_Request)));
                return await response.Content.ReadAsStringAsync();
            }).GetAwaiter().GetResult();
            WriteLog($"[LINK]\t{key}\t{content}\n{JsonConvert.SerializeObject(result)}\n");
            TubeKit_Response res = JsonConvert.DeserializeObject<TubeKit_Response>(result);
            return res.audioData;
        }

        private void DownloadFile(string url, string outputPath)
        {
            using (var client = new WebClient())
            {
                try
                {
                    client.DownloadFile(url, outputPath);
                }
                catch(Exception e)
                {
                    WriteLog($"{e}\t{url}\t{outputPath}");
                }
            }
        }

        private void Select_FFMpeg(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("\"Select FFMpeg executable\" button clicked.");

            var findFFMpegDialog = new OpenFileDialog();
            findFFMpegDialog.Title = "Select ffmpeg.exe";
            findFFMpegDialog.DefaultExt = ".exe";
            findFFMpegDialog.Filter = "Executables (.exe) | *.exe";

            if (findFFMpegDialog.ShowDialog() == true)
            {
                var pathToFFMpeg = findFFMpegDialog.FileName;
                FFMpeg_Location.Text = pathToFFMpeg;
                FFMpegHelper.setFFMpegLocations(pathToFFMpeg);
            }
        }

        private void WriteLog(string log)
        {
            try
            {
                locker.AcquireWriterLock(int.MaxValue); //You might wanna change timeout value 
                File.AppendAllText(logsPath, log + "\n");
            }
            finally
            {
                locker.ReleaseWriterLock();
            }
        }
    }

    internal class FPT_Response
    {
        public string async { get; set; }
        public int error { get; set; }
        public string message { get; set; }
        public string request_id { get; set; }
        public override string ToString()
        {
            return "{\n" + $"\t{async}\n\t{error}\n\t{message}\n\t{request_id}\n" + "}\n";
        }
    }

    internal class TubeKit_Response
    {
        public string status { get; set; }
        public string mess { get; set; }
        public string audioData { get; set; }
        public override string ToString()
        {
            return "{\n" + $"\t{status}\n\t{mess}\n" + "}\n";
        }
    }

    internal class TubeKit_Request
    {
        public string text { get; set; }
        public string voiceServer { get; set; }
        public string voiceID { get; set; }
        public string voiceSpeed { get; set; }

        public TubeKit_Request(string text, string voiceServer, string voiceID, string voiceSpeed)
        {
            this.text = text;
            this.voiceServer = voiceServer;
            this.voiceID = voiceID;
            this.voiceSpeed = voiceSpeed;
        }
    }
}
