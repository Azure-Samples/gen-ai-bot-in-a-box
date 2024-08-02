# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

class ConversationTurn:
    def __init__(
        self,
        role: str = None,
        content: str = None
    ):
        self.role = role
        self.content = content

    def toJSON(self):
        return {"role": self.role, "content": self.content}

class ConversationData:
    def __init__(
        self,
        history: list[ConversationTurn],
        max_turns: int = 10,
        thread_id: str = None,
    ):
        self.thread_id = thread_id
        self.history = history
        self.max_turns = max_turns

    def add_turn(self, role: str, content: str):
        self.history.append(ConversationTurn(role, content))
        if len(self.history) >= self.max_turns:
            self.history.pop(0)

    def toMessages(self):
        return [turn.toJSON() for turn in self.history]
