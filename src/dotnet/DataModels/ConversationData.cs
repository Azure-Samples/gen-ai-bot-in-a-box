// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace Microsoft.BotBuilderSamples
{
    public class ConversationTurn
    {
        public string Role { get; set; } = null;
        public string Message { get; set; } = null;
    }
    public class Attachment
    {
        public string Name { get; set; }
        public List<AttachmentPage> Pages { get; set; } = new List<AttachmentPage>();
    }
    public class AttachmentPage
    {
        public string Content { get; set; } = null;
        public float[] Vector { get; set; } = null;
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

        public void AddTurn(string role, string message)
        {
            History.Add(new ConversationTurn { Role = role, Message = message });
            if (History.Count >= MaxTurns)
            {
                History.RemoveAt(1);
            }
        }

        public List<ChatMessage> toMessages()
        {
            return History.Select<ConversationTurn, ChatMessage>((turn, index) => 
                turn.Role == "assistant" ? new AssistantChatMessage(turn.Message) : 
                turn.Role == "user" ? new UserChatMessage(turn.Message) :
                new SystemChatMessage(turn.Message)).ToList();
        }

        public List<Dictionary<string, string>> toMessagesDict()
        {
            return History.Select((turn, index) =>
                new Dictionary<string, string>(){
                    {"Role", turn.Role},
                    {"Message", turn.Message}
                }).ToList();
        }


    }
}
