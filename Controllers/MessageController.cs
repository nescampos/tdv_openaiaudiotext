using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.AspNetCore.Mvc;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;
using OpenAI.GPT3.ObjectModels.ResponseModels;

namespace TwilioWhatsAppOpenAI.Controllers;

[Route("[controller]")]
public class MessageController : TwilioController
{
    private static readonly HashSet<string> SupportedContentTypes = new()
    {
        "mp3", "mp4", "mpeg", "mpga", "m4a", "wav", "webm"
    };
    
    private readonly HttpClient httpClient;
    private readonly IOpenAIService openAIService;


    public MessageController(HttpClient httpClient, IOpenAIService openAIService)
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

        var mediaUrl = form["MediaUrl0"].ToString();
        var contentType = form["MediaContentType0"].ToString();
        if (!contentType.StartsWith("audio/"))
        {
            response.Message("You can only sent audio files.");
            return TwiML(response);
        }

        var (audioStream,  format) = await GetAudioStream(mediaUrl, contentType, ct);
        await using (audioStream)
        {
            var transcription = await TranscribeAudio(audioStream, format);
            response.Message( $"Transcription for audio: {transcription}");
            return TwiML(response);
        }
    }

    private async Task<(Stream audioStream, string format)> GetAudioStream(string mediaUrl, string contentType, CancellationToken ct)
    {
        var fileResponse = await httpClient.GetAsync(mediaUrl, ct);
        var audioFileStream = await fileResponse.Content.ReadAsStreamAsync(ct);

        var format = contentType.Substring(6);
        if (SupportedContentTypes.Contains(format))
        {
            return (audioFileStream, format);
        }

        await using (audioFileStream)
        {
            var wavAudioStream = new MemoryStream();
            await ConvertMediaUsingFfmpeg(
                input: audioFileStream, inputFormat: format,
                output: wavAudioStream, outputFormat: "wav"
            );
            wavAudioStream.Seek(0, SeekOrigin.Begin);
            return (wavAudioStream, "wav");
        }
    }

    private async Task ConvertMediaUsingFfmpeg(Stream input, string inputFormat, Stream output, string outputFormat)
    {
        await FFMpegArguments
            .FromPipeInput(new StreamPipeSource(input), options => options
                .ForceFormat(inputFormat))
            .OutputToPipe(new StreamPipeSink(output), options => options
                .ForceFormat(outputFormat))
            .ProcessAsynchronously();
    }

    private async Task<string> TranscribeAudio(Stream audioStream, string format)
    {
        AudioCreateTranscriptionRequest audioRequest = new()
        {
            Model = Models.WhisperV1,
            FileStream = audioStream,
            FileName = $"sample.{format}"
        };

        AudioCreateTranscriptionResponse audioResponse = await openAIService.Audio.CreateTranscription(audioRequest);
        if(audioResponse.Successful) return audioResponse.Text;

        throw new Exception(string.Format(
            "Error occurred transcribing audio using OpenAI Whisper API. Code {0}: {1}",
            audioResponse.Error?.Code,
            audioResponse.Error?.Message
        ));
    }
}