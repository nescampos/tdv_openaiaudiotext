using FFMpegCore;
using Microsoft.AspNetCore.Mvc;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using OpenAI.GPT3.ObjectModels.ResponseModels;

namespace TwilioWhatsAppOpenAI.Controllers;

[Route("[controller]")]
public class IncomingAudioController : TwilioController
{
    private const string FilesRootPath = "WhatsAppAudioFiles";
    private readonly HttpClient httpClient;
    private readonly IOpenAIService openAIService;


    public IncomingAudioController(HttpClient httpClient, IOpenAIService openAIService)
    {
        this.httpClient = httpClient;
        this.openAIService = openAIService;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var response = new MessagingResponse();
        var form = await Request.ReadFormAsync(ct);
        var numMedia = int.Parse(form["NumMedia"].ToString());

        if (numMedia == 0)
        {
            response.Message("Please sent an audio file.");
            return TwiML(response);
        }
        if (numMedia > 1)
        {
            response.Message("You can only sent one audio file at a time.");
            return TwiML(response);
        }

        var mediaUrl = form["MediaUrl0"];
        var contentType = form["MediaContentType0"];
        var extension = GetExtensionFromContentType(contentType);
        var filePath = Path.Combine(FilesRootPath, Path.GetFileName(mediaUrl) + extension);
        
        await DownloadUrlToFileAsync(mediaUrl, filePath, ct);

        var oldFilePath = filePath;
        filePath = Path.ChangeExtension(filePath, ".wav");
        ConvertMediaUsingFffmpeg(oldFilePath, filePath);
        System.IO.File.Delete(oldFilePath);
        
        string textFromAudio = await ProcessAudio(filePath);
        response.Message( $"Transcription for audio: {textFromAudio}");
        System.IO.File.Delete(filePath);
        
        return TwiML(response);
    }

    private async Task DownloadUrlToFileAsync(string mediaUrl, string filePath, CancellationToken ct)
    {
        var response = await httpClient.GetAsync(mediaUrl, ct);
        var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = System.IO.File.Create(filePath);
        await httpStream.CopyToAsync(fileStream, ct);
        await fileStream.FlushAsync(ct);
    }

    private static string GetExtensionFromContentType(string mimeType)
    {
        var extension = mimeType switch
        {
            "audio/aac" => ".aac",
            "audio/mp4" => ".mp4",
            "audio/mpeg" => ".mp3",
            "audio/wav" => ".wav",
            "audio/webm" => ".webm",
            "audio/ogg" => ".ogg",
            _ => throw new ArgumentException($"Mimetype {mimeType} is not supported.")
        };
        return extension;
    }

    private async Task<string> ProcessAudio(string filePath)
    {
        var audioFile = await System.IO.File.ReadAllBytesAsync(filePath);
        AudioCreateTranscriptionRequest audioRequest = new()
        {
            Model = Models.WhisperV1,
            File = audioFile,
            FileName = Path.GetFileName(filePath)
        };

        AudioCreateTranscriptionResponse audioResponse = await openAIService.Audio.CreateTranscription(audioRequest);
        return audioResponse.Text;
    }

    private void ConvertMediaUsingFffmpeg(string filePath, string newFilePath)
    {
        FFMpegArguments
            .FromFileInput(filePath)
            .OutputToFile(newFilePath)
            .ProcessSynchronously();
    }
}