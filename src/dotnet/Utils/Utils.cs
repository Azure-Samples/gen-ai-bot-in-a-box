using AdaptiveCards;
using Azure.AI.OpenAI.Chat;

namespace Utils
{
    public class Utils
    {
        

        public static Microsoft.Bot.Schema.Attachment GetCitationsCard(IReadOnlyList<ChatCitation> citations)
        {
            var citationBlocks = citations.Select(getCitationBlock).ToList();
            return new Microsoft.Bot.Schema.Attachment()
            {
                ContentType = AdaptiveCard.ContentType,
                Content = new AdaptiveCard("1.2")
                {
                    Version = "1.2",
                    Body = citationBlocks
                }
            };
        }

        private static AdaptiveElement getCitationBlock(ChatCitation citation, int index)
        {
            return new AdaptiveContainer()
            {
                Items = [
                    new AdaptiveColumnSet() {
                        Columns = [
                            new AdaptiveColumn() {
                                Items = [
                                    new AdaptiveTextBlock() {
                                        Text = $"[{citation.FilePath} ({citation.ChunkId})]({citation.Uri})",
                                        Wrap = true,
                                        Size = AdaptiveTextSize.Medium
                                    }
                                ],
                                Width = "stretch"
                            },
                            new AdaptiveColumn() {
                                Id = $"chevronDown{index}",
                                Spacing = AdaptiveSpacing.Small,
                                VerticalContentAlignment = AdaptiveVerticalContentAlignment.Center,
                                Items = [
                                    new AdaptiveImage() {
                                        Url = new Uri("https://adaptivecards.io/content/down.png"),
                                        PixelWidth = 20,
                                        AltText = "collapsed"
                                    }
                                ],
                                Width = "auto",
                                IsVisible = false
                            },
                            new AdaptiveColumn() {
                                Id = $"chevronUp{index}",
                                Spacing = AdaptiveSpacing.Small,
                                VerticalContentAlignment = AdaptiveVerticalContentAlignment.Center,
                                Items = [
                                    new AdaptiveImage() {
                                        Url = new Uri("https://adaptivecards.io/content/up.png"),
                                        PixelWidth = 20,
                                        AltText = "expanded"
                                    }
                                ],
                                Width = "auto"
                            }
                        ],
                        SelectAction = new AdaptiveToggleVisibilityAction() {
                            TargetElements = [
                                $"cardContent{index}",
                                $"chevronUp{index}",
                                $"chevronDown{index}"
                            ]
                        }
                    },
                    new AdaptiveContainer() {
                        Id = $"cardContent{index}",
                        Items = [
                            new AdaptiveContainer() {
                                Items = [
                                    new AdaptiveTextBlock() {
                                        Text = GetContent(citation),
                                        IsSubtle = true,
                                        Wrap = true
                                    }
                                ]
                            }
                        ],
                        IsVisible = false
                    }
                ],
                Separator = true,
                Spacing = AdaptiveSpacing.Medium
            };
        }
    
        private static string GetContent(ChatCitation citation) {
            if (citation.Content.StartsWith($"Title: {citation.FilePath}"))
                return citation.Content.Replace($"Title: {citation.FilePath}", "");
            else
                return citation.Content;
        }
    }
}