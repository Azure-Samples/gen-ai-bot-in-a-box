import requests

class Phi:

    def __init__(
            self,
            deployment_endpoint: str,
            deployment_key: str
    ):
        self._deployment_endpoint = deployment_endpoint
        self._deployment_key = deployment_key

    def create_completion(
            self,
            messages: list[dict]
    ):
        response = requests.post(
            f"{self._deployment_endpoint}",
            headers={
                "Authorization": f"Bearer {self._deployment_key}"
            },
            json={
                "messages": messages
            }
        )

        response.raise_for_status()

        return response.json()