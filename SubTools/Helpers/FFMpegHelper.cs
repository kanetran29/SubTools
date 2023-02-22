using SubTools.HelperClasses;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SubTools
{
    /// <summary>
    /// Handles all things FFMpeg:  calling the exe, building the arguments, etc.
    /// </summary>
    static class FFMpegHelper
    {
        public static string ffmpegPath;
        public static string ffprobePath;
        public static string ffplayPath;


        /// <summary>
        /// Runs a target executable.
        /// </summary>
        /// <param name="exePath">
        /// Path to an executable
        /// </param>
        /// <param name="args">
        /// List of arguments to pass to the executable
        /// </param>
        /// <returns>
        /// Standard output of the executed program.
        /// </returns>
        private static string Execute(string exePath, string args)
        {
            Debug.WriteLine("running Execute(" + exePath + " " + args + ")");

            var result = string.Empty;

            using (Process p = new Process())
            {
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.FileName = exePath;
                p.StartInfo.Arguments = args;
                p.Start();
                p.WaitForExit();

                result = p.StandardOutput.ReadToEnd();
            }

            return result;
        }

        /// <summary>
        /// Uses ffprobe to retrieve the duration (in seconds) of a video file
        /// </summary>
        /// <param name="videoPath">
        /// Path to a video file
        /// </param>
        /// <returns>
        /// The duration of the target file in sexagesimal format (h:mm:ss.microseconds).
        /// </returns>
        public static string GetFileDuration(string videoPath)
        {
            var VIDEO_LENGTH_ARGUMENTS = "-i \"" + videoPath + "\" -sexagesimal -show_entries format=duration -v quiet -of csv=\"p=0\"";
            var videoLength = Execute(ffprobePath, VIDEO_LENGTH_ARGUMENTS);
            var simplifiedVideoLength = TimeHelper.SimplifyTimestamp(videoLength);
            return simplifiedVideoLength.Trim();
        }

        /// <summary>
        /// Calls ffmpeg to process a target video file
        /// </summary>
        /// <param name="videoPath">
        /// Path to input video file
        /// </param>
        /// <param name="outputPath">
        /// Path to output video file (the file that will be created)
        /// </param>
        /// <param name="startTime">
        /// Timestamp (in seconds) from which to start cutting
        /// </param>
        /// <param name="endTime">
        /// Timestamp (in seconds) at which to end the cut video
        /// </param>
        public static string ProcessVideo(string videoPath, string folderPath, string outputName, string fileExtension, IEnumerable<string[]> times, double[] diff)
        {
            int N = times.Count();
            var duration = $"\'00\\:{GetFileDuration(videoPath).Replace(":", "\\:")}\'";

            string beginParam = $"[0:v]trim=\'00\\:00\\:00.000\':{times.First()[0]},setpts=PTS-STARTPTS[v1];\n";

            var middleParam = Enumerable.Range(0, times.Count() - 1).Select(i =>
            {
                var curTime = times.ElementAt(i);
                var nextTime = times.ElementAt(i + 1);
                string cmd = $"[0:v]trim={curTime[0]}:{nextTime[0]},setpts={diff[i].ToString("0.000")}*(PTS-STARTPTS)[v{i + 2}];\n";
                return cmd;
            })
            .Append($"[0:v]trim={times.Last()[0]}:{times.Last()[1]},setpts={diff.Last().ToString("0.000")}*(PTS-STARTPTS)[v{N + 1}];\n")
            .Append($"[0:v]trim={times.Last()[1]}:{duration},setpts=1*(PTS-STARTPTS)[v{N + 2}];\n");

            var lastParam = String.Join(String.Empty, Enumerable.Range(1, N + 2).Select((index) => $"[v{index}]").ToArray());

            string param = beginParam + String.Join(String.Empty, middleParam) + lastParam + $"concat=n={N + 2}:v=1";

            string videoFilesPath = $"{folderPath}input.txt";

            if (!File.Exists(videoFilesPath))
            {
                FileStream file = File.Create(videoFilesPath);
                file.Dispose();
            }

            File.WriteAllText(videoFilesPath, param);

            string outputPath = $"{folderPath}{outputName}.{fileExtension}";

            var VIDEO_ARGUMENTS = $"-report -i {videoPath} -filter_complex_script {videoFilesPath} -preset superfast -an -profile:v baseline {outputPath}";

            Debug.WriteLine(VIDEO_ARGUMENTS);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var s = Execute(ffmpegPath, VIDEO_ARGUMENTS);
            return outputPath;
        }

        public static string ProcessAudio(string folderPath, int N, string outputName)
        {
            var midParams = String.Join("\n", Enumerable.Range(0, N).Select((index) => $"file \'{folderPath}{index}.mp3\'").ToArray());

            string outputPath = $"{folderPath}{outputName}";
            string audioFilesPath = $"{folderPath}audio_files.txt";

            if (!File.Exists(audioFilesPath))
            {
                FileStream file = File.Create(audioFilesPath);
                file.Dispose();
            }

            File.WriteAllText(audioFilesPath, midParams);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var AUDIO_CONCAT_ARGUMENTS = $"-f concat -safe 0 -i {audioFilesPath} -c copy {outputPath}";
            Execute(ffmpegPath, AUDIO_CONCAT_ARGUMENTS);
            return outputPath;
        }

        public static string AddAudioToVideo(string audioPath, string videoPath, string startTime, string outPutPath)
        {
            var VIDEO_CUT_ARGUMENTS = $" -y -i {videoPath} -itsoffset {startTime.Replace("\'", String.Empty).Replace("\\", String.Empty)} -i {audioPath} -map 0:0 -map 1:0 -c:v copy -preset ultrafast -async 1 {outPutPath}";
            Debug.WriteLine(VIDEO_CUT_ARGUMENTS);
            Execute(ffmpegPath, VIDEO_CUT_ARGUMENTS);
            return outPutPath;
        }

        public static void setFFMpegLocations(string pathToFFMpeg)
        {
            ffmpegPath = pathToFFMpeg;
            ffprobePath = Modify_Path(pathToFFMpeg, "ffprobe.exe"); ;
            ffplayPath = Modify_Path(pathToFFMpeg, "ffplay.exe"); ;
        }

        private static string Modify_Path(string pathToFFMpeg, string otherExecutable)
        {
            string[] splitPath = pathToFFMpeg.Split(Path.DirectorySeparatorChar);
            splitPath[splitPath.Length - 1] = otherExecutable;
            return string.Join(Path.DirectorySeparatorChar.ToString(), splitPath);
        }
    }
}
