using System.ComponentModel;
using System.Text.Encodings.Web;

namespace Plugins;

public class ImagePlugin
{
    static HttpClient client = new HttpClient();

    private ChatClient _chatClient;
    private ITurnContext<IMessageActivity> _turnContext;
    private ConversationData _conversationData;

    public ImagePlugin(ChatClient chatClient, ConversationData conversationData, ITurnContext<IMessageActivity> turnContext)
    {
        _chatClient = chatClient;
        _turnContext = turnContext;
        _conversationData = conversationData;
    }

    [KernelFunction, Description("Get information about an image that was uploaded. Valid only for png, jpg/jpeg, gif and webp files.")]
    public async Task<string> ImageQuery(
        [Description("The question about the image.")] string query,
        [Description("The name of the image file, not including the directory.")] string image_name
    )
    {
        // Find image in attachments by name
        var image = _conversationData.Attachments.Find(a => a.Name == image_name.Split('/').Last());

        // Read image.Url into BinaryData
        var byteArray = await client.GetByteArrayAsync(image.Url);
        var binaryData = new BinaryData(byteArray);

        var messages = new List<ChatMessage>() {
                new UserChatMessage(new List<ChatMessageContentPart>(){
                    ChatMessageContentPart.CreateTextPart(query),
                    ChatMessageContentPart.CreateImagePart(binaryData, image.ContentType),
                })
            };
        var response = await _chatClient.CompleteChatAsync(messages);
        return response.Value.Content.First().Text;
    }
}
