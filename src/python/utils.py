import re
from botbuilder.core import CardFactory
from botbuilder.schema import Activity, ActivityTypes


def get_super(x): 
    normal = "0123456789"
    super_s = "⁰¹²³⁴⁵⁶⁷⁸⁹"
    res = x.maketrans(''.join(normal), ''.join(super_s)) 
    return x.translate(res) 

def replace_citations(x): 
    return re.sub(r"\[doc(\d+)\]", lambda match: f"{get_super(match.group(1))}", str(x))

def get_citations_card(citations):
    return  Activity(
                text="Citations",
                type=ActivityTypes.message,
                attachments=[CardFactory.adaptive_card({
        "type": "AdaptiveCard",
        "body": [
            {
                "type": "Container",
                "items": [
                    {
                        "type": "ColumnSet",
                        "columns": [
                            {
                                "type": "Column",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": f"[{str(i+1)}: {citation['title']}]({citation['url']})",
                                        "wrap": True,
                                        "size": "Medium"
                                    }
                                ],
                                "width": "stretch"
                            },
                            {
                                "type": "Column",
                                "id": "chevronDown"+str(i+1),
                                "spacing": "Small",
                                "verticalContentAlignment": "Center",
                                "items": [
                                    {
                                        "type": "Image",
                                        "url": "https://adaptivecards.io/content/down.png",
                                        "width": "20px",
                                        "altText": "collapsed"
                                    }
                                ],
                                "width": "auto",
                                "isVisible": False
                            },
                            {
                                "type": "Column",
                                "id": "chevronUp"+str(i+1),
                                "spacing": "Small",
                                "verticalContentAlignment": "Center",
                                "items": [
                                    {
                                        "type": "Image",
                                        "url": "https://adaptivecards.io/content/up.png",
                                        "width": "20px",
                                        "altText": "expanded"
                                    }
                                ],
                                "width": "auto"
                            }
                        ],
                        "selectAction": {
                            "type": "Action.ToggleVisibility",
                            "targetElements": [
                                "cardContent"+str(i+1),
                                "chevronUp"+str(i+1),
                                "chevronDown"+str(i+1)
                            ]
                        }
                    },
                    {
                        "type": "Container",
                        "id": "cardContent"+str(i+1),
                        "items": [
                            {
                                "type": "Container",
                                "items": [
                                    {
                                        "type": "TextBlock",
                                        "text": citation['content'],
                                        "isSubtle": True,
                                        "wrap": True
                                    }
                                ]
                            }
                        ],
                        "isVisible": False
                    }
                ],
                "separator": True,
                "spacing": "Medium"
            } for i, citation in enumerate(citations)
        ],
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "version": "1.3",
        "fallbackText": "This card requires Adaptive Cards v1.2 support to be rendered properly."
    })])
    