class ConversationTurn {
    constructor(role = null, content = null) {
        this.role = role;
        this.content = content;
    }
}

class ConversationData {
    constructor(history, max_turns = 10, thread_id = null) {
        this.thread_id = thread_id;
        this.history = history;
        this.max_turns = max_turns;
        this.attachments = [];
    }
}

module.exports = { ConversationTurn, ConversationData };