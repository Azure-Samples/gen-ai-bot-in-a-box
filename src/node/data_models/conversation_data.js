class ConversationTurn {
    constructor(role, message, imageType, imageData) {
        this.role = role;
        this.message = message;
        this.imageType = imageType;
        this.imageData = imageData;
    }
}

class ConversationData {
    constructor(history, max_turns = 10, thread_id = null) {
        this.thread_id = thread_id;
        this.history = history;
        this.max_turns = max_turns;
        this.attachments = [];
    }

    static addTurn(conversationData, role, message, imageType, imageData) {
        conversationData.history.push(new ConversationTurn(role, message, imageType, imageData));
        if (conversationData.history.length >= conversationData.max_turns) {
            conversationData.history.splice(1, 1);
        }
    }

    static toMessages(conversationHistory) {
        let messages = conversationHistory.map((turn, index) => ({
            role: turn.role,
            tool_calls: turn.tool_calls,
            content: turn.message
        }))
        return messages

    }
    
}

module.exports = { ConversationTurn, ConversationData };