using System.Text;
using NAudio.Wave;
using Whisper.net;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: AudioTranscriptTool <audioPath> <modelPath> <outputPath> [language]");
    return 1;
}

string audioPath = Path.GetFullPath(args[0]);
string modelPath = Path.GetFullPath(args[1]);
string outputPath = Path.GetFullPath(args[2]);
string language = args.Length >= 4 ? args[3] : "en";

if (!File.Exists(audioPath))
{
    Console.Error.WriteLine($"Audio file not found: {audioPath}");
    return 2;
}

if (!File.Exists(modelPath))
{
    Console.Error.WriteLine($"Whisper model not found: {modelPath}");
    return 3;
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

float[] samples = LoadMono16kSamples(audioPath);

using var factory = WhisperFactory.FromPath(modelPath);
using var processor = factory.CreateBuilder()
    .WithLanguage(language)
    .WithThreads(Math.Max(1, Environment.ProcessorCount / 2))
    .Build();

var segments = new List<(TimeSpan Start, TimeSpan End, string Text)>();

await foreach (var segment in processor.ProcessAsync(samples))
{
    string text = segment.Text?.Trim() ?? string.Empty;
    if (text.Length == 0)
    {
        continue;
    }

    segments.Add((segment.Start, segment.End, text));
    Console.WriteLine($"[{segment.Start:hh\\:mm\\:ss} - {segment.End:hh\\:mm\\:ss}] {text}");
}

var builder = new StringBuilder();
string title = Path.GetFileNameWithoutExtension(outputPath);
builder.AppendLine($"# {title}");
builder.AppendLine();
builder.AppendLine($"Source audio: {Path.GetFileName(audioPath)}");
builder.AppendLine();
builder.AppendLine("## Transcript");
builder.AppendLine();

foreach (var segment in segments)
{
    builder.AppendLine($"[{segment.Start:mm\\:ss} - {segment.End:mm\\:ss}] {segment.Text}");
    builder.AppendLine();
}

if (segments.Count == 0)
{
    builder.AppendLine("No speech segments were detected.");
}

await File.WriteAllTextAsync(outputPath, builder.ToString(), Encoding.UTF8);
Console.WriteLine($"Saved transcript to: {outputPath}");
return 0;

static float[] LoadMono16kSamples(string audioPath)
{
    using var reader = new MediaFoundationReader(audioPath);
    using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1))
    {
        ResamplerQuality = 60,
    };

    using var pcmStream = new MemoryStream();
    WaveFileWriter.WriteWavFileToStream(pcmStream, resampler);

    byte[] wavBytes = pcmStream.ToArray();
    using var wavStream = new MemoryStream(wavBytes);
    using var wavReader = new WaveFileReader(wavStream);

    byte[] pcmBytes = new byte[wavReader.Length];
    _ = wavReader.Read(pcmBytes, 0, pcmBytes.Length);

    int sampleCount = pcmBytes.Length / 2;
    var samples = new float[sampleCount];

    for (int i = 0; i < sampleCount; i++)
    {
        short sample = BitConverter.ToInt16(pcmBytes, i * 2);
        samples[i] = sample / 32768f;
    }

    return samples;
}