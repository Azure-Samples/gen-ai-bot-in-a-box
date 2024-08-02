class Phi {
    constructor(deploymentEndpoint, deploymentKey) {
        this._deploymentEndpoint = deploymentEndpoint;
        this._deploymentKey = deploymentKey;
    }

    async createCompletion(messages) {
        try {
            const response = await fetch(
                `${this._deploymentEndpoint}`,
                {
                    method: 'POST',
                    headers: {
                        'Authorization': `Bearer ${this._deploymentKey}`,
                        'Content-Type': 'application/json'
                    },
                    body: JSON.stringify({ messages: messages })
                }
            );

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            return await response.json();
        } catch (error) {
            console.error(`Error in createCompletion: ${error.message}`);
        }
    }
}

module.exports.Phi = Phi;