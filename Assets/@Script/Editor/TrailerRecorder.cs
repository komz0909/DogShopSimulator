using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace DogShop.EditorTools
{
    /// <summary>
    /// 소개 영상 촬영(2026-11-06). 재생 중인 게임 화면을 1080p 30프레임 동영상(소리 포함)으로 녹화한다.
    /// 프로젝트 폴더 Recordings/ 아래에 장면 이름으로 저장한다.
    /// </summary>
    public static class TrailerRecorder
    {
        static RecorderController controller;
        public static string LastFile { get; private set; } = "";

        /// <param name="cameraOnly">true 면 촬영용 카메라(TrailerCam)만 찍는다 — 화면 위 글과 창이 빠진 깨끗한 장면.</param>
        public static string Begin(string name, bool cameraOnly = false)
        {
            if (controller != null) End();
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = name;
            movie.Enabled = true;
            movie.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            movie.VideoBitRateMode = UnityEditor.VideoBitrateMode.High;
            if (cameraOnly)
                movie.ImageInputSettings = new CameraInputSettings { Source = ImageSource.TaggedCamera, CameraTag = "TrailerCam", OutputWidth = 1920, OutputHeight = 1080, CaptureUI = false };
            else
                movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
            movie.AudioInputSettings.PreserveAudio = true;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Recordings");
            Directory.CreateDirectory(dir);
            movie.OutputFile = Path.Combine(dir, name);
            LastFile = movie.OutputFile + ".mp4";
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = 30f;
            settings.CapFrameRate = true;
            controller = new RecorderController(settings);
            controller.PrepareRecording();
            return controller.StartRecording() ? "녹화 시작: " + LastFile : "녹화 시작 실패";
        }

        public static string End()
        {
            if (controller == null) return "녹화 중 아님";
            controller.StopRecording();
            controller = null;
            return "녹화 끝: " + LastFile;
        }

        public static bool Recording => controller != null && controller.IsRecording();
    }
}
