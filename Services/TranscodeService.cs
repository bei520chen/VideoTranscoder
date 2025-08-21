using FFMpegCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VideoConverter
{
    internal sealed class TranscodeService
    {
        public void ConfigureFFmpeg()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory;
            GlobalFFOptions.Configure(new FFOptions
            {
                BinaryFolder = bin,
                TemporaryFilesFolder = System.IO.Path.GetTempPath()
            });
        }

        public async Task TranscodeAsync(string inputPath, string outputPath, CancellationToken token, Action<int> onProgress)
        {
            // 竖屏/横屏自适应 1080 的缩放
            var vf = "scale='if(lt(iw,ih),1080,-2)':'if(lt(iw,ih),-2,1080)',format=yuv420p";

            var conversion = FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, true, opt => opt
                    .WithVideoCodec("libx264")
                    .WithAudioCodec("aac")
                    .WithAudioBitrate(128_000)
                    .WithCustomArgument($"-vf \"{vf}\"")
                    .WithFramerate(30));

            conversion.NotifyOnProgress(percent =>
            {
                var p = (int)Math.Clamp(percent, 0, 100);
                onProgress?.Invoke(p);
            }, TimeSpan.FromMilliseconds(300));

            await conversion.CancellableThrough(token).ProcessAsynchronously(true);
        }
    }
}