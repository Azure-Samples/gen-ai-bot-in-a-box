// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.BotBuilderSamples
{
    public class ToolCall {
        public string Id { get; set; }
    }
    public class ConversationTurn
    {
        public string Role { get; set; } = null;
        public string Message { get; set; } = null;
        public string ImageData { get; set; } = null;
        public string ImageType { get; set; } = null;
        public string ToolCallId { get; set; } = null;
        public List<ChatToolCall> ToolCalls { get; set; } = null;
    }
    public class Attachment
    {
        public string Name { get; set; }
        public string ContentType { get; set; }
        public string Url { get; set; }
    }
    // Defines a state property used to track conversation data.
    public class ConversationData
    {
        // The ID of the user's thread.
        public string ThreadId { get; set; }
        public int MaxTurns { get; set; } = 10;

        // Track conversation history
        public List<ConversationTurn> History = new List<ConversationTurn>();

        // Track attached documents
        public List<Attachment> Attachments = new List<Attachment>();

        public void AddTurn(string role, string message = "", string imageType = null, string imageData = null, string toolCallId = null, List<ChatToolCall> toolCalls = null)
        {
            if (imageType == null)
            {
                History.Add(new ConversationTurn { Role = role, Message = message, ToolCallId = toolCallId, ToolCalls = toolCalls });
            }
            else
            {
                History.Add(new ConversationTurn { Role = role, ImageType = imageType, ImageData = imageData });
            }
            if (History.Count >= MaxTurns)
            {
                History.RemoveAt(1);
            }
        }

        public List<ChatMessage> toMessages()
        {
            var messages = History.Select<ConversationTurn, ChatMessage>((turn, index) =>
                turn.Role == "assistant" ? (turn.ToolCalls != null ? new AssistantChatMessage(toolCalls: turn.ToolCalls) : new AssistantChatMessage(turn.Message)) :
                turn.Role == "user" ? new UserChatMessage(new ChatMessageContentPart[]{
                    turn.ImageType == null ? 
                        ChatMessageContentPart.CreateTextMessageContentPart(turn.Message) :
                        ChatMessageContentPart.CreateImageMessageContentPart(BinaryData.FromBytes(Convert.FromBase64String(turn.ImageData)), turn.ImageType)
                }) :
                turn.Role == "system" ? new SystemChatMessage(turn.Message) :
                new ToolChatMessage(turn.ToolCallId, turn.Message)).ToList();
            return messages;
        }

    }
}
