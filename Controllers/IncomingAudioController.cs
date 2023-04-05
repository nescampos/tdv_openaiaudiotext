using Microsoft.AspNetCore.Mvc;
using Twilio;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using OpenAI.GPT3;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using OpenAI.GPT3.ObjectModels.ResponseModels;

namespace TwilioWhatsAppOpenAI.Controllers;

[ApiController]
[Route("[controller]")]
public class IncomingAudioController : TwilioController
{
    private const string FilesRootPath = @"<path to WhatsAppAudioFiles folder>";
    private OpenAIService OpenAIService;


    public IncomingAudioController(IConfiguration configuration)
    {
        TwilioClient.Init(configuration["TwilioAccountSid"], configuration["TwilioAuthToken"]);
        OpenAIService = new OpenAIService(new OpenAiOptions {ApiKey = configuration["OpenAISecret"] });
    }

    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        var requestData = HttpContext.Request.Form;
        var numMedia = requestData["NumMedia"];
        if(numMedia.Any())
        {
            var messagingResponse = new MessagingResponse();
            int numMediaValue = int.Parse(numMedia[0]);
            string transcriptions = string.Empty;
            for (var i = 0; i < numMediaValue; i++)
            {
                
                var mediaUrl = Request.Form[$"MediaUrl{i}"];
                var contentType = Request.Form[$"MediaContentType{i}"];

                var filePath = GetMediaFileName(mediaUrl, contentType);
                DownloadUrlToFileAsync(mediaUrl, filePath).Wait();

                string textFromAudio = await ProcessAudio(filePath);
                transcriptions += "Transcription for audio: "+textFromAudio;
            }
            messagingResponse.Message(transcriptions);

            return TwiML(messagingResponse);
        }
        return Ok();
    }

    private string GetMediaFileName(string mediaUrl, string contentType)
    {
        return Path.Combine(FilesRootPath, Path.GetFileName(mediaUrl)+GetDefaultExtension(contentType));
    }

    private static async Task DownloadUrlToFileAsync(string mediaUrl, string filePath)
    {
        using (var client = new HttpClient())
        {
            var response = await client.GetAsync(mediaUrl);
            var httpStream = await response.Content.ReadAsStreamAsync();
            using (var fileStream = System.IO.File.Create(filePath))
            {
                await httpStream.CopyToAsync(fileStream);
                await fileStream.FlushAsync();
            }
        }
    }

    private static string GetDefaultExtension(string mimeType)
    {
        string extension = string.Empty;

        switch (mimeType)
        {
            case "audio/aac":
                extension = ".aac";
                break;
            case "audio/mp4":
                extension = ".mp4";
                break;
            case "audio/mpeg":
                extension = ".mp3";
                break;
            case "audio/wav":
                extension = ".wav";
                break;
            case "audio/webm":
                extension = ".webm";
                break;
            default:
                throw new ArgumentException($"Mimetype {mimeType} no es un formato de audio válido.");
        }
        return extension;
    }

    private async Task<string> ProcessAudio(string filePath)
    {
        var audioFile = await System.IO.File.ReadAllBytesAsync(filePath);
        AudioCreateTranscriptionRequest audioRequest = new AudioCreateTranscriptionRequest();
        audioRequest.Model = Models.WhisperV1;
        audioRequest.File = audioFile;
        audioRequest.FileName = Path.GetFileName(filePath);
        AudioCreateTranscriptionResponse audioResponse = await OpenAIService.Audio.CreateTranscription(audioRequest);
        return audioResponse.Text;
    }
}
