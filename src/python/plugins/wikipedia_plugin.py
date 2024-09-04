import requests
from botbuilder.core import TurnContext
from botbuilder.schema import Activity
from semantic_kernel.functions import kernel_function
from typing import Dict

class WikipediaPlugin:
    def __init__(self, conversation_data: Dict, turn_context: TurnContext):
        self._turn_context = turn_context
        self.client = requests.Session()

    @kernel_function(
        name="query_articles",
        description="Gets a list of lights and their current state",
    )
    async def query_articles(self, query: str) -> str:
        await self._turn_context.send_activity(f'Searching Wikipedia for "{query}"...')
        response = self.client.get(
            f'https://en.wikipedia.org/w/api.php?action=opensearch&search={requests.utils.quote(query)}&limit=1'
        )
        if response.status_code == 200:
            return response.text
        else:
            return f'FAILED TO FETCH DATA FROM API. STATUS CODE {response.status_code}'

    async def get_article(self, title: str) -> str:
        await self._turn_context.send_activity(f'Getting article "{title}"...')
        response = self.client.get(
            f'https://en.wikipedia.org/w/api.php?action=query&format=json&titles={requests.utils.quote(title)}&prop=extracts&explaintext'
        )
        if response.status_code == 200:
            return response.text
        else:
            return f'FAILED TO FETCH DATA FROM API. STATUS CODE {response.status_code}'