class Phi {
    constructor(deploymentEndpoint, deploymentKey) {
        this.deploymentEndpoint = deploymentEndpoint;
        this.deploymentKey = deploymentKey;
    }

    async createCompletion(messages) {
        try {
            const response = await fetch(
                `${this.deploymentEndpoint}`,
                {
                    method: 'POST',
                    headers: {
                        'Authorization': `Bearer ${this.deploymentKey}`,
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