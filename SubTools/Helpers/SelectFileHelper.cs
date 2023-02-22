using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SubTools.HelperClasses
{
    class SelectFileHelper
    {
        public static void Select_Video(Action<string> updateUI)
        {
            var chooseVideoFileDialog = new OpenFileDialog();
            chooseVideoFileDialog.Title = "Choose a video file";
            chooseVideoFileDialog.Filter = "Video files |*.mp4; *.avi; *.mov; *.mkv; *.wmv; *.flv";

            if (chooseVideoFileDialog.ShowDialog() == true)
            {
                var pathToVideo = chooseVideoFileDialog.FileName;
                updateUI(pathToVideo);
            }
        }

        public static void Select_Srt(Action<string> updateUI)
        {
            var chooseSrtFileDialog = new OpenFileDialog();
            chooseSrtFileDialog.Title = "Choose a srt file";
            chooseSrtFileDialog.Filter = "Srt files (*.srt)|*.srt";

            if (chooseSrtFileDialog.ShowDialog() == true)
            {
                var pathToSrt = chooseSrtFileDialog.FileName;
                updateUI(pathToSrt);
            }
        }
        public static void Select_Keys(Action<string> updateUI)
        {
            var chooseFileDialog = new OpenFileDialog();
            chooseFileDialog.Title = "Choose a api keys file";
            chooseFileDialog.Filter = "Text files |*.txt";

            if (chooseFileDialog.ShowDialog() == true)
            {
                var pathToFile = chooseFileDialog.FileName;
                updateUI(pathToFile);
            }
        }

        public static void Select_Output_Folder(TextBox targetTextBox)
        {
            var chooseOutputFolderDialog = new OpenFileDialog();

            var USER_INSTRUCTIONS = "Select a folder";

            // hacky - disables validation and file existence check, so user can click OK despite not selecting a real file
            chooseOutputFolderDialog.ValidateNames = false;
            chooseOutputFolderDialog.CheckFileExists = false;
            chooseOutputFolderDialog.CheckPathExists = true;

            // pre-fills in text, forcing OpenFileDialog to select a nonexistent file
            chooseOutputFolderDialog.FileName = USER_INSTRUCTIONS;

            if (chooseOutputFolderDialog.ShowDialog() == true)
            {
                var outputPath = chooseOutputFolderDialog.FileName;

                // remove nonexistent file name from path, resulting in a path to a directory
                var splitOutputPath = outputPath.Split(Path.DirectorySeparatorChar);
                var fakeFileName = splitOutputPath[splitOutputPath.Length - 1];
                var correctedOutput = outputPath.Substring(0, outputPath.Length - fakeFileName.Length);

                targetTextBox.Text = correctedOutput;
            }
        }
    }
}
