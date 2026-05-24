namespace InventorySystem.Services.Services
{
    public interface ISpeechService
    {
        Task<string> SpeechToTextAsync(Stream audioStream);
        Task<byte[]?> TextToSpeechAsync(string text);
    }
}
